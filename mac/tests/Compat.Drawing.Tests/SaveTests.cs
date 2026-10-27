using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using Xunit;
using static Compat.Drawing.Tests.TestImages;

namespace Compat.Drawing.Tests
{
	/// <summary>Image.Save: the three overloads, PNG and JPEG output, JPEG quality.</summary>
	public class SaveTests
	{
		private static byte[] Save(Image image, ImageFormat format)
		{
			using (MemoryStream stream = new MemoryStream())
			{
				image.Save(stream, format);
				return stream.ToArray();
			}
		}

		private static int PngColorType(byte[] png) => png[25];

		/// <summary>What BitmapExtensions.SaveImage in the Core does.</summary>
		private static byte[] SaveWithQuality(Image image, int quality, int? colorDepth = null)
		{
			ImageCodecInfo codec = ImageCodecInfo.GetImageEncoders().FirstOrDefault(ici => ici.FormatID == ImageFormat.Jpeg.Guid);
			int count = colorDepth.HasValue ? 2 : 1;
			using (EncoderParameters parameters = new EncoderParameters(count))
			using (MemoryStream stream = new MemoryStream())
			{
				parameters.Param[--count] = new EncoderParameter(Encoder.Quality, quality);
				if (colorDepth.HasValue)
				{
					parameters.Param[--count] = new EncoderParameter(Encoder.ColorDepth, colorDepth.Value);
				}
				image.Save(stream, codec, parameters);
				return stream.ToArray();
			}
		}

		[Fact]
		public void Save_png_to_a_stream_round_trips_not_premultiplied_pixels_exactly()
		{
			using (Bitmap b = new Bitmap(4, 3, PixelFormat.Format32bppArgb))
			{
				for (int y = 0; y < 3; y++)
				{
					for (int x = 0; x < 4; x++)
					{
						b.SetPixel(x, y, Color.FromArgb(40 + x * 50, 200 - y * 30, x * 20, 9 + y));
					}
				}
				byte[] png = Save(b, ImageFormat.Png);
				Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png.Take(4).ToArray());
				Assert.Equal(6, PngColorType(png));
				using (Bitmap back = (Bitmap)Image.FromStream(ToStream(png)))
				{
					Assert.Equal(PixelFormat.Format32bppArgb, back.PixelFormat);
					for (int y = 0; y < 3; y++)
					{
						for (int x = 0; x < 4; x++)
						{
							Assert.Equal(b.GetPixel(x, y), back.GetPixel(x, y));
						}
					}
				}
			}
		}

		[Fact]
		public void Save_png_of_a_premultiplied_bitmap_writes_the_colors_not_the_premultiplied_values()
		{
			using (Bitmap b = new Bitmap(2, 1, PixelFormat.Format32bppPArgb))
			{
				b.SetPixel(0, 0, Color.FromArgb(128, 255, 0, 0));
				b.SetPixel(1, 0, Color.FromArgb(255, 1, 2, 3));
				using (Bitmap back = (Bitmap)Image.FromStream(ToStream(Save(b, ImageFormat.Png))))
				{
					Assert.Equal(PixelFormat.Format32bppArgb, back.PixelFormat);
					Assert.Equal(Color.FromArgb(128, 255, 0, 0), back.GetPixel(0, 0));
					Assert.Equal(Color.FromArgb(255, 1, 2, 3), back.GetPixel(1, 0));
				}
			}
		}

		[Theory]
		[InlineData(PixelFormat.Format24bppRgb)]
		[InlineData(PixelFormat.Format32bppRgb)]
		public void Save_png_of_an_opaque_bitmap_writes_24_bit_rgb(PixelFormat format)
		{
			using (Bitmap b = GradientBitmap(9, 7, format))
			{
				byte[] png = Save(b, ImageFormat.Png);
				Assert.Equal(2, PngColorType(png));
				using (Bitmap back = (Bitmap)Image.FromStream(ToStream(png)))
				{
					Assert.Equal(PixelFormat.Format24bppRgb, back.PixelFormat);
					for (int y = 0; y < 7; y++)
					{
						for (int x = 0; x < 9; x++)
						{
							Assert.Equal(b.GetPixel(x, y), back.GetPixel(x, y));
						}
					}
				}
			}
		}

