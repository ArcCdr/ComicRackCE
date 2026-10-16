// M1.2a surface skeleton. Signatures follow the System.Drawing.Common reference assembly; members the Core uses, bodies filled in by M1.2b/M1.2c.
using System;

namespace System.Drawing.Drawing2D
{
	public sealed class Matrix : MarshalByRefObject, IDisposable
	{
		public Matrix()
		{
			throw new NotSupportedException("System.Drawing.Drawing2D.Matrix.ctor is not yet supported on macOS");
		}

		public void Rotate(float angle, MatrixOrder order) => throw new NotSupportedException("System.Drawing.Drawing2D.Matrix.Rotate is not yet supported on macOS");

		public void TransformPoints(params System.Drawing.Point[] pts) => throw new NotSupportedException("System.Drawing.Drawing2D.Matrix.TransformPoints is not yet supported on macOS");

		public void Translate(float offsetX, float offsetY, MatrixOrder order) => throw new NotSupportedException("System.Drawing.Drawing2D.Matrix.Translate is not yet supported on macOS");

		public void Dispose()
		{
			Dispose(disposing: true);
			GC.SuppressFinalize(this);
		}

		private void Dispose(bool disposing)
		{
		}
	}
}
