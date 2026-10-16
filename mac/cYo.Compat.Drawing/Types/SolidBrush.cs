// M1.2a surface skeleton. Signatures follow the System.Drawing.Common reference assembly; members the Core uses, bodies filled in by M1.2b/M1.2c.
using System;

namespace System.Drawing
{
	public sealed class SolidBrush : Brush
	{
		public SolidBrush(Color color)
		{
			throw new NotSupportedException("System.Drawing.SolidBrush.ctor is not yet supported on macOS");
		}

		public override object Clone() => throw new NotSupportedException("System.Drawing.SolidBrush.Clone is not yet supported on macOS");
	}
}
