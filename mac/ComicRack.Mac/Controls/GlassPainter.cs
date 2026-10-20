using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ComicRack.Mac.Themes;

namespace ComicRack.Mac.Controls
{
	/// <summary>
	/// The glass pictures of the reader overlays (<c>PanelRenderer</c>, <c>ScalableBitmap</c>): a picture is cut into nine parts by its margins, the
	/// corners keep their size and the edges and the centre stretch. <see cref="Panel"/> draws <c>BlackGlassPanel</c> (margins 5, 5, 10, 10 inside the
	/// source rectangle 5, 5, 215, 125) and returns the rectangle the content goes into, as <c>PanelRenderer.Draw</c> does.
	/// </summary>
	public static class GlassPainter
	{
		/// <summary><c>PanelRenderer.panels[BlackGlass]</c>: the margins of the 9-slice.</summary>
		public static readonly Thickness PanelMargin = new Thickness(5, 5, 10, 10);

		/// <summary><c>PanelRenderer.panels[BlackGlass]</c>: the part of the picture that is used.</summary>
		public static readonly Rect PanelSource = new Rect(5, 5, 215, 125);

		/// <summary>The resource name of the glass panel (<c>ComicRack.Engine.Display.Forms/Resources/BlackGlassPanel.png</c>).</summary>
		public const string PanelPicture = "BlackGlassPanel";

		/// <summary>The rectangle of the content inside a panel of <paramref name="dest"/> (<c>PanelRenderer.GetMargin</c>).</summary>
		public static Rect Inner(Rect dest)
		{
			return new Rect(dest.X + PanelMargin.Left, dest.Y + PanelMargin.Top, Math.Max(0, dest.Width - PanelMargin.Left - PanelMargin.Right), Math.Max(0, dest.Height - PanelMargin.Top - PanelMargin.Bottom));
		}

		/// <summary>Draws the glass panel into <paramref name="dest"/> and returns the content rectangle.</summary>
		public static Rect Panel(DrawingContext context, Control owner, Rect dest, double opacity = 1)
		{
			Bitmap bitmap = Icons.TryGet(PanelPicture, ChromePaint.VariantOf(owner));
			if (bitmap != null)
			{
				NineSlice(context, bitmap, dest, PanelSource, PanelMargin, opacity);
			}
			return Inner(dest);
		}

		/// <summary>
		/// <c>ScalableBitmap.Draw</c>: the part <paramref name="src"/> of the picture drawn into <paramref name="dest"/>, cut by <paramref name="margin"/>.
		/// Returns the centre rectangle of <paramref name="dest"/> (the content area).
		/// </summary>
		public static Rect NineSlice(DrawingContext context, Bitmap bitmap, Rect dest, Rect src, Thickness margin, double opacity = 1)
		{
			using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.HighQuality }))
			using (opacity < 1 ? context.PushOpacity(opacity) : default(DrawingContext.PushedState))
			{
				if (margin.Left + margin.Right + margin.Top + margin.Bottom == 0)
				{
					context.DrawImage(bitmap, src, dest);
					return dest;
				}
				double l = margin.Left, r = margin.Right, t = margin.Top, b = margin.Bottom;
				var s2 = new Rect(src.Left + l, src.Top + t, Math.Max(0, src.Width - l - r), Math.Max(0, src.Height - t - b));
				var d2 = new Rect(dest.Left + l, dest.Top + t, Math.Max(0, dest.Width - l - r), Math.Max(0, dest.Height - t - b));
				void Part(Rect from, Rect to)
				{
					if (from.Width > 0 && from.Height > 0 && to.Width > 0 && to.Height > 0)
					{
						context.DrawImage(bitmap, from, to);
					}
				}
				Part(new Rect(src.Left, src.Top, l, t), new Rect(dest.Left, dest.Top, l, t));
				Part(new Rect(s2.Left, src.Top, s2.Width, t), new Rect(d2.Left, dest.Top, d2.Width, t));
				Part(new Rect(s2.Right, src.Top, r, t), new Rect(d2.Right, dest.Top, r, t));
				Part(new Rect(s2.Right, s2.Top, r, s2.Height), new Rect(d2.Right, d2.Top, r, d2.Height));
				Part(new Rect(s2.Right, s2.Bottom, r, b), new Rect(d2.Right, d2.Bottom, r, b));
				Part(new Rect(s2.Left, s2.Bottom, s2.Width, b), new Rect(d2.Left, d2.Bottom, d2.Width, b));
				Part(new Rect(src.Left, s2.Bottom, l, b), new Rect(dest.Left, d2.Bottom, l, b));
				Part(new Rect(src.Left, s2.Top, l, s2.Height), new Rect(dest.Left, d2.Top, l, d2.Height));
				Part(s2, d2);
				return d2;
			}
		}

		/// <summary>
		/// A picture drawn as a one-colour silhouette (the black icons on the glass buttons: the original adjusts the brightness of the picture to
		/// -1, which leaves its shape in black).
		/// </summary>
		public static void Silhouette(DrawingContext context, Bitmap bitmap, Rect dest, IBrush colour, double opacity = 1)
		{
			if (bitmap == null || colour == null)
			{
				return;
			}
			var mask = new ImageBrush(bitmap) { Stretch = Stretch.Fill };
			using (opacity < 1 ? context.PushOpacity(opacity) : default(DrawingContext.PushedState))
			using (context.PushOpacityMask(mask, dest))
			{
				context.DrawRectangle(colour, null, dest);
			}
		}
	}
}
