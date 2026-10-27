// M1.2a surface skeleton. Signatures follow the System.Drawing.Common reference assembly; members the Core uses, bodies filled in by M1.2b/M1.2c.
using System;

namespace System.Drawing
{
	public sealed partial class StringFormat : MarshalByRefObject, ICloneable, IDisposable
	{
		public StringFormat()
		{
			throw new NotSupportedException("System.Drawing.StringFormat.ctor is not yet supported on macOS");
		}

		public StringFormat(StringFormatFlags options)
		{
			throw new NotSupportedException("System.Drawing.StringFormat.ctor is not yet supported on macOS");
		}

		public StringAlignment Alignment
		{
			get => throw new NotSupportedException("System.Drawing.StringFormat.Alignment is not yet supported on macOS");
			set => throw new NotSupportedException("System.Drawing.StringFormat.Alignment is not yet supported on macOS");
		}

		public StringFormatFlags FormatFlags
		{
			get => throw new NotSupportedException("System.Drawing.StringFormat.FormatFlags is not yet supported on macOS");
			set => throw new NotSupportedException("System.Drawing.StringFormat.FormatFlags is not yet supported on macOS");
		}

		public StringAlignment LineAlignment
		{
			get => throw new NotSupportedException("System.Drawing.StringFormat.LineAlignment is not yet supported on macOS");
			set => throw new NotSupportedException("System.Drawing.StringFormat.LineAlignment is not yet supported on macOS");
		}

		public void SetTabStops(float firstTabOffset, float[] tabStops) => throw new NotSupportedException("System.Drawing.StringFormat.SetTabStops is not yet supported on macOS");

		public StringTrimming Trimming
		{
			get => throw new NotSupportedException("System.Drawing.StringFormat.Trimming is not yet supported on macOS");
			set => throw new NotSupportedException("System.Drawing.StringFormat.Trimming is not yet supported on macOS");
		}

		public void Dispose()
		{
			Dispose(disposing: true);
			GC.SuppressFinalize(this);
		}

		private void Dispose(bool disposing)
		{
		}

		public object Clone() => throw new NotSupportedException("System.Drawing.StringFormat.Clone is not yet supported on macOS");
	}
}
