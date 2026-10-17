using System;
using Avalonia;
using SkiaSharp;

namespace ComicRack.Mac.Reader
{
	/// <summary>
	/// How a page is resampled. Skia's cubic filters (Mitchell, Catmull-Rom) and plain linear filtering do not average the source
	/// pixels they skip, so a large reduction aliases (M1.2c measured stripes of period 3 reduced 8:1: Mitchell 119..255 where the true
	/// mean is 170, linear plus mipmaps 159..191). The choice therefore depends on the scale, in device pixels per source pixel:
	/// <list type="bullet">
	/// <item>exactly 1: nearest neighbour, which copies the pixels (the edges are snapped to whole pixels by <see cref="ReaderPainter"/>);</item>
	/// <item>more than 1 (enlarging): Mitchell, smooth without ringing;</item>
	/// <item>between 1/2 and 1 (a mild reduction): Catmull-Rom, which stays sharp and does not skip enough pixels to alias visibly;</item>
	/// <item>below 1/2: linear with mipmaps (trilinear), which averages.</item>
	/// </list>
	/// </summary>
	public static class ReaderSampling
	{
		/// <summary>Below this scale the mipmap filter takes over from the cubic one.</summary>
		public const double MipmapBelow = 0.5;

		public static SKSamplingOptions For(double scale)
		{
			if (Math.Abs(scale - 1.0) < 0.0005)
			{
				return new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None);
			}
			if (scale > 1.0)
			{
				return new SKSamplingOptions(SKCubicResampler.Mitchell);
			}
			if (scale >= MipmapBelow)
			{
				return new SKSamplingOptions(SKCubicResampler.CatmullRom);
			}
			return new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
		}

		/// <summary>The name of the filter <see cref="For"/> picks (diagnostics and tests).</summary>
		public static string Describe(double scale)
		{
			if (Math.Abs(scale - 1.0) < 0.0005)
			{
				return "nearest";
			}
			if (scale > 1.0)
			{
				return "mitchell";
			}
			return scale >= MipmapBelow ? "catmull-rom" : "mipmap";
		}
	}

	/// <summary>Draws a page on a Skia canvas: snapped to whole device pixels, resampled as <see cref="ReaderSampling"/> says.</summary>
	public static class ReaderPainter
	{
		/// <summary>
		/// Draws <paramref name="image"/> into <paramref name="destination"/> (in the canvas's current coordinates). The rectangle is
		/// mapped to device pixels and rounded, so that a page shown at scale 1 lands on whole pixels and is copied exactly, and the
		/// edges of the others are sharp. The canvas is left as it was.
		/// </summary>
		/// <returns>The scale used, in device pixels per source pixel.</returns>
		public static double DrawPage(SKCanvas canvas, SKImage image, Rect destination)
		{
			SKMatrix matrix = canvas.TotalMatrix;
			SKRect local = new SKRect((float)destination.Left, (float)destination.Top, (float)destination.Right, (float)destination.Bottom);
			SKRect device = matrix.MapRect(local);
			var snapped = new SKRect((float)Math.Round(device.Left), (float)Math.Round(device.Top), (float)Math.Round(device.Right), (float)Math.Round(device.Bottom));
			if (snapped.Width < 1 || snapped.Height < 1 || image.Width <= 0 || image.Height <= 0)
			{
				return 0;
			}
			double scale = snapped.Width / image.Width;
			canvas.Save();
			try
			{
				canvas.ResetMatrix();
				canvas.DrawImage(image, snapped, ReaderSampling.For(scale));
			}
			finally
			{
				canvas.Restore();
			}
			return scale;
		}
	}
}
