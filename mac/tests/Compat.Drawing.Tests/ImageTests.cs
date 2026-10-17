using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Xunit;
using static Compat.Drawing.Tests.TestImages;

namespace Compat.Drawing.Tests
{
	/// <summary>Image: decoding (FromStream, FromFile), the read-only properties, Clone, RotateFlip and GetPixelFormatSize.</summary>
	public class ImageTests
	{
		[Fact]
		public void FromStream_decodes_a_jpeg_as_24bpp()
		{
			using (Image image = Image.FromStream(ToStream(SkiaJpeg(40, 30))))
			{
				Assert.IsType<Bitmap>(image);
				Assert.Equal(PixelFormat.Format24bppRgb, image.PixelFormat);
				Assert.Equal(40, image.Width);
				Assert.Equal(30, image.Height);
				Bitmap bitmap = (Bitmap)image;
				AssertColor(Gradient(0, 0, 40, 30), bitmap.GetPixel(0, 0), 8);
				AssertColor(Gradient(39, 29, 40, 30), bitmap.GetPixel(39, 29), 8);
				AssertColor(Gradient(20, 15, 40, 30), bitmap.GetPixel(20, 15), 8);
			}
		}

		[Fact]
		public void FromStream_keeps_translucent_png_pixels_unpremultiplied()
		{
			using (Bitmap bitmap = (Bitmap)Image.FromStream(ToStream(SkiaPngWithAlpha())))
			{
				Assert.Equal(PixelFormat.Format32bppArgb, bitmap.PixelFormat);
				Assert.Equal(new Size(2, 1), bitmap.Size);
				Assert.Equal(Color.FromArgb(128, 255, 0, 0), bitmap.GetPixel(0, 0));
				Assert.Equal(Color.FromArgb(255, 0, 0, 255), bitmap.GetPixel(1, 0));
				Assert.Equal(0x80FF0000u, Raw(bitmap, 0, 0));
			}
		}

		[Fact]
		public void FromStream_opaque_png_is_24bpp()
		{
			using (Bitmap bitmap = (Bitmap)Image.FromStream(ToStream(SkiaPngOpaque())))
			{
				Assert.Equal(PixelFormat.Format24bppRgb, bitmap.PixelFormat);
				Assert.Equal(Color.FromArgb(255, 200, 100, 50), bitmap.GetPixel(2, 1));
				Assert.Equal(0xFFC86432u, Raw(bitmap, 2, 1));
			}
		}

		[Fact]
		public void FromStream_with_color_management_flags_decodes_the_same_image()
		{
			byte[] png = SkiaPngOpaque();
			using (Image a = Image.FromStream(ToStream(png), useEmbeddedColorManagement: false))
			using (Image b = Image.FromStream(ToStream(png), useEmbeddedColorManagement: false, validateImageData: false))
			using (Image c = Image.FromStream(ToStream(png), true, true))
			{
				Assert.Equal(new Size(3, 2), a.Size);
				Assert.Equal(new Size(3, 2), b.Size);
				Assert.Equal(new Size(3, 2), c.Size);
			}
		}

		[Fact]
		public void FromStream_starts_at_the_current_position_and_leaves_the_stream_usable()
		{
			byte[] png = SkiaPngOpaque();
			byte[] padded = new byte[png.Length + 5];
			Array.Copy(png, 0, padded, 5, png.Length);
			using (MemoryStream stream = new MemoryStream(padded))
			{
				stream.Position = 5;
				using (Image image = Image.FromStream(stream))
				{
					Assert.Equal(new Size(3, 2), image.Size);
				}
				Assert.True(stream.CanRead);
			}
		}

		[Fact]
		public void FromStream_reads_streams_that_cannot_seek()
		{
			using (Image image = Image.FromStream(new ForwardOnlyStream(SkiaPngOpaque())))
			{
				Assert.Equal(new Size(3, 2), image.Size);
			}
		}

