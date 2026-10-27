// M1.2c: a colour and a width only. The Core creates a Pen in SystemPensEx; nothing draws with one yet (no DrawLine and the like are declared).
using System;

namespace System.Drawing
{
	public sealed partial class Pen : MarshalByRefObject, ICloneable, IDisposable
	{
		private Color color;
		private float width;

		public Pen(Color color)
			: this(color, 1f)
		{
		}

		public Pen(Color color, float width)
		{
			this.color = color;
			this.width = width;
		}

		public Color Color
		{
			get => color;
			set => color = value;
		}

		public float Width
		{
			get => width;
			set => width = value;
		}

		public void Dispose()
		{
			Dispose(disposing: true);
			GC.SuppressFinalize(this);
		}

		private void Dispose(bool disposing)
		{
		}

		public object Clone() => new Pen(color, width);
	}
}
