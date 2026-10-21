using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using ComicRack.Mac.Browser;
using cYo.Compat.Drawing;
using cYo.Projects.ComicRack.Engine.Drawing;
using SkiaSharp;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingImage = System.Drawing.Image;
using DrawingRectangle = System.Drawing.Rectangle;
using DrawingSize = System.Drawing.Size;

namespace ComicRack.Mac.Editor
{
	/// <summary>
	/// The 3D cover of the Info dialog (<c>InformationCover3D</c>, <c>ThumbTileRenderer.Draw3DComic</c>): the Core's own software renderer (<c>ComicBox3D</c>, which draws a book standing
	/// at an angle with the cover, the pages and a shadow) over the cover thumbnail, thickness from the page count. The original asks for a 512 unit picture with the
	/// <c>Filter</c> option (render at 1.5 times the size, then scale the result back by two thirds); the stand-in's high-quality resize of that option is not available, so
	/// the render is done at 768 and scaled down with Skia, which gives the same picture. The result is trimmed to the book, with transparent corners.
	/// </summary>
	public static class Cover3D
	{
		private const int Size = 512;
		private const int Supersampled = 768;

		/// <summary>Renders the book for <paramref name="cover"/> (a thumbnail, at most 512 high) and <paramref name="pageCount"/> pages. Null if the Core could not draw it. Slow (50 to 200 ms): call it off the UI thread.</summary>
		public static SKBitmap Render(SKImage cover, int pageCount)
		{
			if (cover == null)
			{
				return null;
			}
			using (DrawingBitmap source = ToDrawingBitmap(cover))
			using (DrawingBitmap book = ComicBox3D.CreateDefaultBook(source, null, new DrawingSize(Supersampled, Supersampled), Math.Max(0, pageCount),
				ComicBox3DOptions.Trim | ComicBox3DOptions.SimpleShadow | ComicBox3DOptions.SplitDoublePages))
			{
				if (book == null)
				{
					return null;
				}
				SKBitmap pixels = SkiaInterop.GetSKBitmap(book);
				int width = Math.Max(1, (int)Math.Round(pixels.Width * (double)Size / Supersampled));
				int height = Math.Max(1, (int)Math.Round(pixels.Height * (double)Size / Supersampled));
				var scaled = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
				using (var image = SKImage.FromBitmap(pixels))
				using (var canvas = new SKCanvas(scaled))
				{
					canvas.Clear();
					canvas.DrawImage(image, new SKRect(0, 0, width, height), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
				}
				return scaled;
			}
		}

		/// <summary>
		/// The 3D book for a cover picture, made on the thread pool; the result is a picture a control can draw (the caller owns its one reference), null when the Core could not draw it.
		/// <paramref name="cover"/> is kept alive while the render runs.
		/// </summary>
		public static async Task<CoverImage> RenderAsync(CoverImage cover, int pageCount, CancellationToken token)
		{
			CoverImage held = cover.Retain();
			try
			{
				SKBitmap rendered = await Task.Run(() => Render(held.Image, pageCount), token);
				if (rendered == null)
				{
					return null;
				}
				try
				{
					return ToCoverImage(rendered);
				}
				finally
				{
					rendered.Dispose();
				}
			}
			finally
			{
				held.Release();
			}
		}

		/// <summary>A Drawing stand-in bitmap with the pixels of <paramref name="image"/> (not premultiplied, as the stand-in's ARGB bitmaps are).</summary>
		public static DrawingBitmap ToDrawingBitmap(SKImage image)
		{
			var bitmap = new DrawingBitmap(image.Width, image.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
			try
			{
				System.Drawing.Imaging.BitmapData data = bitmap.LockBits(new DrawingRectangle(0, 0, image.Width, image.Height), System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
				try
				{
					var info = new SKImageInfo(image.Width, image.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
					if (!image.ReadPixels(info, data.Scan0, data.Stride, 0, 0))
					{
						throw new InvalidOperationException("The cover pixels could not be read.");
					}
				}
				finally
				{
					bitmap.UnlockBits(data);
				}
				return bitmap;
			}
			catch
			{
				bitmap.Dispose();
				throw;
			}
		}

		/// <summary>The picture as a <see cref="CoverImage"/> a control can draw (a copy: the stand-in bitmap may be disposed afterwards). The caller owns the one reference.</summary>
		public static CoverImage ToCoverImage(DrawingImage image)
		{
			using (SKBitmap copy = SkiaInterop.GetSKBitmap((DrawingBitmap)image).Copy())
			{
				return new CoverImage(SKImage.FromBitmap(copy));
			}
		}

		/// <summary>An SKBitmap (still owned by the caller) as a <see cref="CoverImage"/> of its own pixels. The caller owns the one reference.</summary>
		public static CoverImage ToCoverImage(SKBitmap bitmap)
		{
			return new CoverImage(SKImage.FromBitmap(bitmap));
		}

		private static readonly ConditionalWeakTable<DrawingImage, CoverImage> iconCache = new ConditionalWeakTable<DrawingImage, CoverImage>();

		/// <summary>
		/// The pictures of the icons <c>ComicBook.GetIcons()</c> returns (publisher, imprint, age rating, format, tags, ...) as drawable images, converted once per icon
		/// (the Core keeps the icon bitmaps for the run of the program).
		/// </summary>
		public static List<CoverImage> IconImages(IEnumerable<DrawingImage> icons)
		{
			var result = new List<CoverImage>();
			foreach (DrawingImage icon in icons)
			{
				if (icon is DrawingBitmap)
				{
					lock (iconCache)
					{
						if (!iconCache.TryGetValue(icon, out CoverImage cached))
						{
							cached = ToCoverImage(icon);
							iconCache.Add(icon, cached);
						}
						result.Add(cached);
					}
				}
			}
			return result;
		}
	}
}
