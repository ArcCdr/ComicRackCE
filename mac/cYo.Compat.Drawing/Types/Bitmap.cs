// M1.2b: real behaviour. Signatures follow the System.Drawing.Common reference assembly.
// Pixel storage: one SKBitmap, Bgra8888, with the alpha type of the pixel format:
//   Format32bppArgb -> Unpremul, Format32bppPArgb -> Premul, Format32bppRgb and Format24bppRgb -> Opaque.
// Format24bppRgb has no 3-byte storage: LockBits(Format24bppRgb) converts into a pinned BGR buffer (stride rounded up to 4) and
// UnlockBits converts it back. Other pixel formats (indexed, 16, 48 and 64 bpp) are not supported.
using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using cYo.Compat.Drawing.Internal;
using SkiaSharp;

namespace System.Drawing
{
	public sealed partial class Bitmap : Image
	{
		private readonly object lockSync = new object();
		private readonly List<BitmapData> locks = new List<BitmapData>();

		private Bitmap()
		{
		}

		public Bitmap(int width, int height, System.Drawing.Imaging.PixelFormat format)
		{
			AttachStorage(AllocateStorage(width, height, format), format);
		}

		public Bitmap(int width, int height)
			: this(width, height, PixelFormat.Format32bppArgb)
		{
		}

		/// <summary>
		/// A new Format32bppArgb bitmap with <paramref name="original"/> scaled into it, as in .NET: cleared to transparent, then drawn with the default
		/// Graphics settings (bilinear interpolation, so a large reduction aliases; use Graphics with HighQualityBicubic for a better one).
		/// </summary>
		public Bitmap(Image original, int width, int height)
			: this(width, height, PixelFormat.Format32bppArgb)
		{
			try
			{
				if (original == null)
				{
					throw new ArgumentNullException(nameof(original));
				}
				using (Graphics graphics = Graphics.FromImage(this))
				{
					graphics.Clear(Color.Transparent);
					graphics.DrawImage(original, 0, 0, width, height);
				}
			}
			catch
			{
				Dispose();
				throw;
			}
		}

		/// <summary>Wraps a decoded bitmap (ownership passes to the new image).</summary>
		internal static Bitmap FromStorage(SKBitmap storage, PixelFormat format)
		{
			Bitmap bitmap = new Bitmap();
			bitmap.AttachStorage(storage, format);
			return bitmap;
		}

		internal override bool HasOutstandingLocks
		{
			get
			{
				lock (lockSync)
				{
					return locks.Count > 0;
				}
			}
		}

		/// <summary>A new, zero-filled bitmap (transparent black for alpha formats, opaque black for the others).</summary>
		private static SKBitmap AllocateStorage(int width, int height, PixelFormat format)
		{
			PixKind kind = PixelKinds.GetKind(format, "Bitmap");
			if (width <= 0 || height <= 0)
			{
				throw new ArgumentException("Parameter is not valid.");
			}
			SKBitmap storage = new SKBitmap();
			if (!storage.TryAllocPixels(new SKImageInfo(width, height, SKColorType.Bgra8888, PixelKinds.AlphaType(PixelKinds.StorageKind(kind)))))
			{
				storage.Dispose();
				throw new OutOfMemoryException($"Not enough memory for a {width} x {height} bitmap");
			}
			// Skia does not clear new pixel memory.
			storage.Erase(PixelKinds.StorageKind(kind) == PixKind.Rgb32 ? SKColors.Black : SKColors.Transparent);
			return storage;
		}

		/// <summary>Copies a region of <paramref name="source"/> into a new bitmap of the given format (pixels are converted).</summary>
		internal static unsafe Bitmap CopyOf(Image source, Rectangle region, PixelFormat format)
		{
			SKBitmap storage = AllocateStorage(region.Width, region.Height, format);
			try
			{
				PixKind from = source.StorageKind;
				PixKind to = PixelKinds.StorageKind(PixelKinds.GetKind(format, "Bitmap"));
				IntPtr start = source.PixelsAddress + region.Y * source.RowBytes + region.X * 4;
				PixelConvert.Convert(start, source.RowBytes, from, storage.GetPixels(), storage.RowBytes, to, region.Width, region.Height);
				GC.KeepAlive(source); // the raw pointer must not outlive the image (its finalizer frees the pixels)
				return FromStorage(storage, format);
			}
			catch
			{
				storage.Dispose();
				throw;
			}
		}