		[Fact]
		public void FromStream_decodes_bmp_gif_and_webp()
		{
			// 2 x 1 BMP, 24 bpp bottom-up, written by hand: blue then red.
			byte[] bmp =
			{
				0x42, 0x4D, 0x3A, 0, 0, 0, 0, 0, 0, 0, 0x36, 0, 0, 0,
				0x28, 0, 0, 0, 2, 0, 0, 0, 1, 0, 0, 0, 1, 0, 24, 0, 0, 0, 0, 0, 8, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
				0xFF, 0, 0, 0, 0, 0xFF, 0, 0,
			};
			using (Bitmap bitmap = (Bitmap)Image.FromStream(ToStream(bmp)))
			{
				Assert.Equal(new Size(2, 1), bitmap.Size);
				AssertColor(Color.FromArgb(255, 0, 0, 255), bitmap.GetPixel(0, 0));
				AssertColor(Color.FromArgb(255, 255, 0, 0), bitmap.GetPixel(1, 0));
			}

			byte[] gif = System.Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7");
			using (Image image = Image.FromStream(ToStream(gif)))
			{
				Assert.Equal(new Size(1, 1), image.Size);
			}

			byte[] webp;
			using (Bitmap source = GradientBitmap(16, 8))
			{
				webp = SkiaEncode(cYo.Compat.Drawing.SkiaInterop.GetSKBitmap(source), SkiaSharp.SKEncodedImageFormat.Webp);
			}
			using (Bitmap bitmap = (Bitmap)Image.FromStream(ToStream(webp)))
			{
				Assert.Equal(new Size(16, 8), bitmap.Size);
			}
		}

		[Fact]
		public void FromStream_rejects_data_that_is_not_an_image()
		{
			Assert.Throws<ArgumentException>(() => Image.FromStream(ToStream(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 })));
			Assert.Throws<ArgumentException>(() => Image.FromStream(new MemoryStream()));
			Assert.Throws<ArgumentNullException>(() => Image.FromStream(null));
		}

		[Fact]
		public void FromStream_accepts_a_truncated_jpeg()
		{
			byte[] jpeg = SkiaJpeg(64, 64);
			byte[] cut = new byte[jpeg.Length * 2 / 3];
			Array.Copy(jpeg, cut, cut.Length);
			using (Bitmap image = (Bitmap)Image.FromStream(ToStream(cut)))
			{
				Assert.Equal(new Size(64, 64), image.Size);
				Assert.Equal(PixelFormat.Format24bppRgb, image.PixelFormat);
				// The part that could not be decoded is opaque, too.
				Assert.Equal(0xFFu, Raw(image, 63, 63) >> 24);
				Assert.Equal(0xFFu, Raw(image, 0, 0) >> 24);
			}
		}

		[Fact]
		public void FromFile_decodes_and_does_not_keep_the_file_open()
		{
			string file = Path.Combine(Path.GetTempPath(), "cyo-compat-drawing-" + Guid.NewGuid().ToString("N") + ".png");
			File.WriteAllBytes(file, SkiaPngOpaque());
			try
			{
				using (Image image = Image.FromFile(file))
				{
					Assert.Equal(new Size(3, 2), image.Size);
					File.Delete(file);
					Assert.False(File.Exists(file));
				}
			}
			finally
			{
				if (File.Exists(file))
				{
					File.Delete(file);
				}
			}
			Assert.Throws<FileNotFoundException>(() => Image.FromFile(file));
			Assert.Throws<ArgumentNullException>(() => Image.FromFile(null));
		}

