using System;
using System.IO;
using System.Threading;
using ComicRack.Mac.Controls;
using SkiaSharp;

namespace ComicRack.Mac.Thumbnails
{
	/// <summary>Skia-side helpers of the thumbnail path: decoding the Core's thumbnail bytes and drawing the placeholder.</summary>
	public static class ThumbnailPixels
	{
		/// <summary>
		/// Decodes the bytes of a thumbnail (the JPEG or PNG that <c>ThumbnailImage.Data</c> holds) straight into a BGRA bitmap that
		/// the returned block owns: one allocation, no copy afterwards. A JPEG comes out <see cref="SKAlphaType.Opaque"/>. If the
		/// picture is taller than <paramref name="targetHeight"/> it is reduced to that height with a filter that averages
		/// (linear with mipmaps), because the plain cubic filters alias on large reductions.
		/// </summary>
		/// <exception cref="InvalidDataException">The bytes are not a complete image that Skia can decode.</exception>
		public static PixelBlock Decode(byte[] data, int targetHeight, CancellationToken cancellationToken = default)
		{
			if (data == null || data.Length == 0)
			{
				throw new InvalidDataException("The thumbnail has no data.");
			}
			SKBitmap bitmap = null;
			try
			{
				using (SKData skData = SKData.CreateCopy(data))
				using (SKCodec codec = SKCodec.Create(skData))
				{
					if (codec == null)
					{
						throw new InvalidDataException("The thumbnail is not an image Skia can decode.");
					}
					SKImageInfo source = codec.Info;
					var info = new SKImageInfo(source.Width, source.Height, SKColorType.Bgra8888, source.IsOpaque ? SKAlphaType.Opaque : SKAlphaType.Premul);
					bitmap = new SKBitmap(info);
					SKCodecResult result = codec.GetPixels(info, bitmap.GetPixels());
					if (result != SKCodecResult.Success)
					{
						throw new InvalidDataException("The thumbnail could not be decoded completely (" + result + ").");
					}
				}
				cancellationToken.ThrowIfCancellationRequested();
				if (targetHeight > 0 && bitmap.Height > targetHeight)
				{
					int width = Math.Max(1, (int)Math.Round((double)bitmap.Width * targetHeight / bitmap.Height));
					var smallInfo = new SKImageInfo(width, targetHeight, SKColorType.Bgra8888, bitmap.AlphaType);
					SKBitmap small = bitmap.Resize(smallInfo, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
					if (small == null)
					{
						throw new InvalidDataException("The thumbnail could not be reduced.");
					}
					bitmap.Dispose();
					bitmap = small;
				}
				PixelBlock block = PixelBlock.FromBitmap(bitmap, onFree: null, detectOpaque: true);
				bitmap = null;
				return block;
			}
			finally
			{
				bitmap?.Dispose();
			}
		}

		private static SKColor Skia(uint argb) => argb; // SKColor converts from 0xAARRGGBB

		/// <summary>
		/// The picture shown when a thumbnail cannot be made (a missing or damaged file): a 2:3 tile with a cross in the colours of the current appearance (light grey in the light one). No text,
		/// so it does not depend on a font.
		/// </summary>
		public static PixelBlock CreatePlaceholder(int height) => CreatePlaceholder(height, ThumbnailPlaceholderLook.Current);

		/// <summary>The placeholder in the given colours (the chrome layer's <see cref="ThumbnailPlaceholderLook"/> for the light or the dark appearance).</summary>
		public static PixelBlock CreatePlaceholder(int height, ThumbnailPlaceholderLook look)
		{
			look = look ?? ThumbnailPlaceholderLook.Current;
			height = Math.Max(8, height);
			int width = Math.Max(6, height * 2 / 3);
			var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque));
			try
			{
				using (var canvas = new SKCanvas(bitmap))
				using (var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke })
				{
					canvas.Clear(Skia(look.Tile));
					float line = Math.Max(1f, height / 80f);
					paint.StrokeWidth = line;
					paint.Color = Skia(look.Frame);
					canvas.DrawRect(line / 2, line / 2, width - line, height - line, paint);
					float inset = width * 0.22f;
					float top = (height - (width - 2 * inset)) / 2;
					paint.StrokeWidth = Math.Max(1.5f, height / 45f);
					paint.StrokeCap = SKStrokeCap.Round;
					paint.Color = Skia(look.Cross);
					canvas.DrawLine(inset, top, width - inset, top + (width - 2 * inset), paint);
					canvas.DrawLine(width - inset, top, inset, top + (width - 2 * inset), paint);
				}
				PixelBlock block = PixelBlock.FromBitmap(bitmap);
				bitmap = null;
				return block;
			}
			finally
			{
				bitmap?.Dispose();
			}
		}
	}
}
