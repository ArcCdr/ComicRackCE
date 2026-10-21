using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using ComicRack.Mac.Browser;
using ComicRack.Mac.Themes;

namespace ComicRack.Mac.Controls
{
	/// <summary>
	/// The page viewer of the Colors tab (<c>pageViewer</c>, a <c>BitmapViewer</c> with <c>ScaleMode.FitWidth</c> and <c>AutoScrollMode.Pan</c>): the page scaled to the width of the view, a
	/// vertical scroll bar when it is taller, dragging pans it, the wheel scrolls it, and the prompt text ("Double Click on Color to set White Point") lies over the bottom centre in
	/// <c>Cr.ComicBookPageViewerText</c>. A double click raises <see cref="Picked"/> with the place in the picture as fractions (0 to 1) of its width and height, so the owner can read
	/// the pixel of the page it holds (<c>BitmapViewer.GetPixel</c>). The picture is whatever the owner sets: the adjusted page.
	/// </summary>
	public sealed class CrPageViewer : Control, IChromeElement
	{
		public static readonly StyledProperty<string> TextProperty = AvaloniaProperty.Register<CrPageViewer, string>(nameof(Text), string.Empty);

		private readonly ScrollBar vertical = new ScrollBar { Orientation = Orientation.Vertical, Visibility = ScrollBarVisibility.Hidden };
		private CoverImage picture;
		private double offset;
		private bool settingBar;
		private bool barShown;
		private bool panning;
		private Point panStart;
		private double panOffset;

		static CrPageViewer()
		{
			AffectsRender<CrPageViewer>(TextProperty);
		}

		public CrPageViewer()
		{
			ClipToBounds = true;
			Focusable = true;
			vertical.PropertyChanged += (sender, e) =>
			{
				if (!settingBar && e.Property == RangeBase.ValueProperty)
				{
					SetOffset(vertical.Value);
				}
			};
			((ISetLogicalParent)vertical).SetParent(this);
			VisualChildren.Add(vertical);
			LogicalChildren.Add(vertical);
			ActualThemeVariantChanged += (sender, e) => InvalidateVisual();
			ChromeVariants.Bind(this);
		}

		public ChromeElement Element => ChromeElement.ItemView;

		/// <summary>The prompt drawn over the bottom centre of the view (<c>BitmapViewer.Text</c>).</summary>
		public string Text
		{
			get => GetValue(TextProperty);
			set => SetValue(TextProperty, value);
		}

		/// <summary>The picture shown (the viewer takes its own reference), or null.</summary>
		public CoverImage Picture => picture;

		/// <summary>Replaces the picture; the scroll position stays unless the picture is another size.</summary>
		public void SetPicture(CoverImage value, bool keepOffset = true)
		{
			CoverImage old = picture;
			picture = value?.Retain();
			old?.Release();
			if (!keepOffset || old == null || picture == null || old.Width != picture.Width || old.Height != picture.Height)
			{
				offset = 0;
			}
			InvalidateArrange();
			UpdateBar();
			InvalidateVisual();
		}

		/// <summary>The width of the picture area: the control less the scroll bar.</summary>
		public double ViewWidth => Math.Max(0, Bounds.Width - (barShown ? vertical.DesiredSize.Width : 0));

		/// <summary>The size the picture is drawn at (fit to the width).</summary>
		public Size PictureSize => ScaledSize(ViewWidth);

		private Size ScaledSize(double viewWidth)
		{
			if (picture == null || picture.Width <= 0 || viewWidth <= 0)
			{
				return default;
			}
			return new Size(viewWidth, Math.Floor(picture.Height * viewWidth / picture.Width));
		}

		/// <summary>Where the picture is drawn in the control (scrolled).</summary>
		public Rect PictureRect
		{
			get
			{
				Size size = PictureSize;
				return new Rect(0, -offset, size.Width, size.Height);
			}
		}

		public double Offset => offset;

		public ScrollBar VerticalBar => vertical;

		/// <summary>A double click on the picture: the place in it as fractions of its width and height.</summary>
		public event EventHandler<Point> Picked;

		/// <summary>The fractions (0 to 1) of the picture's width and height at a point of the control, or null outside the picture (<c>BitmapViewer.GetPixel</c>).</summary>
		public Point? PictureFractionAt(Point point)
		{
			Rect r = PictureRect;
			if (r.Width <= 0 || r.Height <= 0 || !r.Contains(point) || point.X >= ViewWidth)
			{
				return null;
			}
			return new Point((point.X - r.X) / r.Width, (point.Y - r.Y) / r.Height);
		}

		protected override Size MeasureOverride(Size availableSize)
		{
			vertical.Measure(availableSize);
			return new Size(double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width, double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height);
		}

