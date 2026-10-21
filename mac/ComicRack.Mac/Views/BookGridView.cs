using System;
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using ComicRack.Mac.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.VisualTree;
using ComicRack.Mac.Browser;
using ComicRack.Mac.Thumbnails;
using ComicRack.Mac.ViewModels;

namespace ComicRack.Mac.Views
{
	/// <summary>
	/// The browser grid. A <see cref="ScrollViewer"/> around an <see cref="ItemsRepeater"/> with a <see cref="UniformGridLayout"/>:
	/// the repeater realizes the cells in (and a little around) the viewport and recycles them while scrolling, so the number of
	/// <see cref="BookCell"/>s depends on the window size, not on the library (14,706 books or 100,000 cost the same). The view model is
	/// the DataContext. The selection is the view model's <see cref="SelectionModel"/> (the rules of the original's <c>ItemView</c>); this
	/// view turns pointer and keys into its operations: a click selects, Cmd toggles, Shift extends; arrows, Home, End, Page Up/Down move the focus (and
	/// select, with Shift extend, with Cmd only move); Space selects the focused book (Option+Space toggles it); Cmd+A selects all; Delete or Cmd+Backspace asks
	/// to remove the selected books; Enter or a double click raises <see cref="BrowserViewModel.OpenBookRequested"/> for the focused book.
	/// </summary>
	public sealed class BookGridView : UserControl
	{
		/// <summary>Space around the grid and between the cells: <c>ItemView.ItemPadding</c> (1, 1) on each side of an item, so 1 and 2 (the cell's own border is 4).</summary>
		public const double GridMargin = 1, Spacing = 2;

		private readonly ScrollViewer scroll;
		private readonly ItemsRepeater repeater;
		private readonly UniformGridLayout layout;
		private readonly ThumbnailOptions fallbackOptions = new ThumbnailOptions();
		private BrowserViewModel model;
		private BookItem[] items = Array.Empty<BookItem>();
		private int realized;
		private int focusedIndex = -1;
		private int pendingClick = -1;
		private bool pendingToggle, pendingExtend;
		private double cellWidth, cellHeight;

		public BookGridView()
		{
			Focusable = true;
			layout = new UniformGridLayout
			{
				Orientation = Orientation.Horizontal,
				ItemsStretch = UniformGridLayoutItemsStretch.None,
				MinRowSpacing = Spacing,
				MinColumnSpacing = Spacing,
			};
			repeater = new ItemsRepeater
			{
				Layout = layout,
				ItemTemplate = new FuncDataTemplate<BookItem>((item, scope) => new BookCell(), supportsRecycling: true),
				VerticalCacheLength = 1,
				HorizontalCacheLength = 0,
				Margin = new Thickness(GridMargin),
				HorizontalAlignment = HorizontalAlignment.Left, // the leftover width is shared by both sides: see CenterCells
			};
			PointerTouchPadGestureMagnify += OnMagnify;
			AddHandler(PointerWheelChangedEvent, OnPreviewWheel, RoutingStrategies.Tunnel);
			repeater.ElementPrepared += OnElementPrepared;
			repeater.ElementClearing += OnElementClearing;
			scroll = new ScrollViewer
			{
				Classes = { "cr-item-view" },
				HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
				VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
				Content = repeater,
			};
			scroll.SizeChanged += (sender, e) => OnViewportChanged();
			Content = scroll;
			ApplyMetrics(BrowserViewModel.DefaultThumbnailSize);
		}

		public ScrollViewer Scroller => scroll;

		public ItemsRepeater Repeater => repeater;

		/// <summary>Cells that are bound to a book right now (what the repeater realized, without the ones in its recycle pool).</summary>
		public int RealizedCount => realized;

		/// <summary>Cells in the repeater, bound or pooled.</summary>
		public int CellControlCount => repeater.Children.Count;

		public double CellWidth => cellWidth;

		public double CellHeight => cellHeight;

		public ThumbnailService Thumbnails => model?.Thumbnails;

		/// <summary>The settings that change how thumbnails look and behave (dog-ear, fade-in, numeric ratings, same size covers).</summary>
		public ThumbnailOptions Options => model?.ThumbnailOptions ?? fallbackOptions;

