// M1.2c: the Graphics subset, drawing straight onto the backing SKBitmap of a Bitmap (see Image.Storage and the format notes in Bitmap.cs).
// Implemented: FromImage, Dispose, Clear, FillRectangle (SolidBrush), DrawImage (all overloads the Core uses), InterpolationMode, SmoothingMode,
// CompositingQuality, CompositingMode, PixelOffsetMode (stored, no effect), TranslateTransform, ScaleTransform, ResetTransform.
// Everything else (text, clip, Save/Restore, paths, ellipses, icons, device contexts) still throws NotSupportedException.
// Semantics that differ from GDI+ on purpose are listed in mac/plans/mvp-track.md section 3, "Facts from M1.2c".
using System;
using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using cYo.Compat.Drawing.Internal;
using SkiaSharp;

namespace System.Drawing
{
	public sealed class Graphics : MarshalByRefObject, IDisposable, IDeviceContext
	{
		private Image image;
		private SKCanvas canvas;
		private SKBitmap canvasStorage; // the bitmap the canvas draws on; RotateFlip replaces the storage of an image
		private SKMatrix world = SKMatrix.Identity;
		private InterpolationMode interpolationMode = InterpolationMode.Bilinear; // the GDI+ default of a new Graphics
		private SmoothingMode smoothingMode = SmoothingMode.None;
		private CompositingQuality compositingQuality = CompositingQuality.Default;
		private CompositingMode compositingMode = CompositingMode.SourceOver;
		private PixelOffsetMode pixelOffsetMode = PixelOffsetMode.Default;

		private Graphics(Image image)
		{
			this.image = image;
			canvasStorage = image.Storage;
			canvas = new SKCanvas(canvasStorage);
		}

		/// <summary>
		/// A Graphics that draws onto <paramref name="image"/> (which must be a <see cref="Bitmap"/>). The Graphics shares the bitmap's pixels, so nothing is copied;
		/// the image must not be locked while it draws and must outlive the Graphics (a disposed image makes the next call throw ObjectDisposedException).
		/// </summary>
		public static Graphics FromImage(Image image)
		{
			if (image == null)
			{
				throw new ArgumentNullException(nameof(image));
			}
			image.EnsureAlive();
			if (image.HasOutstandingLocks)
			{
				throw new InvalidOperationException("Bitmap region is already locked.");
			}
			return new Graphics(image);
		}

		private SKCanvas Canvas
		{
			get
			{
				if (canvas == null)
				{
					throw new ObjectDisposedException(nameof(Graphics));
				}
				image.EnsureAlive();
				if (image.HasOutstandingLocks)
				{
					throw new InvalidOperationException("Bitmap region is already locked.");
				}
				if (!ReferenceEquals(canvasStorage, image.Storage))
				{
					// RotateFlip gave the image a new bitmap: follow it, keeping the transform.
					canvas.Dispose();
					canvasStorage = image.Storage;
					canvas = new SKCanvas(canvasStorage);
					canvas.SetMatrix(world);
				}
				return canvas;
			}
		}

		public void Clear(Color color)
		{
			_ = Canvas; // checks the state
			unsafe
			{
				// Written directly: exact for every format (Skia would premultiply and unpremultiply a colour for an Argb bitmap), and the transform does not apply to Clear.
				// A format without alpha gets the colour times its alpha (Clear is a SourceCopy through the premultiplied value, as in GDI+: Color.Transparent gives black).
				uint value = unchecked((uint)color.ToArgb());
				switch (image.StorageKind)
				{
					case PixKind.PArgb:
						value = PixelConvert.Premultiply(value);
						break;
					case PixKind.Rgb32:
						value = PixelConvert.Premultiply(value) | 0xFF000000u;
						break;
				}
				byte* row = (byte*)image.PixelsAddress;
				int rowBytes = image.RowBytes;
				for (int y = 0; y < image.Height; y++)
				{
					new Span<uint>(row + (long)y * rowBytes, image.Width).Fill(value);
				}
			}
			image.Storage.NotifyPixelsChanged();
			GC.KeepAlive(image);
		}

		public System.Drawing.Drawing2D.CompositingMode CompositingMode
		{
			get => Checked(compositingMode);
			set
			{
				CheckEnum(value >= CompositingMode.SourceOver && value <= CompositingMode.SourceCopy, value);
				compositingMode = value;
			}
		}

