// M1.2a surface skeleton. Signatures follow the System.Drawing.Common reference assembly; members the Core uses, bodies filled in by M1.2b/M1.2c.
using System;

namespace System.Drawing.Drawing2D
{
	public sealed class PathGradientBrush : System.Drawing.Brush
	{
		public PathGradientBrush(GraphicsPath path)
		{
			throw new NotSupportedException("System.Drawing.Drawing2D.PathGradientBrush.ctor is not yet supported on macOS");
		}

		public System.Drawing.Color CenterColor
		{
			set => throw new NotSupportedException("System.Drawing.Drawing2D.PathGradientBrush.CenterColor is not yet supported on macOS");
		}

		public System.Drawing.Color[] SurroundColors
		{
			set => throw new NotSupportedException("System.Drawing.Drawing2D.PathGradientBrush.SurroundColors is not yet supported on macOS");
		}

		public override object Clone() => throw new NotSupportedException("System.Drawing.Drawing2D.PathGradientBrush.Clone is not yet supported on macOS");
	}
}
