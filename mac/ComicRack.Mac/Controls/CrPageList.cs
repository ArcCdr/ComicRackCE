using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using ComicRack.Mac.Browser;
using ComicRack.Mac.Themes;

namespace ComicRack.Mac.Controls
{
	/// <summary>
	/// The list of pages of a book (the original's <c>ItemView</c> inside <c>PagesView</c>), drawn by itself: the detail view with the column headers (sort arrow, resizable columns, horizontal
	/// scrolling) and the thumbnail and tile views. The rows come from an <see cref="IPageListSource"/> (texts, states, pictures and the selection), so the control holds no data. The selection
	/// follows the rules of the original's item view (a click selects, Cmd toggles, Shift extends from the anchor, the arrow keys move the focus), Cmd and the wheel change the size of the rows
	/// and thumbnails. The look is the item view's: <c>Cr.ItemViewMainBack</c> paper, <c>Cr.DetailRowHighlight</c> on every second row, the selection style of <c>StyledSelection</c>, the
	/// thumbnails with the shadow, the frame, the page number badge and the bookmark flag of <c>ThumbRenderer</c>. Spec: <c>mac/specs/views/PagesView.md</c>.
	/// </summary>
	public sealed class CrPageList : Control, IChromeElement
	{
		/// <summary><c>Program.MinRowHeight</c> and <c>MaxRowHeight</c>: the range of the detail rows.</summary>
		public const double MinRowHeight = 12, MaxRowHeight = 48;

		/// <summary><c>Program.MinThumbHeight</c> and <c>MaxThumbHeight</c>.</summary>
		public const double MinThumbHeight = 96, MaxThumbHeight = 512;

		/// <summary><c>Program.MinTileHeight</c> and <c>MaxTileHeight</c>.</summary>
		public const double MinTileHeight = 64, MaxTileHeight = 512;

		/// <summary>The size of a thumbnail (<c>ItemView.itemThumbSize</c> 128 x 128) and of a tile (192 x 96) at the start.</summary>
		public const double DefaultThumbHeight = 128, DefaultTileHeight = 96;

		/// <summary>The space around the picture of a thumbnail cell and of a tile (<c>ThumbnailViewItem.border</c>, <c>ItemPadding</c>).</summary>
		public const double CellPadding = 4;

		/// <summary>The width of a column the user dragged to zero can come back by (its separator stays grabbable).</summary>
		private const double SeparatorGrab = 4;

		private readonly ScrollBar vertical = new ScrollBar { Orientation = Orientation.Vertical, Visibility = ScrollBarVisibility.Hidden };
		private readonly ScrollBar horizontal = new ScrollBar { Orientation = Orientation.Horizontal, Visibility = ScrollBarVisibility.Hidden };
		private bool verticalShown, horizontalShown;
		private IPageListSource source;
		private PageListMode mode = PageListMode.Detail;
		private double rowHeight = Math.Ceiling(ChromeFonts.LineHeight(ChromeFonts.ItemSize)) + 6; // PagesView: Font.Height + 6
		private double thumbHeight = DefaultThumbHeight;
		private double tileHeight = DefaultTileHeight;
		private Vector offset;
		private int focusRow = -1;
		private int anchorRow = -1;
		private int hotRow = -1;
		private int pressedColumn = -1;
		private int hotColumn = -1;
		private int resizeColumn = -1;
		private double resizeStart;
		private double resizeWidth;
		private bool settingBars;
		private bool suppressTip;

		public CrPageList()
		{
			Focusable = true;
			ClipToBounds = true;
			vertical.Scroll += OnBarScroll;
			horizontal.Scroll += OnBarScroll;
			vertical.PropertyChanged += OnBarChanged;
			horizontal.PropertyChanged += OnBarChanged;
			AddChild(vertical);
			AddChild(horizontal);
			ActualThemeVariantChanged += (sender, e) => InvalidateVisual();
			ChromeVariants.Bind(this);
		}

		public ChromeElement Element => ChromeElement.ItemView;

		// the scroll bars are visual and logical children of the control (a Control, not a Panel, because the list draws itself)
		private void AddChild(Control child)
		{
			((ISetLogicalParent)child).SetParent(this);
			VisualChildren.Add(child);
			LogicalChildren.Add(child);
		}

		// ---- properties ----------------------------------------------------------------------------------------------

		/// <summary>The rows, or null.</summary>
		public IPageListSource Source
		{
			get => source;
			set
			{
				if (ReferenceEquals(source, value))
				{
					return;
				}
				if (source != null)
				{
					source.Changed -= OnSourceChanged;
					source.RowsChanged -= OnRowsChanged;
					source.SelectionChanged -= OnSelectionChanged;
					source.PicturesChanged -= OnRowsChanged;
				}
				source = value;
				if (source != null)
				{
					source.Changed += OnSourceChanged;
					source.RowsChanged += OnRowsChanged;
					source.SelectionChanged += OnSelectionChanged;
					source.PicturesChanged += OnRowsChanged;
				}
				focusRow = anchorRow = hotRow = -1;
				offset = default;
				UpdateBars();
				InvalidateMeasure();
				InvalidateVisual();
			}
		}

