// M1.2a surface skeleton. Signatures follow the System.Drawing.Common reference assembly; members the Core uses, bodies filled in by M1.2b/M1.2c.
using System;

namespace System.Drawing
{
	[Flags]
	public enum StringFormatFlags
	{
		DirectionRightToLeft = 1,
		DirectionVertical = 2,
		FitBlackBox = 4,
		DisplayFormatControl = 32,
		NoFontFallback = 1024,
		MeasureTrailingSpaces = 2048,
		NoWrap = 4096,
		LineLimit = 8192,
		NoClip = 16384
	}
}
