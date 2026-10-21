using System;
using System.Collections.Concurrent;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using Avalonia.VisualTree;
using ComicRack.Mac.Themes;

namespace ComicRack.Mac.Controls
{
	/// <summary>
	/// What the custom-drawn controls share: the brushes of the <c>Cr.*</c> tokens for the appearance of a control (cached per variant),
	/// the vertical gradients of the toolstrip colour table, crisp one pixel lines (filled rectangles, so they never blend at any display
	/// scale) and the image drawing of the original's 16 pixel pictures.
	/// </summary>
	public static class ChromePaint
	{
		private static readonly ConcurrentDictionary<(ThemeVariant, string), IBrush> brushes = new ConcurrentDictionary<(ThemeVariant, string), IBrush>();

		/// <summary>The concrete appearance of a control: light or dark, never the default.</summary>
		public static ThemeVariant VariantOf(Control control)
		{
			ThemeVariant variant = control?.ActualThemeVariant;
			return variant == ThemeVariant.Dark ? ThemeVariant.Dark : ThemeVariant.Light;
		}

		public static Color Color(Control control, string token) => ThemeTokens.Get(token, VariantOf(control));

		private static readonly ConcurrentDictionary<(ThemeVariant, string), IBrush> resourceBrushes = new ConcurrentDictionary<(ThemeVariant, string), IBrush>();

		/// <summary>
		/// A brush that the theme files define under <paramref name="key"/> (the <c>Cr.Mvp.*</c> entries of <c>CrMvpLook.axaml</c>), for the appearance
		/// of the control, cached per appearance. For custom-drawn views, which cannot use a style class; null when the key is not defined.
		/// </summary>
		public static IBrush ResourceBrush(Control control, string key)
		{
			ThemeVariant variant = VariantOf(control);
			if (resourceBrushes.TryGetValue((variant, key), out IBrush cached))
			{
				return cached;
			}
			if (Application.Current != null && Application.Current.TryGetResource(key, variant, out object found) && found is IBrush brush)
			{
				IBrush frozen = brush.ToImmutable();
				resourceBrushes[(variant, key)] = frozen;
				return frozen;
			}
			return null;
		}

		/// <summary>
		/// The chrome element a control (or the control it is part of) belongs to, or null for a plain control. The first <see cref="IChromeElement"/> on
		/// the way up the visual tree decides, so the parts of a chrome control (the overflow button of a toolbar) follow their control.
		/// </summary>
		public static ChromeElement? ElementOf(Control control)
		{
			for (Visual v = control; v != null; v = v.GetVisualParent())
			{
				if (v is IChromeElement chrome)
				{
					return chrome.Element;
				}
			}
			return null;
		}

		/// <summary>True while the element the control belongs to has the Native variant (RV5b).</summary>
		public static bool IsNative(Control control)
		{
			if (!ChromeVariants.AnyNative)
			{
				return false;
			}
			ChromeElement? element = ElementOf(control);
			return element.HasValue && ChromeVariants.IsNative(element.Value);
		}

		/// <summary>The stock theme's brush (<c>SystemControl*</c>) for the appearance of a control, or null.</summary>
		public static IBrush StockBrush(Control control, string key) => ChromeNativePalette.StockBrush(VariantOf(control), key);

		/// <summary>A solid brush in the colour of a token (alpha kept), cached per appearance. For the Native variant: the palette's stock brush.</summary>
		public static IBrush Brush(Control control, string token)
		{
			ThemeVariant variant = VariantOf(control);
			if (IsNative(control) && ChromeNativePalette.Brush(variant, token) is IBrush native)
			{
				return native;
			}
			return brushes.GetOrAdd((variant, token), key => new ImmutableSolidColorBrush(ThemeTokens.Get(key.Item2, key.Item1)));
		}

		/// <summary>The same for an element other than the control's own (a header drawn by the page list is a <see cref="ChromeElement.ListHeader"/>).</summary>
		public static IBrush Brush(ChromeElement element, Control control, string token)
		{
			ThemeVariant variant = VariantOf(control);
			if (ChromeVariants.IsNative(element) && ChromeNativePalette.Brush(variant, token) is IBrush native)
			{
				return native;
			}
			return brushes.GetOrAdd((variant, token), key => new ImmutableSolidColorBrush(ThemeTokens.Get(key.Item2, key.Item1)));
		}

		/// <summary>A solid brush in the colour of a token with its own alpha (0 to 255, over the token's), cached per appearance.</summary>
		public static IBrush Brush(Control control, string token, byte alpha)
		{
			ThemeVariant variant = VariantOf(control);
			if (IsNative(control) && ChromeNativePalette.Brush(variant, token, alpha) is IBrush native)
			{
				return native;
			}
			return brushes.GetOrAdd((variant, token + "@" + alpha), key =>
			{
				Avalonia.Media.Color c = ThemeTokens.Get(token, key.Item1);
				return new ImmutableSolidColorBrush(Avalonia.Media.Color.FromArgb(alpha, c.R, c.G, c.B));
			});
		}