		public PageListMode Mode
		{
			get => mode;
			set
			{
				if (mode != value)
				{
					mode = value;
					offset = default;
					UpdateBars();
					InvalidateArrange();
					InvalidateVisual();
					ViewChanged?.Invoke(this, EventArgs.Empty);
				}
			}
		}

		/// <summary>The height of a detail row (<c>ItemRowHeight</c>), 12 to 48.</summary>
		public double RowHeight
		{
			get => rowHeight;
			set => SetSize(ref rowHeight, Math.Clamp(Math.Round(value), MinRowHeight, MaxRowHeight), PageListMode.Detail);
		}

		/// <summary>The side of the square a thumbnail is drawn in (<c>ItemThumbSize</c>), 96 to 512.</summary>
		public double ThumbnailHeight
		{
			get => thumbHeight;
			set => SetSize(ref thumbHeight, Math.Clamp(Math.Round(value), MinThumbHeight, MaxThumbHeight), PageListMode.Thumbnail);
		}

		/// <summary>The height of a tile (<c>ItemTileSize</c>; the width is twice as much), 64 to 512.</summary>
		public double TileHeight
		{
			get => tileHeight;
			set => SetSize(ref tileHeight, Math.Clamp(Math.Round(value), MinTileHeight, MaxTileHeight), PageListMode.Tile);
		}

		private void SetSize(ref double field, double value, PageListMode affects)
		{
			if (field == value)
			{
				return;
			}
			field = value;
			if (mode == affects)
			{
				UpdateBars();
				InvalidateVisual();
			}
			ViewChanged?.Invoke(this, EventArgs.Empty);
		}

		/// <summary>The mode, a size or a column width changed (the view keeps these for the next time).</summary>
		public event EventHandler ViewChanged;

		/// <summary>A sortable column header was clicked.</summary>
		public event EventHandler<int> HeaderClicked;

		/// <summary>A row was double clicked or Return was pressed on the focused row (<c>ItemActivate</c>).</summary>
		public event EventHandler<int> ItemActivated;

		/// <summary>The index of the row with the keyboard focus, or -1.</summary>
		public int FocusRow => focusRow;

		/// <summary>The scroll position.</summary>
		public Vector Offset => offset;

		public ScrollBar VerticalBar => vertical;

		public ScrollBar HorizontalBar => horizontal;

		// ---- geometry --------------------------------------------------------------------------------------------------

		private int Count => source?.Count ?? 0;

		private IReadOnlyList<PageListColumn> Columns => source?.Columns ?? Array.Empty<PageListColumn>();

		private double HeaderHeight => mode == PageListMode.Detail ? ChromeMetrics.ColumnHeaderHeight : 0;

		/// <summary>The total width of the columns.</summary>
		public double ColumnsWidth => Columns.Sum(c => c.Width);

		private double BarWidth => verticalShown ? vertical.DesiredSize.Width : 0;

		private double BarHeight => horizontalShown ? horizontal.DesiredSize.Height : 0;

		/// <summary>The area the rows are drawn in: below the header, left of the vertical scroll bar and above the horizontal one.</summary>
		public Rect ViewRect => new Rect(0, HeaderHeight, Math.Max(0, Bounds.Width - BarWidth), Math.Max(0, Bounds.Height - HeaderHeight - BarHeight));

		/// <summary>The size of one cell in the thumbnail and tile views.</summary>
		public Size CellSize
		{
			get
			{
				if (mode == PageListMode.Thumbnail)
				{
					return new Size(thumbHeight + 2 * CellPadding + ThumbnailMetrics.ShadowDepth, thumbHeight + 2 * CellPadding + ThumbnailMetrics.ShadowDepth);
				}
				return new Size(tileHeight * 2 + 2 * CellPadding, tileHeight + 2 * CellPadding);
			}
		}

		private int ColumnsPerRow(double viewWidth) => mode == PageListMode.Detail ? 1 : Math.Max(1, (int)Math.Floor(viewWidth / CellSize.Width));

		/// <summary>The size of everything the list could show.</summary>
		public Size ContentSize => ContentSizeIn(ViewRect.Width);

		private Size ContentSizeIn(double viewWidth)
		{
			if (mode == PageListMode.Detail)
			{
				return new Size(ColumnsWidth, Count * rowHeight);
			}
			int perRow = ColumnsPerRow(viewWidth);
			return new Size(perRow * CellSize.Width, Math.Ceiling(Count / (double)perRow) * CellSize.Height);
		}

