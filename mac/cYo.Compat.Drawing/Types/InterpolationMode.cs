// M1.2a surface skeleton. Signatures follow the System.Drawing.Common reference assembly; members the Core uses, bodies filled in by M1.2b/M1.2c.
using System;

namespace System.Drawing.Drawing2D
{
	public enum InterpolationMode
	{
		Invalid = -1,
		Default = 0,
		Low = 1,
		High = 2,
		Bilinear = 3,
		Bicubic = 4,
		NearestNeighbor = 5,
		HighQualityBilinear = 6,
		HighQualityBicubic = 7
	}
}