		[Theory]
		[InlineData(PixelFormat.Format1bppIndexed, 1)]
		[InlineData(PixelFormat.Format4bppIndexed, 4)]
		[InlineData(PixelFormat.Format8bppIndexed, 8)]
		[InlineData(PixelFormat.Format16bppRgb555, 16)]
		[InlineData(PixelFormat.Format16bppRgb565, 16)]
		[InlineData(PixelFormat.Format16bppArgb1555, 16)]
		[InlineData(PixelFormat.Format16bppGrayScale, 16)]
		[InlineData(PixelFormat.Format24bppRgb, 24)]
		[InlineData(PixelFormat.Format32bppRgb, 32)]
		[InlineData(PixelFormat.Format32bppArgb, 32)]
		[InlineData(PixelFormat.Format32bppPArgb, 32)]
		[InlineData(PixelFormat.Format48bppRgb, 48)]
		[InlineData(PixelFormat.Format64bppArgb, 64)]
		[InlineData(PixelFormat.Format64bppPArgb, 64)]
		[InlineData(PixelFormat.Undefined, 0)]
		public void GetPixelFormatSize_returns_the_bits_per_pixel(PixelFormat format, int bits)
		{
			Assert.Equal(bits, Image.GetPixelFormatSize(format));
		}

		[Theory]
		[InlineData(PixelFormat.Format32bppArgb)]
		[InlineData(PixelFormat.Format32bppPArgb)]
		[InlineData(PixelFormat.Format32bppRgb)]
		[InlineData(PixelFormat.Format24bppRgb)]
		public void Width_Height_Size_and_PixelFormat_report_the_construction_values(PixelFormat format)
		{
			using (Image image = new Bitmap(7, 3, format))
			{
				Assert.Equal(7, image.Width);
				Assert.Equal(3, image.Height);
				Assert.Equal(new Size(7, 3), image.Size);
				Assert.Equal(format, image.PixelFormat);
			}
		}

		[Fact]
		public void Flags_report_alpha_only_for_alpha_formats()
		{
			using (Image argb = new Bitmap(2, 2, PixelFormat.Format32bppArgb))
			using (Image pargb = new Bitmap(2, 2, PixelFormat.Format32bppPArgb))
			using (Image rgb32 = new Bitmap(2, 2, PixelFormat.Format32bppRgb))
			using (Image rgb24 = new Bitmap(2, 2, PixelFormat.Format24bppRgb))
			{
				Assert.NotEqual(0, argb.Flags & (int)ImageFlags.HasAlpha);
				Assert.NotEqual(0, pargb.Flags & (int)ImageFlags.HasAlpha);
				Assert.Equal(0, rgb32.Flags & (int)ImageFlags.HasAlpha);
				Assert.Equal(0, rgb24.Flags & (int)ImageFlags.HasAlpha);
				Assert.NotEqual(0, rgb24.Flags & (int)ImageFlags.ColorSpaceRgb);
			}
		}

		[Theory]
		[InlineData(PixelFormat.Format32bppArgb)]
		[InlineData(PixelFormat.Format32bppPArgb)]
		[InlineData(PixelFormat.Format32bppRgb)]
		[InlineData(PixelFormat.Format24bppRgb)]
		public void Clone_is_an_independent_copy_of_the_same_format(PixelFormat format)
		{
			using (Bitmap original = GradientBitmap(6, 4, format))
			{
				object clone = original.Clone();
				using ((Bitmap)clone)
				{
					Bitmap copy = Assert.IsType<Bitmap>(clone);
					Assert.Equal(format, copy.PixelFormat);
					Assert.Equal(original.Size, copy.Size);
					for (int y = 0; y < 4; y++)
					{
						for (int x = 0; x < 6; x++)
						{
							Assert.Equal(original.GetPixel(x, y), copy.GetPixel(x, y));
						}
					}
					copy.SetPixel(0, 0, Color.Red);
					Assert.NotEqual(Color.Red.ToArgb(), original.GetPixel(0, 0).ToArgb());
				}
			}
		}

		private static readonly Color A = Color.FromArgb(255, 10, 0, 0);
		private static readonly Color B = Color.FromArgb(255, 20, 0, 0);
		private static readonly Color C = Color.FromArgb(255, 30, 0, 0);
		private static readonly Color D = Color.FromArgb(255, 40, 0, 0);
		private static readonly Color E = Color.FromArgb(255, 50, 0, 0);
		private static readonly Color F = Color.FromArgb(255, 60, 0, 0);

