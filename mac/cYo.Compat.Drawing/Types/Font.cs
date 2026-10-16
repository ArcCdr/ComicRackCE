// M1.2a surface skeleton. Signatures follow the System.Drawing.Common reference assembly; members the Core uses, bodies filled in by M1.2b/M1.2c.
using System;

namespace System.Drawing
{
	public sealed class Font : MarshalByRefObject, ICloneable, IDisposable
	{
		public Font(string familyName, float emSize, FontStyle style)
		{
			throw new NotSupportedException("System.Drawing.Font.ctor is not yet supported on macOS");
		}

		public FontFamily FontFamily
		{
			get => throw new NotSupportedException("System.Drawing.Font.FontFamily is not yet supported on macOS");
		}

		public bool IsSystemFont
		{
			get => throw new NotSupportedException("System.Drawing.Font.IsSystemFont is not yet supported on macOS");
		}

		public float Size
		{
			get => throw new NotSupportedException("System.Drawing.Font.Size is not yet supported on macOS");
		}

		public FontStyle Style
		{
			get => throw new NotSupportedException("System.Drawing.Font.Style is not yet supported on macOS");
		}

		public IntPtr ToHfont() => throw new NotSupportedException("System.Drawing.Font.ToHfont is not yet supported on macOS");

		public void Dispose()
		{
			Dispose(disposing: true);
			GC.SuppressFinalize(this);
		}

		private void Dispose(bool disposing)
		{
		}

		public object Clone() => throw new NotSupportedException("System.Drawing.Font.Clone is not yet supported on macOS");
	}
}
