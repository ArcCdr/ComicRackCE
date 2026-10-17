// M1.2b: real behaviour. Signatures follow the System.Drawing.Common reference assembly.
using System;
using cYo.Compat.Drawing.Internal;

namespace System.Drawing.Imaging
{
	public sealed class BitmapData
	{
		// The lock bookkeeping stays in internal fields: the public properties have setters (as in GDI+), and code that
		// writes them must not be able to confuse UnlockBits.
		internal Bitmap Owner;
		internal Rectangle Region;
		internal ImageLockMode Mode;
		internal PixKind Kind;
		internal byte[] Buffer;
		internal bool IsLocked;

		public BitmapData()
		{
		}

		public int Height { get; set; }

		public int Width { get; set; }

		public int Stride { get; set; }

		public PixelFormat PixelFormat { get; set; }

		public IntPtr Scan0 { get; set; }

		public int Reserved { get; set; }
	}
}