		[Fact]
		public void Save_jpeg_to_a_stream_writes_a_decodable_jpeg_at_the_default_quality()
		{
			using (Bitmap b = GradientBitmap(48, 32))
			{
				byte[] jpeg = Save(b, ImageFormat.Jpeg);
				Assert.Equal(new byte[] { 0xFF, 0xD8 }, jpeg.Take(2).ToArray());
				Assert.Equal(new byte[] { 0xFF, 0xD9 }, jpeg.Skip(jpeg.Length - 2).ToArray());
				using (Bitmap back = (Bitmap)Image.FromStream(ToStream(jpeg)))
				{
					Assert.Equal(PixelFormat.Format24bppRgb, back.PixelFormat);
					Assert.Equal(b.Size, back.Size);
					AssertColor(b.GetPixel(10, 10), back.GetPixel(10, 10), 12);
					AssertColor(b.GetPixel(40, 25), back.GetPixel(40, 25), 12);
				}
				// The default is the GDI+ default quality, 75.
				Assert.Equal(SaveWithQuality(b, 75).Length, jpeg.Length);
			}
		}

		[Fact]
		public void Save_to_a_file_writes_the_file()
		{
			string file = Path.Combine(Path.GetTempPath(), "cyo-compat-drawing-" + Guid.NewGuid().ToString("N") + ".png");
			try
			{
				using (Bitmap b = GradientBitmap(5, 4))
				{
					b.Save(file, ImageFormat.Png);
				}
				using (Image back = Image.FromFile(file))
				{
					Assert.Equal(new Size(5, 4), back.Size);
				}
				using (Bitmap b = GradientBitmap(5, 4))
				{
					Assert.Throws<ArgumentNullException>(() => b.Save((string)null, ImageFormat.Png));
				}
			}
			finally
			{
				File.Delete(file);
			}
		}

		[Fact]
		public void Save_with_a_codec_and_a_quality_parameter_changes_the_jpeg_size()
		{
			using (Bitmap noise = NoiseBitmap(160, 120))
			{
				int low = SaveWithQuality(noise, 10).Length;
				int mid = SaveWithQuality(noise, 50).Length;
				int high = SaveWithQuality(noise, 95).Length;
				Assert.True(low < mid, $"quality 10 ({low} bytes) should be smaller than quality 50 ({mid} bytes)");
				Assert.True(mid < high, $"quality 50 ({mid} bytes) should be smaller than quality 95 ({high} bytes)");
				Assert.True(high > low * 2, $"quality 95 ({high} bytes) should be much larger than quality 10 ({low} bytes)");

				// The Core always passes the quality it was given; the stored image gets better with the quality, too.
				using (Bitmap lowBack = (Bitmap)Image.FromStream(ToStream(SaveWithQuality(noise, 10))))
				using (Bitmap highBack = (Bitmap)Image.FromStream(ToStream(SaveWithQuality(noise, 95))))
				{
					Assert.True(Distance(noise, highBack) < Distance(noise, lowBack));
				}
			}
		}

		private static long Distance(Bitmap a, Bitmap b)
		{
			long sum = 0;
			for (int y = 0; y < a.Height; y += 3)
			{
				for (int x = 0; x < a.Width; x += 3)
				{
					Color p = a.GetPixel(x, y);
					Color q = b.GetPixel(x, y);
					sum += Math.Abs(p.R - q.R) + Math.Abs(p.G - q.G) + Math.Abs(p.B - q.B);
				}
			}
			return sum;
		}

