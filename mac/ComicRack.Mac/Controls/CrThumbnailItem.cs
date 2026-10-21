using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using ComicRack.Mac.Browser;
using ComicRack.Mac.Themes;

namespace ComicRack.Mac.Controls
{
	/// <summary>What the thumbnail of a book shows besides its cover: the facts the badges are made of (all plain values, no Core types).</summary>
	public sealed class ThumbnailBadges
	{
		/// <summary>No badges at all (a cover that is only a cover).</summary>
		public static readonly ThumbnailBadges None = new ThumbnailBadges();

		/// <summary>Pages of the book; the ribbons are positions in it.</summary>
		public int PageCount { get; set; }

		/// <summary>The page the book was left at, and the last page read: two ribbons on the right edge (orange and green).</summary>
		public int CurrentPage { get; set; }

		public int LastPageRead { get; set; }

		/// <summary>False for a fileless book entry: it has no ribbons (<c>EnableVerticalBookmarks</c> is removed for it).</summary>
		public bool ShowRibbons { get; set; }

		/// <summary>The user's rating (yellow), 0 to 5, and the community rating (blue).</summary>
		public float Rating { get; set; }

		public float CommunityRating { get; set; }

		/// <summary>Resource names of the state icons in drawing order (<c>UpdateBig</c>, <c>OpenMarker</c>, <c>LastMarker</c>, <c>NewPages1</c> ..., <c>FilelessMarker</c>, <c>Deleted</c>).</summary>
		public IReadOnlyList<string> StateIcons { get; set; } = Array.Empty<string>();

		/// <summary>A number in a black badge over the top right corner of the cover (<c>EnablePageNumber</c>; the browser does not use it, the pages view does), or 0.</summary>
		public int PageNumber { get; set; }
	}

	/// <summary>
	/// One thumbnail of the browser as <c>ThumbIconRenderer</c> and <c>ThumbRenderer</c> draw it (chrome-guidelines section 4, decision C-LIST-1):
	/// a cover with a 4 unit blurred shadow on its right and bottom side, a one unit black frame, the badges (state icons bottom left,
	/// two ribbons on the right edge, user and community rating as tags or stars, the dog-ear of the selected book), the "Loading Thumbnail..."
	/// tile while the cover is not there, and under the cover the caption (three lines at most) with the selection style of the original behind it.
	/// The item owns a <see cref="CoverView"/> for the picture; a view gives it data (<see cref="Badges"/>, <see cref="CaptionText"/>, the picture)
	/// and state (<see cref="SetSelected"/>) and never decides how anything looks.
	/// </summary>
	public class CrThumbnailItem : Border, IChromeElement
	{
		private readonly CoverView cover = new CoverView { SnapToUnits = true };
		private readonly TextBlock caption = new TextBlock
		{
			TextAlignment = TextAlignment.Center,
			TextWrapping = TextWrapping.Wrap,
			TextTrimming = TextTrimming.CharacterEllipsis,
			MaxLines = ThumbnailMetrics.LabelLines,
			HorizontalAlignment = HorizontalAlignment.Stretch,
			VerticalAlignment = VerticalAlignment.Stretch,
			IsHitTestVisible = false,
		};
		private readonly Backdrop backdrop;
		private readonly ThumbnailPanel panel;
		private readonly Overlay overlay;
		private double coverHeight = 200;
		private bool selected;
		private bool hot;
		private bool itemFocused;
		private bool viewFocused;
		private bool sameSize;
		private bool dogEar = true;
		private ThumbnailRatingMode ratingMode = ThumbnailRatingMode.Tags;
		private ThumbnailBadges badges = ThumbnailBadges.None;
		private CoverImage backImage;
		private string loadingText = "Loading Thumbnail...";

		public CrThumbnailItem()
		{
			backdrop = new Backdrop(this);
			overlay = new Overlay(this);
			panel = new ThumbnailPanel(this) { Children = { backdrop, cover, overlay, caption } };
			Child = panel;
			Background = Brushes.Transparent; // the whole item takes the pointer (selection, hot state)
			ClipToBounds = true;
			ApplyTokens();
			ActualThemeVariantChanged += (sender, e) =>
			{
				ApplyTokens();
				Redraw(); // the brushes of the shadow, the frame and the selection are cached per appearance
			};
			AttachedToVisualTree += (sender, e) => ApplyTokens();
			cover.PropertyChanged += (sender, e) =>
			{
				if (e.Property == Visual.BoundsProperty)
				{
					Redraw();
				}
			};
			cover.ImageChanged += (sender, e) => Redraw();
			ChromeVariants.Bind(this);
		}

