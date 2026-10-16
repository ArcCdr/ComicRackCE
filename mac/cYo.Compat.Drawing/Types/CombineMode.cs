// M1.2a surface skeleton. Signatures follow the System.Drawing.Common reference assembly; members the Core uses, bodies filled in by M1.2b/M1.2c.
using System;

namespace System.Drawing.Drawing2D
{
	public enum CombineMode
	{
		Replace = 0,
		Intersect = 1,
		Union = 2,
		Xor = 3,
		Exclude = 4,
		Complement = 5
	}
}