		/// <summary>The rectangle of a row in the control's coordinates (scrolled), or an empty one.</summary>
		public Rect RowRect(int row)
		{
			if (row < 0 || row >= Count)
			{
				return default;
			}
			Rect view = ViewRect;
			if (mode == PageListMode.Detail)
			{
				return new Rect(-offset.X, view.Y + row * rowHeight - offset.Y, Math.Max(ColumnsWidth, view.Width), rowHeight);
			}
			int perRow = ColumnsPerRow(view.Width);
			Size cell = CellSize;
			return new Rect(view.X + row % perRow * cell.Width, view.Y + row / perRow * cell.Height - offset.Y, cell.Width, cell.Height);
		}

		/// <summary>The rectangle of a column header in the control's coordinates.</summary>
		public Rect HeaderRect(int columnIndex)
		{
			double x = -offset.X;
			for (int i = 0; i < Columns.Count; i++)
			{
				if (i == columnIndex)
				{
					return new Rect(x, 0, Columns[i].Width, HeaderHeight);
				}
				x += Columns[i].Width;
			}
			return default;
		}

		/// <summary>The rectangle of the cell of a column in a row (the detail view).</summary>
		public Rect CellRect(int row, int columnIndex)
		{
			Rect r = RowRect(row);
			Rect h = HeaderRect(columnIndex);
			return new Rect(h.X, r.Y, h.Width, r.Height);
		}

		/// <summary>The row at a point of the control, or -1.</summary>
		public int RowAt(Point point)
		{
			Rect view = ViewRect;
			if (!view.Contains(point) || Count == 0)
			{
				return -1;
			}
			if (mode == PageListMode.Detail)
			{
				int row = (int)Math.Floor((point.Y - view.Y + offset.Y) / rowHeight);
				return row >= 0 && row < Count ? row : -1;
			}
			int perRow = ColumnsPerRow(view.Width);
			Size cell = CellSize;
			int column = (int)Math.Floor((point.X - view.X) / cell.Width);
			int line = (int)Math.Floor((point.Y - view.Y + offset.Y) / cell.Height);
			int index = line * perRow + column;
			return column < perRow && index >= 0 && index < Count ? index : -1;
		}

		/// <summary>The index of the column header at a point (y inside the header), or -1.</summary>
		public int HeaderAt(Point point)
		{
			if (mode != PageListMode.Detail || point.Y < 0 || point.Y >= HeaderHeight)
			{
				return -1;
			}
			for (int i = 0; i < Columns.Count; i++)
			{
				if (HeaderRect(i).Contains(point))
				{
					return i;
				}
			}
			return -1;
		}

		// a point on the right edge of a header (4 units around it): the separator that resizes the column
		private int SeparatorAt(Point point)
		{
			if (mode != PageListMode.Detail || point.Y < 0 || point.Y >= HeaderHeight)
			{
				return -1;
			}
			for (int i = 0; i < Columns.Count; i++)
			{
				double edge = HeaderRect(i).Right;
				if (Math.Abs(point.X - edge) <= SeparatorGrab / 2 + 1)
				{
					return i;
				}
			}
			return -1;
		}

		/// <summary>Scrolls so that the row is inside the view.</summary>
		public void EnsureVisible(int row)
		{
			if (row < 0 || row >= Count)
			{
				return;
			}
			Rect view = ViewRect;
			Rect r = RowRect(row);
			double y = offset.Y;
			if (r.Y < view.Y)
			{
				y -= view.Y - r.Y;
			}
			else if (r.Bottom > view.Bottom)
			{
				y += r.Bottom - view.Bottom;
			}
			SetOffset(new Vector(offset.X, y));
		}

		private void SetOffset(Vector value)
		{
			Rect view = ViewRect;
			Size content = ContentSizeIn(view.Width);
			var clamped = new Vector(Math.Round(Math.Clamp(value.X, 0, Math.Max(0, content.Width - view.Width))), Math.Round(Math.Clamp(value.Y, 0, Math.Max(0, content.Height - view.Height))));
			if (clamped == offset)
			{
				return;
			}
			offset = clamped;
			UpdateBars();
			InvalidateVisual();
		}

		// ---- scroll bars and layout --------------------------------------------------------------------------------------

		protected override Size MeasureOverride(Size availableSize)
		{
			vertical.Measure(availableSize);
			horizontal.Measure(availableSize);
			double w = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;
			double h = double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height;
			return new Size(w, h);
		}

		protected override Size ArrangeOverride(Size finalSize)
		{
			// the bars are visible when the content is larger than the view; the width of the vertical bar decides about the horizontal one and the other way round
			Size vb = vertical.DesiredSize, hb = horizontal.DesiredSize;
			bool needV = false, needH = false;
			for (int pass = 0; pass < 2; pass++)
			{
				double viewW = finalSize.Width - (needV ? vb.Width : 0), viewH = finalSize.Height - HeaderHeight - (needH ? hb.Height : 0);
				Size content = ContentSizeIn(viewW);
				needV = content.Height > viewH + 0.5;
				needH = mode == PageListMode.Detail && content.Width > viewW + 0.5;
			}
			verticalShown = needV;
			horizontalShown = needH;
			vertical.Visibility = needV ? ScrollBarVisibility.Visible : ScrollBarVisibility.Hidden;
			horizontal.Visibility = needH ? ScrollBarVisibility.Visible : ScrollBarVisibility.Hidden;
			vertical.Arrange(new Rect(finalSize.Width - vb.Width, 0, vb.Width, Math.Max(0, finalSize.Height - (needH ? hb.Height : 0))));
			horizontal.Arrange(new Rect(0, finalSize.Height - hb.Height, Math.Max(0, finalSize.Width - (needV ? vb.Width : 0)), hb.Height));
			UpdateBars(finalSize);
			return finalSize;
		}