		/// <summary>
		/// A top to bottom gradient through the given token colours (evenly spaced), cached per appearance. The Native variant is flat: the palette's
		/// brush of the middle token (when the palette maps it).
		/// </summary>
		public static IBrush VerticalGradient(Control control, params string[] tokens)
		{
			ThemeVariant variant = VariantOf(control);
			if (IsNative(control) && ChromeNativePalette.Brush(variant, tokens[(tokens.Length - 1) / 2]) is IBrush native)
			{
				return native;
			}
			return brushes.GetOrAdd((variant, "gradient:v:" + string.Join("|", tokens)), key => CreateGradient(key.Item1, tokens, new RelativePoint(0, 0, RelativeUnit.Relative), new RelativePoint(0, 1, RelativeUnit.Relative)));
		}

		/// <summary>A left to right gradient (<c>LinearGradientBrush</c> with angle 0 of the original), cached per appearance. Flat for Native, as <see cref="VerticalGradient"/>.</summary>
		public static IBrush HorizontalGradient(Control control, params string[] tokens)
		{
			ThemeVariant variant = VariantOf(control);
			if (IsNative(control) && ChromeNativePalette.Brush(variant, tokens[(tokens.Length - 1) / 2]) is IBrush native)
			{
				return native;
			}
			return brushes.GetOrAdd((variant, "gradient:h:" + string.Join("|", tokens)), key => CreateGradient(key.Item1, tokens, new RelativePoint(0, 0, RelativeUnit.Relative), new RelativePoint(1, 0, RelativeUnit.Relative)));
		}

		private static IBrush CreateGradient(ThemeVariant variant, string[] tokens, RelativePoint start, RelativePoint end)
		{
			var stops = new ImmutableGradientStop[tokens.Length];
			for (int i = 0; i < tokens.Length; i++)
			{
				stops[i] = new ImmutableGradientStop(tokens.Length == 1 ? 0 : (double)i / (tokens.Length - 1), ThemeTokens.Get(tokens[i], variant));
			}
			return new ImmutableLinearGradientBrush(stops, 1, null, null, GradientSpreadMethod.Pad, start, end);
		}

		/// <summary>A horizontal line as a filled rectangle (crisp at every scale).</summary>
		public static void HLine(DrawingContext context, IBrush brush, double x, double y, double width, double thickness = 1)
		{
			context.DrawRectangle(brush, null, new Rect(x, y, width, thickness));
		}

		/// <summary>A vertical line as a filled rectangle.</summary>
		public static void VLine(DrawingContext context, IBrush brush, double x, double y, double height, double thickness = 1)
		{
			context.DrawRectangle(brush, null, new Rect(x, y, thickness, height));
		}

		/// <summary>A frame inside <paramref name="rect"/> drawn as filled rectangles; <paramref name="sides"/> picks the sides.</summary>
		public static void Frame(DrawingContext context, IBrush brush, Rect rect, Sides sides = Sides.All, double thickness = 1)
		{
			if ((sides & Sides.Top) != 0)
			{
				HLine(context, brush, rect.X, rect.Y, rect.Width, thickness);
			}
			if ((sides & Sides.Bottom) != 0)
			{
				HLine(context, brush, rect.X, rect.Bottom - thickness, rect.Width, thickness);
			}
			if ((sides & Sides.Left) != 0)
			{
				VLine(context, brush, rect.X, rect.Y, rect.Height, thickness);
			}
			if ((sides & Sides.Right) != 0)
			{
				VLine(context, brush, rect.Right - thickness, rect.Y, rect.Height, thickness);
			}
		}

		/// <summary>The selection style of the original (square, one unit frame, flat fill) in a rectangle.</summary>
		public static void Selection(DrawingContext context, Rect rect, StyledSelection style)
		{
			if (style.State == SelectionState.None)
			{
				return;
			}
			if (style.HasFill && style.FillAlpha > 0)
			{
				context.DrawRectangle(new ImmutableSolidColorBrush(style.Fill), null, rect);
			}
			Frame(context, new ImmutableSolidColorBrush(style.Frame), rect);
		}

		/// <summary>The selection of a control that belongs to a chrome element: the original's, or for the Native variant of the element a rounded one (<see cref="NativeSelection"/>).</summary>
		public static void Selection(Control control, DrawingContext context, Rect rect, StyledSelection style)
		{
			if (IsNative(control))
			{
				NativeSelection(control, context, rect, style);
			}
			else
			{
				Selection(context, rect, style);
			}
		}