		/// <summary>As GDI+: a region outside the bitmap is reported as OutOfMemoryException, an empty one as ArgumentException.</summary>
		public Bitmap Clone(Rectangle rect, System.Drawing.Imaging.PixelFormat format)
		{
			EnsureAlive();
			if (rect.Width <= 0 || rect.Height <= 0)
			{
				throw new ArgumentException($"Rectangle '{rect}' is not valid.");
			}
			if (rect.X < 0 || rect.Y < 0 || rect.Right > Width || rect.Bottom > Height)
			{
				throw new OutOfMemoryException("Out of memory.");
			}
			return CopyOf(this, rect, format == PixelFormat.DontCare ? PixelFormat : format);
		}

		public IntPtr GetHbitmap() => throw new NotSupportedException("System.Drawing.Bitmap.GetHbitmap is not yet supported on macOS");

		public IntPtr GetHicon() => throw new NotSupportedException("System.Drawing.Bitmap.GetHicon is not yet supported on macOS");

		/// <summary>The colour as GDI+ reports it: not premultiplied, alpha 255 for formats without alpha.</summary>
		public unsafe Color GetPixel(int x, int y)
		{
			EnsureAlive();
			CheckPoint(x, y);
			uint value = *(uint*)(PixelsAddress + y * RowBytes + x * 4);
			GC.KeepAlive(this);
			switch (StorageKind)
			{
				case PixKind.PArgb:
					value = PixelConvert.Unpremultiply(value);
					break;
				case PixKind.Rgb32:
					value |= 0xFF000000u;
					break;
			}
			return Color.FromArgb(unchecked((int)value));
		}

		/// <summary>Alpha is dropped for formats without alpha and the colour is premultiplied for Format32bppPArgb. Not allowed while a region is locked.</summary>
		public unsafe void SetPixel(int x, int y, Color color)
		{
			EnsureAlive();
			CheckPoint(x, y);
			if (HasOutstandingLocks)
			{
				throw new InvalidOperationException("Bitmap region is already locked.");
			}
			uint value = unchecked((uint)color.ToArgb());
			switch (StorageKind)
			{
				case PixKind.PArgb:
					value = PixelConvert.Premultiply(value);
					break;
				case PixKind.Rgb32:
					value |= 0xFF000000u;
					break;
			}
			*(uint*)(PixelsAddress + y * RowBytes + x * 4) = value;
			Storage.NotifyPixelsChanged();
			GC.KeepAlive(this);
		}

		private void CheckPoint(int x, int y)
		{
			if (x < 0 || x >= Width)
			{
				throw new ArgumentOutOfRangeException(nameof(x), "Parameter must be positive and < Width.");
			}
			if (y < 0 || y >= Height)
			{
				throw new ArgumentOutOfRangeException(nameof(y), "Parameter must be positive and < Height.");
			}
		}

