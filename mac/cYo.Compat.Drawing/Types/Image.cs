// M1.2a surface skeleton. Signatures follow the System.Drawing.Common reference assembly; members the Core uses, bodies filled in by M1.2b/M1.2c.
using System;

namespace System.Drawing
{
	public abstract class Image : MarshalByRefObject, IDisposable, ICloneable
	{
		internal Image()
		{
		}

		public object Clone() => throw new NotSupportedException("System.Drawing.Image.Clone is not yet supported on macOS");

		public int Flags
		{
			get => throw new NotSupportedException("System.Drawing.Image.Flags is not yet supported on macOS");
		}

		public static Image FromFile(string filename) => throw new NotSupportedException("System.Drawing.Image.FromFile is not yet supported on macOS");

		public static Image FromStream(System.IO.Stream stream) => throw new NotSupportedException("System.Drawing.Image.FromStream is not yet supported on macOS");

		public static Image FromStream(System.IO.Stream stream, bool useEmbeddedColorManagement, bool validateImageData) => throw new NotSupportedException("System.Drawing.Image.FromStream is not yet supported on macOS");

		public static Image FromStream(System.IO.Stream stream, bool useEmbeddedColorManagement) => throw new NotSupportedException("System.Drawing.Image.FromStream is not yet supported on macOS");

		public static int GetPixelFormatSize(System.Drawing.Imaging.PixelFormat pixfmt) => throw new NotSupportedException("System.Drawing.Image.GetPixelFormatSize is not yet supported on macOS");

		public int Height
		{
			get => throw new NotSupportedException("System.Drawing.Image.Height is not yet supported on macOS");
		}

		public System.Drawing.Imaging.PixelFormat PixelFormat
		{
			get => throw new NotSupportedException("System.Drawing.Image.PixelFormat is not yet supported on macOS");
		}

		public void RotateFlip(RotateFlipType rotateFlipType) => throw new NotSupportedException("System.Drawing.Image.RotateFlip is not yet supported on macOS");

		public void Save(string filename, System.Drawing.Imaging.ImageFormat format) => throw new NotSupportedException("System.Drawing.Image.Save is not yet supported on macOS");

		public void Save(System.IO.Stream stream, System.Drawing.Imaging.ImageFormat format) => throw new NotSupportedException("System.Drawing.Image.Save is not yet supported on macOS");

		public void Save(System.IO.Stream stream, System.Drawing.Imaging.ImageCodecInfo encoder, System.Drawing.Imaging.EncoderParameters encoderParams) => throw new NotSupportedException("System.Drawing.Image.Save is not yet supported on macOS");

		public Size Size
		{
			get => throw new NotSupportedException("System.Drawing.Image.Size is not yet supported on macOS");
		}

		public int Width
		{
			get => throw new NotSupportedException("System.Drawing.Image.Width is not yet supported on macOS");
		}

		public void Dispose()
		{
			Dispose(disposing: true);
			GC.SuppressFinalize(this);
		}

		protected virtual void Dispose(bool disposing)
		{
		}
	}
}