		/// <summary>3 x 2:  A B C / D E F</summary>
		private static Bitmap Letters(PixelFormat format = PixelFormat.Format32bppArgb)
		{
			Bitmap bitmap = new Bitmap(3, 2, format);
			Color[] colors = { A, B, C, D, E, F };
			for (int i = 0; i < 6; i++)
			{
				bitmap.SetPixel(i % 3, i / 3, colors[i]);
			}
			return bitmap;
		}

		private static void AssertGrid(Bitmap bitmap, int width, int height, params Color[] rowMajor)
		{
			Assert.Equal(new Size(width, height), bitmap.Size);
			for (int i = 0; i < rowMajor.Length; i++)
			{
				Assert.Equal(rowMajor[i], bitmap.GetPixel(i % width, i / width));
			}
		}

		[Fact]
		public void RotateFlip_known_results()
		{
			using (Bitmap b = Letters())
			{
				b.RotateFlip(RotateFlipType.Rotate90FlipNone);
				AssertGrid(b, 2, 3, D, A, E, B, F, C);
			}
			using (Bitmap b = Letters())
			{
				b.RotateFlip(RotateFlipType.Rotate180FlipNone);
				AssertGrid(b, 3, 2, F, E, D, C, B, A);
			}
			using (Bitmap b = Letters())
			{
				b.RotateFlip(RotateFlipType.Rotate270FlipNone);
				AssertGrid(b, 2, 3, C, F, B, E, A, D);
			}
			using (Bitmap b = Letters())
			{
				b.RotateFlip(RotateFlipType.RotateNoneFlipX);
				AssertGrid(b, 3, 2, C, B, A, F, E, D);
			}
			using (Bitmap b = Letters())
			{
				b.RotateFlip(RotateFlipType.RotateNoneFlipY);
				AssertGrid(b, 3, 2, D, E, F, A, B, C);
			}
			using (Bitmap b = Letters())
			{
				b.RotateFlip(RotateFlipType.RotateNoneFlipNone);
				AssertGrid(b, 3, 2, A, B, C, D, E, F);
			}
		}

		private static Color[,] Grid(Bitmap bitmap)
		{
			Color[,] grid = new Color[bitmap.Width, bitmap.Height];
			for (int y = 0; y < bitmap.Height; y++)
			{
				for (int x = 0; x < bitmap.Width; x++)
				{
					grid[x, y] = bitmap.GetPixel(x, y);
				}
			}
			return grid;
		}

		private static Color[,] Rotate90Clockwise(Color[,] a)
		{
			int w = a.GetLength(0), h = a.GetLength(1);
			Color[,] r = new Color[h, w];
			for (int y = 0; y < h; y++)
			{
				for (int x = 0; x < w; x++)
				{
					r[h - 1 - y, x] = a[x, y];
				}
			}
			return r;
		}

		private static Color[,] MirrorHorizontally(Color[,] a)
		{
			int w = a.GetLength(0), h = a.GetLength(1);
			Color[,] r = new Color[w, h];
			for (int y = 0; y < h; y++)
			{
				for (int x = 0; x < w; x++)
				{
					r[w - 1 - x, y] = a[x, y];
				}
			}
			return r;
		}

