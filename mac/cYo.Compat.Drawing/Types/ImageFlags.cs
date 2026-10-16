// M1.2a surface skeleton. Signatures follow the System.Drawing.Common reference assembly; members the Core uses, bodies filled in by M1.2b/M1.2c.
using System;

namespace System.Drawing.Imaging
{
	[Flags]
	public enum ImageFlags
	{
		None = 0,
		Scalable = 1,
		HasAlpha = 2,
		HasTranslucent = 4,
		PartiallyScalable = 8,
		ColorSpaceRgb = 16,
		ColorSpaceCmyk = 32,
		ColorSpaceGray = 64,
		ColorSpaceYcbcr = 128,
		ColorSpaceYcck = 256,
		HasRealDpi = 4096,
		HasRealPixelSize = 8192,
		ReadOnly = 65536,
		Caching = 131072
	}
}