		/// <summary>The same for an element other than the control's own.</summary>
		public static void Selection(ChromeElement element, Control control, DrawingContext context, Rect rect, StyledSelection style)
		{
			if (ChromeVariants.IsNative(element))
			{
				NativeSelection(control, context, rect, style);
			}
			else
			{
				Selection(context, rect, style);
			}
		}

		/// <summary>
		/// The Native selection: a rounded rectangle (<see cref="ToolPaint.NativeRadius"/>) filled with the system accent while the view has the focus and
		/// with the stock line colour while it has not, at half the alpha of the original's frame; a focused item without selection gets a rounded accent frame.
		/// </summary>
		public static void NativeSelection(Control control, DrawingContext context, Rect rect, StyledSelection style)
		{
			if (style.State == SelectionState.None)
			{
				return;
			}
			ThemeVariant variant = VariantOf(control);
			bool viewFocused = style.BaseColor == ThemeTokens.Get(ThemeTokens.Names.SelectionFocused, variant);
			IBrush stock = StockBrush(control, ChromeNativePalette.KeyOf(viewFocused ? ChromeNativePalette.Role.Accent : ChromeNativePalette.Role.Line));
			Avalonia.Media.Color c = (stock as ISolidColorBrush)?.Color ?? style.BaseColor;
			if (style.State == SelectionState.Focused)
			{
				context.DrawRectangle(null, new Pen(new ImmutableSolidColorBrush(Avalonia.Media.Color.FromArgb(160, c.R, c.G, c.B)), 1), rect.Deflate(0.5), ToolPaint.NativeRadius, ToolPaint.NativeRadius);
				return;
			}
			context.DrawRectangle(new ImmutableSolidColorBrush(Avalonia.Media.Color.FromArgb((byte)(style.FrameAlpha / 2), c.R, c.G, c.B)), null, rect, ToolPaint.NativeRadius, ToolPaint.NativeRadius);
		}

		/// <summary>An image of the original drawn into a rectangle (its own size when the rectangle has it) with high-quality filtering.</summary>
		public static void Image(DrawingContext context, Avalonia.Media.Imaging.Bitmap bitmap, Rect target, double opacity = 1)
		{
			if (bitmap == null)
			{
				return;
			}
			var source = new Rect(bitmap.Size);
			using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = Avalonia.Media.Imaging.BitmapInterpolationMode.HighQuality }))
			using (opacity < 1 ? context.PushOpacity(opacity) : default(DrawingContext.PushedState))
			{
				context.DrawImage(bitmap, source, target);
			}
		}

		/// <summary>
		/// An icon of the original (a picture from <see cref="Icons"/>). For the Native variant of <see cref="ChromeElement.Icons"/> it is drawn without its
		/// colours (<see cref="Icons.Monochrome"/>): the flat grey glyphs of the macOS toolbars instead of the coloured pictures of the original.
		/// </summary>
		public static void Icon(Control control, DrawingContext context, Avalonia.Media.Imaging.Bitmap bitmap, Rect target, double opacity = 1)
		{
			if (bitmap == null)
			{
				return;
			}
			Image(context, ChromeVariants.IsNative(ChromeElement.Icons) ? Icons.Monochrome(bitmap) : bitmap, target, opacity);
		}

		/// <summary>The text colour of the reader overlays: the white of the glass panels, or for the Native variant the stock text colour (the Native panel is a stock surface).</summary>
		public static IBrush ReaderText(Control control)
		{
			return IsNative(control) ? StockBrush(control, ChromeNativePalette.KeyOf(ChromeNativePalette.Role.Text)) : ResourceBrush(control, "Cr.Reader.OverlayTextBrush");
		}

		/// <summary>The colour of the icons on the glass buttons (black on the glass), or for the Native variant the stock text colour.</summary>
		public static IBrush ReaderGlassIcon(Control control)
		{
			return IsNative(control) ? StockBrush(control, ChromeNativePalette.KeyOf(ChromeNativePalette.Role.Text)) : ResourceBrush(control, "Cr.Reader.GlassIconBrush");
		}

		/// <summary>A rectangle of the given size centred in <paramref name="area"/> on whole units.</summary>
		public static Rect Centered(Size size, Rect area)
		{
			return new Rect(Math.Floor(area.X + (area.Width - size.Width) / 2), Math.Floor(area.Y + (area.Height - size.Height) / 2), size.Width, size.Height);
		}
	}

	[Flags]
	public enum Sides
	{
		None = 0,
		Left = 1,
		Top = 2,
		Right = 4,
		Bottom = 8,
		All = Left | Top | Right | Bottom,
	}
}
