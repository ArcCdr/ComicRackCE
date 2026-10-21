using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Transformation;
using ComicRack.Mac.Browser;
using ComicRack.Mac.Themes;

namespace ComicRack.Mac.Controls
{
	/// <summary>Where a browse request of the navigation overlay goes (<c>PageSeekOrigin</c>).</summary>
	public enum NavigationOrigin
	{
		/// <summary>The first page.</summary>
		Beginning,

		/// <summary>Relative to the current page by <see cref="NavigationBrowseEventArgs.Offset"/>.</summary>
		Current,

		/// <summary>The last page.</summary>
		End,

		/// <summary>The page <see cref="NavigationBrowseEventArgs.Offset"/> (0-based).</summary>
		Absolute,
	}

	public sealed class NavigationBrowseEventArgs : EventArgs
	{
		public NavigationBrowseEventArgs(NavigationOrigin origin, int offset)
		{
			Origin = origin;
			Offset = offset;
		}

		public NavigationOrigin Origin { get; }

		public int Offset { get; }
	}

	/// <summary>
	/// Where the overlay gets the pictures of the page strip. <see cref="Get"/> gives a retained picture of a page at about the height in device
	/// pixels (the caller releases it) or null while it is not there yet, and starts making it; <see cref="Ready"/> says a page has its picture (the overlay redraws).
	/// </summary>
	public interface INavigationThumbnailSource
	{
		CoverImage Get(int page, int height);

		event EventHandler<int> Ready;
	}

	/// <summary>The rectangles of the navigation overlay for a size, as <c>NavigationOverlay</c> and its child panels place them (units at 96 DPI).</summary>
	public sealed class NavigationLayout
	{
		/// <summary><c>NavigationOverlay.ButtonSize</c>.</summary>
		public const double ButtonSize = 28;

		/// <summary><c>PagePadding</c>: the gap between two page pictures.</summary>
		public const double PagePadding = 6;

		/// <summary><c>w4</c>: the gap between buttons.</summary>
		public const double Gap = 4;

		/// <summary>The height of the name and time labels.</summary>
		public const double LabelHeight = 15;

		/// <summary>The glass picture drawn into the whole overlay (opacity 0.7).</summary>
		public Rect Panel { get; private set; }

		/// <summary>The content area of the glass (the parent rectangle the child panels align in).</summary>
		public Rect Display { get; private set; }

		public Rect First { get; private set; }

		public Rect Previous { get; private set; }

		public Rect Next { get; private set; }

		public Rect Last { get; private set; }

		/// <summary>The page slider, the width of the overlay less the four buttons and the gaps, <c>ButtonSize - 4</c> high, 2 above the bottom.</summary>
		public Rect Slider { get; private set; }

		public Rect NameLabel { get; private set; }

		public Rect TimeLabel { get; private set; }

		/// <summary>Where <c>GradientFramedBackground</c> is drawn.</summary>
		public Rect StripFrame { get; private set; }

		/// <summary>The clip of the page pictures: the frame less its 3 unit top and bottom, then 4 on each side and 2 at the top.</summary>
		public Rect Strip { get; private set; }

		public static NavigationLayout Compute(Size size)
		{
			var layout = new NavigationLayout { Panel = new Rect(0, 0, size.Width, size.Height) };
			Rect d = GlassPainter.Inner(layout.Panel);
			layout.Display = d;
			double b = ButtonSize;
			double bottom = d.Bottom - b;
			layout.First = new Rect(d.Left, bottom, b, b);
			layout.Previous = new Rect(d.Left + b + Gap, bottom, b, b);
			layout.Next = new Rect(d.Right - b - b - Gap, bottom, b, b);
			layout.Last = new Rect(d.Right - b, bottom, b, b);
			double sliderWidth = Math.Max(0, size.Width - 15 - 4 * Gap - 2 * Gap - 4 * b);
			double sliderHeight = b - Gap;
			layout.Slider = new Rect(d.Left + Math.Floor((d.Width - sliderWidth) / 2), d.Bottom - sliderHeight - 2, sliderWidth, sliderHeight);
			layout.NameLabel = new Rect(d.Left, d.Top - 1, Math.Max(0, size.Width - 80), LabelHeight);
			layout.TimeLabel = new Rect(d.Right - 50, d.Top - 1, 50, LabelHeight);
			double frameTop = d.Top + LabelHeight;
			double frameHeight = Math.Max(0, d.Height - LabelHeight - (b + 4));
			layout.StripFrame = new Rect(d.Left, frameTop, d.Width, frameHeight);
			layout.Strip = new Rect(d.Left + 4, frameTop + 3 + 2, Math.Max(0, d.Width - 8), Math.Max(0, frameHeight - 6 - 2));
			return layout;
		}
	}