		private void UpdateBars(Size? size = null)
		{
			Size full = size ?? Bounds.Size;
			double barW = BarWidth, barH = BarHeight;
			double viewW = Math.Max(0, full.Width - barW), viewH = Math.Max(0, full.Height - HeaderHeight - barH);
			Size content = ContentSizeIn(viewW);
			settingBars = true;
			try
			{
				vertical.Minimum = 0;
				vertical.Maximum = Math.Max(0, content.Height - viewH);
				vertical.ViewportSize = viewH;
				vertical.SmallChange = mode == PageListMode.Detail ? rowHeight : CellSize.Height / 2;
				vertical.LargeChange = Math.Max(1, viewH);
				vertical.Value = offset.Y;
				horizontal.Minimum = 0;
				horizontal.Maximum = Math.Max(0, content.Width - viewW);
				horizontal.ViewportSize = viewW;
				horizontal.SmallChange = 20;
				horizontal.LargeChange = Math.Max(1, viewW);
				horizontal.Value = offset.X;
			}
			finally
			{
				settingBars = false;
			}
			// the content shrank (rows removed, window larger): the offset follows
			Vector clamped = new Vector(Math.Clamp(offset.X, 0, horizontal.Maximum), Math.Clamp(offset.Y, 0, vertical.Maximum));
			if (clamped != offset)
			{
				offset = clamped;
				InvalidateVisual();
			}
		}

		private void OnBarScroll(object sender, ScrollEventArgs e)
		{
			if (!settingBars)
			{
				SetOffset(new Vector(horizontal.Value, vertical.Value));
			}
		}

		private void OnBarChanged(object sender, AvaloniaPropertyChangedEventArgs e)
		{
			if (!settingBars && e.Property == RangeBase.ValueProperty)
			{
				SetOffset(new Vector(horizontal.Value, vertical.Value));
			}
		}

		private void OnSourceChanged(object sender, EventArgs e)
		{
			if (focusRow >= Count)
			{
				focusRow = Count - 1;
			}
			if (anchorRow >= Count)
			{
				anchorRow = -1;
			}
			hotRow = -1;
			InvalidateArrange();
			UpdateBars();
			InvalidateVisual();
		}

		private void OnRowsChanged(object sender, EventArgs e) => InvalidateVisual();

		private void OnSelectionChanged(object sender, EventArgs e) => InvalidateVisual();

		protected override void OnSizeChanged(SizeChangedEventArgs e)
		{
			base.OnSizeChanged(e);
			UpdateBars();
			InvalidateVisual();
		}

		// ---- drawing -----------------------------------------------------------------------------------------------------

		private bool ViewFocused => IsKeyboardFocusWithin;

		public override void Render(DrawingContext context)
		{
			var all = new Rect(Bounds.Size);
			context.DrawRectangle(ChromePaint.Brush(this, ThemeTokens.Names.ItemViewMainBack), null, all);
			if (source == null)
			{
				return;
			}
			Rect view = ViewRect;
			using (context.PushClip(view))
			{
				switch (mode)
				{
					case PageListMode.Detail:
						DrawDetail(context, view);
						break;
					case PageListMode.Thumbnail:
						DrawCells(context, view, tile: false);
						break;
					default:
						DrawCells(context, view, tile: true);
						break;
				}
			}
			if (mode == PageListMode.Detail)
			{
				DrawHeaders(context);
			}
		}

		private (int First, int Last) VisibleRows(Rect view)
		{
			if (Count == 0)
			{
				return (0, -1);
			}
			if (mode == PageListMode.Detail)
			{
				int first = Math.Max(0, (int)Math.Floor(offset.Y / rowHeight));
				int last = Math.Min(Count - 1, (int)Math.Floor((offset.Y + view.Height) / rowHeight));
				return (first, last);
			}
			int perRow = ColumnsPerRow(view.Width);
			double cellH = CellSize.Height;
			int firstLine = Math.Max(0, (int)Math.Floor(offset.Y / cellH));
			int lastLine = (int)Math.Floor((offset.Y + view.Height) / cellH);
			return (Math.Min(Count - 1, firstLine * perRow), Math.Min(Count - 1, lastLine * perRow + perRow - 1));
		}

