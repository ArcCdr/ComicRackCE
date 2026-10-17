// M1.2c: real behaviour. Signatures follow the System.Drawing.Common reference assembly.
// A 5 x 5 matrix in the GDI+ row-vector convention: [R G B A 1] x M = [R' G' B' A' 1]. Only the matrix itself is declared; the Matrix00..Matrix44 properties are not (the Core never uses them).
using System;

namespace System.Drawing.Imaging
{
	public sealed class ColorMatrix
	{
		private readonly float[] values = new float[25];

		/// <summary>The identity matrix.</summary>
		public ColorMatrix()
		{
			for (int i = 0; i < 5; i++)
			{
				values[i * 5 + i] = 1f;
			}
		}

		/// <summary>Copies a 5 x 5 jagged array (anything else gives <see cref="ArgumentException"/>).</summary>
		public ColorMatrix(float[][] newColorMatrix)
		{
			if (newColorMatrix == null)
			{
				throw new ArgumentNullException(nameof(newColorMatrix));
			}
			if (newColorMatrix.Length != 5)
			{
				throw new ArgumentException("Parameter is not valid.", nameof(newColorMatrix));
			}
			for (int row = 0; row < 5; row++)
			{
				if (newColorMatrix[row] == null || newColorMatrix[row].Length != 5)
				{
					throw new ArgumentException("Parameter is not valid.", nameof(newColorMatrix));
				}
				Array.Copy(newColorMatrix[row], 0, values, row * 5, 5);
			}
		}

		public float this[int row, int column]
		{
			get
			{
				Check(row, column);
				return values[row * 5 + column];
			}
			set
			{
				Check(row, column);
				values[row * 5 + column] = value;
			}
		}

		/// <summary>The alpha scale (row 3, column 3); the Core sets it to draw an image with an opacity.</summary>
		public float Matrix33
		{
			get => values[3 * 5 + 3];
			set => values[3 * 5 + 3] = value;
		}

		private static void Check(int row, int column)
		{
			if ((uint)row > 4 || (uint)column > 4)
			{
				throw new IndexOutOfRangeException();
			}
		}

		/// <summary>
		/// The 4 x 5 row-major matrix that Skia's colour filter takes (rows = output R, G, B, A; columns = input R, G, B, A and a translation in 0..1 units),
		/// which is the transpose of the 4 x 4 block of the GDI+ matrix plus its fifth row as the translation. The fifth column of the GDI+ matrix is ignored (as in GDI+).
		/// </summary>
		internal float[] ToSkia()
		{
			float[] skia = new float[20];
			for (int output = 0; output < 4; output++)
			{
				for (int input = 0; input < 4; input++)
				{
					skia[output * 5 + input] = values[input * 5 + output];
				}
				skia[output * 5 + 4] = values[4 * 5 + output];
			}
			return skia;
		}
	}
}
