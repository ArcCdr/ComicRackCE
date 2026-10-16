// M1.2a surface skeleton. Signatures follow the System.Drawing.Common reference assembly; members the Core uses, bodies filled in by M1.2b/M1.2c.
using System;

namespace System.Drawing.Imaging
{
	public sealed class Encoder
	{
		private readonly Guid guid;

		public Encoder(Guid guid)
		{
			this.guid = guid;
		}

		public Guid Guid => guid;

		public static readonly Encoder ColorDepth = new Encoder(new Guid("66087055-ad66-4c7c-9a18-38a2310b8337"));

		public static readonly Encoder Quality = new Encoder(new Guid("1d5be4b5-fa4a-452d-9cdd-5db35105e7eb"));
	}
}
