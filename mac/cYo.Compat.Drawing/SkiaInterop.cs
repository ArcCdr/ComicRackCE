using System;
using System.Drawing;
using SkiaSharp;

namespace cYo.Compat.Drawing
{
	/// <summary>The bridge between the System.Drawing stand-in and Skia (for the Avalonia UI, which draws with Skia).</summary>
	public static class SkiaInterop
	{
		/// <summary>
		/// The SKBitmap that holds the pixels of <paramref name="bitmap"/>: the same object and memory, no copy.
		/// It is always BGRA8888. Its alpha type follows the pixel format: Unpremul for Format32bppArgb, Premul for Format32bppPArgb, Opaque for
		/// Format32bppRgb and Format24bppRgb (an opaque bitmap draws without any alpha handling, so prefer it for page images).
		/// The bitmap owns it: do not dispose it, and do not use it after the bitmap is disposed (ObjectDisposedException if it already is).
		/// </summary>
		public static SKBitmap GetSKBitmap(Bitmap bitmap)
		{
			if (bitmap == null)
			{
				throw new ArgumentNullException(nameof(bitmap));
			}
			return bitmap.Storage;
		}
	}
}
