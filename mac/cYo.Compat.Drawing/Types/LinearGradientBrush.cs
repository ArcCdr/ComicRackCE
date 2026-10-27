// M1.2a surface skeleton. Signatures follow the System.Drawing.Common reference assembly; members the Core uses, bodies filled in by M1.2b/M1.2c.
using System;

namespace System.Drawing.Drawing2D
{
	public sealed partial class LinearGradientBrush : System.Drawing.Brush
	{
		public LinearGradientBrush(System.Drawing.Rectangle rect, System.Drawing.Color color1, System.Drawing.Color color2, float angle)
		{
			throw new NotSupportedException("System.Drawing.Drawing2D.LinearGradientBrush.ctor is not yet supported on macOS");
		}

		public void SetSigmaBellShape(float focus, float scale) => throw new NotSupportedException("System.Drawing.Drawing2D.LinearGradientBrush.SetSigmaBellShape is not yet supported on macOS");

		public override object Clone() => throw new NotSupportedException("System.Drawing.Drawing2D.LinearGradientBrush.Clone is not yet supported on macOS");
	}
}
