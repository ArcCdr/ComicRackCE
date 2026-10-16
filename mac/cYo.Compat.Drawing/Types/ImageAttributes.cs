// M1.2a surface skeleton. Signatures follow the System.Drawing.Common reference assembly; members the Core uses, bodies filled in by M1.2b/M1.2c.
using System;

namespace System.Drawing.Imaging
{
	public sealed class ImageAttributes : ICloneable, IDisposable
	{
		public ImageAttributes()
		{
			throw new NotSupportedException("System.Drawing.Imaging.ImageAttributes.ctor is not yet supported on macOS");
		}

		public void SetColorMatrix(ColorMatrix newColorMatrix) => throw new NotSupportedException("System.Drawing.Imaging.ImageAttributes.SetColorMatrix is not yet supported on macOS");

		public void Dispose()
		{
			Dispose(disposing: true);
			GC.SuppressFinalize(this);
		}

		private void Dispose(bool disposing)
		{
		}

		public object Clone() => throw new NotSupportedException("System.Drawing.Imaging.ImageAttributes.Clone is not yet supported on macOS");
	}
}
