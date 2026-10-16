// M1.2a surface skeleton. Signatures follow the System.Drawing.Common reference assembly; members the Core uses, bodies filled in by M1.2b/M1.2c.
using System;

namespace System.Drawing.Imaging
{
	public sealed class ImageFormat
	{
		internal ImageFormat()
		{
		}

		public static ImageFormat Bmp
		{
			get => throw new NotSupportedException("System.Drawing.Imaging.ImageFormat.Bmp is not yet supported on macOS");
		}

		public static ImageFormat Gif
		{
			get => throw new NotSupportedException("System.Drawing.Imaging.ImageFormat.Gif is not yet supported on macOS");
		}

		public Guid Guid
		{
			get => throw new NotSupportedException("System.Drawing.Imaging.ImageFormat.Guid is not yet supported on macOS");
		}

		public static ImageFormat Jpeg
		{
			get => throw new NotSupportedException("System.Drawing.Imaging.ImageFormat.Jpeg is not yet supported on macOS");
		}

		public static ImageFormat Png
		{
			get => throw new NotSupportedException("System.Drawing.Imaging.ImageFormat.Png is not yet supported on macOS");
		}

		public static ImageFormat Tiff
		{
			get => throw new NotSupportedException("System.Drawing.Imaging.ImageFormat.Tiff is not yet supported on macOS");
		}
	}
}