	/// <summary>
	/// The navigation overlay (<c>NavigationOverlay</c>, TB/RK.21): a glass panel at the bottom of the reader (or at the top, <c>NavigationOverlayOnTop</c>)
	/// that slides in when the pointer comes near the edge (<c>ShowNavigationOverlay</c>, the auto-show option) or when it is toggled. It has the
	/// first, previous, next and last page buttons on gray glass, a page slider on the black glass scroll bar, the book name at the top left, the
	/// time at the top right and the strip of page pictures, the displayed page in full strength and the others at 0.3. The panel is
	/// 0.9 of the reader wide (at least 200) and 200 high (at least 120).
	/// </summary>
	public class CrNavigationOverlay : CrReaderOverlay
	{
		/// <summary><c>NavigationOverlayDefaultHeight</c>.</summary>
		public const double DefaultHeight = 200;

		/// <summary><c>EngineConfiguration.NavigationPanelWidth</c>.</summary>
		public const double WidthFraction = 0.9;

		/// <summary>The part of the reader edge that wakes the overlay (<c>new Size(500, 50)</c>).</summary>
		public static readonly Size HotZone = new Size(500, 50);

		/// <summary>The slide in takes 300 ms after a 500 ms delay, the slide out 200 ms (<c>MoveAnimator</c>).</summary>
		public const double SlideInMilliseconds = 300, SlideInDelayMilliseconds = 500, SlideOutMilliseconds = 200;

		private readonly List<(Rect Bounds, int Page)> pageAreas = new List<(Rect, int)>();
		private INavigationThumbnailSource thumbnails;
		private int pageCount;
		private int displayed;
		private bool isDoublePage;
		private bool mirror;
		private string caption = string.Empty;
		private int hoverPage = -1;
		private Button hotButton = Button.None;
		private Button pressedButton = Button.None;
		private bool sliderDrag;
		private int sliderValue;
		private bool thumbnailScroll;
		private Point downPoint;
		private bool havePointer;
		private int downPage = -1;
		private bool autoShow = true;
		private bool onTop;
		private bool forced;
		private bool shown;
		private bool hasContent;
		private double slide = 1; // 0 = shown, 1 = hidden
		private double slideFrom = 1, slideTo = 1, slideElapsed, slideDelay, slideSpan;
		private bool sliding;
		private Func<DateTime> clock = () => DateTime.Now;
		private readonly SlideAnimation slideAnimation;

		public CrNavigationOverlay()
		{
			slideAnimation = new SlideAnimation(this);
			IsHitTestVisible = false;
			ApplySlide();
		}

		private enum Button
		{
			None,
			First,
			Previous,
			Next,
			Last,
		}

		/// <summary>The user asked to browse (a button, the slider or a page picture).</summary>
		public event EventHandler<NavigationBrowseEventArgs> Browse;

		/// <summary>The source of the page pictures (null shows the page numbers only).</summary>
		public INavigationThumbnailSource Thumbnails
		{
			get => thumbnails;
			set
			{
				if (thumbnails != null)
				{
					thumbnails.Ready -= OnThumbnailReady;
				}
				thumbnails = value;
				if (thumbnails != null)
				{
					thumbnails.Ready += OnThumbnailReady;
				}
				InvalidateVisual();
			}
		}

		private void OnThumbnailReady(object sender, int page) => InvalidateVisual();

		/// <summary>The number of pages of the book (the slider runs from 0 to this less one).</summary>
		public int PageCount
		{
			get => pageCount;
			set
			{
				pageCount = Math.Max(0, value);
				InvalidateVisual();
			}
		}

		/// <summary>The page that is displayed (the first of two; <c>DisplayedPageIndex</c>). A slider drag in progress keeps its own value.</summary>
		public int DisplayedPage
		{
			get => displayed;
			set
			{
				displayed = value;
				if (!sliderDrag)
				{
					sliderValue = Math.Max(0, Math.Min(value, Math.Max(0, pageCount - 1)));
				}
				InvalidateVisual();
			}
		}

