// M1.2b: real behaviour.
using System;

namespace System.Drawing.Imaging
{
	public sealed partial class EncoderParameters : IDisposable
	{
		public EncoderParameters(int count)
		{
			Param = new EncoderParameter[count];
		}

		public EncoderParameter[] Param { get; set; }

		public void Dispose()
		{
			Dispose(disposing: true);
			GC.SuppressFinalize(this);
		}

		private void Dispose(bool disposing)
		{
			// As in GDI+, disposing the collection disposes its parameters.
			if (disposing && Param != null)
			{
				foreach (EncoderParameter parameter in Param)
				{
					parameter?.Dispose();
				}
			}
		}
	}
}