		/// <summary>
		/// A request in the layout the bitmap is stored in is zero-copy: Scan0 points into the bitmap, Stride is the bitmap's own.
		/// Any other format is converted into a separate pinned buffer: filled from the bitmap unless the mode is WriteOnly (then zeroed),
		/// and written back by <see cref="UnlockBits"/> unless the mode is ReadOnly. A region may not overlap another outstanding region (InvalidOperationException).
		/// ImageLockMode.UserInputBuffer is not supported.
		/// </summary>
		public unsafe System.Drawing.Imaging.BitmapData LockBits(Rectangle rect, System.Drawing.Imaging.ImageLockMode flags, System.Drawing.Imaging.PixelFormat format)
		{
			EnsureAlive();
			if ((flags & ImageLockMode.UserInputBuffer) != 0)
			{
				throw new NotSupportedException("System.Drawing.Bitmap.LockBits(ImageLockMode.UserInputBuffer) is not yet supported on macOS");
			}
			if ((flags & ImageLockMode.ReadWrite) == 0)
			{
				throw new ArgumentException("Parameter is not valid.", nameof(flags));
			}
			if (format == PixelFormat.DontCare)
			{
				format = PixelFormat;
			}
			PixKind kind = PixelKinds.GetKind(format, "Bitmap.LockBits");
			if (rect.Width <= 0 || rect.Height <= 0 || rect.X < 0 || rect.Y < 0 || rect.Right > Width || rect.Bottom > Height)
			{
				throw new ArgumentException("Parameter is not valid.", nameof(rect));
			}

			BitmapData data = new BitmapData
			{
				Owner = this,
				Region = rect,
				Mode = flags,
				Kind = kind,
				Width = rect.Width,
				Height = rect.Height,
				PixelFormat = format,
			};
			lock (lockSync)
			{
				foreach (BitmapData other in locks)
				{
					if (other.Region.IntersectsWith(rect))
					{
						throw new InvalidOperationException("Bitmap region is already locked.");
					}
				}
				data.IsLocked = true;
				locks.Add(data);
			}
			try
			{
				PixKind storage = StorageKind;
				IntPtr regionStart = PixelsAddress + rect.Y * RowBytes + rect.X * 4;
				if (kind == storage)
				{
					data.Scan0 = regionStart;
					data.Stride = RowBytes;
				}
				else
				{
					int stride = PixelKinds.Stride(kind, rect.Width);
					long size = (long)stride * rect.Height;
					if (size > int.MaxValue)
					{
						throw new OutOfMemoryException($"Not enough memory for a locked {rect.Width} x {rect.Height} region");
					}
					// A pinned (POH) array: the pointer stays valid until the BitmapData is unreachable, and there is nothing to free by hand.
					byte[] buffer = GC.AllocateArray<byte>((int)size, pinned: true);
					if ((flags & ImageLockMode.ReadOnly) != 0)
					{
						PixelConvert.Convert(regionStart, RowBytes, storage, (IntPtr)Unsafe.AsPointer(ref MemoryMarshal.GetArrayDataReference(buffer)), stride, kind, rect.Width, rect.Height);
					}
					data.Buffer = buffer;
					data.Scan0 = (IntPtr)Unsafe.AsPointer(ref MemoryMarshal.GetArrayDataReference(buffer));
					data.Stride = stride;
				}
				return data;
			}
			catch
			{
				lock (lockSync)
				{
					data.IsLocked = false;
					locks.Remove(data);
				}
				throw;
			}
		}

		/// <summary>Writes a converted buffer back (unless the lock was ReadOnly) and ends the lock. A region that was not locked by this bitmap is an InvalidOperationException.</summary>
		public unsafe void UnlockBits(System.Drawing.Imaging.BitmapData bitmapdata)
		{
			if (bitmapdata == null)
			{
				throw new ArgumentNullException(nameof(bitmapdata));
			}
			lock (lockSync)
			{
				if (bitmapdata.Owner != this || !bitmapdata.IsLocked || !locks.Remove(bitmapdata))
				{
					throw new InvalidOperationException("Bitmap region is not locked.");
				}
				bitmapdata.IsLocked = false;
			}
			if (IsDisposed || (bitmapdata.Mode & ImageLockMode.WriteOnly) == 0)
			{
				return;
			}
			Rectangle region = bitmapdata.Region;
			PixKind storage = StorageKind;
			IntPtr regionStart = PixelsAddress + region.Y * RowBytes + region.X * 4;
			if (bitmapdata.Buffer != null)
			{
				PixelConvert.Convert((IntPtr)Unsafe.AsPointer(ref MemoryMarshal.GetArrayDataReference(bitmapdata.Buffer)), bitmapdata.Stride, bitmapdata.Kind, regionStart, RowBytes, storage, region.Width, region.Height);
				GC.KeepAlive(bitmapdata.Buffer); // a pinned array can still be collected once nothing refers to it
			}
			else if (storage == PixKind.Rgb32)
			{
				// Zero-copy write into an opaque bitmap: the caller owns the alpha byte, the bitmap must not keep a value other than 255.
				PixelConvert.Convert(regionStart, RowBytes, PixKind.Rgb32, regionStart, RowBytes, PixKind.Rgb32, region.Width, region.Height);
			}
			Storage.NotifyPixelsChanged();
			GC.KeepAlive(this);
		}

		protected override void Dispose(bool disposing)
		{
			lock (lockSync)
			{
				// Outstanding BitmapData objects keep their own buffers; UnlockBits on them fails with "not locked" instead of writing into freed pixels.
				foreach (BitmapData data in locks)
				{
					data.IsLocked = false;
				}
				locks.Clear();
			}
			base.Dispose(disposing);
		}
	}
}
