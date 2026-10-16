// M1.2a surface skeleton. Signatures follow the System.Drawing.Common reference assembly; members the Core uses, bodies filled in by M1.2b/M1.2c.
using System;

namespace System.Drawing.Imaging
{
	public sealed class BitmapData
	{
		internal BitmapData()
		{
		}

		public int Height
		{
			get => throw new NotSupportedException("System.Drawing.Imaging.BitmapData.Height is not yet supported on macOS");
		}

		public IntPtr Scan0
		{
			get => throw new NotSupportedException("System.Drawing.Imaging.BitmapData.Scan0 is not yet supported on macOS");
		}

		public int Stride
		{
			get => throw new NotSupportedException("System.Drawing.Imaging.BitmapData.Stride is not yet supported on macOS");
		}
	}
}