		public System.Drawing.Drawing2D.CompositingQuality CompositingQuality
		{
			get => Checked(compositingQuality);
			set
			{
				CheckEnum(value >= CompositingQuality.Default && value <= CompositingQuality.AssumeLinear, value);
				compositingQuality = value;
			}
		}

		/// <summary>Stored and returned, but without effect: pixel centres are always the Skia ones.</summary>
		public System.Drawing.Drawing2D.PixelOffsetMode PixelOffsetMode
		{
			get => Checked(pixelOffsetMode);
			set
			{
				CheckEnum(value >= PixelOffsetMode.Default && value <= PixelOffsetMode.Half, value);
				pixelOffsetMode = value;
			}
		}

		/// <summary>Default is Bilinear for a new Graphics. Invalid is refused, as in GDI+. See <see cref="Sampling"/> for the mapping to Skia.</summary>
		public System.Drawing.Drawing2D.InterpolationMode InterpolationMode
		{
			get => Checked(interpolationMode);
			set
			{
				CheckEnum(value >= InterpolationMode.Default && value <= InterpolationMode.HighQualityBicubic, value);
				interpolationMode = value;
			}
		}

		/// <summary>Anti-aliases the edges of filled shapes for AntiAlias and HighQuality; the other modes (including Default) do not.</summary>
		public System.Drawing.Drawing2D.SmoothingMode SmoothingMode
		{
			get => Checked(smoothingMode);
			set
			{
				CheckEnum(value >= SmoothingMode.Default && value <= SmoothingMode.AntiAlias, value);
				smoothingMode = value;
			}
		}

		private T Checked<T>(T value)
		{
			if (canvas == null)
			{
				throw new ObjectDisposedException(nameof(Graphics));
			}
			return value;
		}

		private void CheckEnum<T>(bool valid, T value) where T : Enum
		{
			if (canvas == null)
			{
				throw new ObjectDisposedException(nameof(Graphics));
			}
			if (!valid)
			{
				throw new InvalidEnumArgumentException("value", Convert.ToInt32(value), typeof(T));
			}
		}

		// ---- transform -------------------------------------------------------------------------------------------------------------

		public void ResetTransform()
		{
			Canvas.ResetMatrix();
			world = SKMatrix.Identity;
		}

		public void TranslateTransform(float dx, float dy) => TranslateTransform(dx, dy, MatrixOrder.Prepend);

		/// <summary>Prepend (the default) applies the translation to the coordinates before the existing transform; Append after it.</summary>
		public void TranslateTransform(float dx, float dy, System.Drawing.Drawing2D.MatrixOrder order) => Concat(SKMatrix.CreateTranslation(dx, dy), order);

		public void ScaleTransform(float sx, float sy) => ScaleTransform(sx, sy, MatrixOrder.Prepend);

		public void ScaleTransform(float sx, float sy, System.Drawing.Drawing2D.MatrixOrder order) => Concat(SKMatrix.CreateScale(sx, sy), order);

		private void Concat(SKMatrix operation, MatrixOrder order)
		{
			SKCanvas target = Canvas;
			if (order != MatrixOrder.Prepend && order != MatrixOrder.Append)
			{
				throw new ArgumentException("Parameter is not valid.", nameof(order));
			}
			// Skia matrices act on column vectors: world x operation applies the operation first (GDI+ Prepend), operation x world last (Append).
			world = order == MatrixOrder.Prepend ? SKMatrix.Concat(world, operation) : SKMatrix.Concat(operation, world);
			target.SetMatrix(world);
		}

		// ---- filling ---------------------------------------------------------------------------------------------------------------

		public void FillRectangle(Brush brush, Rectangle rect) => FillRectangle(brush, rect.X, rect.Y, rect.Width, rect.Height);

		/// <summary>Only <see cref="SolidBrush"/> is supported. The colour is blended over the bitmap (SourceOver), or replaces it with CompositingMode.SourceCopy.</summary>
		public void FillRectangle(Brush brush, int x, int y, int width, int height)
		{
			if (brush == null)
			{
				throw new ArgumentNullException(nameof(brush));
			}
			SKCanvas target = Canvas;
			if (!(brush is SolidBrush solid))
			{
				throw new NotSupportedException("System.Drawing.Graphics.FillRectangle with a " + brush.GetType().Name + " is not yet supported on macOS");
			}
			Color color = solid.Color;
			using (SKPaint paint = new SKPaint())
			{
				paint.Color = new SKColor(color.R, color.G, color.B, color.A);
				paint.IsAntialias = IsAntiAliased;
				paint.BlendMode = BlendMode;
				target.DrawRect(SKRect.Create(x, y, width, height), paint);
			}
			Changed(SKRect.Create(x, y, width, height));
		}

