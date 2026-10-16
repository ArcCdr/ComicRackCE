// M1.2a surface skeleton. Signatures follow the System.Drawing.Common reference assembly; members the Core uses, bodies filled in by M1.2b/M1.2c.
using System;

namespace System.Drawing
{
	public sealed class Bitmap : Image
	{
		public Bitmap(int width, int height, System.Drawing.Imaging.PixelFormat format)
		{
			throw new NotSupportedException("System.Drawing.Bitmap.ctor is not yet supported on macOS");
		}

		public Bitmap(int width, int height)
		{
			throw new NotSupportedException("System.Drawing.Bitmap.ctor is not yet supported on macOS");
		}

		public Bitmap(Image original, int width, int height)
		{
			throw new NotSupportedException("System.Drawing.Bitmap.ctor is not yet supported on macOS");
		}

		public Bitmap Clone(Rectangle rect, System.Drawing.Imaging.PixelFormat format) => throw new NotSupportedException("System.Drawing.Bitmap.Clone is not yet supported on macOS");

		public IntPtr GetHbitmap() => throw new NotSupportedException("System.Drawing.Bitmap.GetHbitmap is not yet supported on macOS");

		public IntPtr GetHicon() => throw new NotSupportedException("System.Drawing.Bitmap.GetHicon is not yet supported on macOS");

		public Color GetPixel(int x, int y) => throw new NotSupportedException("System.Drawing.Bitmap.GetPixel is not yet supported on macOS");

		public System.Drawing.Imaging.BitmapData LockBits(Rectangle rect, System.Drawing.Imaging.ImageLockMode flags, System.Drawing.Imaging.PixelFormat format) => throw new NotSupportedException("System.Drawing.Bitmap.LockBits is not yet supported on macOS");

		public void UnlockBits(System.Drawing.Imaging.BitmapData bitmapdata) => throw new NotSupportedException("System.Drawing.Bitmap.UnlockBits is not yet supported on macOS");
	}
}
