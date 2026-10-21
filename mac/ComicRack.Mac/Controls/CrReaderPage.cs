using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using ComicRack.Mac.Browser;
using ComicRack.Mac.Reader;
using ComicRack.Mac.Themes;

namespace ComicRack.Mac.Controls
{
	/// <summary>Which display control the surface stands for: the reading area (<c>MainForm.CreateComicDisplay</c>) or the page viewer of the small preview (<c>SmallComicPreview.pageViewer</c>).</summary>
	public enum ReaderPageLook
	{
		Reader,
		SmallPreview,
	}

	/// <summary>
	/// The surface of the reading area (<c>ComicDisplayControl</c>'s page drawing): the paper colour of the original (<c>Cr.ReaderBackground</c>,
	/// WhiteSmoke), the shadow around the pages and the one unit outline of each page ("realistic pages", <see cref="PageFrame"/>), and the pages
	/// themselves, one Skia <c>DrawImageRect</c> each through <see cref="ReaderDrawOperation"/>. It draws what its <see cref="Source"/> says and does nothing
	/// else: the reader view (<c>Views/ReaderView</c>) is this control plus the behaviour (pointer, wheel, gestures, cursor), so the look of the page
	/// belongs to the chrome layer. <see cref="Render"/> runs when the view calls <c>InvalidateVisual</c>; the pictures are held by the draw operation for
	/// as long as a frame needs them.
	/// </summary>
	public class CrReaderPage : Control, IChromeElement
	{
		public static readonly StyledProperty<ReaderPageLook> LookProperty = AvaloniaProperty.Register<CrReaderPage, ReaderPageLook>(nameof(Look));

		public static readonly StyledProperty<string> PlaceholderTextProperty = AvaloniaProperty.Register<CrReaderPage, string>(nameof(PlaceholderText));

		static CrReaderPage()
		{
			AffectsRender<CrReaderPage>(LookProperty, PlaceholderTextProperty);
		}

		public CrReaderPage()
		{
			ClipToBounds = true;
			ActualThemeVariantChanged += (sender, e) => InvalidateVisual();
			ChromeVariants.Bind(this);
		}

		public ChromeElement Element => ChromeElement.ReaderOverlay;

		/// <summary>
		/// <see cref="ReaderPageLook.SmallPreview"/> paints the page viewer of the small preview: its paper is <c>SmallPreviewPageViewerBack</c> (white, dark #191919) and its
		/// message is <c>SmallPreviewPageViewerText</c> (light gray, dark #303030) in the italic font of <c>pageViewer</c> (Arial 15.75 pt, 21 units).
		/// </summary>
		public ReaderPageLook Look
		{
			get => GetValue(LookProperty);
			set => SetValue(LookProperty, value);
		}

		/// <summary>The text of the display control (<c>Text</c>): drawn in the middle over the paper and the pages; empty draws nothing.</summary>
		public string PlaceholderText
		{
			get => GetValue(PlaceholderTextProperty);
			set => SetValue(PlaceholderTextProperty, value);
		}

		/// <summary>What is drawn (the reader's view model); null draws the paper only.</summary>
		public IReaderPageSource Source { get; set; }

		protected override Size MeasureOverride(Size availableSize)
		{
			return new Size(double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width, double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height);
		}

		public override void Render(Avalonia.Media.DrawingContext context)
		{
			var area = new Rect(Bounds.Size);
			bool preview = Look == ReaderPageLook.SmallPreview;
			context.DrawRectangle(ChromePaint.Brush(this, preview ? ThemeTokens.Names.SmallPreviewPageViewerBack : ThemeTokens.Names.ReaderBackground), null, area);
			DrawPages(context, area);
			DrawPlaceholder(context, area, preview);
		}

		// ComicDisplayControl.OnPaint: the Text of the control, centred, after the scene
		private void DrawPlaceholder(Avalonia.Media.DrawingContext context, Rect area, bool preview)
		{
			string text = PlaceholderText;
			if (string.IsNullOrEmpty(text) || area.Width <= 0 || area.Height <= 0)
			{
				return;
			}
			var face = new Avalonia.Media.Typeface(Avalonia.Media.FontFamily.Default, Avalonia.Media.FontStyle.Italic, Avalonia.Media.FontWeight.Normal);
			var formatted = new Avalonia.Media.FormattedText(text, CultureInfo.CurrentUICulture, Avalonia.Media.FlowDirection.LeftToRight, face, ChromeFonts.PreviewMessageSize,
				ChromePaint.Brush(this, preview ? ThemeTokens.Names.SmallPreviewPageViewerText : ThemeTokens.Names.ControlText))
			{
				MaxTextWidth = area.Width,
				TextAlignment = Avalonia.Media.TextAlignment.Center,
			};
			formatted.MaxTextHeight = area.Height;
			context.DrawText(formatted, new Point(0, Math.Max(0, (area.Height - formatted.Height) / 2)));
		}

		private void DrawPages(Avalonia.Media.DrawingContext context, Rect area)
		{
			IReaderPageSource source = Source;
			ReaderScene scene = source?.Scene;
			if (source == null || scene == null || scene.Pages.Count == 0 || area.Width <= 0 || area.Height <= 0)
			{
				return;
			}
			PageArrangement arrangement = source.ArrangeFor(area.Size);
			var images = new CoverImage[scene.Pages.Count];
			for (int i = 0; i < images.Length; i++)
			{
				images[i] = scene.Pages[i].Image;
			}
			Rect[] snapped = null;
			if (source.RealisticPages)
			{
				double scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
				snapped = new Rect[arrangement.Pages.Length];
				Rect union = default(Rect);
				for (int i = 0; i < snapped.Length; i++)
				{
					snapped[i] = ReaderPainter.SnapToDevice(arrangement.Pages[i], scaling);
					union = i == 0 ? snapped[i] : union.Union(snapped[i]);
				}
				PageFrame.DrawShadow(context, this, union);
			}
			context.Custom(new ReaderDrawOperation(images, arrangement.Pages, area, scene.Turn, source.Meter));
			if (snapped != null)
			{
				foreach (Rect page in snapped)
				{
					PageFrame.DrawOutline(context, this, page);
				}
			}
		}
	}
}
