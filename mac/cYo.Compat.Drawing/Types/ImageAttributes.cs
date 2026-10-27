// M1.2c: real behaviour. Signatures follow the System.Drawing.Common reference assembly.
// Only SetColorMatrix is implemented (it is applied by Graphics.DrawImage as a Skia colour filter). Other attributes (wrap mode, colour keys, gamma, remap tables) are not declared.
using System;

namespace System.Drawing.Imaging
{
	public sealed partial class ImageAttributes : ICloneable, IDisposable
	{
		private ColorMatrix colorMatrix;
		private bool disposed;

		public ImageAttributes()
		{
		}

		/// <summary>The matrix is copied, so later changes of <paramref name="newColorMatrix"/> do not affect the attributes.</summary>
		public void SetColorMatrix(ColorMatrix newColorMatrix)
		{
			EnsureAlive();
			if (newColorMatrix == null)
			{
				throw new ArgumentNullException(nameof(newColorMatrix));
			}
			colorMatrix = new ColorMatrix(ToJagged(newColorMatrix));
		}

		/// <summary>The Skia form of the colour matrix, or null when none is set.</summary>
		internal float[] SkiaColorMatrix
		{
			get
			{
				EnsureAlive();
				return colorMatrix?.ToSkia();
			}
		}

		private static float[][] ToJagged(ColorMatrix matrix)
		{
			float[][] rows = new float[5][];
			for (int row = 0; row < 5; row++)
			{
				rows[row] = new float[5];
				for (int column = 0; column < 5; column++)
				{
					rows[row][column] = matrix[row, column];
				}
			}
			return rows;
		}

		private void EnsureAlive()
		{
			if (disposed)
			{
				throw new ObjectDisposedException(nameof(ImageAttributes));
			}
		}

		public void Dispose()
		{
			Dispose(disposing: true);
			GC.SuppressFinalize(this);
		}

		private void Dispose(bool disposing)
		{
			disposed = true;
		}

		public object Clone()
		{
			EnsureAlive();
			ImageAttributes copy = new ImageAttributes();
			if (colorMatrix != null)
			{
				copy.colorMatrix = new ColorMatrix(ToJagged(colorMatrix));
			}
			return copy;
		}
	}
}