		/// <summary>
		/// Called after every draw with the destination rectangle in world units. Skia does not force the alpha byte of an opaque bitmap to 255
		/// (SourceCopy of a translucent colour leaves it below 255), but the invariant of the opaque formats says it is always 255
		/// (the colour channels then hold the colour times its alpha, which is what GDI+ stores).
		/// </summary>
		private unsafe void Changed(SKRect worldRect)
		{
			if (image.StorageKind == PixKind.Rgb32 && compositingMode == CompositingMode.SourceCopy)
			{
				SKRect device = world.MapRect(worldRect);
				int left = Math.Max(0, (int)Math.Floor(device.Left));
				int top = Math.Max(0, (int)Math.Floor(device.Top));
				int right = Math.Min(image.Width, (int)Math.Ceiling(device.Right));
				int bottom = Math.Min(image.Height, (int)Math.Ceiling(device.Bottom));
				byte* start = (byte*)image.PixelsAddress;
				int rowBytes = image.RowBytes;
				for (int y = top; y < bottom; y++)
				{
					uint* pixel = (uint*)(start + (long)y * rowBytes) + left;
					for (int x = left; x < right; x++, pixel++)
					{
						*pixel |= 0xFF000000u;
					}
				}
			}
			image.Storage.NotifyPixelsChanged();
			GC.KeepAlive(image);
		}

		private bool IsAntiAliased => smoothingMode == SmoothingMode.AntiAlias || smoothingMode == SmoothingMode.HighQuality;

		private SKBlendMode BlendMode => compositingMode == CompositingMode.SourceCopy ? SKBlendMode.Src : SKBlendMode.SrcOver;

		// ---- DrawImage -------------------------------------------------------------------------------------------------------------

		public void DrawImage(Image image, int x, int y)
		{
			RequireImage(image);
			DrawImageCore(image, new RectangleF(x, y, image.Width, image.Height), new RectangleF(0, 0, image.Width, image.Height), null);
		}

		public void DrawImage(Image image, Rectangle rect)
		{
			RequireImage(image);
			DrawImageCore(image, rect, new RectangleF(0, 0, image.Width, image.Height), null);
		}

		public void DrawImage(Image image, RectangleF rect)
		{
			RequireImage(image);
			DrawImageCore(image, rect, new RectangleF(0, 0, image.Width, image.Height), null);
		}

		public void DrawImage(Image image, int x, int y, int width, int height)
		{
			RequireImage(image);
			DrawImageCore(image, new RectangleF(x, y, width, height), new RectangleF(0, 0, image.Width, image.Height), null);
		}

		public void DrawImage(Image image, Rectangle destRect, Rectangle srcRect, GraphicsUnit srcUnit)
		{
			RequireImage(image);
			RequirePixelUnit(srcUnit);
			DrawImageCore(image, destRect, srcRect, null);
		}

		public void DrawImage(Image image, Rectangle destRect, int srcX, int srcY, int srcWidth, int srcHeight, GraphicsUnit srcUnit)
		{
			RequireImage(image);
			RequirePixelUnit(srcUnit);
			DrawImageCore(image, destRect, new RectangleF(srcX, srcY, srcWidth, srcHeight), null);
		}

		/// <summary>Only the colour matrix of <paramref name="imageAttr"/> is applied (null means none).</summary>
		public void DrawImage(Image image, Rectangle destRect, int srcX, int srcY, int srcWidth, int srcHeight, GraphicsUnit srcUnit, System.Drawing.Imaging.ImageAttributes imageAttr)
		{
			RequireImage(image);
			RequirePixelUnit(srcUnit);
			DrawImageCore(image, destRect, new RectangleF(srcX, srcY, srcWidth, srcHeight), imageAttr);
		}

		private static void RequireImage(Image image)
		{
			if (image == null)
			{
				throw new ArgumentNullException(nameof(image));
			}
		}

		private static void RequirePixelUnit(GraphicsUnit unit)
		{
			if (unit != GraphicsUnit.Pixel)
			{
				throw new NotSupportedException("System.Drawing.Graphics.DrawImage with GraphicsUnit." + unit + " is not yet supported on macOS (only Pixel)");
			}
		}

