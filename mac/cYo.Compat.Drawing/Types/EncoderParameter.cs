// M1.2b: real behaviour for the (Encoder, long) constructor the Core uses; the other GDI+ overloads are not declared.
using System;

namespace System.Drawing.Imaging
{
	public sealed class EncoderParameter : IDisposable
	{
		public EncoderParameter(Encoder encoder, long value)
		{
			Encoder = encoder;
			Value = value;
		}

		public Encoder Encoder { get; set; }

		public int NumberOfValues => 1;

		internal long Value { get; }

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