		/// <summary>True while two pages are displayed together (they are both drawn in full strength).</summary>
		public bool IsDoublePage
		{
			get => isDoublePage;
			set
			{
				isDoublePage = value;
				InvalidateVisual();
			}
		}

		/// <summary>True for right to left reading: the slider and the strip run the other way.</summary>
		public bool Mirror
		{
			get => mirror;
			set
			{
				mirror = value;
				InvalidateVisual();
			}
		}

		/// <summary>The name of the book at the top left.</summary>
		public string Caption
		{
			get => caption;
			set
			{
				caption = value ?? string.Empty;
				InvalidateVisual();
			}
		}

		/// <summary>The time at the top right comes from this clock (a test fixes it).</summary>
		public Func<DateTime> Clock
		{
			get => clock;
			set => clock = value ?? (() => DateTime.Now);
		}

		/// <summary><c>ShowNavigationOverlay</c>: the overlay slides in by itself when the pointer comes to the edge.</summary>
		public bool AutoShow
		{
			get => autoShow;
			set => autoShow = value;
		}

		/// <summary><c>NavigationOverlayOnTop</c>: the overlay sits at the top edge instead of the bottom edge.</summary>
		public bool OnTop
		{
			get => onTop;
			set
			{
				onTop = value;
				InvalidateMeasure();
				ApplySlide();
			}
		}

		/// <summary><c>NavigationOverlayVisible</c>: shown whatever the pointer does (the toggle).</summary>
		public bool ForcedVisible
		{
			get => forced;
			set
			{
				forced = value;
			}
		}

		/// <summary>True when there is a page to navigate (the original also needs a valid image).</summary>
		public bool HasContent
		{
			get => hasContent;
			set => hasContent = value;
		}

		/// <summary>True while the overlay is shown or sliding in.</summary>
		public bool IsShown => shown;

		/// <summary>0 when the overlay is completely in, 1 when it is completely out.</summary>
		public double SlidePosition => slide;

		/// <summary>The layout for the current size.</summary>
		public NavigationLayout Layout => NavigationLayout.Compute(Bounds.Size);

		/// <summary>The page pictures drawn by the last render and the page each one stands for.</summary>
		public IReadOnlyList<(Rect Bounds, int Page)> PageAreas => pageAreas;

		/// <summary>The page the pointer is over in the strip, or -1.</summary>
		public int HoverPage => hoverPage;

		/// <summary>The page the slider is on while it is dragged, else the displayed page.</summary>
		public int SliderValue => sliderValue;

		/// <summary>The size for a reader area: 0.9 of its width (at least 200) by 200 (at least 120).</summary>
		public static Size SizeFor(Size area)
		{
			return new Size(Math.Max(200, Math.Floor(area.Width * WidthFraction)), Math.Max(DefaultHeight, 120));
		}

		protected override Size MeasureOverride(Size availableSize)
		{
			double width = double.IsInfinity(availableSize.Width) ? 500 : availableSize.Width;
			return SizeFor(new Size(width, DefaultHeight));
		}

		protected override Size ContentSize() => SizeFor(new Size(500 / WidthFraction, DefaultHeight));

		// ---- showing and hiding ----------------------------------------------------------------------------------------

		/// <summary>
		/// <c>ComicDisplayControl.UpdateNavigationOverlay(pt, ...)</c>: decides whether the overlay is in. <paramref name="pointer"/> is the pointer in the
		/// coordinates of the reader area (null when it left), <paramref name="areaSize"/> the size of that area, <paramref name="buttonDown"/> whether a
		/// mouse button is down. It is in when there is content and either it was toggled or the auto-show option is on and the pointer is over the
		/// overlay or (with no button down) inside the hot zone at the edge.
		/// </summary>
		public void UpdatePointer(Point? pointer, Size areaSize, bool buttonDown)
		{
			bool over = IsPointerOver || (shown && pointer.HasValue && ContainsInArea(pointer.Value, areaSize));
			bool hot = pointer.HasValue && !buttonDown && HotRect(areaSize).Contains(pointer.Value);
			SetShown(hasContent && (forced || (autoShow && (over || hot))));
		}

