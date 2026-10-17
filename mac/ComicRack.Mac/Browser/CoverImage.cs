using System;
using System.Threading;
using SkiaSharp;

namespace ComicRack.Mac.Browser
{
	/// <summary>
	/// A cover picture shared between a cell and the draw operations that are queued for the render thread. The cell cannot dispose
	/// its <see cref="SKImage"/> when it is recycled while a frame that draws it is still pending, so the picture is reference counted:
	/// the cell holds one reference, every draw operation takes one for its lifetime, and the image is disposed with the last one
	/// (which releases the thumbnail service's pixels, see <c>PixelBlock</c>). Taking a reference is an increment; nothing is copied.
	/// </summary>
	public sealed class CoverImage
	{
		private SKImage image;
		private int references = 1;

		/// <summary>Takes over <paramref name="image"/> (the caller's one reference becomes this object's first).</summary>
		public CoverImage(SKImage image)
		{
			this.image = image ?? throw new ArgumentNullException(nameof(image));
			Width = image.Width;
			Height = image.Height;
		}

		public int Width { get; }

		public int Height { get; }

		/// <summary>References held right now (for tests).</summary>
		public int References => Volatile.Read(ref references);

		/// <summary>The image; only valid while the caller holds a reference.</summary>
		public SKImage Image => image;

		/// <summary>Adds a reference. The caller must release it.</summary>
		public CoverImage Retain()
		{
			int n;
			do
			{
				n = Volatile.Read(ref references);
				if (n <= 0)
				{
					throw new ObjectDisposedException(nameof(CoverImage));
				}
			}
			while (Interlocked.CompareExchange(ref references, n + 1, n) != n);
			return this;
		}

		public void Release()
		{
			if (Interlocked.Decrement(ref references) == 0)
			{
				SKImage i = image;
				image = null;
				i?.Dispose();
			}
		}
	}
}
