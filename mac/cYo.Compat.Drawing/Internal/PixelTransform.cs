using System;
using System.Threading.Tasks;
using SkiaSharp;

namespace cYo.Compat.Drawing.Internal
{
	internal static unsafe class PixelTransform
	{
		private const int Tile = 64;

		/// <summary>
		/// Returns a new bitmap holding <paramref name="source"/> rotated clockwise by <paramref name="quarterTurns"/> x 90 degrees and then,
		/// if <paramref name="flipHorizontal"/>, mirrored left to right. Returns null when nothing changes. Pixels are copied bit for bit.
		/// </summary>
		public static SKBitmap RotateFlip(SKBitmap source, int quarterTurns, bool flipHorizontal)
		{
			quarterTurns &= 3;
			if (quarterTurns == 0 && !flipHorizontal)
			{
				return null;
			}
			int w = source.Width;
			int h = source.Height;
			bool swap = (quarterTurns & 1) == 1;
			int dw = swap ? h : w;
			int dh = swap ? w : h;
			SKImageInfo info = new SKImageInfo(dw, dh, source.ColorType, source.AlphaType);
			SKBitmap target = new SKBitmap();
			if (!target.TryAllocPixels(info))
			{
				target.Dispose();
				throw new OutOfMemoryException("Not enough memory for the rotated bitmap");
			}
			try
			{
				// Destination pixel x' = xc + xx*x + xy*y, y' = yc + yx*x + yy*y for source pixel (x, y).
				long xc, xx, xy, yc, yx, yy;
				switch (quarterTurns)
				{
					case 0:
						(xc, xx, xy, yc, yx, yy) = (0, 1, 0, 0, 0, 1);
						break;
					case 1:
						(xc, xx, xy, yc, yx, yy) = (h - 1, 0, -1, 0, 1, 0);
						break;
					case 2:
						(xc, xx, xy, yc, yx, yy) = (w - 1, -1, 0, h - 1, 0, -1);
						break;
					default:
						(xc, xx, xy, yc, yx, yy) = (0, 0, 1, w - 1, -1, 0);
						break;
				}
				if (flipHorizontal)
				{
					(xc, xx, xy) = (dw - 1 - xc, -xx, -xy);
				}
				long dstStride = target.RowBytes / 4;
				long posC = yc * dstStride + xc;
				long posX = yx * dstStride + xx;
				long posY = yy * dstStride + xy;
				long srcStride = source.RowBytes / 4;
				uint* src = (uint*)source.GetPixels();
				uint* dst = (uint*)target.GetPixels();
				int tilesY = (h + Tile - 1) / Tile;
				Action<int> tileRow = ty =>
				{
					int y0 = ty * Tile;
					int y1 = Math.Min(h, y0 + Tile);
					for (int x0 = 0; x0 < w; x0 += Tile)
					{
						int x1 = Math.Min(w, x0 + Tile);
						for (int y = y0; y < y1; y++)
						{
							uint* s = src + y * srcStride;
							for (int x = x0; x < x1; x++)
							{
								dst[posC + x * posX + y * posY] = s[x];
							}
						}
					}
				};
				if ((long)w * h >= 1 << 18 && tilesY > 1)
				{
					Parallel.For(0, tilesY, tileRow);
				}
				else
				{
					for (int ty = 0; ty < tilesY; ty++)
					{
						tileRow(ty);
					}
				}
				return target;
			}
			catch
			{
				target.Dispose();
				throw;
			}
		}
	}
}
