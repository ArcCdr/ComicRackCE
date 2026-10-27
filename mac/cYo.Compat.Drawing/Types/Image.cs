// M1.2b: real behaviour, backed by one SKBitmap per image (4 bytes per pixel, BGRA). Signatures follow the System.Drawing.Common reference assembly.
using System;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using cYo.Compat.Drawing.Internal;
using SkiaSharp;

namespace System.Drawing
{
	public abstract partial class Image : MarshalByRefObject, IDisposable, ICloneable
	{
		// The storage. The pixel pointer and the geometry are cached because the Core reads Width/Height in hot loops
		// and the SKBitmap properties are P/Invoke calls. The pixel memory never moves for the life of the SKBitmap.
		private SKBitmap sk;
		private IntPtr pixels;
		private int rowBytes;
		private int width;
		private int height;
		private PixelFormat format;
		private long memoryPressure;

		private static int liveStorage;
		private static volatile ImageStorageTracker storageTracker;
		private ImageStorageTracker storageOwner;

		internal Image()
		{
		}

		/// <summary>Number of SKBitmaps currently owned by images (tests use it to check that Dispose releases them).</summary>
		internal static int LiveStorageCount => Volatile.Read(ref liveStorage);

		/// <summary>The tracker that counts the storages made from now on (tests set one per test); null in the app.</summary>
		internal static ImageStorageTracker StorageTracker
		{
			get => storageTracker;
			set => storageTracker = value;
		}

		/// <summary>The backing bitmap; throws once the image is disposed. Owned by this image: callers must not dispose it.</summary>
		internal SKBitmap Storage => sk ?? throw new ObjectDisposedException(GetType().Name);

		internal bool IsDisposed => Volatile.Read(ref sk) == null;

		/// <summary>Address of the first pixel of the backing bitmap.</summary>
		internal IntPtr PixelsAddress
		{
			get
			{
				EnsureAlive();
				return pixels;
			}
		}

		/// <summary>Bytes per row of the backing bitmap (not necessarily width * 4).</summary>
		internal int RowBytes
		{
			get
			{
				EnsureAlive();
				return rowBytes;
			}
		}

		/// <summary>The layout of the backing bitmap: Argb, PArgb or Rgb32 (24 bpp is stored as opaque BGRA).</summary>
		internal PixKind StorageKind => PixelKinds.StorageKind(PixelKinds.GetKind(format, "Image"));

		/// <summary>True while a LockBits region is outstanding (only <see cref="Bitmap"/> locks).</summary>
		internal virtual bool HasOutstandingLocks => false;

		/// <summary>Takes ownership of <paramref name="bitmap"/> (BGRA8888 with the alpha type that matches <paramref name="pixelFormat"/>).</summary>
		internal void AttachStorage(SKBitmap bitmap, PixelFormat pixelFormat)
		{
			if (bitmap.ColorType != SKColorType.Bgra8888 || bitmap.AlphaType != PixelKinds.AlphaType(PixelKinds.StorageKind(PixelKinds.GetKind(pixelFormat, "Image"))))
			{
				bitmap.Dispose();
				throw new InvalidOperationException("The backing bitmap does not match the pixel format");
			}
			sk = bitmap;
			format = pixelFormat;
			SyncGeometry();
			memoryPressure = Math.Max(1, bitmap.ByteCount);
			GC.AddMemoryPressure(memoryPressure);
			Interlocked.Increment(ref liveStorage);
			storageOwner = storageTracker;
			storageOwner?.Add(1);
		}

		/// <summary>Replaces the backing bitmap (RotateFlip) and disposes the old one. The byte count must not change.</summary>
		internal void ReplaceStorage(SKBitmap bitmap)
		{
			SKBitmap old = sk;
			sk = bitmap;
			SyncGeometry();
			old?.Dispose();
		}

		private void SyncGeometry()
		{
			width = sk.Width;
			height = sk.Height;
			rowBytes = sk.RowBytes;
			pixels = sk.GetPixels();
		}

		internal void EnsureAlive()
		{
			if (Volatile.Read(ref sk) == null)
			{
				throw new ObjectDisposedException(GetType().Name);
			}
		}

		public object Clone()
		{
			EnsureAlive();
			return Bitmap.CopyOf(this, new Rectangle(0, 0, width, height), format);
		}

		public int Flags
		{
			get
			{
				EnsureAlive();
				// Same bits as GDI+ for a bitmap in a canonical RGB colour space; HasTranslucent is not computed (it would need a pixel scan).
				return (int)(ImageFlags.ColorSpaceRgb | (PixelKinds.HasAlpha(format) ? ImageFlags.HasAlpha : ImageFlags.None));
			}
		}