		public ChromeElement Element => ChromeElement.ItemView;

		/// <summary>The picture area. The item sizes and places it; a view sets its picture.</summary>
		public CoverView Cover => cover;

		/// <summary>The cover height of the browser (the item size): the caption font and the number of caption lines scale with it.</summary>
		public double CoverHeight
		{
			get => coverHeight;
			set
			{
				if (coverHeight != value)
				{
					coverHeight = value;
					caption.FontSize = ThumbnailMetrics.LabelFontSize(value);
					panel.InvalidateMeasure();
					InvalidateMeasure();
					Redraw();
				}
			}
		}

		/// <summary>The caption text (<c>ComicBook.Caption</c>).</summary>
		public string CaptionText
		{
			get => caption.Text;
			set
			{
				caption.Text = value;
				Redraw();
			}
		}

		/// <summary>The caption as it is laid out, for tests and for the highlight behind it.</summary>
		public TextBlock CaptionBlock => caption;

		public ThumbnailBadges Badges
		{
			get => badges;
			set
			{
				badges = value ?? ThumbnailBadges.None;
				Redraw();
			}
		}

		public ThumbnailRatingMode RatingMode
		{
			get => ratingMode;
			set
			{
				if (ratingMode != value)
				{
					ratingMode = value;
					panel.InvalidateMeasure();
					InvalidateArrange();
					Redraw();
				}
			}
		}

		/// <summary><c>CoverThumbnailsSameSize</c>: every cover fills the same 2:3 box.</summary>
		public bool SameSizeCovers
		{
			get => sameSize;
			set
			{
				if (sameSize != value)
				{
					sameSize = value;
					cover.FillBox = value;
					panel.InvalidateMeasure();
					InvalidateArrange();
					Redraw();
				}
			}
		}

		/// <summary><c>DogEarThumbnails</c>.</summary>
		public bool DogEarEnabled
		{
			get => dogEar;
			set
			{
				dogEar = value;
				Redraw();
			}
		}

		/// <summary>The picture of the page behind the dog-ear (the item takes a reference), or null.</summary>
		public CoverImage BackImage
		{
			get => backImage;
			set
			{
				if (!ReferenceEquals(backImage, value))
				{
					CoverImage old = backImage;
					backImage = value?.Retain();
					old?.Release();
					Redraw();
				}
			}
		}

		/// <summary>The text of the empty tile (<c>TR.Messages["loadingThumbnail"]</c>).</summary>
		public string LoadingText
		{
			get => loadingText;
			set
			{
				loadingText = value;
				Redraw();
			}
		}

		public bool IsSelected => selected;

		public bool IsHot => hot;

		/// <summary>The item that has the focus (a focused item without selection gets the frame only); the selected item has it in a single selection browser.</summary>
		public bool IsFocusedItem
		{
			get => itemFocused;
			set
			{
				if (itemFocused != value)
				{
					itemFocused = value;
					Redraw();
				}
			}
		}

		/// <summary>True while the browser has the keyboard focus: the selection takes <c>Cr.SelectionFocused</c>, else <c>Cr.SelectionUnfocused</c>.</summary>
		public bool ViewFocused
		{
			get => viewFocused;
			set
			{
				if (viewFocused != value)
				{
					viewFocused = value;
					Redraw();
				}
			}
		}

		/// <summary>Sets the <c>:selected</c> state.</summary>
		public virtual void SetSelected(bool value)
		{
			selected = value;
			PseudoClasses.Set(":selected", value);
			Redraw();
		}

		/// <summary>The style of the selection frame and fill behind the caption for the current state.</summary>
		public StyledSelection SelectionStyle => StyledSelection.For(StyledSelection.GetState(selected, hot, itemFocused), viewFocused, ChromePaint.VariantOf(this));

		/// <summary>
		/// The rectangle the selection style is drawn in behind the caption (<c>ThumbIconRenderer.Draw</c>): the text as laid out, two units larger on
		/// every side, inside the caption area; empty when there is no text or no state to show.
		/// </summary>
		public Rect CaptionHighlight
		{
			get
			{
				if (string.IsNullOrEmpty(caption.Text) || SelectionStyle.State == SelectionState.None)
				{
					return default;
				}
				Rect textRect = CaptionTextRectangle();
				ThumbnailGeometry g = Geometry;
				var textArea = new Rect(g.Inner.X, g.Area.Bottom, g.Inner.Width, g.Inner.Bottom - g.Area.Bottom);
				return Snapped(new Rect(textRect.X - 2, textRect.Y - 2, textRect.Width + 4, textRect.Height + 4).Intersect(textArea));
			}
		}

