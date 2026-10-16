// M1.2a surface skeleton. Signatures follow the System.Drawing.Common reference assembly; members the Core uses, bodies filled in by M1.2b/M1.2c.
using System;

namespace System.Drawing.Drawing2D
{
	public enum CompositingQuality
	{
		Invalid = -1,
		Default = 0,
		HighSpeed = 1,
		HighQuality = 2,
		GammaCorrected = 3,
		AssumeLinear = 4
	}
}