		/// <summary>The hot zone at the edge of an area of the given size (500 x 50 in the middle of the bottom or top edge).</summary>
		public Rect HotRect(Size areaSize)
		{
			double x = Math.Floor((areaSize.Width - HotZone.Width) / 2);
			return onTop ? new Rect(x, 0, HotZone.Width, HotZone.Height) : new Rect(x, areaSize.Height - HotZone.Height, HotZone.Width, HotZone.Height);
		}

		private bool ContainsInArea(Point p, Size areaSize)
		{
			Size size = SizeFor(areaSize);
			double x = Math.Floor((areaSize.Width - size.Width) / 2);
			double y = onTop ? 0 : areaSize.Height - size.Height;
			return new Rect(x, y, size.Width, size.Height).Contains(p);
		}

		/// <summary>Slides the overlay in or out (does nothing when it is already going there).</summary>
		public void SetShown(bool visible)
		{
			if (visible == shown && (visible || slide >= 1 || sliding))
			{
				return;
			}
			if (!visible && slide >= 1 && !sliding)
			{
				return;
			}
			shown = visible;
			slideFrom = slide;
			slideTo = visible ? 0 : 1;
			slideElapsed = 0;
			slideDelay = visible ? SlideInDelayMilliseconds : 0;
			slideSpan = visible ? SlideInMilliseconds : SlideOutMilliseconds;
			sliding = true;
			IsHitTestVisible = visible;
			ReaderOverlayClock.Start(slideAnimation);
		}

		/// <summary>Puts the overlay in or out at once, without the slide (a test, or a reader that opens with it shown).</summary>
		public void SnapShown(bool visible)
		{
			if (sliding)
			{
				sliding = false;
				ReaderOverlayClock.Stop(slideAnimation);
			}
			shown = visible;
			slide = visible ? 0 : 1;
			slideFrom = slideTo = slide;
			IsHitTestVisible = visible;
			ApplySlide();
		}

		private static double SinusRise(double time, double span)
		{
			double t = span <= 0 ? 1 : time / span;
			return t <= 0 || t >= 1 ? Math.Clamp(t, 0, 1) : Math.Sin(t * Math.PI / 2);
		}

		/// <summary>Moves the slide on (the clock calls this; a test passes the time it wants through <see cref="ReaderOverlayClock.Advance"/>).</summary>
		internal bool AdvanceSlide(double milliseconds)
		{
			if (!sliding)
			{
				return false;
			}
			slideElapsed += milliseconds;
			double t = SinusRise(Math.Max(0, slideElapsed - slideDelay), slideSpan);
			slide = slideFrom + (slideTo - slideFrom) * t;
			ApplySlide();
			if (slideElapsed - slideDelay >= slideSpan)
			{
				slide = slideTo;
				sliding = false;
				ApplySlide();
				return false;
			}
			return true;
		}

		private void ApplySlide()
		{
			// out of the area: down by its own height (and the margin below it), or up when it sits at the top
			double travel = Math.Max(Bounds.Height, DefaultHeight) + 8;
			double y = slide * travel * (onTop ? -1 : 1);
			RenderTransform = TransformOperations.Parse("translate(0px, " + y.ToString("0.###", CultureInfo.InvariantCulture) + "px)");
			Opacity = slide >= 1 ? 0 : 1;
		}

		/// <summary>The fade of the base class is not used; the slide is.</summary>
		protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
		{
			base.OnAttachedToVisualTree(e);
			ApplySlide();
		}

		protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
		{
			base.OnDetachedFromVisualTree(e);
			if (sliding)
			{
				sliding = false;
				ReaderOverlayClock.Stop(slideAnimation);
			}
		}

		// The base class's Advance is private to its interface implementation; the slide is driven by a small adapter.
		private sealed class SlideAnimation : IOverlayAnimation
		{
			private readonly CrNavigationOverlay overlay;

			public SlideAnimation(CrNavigationOverlay overlay) => this.overlay = overlay;

			public bool Advance(double milliseconds) => overlay.AdvanceSlide(milliseconds);
		}

		// ---- drawing ---------------------------------------------------------------------------------------------------