		/// <summary>The item size of the browser: the cover height the cells are drawn for.</summary>
		public double CoverHeight => model?.ThumbnailSize ?? BrowserViewModel.DefaultThumbnailSize;

		/// <summary>The height to request thumbnails at: the cover height in device pixels.</summary>
		public int RequestPixels
		{
			get
			{
				double scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
				return (int)Math.Ceiling((model?.ThumbnailSize ?? BrowserViewModel.DefaultThumbnailSize) * scale);
			}
		}

		/// <summary>Cells per row, as the layout computes them for the current width.</summary>
		public int Columns
		{
			get
			{
				double width = scroll.Viewport.Width > 0 ? scroll.Viewport.Width : scroll.Bounds.Width;
				double usable = width - 2 * GridMargin;
				return Math.Max(1, (int)Math.Floor((usable + Spacing) / (cellWidth + Spacing)));
			}
		}

		/// <summary>The realized cell for a position in the list, or null.</summary>
		public BookCell CellAt(int index) => repeater.TryGetElement(index) as BookCell;

		/// <summary>The realized cells that show a book.</summary>
		public IEnumerable<BookCell> BoundCells()
		{
			foreach (Control child in repeater.Children)
			{
				if (child is BookCell cell && cell.Item != null)
				{
					yield return cell;
				}
			}
		}

		private TopLevel scaleSource;

		protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
		{
			base.OnAttachedToVisualTree(e);
			scaleSource = TopLevel.GetTopLevel(this);
			if (scaleSource != null)
			{
				scaleSource.ScalingChanged += OnScalingChanged;
			}
		}

		protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
		{
			if (scaleSource != null)
			{
				scaleSource.ScalingChanged -= OnScalingChanged;
				scaleSource = null;
			}
			base.OnDetachedFromVisualTree(e);
		}

		// The window moved to a display with another scale (a Retina display and a 1x one): the covers are asked for in device pixels, so the
		// cached step may change; a cell asks again only if it does (and keeps its picture until the new one arrives).
		private void OnScalingChanged(object sender, EventArgs e)
		{
			foreach (BookCell cell in BoundCells())
			{
				cell.RefreshThumbnail(force: false);
			}
		}

		protected override void OnDataContextChanged(EventArgs e)
		{
			base.OnDataContextChanged(e);
			if (model != null)
			{
				model.PropertyChanged -= OnModelChanged;
				model.ThumbnailOptions.Changed -= OnOptionsChanged;
				model.BooksChanged -= OnBooksChanged;
				model.SelectionChanged -= OnSelectionChanged;
			}
			model = DataContext as BrowserViewModel;
			if (model != null)
			{
				model.PropertyChanged += OnModelChanged;
				model.ThumbnailOptions.Changed += OnOptionsChanged;
				model.BooksChanged += OnBooksChanged;
				model.SelectionChanged += OnSelectionChanged;
				ApplyMetrics(model.ThumbnailSize);
			}
			ApplyItems();
		}

		// A thumbnail setting changed: the cells are drawn again; same size covers change the cell's content, not its size.
		private void OnOptionsChanged(object sender, EventArgs e)
		{
			foreach (BookCell cell in BoundCells())
			{
				cell.RefreshLook();
			}
		}

		// Books of the library changed (a page was read): the cells that show them draw their state again.
		private void OnBooksChanged(object sender, IReadOnlyCollection<cYo.Projects.ComicRack.Engine.ComicBook> books)
		{
			var changed = books as ISet<cYo.Projects.ComicRack.Engine.ComicBook> ?? new HashSet<cYo.Projects.ComicRack.Engine.ComicBook>(books);
			foreach (BookCell cell in BoundCells())
			{
				if (cell.Item.Book != null && changed.Contains(cell.Item.Book))
				{
					cell.RefreshLook();
				}
			}
		}

		protected override void OnGotFocus(FocusChangedEventArgs e)
		{
			base.OnGotFocus(e);
			SetViewFocused(true);
		}

		protected override void OnLostFocus(FocusChangedEventArgs e)
		{
			base.OnLostFocus(e);
			SetViewFocused(IsKeyboardFocusWithin);
		}