		public static Image FromFile(string filename)
		{
			if (filename == null)
			{
				throw new ArgumentNullException(nameof(filename));
			}
			return ImageCodec.Decode(File.ReadAllBytes(filename));
		}

		public static Image FromStream(System.IO.Stream stream) => ImageCodec.Decode(stream);

		/// <summary>Colour management and data validation flags are accepted and ignored: the image is always decoded completely, without colour conversion.</summary>
		public static Image FromStream(System.IO.Stream stream, bool useEmbeddedColorManagement, bool validateImageData) => ImageCodec.Decode(stream);

		public static Image FromStream(System.IO.Stream stream, bool useEmbeddedColorManagement) => ImageCodec.Decode(stream);

		/// <summary>Bits per pixel, from the size byte that every <see cref="PixelFormat"/> value carries (as in GDI+).</summary>
		public static int GetPixelFormatSize(System.Drawing.Imaging.PixelFormat pixfmt) => ((int)pixfmt >> 8) & 0xFF;

		public int Height
		{
			get
			{
				EnsureAlive();
				return height;
			}
		}

		public System.Drawing.Imaging.PixelFormat PixelFormat
		{
			get
			{
				EnsureAlive();
				return format;
			}
		}

		/// <summary>Rotates clockwise, then flips horizontally (the order GDI+ documents), in place. Not allowed while a LockBits region is outstanding.</summary>
		public void RotateFlip(RotateFlipType rotateFlipType)
		{
			EnsureAlive();
			if (HasOutstandingLocks)
			{
				throw new InvalidOperationException("Bitmap region is already locked.");
			}
			int value = (int)rotateFlipType;
			if (value < 0 || value > 7)
			{
				throw new ArgumentException("Parameter is not valid.", nameof(rotateFlipType));
			}
			SKBitmap rotated = PixelTransform.RotateFlip(sk, value & 3, (value & 4) != 0);
			if (rotated != null)
			{
				ReplaceStorage(rotated);
			}
		}

		public void Save(string filename, System.Drawing.Imaging.ImageFormat format)
		{
			if (filename == null)
			{
				throw new ArgumentNullException(nameof(filename));
			}
			using (FileStream stream = File.Create(filename))
			{
				Save(stream, format);
			}
		}

		public void Save(System.IO.Stream stream, System.Drawing.Imaging.ImageFormat format)
		{
			if (format == null)
			{
				throw new ArgumentNullException(nameof(format));
			}
			EnsureAlive();
			ImageCodec.Encode(this, stream, format.Guid, ImageCodec.DefaultJpegQuality);
		}

		/// <summary>
		/// JPEG honours <see cref="Encoder.Quality"/> (0 to 100). <see cref="Encoder.ColorDepth"/> is accepted and ignored. Only JPEG and PNG encoders exist.
		/// </summary>
		public void Save(System.IO.Stream stream, System.Drawing.Imaging.ImageCodecInfo encoder, System.Drawing.Imaging.EncoderParameters encoderParams)
		{
			if (encoder == null)
			{
				throw new ArgumentNullException(nameof(encoder));
			}
			EnsureAlive();
			int quality = ImageCodec.DefaultJpegQuality;
			if (encoderParams?.Param != null)
			{
				foreach (EncoderParameter parameter in encoderParams.Param)
				{
					if (parameter != null && parameter.Encoder != null && parameter.Encoder.Guid == Encoder.Quality.Guid)
					{
						quality = (int)Math.Max(0, Math.Min(100, parameter.Value));
					}
				}
			}
			ImageCodec.Encode(this, stream, encoder.FormatID, quality);
		}

		public Size Size
		{
			get
			{
				EnsureAlive();
				return new Size(width, height);
			}
		}

		public int Width
		{
			get
			{
				EnsureAlive();
				return width;
			}
		}

		public void Dispose()
		{
			Dispose(disposing: true);
			GC.SuppressFinalize(this);
		}

		protected virtual void Dispose(bool disposing)
		{
			SKBitmap old = Interlocked.Exchange(ref sk, null);
			if (old != null)
			{
				old.Dispose();
				GC.RemoveMemoryPressure(memoryPressure);
				Interlocked.Decrement(ref liveStorage);
				storageOwner?.Add(-1);
			}
		}

		~Image()
		{
			Dispose(disposing: false);
		}
	}
}