		private StyledSelection StyleOf(int row)
		{
			bool selected = IsSelected(row);
			return StyledSelection.For(StyledSelection.GetState(selected, row == hotRow, row == focusRow && ViewFocused), ViewFocused, ChromePaint.VariantOf(this));
		}

		private HashSet<int> selectionCache;

		private bool IsSelected(int row)
		{
			selectionCache ??= new HashSet<int>(source.Selection);
			return selectionCache.Contains(row);
		}

		private void DrawDetail(DrawingContext context, Rect view)
		{
			selectionCache = null;
			(int first, int last) = VisibleRows(view);
			IBrush text = ChromePaint.Brush(this, ThemeTokens.Names.ControlText);
			IBrush stripe = ChromePaint.Brush(this, ThemeTokens.Names.DetailRowHighlight);
			FontFamily family = TextElement.GetFontFamily(this);
			for (int row = first; row <= last; row++)
			{
				Rect r = RowRect(row);
				if (r.Bottom < view.Y || r.Y > view.Bottom)
				{
					continue;
				}
				bool selected = IsSelected(row);
				if (row % 2 == 0 && !selected)
				{
					context.DrawRectangle(stripe, null, r);
				}
				StyledSelection style = StyleOf(row);
				if (style.State != SelectionState.None)
				{
					ChromePaint.Selection(this, context, r, style);
				}
				for (int c = 0; c < Columns.Count; c++)
				{
					PageListColumn column = Columns[c];
					Rect cell = CellRect(row, c);
					if (cell.Right < 0 || cell.X > view.Right || column.Width <= 0)
					{
						continue;
					}
					using (context.PushClip(new Rect(cell.X, Math.Max(cell.Y, view.Y), cell.Width, cell.Height)))
					{
						if (column.IsThumbnail)
						{
							DrawDetailPicture(context, row, cell);
						}
						else
						{
							DrawCellText(context, source.Text(row, column.Id), cell, column.FarAligned, text, family);
						}
					}
				}
			}
		}

		// PageViewItem.OnDraw, Detail: the thumbnail as wide as the cell with its aspect (taller than a normal row: the row cuts it off), 2 units in, with the frame and the flags
		private void DrawDetailPicture(DrawingContext context, int row, Rect cell)
		{
			CoverImage picture = source.Picture(row, 256);
			PageRowState state = source.State(row);
			try
			{
				Rect bounds = cell.Deflate(2);
				if (picture != null && picture.Width > 0)
				{
					bounds = new Rect(bounds.X, cell.Y + 2, bounds.Width, Math.Max(1, Math.Floor(bounds.Width * picture.Height / picture.Width)));
					context.Custom(new CoverDrawOperation(picture, bounds));
					ChromePaint.Frame(context, ChromePaint.ResourceBrush(this, "Cr.Thumb.BorderBrush"), new Rect(bounds.X, bounds.Y, bounds.Width + 1, bounds.Height + 1));
				}
				else
				{
					bounds = new Rect(bounds.X, bounds.Y, bounds.Width, Math.Max(1, bounds.Height));
					context.DrawRectangle(ChromePaint.ResourceBrush(this, "Cr.Thumb.MissingBackBrush"), null, bounds);
				}
				DrawFlags(context, bounds, state, bookmarkPercent: 0);
			}
			finally
			{
				picture?.Release();
			}
		}

		// PageViewItem.DeletedStateImage over the thumbnail, and the bookmark flag
		private void DrawFlags(DrawingContext context, Rect picture, PageRowState state, double bookmarkPercent)
		{
			if (state.IsBookmarked)
			{
				ThumbnailAdornments.DrawBookmark(context, this, picture, bookmarkPercent, 0);
			}
			if (state.IsDeleted)
			{
				Avalonia.Media.Imaging.Bitmap deleted = Icons.TryGet("Deleted", ChromePaint.VariantOf(this));
				if (deleted != null)
				{
					double size = Math.Min(Math.Min(picture.Width, picture.Height), deleted.Size.Width);
					ChromePaint.Image(context, deleted, new Rect(picture.X + 1, picture.Bottom - size - 1, size, size));
				}
			}
		}

		private void DrawCellText(DrawingContext context, string value, Rect cell, bool far, IBrush brush, FontFamily family)
		{
			if (string.IsNullOrEmpty(value))
			{
				return;
			}
			Rect area = cell.Deflate(new Thickness(2, 0));
			if (area.Width <= 0)
			{
				return;
			}
			var formatted = new FormattedText(value, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(family), ChromeFonts.ItemSize, brush)
			{
				MaxTextWidth = area.Width,
				MaxLineCount = 1,
				Trimming = TextTrimming.CharacterEllipsis,
				TextAlignment = far ? TextAlignment.Right : TextAlignment.Left,
			};
			context.DrawText(formatted, new Point(area.X, Math.Floor(area.Y + Math.Max(0, (area.Height - formatted.Height) / 2))));
		}