		// The caption as laid out (up to three centred lines in the label rectangle); empty when there is no text.
		private Rect CaptionTextRectangle()
		{
			string text = caption.Text;
			if (string.IsNullOrEmpty(text))
			{
				return default;
			}
			ThumbnailGeometry g = Geometry;
			var layout = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(caption.FontFamily, caption.FontStyle, caption.FontWeight), caption.FontSize, caption.Foreground)
			{
				MaxTextWidth = Math.Max(1, g.Label.Width),
				MaxTextHeight = Math.Max(1, g.Label.Height),
				MaxLineCount = ThumbnailMetrics.LabelLines,
				TextAlignment = TextAlignment.Center,
			};
			double width = Math.Min(g.Label.Width, Math.Ceiling(layout.Width)), height = Math.Min(g.Label.Height, Math.Ceiling(layout.Height));
			return new Rect(g.Label.X + Math.Floor((g.Label.Width - width) / 2), g.Label.Y, width, height);
		}

		/// <summary>
		/// What the original's item answers a hit test with (<c>CoverViewItem.Contains</c> and <c>IntersectsWith</c> over the rectangle <c>ThumbIconRenderer.Draw</c>
		/// returns): the cover picture's rectangle together with the caption text as laid out, in the coordinates of the item. The border around the
		/// cover, its shadow and the free part of the caption area are not part of the item: a press there is a press on the empty area of the view.
		/// </summary>
		public Rect HitRegion
		{
			get
			{
				Rect picture = ThumbnailBounds();
				Rect text = CaptionTextRectangle();
				return text.Width > 0 && text.Height > 0 ? picture.Union(text) : picture;
			}
		}

		/// <summary>True if the point (in the coordinates of the item) is on the item as the original's hit test sees it (<see cref="HitRegion"/>, right and bottom edges excluded).</summary>
		public bool HitTest(Point point)
		{
			Rect region = HitRegion;
			return point.X >= region.X && point.X < region.Right && point.Y >= region.Y && point.Y < region.Bottom;
		}

		/// <summary>The rectangles of the item at its current size.</summary>
		public ThumbnailGeometry Geometry
		{
			get
			{
				CoverImage image = cover.Image;
				return ThumbnailLayout.Compute(new Size(Bounds.Width, Bounds.Height), coverHeight, ratingMode == ThumbnailRatingMode.StarsBelow, sameSize, image == null ? default : new Size(image.Width, image.Height));
			}
		}

		protected virtual void OnHotChanged()
		{
		}

		protected override void OnPointerEntered(Avalonia.Input.PointerEventArgs e)
		{
			base.OnPointerEntered(e);
			SetHot(true);
		}

		protected override void OnPointerExited(Avalonia.Input.PointerEventArgs e)
		{
			base.OnPointerExited(e);
			SetHot(false);
		}

		/// <summary>Sets the hot state by hand (a test, or a view that tracks the pointer itself).</summary>
		public void SetHot(bool value)
		{
			if (hot != value)
			{
				hot = value;
				Redraw();
				OnHotChanged();
			}
		}

		private void ApplyTokens()
		{
			caption.Foreground = ChromePaint.Brush(this, ThemeTokens.Names.WindowText);
			caption.FontSize = ThumbnailMetrics.LabelFontSize(coverHeight);
		}

		private void Redraw()
		{
			backdrop?.InvalidateVisual();
			overlay?.InvalidateVisual();
		}

		// ---- layout ----------------------------------------------------------------------------------------------------

		private sealed class ThumbnailPanel : Panel
		{
			private readonly CrThumbnailItem item;

			public ThumbnailPanel(CrThumbnailItem item)
			{
				this.item = item;
			}

