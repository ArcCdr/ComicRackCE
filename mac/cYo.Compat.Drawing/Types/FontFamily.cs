// M1.2a surface skeleton. Signatures follow the System.Drawing.Common reference assembly; members the Core uses, bodies filled in by M1.2b/M1.2c.
using System;

namespace System.Drawing
{
	public sealed partial class FontFamily : MarshalByRefObject, IDisposable
	{
		internal FontFamily()
		{
		}

		public string Name
		{
			get => throw new NotSupportedException("System.Drawing.FontFamily.Name is not yet supported on macOS");
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
