// M1.2c: real behaviour. Signatures follow the System.Drawing.Common reference assembly.
using System;

namespace System.Drawing
{
	public sealed partial class SolidBrush : Brush
	{
		private Color color;
		private readonly bool immutable;

		public SolidBrush(Color color)
		{
			this.color = color;
		}

		/// <summary>A read-only brush such as Brushes.Black: the colour cannot change and Dispose does nothing.</summary>
		internal SolidBrush(Color color, bool immutable)
		{
			this.color = color;
			this.immutable = immutable;
		}

		public Color Color
		{
			get => color;
			set
			{
				if (immutable)
				{
					throw new ArgumentException("Setting a read-only property is not allowed.");
				}
				color = value;
			}
		}

		public override object Clone() => new SolidBrush(color);
	}
}