		[Fact]
		public void Save_with_a_codec_accepts_color_depth_and_clamps_the_quality()
		{
			using (Bitmap b = GradientBitmap(20, 20))
			{
				Assert.Equal(SaveWithQuality(b, 80).Length, SaveWithQuality(b, 80, colorDepth: 24).Length);
				Assert.Equal(SaveWithQuality(b, 100).Length, SaveWithQuality(b, 5000).Length);
				Assert.Equal(SaveWithQuality(b, 0).Length, SaveWithQuality(b, -20).Length);
			}
		}

		[Fact]
		public void Save_with_a_codec_but_no_parameters_uses_the_default_quality()
		{
			using (Bitmap b = GradientBitmap(20, 20))
			using (MemoryStream a = new MemoryStream())
			using (MemoryStream c = new MemoryStream())
			{
				ImageCodecInfo jpeg = ImageCodecInfo.GetImageEncoders().First(ici => ici.FormatID == ImageFormat.Jpeg.Guid);
				b.Save(a, jpeg, null);
				b.Save(c, ImageFormat.Jpeg);
				Assert.Equal(c.ToArray(), a.ToArray());
			}
		}

		[Fact]
		public void Save_with_a_png_codec_writes_a_png_and_ignores_the_quality()
		{
			using (Bitmap b = GradientBitmap(20, 20, PixelFormat.Format32bppArgb))
			using (EncoderParameters parameters = new EncoderParameters(1))
			using (MemoryStream stream = new MemoryStream())
			{
				parameters.Param[0] = new EncoderParameter(Encoder.Quality, 10L);
				ImageCodecInfo png = ImageCodecInfo.GetImageEncoders().First(ici => ici.FormatID == ImageFormat.Png.Guid);
				b.Save(stream, png, parameters);
				Assert.Equal(Save(b, ImageFormat.Png), stream.ToArray());
			}
		}

		[Fact]
		public void Save_jpeg_ignores_alpha_and_keeps_the_color_channels()
		{
			foreach (PixelFormat format in new[] { PixelFormat.Format32bppArgb, PixelFormat.Format32bppPArgb })
			{
				using (Bitmap b = new Bitmap(16, 16, format))
				{
					for (int y = 0; y < 16; y++)
					{
						for (int x = 0; x < 16; x++)
						{
							b.SetPixel(x, y, Color.FromArgb(128, 255, 0, 0));
						}
					}
					using (Bitmap back = (Bitmap)Image.FromStream(ToStream(SaveWithQuality(b, 100))))
					{
						Color c = back.GetPixel(8, 8);
						Assert.Equal(255, c.A);
						Assert.InRange(c.R, 245, 255);
						Assert.InRange(c.G, 0, 10);
						Assert.InRange(c.B, 0, 10);
					}
				}
			}
		}

		[Fact]
		public void Save_reports_bad_arguments_and_formats_without_an_encoder()
		{
			using (Bitmap b = GradientBitmap(4, 4))
			using (MemoryStream stream = new MemoryStream())
			{
				Assert.Throws<ArgumentNullException>(() => b.Save(stream, (ImageFormat)null));
				Assert.Throws<ArgumentNullException>(() => b.Save((Stream)null, ImageFormat.Png));
				Assert.Throws<ArgumentNullException>(() => b.Save(stream, (ImageCodecInfo)null, new EncoderParameters(1)));
				foreach (ImageFormat format in new[] { ImageFormat.Bmp, ImageFormat.Gif, ImageFormat.Tiff })
				{
					NotSupportedException error = Assert.Throws<NotSupportedException>(() => b.Save(stream, format));
					Assert.Contains(format.ToString(), error.Message);
				}
				Assert.Equal(0, stream.Length);
			}
		}

		[Fact]
		public void Save_survives_a_large_image_in_every_pixel_format()
		{
			foreach (PixelFormat format in AllFormats)
			{
				using (Bitmap b = new Bitmap(1200, 1800, format))
				{
					Assert.True(Save(b, ImageFormat.Jpeg).Length > 100);
					Assert.True(Save(b, ImageFormat.Png).Length > 100);
				}
			}
		}
	}
}