		public override void Render(DrawingContext context)
		{
			Size size = Bounds.Size;
			if (size.Width <= 0 || size.Height <= 0)
			{
				return;
			}
			NavigationLayout layout = NavigationLayout.Compute(size);
			GlassPainter.Panel(context, this, layout.Panel, 0.7);
			DrawLabels(context, layout);
			DrawStrip(context, layout);
			DrawButton(context, layout.First, Button.First, "ComicRack.Engine.Display.Forms:GoFirst");
			DrawButton(context, layout.Previous, Button.Previous, "ComicRack.Engine.Display.Forms:GoPrevious");
			DrawButton(context, layout.Next, Button.Next, "ComicRack.Engine.Display.Forms:GoNext");
			DrawButton(context, layout.Last, Button.Last, "ComicRack.Engine.Display.Forms:GoLast");
			DrawSlider(context, layout.Slider);
		}

		private void DrawLabels(DrawingContext context, NavigationLayout layout)
		{
			IBrush ink = ChromePaint.ResourceBrush(this, "Cr.Reader.OverlayTextBrush");
			if (ink == null)
			{
				return;
			}
			double fontSize = ChromeFonts.ItemSize; // LabelPanel.TextSize 9 pt
			if (layout.NameLabel.Width > 0 && !string.IsNullOrEmpty(caption))
			{
				FormattedText name = Text(caption, fontSize, ink);
				name.MaxTextWidth = layout.NameLabel.Width;
				name.MaxLineCount = 1;
				name.Trimming = TextTrimming.CharacterEllipsis;
				context.DrawText(name, new Point(layout.NameLabel.X, layout.NameLabel.Y + Math.Max(0, (layout.NameLabel.Height - name.Height) / 2)));
			}
			FormattedText time = Text(clock().ToString("t", CultureInfo.CurrentCulture), fontSize, ink);
			context.DrawText(time, new Point(layout.TimeLabel.Right - Math.Ceiling(time.Width), layout.TimeLabel.Y + Math.Max(0, (layout.TimeLabel.Height - time.Height) / 2)));
		}

		private static FormattedText Text(string text, double size, IBrush brush, string family = null, FontWeight weight = FontWeight.Normal)
		{
			return new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(family == null ? FontFamily.Default : new FontFamily(family), FontStyle.Normal, weight), size, brush);
		}

