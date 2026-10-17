using System;
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
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
	/// the DataContext. Keyboard: arrows, Home, End, Page Up/Down move the selection; Enter or a double click raises
	/// <see cref="BrowserViewModel.OpenBookRequested"/>.
	/// </summary>
	public sealed class BookGridView : UserControl
	{
		/// <summary>Space around the grid, and between the cells.</summary>
		public const double GridMargin = 12, Spacing = 10;

		private readonly ScrollViewer scroll;
		private readonly ItemsRepeater repeater;
		private readonly UniformGridLayout layout;
		private BrowserViewModel model;
		private BookItem[] items = Array.Empty<BookItem>();
		private int realized;
		private int selected = -1;
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
				HorizontalAlignment = HorizontalAlignment.Center, // the leftover width is shared by both sides
			};
			repeater.ElementPrepared += OnElementPrepared;
			repeater.ElementClearing += OnElementClearing;
			scroll = new ScrollViewer
			{
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

		protected override void OnDataContextChanged(EventArgs e)
		{
			base.OnDataContextChanged(e);
			if (model != null)
			{
				model.PropertyChanged -= OnModelChanged;
			}
			model = DataContext as BrowserViewModel;
			if (model != null)
			{
				model.PropertyChanged += OnModelChanged;
				ApplyMetrics(model.ThumbnailSize);
			}
			ApplyItems();
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
						cell.RefreshThumbnail(force: false);
					}
					break;
				case nameof(BrowserViewModel.Thumbnails):
					foreach (BookCell cell in BoundCells())
					{
						cell.RefreshThumbnail(force: true);
					}
					break;
				case nameof(BrowserViewModel.SelectedIndex):
					ApplySelection(model.SelectedIndex);
					break;
			}
		}

		private void ApplyItems()
		{
			items = model?.Items ?? Array.Empty<BookItem>();
			selected = -1;
			repeater.ItemsSource = items;
			if (model != null && model.ConsumeScrollReset())
			{
				scroll.Offset = new Vector(0, 0);
			}
			ApplySelection(model?.SelectedIndex ?? -1);
		}

		private void ApplyMetrics(double coverHeight)
		{
			// A cover cell is a box of cover proportions (about 2:3 to 3:4) with two lines of text under it.
			cellWidth = Math.Round(coverHeight * 0.74) + 28;
			cellHeight = Math.Round(coverHeight) + 28 + 38;
			layout.MinItemWidth = cellWidth;
			layout.MinItemHeight = cellHeight;
		}

		private void OnViewportChanged()
		{
			// Only the number of columns changes with the width; the repeater lays out again by itself.
		}

		private void OnElementPrepared(object sender, ItemsRepeaterElementPreparedEventArgs e)
		{
			if (e.Element is BookCell cell && e.Index >= 0 && e.Index < items.Length)
			{
				realized++;
				cell.Bind(this, items[e.Index], e.Index, e.Index == selected);
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

		private void ApplySelection(int index)
		{
			int old = selected;
			selected = index;
			if (old >= 0)
			{
				CellAt(old)?.SetSelected(false);
			}
			if (index >= 0)
			{
				CellAt(index)?.SetSelected(true);
				EnsureVisible(index);
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

		protected override void OnPointerPressed(PointerPressedEventArgs e)
		{
			base.OnPointerPressed(e);
			if (model == null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
			{
				return;
			}
			Focus();
			BookCell cell = (e.Source as Visual)?.FindAncestorOfType<BookCell>(includeSelf: true);
			if (cell?.Item == null)
			{
				return;
			}
			model.SelectedIndex = cell.Index;
			if (e.ClickCount >= 2)
			{
				model.OpenSelected();
			}
			e.Handled = true;
		}

		protected override void OnKeyDown(KeyEventArgs e)
		{
			base.OnKeyDown(e);
			if (model == null || items.Length == 0 || e.Handled)
			{
				return;
			}
			int count = items.Length;
			int columns = Columns;
			int current = model.SelectedIndex;
			int target;
			switch (e.Key)
			{
				case Key.Enter:
					if (current >= 0)
					{
						model.OpenSelected();
						e.Handled = true;
					}
					return;
				case Key.Right:
					target = current < 0 ? 0 : current + 1;
					break;
				case Key.Left:
					target = current < 0 ? 0 : current - 1;
					break;
				case Key.Down:
					target = current < 0 ? 0 : current + columns;
					if (target >= count && current / columns < (count - 1) / columns)
					{
						target = count - 1; // from the row above a short last row to its last book
					}
					break;
				case Key.Up:
					target = current < 0 ? 0 : current - columns;
					break;
				case Key.Home:
					target = 0;
					break;
				case Key.End:
					target = count - 1;
					break;
				case Key.PageDown:
					target = (current < 0 ? 0 : current) + columns * VisibleRows();
					break;
				case Key.PageUp:
					target = (current < 0 ? 0 : current) - columns * VisibleRows();
					break;
				default:
					return;
			}
			e.Handled = true;
			target = Math.Clamp(target, 0, count - 1);
			if (target != current)
			{
				model.SelectedIndex = target;
			}
		}

		private int VisibleRows()
		{
			return Math.Max(1, (int)(scroll.Viewport.Height / (cellHeight + Spacing)));
		}
	}
}
