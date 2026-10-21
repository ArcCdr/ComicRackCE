using System;
using System.Threading;
using SkiaSharp;

namespace ComicRack.Mac.Thumbnails
{
	/// <summary>
	/// A decoded bitmap plus a reference count: the one owner of the pixel memory behind every <see cref="SKImage"/> the
	/// thumbnail service hands out.
	///
	/// <b>Lifetime rule.</b> The cache holds one reference; every image made by <see cref="CreateImage"/> holds one more through
	/// its Skia release callback. The pixels are freed when the last reference is gone: after eviction, after the cache is
	/// cleared, or after the last image is disposed or garbage collected, whichever comes last. So evicting an entry never
	/// pulls the memory away from a cell that is still drawing it, and a consumer can dispose its image at any time without
	/// hurting the others (each consumer gets its own <see cref="SKImage"/> over the same pixels, with no copy).
	/// </summary>
	public sealed class PixelBlock
	{
		private static long liveCount;
		private static long liveBytes;
		private static volatile PixelBlockTracker tracker;

		private readonly PixelBlockTracker owner;
		private readonly SKBitmap bitmap;
		private readonly Action onFree;
		private readonly IntPtr pixels;
		private readonly int rowBytes;
		private readonly SKImageInfo viewInfo;
		private int refs = 1;
		private int pressured;

		/// <summary>Blocks that are not freed yet, in the whole process (diagnostics and tests).</summary>
		public static long LiveCount => Interlocked.Read(ref liveCount);

		/// <summary>Pixel bytes of the blocks that are not freed yet, in the whole process.</summary>
		public static long LiveBytes => Interlocked.Read(ref liveBytes);

		/// <summary>
		/// The tracker that counts the blocks made from now on (tests set one per test, so that a block of an earlier test that is
		/// still being released cannot disturb their counts); null in the app. Set it only from test code.
		/// </summary>
		public static PixelBlockTracker Tracker
		{
			get => tracker;
			set => tracker = value;
		}

		public int Width => viewInfo.Width;

		public int Height => viewInfo.Height;

		/// <summary>Row bytes times height: what this block counts against the cache bound.</summary>
		public long ByteCount { get; }

		/// <summary>The alpha type the images are made with (see <see cref="FromBitmap"/>).</summary>
		public SKAlphaType AlphaType => viewInfo.AlphaType;

		/// <summary>The address of the first pixel. Every image of this block reads from here (nothing is copied).</summary>
		public IntPtr PixelsAddress => pixels;

		/// <summary>Remaining references (diagnostics and tests).</summary>
		public int References => Volatile.Read(ref refs);

		public bool IsFreed => Volatile.Read(ref refs) <= 0;

		private PixelBlock(SKBitmap bitmap, SKAlphaType alpha, Action onFree)
		{
			this.bitmap = bitmap;
			this.onFree = onFree;
			pixels = bitmap.GetPixels();
			rowBytes = bitmap.RowBytes;
			viewInfo = new SKImageInfo(bitmap.Width, bitmap.Height, bitmap.ColorType, alpha);
			ByteCount = (long)rowBytes * bitmap.Height;
			Interlocked.Increment(ref liveCount);
			Interlocked.Add(ref liveBytes, ByteCount);
			owner = tracker;
			owner?.Add(1);
		}

		/// <summary>
		/// Takes ownership of <paramref name="bitmap"/> (it is disposed when the last reference goes) with a reference count of 1.
		/// <paramref name="onFree"/> runs after that, once (for example to dispose the owner of memory the bitmap only wraps).
		/// With <paramref name="detectOpaque"/> a bitmap that is not marked opaque but whose alpha bytes are all 255 is viewed as
		/// <see cref="SKAlphaType.Opaque"/> (the same memory, no copy): Skia then skips the premultiply or the blend on every draw.
		/// The Core's page bitmaps are usually <c>Format32bppArgb</c>, which the Drawing shim stores as Unpremul even when every
		/// pixel is opaque.
		/// </summary>
		public static PixelBlock FromBitmap(SKBitmap bitmap, Action onFree = null, bool detectOpaque = false)
		{
			if (bitmap == null)
			{
				throw new ArgumentNullException(nameof(bitmap));
			}
			if (bitmap.GetPixels() == IntPtr.Zero)
			{
				throw new ArgumentException("The bitmap has no pixels.", nameof(bitmap));
			}
			SKAlphaType alpha = bitmap.AlphaType;
			if (detectOpaque && alpha != SKAlphaType.Opaque && AllAlphaIs255(bitmap))
			{
				alpha = SKAlphaType.Opaque;
			}
			return new PixelBlock(bitmap, alpha, onFree);
		}