		// The selection is drawn in the focused colour while the grid has the keyboard focus (StyledRenderer.GetSelectionColor(controlFocused)).
		private void SetViewFocused(bool focused)
		{
			foreach (BookCell cell in BoundCells())
			{
				cell.ViewFocused = focused;
			}
		}

		// BV.04: Cmd+wheel (the original's Ctrl+wheel) changes the item size by 16 per notch. It is taken in the tunnel phase: the scroll viewer
		// below would scroll with the wheel before the event reached this view.
		private void OnPreviewWheel(object sender, PointerWheelEventArgs e)
		{
			if (model != null && e.KeyModifiers.HasFlag(KeyModifiers.Meta))
			{
				model.ThumbnailSize = Math.Clamp(model.ThumbnailSize + e.Delta.Y * 16, BrowserViewModel.MinThumbnailSize, BrowserViewModel.MaxThumbnailSize);
				e.Handled = true;
			}
		}

		// BV.04: a pinch on the trackpad scales the item size.
		private void OnMagnify(object sender, PointerDeltaEventArgs e)
		{
			if (model != null)
			{
				model.ThumbnailSize = Math.Clamp(model.ThumbnailSize * (1 + e.Delta.X), BrowserViewModel.MinThumbnailSize, BrowserViewModel.MaxThumbnailSize);
				e.Handled = true;
			}
		}

		private void OnModelChanged(object sender, PropertyChangedEventArgs e)
		{
			switch (e.PropertyName)
			{
				case nameof(BrowserViewModel.Items):
					ApplyItems();
					break;
				case nameof(BrowserViewModel.ThumbnailSize):
					ApplyMetrics(model.ThumbnailSize);
					foreach (BookCell cell in BoundCells())
					{
						cell.RefreshLook();
						cell.RefreshThumbnail(force: false);
					}
					break;
				case nameof(BrowserViewModel.Thumbnails):
					foreach (BookCell cell in BoundCells())
					{
						cell.RefreshThumbnail(force: true);
					}
					break;
			}
		}

		// The selection or the focus changed: the cells draw their state, and the focused book is scrolled into view when the focus moved.
		private void OnSelectionChanged(object sender, EventArgs e) => ApplySelection(ensureVisible: !model.KeepsView);

		private void ApplyItems()
		{
			items = model?.Items ?? Array.Empty<BookItem>();
			focusedIndex = -1;
			Vector before = scroll.Offset;
			repeater.ItemsSource = items;
			if (model != null && model.ConsumeScrollReset())
			{
				scroll.Offset = new Vector(0, 0);
			}
			else if (model != null && model.KeepsView && before.Y > 0)
			{
				KeepOffset(before);
			}
			ApplySelection(ensureVisible: !(model?.KeepsView ?? false));
		}

		// A refresh replaces the list under the user's hands: the scroll position stays where it was (as far as the new list is long enough).
		private void KeepOffset(Vector offset)
		{
			scroll.Offset = offset;
			if (scroll.Offset.Y + 0.5 < offset.Y)
			{
				// the extent is not known until the repeater has laid out the new list: put the offset back once it has
				void Restore(object sender, EventArgs e)
				{
					repeater.LayoutUpdated -= Restore;
					scroll.Offset = offset;
				}
				repeater.LayoutUpdated += Restore;
			}
		}

		private void ApplyMetrics(double coverHeight)
		{
			// An item is the cover (2:3) with its border and the caption under it (ThumbnailMetrics, from CoverViewItem.MeasureItem).
			cellWidth = ThumbnailMetrics.CellWidth(coverHeight);
			cellHeight = ThumbnailMetrics.CellHeight(coverHeight);
			layout.MinItemWidth = cellWidth;
			layout.MinItemHeight = cellHeight;
			CenterCells();
		}

		private void OnViewportChanged()
		{
			// The number of columns changes with the width; the repeater lays out again by itself.
			CenterCells();
		}

