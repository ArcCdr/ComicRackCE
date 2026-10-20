using System;
using System.Collections.Concurrent;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
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

		/// <summary>A solid brush in the colour of a token (alpha kept), cached per appearance.</summary>
		public static IBrush Brush(Control control, string token)
		{
			ThemeVariant variant = VariantOf(control);
			return brushes.GetOrAdd((variant, token), key => new ImmutableSolidColorBrush(ThemeTokens.Get(key.Item2, key.Item1)));
		}

		/// <summary>A top to bottom gradient through the given token colours (evenly spaced), cached per appearance.</summary>
		public static IBrush VerticalGradient(Control control, params string[] tokens)
		{
			ThemeVariant variant = VariantOf(control);
			return brushes.GetOrAdd((variant, "gradient:v:" + string.Join("|", tokens)), key => CreateGradient(key.Item1, tokens, new RelativePoint(0, 0, RelativeUnit.Relative), new RelativePoint(0, 1, RelativeUnit.Relative)));
		}

		/// <summary>A left to right gradient (<c>LinearGradientBrush</c> with angle 0 of the original), cached per appearance.</summary>
		public static IBrush HorizontalGradient(Control control, params string[] tokens)
		{
			ThemeVariant variant = VariantOf(control);
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