		[Theory]
		[InlineData(RotateFlipType.RotateNoneFlipNone)]
		[InlineData(RotateFlipType.Rotate90FlipNone)]
		[InlineData(RotateFlipType.Rotate180FlipNone)]
		[InlineData(RotateFlipType.Rotate270FlipNone)]
		[InlineData(RotateFlipType.RotateNoneFlipX)]
		[InlineData(RotateFlipType.Rotate90FlipX)]
		[InlineData(RotateFlipType.Rotate180FlipX)]
		[InlineData(RotateFlipType.Rotate270FlipX)]
		public void RotateFlip_is_a_clockwise_rotation_then_a_horizontal_flip(RotateFlipType type)
		{
			// Odd and even sizes, wider than a copy tile, so that the tiled copy is covered.
			foreach ((int w, int h) in new[] { (3, 2), (70, 67), (1, 5) })
			{
				using (Bitmap b = new Bitmap(w, h, PixelFormat.Format32bppArgb))
				{
					for (int y = 0; y < h; y++)
					{
						for (int x = 0; x < w; x++)
						{
							b.SetPixel(x, y, Color.FromArgb(255, x % 256, y % 256, (x * 7 + y * 3) % 256));
						}
					}
					Color[,] expected = Grid(b);
					int value = (int)type;
					for (int i = 0; i < (value & 3); i++)
					{
						expected = Rotate90Clockwise(expected);
					}
					if ((value & 4) != 0)
					{
						expected = MirrorHorizontally(expected);
					}
					b.RotateFlip(type);
					Assert.Equal(expected.GetLength(0), b.Width);
					Assert.Equal(expected.GetLength(1), b.Height);
					Color[,] actual = Grid(b);
					for (int y = 0; y < b.Height; y++)
					{
						for (int x = 0; x < b.Width; x++)
						{
							Assert.Equal(expected[x, y], actual[x, y]);
						}
					}
				}
			}
		}

		[Fact]
		public void RotateFlip_aliases_name_the_same_operation()
		{
			Assert.Equal(RotateFlipType.Rotate180FlipX, RotateFlipType.RotateNoneFlipY);
			Assert.Equal(RotateFlipType.Rotate270FlipX, RotateFlipType.Rotate90FlipY);
			Assert.Equal(RotateFlipType.Rotate180FlipNone, RotateFlipType.RotateNoneFlipXY);
			using (Bitmap a = Letters())
			using (Bitmap b = Letters())
			{
				a.RotateFlip(RotateFlipType.Rotate90FlipY);
				b.RotateFlip(RotateFlipType.Rotate90FlipNone);
				b.RotateFlip(RotateFlipType.RotateNoneFlipY);
				AssertGrid(a, 2, 3, F, C, E, B, D, A);
				AssertGrid(b, 2, 3, F, C, E, B, D, A);
			}
		}

		[Theory]
		[InlineData(PixelFormat.Format32bppArgb)]
		[InlineData(PixelFormat.Format32bppPArgb)]
		[InlineData(PixelFormat.Format32bppRgb)]
		[InlineData(PixelFormat.Format24bppRgb)]
		public void RotateFlip_keeps_the_pixel_format_and_the_raw_pixels(PixelFormat format)
		{
			using (Bitmap b = new Bitmap(2, 1, format))
			{
				b.SetPixel(0, 0, Color.FromArgb(200, 10, 20, 30));
				b.SetPixel(1, 0, Color.FromArgb(100, 40, 50, 60));
				uint left = Raw(b, 0, 0);
				uint right = Raw(b, 1, 0);
				b.RotateFlip(RotateFlipType.Rotate90FlipNone);
				Assert.Equal(format, b.PixelFormat);
				Assert.Equal(new Size(1, 2), b.Size);
				Assert.Equal(left, Raw(b, 0, 0));
				Assert.Equal(right, Raw(b, 0, 1));
			}
		}

		[Fact]
		public void RotateFlip_is_refused_while_a_region_is_locked()
		{
			using (Bitmap b = Letters())
			{
				BitmapData data = b.LockBits(new Rectangle(0, 0, 3, 2), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
				Assert.Throws<InvalidOperationException>(() => b.RotateFlip(RotateFlipType.Rotate90FlipNone));
				b.UnlockBits(data);
				b.RotateFlip(RotateFlipType.Rotate90FlipNone);
			}
		}

		[Fact]
		public void RotateFlip_rejects_values_outside_the_enum_range()
		{
			using (Bitmap b = Letters())
			{
				Assert.Throws<ArgumentException>(() => b.RotateFlip((RotateFlipType)8));
			}
		}
	}
}