		// The cells are centred in the viewport on a whole unit, so that their frames and shadows stay sharp (a half unit of leftover would put
		// every edge between two pixels at 1x).
		private void CenterCells()
		{
			double viewport = scroll.Viewport.Width > 0 ? scroll.Viewport.Width : scroll.Bounds.Width;
			if (viewport <= 0)
			{
				return;
			}
			int columns = Columns;
			double content = columns * cellWidth + (columns - 1) * Spacing;
			double left = GridMargin + Math.Max(0, Math.Floor((viewport - 2 * GridMargin - content) / 2));
			double width = content + Spacing + 1; // the layout divides by (cell + spacing): 6.9999 columns would be 6
			if (repeater.Margin.Left != left || repeater.Width != width)
			{
				repeater.Width = width; // the layout then fills exactly this many columns, whatever margin is left of them
				repeater.Margin = new Thickness(left, GridMargin, GridMargin, GridMargin);
			}
		}

		private void OnElementPrepared(object sender, ItemsRepeaterElementPreparedEventArgs e)
		{
			if (e.Element is BookCell cell && e.Index >= 0 && e.Index < items.Length)
			{
				realized++;
				cell.Bind(this, items[e.Index], e.Index, model?.Selection.IsSelected(e.Index) ?? false, e.Index == focusedIndex);
			}
		}

		private void OnElementClearing(object sender, ItemsRepeaterElementClearingEventArgs e)
		{
			if (e.Element is BookCell cell && cell.Item != null)
			{
				realized--;
				cell.Unbind();
			}
		}

		// Draws the selection of the model on the realized cells; the focused book is scrolled into view if the focus moved.
		private void ApplySelection(bool ensureVisible = true)
		{
			SelectionModel selection = model?.Selection;
			int focus = selection?.FocusedIndex ?? -1;
			bool moved = focus != focusedIndex;
			focusedIndex = focus;
			foreach (BookCell cell in BoundCells())
			{
				cell.SetState(selection != null && selection.IsSelected(cell.Index), cell.Index == focus);
			}
			if (focus >= 0 && moved && ensureVisible)
			{
				EnsureVisible(focus);
			}
		}

		/// <summary>Scrolls just far enough to show the cell at <paramref name="index"/>.</summary>
		public void EnsureVisible(int index)
		{
			if (index < 0 || index >= items.Length)
			{
				return;
			}
			int row = index / Columns;
			double top = GridMargin + row * (cellHeight + Spacing);
			double bottom = top + cellHeight + GridMargin;
			double viewHeight = scroll.Viewport.Height;
			double offset = scroll.Offset.Y;
			if (viewHeight <= 0)
			{
				return;
			}
			if (top - GridMargin < offset)
			{
				offset = top - GridMargin;
			}
			else if (bottom > offset + viewHeight)
			{
				offset = bottom - viewHeight;
			}
			if (Math.Abs(offset - scroll.Offset.Y) > 0.5)
			{
				scroll.Offset = new Vector(scroll.Offset.X, Math.Max(0, offset));
			}
		}

		/// <summary>The scroll offset that puts row <paramref name="row"/> at the top of the viewport (for scripted scrolling).</summary>
		public double OffsetOfRow(int row) => row * (cellHeight + Spacing);

		/// <summary>Number of rows the list has at the current width.</summary>
		public int RowCount => items.Length == 0 ? 0 : (items.Length + Columns - 1) / Columns;

		// ---- pointer ---------------------------------------------------------------------------------------------------------------

		protected override void OnPointerPressed(PointerPressedEventArgs e)
		{
			base.OnPointerPressed(e);
			pendingClick = -1;
			if (model == null)
			{
				return;
			}
			PointerPointProperties properties = e.GetCurrentPoint(this).Properties;
			bool left = properties.IsLeftButtonPressed;
			if (!left && !properties.IsRightButtonPressed)
			{
				return;
			}
			var source = e.Source as Visual;
			if (source?.FindAncestorOfType<Avalonia.Controls.Primitives.ScrollBar>(includeSelf: true) != null)
			{
				return; // the scroll bar is not an empty spot of the list
			}
			Focus();
			bool toggle = e.KeyModifiers.HasFlag(KeyModifiers.Meta); // Ctrl of the original
			bool extend = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
			SelectionModel selection = model.Selection;
			BookCell cell = source?.FindAncestorOfType<BookCell>(includeSelf: true);
			if (cell?.Item == null)
			{
				if (left)
				{
					selection.ClickEmpty(toggle, extend);
				}
				return;
			}
			int index = cell.Index;
			if (left && e.ClickCount >= 2)
			{
				if (selection.FocusedIndex != index)
				{
					selection.Click(index, toggle: false, extend: false);
				}
				model.OpenSelected();
				e.Handled = true;
				return;
			}
			if (!left)
			{
				// a context click: an unselected book becomes the selection, a selected one keeps it (the menu works on the selection)
				if (selection.IsSelected(index))
				{
					selection.Focus(index);
				}
				else
				{
					selection.Click(index, toggle: false, extend: false);
				}
				e.Handled = true;
				return;
			}
			if (selection.IsSelected(index))
			{
				// ItemView.OnMouseDownView: a press on a selected book waits for the release, because it could start dragging the selection
				pendingClick = index;
				pendingToggle = toggle;
				pendingExtend = extend;
			}
			else
			{
				selection.Click(index, toggle, extend);
			}
			e.Handled = true;
		}

