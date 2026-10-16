// M1.2a surface skeleton. Signatures follow the System.Drawing.Common reference assembly; members the Core uses, bodies filled in by M1.2b/M1.2c.
using System;

namespace System.Drawing.Imaging
{
	public sealed class ColorMatrix
	{
		public ColorMatrix()
		{
			throw new NotSupportedException("System.Drawing.Imaging.ColorMatrix.ctor is not yet supported on macOS");
		}

		public ColorMatrix(float[][] newColorMatrix)
		{
			throw new NotSupportedException("System.Drawing.Imaging.ColorMatrix.ctor is not yet supported on macOS");
		}

		public float this[int row, int column]
		{
			get => throw new NotSupportedException("System.Drawing.Imaging.ColorMatrix.Item is not yet supported on macOS");
		}

		public float Matrix33
		{
			set => throw new NotSupportedException("System.Drawing.Imaging.ColorMatrix.Matrix33 is not yet supported on macOS");
		}
	}
}
