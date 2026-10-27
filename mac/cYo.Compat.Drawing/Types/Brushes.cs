// M1.2c: Black only, the one static brush the Core uses (ImagePool.CreateErrorPage). The other ~140 named brushes are not declared.
using System;

namespace System.Drawing
{
	public static partial class Brushes
	{
		private static readonly SolidBrush black = new SolidBrush(Color.Black, immutable: true);

		public static Brush Black => black;
	}
}
