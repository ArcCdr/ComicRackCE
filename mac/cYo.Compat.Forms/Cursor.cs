using System;
using System.Drawing;

namespace System.Windows.Forms
{
	public sealed partial class Cursor : IDisposable
	{
		private readonly IntPtr handle;

		public IntPtr Handle => handle;

		public Cursor(IntPtr handle)
		{
			this.handle = handle;
		}

		public Point HotSpot => throw new NotSupportedException("System.Windows.Forms.Cursor.HotSpot is not yet supported on macOS");

		public Size Size => throw new NotSupportedException("System.Windows.Forms.Cursor.Size is not yet supported on macOS");

		public void Draw(Graphics g, Rectangle targetRect)
		{
			throw new NotSupportedException("System.Windows.Forms.Cursor.Draw is not yet supported on macOS");
		}

		public void Dispose()
		{
		}

		public override bool Equals(object obj)
		{
			return obj is Cursor other && this == other;
		}

		public override int GetHashCode()
		{
			return handle.GetHashCode();
		}

		public static bool operator ==(Cursor left, Cursor right)
		{
			if ((object)left == null || (object)right == null)
			{
				return (object)left == (object)right;
			}
			return left.handle == right.handle;
		}

		public static bool operator !=(Cursor left, Cursor right)
		{
			return !(left == right);
		}
	}

	public partial class Screen
	{
		internal Screen()
		{
		}

		public static Screen PrimaryScreen => throw new NotSupportedException("System.Windows.Forms.Screen.PrimaryScreen is not yet supported on macOS");

		public int BitsPerPixel => throw new NotSupportedException("System.Windows.Forms.Screen.BitsPerPixel is not yet supported on macOS");
	}
}