		protected override void OnPointerReleased(PointerReleasedEventArgs e)
		{
			base.OnPointerReleased(e);
			int index = pendingClick;
			pendingClick = -1;
			if (model == null || index < 0)
			{
				return;
			}
			BookCell cell = (e.Source as Visual)?.FindAncestorOfType<BookCell>(includeSelf: true);
			if (cell?.Item != null && cell.Index == index)
			{
				model.Selection.Click(index, pendingToggle, pendingExtend);
			}
		}

		// ---- keyboard --------------------------------------------------------------------------------------------------------------

		protected override void OnKeyDown(KeyEventArgs e)
		{
			base.OnKeyDown(e);
			if (model == null || items.Length == 0 || e.Handled)
			{
				return;
			}
			SelectionModel selection = model.Selection;
			bool toggle = e.KeyModifiers.HasFlag(KeyModifiers.Meta); // Ctrl of the original (DD20)
			bool extend = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
			int count = items.Length;
			int columns = Columns;
			int current = selection.FocusedIndex;
			int from = current < 0 ? 0 : current; // the first movement key selects the first book
			int target;
			switch (e.Key)
			{
				case Key.A when toggle && !extend && !e.KeyModifiers.HasFlag(KeyModifiers.Alt):
					model.SelectAll();
					e.Handled = true;
					return;
				case Key.Delete:
				case Key.Back when toggle: // Cmd+Backspace: the Mac alias of Delete (DD20)
					model.RemoveSelected();
					e.Handled = true;
					return;
				case Key.Enter:
					if (current >= 0)
					{
						model.OpenSelected();
						e.Handled = true;
					}
					return;
				case Key.Space:
					// Ctrl+Space of the original is Option+Space here: Cmd+Space, Option+Cmd+Space and Ctrl+Cmd+Space belong to the system
					selection.Space(e.KeyModifiers.HasFlag(KeyModifiers.Alt) || toggle);
					e.Handled = true;
					return;
				case Key.Right:
					target = current < 0 ? 0 : current + 1;
					break;
				case Key.Left:
					target = current < 0 ? 0 : current - 1;
					break;
				case Key.Down:
					target = current < 0 ? 0 : RelativeRows(from, 1, columns);
					break;
				case Key.Up:
					target = current < 0 ? 0 : RelativeRows(from, -1, columns);
					break;
				case Key.Home:
					target = 0;
					break;
				case Key.End:
					target = count - 1;
					break;
				case Key.PageDown:
					target = RelativeRows(from, VisibleRows(), columns);
					break;
				case Key.PageUp:
					target = RelativeRows(from, -VisibleRows(), columns);
					break;
				default:
					return;
			}
			e.Handled = true;
			selection.Move(Math.Clamp(target, 0, count - 1), toggle, extend);
		}

		// ItemView.GetRelativeItem with a row delta: the same column of the row (the last book of a short row), and the focus stays in the first or last row
		// when the delta goes past it.
		private int RelativeRows(int index, int deltaRows, int columns)
		{
			int count = items.Length;
			int lastRow = (count - 1) / columns;
			int row = Math.Clamp(index / columns + deltaRows, 0, lastRow);
			int rowLength = row == lastRow ? count - lastRow * columns : columns;
			return row * columns + Math.Min(index % columns, rowLength - 1);
		}

		private int VisibleRows()
		{
			return Math.Max(1, (int)(scroll.Viewport.Height / (cellHeight + Spacing)));
		}
	}
}
