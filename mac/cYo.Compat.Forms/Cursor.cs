using System;
using System.Drawing;

namespace System.Windows.Forms
{
	public sealed class Cursor : IDisposable
	{
		private readonly IntPtr handle;

		public IntPtr Handle => handle;

		public Cursor(IntPtr handle)
		{
			this.handle = handle;
		}

		public Point HotSpot => throw new NotSupportedException("System.Windows.Forms.Cursor.HotSpot is not yet supported on macOS");

		public Size Size => throw new NotSupportedException("System.Windows.Forms.Cursor.Size is not yet supported on macOS");

		// Cursor.Draw(Graphics, Rectangle) is added by M1.2a, when System.Drawing.Graphics exists (see mvp-track.md section 9).

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

	public class Screen
	{
		internal Screen()
		{
		}

		public static Screen PrimaryScreen => throw new NotSupportedException("System.Windows.Forms.Screen.PrimaryScreen is not yet supported on macOS");

		public int BitsPerPixel => throw new NotSupportedException("System.Windows.Forms.Screen.BitsPerPixel is not yet supported on macOS");
	}
}