		private void DrawHeaders(DrawingContext context)
		{
			FontFamily family = TextElement.GetFontFamily(this);
			double viewRight = ViewRect.Right;
			context.DrawRectangle(ChromePaint.Brush(this, ThemeTokens.Names.HeaderBack), null, new Rect(0, 0, Bounds.Width, HeaderHeight));
			using (context.PushClip(new Rect(0, 0, viewRight, HeaderHeight)))
			{
				for (int c = 0; c < Columns.Count; c++)
				{
					PageListColumn column = Columns[c];
					Rect h = HeaderRect(c);
					if (h.Right < 0 || h.X > viewRight || h.Width <= 0)
					{
						continue;
					}
					HeaderAdornments adornments = HeaderAdornments.None;
					if (source.SortColumn == column.Id)
					{
						adornments = source.SortAscending ? HeaderAdornments.SortDown : HeaderAdornments.SortUp;
					}
					HeaderState state = pressedColumn == c ? HeaderState.Pressed : hotColumn == c ? HeaderState.Hot : source.SortColumn == column.Id ? HeaderState.Active : HeaderState.Normal;
					CrHeader.Draw(context, this, h, column.Title, column.FarAligned ? TextAlignment.Right : TextAlignment.Left, adornments, state, family, ChromeFonts.ItemSize);
				}
			}
		}

		private void DrawCells(DrawingContext context, Rect view, bool tile)
		{
			selectionCache = null;
			(int first, int last) = VisibleRows(view);
			FontFamily family = TextElement.GetFontFamily(this);
			IBrush text = ChromePaint.Brush(this, ThemeTokens.Names.ControlText);
			for (int row = first; row <= last; row++)
			{
				Rect cell = RowRect(row);
				if (cell.Bottom < view.Y || cell.Y > view.Bottom)
				{
					continue;
				}
				StyledSelection style = StyleOf(row);
				if (style.State != SelectionState.None)
				{
					ChromePaint.Selection(this, context, cell.Deflate(1), style);
				}
				PageRowState state = source.State(row);
				Rect inner = cell.Deflate(CellPadding);
				if (tile)
				{
					DrawTile(context, row, inner, state, text);
				}
				else
				{
					DrawThumbnail(context, row, inner, state);
				}
			}
		}

