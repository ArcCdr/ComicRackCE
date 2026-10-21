using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using ComicRack.Mac.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ComicRack.Mac.Browser;
using ComicRack.Mac.Services;
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
		private readonly CrSelectionRectangle band = new CrSelectionRectangle();
		private bool marqueeActive;
		private Point marqueeStart;      // where the band started, in the coordinates of the repeater (they scroll with the content)
		private Rect marqueeRect;        // the band now, in the same coordinates
		private Point lastView;          // the pointer in the coordinates of the scroll viewer
		private KeyModifiers lastModifiers;
		private (double Offset, int Realized, double Viewport) evaluated;
		private DispatcherTimer autoScrollTimer;
		private bool releaseEnsuresVisible;
		private int dragIndex = -1;
		private PointerPressedEventArgs dragPress;
		private Point dragOrigin;

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
			scroll.ScrollChanged += OnScrollChanged;
			repeater.LayoutUpdated += OnRepeaterLayoutUpdated;
			Content = new Grid { Children = { scroll, band } };
			ApplyMetrics(BrowserViewModel.DefaultThumbnailSize);
		}

		public ScrollViewer Scroller => scroll;

		/// <summary>The rubber band, drawn over the grid (its <see cref="CrSelectionRectangle.Rectangle"/> is empty while there is none).</summary>
		public CrSelectionRectangle Band => band;

		/// <summary>True while a rubber band is being dragged.</summary>
		public bool IsMarqueeActive => marqueeActive;

		/// <summary>The rubber band in the coordinates of the grid content (scrolls with it); empty when there is none.</summary>
		public Rect MarqueeRectangle => marqueeRect;

		/// <summary>The scroll timer of a rubber band runs in the application (<c>ScrollControl.scrollTimer</c>, 20 ms); tests leave it off and call <see cref="AdvanceAutoScroll"/>.</summary>
		public static bool AutoScrollEnabled { get; set; }

		/// <summary>How far the pointer moves with a pressed book before the books are dragged out (<c>SystemInformation.DragSize</c>).</summary>
		public const double DragThreshold = 4;

		/// <summary>Starts the drag session of the selected books; a test replaces it.</summary>
		public IBookDragStarter DragStarter { get; set; } = new AvaloniaBookDragStarter();

		/// <summary>What a target may do with dragged books: copy them. Move is added with the editable lists (P5.16c), as <c>itemView_ItemDrag</c> adds it for a list that is not the library.</summary>
		public DragDropEffects DragEffects { get; set; } = DragDropEffects.Copy;

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
		private void OnSelectionChanged(object sender, EventArgs e) => ApplySelection(ensureVisible: !model.KeepsView && !marqueeActive);

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

		// The item under a pointer event: the cell, if the point is on what the item draws (CrThumbnailItem.HitRegion), else null: the border around a
		// cover and the free part of the caption area are the empty area of the view, as in the original (CoverViewItem.Contains).
		private static BookCell ItemUnder(PointerEventArgs e, Visual source)
		{
			BookCell cell = source?.FindAncestorOfType<BookCell>(includeSelf: true);
			return cell?.Item != null && cell.HitTest(e.GetPosition(cell)) ? cell : null;
		}

		protected override void OnPointerPressed(PointerPressedEventArgs e)
		{
			base.OnPointerPressed(e);
			pendingClick = -1;
			dragIndex = -1;
			dragPress = null;
			releaseEnsuresVisible = false;
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
			releaseEnsuresVisible = left;
			bool toggle = e.KeyModifiers.HasFlag(KeyModifiers.Meta); // Ctrl of the original
			bool extend = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
			SelectionModel selection = model.Selection;
			BookCell cell = ItemUnder(e, source);
			if (cell == null)
			{
				// ItemView.UpdateSelectionFromMouse(null): any button clears (unless Cmd or Shift is held); the left button also starts the rubber band
				selection.ClickEmpty(toggle, extend);
				if (left)
				{
					BeginMarquee(e);
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
			dragIndex = index;
			dragPress = e;
			dragOrigin = e.GetPosition(this);
			e.Handled = true;
		}

		protected override void OnPointerMoved(PointerEventArgs e)
		{
			base.OnPointerMoved(e);
			if (model == null)
			{
				return;
			}
			if (marqueeActive)
			{
				lastView = e.GetPosition(scroll);
				lastModifiers = e.KeyModifiers;
				EvaluateMarquee(force: false);
				return;
			}
			if (dragIndex >= 0)
			{
				if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
				{
					dragIndex = -1;
					dragPress = null;
					return;
				}
				Point now = e.GetPosition(this);
				if (Math.Abs(now.X - dragOrigin.X) > DragThreshold || Math.Abs(now.Y - dragOrigin.Y) > DragThreshold)
				{
					StartDragOut();
				}
			}
		}

		protected override void OnPointerReleased(PointerReleasedEventArgs e)
		{
			base.OnPointerReleased(e);
			int index = pendingClick;
			pendingClick = -1;
			dragIndex = -1;
			dragPress = null;
			if (model == null)
			{
				return;
			}
			if (marqueeActive)
			{
				EndMarquee();
			}
			else if (index >= 0)
			{
				// ItemView.OnMouseUpView: the click that was waiting is applied wherever the pointer is
				model.Selection.Click(index, pendingToggle, pendingExtend);
			}
			if (releaseEnsuresVisible)
			{
				releaseEnsuresVisible = false;
				EnsureVisible(model.Selection.FocusedIndex);
			}
		}

		protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
		{
			base.OnPointerCaptureLost(e);
			if (marqueeActive)
			{
				EndMarquee();
			}
		}

		// ---- rubber band -----------------------------------------------------------------------------------------------------------

		// The coordinates of the grid content, which scroll with it: the repeater's own, i.e. the viewport point plus the offset, less the margin around the repeater.
		private Point ToGrid(Point viewPoint) => new Point(Math.Floor(viewPoint.X + scroll.Offset.X - repeater.Margin.Left), Math.Floor(viewPoint.Y + scroll.Offset.Y - repeater.Margin.Top));

		private Vector GridOrigin => new Vector(scroll.Offset.X - repeater.Margin.Left, scroll.Offset.Y - repeater.Margin.Top);

		private static bool Touches(Rect a, Rect b) => a.X < b.Right && b.X < a.Right && a.Y < b.Bottom && b.Y < a.Bottom; // Rectangle.IntersectsWith

		private static bool Holds(Rect a, Point p) => p.X >= a.X && p.X < a.Right && p.Y >= a.Y && p.Y < a.Bottom; // Rectangle.Contains

		// ItemView.UpdateSelectionFromMouse(null) and OnMouseDownView: the band starts where the button went down.
		private void BeginMarquee(PointerPressedEventArgs e)
		{
			lastView = e.GetPosition(scroll);
			lastModifiers = e.KeyModifiers;
			marqueeStart = ToGrid(lastView);
			marqueeRect = default;
			marqueeActive = true;
			evaluated = default;
			model.Selection.BeginMarquee();
			e.Pointer.Capture(this);
			e.Handled = true;
			if (AutoScrollEnabled)
			{
				autoScrollTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(20), DispatcherPriority.Input, (sender, args) => AdvanceAutoScroll());
				autoScrollTimer.Start();
			}
		}

		// ItemView.UpdateSelection: the band from the start to the pointer; the books on screen that it touches are selected (see SelectionModel.UpdateMarquee).
		private void EvaluateMarquee(bool force)
		{
			if (!marqueeActive)
			{
				return;
			}
			Point p = ToGrid(lastView);
			double left = Math.Min(marqueeStart.X, p.X), top = Math.Min(marqueeStart.Y, p.Y);
			var rect = new Rect(left, top, Math.Max(marqueeStart.X, p.X) - left, Math.Max(marqueeStart.Y, p.Y) - top);
			ShowBand(rect);
			evaluated = (scroll.Offset.Y, realized, scroll.Viewport.Height);
			if (!force && rect == marqueeRect)
			{
				return;
			}
			marqueeRect = rect;
			Vector origin = GridOrigin;
			var viewport = new Rect(origin.X, origin.Y, scroll.Viewport.Width, scroll.Viewport.Height);
			var visible = new List<int>();
			var hit = new List<int>();
			int pointed = -1;
			foreach (BookCell cell in BoundCells())
			{
				Rect region = cell.HitRegion;
				region = new Rect(cell.Bounds.X + region.X, cell.Bounds.Y + region.Y, region.Width, region.Height);
				if (!Touches(region, viewport))
				{
					continue;
				}
				visible.Add(cell.Index);
				if (Touches(region, rect))
				{
					hit.Add(cell.Index);
					if (pointed < 0 && Holds(region, p))
					{
						pointed = cell.Index;
					}
				}
			}
			visible.Sort();
			bool keepBase = lastModifiers.HasFlag(KeyModifiers.Meta) || lastModifiers.HasFlag(KeyModifiers.Shift);
			model.Selection.UpdateMarquee(visible, hit, pointed, keepBase, flip: lastModifiers.HasFlag(KeyModifiers.Meta));
		}

		private void ShowBand(Rect gridRect)
		{
			Vector origin = GridOrigin;
			band.Rectangle = gridRect.Width <= 0 && gridRect.Height <= 0 ? default : new Rect(gridRect.X - origin.X, gridRect.Y - origin.Y, gridRect.Width, gridRect.Height);
		}

		private void EndMarquee()
		{
			marqueeActive = false;
			marqueeRect = default;
			autoScrollTimer?.Stop();
			autoScrollTimer = null;
			band.Rectangle = default;
			model?.Selection.EndMarquee();
		}

		// The view scrolled (the wheel, the scroll bar, the auto scroll): the band stays where it started in the content, so it grows or shrinks; ItemView.OnScroll.
		private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
		{
			if (marqueeActive)
			{
				EvaluateMarquee(force: true);
			}
		}

		// The repeater realized other cells after a scroll: the band is evaluated against what is on screen now.
		private void OnRepeaterLayoutUpdated(object sender, EventArgs e)
		{
			if (marqueeActive && evaluated != (scroll.Offset.Y, realized, scroll.Viewport.Height))
			{
				EvaluateMarquee(force: true);
			}
		}

		/// <summary>
		/// One tick of the auto scroll of <c>ScrollControl</c>: while the pointer is above or below the view, the content moves by the distance to its edge
		/// (as it is up to 10, beyond that 10 plus a tenth of the rest). Returns true if the view scrolled.
		/// </summary>
		public bool AdvanceAutoScroll()
		{
			if (!marqueeActive)
			{
				return false;
			}
			double height = scroll.Viewport.Height;
			double beyond = lastView.Y < 0 ? lastView.Y : lastView.Y > height ? lastView.Y - height : 0;
			if (beyond == 0)
			{
				return false;
			}
			int delta = (int)Math.Abs(beyond);
			if (delta > 10)
			{
				delta = 10 + (delta - 10) / 10;
			}
			double max = Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height);
			double target = Math.Clamp(scroll.Offset.Y + Math.Sign(beyond) * delta, 0, max);
			if (Math.Abs(target - scroll.Offset.Y) < 0.001)
			{
				return false;
			}
			scroll.Offset = new Vector(scroll.Offset.X, target);
			return true;
		}

		// ---- drag out --------------------------------------------------------------------------------------------------------------

		// ItemView.OnItemDrag then ComicBrowserControl.itemView_ItemDrag: the selected books are dragged out; the click that was waiting is dropped.
		private void StartDragOut()
		{
			PointerPressedEventArgs trigger = dragPress;
			dragIndex = -1;
			dragPress = null;
			pendingClick = -1;
			IReadOnlyList<cYo.Projects.ComicRack.Engine.ComicBook> books = model.SelectedBooks;
			if (trigger == null || books.Count == 0)
			{
				return;
			}
			_ = RunDragAsync(new BookDragRequest(this, trigger, books, DragEffects));
		}

		private async Task RunDragAsync(BookDragRequest request)
		{
			try
			{
				await DragStarter.StartAsync(request);
			}
			catch (Exception ex)
			{
				AppLog.Error("Dragging books out of the grid failed", ex);
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
			int from = current < 0 ? 0 : current; // ItemView.OnKeyDown: without a focused book the keys move from the first one (Right goes to the second)
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
					EnsureVisible(from);
					e.Handled = true;
					return;
				case Key.Right:
					target = from + 1;
					break;
				case Key.Left:
					target = from - 1;
					break;
				case Key.Down:
					target = RelativeRows(from, 1, columns);
					break;
				case Key.Up:
					target = RelativeRows(from, -1, columns);
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
