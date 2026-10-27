// M1.2a surface skeleton. Signatures follow the System.Drawing.Common reference assembly; members the Core uses, bodies filled in by M1.2b/M1.2c.
using System;

namespace System.Drawing.Drawing2D
{
	public sealed partial class GraphicsPath : MarshalByRefObject, ICloneable, IDisposable
	{
		public GraphicsPath()
		{
			throw new NotSupportedException("System.Drawing.Drawing2D.GraphicsPath.ctor is not yet supported on macOS");
		}

		public void AddArc(int x, int y, int width, int height, float startAngle, float sweepAngle) => throw new NotSupportedException("System.Drawing.Drawing2D.GraphicsPath.AddArc is not yet supported on macOS");

		public void AddEllipse(int x, int y, int width, int height) => throw new NotSupportedException("System.Drawing.Drawing2D.GraphicsPath.AddEllipse is not yet supported on macOS");

		public void AddLine(int x1, int y1, int x2, int y2) => throw new NotSupportedException("System.Drawing.Drawing2D.GraphicsPath.AddLine is not yet supported on macOS");

		public void AddLines(params System.Drawing.PointF[] points) => throw new NotSupportedException("System.Drawing.Drawing2D.GraphicsPath.AddLines is not yet supported on macOS");

		public void CloseFigure() => throw new NotSupportedException("System.Drawing.Drawing2D.GraphicsPath.CloseFigure is not yet supported on macOS");

		public void Dispose()
		{
			Dispose(disposing: true);
			GC.SuppressFinalize(this);
		}

		private void Dispose(bool disposing)
		{
		}

		public object Clone() => throw new NotSupportedException("System.Drawing.Drawing2D.GraphicsPath.Clone is not yet supported on macOS");
	}
}