			protected override Size MeasureOverride(Size availableSize)
			{
				double width = double.IsInfinity(availableSize.Width) ? ThumbnailMetrics.CellWidth(item.coverHeight) : availableSize.Width;
				double height = double.IsInfinity(availableSize.Height) ? ThumbnailMetrics.CellHeight(item.coverHeight) : availableSize.Height;
				ThumbnailGeometry g = ThumbnailLayout.Compute(new Size(width, height), item.coverHeight, item.ratingMode == ThumbnailRatingMode.StarsBelow, item.sameSize, default);
				item.backdrop.Measure(new Size(width, height));
				item.cover.Measure(g.Box.Size);
				item.overlay.Measure(new Size(width, height));
				item.caption.Measure(g.Label.Size);
				return new Size(width, height);
			}

			protected override Size ArrangeOverride(Size finalSize)
			{
				ThumbnailGeometry g = ThumbnailLayout.Compute(finalSize, item.coverHeight, item.ratingMode == ThumbnailRatingMode.StarsBelow, item.sameSize, default);
				var all = new Rect(finalSize);
				item.backdrop.Arrange(all);
				item.cover.Arrange(g.Box);
				item.overlay.Arrange(all);
				item.caption.Measure(g.Label.Size);
				item.caption.Arrange(g.Label);
				return finalSize;
			}
		}

		// ---- drawing ---------------------------------------------------------------------------------------------------

		private static readonly Typeface Arial = new Typeface("Arial");

		private static readonly Typeface ArialBoldItalic = new Typeface("Arial", FontStyle.Italic, FontWeight.Bold);