		protected override Size ArrangeOverride(Size finalSize)
		{
			Size bar = vertical.DesiredSize;
			barShown = picture != null && ScaledSize(finalSize.Width).Height > finalSize.Height + 0.5;
			if (barShown)
			{
				// with the bar the picture is narrower and so shorter: it can fit again
				barShown = ScaledSize(Math.Max(0, finalSize.Width - bar.Width)).Height > finalSize.Height + 0.5;
			}
			vertical.Visibility = barShown ? ScrollBarVisibility.Visible : ScrollBarVisibility.Hidden;
			vertical.Arrange(new Rect(finalSize.Width - bar.Width, 0, bar.Width, finalSize.Height));
			UpdateBar(finalSize);
			return finalSize;
		}

		private void UpdateBar(Size? size = null)
		{
			Size full = size ?? Bounds.Size;
			double height = full.Height;
			double content = ScaledSize(Math.Max(0, full.Width - (barShown ? vertical.DesiredSize.Width : 0))).Height;
			settingBar = true;
			try
			{
				vertical.Minimum = 0;
				vertical.Maximum = Math.Max(0, content - height);
				vertical.ViewportSize = height;
				vertical.SmallChange = 40;
				vertical.LargeChange = Math.Max(1, height * 0.9);
				offset = Math.Clamp(offset, 0, vertical.Maximum);
				vertical.Value = offset;
			}
			finally
			{
				settingBar = false;
			}
		}

		private void SetOffset(double value)
		{
			double clamped = Math.Round(Math.Clamp(value, 0, vertical.Maximum));
			if (clamped != offset)
			{
				offset = clamped;
				UpdateBar();
				InvalidateVisual();
			}
		}

		public override void Render(DrawingContext context)
		{
			var all = new Rect(Bounds.Size);
			context.DrawRectangle(ChromePaint.Brush(this, ThemeTokens.Names.Control), null, all);
			Rect view = new Rect(0, 0, ViewWidth, Bounds.Height);
			using (context.PushClip(view))
			{
				if (picture != null)
				{
					Rect r = PictureRect;
					context.Custom(new CoverDrawOperation(picture, r));
				}
				if (!string.IsNullOrEmpty(Text))
				{
					Rect area = view.Deflate(4);
					var formatted = new FormattedText(Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(TextElement.GetFontFamily(this), FontStyle.Normal, FontWeight.Bold), ChromeFonts.CaptionSize,
						ChromePaint.Brush(this, ThemeTokens.Names.ComicBookPageViewerText))
					{
						MaxTextWidth = Math.Max(0, area.Width),
						TextAlignment = TextAlignment.Center,
					};
					context.DrawText(formatted, new Point(area.X, Math.Max(area.Y, area.Bottom - formatted.Height)));
				}
			}
		}

		// ---- pointer: drag pans, double click picks --------------------------------------------------------------------

		protected override void OnPointerPressed(PointerPressedEventArgs e)
		{
			base.OnPointerPressed(e);
			Focus();
			PointerPoint pp = e.GetCurrentPoint(this);
			if (!pp.Properties.IsLeftButtonPressed)
			{
				return;
			}
			if (e.ClickCount == 2)
			{
				Point? fraction = PictureFractionAt(pp.Position);
				if (fraction.HasValue)
				{
					Picked?.Invoke(this, fraction.Value);
				}
				e.Handled = true;
				return;
			}
			panning = true;
			panStart = pp.Position;
			panOffset = offset;
			e.Pointer.Capture(this);
		}

		protected override void OnPointerMoved(PointerEventArgs e)
		{
			base.OnPointerMoved(e);
			if (panning)
			{
				SetOffset(panOffset - (e.GetPosition(this).Y - panStart.Y));
			}
		}

		protected override void OnPointerReleased(PointerReleasedEventArgs e)
		{
			base.OnPointerReleased(e);
			if (panning)
			{
				panning = false;
				e.Pointer.Capture(null);
			}
		}

		protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
		{
			base.OnPointerWheelChanged(e);
			SetOffset(offset - e.Delta.Y * 40);
			e.Handled = true;
		}

		protected override void OnKeyDown(KeyEventArgs e)
		{
			base.OnKeyDown(e);
			switch (e.Key)
			{
				case Key.Up:
					SetOffset(offset - 40);
					break;
				case Key.Down:
					SetOffset(offset + 40);
					break;
				case Key.PageUp:
					SetOffset(offset - Bounds.Height * 0.9);
					break;
				case Key.PageDown:
					SetOffset(offset + Bounds.Height * 0.9);
					break;
				case Key.Home:
					SetOffset(0);
					break;
				case Key.End:
					SetOffset(double.MaxValue);
					break;
				default:
					return;
			}
			e.Handled = true;
		}

		/// <summary>Lets go of the picture (the owner closes).</summary>
		public void Clear() => SetPicture(null);
	}
}