		private void DrawButton(DrawingContext context, Rect rect, Button which, string icon)
		{
			Bitmap back = Icons.TryGet("GrayGlassButton", ChromePaint.VariantOf(this));
			bool hot = hotButton == which || pressedButton == which;
			if (back != null)
			{
				double opacity = hot ? 1 : 0.9;
				using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.HighQuality }))
				using (context.PushOpacity(opacity))
				{
					context.DrawImage(back, new Rect(back.Size), rect);
				}
				if (hot)
				{
					// SimpleButtonPanel: a selected button is brightened by 0.4
					using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.HighQuality, BitmapBlendingMode = BitmapBlendingMode.Plus }))
					using (context.PushOpacity(0.4))
					{
						context.DrawImage(back, new Rect(back.Size), rect);
					}
				}
			}
			Bitmap picture = Icons.TryGet(icon, ChromePaint.VariantOf(this));
			if (picture != null)
			{
				Rect area = new Rect(rect.X + 4, rect.Y + 4, rect.Width - 8, rect.Height - 8);
				Rect target = ChromePaint.Centered(picture.Size, area);
				GlassPainter.Silhouette(context, picture, target, ChromePaint.ResourceBrush(this, "Cr.Reader.GlassIconBrush"));
			}
		}

		private int SliderMaximum => Math.Max(0, pageCount - 1);

		private double SliderBorder(Rect rect) => Math.Floor(rect.Height / 2) - 3;

		private void DrawSlider(DrawingContext context, Rect rect)
		{
			if (rect.Width <= 0 || rect.Height <= 0)
			{
				return;
			}
			double border = SliderBorder(rect);
			Bitmap track = Icons.TryGet("BlackGlassScrollbarBack", ChromePaint.VariantOf(this));
			if (track != null)
			{
				var inside = new Rect(rect.X + border, rect.Y + border, Math.Max(0, rect.Width - 2 * border), Math.Max(0, rect.Height - 2 * border));
				GlassPainter.NineSlice(context, track, inside, new Rect(track.Size), new Thickness(3));
			}
			int max = SliderMaximum;
			if (max <= 0)
			{
				return;
			}
			Bitmap knob = Icons.TryGet("GrayGlassButton", ChromePaint.VariantOf(this));
			if (knob != null)
			{
				double height = rect.Height - 1;
				double width = height; // the knob picture is resized to 32 x 32
				double x = sliderValue * (rect.Width - width) / max;
				x = mirror ? rect.Right - x - width : rect.Left + x;
				using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.HighQuality }))
				{
					context.DrawImage(knob, new Rect(knob.Size), new Rect(Math.Round(x), rect.Top, width, height));
				}
			}
		}

		/// <summary>The size of the picture of a page in the strip: its own proportions at the strip height, or 3:4 while it is not there.</summary>
		private Size PageSize(int page, double height, CoverImage picture)
		{
			if (page < 0 || page >= pageCount)
			{
				return default(Size);
			}
			if (picture == null || picture.Height <= 0)
			{
				return new Size(Math.Floor(height * 3 / 4), height);
			}
			return new Size(Math.Max(1, Math.Round(picture.Width * height / picture.Height)), height);
		}

		private void DrawStrip(DrawingContext context, NavigationLayout layout)
		{
			pageAreas.Clear();
			Bitmap frame = Icons.TryGet("GradientFramedBackground", ChromePaint.VariantOf(this));
			if (frame != null && layout.StripFrame.Width > 0)
			{
				GlassPainter.NineSlice(context, frame, layout.StripFrame, new Rect(frame.Size), new Thickness(0, 3, 0, 3));
			}
			Rect strip = layout.Strip;
			if (strip.Width <= 0 || strip.Height <= 0 || pageCount <= 0)
			{
				return;
			}
			int height = (int)Math.Round(strip.Height);
			// the pictures are asked for in device pixels (a Retina screen shows twice as many), the layout stays in units
			double scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
			int pixels = Math.Max(1, (int)Math.Round(strip.Height * scaling));
			// the strip is centred on the slider (NavigationOverlay.DisplayedPageIndex is the slider's value): while the slider is dragged the pages run with it
			int centrePage = sliderValue;
			double direction = mirror ? -1 : 1;
			double centre = Math.Floor(strip.X + strip.Width / 2);
			double left = centre, right = centre;
			int leftPage = centrePage, rightPage = centrePage + 1;
			bool selected = true, more = false;
			using (context.PushClip(strip))
			{
				if (isDoublePage)
				{
					more = true;
				}
				else
				{
					using (CoverImageLease picture = Hold(centrePage, pixels))
					{
						Size size = PageSize(centrePage, height, picture.Value);
						var rect = new Rect(Math.Floor(centre - size.Width / 2), strip.Y, size.Width, size.Height);
						if (strip.Intersects(rect))
						{
							DrawPage(context, rect, centrePage, true, picture.Value);
							pageAreas.Add((rect, centrePage));
							more = true;
						}
						left -= (Math.Floor(size.Width / 2) + NavigationLayout.PagePadding) * direction;
						right += (Math.Floor(size.Width / 2) + NavigationLayout.PagePadding) * direction;
						leftPage--;
						selected = false;
					}
				}
				for (int guard = 0; more && guard < 400; guard++)
				{
					more = false;
					using (CoverImageLease picture = Hold(leftPage, pixels))
					{
						Size size = PageSize(leftPage, height, picture.Value);
						if (size.Width > 0)
						{
							double x = mirror ? left : left - size.Width; // Far: the left edge at the point; Near: the right edge
							var rect = new Rect(x, strip.Y, size.Width, size.Height);
							if (strip.Intersects(rect))
							{
								DrawPage(context, rect, leftPage, selected || leftPage == hoverPage, picture.Value);
								pageAreas.Add((rect, leftPage));
								left -= (size.Width + NavigationLayout.PagePadding) * direction;
								leftPage--;
								more = true;
							}
						}
					}
					using (CoverImageLease picture = Hold(rightPage, pixels))
					{
						Size size = PageSize(rightPage, height, picture.Value);
						if (size.Width > 0)
						{
							double x = mirror ? right - size.Width : right;
							var rect = new Rect(x, strip.Y, size.Width, size.Height);
							if (strip.Intersects(rect))
							{
								DrawPage(context, rect, rightPage, selected || rightPage == hoverPage, picture.Value);
								pageAreas.Add((rect, rightPage));
								right += (size.Width + NavigationLayout.PagePadding) * direction;
								rightPage++;
								more = true;
							}
						}
					}
					selected = false;
				}
			}
		}

		/// <summary>A held picture of a page for a drawing pass (disposing the lease releases it); <paramref name="pixels"/> is the height in device pixels.</summary>
		private CoverImageLease Hold(int page, int pixels)
		{
			CoverImage picture = thumbnails == null || page < 0 || page >= pageCount ? null : thumbnails.Get(page, pixels);
			return new CoverImageLease(picture);
		}

		private sealed class CoverImageLease : IDisposable
		{
			public CoverImageLease(CoverImage value) => Value = value;

			public CoverImage Value { get; }

			public void Dispose() => Value?.Release();
		}

		private void DrawPage(DrawingContext context, Rect rect, int page, bool strong, CoverImage picture)
		{
			double opacity = strong ? 1 : 0.3;
			if (picture == null || picture.Image == null)
			{
				IBrush ink = ChromePaint.ResourceBrush(this, "Cr.Reader.OverlayTextBrush");
				if (ink != null)
				{
					// NavigationOverlay.DrawPage: the page number in Times 32 pt, in the text colour of the glass at the strength of the page
					FormattedText number = Text((page + 1).ToString(CultureInfo.CurrentCulture), 32 * 4 / 3.0, ink, "Times");
					using (context.PushOpacity(opacity))
					{
						context.DrawText(number, new Point(rect.X + (rect.Width - number.Width) / 2, rect.Y + (rect.Height - number.Height) / 2));
					}
				}
				return;
			}
			context.Custom(new CoverDrawOperation(picture, rect, null, opacity));
			using (opacity < 1 ? context.PushOpacity(opacity) : default(DrawingContext.PushedState))
			{
				// ThumbRenderer without the shadow: the one unit frame and the page number badge
				ChromePaint.Frame(context, ChromePaint.ResourceBrush(this, "Cr.Thumb.BorderBrush"), new Rect(rect.X, rect.Y, rect.Width + 1, rect.Height + 1));
				FormattedText text = Text((page + 1).ToString(CultureInfo.CurrentCulture), 7 * 4 / 3.0, ChromePaint.ResourceBrush(this, "Cr.Thumb.BadgeTextBrush"), "Arial");
				Rect inside = rect.Deflate(2);
				double w = Math.Max(Math.Ceiling(text.Width), 20) + 4, h = Math.Ceiling(text.Height) + 4;
				var badge = new Rect(inside.Right - w, inside.Y, w, h);
				context.DrawRectangle(ChromePaint.ResourceBrush(this, "Cr.Thumb.BadgeBackBrush"), new Pen(ChromePaint.ResourceBrush(this, "Cr.Thumb.BadgeFrameBrush"), 1), badge, 3, 3);
				context.DrawText(text, new Point(badge.X + (badge.Width - text.Width) / 2, badge.Y + (badge.Height - text.Height) / 2));
			}
		}

		// ---- pointer ---------------------------------------------------------------------------------------------------

		private Button ButtonAt(NavigationLayout layout, Point p)
		{
			if (layout.First.Contains(p))
			{
				return Button.First;
			}
			if (layout.Previous.Contains(p))
			{
				return Button.Previous;
			}
			if (layout.Next.Contains(p))
			{
				return Button.Next;
			}
			return layout.Last.Contains(p) ? Button.Last : Button.None;
		}

		/// <summary>The page whose picture is at the point, or -1 (<c>PageHitTest</c>).</summary>
		public int PageAt(Point p)
		{
			foreach ((Rect bounds, int page) area in pageAreas)
			{
				if (area.bounds.Contains(p))
				{
					return area.page;
				}
			}
			return -1;
		}

		private int SliderValueAt(Rect slider, Point p)
		{
			double border = SliderBorder(slider);
			int max = SliderMaximum;
			if (max <= 0 || slider.Width - 2 * border <= 0)
			{
				return 0;
			}
			int value = (int)Math.Clamp(Math.Floor((p.X - slider.X - border) * (max + 1) / (slider.Width - 2 * border)), 0, max);
			return mirror ? max - value : value;
		}

		protected override void OnPointerMoved(PointerEventArgs e)
		{
			base.OnPointerMoved(e);
			Point p = e.GetPosition(this);
			havePointer = true;
			NavigationLayout layout = NavigationLayout.Compute(Bounds.Size);
			if (sliderDrag)
			{
				int value = SliderValueAt(layout.Slider, p);
				if (value != sliderValue)
				{
					sliderValue = value;
					InvalidateVisual();
				}
				return;
			}
			Button button = ButtonAt(layout, p);
			if (button != hotButton)
			{
				hotButton = button;
				InvalidateVisual();
			}
			if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
			{
				SetHoverPage(PageAt(p));
				return;
			}
			if (downPage == -1)
			{
				downPage = PageAt(p);
			}
			if (downPage == -1)
			{
				return;
			}
			SetHoverPage(downPage);
			(Rect bounds, int page) downArea = pageAreas.Find(a => a.Page == downPage);
			if (downArea.bounds.Width == 0 || p.Y < downArea.bounds.Top || p.Y > downArea.bounds.Bottom)
			{
				return;
			}
			int step = 0;
			if (p.X > downArea.bounds.Right + NavigationLayout.PagePadding)
			{
				step = -1;
			}
			else if (p.X < downArea.bounds.Left - NavigationLayout.PagePadding)
			{
				step = 1;
			}
			if (step != 0)
			{
				thumbnailScroll = true;
				downPage = -1;
				sliderValue = Math.Clamp(sliderValue + (mirror ? -step : step), 0, SliderMaximum);
				InvalidateVisual();
			}
		}

		private void SetHoverPage(int page)
		{
			if (page != hoverPage)
			{
				hoverPage = page;
				InvalidateVisual();
			}
		}

		protected override void OnPointerExited(PointerEventArgs e)
		{
			base.OnPointerExited(e);
			havePointer = false;
			hotButton = Button.None;
			SetHoverPage(-1);
			InvalidateVisual();
		}

		protected override void OnPointerPressed(PointerPressedEventArgs e)
		{
			base.OnPointerPressed(e);
			if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
			{
				return;
			}
			Point p = e.GetPosition(this);
			NavigationLayout layout = NavigationLayout.Compute(Bounds.Size);
			downPoint = p;
			downPage = -1;
			thumbnailScroll = false;
			e.Handled = true;
			Button button = ButtonAt(layout, p);
			if (button != Button.None)
			{
				pressedButton = button;
				e.Pointer.Capture(this);
				InvalidateVisual();
				return;
			}
			if (layout.Slider.Contains(p) && SliderMaximum > 0)
			{
				sliderDrag = true;
				sliderValue = SliderValueAt(layout.Slider, p);
				e.Pointer.Capture(this);
				InvalidateVisual();
			}
		}

		protected override void OnPointerReleased(PointerReleasedEventArgs e)
		{
			base.OnPointerReleased(e);
			Point p = e.GetPosition(this);
			NavigationLayout layout = NavigationLayout.Compute(Bounds.Size);
			if (ReferenceEquals(e.Pointer.Captured, this))
			{
				e.Pointer.Capture(null);
			}
			if (pressedButton != Button.None)
			{
				Button was = pressedButton;
				pressedButton = Button.None;
				InvalidateVisual();
				if (ButtonAt(layout, p) == was)
				{
					switch (was)
					{
						case Button.First:
							Browse?.Invoke(this, new NavigationBrowseEventArgs(NavigationOrigin.Beginning, 0));
							break;
						case Button.Previous:
							Browse?.Invoke(this, new NavigationBrowseEventArgs(NavigationOrigin.Current, -1));
							break;
						case Button.Next:
							Browse?.Invoke(this, new NavigationBrowseEventArgs(NavigationOrigin.Current, 1));
							break;
						case Button.Last:
							Browse?.Invoke(this, new NavigationBrowseEventArgs(NavigationOrigin.End, 0));
							break;
					}
				}
				e.Handled = true;
				return;
			}
			if (sliderDrag)
			{
				sliderDrag = false;
				Browse?.Invoke(this, new NavigationBrowseEventArgs(NavigationOrigin.Absolute, sliderValue));
				InvalidateVisual();
				e.Handled = true;
				return;
			}
			int page = thumbnailScroll ? sliderValue : PageAt(p);
			downPoint = default(Point);
			downPage = -1;
			if (page >= 0)
			{
				Browse?.Invoke(this, new NavigationBrowseEventArgs(NavigationOrigin.Absolute, page));
			}
			thumbnailScroll = false;
			e.Handled = true;
		}

		protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
		{
			base.OnPointerWheelChanged(e);
			e.Handled = true; // the wheel over the panel does not scroll the page behind it
		}
	}
}