		/// <summary>The Skia sampling for a GDI+ interpolation mode (the mapping of task M1.2c).</summary>
		internal static SKSamplingOptions Sampling(InterpolationMode mode)
		{
			switch (mode)
			{
				case InterpolationMode.NearestNeighbor:
					return new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None);
				case InterpolationMode.HighQualityBilinear:
					return new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
				case InterpolationMode.Bicubic:
				case InterpolationMode.HighQualityBicubic:
				case InterpolationMode.High:
					return new SKSamplingOptions(SKCubicResampler.Mitchell);
				default: // Default, Low, Bilinear
					return new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None);
			}
		}

		/// <summary>
		/// Draws the part <paramref name="srcRect"/> of <paramref name="source"/> (pixels) into <paramref name="destRect"/> (world units, through the current transform).
		/// A source rectangle that reaches outside the image is clipped to it, and the destination shrinks in proportion (as in GDI+).
		/// The source pixels are read in place: the sub-image is a zero-copy view, so filtering never reads pixels outside the source rectangle.
		/// </summary>
		private unsafe void DrawImageCore(Image source, RectangleF destRect, RectangleF srcRect, ImageAttributes attributes)
		{
			SKCanvas target = Canvas;
			source.EnsureAlive();
			if (destRect.Width < 0 || destRect.Height < 0 || srcRect.Width < 0 || srcRect.Height < 0)
			{
				throw new NotSupportedException("System.Drawing.Graphics.DrawImage with a negative width or height (a mirrored image) is not yet supported on macOS");
			}
			if (destRect.Width == 0 || destRect.Height == 0 || srcRect.Width == 0 || srcRect.Height == 0)
			{
				return;
			}

			// Clip the source rectangle to the image and shrink the destination in proportion.
			RectangleF bounds = new RectangleF(0, 0, source.Width, source.Height);
			RectangleF clipped = RectangleF.Intersect(srcRect, bounds);
			if (clipped.IsEmpty)
			{
				return;
			}
			float sx = destRect.Width / srcRect.Width;
			float sy = destRect.Height / srcRect.Height;
			SKRect dest = SKRect.Create(destRect.X + (clipped.X - srcRect.X) * sx, destRect.Y + (clipped.Y - srcRect.Y) * sy, clipped.Width * sx, clipped.Height * sy);

			// Whole pixels only (the Core passes integers; a fractional source rectangle is widened to the pixels it touches).
			int left = (int)Math.Floor(clipped.Left);
			int top = (int)Math.Floor(clipped.Top);
			int right = Math.Min(source.Width, (int)Math.Ceiling(clipped.Right));
			int bottom = Math.Min(source.Height, (int)Math.Ceiling(clipped.Bottom));

			// Drawing an image onto itself would read pixels that are being written: draw from a copy.
			Image from = source;
			Bitmap copy = null;
			if (ReferenceEquals(source.Storage, image.Storage))
			{
				copy = Bitmap.CopyOf(source, new Rectangle(left, top, right - left, bottom - top), source.PixelFormat);
				from = copy;
				right -= left;
				bottom -= top;
				left = 0;
				top = 0;
			}
			try
			{
				SKBitmap storage = from.Storage;
				SKImageInfo info = new SKImageInfo(right - left, bottom - top, storage.ColorType, storage.AlphaType);
				IntPtr start = from.PixelsAddress + top * from.RowBytes + left * 4;
				using (SKImage view = SKImage.FromPixels(info, start, from.RowBytes))
				using (SKPaint paint = new SKPaint())
				{
					paint.BlendMode = BlendMode;
					float[] matrix = attributes?.SkiaColorMatrix;
					if (matrix != null)
					{
						paint.ColorFilter = SKColorFilter.CreateColorMatrix(matrix);
					}
					SKSamplingOptions sampling = IsPixelAlignedCopy(dest, right - left, bottom - top) ? Sampling(InterpolationMode.NearestNeighbor) : Sampling(interpolationMode);
					target.DrawImage(view, dest, sampling, paint);
				}
				GC.KeepAlive(from);
			}
			finally
			{
				copy?.Dispose();
			}
			Changed(dest);
		}

		/// <summary>
		/// True when the draw maps source pixels one to one onto whole destination pixels (no scaling, a whole-pixel translation). GDI+ copies such a draw exactly
		/// for every interpolation mode; Skia's cubic filter would still blur it, so it is drawn as nearest neighbour.
		/// </summary>
		private bool IsPixelAlignedCopy(SKRect dest, int sourceWidth, int sourceHeight)
		{
			if (world.ScaleX != 1f || world.ScaleY != 1f || world.SkewX != 0f || world.SkewY != 0f || world.Persp0 != 0f || world.Persp1 != 0f || world.Persp2 != 1f)
			{
				return false;
			}
			float left = dest.Left + world.TransX;
			float top = dest.Top + world.TransY;
			return dest.Width == sourceWidth && dest.Height == sourceHeight && left == (float)Math.Round(left) && top == (float)Math.Round(top);
		}

		// ---- not supported yet -----------------------------------------------------------------------------------------------------

		public Region Clip
		{
			set => throw new NotSupportedException("System.Drawing.Graphics.Clip is not yet supported on macOS");
		}

		public void DrawIcon(Icon icon, Rectangle targetRect) => throw new NotSupportedException("System.Drawing.Graphics.DrawIcon is not yet supported on macOS");

		public void DrawString(string s, Font font, Brush brush, RectangleF layoutRectangle, StringFormat format) => throw new NotSupportedException("System.Drawing.Graphics.DrawString is not yet supported on macOS");

		public void DrawString(string s, Font font, Brush brush, float x, float y) => throw new NotSupportedException("System.Drawing.Graphics.DrawString is not yet supported on macOS");

		public void FillEllipse(Brush brush, int x, int y, int width, int height) => throw new NotSupportedException("System.Drawing.Graphics.FillEllipse is not yet supported on macOS");

		public IntPtr GetHdc() => throw new NotSupportedException("System.Drawing.Graphics.GetHdc is not yet supported on macOS");

		public SizeF MeasureString(string text, Font font) => throw new NotSupportedException("System.Drawing.Graphics.MeasureString is not yet supported on macOS");

		public SizeF MeasureString(string text, Font font, SizeF layoutArea, StringFormat stringFormat) => throw new NotSupportedException("System.Drawing.Graphics.MeasureString is not yet supported on macOS");

		public GraphicsUnit PageUnit
		{
			set => throw new NotSupportedException("System.Drawing.Graphics.PageUnit is not yet supported on macOS");
		}

		public void ReleaseHdc() => throw new NotSupportedException("System.Drawing.Graphics.ReleaseHdc is not yet supported on macOS");

		public void Restore(System.Drawing.Drawing2D.GraphicsState gstate) => throw new NotSupportedException("System.Drawing.Graphics.Restore is not yet supported on macOS");

		public System.Drawing.Drawing2D.GraphicsState Save() => throw new NotSupportedException("System.Drawing.Graphics.Save is not yet supported on macOS");

		public void SetClip(Rectangle rect, System.Drawing.Drawing2D.CombineMode combineMode) => throw new NotSupportedException("System.Drawing.Graphics.SetClip is not yet supported on macOS");

		public void SetClip(RectangleF rect, System.Drawing.Drawing2D.CombineMode combineMode) => throw new NotSupportedException("System.Drawing.Graphics.SetClip is not yet supported on macOS");

		public System.Drawing.Text.TextRenderingHint TextRenderingHint
		{
			get => throw new NotSupportedException("System.Drawing.Graphics.TextRenderingHint is not yet supported on macOS");
			set => throw new NotSupportedException("System.Drawing.Graphics.TextRenderingHint is not yet supported on macOS");
		}

		public void TransformPoints(System.Drawing.Drawing2D.CoordinateSpace destSpace, System.Drawing.Drawing2D.CoordinateSpace srcSpace, params Point[] pts) => throw new NotSupportedException("System.Drawing.Graphics.TransformPoints is not yet supported on macOS");

		public void TransformPoints(System.Drawing.Drawing2D.CoordinateSpace destSpace, System.Drawing.Drawing2D.CoordinateSpace srcSpace, params PointF[] pts) => throw new NotSupportedException("System.Drawing.Graphics.TransformPoints is not yet supported on macOS");

		public void Dispose()
		{
			Dispose(disposing: true);
			GC.SuppressFinalize(this);
		}

		private void Dispose(bool disposing)
		{
			SKCanvas old = canvas;
			canvas = null;
			canvasStorage = null;
			old?.Dispose();
		}
	}
}