		private static FormattedText Text(string text, Typeface face, double size, IBrush brush)
		{
			return new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, size, brush);
		}

		/// <summary>The rectangle with its edges on whole units (the original draws on whole pixels).</summary>
		private static Rect Snapped(Rect r)
		{
			double left = Math.Round(r.Left), top = Math.Round(r.Top);
			return new Rect(left, top, Math.Round(r.Right) - left, Math.Round(r.Bottom) - top);
		}

		/// <summary>The cover as the original sees it: the picture's rectangle (the box for an empty tile), on whole units.</summary>
		private Rect ThumbnailBounds()
		{
			Rect box = Geometry.Box;
			Rect picture = cover.PictureRect;
			return picture.Width > 0 ? Snapped(new Rect(box.X + picture.X, box.Y + picture.Y, picture.Width, picture.Height)) : Snapped(Geometry.Picture);
		}

		/// <summary>Under the cover: the shadow, the empty tile, and the selection behind the caption.</summary>
		private sealed class Backdrop : Control
		{
			private readonly CrThumbnailItem item;

			public Backdrop(CrThumbnailItem item)
			{
				this.item = item;
				IsHitTestVisible = false;
			}

			public override void Render(DrawingContext context)
			{
				Rect t = item.ThumbnailBounds();
				if (t.Width <= 0 || t.Height <= 0)
				{
					return;
				}
				DrawShadow(context, t);
				CoverView view = item.cover;
				if (view.Image == null || view.PictureOpacity < 0.95)
				{
					DrawMissingThumbnail(context, t);
				}
				DrawCaptionSelection(context);
			}

			// ThumbRenderer.DrawShadow (ThumbnailShadow)
			private void DrawShadow(DrawingContext context, Rect t) => ThumbnailShadow.Draw(context, item, t);

			// ThumbRenderer.DrawMissingThumbnail: a white tile with the text in light grey Arial 6 pt, four units in from the edge.
			private void DrawMissingThumbnail(DrawingContext context, Rect t)
			{
				IBrush back = ChromePaint.ResourceBrush(item, "Cr.Thumb.MissingBackBrush");
				context.DrawRectangle(back, null, t);
				IBrush ink = ChromePaint.ResourceBrush(item, "Cr.Thumb.MissingTextBrush");
				Rect r = t.Deflate(4);
				if (ink == null || r.Width <= 0 || string.IsNullOrEmpty(item.loadingText))
				{
					return;
				}
				FormattedText text = Text(item.loadingText, Arial, 8, ink);
				text.MaxTextWidth = r.Width;
				text.MaxTextHeight = r.Height;
				text.TextAlignment = TextAlignment.Center;
				context.DrawText(text, new Point(r.X, r.Y + Math.Max(0, (r.Height - text.Height) / 2)));
			}

			// ThumbIconRenderer.Draw: the selection style behind the text, as large as the text plus 2 units, inside the caption area.
			private void DrawCaptionSelection(DrawingContext context)
			{
				Rect r = item.CaptionHighlight;
				if (r.Width > 0 && r.Height > 0)
				{
					ChromePaint.Selection(context, r, item.SelectionStyle);
				}
			}
		}

		/// <summary>Over the cover: the frame, the ribbons, the state icons, the rating, the dog-ear.</summary>
		private sealed class Overlay : Control
		{
			private readonly CrThumbnailItem item;

			public Overlay(CrThumbnailItem item)
			{
				this.item = item;
				IsHitTestVisible = false;
			}

			public override void Render(DrawingContext context)
			{
				Rect t = item.ThumbnailBounds();
				if (t.Width <= 0 || t.Height <= 0)
				{
					return;
				}
				ThumbnailBadges b = item.badges;
				ThumbnailGeometry g = item.Geometry;
				DrawDogEar(context, t);
				// graphics.DrawRectangle(Pens.Black, bounds): the frame is one unit wide and one unit outside to the right and below
				ChromePaint.Frame(context, ChromePaint.ResourceBrush(item, "Cr.Thumb.BorderBrush"), new Rect(t.X, t.Y, t.Width + 1, t.Height + 1));
				if (b.PageNumber > 0)
				{
					DrawPageNumber(context, t, b.PageNumber);
				}
				if (b.ShowRibbons && b.PageCount > 0)
				{
					DrawBookmark(context, t, b.CurrentPage / (double)b.PageCount, 0);
					DrawBookmark(context, t, b.LastPageRead / (double)b.PageCount, 1);
				}
				DrawStateIcons(context, t, b.StateIcons);
				switch (item.ratingMode)
				{
					case ThumbnailRatingMode.Tags:
						DrawTags(context, t, b);
						break;
					case ThumbnailRatingMode.StarsOverlay:
						DrawOverlayStars(context, t, b);
						break;
					default:
						DrawStarStrips(context, g.Strip, b);
						break;
				}
			}

			// ThumbRenderer.DrawPageCurlOverlay: the next page shows through a triangle in the bottom right corner, with the curl picture over it.
			private void DrawDogEar(DrawingContext context, Rect t)
			{
				CoverImage back = item.backImage;
				if (!item.dogEar || back == null || back.Image == null || !(item.selected || item.hot))
				{
					return;
				}
				double width = item.selected ? 0.9 : 0.5;
				double scale = t.Height / back.Height;
				Rect picture = new Rect(t.X, t.Y, scale * back.Width, t.Height);
				double visible = t.Width;
				if (t.Width > t.Height)
				{
					visible = Math.Floor(visible / 2);
				}
				if (picture.Width < visible)
				{
					visible = Math.Floor(picture.Width);
				}
				Rect rc = new Rect(t.Right - visible, t.Y, visible, t.Height);
				double curl = Math.Min(rc.Width, rc.Height) * width;
				picture = new Rect(rc.Right - picture.Width, picture.Y, picture.Width, picture.Height);
				var triangle = new[] { new Point(rc.Right, rc.Bottom - curl), new Point(rc.Right, rc.Bottom), new Point(rc.Right - curl, rc.Bottom) };
				context.Custom(new CoverDrawOperation(back, picture, null, 1, triangle));
				var curlRect = new Rect(rc.Right - curl, rc.Bottom - curl, curl, curl);
				ChromePaint.Image(context, Icons.Get("PageCurlShadow", ChromePaint.VariantOf(item)), curlRect);
				ChromePaint.Image(context, Icons.Get("PageCurl", ChromePaint.VariantOf(item)), curlRect);
			}

			// ThumbRenderer.DrawPageNumber: a black badge (alpha 192) with a white number, Arial 7 pt, at least 20 wide, radius 3.
			private void DrawPageNumber(DrawingContext context, Rect t, int number)
			{
				FormattedText text = Text(number.ToString(CultureInfo.CurrentCulture), Arial, 7 * 4 / 3.0, ChromePaint.ResourceBrush(item, "Cr.Thumb.BadgeTextBrush"));
				Rect inside = t.Deflate(2);
				double w = Math.Max(Math.Ceiling(text.Width), 20) + 4, h = Math.Ceiling(text.Height) + 4;
				var badge = new Rect(inside.Right - w, inside.Y, w, h);
				var pen = new Pen(ChromePaint.ResourceBrush(item, "Cr.Thumb.BadgeFrameBrush"), 1);
				context.DrawRectangle(ChromePaint.ResourceBrush(item, "Cr.Thumb.BadgeBackBrush"), pen, badge, 3, 3);
				context.DrawText(text, new Point(badge.X + (badge.Width - text.Width) / 2, badge.Y + (badge.Height - text.Height) / 2));
			}

			// ThumbRenderer.DrawBookmarkV: a flag with a notch, at most 16 x 8, sliding down the right edge with the page; a black shadow one unit
			// below and to the right of it, a black outline, a fill that fades to half of each channel.
			private void DrawBookmark(DrawingContext context, Rect t, double percent, int index)
			{
				IBrush colour = ChromePaint.ResourceBrush(item, "Cr.Thumb.Bookmark" + (index % 4) + "Brush");
				if (!(colour is ISolidColorBrush solid))
				{
					return;
				}
				double height = Math.Min(8, t.Height - 2), width = Math.Min(16, t.Width - 2);
				double top = Math.Truncate(t.Y + (t.Height - height) * percent);
				var rect = new Rect(t.Right - width, top, width, height);
				Color c1 = solid.Color, c2 = new Color(255, (byte)(c1.R / 2), (byte)(c1.G / 2), (byte)(c1.B / 2)); // half of each channel
				StreamGeometry Flag(double dx, double dy)
				{
					var g = new StreamGeometry();
					using (StreamGeometryContext s = g.Open())
					{
						s.BeginFigure(new Point(rect.Left + dx, rect.Top + dy), true);
						s.LineTo(new Point(rect.Right + dx, rect.Top + dy));
						s.LineTo(new Point(rect.Right + dx, rect.Bottom + dy));
						s.LineTo(new Point(rect.Left + dx, rect.Bottom + dy));
						s.LineTo(new Point(rect.Left + height + dx, rect.Top + height / 2 + dy));
						s.EndFigure(true);
					}
					return g;
				}
				context.DrawGeometry(ChromePaint.ResourceBrush(item, "Cr.Thumb.BookmarkShadowBrush"), null, Flag(1, 1));
				var fill = new ImmutableLinearGradientBrush(new[] { new ImmutableGradientStop(0, c1), new ImmutableGradientStop(1, c2) }, 1, null, null, GradientSpreadMethod.Pad,
					new RelativePoint(0, 0, RelativeUnit.Relative), new RelativePoint(0, 1, RelativeUnit.Relative));
				context.DrawGeometry(fill, new Pen(ChromePaint.ResourceBrush(item, "Cr.Thumb.BookmarkFrameBrush"), 1), Flag(0, 0));
			}

			// ThumbRenderer.DrawStateImage and DrawImageList: the icons in a row at the bottom left of the cover, each over the one before it by 80 %.
			private void DrawStateIcons(DrawingContext context, Rect t, IReadOnlyList<string> names)
			{
				if (names == null || names.Count == 0)
				{
					return;
				}
				double band = ThumbnailMetrics.OverlaysHeight(t.Height);
				var bounds = new Rect(t.X, t.Bottom - band, t.Width, band);
				var images = new List<(Avalonia.Media.Imaging.Bitmap Image, Size Size)>();
				foreach (string name in names)
				{
					Avalonia.Media.Imaging.Bitmap image = Icons.TryGet(name, ChromePaint.VariantOf(item));
					if (image == null)
					{
						continue;
					}
					double scale = Math.Min(bounds.Width / image.Size.Width, bounds.Height / image.Size.Height);
					images.Add((image, new Size(Math.Truncate(image.Size.Width * scale), Math.Truncate(image.Size.Height * scale))));
				}
				if (images.Count == 0)
				{
					return;
				}
				const double overlap = 0.8;
				double Step(double w) => w - Math.Truncate(overlap * w);
				double tallest = 0, total = images[images.Count - 1].Size.Width;
				for (int i = 0; i < images.Count; i++)
				{
					tallest = Math.Max(tallest, images[i].Size.Height);
					if (i < images.Count - 1)
					{
						total += Step(images[i].Size.Width);
					}
				}
				double shrink = Math.Min(1, bounds.Width / total);
				var all = new Rect(bounds.X, bounds.Y + Math.Truncate((bounds.Height - Math.Truncate(shrink * tallest)) / 2), Math.Truncate(shrink * total), Math.Truncate(shrink * tallest));
				double x = all.X;
				foreach ((Avalonia.Media.Imaging.Bitmap image, Size size) in images)
				{
					var scaled = new Size(Math.Truncate(size.Width * shrink), Math.Truncate(size.Height * shrink));
					ChromePaint.Image(context, image, new Rect(x, all.Y + Math.Truncate((all.Height - scaled.Height) / 2), scaled.Width, scaled.Height));
					x += Step(scaled.Width);
				}
			}

			// ThumbRenderer.DrawRating (Tags): the community tag (blue) in the bottom right corner, the user's (yellow) to its left.
			private void DrawTags(DrawingContext context, Rect t, ThumbnailBadges b)
			{
				double size = ThumbnailMetrics.TagHeight(t.Height);
				double right = t.Right;
				if (b.CommunityRating > 0)
				{
					DrawTag(context, new Rect(right - size, t.Bottom - size, size, size), "RatingBlue", b.CommunityRating);
					right -= size;
				}
				if (b.Rating > 0)
				{
					DrawTag(context, new Rect(right - size, t.Bottom - size, size, size), "RatingYellow", b.Rating);
				}
			}

			// RatingRenderer.DrawRatingTag: the 128 unit tag picture, and the number in Arial 12 pt bold italic, scaled to 90 % of the tag's inner width
			// and sitting on its lower edge.
			private void DrawTag(DrawingContext context, Rect bounds, string imageName, float rating)
			{
				Avalonia.Media.Imaging.Bitmap image = Icons.TryGet(imageName, ChromePaint.VariantOf(item));
				if (image == null)
				{
					return;
				}
				double scale = Math.Min(bounds.Width / image.Size.Width, bounds.Height / image.Size.Height);
				var tag = new Rect(bounds.X + Math.Truncate((bounds.Width - image.Size.Width * scale) / 2), bounds.Y + Math.Truncate((bounds.Height - image.Size.Height * scale) / 2),
					Math.Truncate(image.Size.Width * scale), Math.Truncate(image.Size.Height * scale));
				ChromePaint.Image(context, image, tag);
				FormattedText text = Text(rating.ToString("N1", CultureInfo.CurrentCulture), ArialBoldItalic, 16, ChromePaint.ResourceBrush(item, "Cr.Thumb.RatingTextBrush"));
				Rect inner = tag.Deflate(2);
				if (inner.Width <= 0 || text.Width <= 0)
				{
					return;
				}
				double fit = inner.Width / text.Width * 0.9;
				double x = inner.X + (inner.Width - text.Width * fit) / 2, y = inner.Bottom - text.Height * fit;
				using (context.PushTransform(Matrix.CreateScale(fit, fit) * Matrix.CreateTranslation(x, y)))
				{
					context.DrawText(text, new Point(0, 0));
				}
			}

			// ThumbRenderer.DrawRatingStrip (StarsOverlay): a column of five stars over the left edge, the community's first, the user's a quarter in.
			private void DrawOverlayStars(DrawingContext context, Rect t, ThumbnailBadges b)
			{
				int height = ThumbnailMetrics.OverlaysHeight(t.Height);
				int star = height / ThumbnailMetrics.StarCount;
				DrawStrip(context, "StarBlue", new Rect(t.X + 2, t.Y + 2, star, height), true, b.CommunityRating, 1, 0.25);
				DrawStrip(context, "StarYellow", new Rect(t.X + 2 + (int)(star * 0.25), t.Y + 2, star, height), true, b.Rating, 1, 0.25);
			}

			// ThumbRenderer.DrawThumbnail (StarsBelow): two strips two units apart under the cover, the community's behind, the user's in front.
			private void DrawStarStrips(DrawingContext context, Rect strip, ThumbnailBadges b)
			{
				if (strip.Width <= 0 || strip.Height <= 0)
				{
					return;
				}
				var behind = new Rect(strip.X, strip.Y + 2, strip.Width, strip.Height);
				DrawStrip(context, "StarBlue", behind, false, b.CommunityRating, 1, b.CommunityRating > 0 ? 0.25 : 0);
				DrawStrip(context, "StarYellow", strip, false, b.Rating, 1, b.CommunityRating > 0 ? 0 : 0.25);
			}

			// RatingRenderer.DrawRatingStrip: five pictures in a row (or column) scaled to fill the bounds, the rated part at alpha1, the rest at alpha2.
			private void DrawStrip(DrawingContext context, string imageName, Rect bounds, bool vertical, float rating, double alpha1, double alpha2)
			{
				Avalonia.Media.Imaging.Bitmap image = Icons.TryGet(imageName, ChromePaint.VariantOf(item));
				if (image == null || bounds.Width <= 0 || bounds.Height <= 0)
				{
					return;
				}
				int count = ThumbnailMetrics.StarCount;
				Size natural = vertical ? new Size(image.Size.Width, count * image.Size.Height) : new Size(count * image.Size.Width, image.Size.Height);
				double scale = Math.Min(bounds.Width / natural.Width, bounds.Height / natural.Height);
				var all = new Rect(bounds.X + Math.Truncate((bounds.Width - natural.Width * scale) / 2), bounds.Y + Math.Truncate((bounds.Height - natural.Height * scale) / 2), natural.Width * scale, natural.Height * scale);
				Rect rated = all, rest = all;
				double share = Math.Clamp(rating / count, 0, 1);
				if (vertical)
				{
					rated = new Rect(all.X, all.Y, all.Width, all.Height * share);
					rest = new Rect(all.X, all.Y + rated.Height, all.Width, all.Height - rated.Height);
				}
				else
				{
					rated = new Rect(all.X, all.Y, all.Width * share, all.Height);
					rest = new Rect(all.X + rated.Width, all.Y, all.Width - rated.Width, all.Height);
				}
				double stepX = vertical ? 0 : image.Size.Width * scale, stepY = vertical ? image.Size.Height * scale : 0;
				void Stars(Rect clip, double alpha)
				{
					if (clip.Width <= 0 || clip.Height <= 0 || alpha <= 0.05)
					{
						return;
					}
					using (context.PushClip(clip))
					{
						var star = new Rect(all.X, all.Y, image.Size.Width * scale, image.Size.Height * scale);
						for (int i = 0; i < count; i++)
						{
							ChromePaint.Image(context, image, new Rect(Math.Round(star.X), Math.Round(star.Y), Math.Round(star.Right) - Math.Round(star.X), Math.Round(star.Bottom) - Math.Round(star.Y)), alpha);
							star = new Rect(star.X + stepX, star.Y + stepY, star.Width, star.Height);
						}
					}
				}
				Stars(rated, alpha1);
				Stars(rest, alpha2);
			}
		}
	}

	/// <summary>
	/// <c>ThumbRenderer.DrawShadow</c>: <c>BlurShadowParts.Default</c> (right edge, bottom edge and the three corners that touch them) of a 4 unit shadow of the
	/// rectangle grown by 4; each part is a quarter or an edge of a radial fall-off from 0.33 black to nothing. The thumbnail item and the cover of the Info dialog use it.
	/// </summary>
	public static class ThumbnailShadow
	{
		public static void Draw(DrawingContext context, Control owner, Rect t)
		{
			IBrush shadow = ChromePaint.ResourceBrush(owner, "Cr.Thumb.ShadowBrush");
			if (!(shadow is ISolidColorBrush solid))
			{
				return;
			}
			double d = ThumbnailMetrics.ShadowDepth;
			Color near = solid.Color, far = new Color(0, near.R, near.G, near.B); // the same colour, fully transparent
			var stops = new[] { new ImmutableGradientStop(0, near), new ImmutableGradientStop(1, far) };
			if (t.Height > d)
			{
				context.DrawRectangle(Linear(stops, 0, 0, 1, 0), null, new Rect(t.Right, t.Y + d, d, t.Height - d));
			}
			if (t.Width > d)
			{
				context.DrawRectangle(Linear(stops, 0, 0, 0, 1), null, new Rect(t.X + d, t.Bottom, t.Width - d, d));
			}
			context.DrawRectangle(Radial(stops, 0, 1), null, new Rect(t.Right, t.Y, d, d));
			context.DrawRectangle(Radial(stops, 0, 0), null, new Rect(t.Right, t.Bottom, d, d));
			context.DrawRectangle(Radial(stops, 1, 0), null, new Rect(t.X, t.Bottom, d, d));
		}

		private static IBrush Linear(ImmutableGradientStop[] stops, double x0, double y0, double x1, double y1)
		{
			return new ImmutableLinearGradientBrush(stops, 1, null, null, GradientSpreadMethod.Pad, new RelativePoint(x0, y0, RelativeUnit.Relative), new RelativePoint(x1, y1, RelativeUnit.Relative));
		}

		private static IBrush Radial(ImmutableGradientStop[] stops, double cx, double cy)
		{
			var centre = new RelativePoint(cx, cy, RelativeUnit.Relative);
			return new ImmutableRadialGradientBrush(stops, 1, null, null, GradientSpreadMethod.Pad, centre, centre, new RelativeScalar(1, RelativeUnit.Relative), new RelativeScalar(1, RelativeUnit.Relative));
		}
	}
}
