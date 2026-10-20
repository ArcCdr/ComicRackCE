using System;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace ComicRack.Mac.Controls
{
	/// <summary>
	/// What the original draws around the pages when "realistic pages" is on, which is its default (<c>DisplayWorkspace.DrawRealisticPages</c>,
	/// <c>ComicDisplayControl.DrawPageOrnaments</c>): a one unit black outline on each page and a shadow around the pages together, as wide as
	/// 1 percent of the smaller side of the pages (<c>PageShadowWidthPercentage</c>, at most 255) and fading from 0.6 black (<c>PageShadowOpacity</c>)
	/// to nothing, drawn as a radial fall-off at the corners and a linear one along the sides (<c>GraphicsExtensions.DrawShadow</c> with the edge
	/// parts). The colours are <c>Cr.Reader.PageShadowBrush</c> and <c>Cr.Reader.PageFrameBrush</c>.
	/// </summary>
	public static class PageFrame
	{
		/// <summary><c>EngineConfiguration.PageShadowWidthPercentage</c>.</summary>
		public const double ShadowPercent = 1;

		/// <summary>The shadow depth in units for pages that together fill <paramref name="union"/>: <c>(int)(Math.Min(w, h) * 1 / 100)</c>, at most 255.</summary>
		public static double ShadowDepth(Rect union)
		{
			return Math.Clamp(Math.Floor(Math.Min(union.Width, union.Height) * ShadowPercent / 100), 0, 255);
		}

		/// <summary>The shadow around <paramref name="union"/>, outside it.</summary>
		public static void DrawShadow(DrawingContext context, Avalonia.Controls.Control owner, Rect union)
		{
			double d = ShadowDepth(union);
			if (d <= 0 || !(ChromePaint.ResourceBrush(owner, "Cr.Reader.PageShadowBrush") is ISolidColorBrush solid))
			{
				return;
			}
			Color near = solid.Color, far = new Color(0, near.R, near.G, near.B);
			var stops = new[] { new ImmutableGradientStop(0, near), new ImmutableGradientStop(1, far) };
			// the four sides (the corners are left out of them)
			context.DrawRectangle(Linear(stops, 0, 1, 0, 0), null, new Rect(union.X, union.Y - d, union.Width, d));
			context.DrawRectangle(Linear(stops, 0, 0, 0, 1), null, new Rect(union.X, union.Bottom, union.Width, d));
			context.DrawRectangle(Linear(stops, 1, 0, 0, 0), null, new Rect(union.X - d, union.Y, d, union.Height));
			context.DrawRectangle(Linear(stops, 0, 0, 1, 0), null, new Rect(union.Right, union.Y, d, union.Height));
			// the four corners: a quarter of a radial fall-off centred on the corner of the pages
			context.DrawRectangle(Radial(stops, 1, 1), null, new Rect(union.X - d, union.Y - d, d, d));
			context.DrawRectangle(Radial(stops, 0, 1), null, new Rect(union.Right, union.Y - d, d, d));
			context.DrawRectangle(Radial(stops, 1, 0), null, new Rect(union.X - d, union.Bottom, d, d));
			context.DrawRectangle(Radial(stops, 0, 0), null, new Rect(union.Right, union.Bottom, d, d));
		}

		/// <summary><c>gr.DrawRectangle(rect, Color.Black, 1f)</c>: one unit on the left and top edge of the page and one unit outside it on the right and bottom.</summary>
		public static void DrawOutline(DrawingContext context, Avalonia.Controls.Control owner, Rect page)
		{
			if (page.Width <= 0 || page.Height <= 0)
			{
				return;
			}
			IBrush brush = ChromePaint.ResourceBrush(owner, "Cr.Reader.PageFrameBrush");
			if (brush != null)
			{
				ChromePaint.Frame(context, brush, new Rect(page.X, page.Y, page.Width + 1, page.Height + 1));
			}
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
