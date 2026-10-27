// M1.2a surface skeleton. Signatures follow the System.Drawing.Common reference assembly; members the Core uses, bodies filled in by M1.2b/M1.2c.
using System;

namespace System.Drawing
{
	public static partial class SystemFonts
	{
		public static Font DefaultFont
		{
			get => throw new NotSupportedException("System.Drawing.SystemFonts.DefaultFont is not yet supported on macOS");
		}

		public static Font MenuFont
		{
			get => throw new NotSupportedException("System.Drawing.SystemFonts.MenuFont is not yet supported on macOS");
		}
	}
}