		/// <summary>
		/// A new image over the pixels of this block, with its own reference: dispose it when the view is done with it, or leave
		/// it to the garbage collector (the safety net). Throws <see cref="ObjectDisposedException"/> if the block was freed.
		/// </summary>
		public SKImage CreateImage()
		{
			Retain();
			var lease = new Lease(this);
			try
			{
				using (var pixmap = new SKPixmap(viewInfo, pixels, rowBytes))
				{
					SKImage image = SKImage.FromPixels(pixmap, static (address, context) => ((Lease)context).Free(), lease);
					if (image == null)
					{
						throw new InvalidOperationException("Skia could not wrap the pixels of the thumbnail.");
					}
					return image;
				}
			}
			catch
			{
				lease.Free();
				throw;
			}
		}

		/// <summary>Takes one more reference. Throws <see cref="ObjectDisposedException"/> if the block was freed.</summary>
		public void Retain()
		{
			while (true)
			{
				int current = Volatile.Read(ref refs);
				if (current <= 0)
				{
					throw new ObjectDisposedException(nameof(PixelBlock));
				}
				if (Interlocked.CompareExchange(ref refs, current + 1, current) == current)
				{
					return;
				}
			}
		}

		/// <summary>Gives one reference back; the last one frees the pixels.</summary>
		public void Release()
		{
			if (Interlocked.Decrement(ref refs) == 0)
			{
				Free();
			}
		}

		/// <summary>
		/// The cache gives up its reference. If images still use the pixels the block lives on without being counted anywhere, so
		/// tell the garbage collector about the memory: an image that a consumer forgot to dispose is then found and finalized
		/// sooner, instead of waiting for a collection that managed allocations alone would not trigger.
		/// </summary>
		public void ReleaseOwner()
		{
			if (Volatile.Read(ref refs) > 1 && Interlocked.Exchange(ref pressured, 1) == 0)
			{
				GC.AddMemoryPressure(Math.Max(1, ByteCount));
			}
			Release();
		}

		private void Free()
		{
			if (Interlocked.Exchange(ref pressured, 0) == 1)
			{
				GC.RemoveMemoryPressure(Math.Max(1, ByteCount));
			}
			try
			{
				bitmap.Dispose();
			}
			finally
			{
				Interlocked.Decrement(ref liveCount);
				Interlocked.Add(ref liveBytes, -ByteCount);
				owner?.Add(-1);
				try
				{
					onFree?.Invoke();
				}
				catch (Exception)
				{
				}
			}
		}

		/// <summary>One image's hold on the block; whatever calls <see cref="Free"/>, the reference is given back once.</summary>
		private sealed class Lease
		{
			private readonly PixelBlock block;
			private int freed;

			public Lease(PixelBlock block)
			{
				this.block = block;
			}

			public void Free()
			{
				if (Interlocked.Exchange(ref freed, 1) == 0)
				{
					block.Release();
				}
			}
		}

		/// <summary>True if every alpha byte of a 4-byte-per-pixel bitmap is 255 (a bitmap without alpha, or another layout, gives false).</summary>
		private static unsafe bool AllAlphaIs255(SKBitmap bitmap)
		{
			if (bitmap.BytesPerPixel != 4)
			{
				return false;
			}
			int alphaOffset;
			switch (bitmap.ColorType)
			{
			case SKColorType.Bgra8888:
			case SKColorType.Rgba8888:
				alphaOffset = 3;
				break;
			default:
				return false;
			}
			byte* row = (byte*)bitmap.GetPixels().ToPointer();
			int width = bitmap.Width;
			int stride = bitmap.RowBytes;
			for (int y = 0; y < bitmap.Height; y++)
			{
				// AND of the pixels' alpha bytes: it stays 255 only if none is lower.
				byte acc = 255;
				byte* p = row + alphaOffset;
				for (int x = 0; x < width; x++)
				{
					acc &= *p;
					p += 4;
				}
				if (acc != 255)
				{
					return false;
				}
				row += stride;
			}
			return true;
		}
	}
}