		// ThumbnailViewItem / ThumbRenderer.DrawThumbnail: the picture as large as fits in the square, the shadow right and below, the one unit frame, the page number badge, the flags
		private void DrawThumbnail(DrawingContext context, int row, Rect inner, PageRowState state)
		{
			double box = thumbHeight;
			var area = new Rect(inner.X, inner.Y, Math.Min(inner.Width, box + ThumbnailMetrics.ShadowDepth), Math.Min(inner.Height, box + ThumbnailMetrics.ShadowDepth));
			Rect picture = DrawPicture(context, row, new Rect(area.X, area.Y, Math.Max(0, area.Width - ThumbnailMetrics.ShadowDepth), Math.Max(0, area.Height - ThumbnailMetrics.ShadowDepth)), (int)thumbHeight);
			if (picture.Width <= 0)
			{
				return;
			}
			int number;
			if (int.TryParse(state.Number, NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
			{
				ThumbnailAdornments.DrawPageNumber(context, this, picture, number);
			}
			DrawFlags(context, picture, state, 0);
		}

		// the picture centred in a box with its shadow and frame; returns where it is
		private Rect DrawPicture(DrawingContext context, int row, Rect box, int height)
		{
			CoverImage picture = source.Picture(row, height);
			try
			{
				Rect fit = picture != null && picture.Width > 0 ? CoverView.Fit(box.Width, box.Height, picture.Width, picture.Height) : CoverView.Fit(box.Width, box.Height, 2, 3);
				var t = new Rect(Math.Round(box.X + fit.X), Math.Round(box.Y + fit.Y), Math.Round(fit.Width), Math.Round(fit.Height));
				if (t.Width <= 0 || t.Height <= 0)
				{
					return default;
				}
				ThumbnailShadow.Draw(context, this, t);
				if (picture == null)
				{
					context.DrawRectangle(ChromePaint.ResourceBrush(this, "Cr.Thumb.MissingBackBrush"), null, t);
				}
				else
				{
					context.Custom(new CoverDrawOperation(picture, t));
				}
				ChromePaint.Frame(context, ChromePaint.ResourceBrush(this, "Cr.Thumb.BorderBrush"), new Rect(t.X, t.Y, t.Width + 1, t.Height + 1));
				return t;
			}
			finally
			{
				picture?.Release();
			}
		}

		// ThumbTileRenderer.DrawTile: the picture on the left (half of the width at most), the text lines beside it
		private void DrawTile(DrawingContext context, int row, Rect inner, PageRowState state, IBrush text)
		{
			var box = new Rect(inner.X, inner.Y, Math.Max(0, Math.Min(inner.Width / 2, inner.Height) - ThumbnailMetrics.ShadowDepth), Math.Max(0, inner.Height - ThumbnailMetrics.ShadowDepth));
			Rect picture = DrawPicture(context, row, box, (int)Math.Ceiling(inner.Height));
			if (picture.Width > 0)
			{
				DrawFlags(context, picture, state, 0);
			}
			double textX = (picture.Width > 0 ? picture.Right : box.X) + 4 + ThumbnailMetrics.ShadowDepth;
			var area = new Rect(textX, inner.Y, Math.Max(0, inner.Right - textX), inner.Height);
			// PageViewItem.OnDraw, Tile: the text font is the list font made as large as the tile allows, between 0.8 and 1 times its size (FC.Get(font, (Height * 0.07).Clamp(Size * 0.8, Size)))
			double fontSize = ChromeFonts.ItemSize * Math.Clamp(inner.Height * 0.07 / 9.0, 0.8, 1.0);
			TileTextPainter.Draw(context, source.Lines(row), area, false, fontSize, line => text);
		}

		// ---- pointer -----------------------------------------------------------------------------------------------------

		protected override void OnPointerPressed(PointerPressedEventArgs e)
		{
			base.OnPointerPressed(e);
			Focus();
			if (source == null)
			{
				return;
			}
			PointerPoint pp = e.GetCurrentPoint(this);
			Point p = pp.Position;
			bool left = pp.Properties.IsLeftButtonPressed, right = pp.Properties.IsRightButtonPressed;
			if (!left && !right)
			{
				return;
			}
			if (p.Y < HeaderHeight && mode == PageListMode.Detail)
			{
				if (!left)
				{
					return;
				}
				int separator = SeparatorAt(p);
				if (separator >= 0)
				{
					resizeColumn = separator;
					resizeStart = p.X;
					resizeWidth = Columns[separator].Width;
					e.Pointer.Capture(this);
					e.Handled = true;
					return;
				}
				int header = HeaderAt(p);
				if (header >= 0)
				{
					pressedColumn = header;
					e.Pointer.Capture(this);
					InvalidateVisual();
					e.Handled = true;
				}
				return;
			}
			int row = RowAt(p);
			bool toggle = (e.KeyModifiers & KeyModifiers.Meta) != 0, extend = (e.KeyModifiers & KeyModifiers.Shift) != 0;
			if (row < 0)
			{
				if (left && !toggle && !extend)
				{
					source.SetSelection(Array.Empty<int>());
					focusRow = -1;
					anchorRow = -1;
					InvalidateVisual();
				}
				return;
			}
			if (right)
			{
				// a context click on a page that is not selected selects it alone; on a selected one it keeps the selection
				if (!IsSelectedNow(row))
				{
					ClickRow(row, false, false);
				}
				else
				{
					focusRow = row;
				}
				return;
			}
			ClickRow(row, toggle, extend);
			if (e.ClickCount == 2 && !toggle && !extend)
			{
				ItemActivated?.Invoke(this, row);
			}
			e.Handled = true;
		}

		private bool IsSelectedNow(int row) => source.Selection.Contains(row);

		protected override void OnPointerMoved(PointerEventArgs e)
		{
			base.OnPointerMoved(e);
			if (source == null)
			{
				return;
			}
			Point p = e.GetPosition(this);
			if (resizeColumn >= 0)
			{
				Columns[resizeColumn].Width = Math.Clamp(resizeWidth + p.X - resizeStart, 0, 10000);
				UpdateBars();
				InvalidateVisual();
				ViewChanged?.Invoke(this, EventArgs.Empty);
				return;
			}
			int header = HeaderAt(p);
			if (header != hotColumn)
			{
				hotColumn = header;
				InvalidateVisual();
			}
			Cursor = mode == PageListMode.Detail && SeparatorAt(p) >= 0 ? new Cursor(StandardCursorType.SizeWestEast) : null;
			SetHot(RowAt(p));
		}

		protected override void OnPointerExited(PointerEventArgs e)
		{
			base.OnPointerExited(e);
			hotColumn = -1;
			SetHot(-1);
			InvalidateVisual();
		}

		private void SetHot(int row)
		{
			if (row == hotRow)
			{
				return;
			}
			hotRow = row;
			ToolTip.SetTip(this, row >= 0 && source != null && !suppressTip ? source.ToolTip(row) : null);
			InvalidateVisual();
		}

		protected override void OnPointerReleased(PointerReleasedEventArgs e)
		{
			base.OnPointerReleased(e);
			if (resizeColumn >= 0)
			{
				resizeColumn = -1;
				e.Pointer.Capture(null);
				return;
			}
			if (pressedColumn >= 0)
			{
				int pressed = pressedColumn;
				pressedColumn = -1;
				e.Pointer.Capture(null);
				if (HeaderAt(e.GetPosition(this)) == pressed && Columns[pressed].CanSort)
				{
					HeaderClicked?.Invoke(this, Columns[pressed].Id);
				}
				InvalidateVisual();
			}
		}

		protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
		{
			base.OnPointerWheelChanged(e);
			if (source == null)
			{
				return;
			}
			if ((e.KeyModifiers & KeyModifiers.Meta) != 0)
			{
				// SetItemSize: one wheel notch is 16 units for thumbnails and tiles, one for rows
				int notches = Math.Sign(e.Delta.Y);
				switch (mode)
				{
					case PageListMode.Thumbnail:
						ThumbnailHeight = thumbHeight + notches * 16;
						break;
					case PageListMode.Tile:
						TileHeight = tileHeight + notches * 16;
						break;
					default:
						RowHeight = rowHeight + notches;
						break;
				}
				e.Handled = true;
				return;
			}
			double step = mode == PageListMode.Detail ? rowHeight * 3 : CellSize.Height / 2;
			SetOffset(new Vector(offset.X - e.Delta.X * 20, offset.Y - e.Delta.Y * step));
			e.Handled = true;
		}

		// ---- selection and keys ------------------------------------------------------------------------------------------

		// UpdateSelectionFromMouse
		private void ClickRow(int row, bool toggle, bool extend)
		{
			var selection = new HashSet<int>(source.Selection);
			if (extend && anchorRow >= 0 && anchorRow < Count)
			{
				if (!toggle)
				{
					selection.Clear();
				}
				for (int i = Math.Min(anchorRow, row); i <= Math.Max(anchorRow, row); i++)
				{
					selection.Add(i);
				}
			}
			else if (toggle)
			{
				// the anchor of a range stays where the last plain click put it
				if (!selection.Remove(row))
				{
					selection.Add(row);
				}
			}
			else
			{
				selection.Clear();
				selection.Add(row);
				anchorRow = row;
			}
			focusRow = row;
			source.SetSelection(selection);
			InvalidateVisual();
		}

		// OnKeyDown: a movement key takes the focus to the target; Shift extends the range from the anchor, Cmd only moves the focus
		private void MoveFocus(int target, bool toggle, bool extend)
		{
			target = Math.Clamp(target, 0, Count - 1);
			if (toggle && !extend)
			{
				focusRow = target;
			}
			else if (extend)
			{
				if (anchorRow < 0)
				{
					anchorRow = focusRow >= 0 ? focusRow : 0;
				}
				var selection = new HashSet<int>();
				for (int i = Math.Min(anchorRow, target); i <= Math.Max(anchorRow, target); i++)
				{
					selection.Add(i);
				}
				focusRow = target;
				source.SetSelection(selection);
			}
			else
			{
				anchorRow = target;
				focusRow = target;
				source.SetSelection(new[] { target });
			}
			EnsureVisible(focusRow);
			InvalidateVisual();
		}

		protected override void OnKeyDown(KeyEventArgs e)
		{
			base.OnKeyDown(e);
			if (source == null || e.Handled || Count == 0)
			{
				return;
			}
			bool toggle = (e.KeyModifiers & KeyModifiers.Meta) != 0, extend = (e.KeyModifiers & KeyModifiers.Shift) != 0;
			int current = focusRow >= 0 ? focusRow : 0;
			int perRow = ColumnsPerRow(ViewRect.Width);
			int page = Math.Max(1, (int)(ViewRect.Height / (mode == PageListMode.Detail ? rowHeight : CellSize.Height)) * perRow);
			int target = -1;
			bool move = true;
			switch (e.Key)
			{
				case Key.Up:
					target = current - perRow;
					break;
				case Key.Down:
					target = focusRow < 0 ? 0 : current + perRow;
					break;
				case Key.Left when mode != PageListMode.Detail:
					target = current - 1;
					break;
				case Key.Right when mode != PageListMode.Detail:
					target = focusRow < 0 ? 0 : current + 1;
					break;
				case Key.PageUp:
					target = current - page;
					break;
				case Key.PageDown:
					target = current + page;
					break;
				case Key.Home:
					target = 0;
					break;
				case Key.End:
					target = Count - 1;
					break;
				case Key.Space:
					if (focusRow >= 0)
					{
						ClickRow(focusRow, toggle, false);
						e.Handled = true;
					}
					return;
				case Key.Return:
					if (focusRow >= 0)
					{
						ItemActivated?.Invoke(this, focusRow);
						e.Handled = true;
					}
					return;
				case Key.A when toggle:
					source.SetSelection(Enumerable.Range(0, Count));
					e.Handled = true;
					InvalidateVisual();
					return;
				default:
					move = false;
					break;
			}
			if (move)
			{
				MoveFocus(target, toggle && !extend, extend);
				e.Handled = true;
			}
		}

		protected override void OnGotFocus(FocusChangedEventArgs e)
		{
			base.OnGotFocus(e);
			if (focusRow < 0 && Count > 0)
			{
				focusRow = 0;
			}
			InvalidateVisual();
		}

		protected override void OnLostFocus(FocusChangedEventArgs e)
		{
			base.OnLostFocus(e);
			InvalidateVisual();
		}
	}
}
