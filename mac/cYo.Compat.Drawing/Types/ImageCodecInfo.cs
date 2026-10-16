// M1.2a surface skeleton. Signatures follow the System.Drawing.Common reference assembly; members the Core uses, bodies filled in by M1.2b/M1.2c.
using System;

namespace System.Drawing.Imaging
{
	public sealed class ImageCodecInfo
	{
		internal ImageCodecInfo()
		{
		}

		public Guid FormatID
		{
			get => throw new NotSupportedException("System.Drawing.Imaging.ImageCodecInfo.FormatID is not yet supported on macOS");
		}

		public static ImageCodecInfo[] GetImageEncoders() => throw new NotSupportedException("System.Drawing.Imaging.ImageCodecInfo.GetImageEncoders is not yet supported on macOS");
	}
}
