// M1.2a surface skeleton. Signatures follow the System.Drawing.Common reference assembly; members the Core uses, bodies filled in by M1.2b/M1.2c.
using System;

namespace System.Drawing.Imaging
{
	public enum ImageLockMode
	{
		ReadOnly = 1,
		WriteOnly = 2,
		ReadWrite = 3,
		UserInputBuffer = 4
	}
}
