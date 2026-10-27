// M1.2a surface skeleton. Signatures follow the System.Drawing.Common reference assembly; members the Core uses, bodies filled in by M1.2b/M1.2c.
using System;

namespace System.Drawing
{
	public sealed partial class Icon : MarshalByRefObject, ICloneable, IDisposable
	{
		public Icon(System.IO.Stream stream, int width, int height)
		{
			throw new NotSupportedException("System.Drawing.Icon.ctor is not yet supported on macOS");
		}

		public object Clone() => throw new NotSupportedException("System.Drawing.Icon.Clone is not yet supported on macOS");

		public static Icon FromHandle(IntPtr handle) => throw new NotSupportedException("System.Drawing.Icon.FromHandle is not yet supported on macOS");

		public int Height
		{
			get => throw new NotSupportedException("System.Drawing.Icon.Height is not yet supported on macOS");
		}

		public Bitmap ToBitmap() => throw new NotSupportedException("System.Drawing.Icon.ToBitmap is not yet supported on macOS");

		public int Width
		{
			get => throw new NotSupportedException("System.Drawing.Icon.Width is not yet supported on macOS");
		}

		public void Dispose()
		{
			Dispose(disposing: true);
			GC.SuppressFinalize(this);
		}

		private void Dispose(bool disposing)
		{
		}
	}
}
