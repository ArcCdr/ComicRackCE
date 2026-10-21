using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Xunit;
using static Compat.Drawing.Tests.TestImages;

namespace Compat.Drawing.Tests
{
	/// <summary>Graphics.DrawImage: every overload, source rectangles, interpolation, alpha and the checkerboard downscale of M0.4.</summary>
	public class DrawImageTests
	{
		private static readonly Color Clear0 = Color.FromArgb(0, 0, 0, 0);

		public static readonly TheoryData<PixelFormat, PixelFormat> FormatPairs = BuildPairs();

		private static TheoryData<PixelFormat, PixelFormat> BuildPairs()
		{
			var data = new TheoryData<PixelFormat, PixelFormat>();
			foreach (PixelFormat from in AllFormats)
			{
				foreach (PixelFormat to in AllFormats)
				{
					data.Add(from, to);
				}
			}
			return data;
		}

		/// <summary>Left half red, right half blue.</summary>
		private static Bitmap RedBlue(int w, int h)
		{
			Bitmap bitmap = new Bitmap(w, h, PixelFormat.Format32bppArgb);
			for (int y = 0; y < h; y++)
			{
				for (int x = 0; x < w; x++)
				{
					bitmap.SetPixel(x, y, x < w / 2 ? Color.Red : Color.Blue);
				}
			}
			return bitmap;
		}

		/// <summary>1-pixel black and white checkerboard.</summary>
		private static Bitmap Checkerboard(int w, int h, PixelFormat format = PixelFormat.Format24bppRgb)
		{
			Bitmap bitmap = new Bitmap(w, h, format);
			for (int y = 0; y < h; y++)
			{
				for (int x = 0; x < w; x++)
				{
					bitmap.SetPixel(x, y, ((x + y) & 1) == 0 ? Color.Black : Color.White);
				}
			}
			return bitmap;
		}

		private static void AssertSameImage(Bitmap expected, Bitmap actual, int tolerance = 0)
		{
			Assert.Equal(expected.Width, actual.Width);
			Assert.Equal(expected.Height, actual.Height);
			for (int y = 0; y < expected.Height; y++)
			{
				for (int x = 0; x < expected.Width; x++)
				{
					AssertColor(expected.GetPixel(x, y), actual.GetPixel(x, y), tolerance);
				}
			}
		}

		// ---- 1:1 copies ------------------------------------------------------------------------------------------------------------

		[Theory]
		[MemberData(nameof(FormatPairs))]
		public void DrawImage_at_natural_size_copies_the_pixels_between_all_format_pairs(PixelFormat from, PixelFormat to)
		{
			using (Bitmap source = GradientBitmap(23, 17, from))
			using (Bitmap target = new Bitmap(23, 17, to))
			{
				using (Graphics g = Graphics.FromImage(target))
				{
					g.DrawImage(source, 0, 0);
				}
				AssertSameImage(source, target);
			}
		}

		[Theory]
		[MemberData(nameof(FormatPairs))]
		public void DrawImage_copies_with_every_interpolation_mode_when_not_scaled(PixelFormat from, PixelFormat to)
		{
			using (Bitmap source = GradientBitmap(16, 16, from))
			foreach (InterpolationMode mode in new[] { InterpolationMode.NearestNeighbor, InterpolationMode.Bilinear, InterpolationMode.HighQualityBilinear, InterpolationMode.HighQualityBicubic })
			{
				using (Bitmap target = new Bitmap(16, 16, to))
				{
					using (Graphics g = Graphics.FromImage(target))
					{
						g.InterpolationMode = mode;
						g.DrawImage(source, new Rectangle(0, 0, 16, 16));
					}
					AssertSameImage(source, target);
				}
			}
		}

		[Fact]
		public void A_translucent_Argb_image_drawn_onto_transparent_stays_the_same_within_one_level()
		{
			using (Bitmap source = new Bitmap(4, 4, PixelFormat.Format32bppArgb))
			using (Bitmap target = new Bitmap(4, 4, PixelFormat.Format32bppArgb))
			{
				for (int i = 0; i < 4; i++)
				{
					source.SetPixel(i, 0, Color.FromArgb(128, 255, 0, 0));
					source.SetPixel(i, 1, Color.FromArgb(64, 10, 200, 90));
					source.SetPixel(i, 2, Color.FromArgb(0, 0, 0, 0));
					source.SetPixel(i, 3, Color.FromArgb(255, 1, 2, 3));
				}
				using (Graphics g = Graphics.FromImage(target))
				{
					g.DrawImage(source, 0, 0);
				}
				AssertSameImage(source, target, 2);
			}
		}

		[Fact]
		public void SourceOver_blends_a_translucent_image_over_an_opaque_background()
		{
			using (Bitmap source = Solid(4, 4, Color.FromArgb(128, 255, 0, 0)))
			using (Bitmap target = Solid(4, 4, Color.White, PixelFormat.Format24bppRgb))
			{
				using (Graphics g = Graphics.FromImage(target))
				{
					g.DrawImage(source, 0, 0);
				}
				AssertColor(Color.FromArgb(255, 255, 127, 127), target.GetPixel(1, 1), 2);
			}
		}

		[Fact]
		public void SourceCopy_replaces_the_pixels_instead_of_blending()
		{
			using (Bitmap source = Solid(4, 4, Color.FromArgb(128, 255, 0, 0)))
			using (Bitmap target = Solid(4, 4, Color.White))
			{
				using (Graphics g = Graphics.FromImage(target))
				{
					g.CompositingMode = CompositingMode.SourceCopy;
					g.DrawImage(source, 0, 0);
				}
				AssertColor(Color.FromArgb(128, 255, 0, 0), target.GetPixel(1, 1), 1);
			}
		}

		// ---- the overloads ---------------------------------------------------------------------------------------------------------

		[Fact]
		public void DrawImage_x_y_places_the_image_at_its_own_size_and_clips_at_the_edges()
		{
			using (Bitmap source = Solid(4, 4, Color.Red))
			using (Bitmap target = new Bitmap(10, 10, PixelFormat.Format32bppArgb))
			{
				using (Graphics g = Graphics.FromImage(target))
				{
					g.DrawImage(source, 3, 2);
					g.DrawImage(source, 8, 8); // partly outside
					g.DrawImage(source, -3, -3); // partly outside
				}
				AssertColor(Color.Red, target.GetPixel(3, 2));
				AssertColor(Color.Red, target.GetPixel(6, 5));
				AssertColor(Clear0, target.GetPixel(7, 5));
				AssertColor(Clear0, target.GetPixel(3, 1));
				AssertColor(Color.Red, target.GetPixel(9, 9));
				AssertColor(Color.Red, target.GetPixel(0, 0));
				AssertColor(Clear0, target.GetPixel(1, 1));
			}
		}

		[Fact]
		public void DrawImage_scales_to_a_Rectangle_a_RectangleF_and_x_y_w_h()
		{
			using (Bitmap source = Solid(2, 2, Color.Green))
			{
				foreach (Action<Graphics> draw in new Action<Graphics>[]
				{
					g => g.DrawImage(source, new Rectangle(3, 4, 6, 5)),
					g => g.DrawImage(source, new RectangleF(3, 4, 6, 5)),
					g => g.DrawImage(source, 3, 4, 6, 5),
					g => g.DrawImage(source, new Rectangle(3, 4, 6, 5), new Rectangle(0, 0, 2, 2), GraphicsUnit.Pixel),
					g => g.DrawImage(source, new Rectangle(3, 4, 6, 5), 0, 0, 2, 2, GraphicsUnit.Pixel),
					g => g.DrawImage(source, new Rectangle(3, 4, 6, 5), 0, 0, 2, 2, GraphicsUnit.Pixel, null),
					g => g.DrawImage(source, new Rectangle(3, 4, 6, 5), 0, 0, 2, 2, GraphicsUnit.Pixel, new ImageAttributes()),
				})
				{
					using (Bitmap target = new Bitmap(12, 12, PixelFormat.Format32bppArgb))
					{
						using (Graphics g = Graphics.FromImage(target))
						{
							draw(g);
						}
						for (int y = 0; y < 12; y++)
						{
							for (int x = 0; x < 12; x++)
							{
								bool inside = x >= 3 && x < 9 && y >= 4 && y < 9;
								AssertColor(inside ? Color.Green : Clear0, target.GetPixel(x, y), 1);
							}
						}
					}
				}
			}
		}

		[Fact]
		public void DrawImage_with_a_source_rectangle_draws_only_that_part()
		{
			using (Bitmap source = RedBlue(10, 4))
			using (Bitmap target = new Bitmap(6, 4, PixelFormat.Format32bppArgb))
			{
				using (Graphics g = Graphics.FromImage(target))
				{
					g.InterpolationMode = InterpolationMode.NearestNeighbor;
					// source columns 3..8 (6 pixels): 2 red, 4 blue
					g.DrawImage(source, new Rectangle(0, 0, 6, 4), 3, 0, 6, 4, GraphicsUnit.Pixel);
				}
				AssertColor(Color.Red, target.GetPixel(0, 0));
				AssertColor(Color.Red, target.GetPixel(1, 3));
				AssertColor(Color.Blue, target.GetPixel(2, 0));
				AssertColor(Color.Blue, target.GetPixel(5, 3));
			}
		}

		[Fact]
		public void The_Rectangle_srcRect_overload_equals_the_int_overload()
		{
			using (Bitmap source = GradientBitmap(20, 20))
			using (Bitmap a = new Bitmap(8, 8, PixelFormat.Format24bppRgb))
			using (Bitmap b = new Bitmap(8, 8, PixelFormat.Format24bppRgb))
			{
				using (Graphics g = Graphics.FromImage(a)) g.DrawImage(source, new Rectangle(0, 0, 8, 8), new Rectangle(5, 6, 8, 8), GraphicsUnit.Pixel);
				using (Graphics g = Graphics.FromImage(b)) g.DrawImage(source, new Rectangle(0, 0, 8, 8), 5, 6, 8, 8, GraphicsUnit.Pixel);
				AssertSameImage(a, b);
				AssertColor(source.GetPixel(5, 6), a.GetPixel(0, 0));
				AssertColor(source.GetPixel(12, 13), a.GetPixel(7, 7));
			}
		}

		[Fact]
		public void A_source_rectangle_that_reaches_outside_the_image_is_clipped_and_the_destination_shrinks_in_proportion()
		{
			using (Bitmap source = Solid(10, 10, Color.Red))
			using (Bitmap target = new Bitmap(40, 20, PixelFormat.Format32bppArgb))
			{
				using (Graphics g = Graphics.FromImage(target))
				{
					g.InterpolationMode = InterpolationMode.NearestNeighbor;
					// source x 5..15 -> dest 0..20 (2x); only x 5..10 exists -> dest 0..10
					g.DrawImage(source, new Rectangle(0, 0, 20, 20), 5, 0, 10, 10, GraphicsUnit.Pixel);
					// source x -5..5 -> dest 20..40; only x 0..5 exists -> dest 30..40
					g.DrawImage(source, new Rectangle(20, 0, 20, 20), -5, 0, 10, 10, GraphicsUnit.Pixel);
				}
				AssertColor(Color.Red, target.GetPixel(9, 5));
				AssertColor(Clear0, target.GetPixel(10, 5));
				AssertColor(Clear0, target.GetPixel(29, 5));
				AssertColor(Color.Red, target.GetPixel(30, 5));
				AssertColor(Color.Red, target.GetPixel(39, 19));
			}
		}

		[Fact]
		public void A_source_rectangle_completely_outside_or_an_empty_one_draws_nothing()
		{
			using (Bitmap source = Solid(4, 4, Color.Red))
			using (Bitmap target = new Bitmap(8, 8, PixelFormat.Format32bppArgb))
			using (Graphics g = Graphics.FromImage(target))
			{
				g.DrawImage(source, new Rectangle(0, 0, 8, 8), 10, 10, 4, 4, GraphicsUnit.Pixel);
				g.DrawImage(source, new Rectangle(0, 0, 8, 8), 0, 0, 0, 4, GraphicsUnit.Pixel);
				g.DrawImage(source, new Rectangle(0, 0, 0, 8), 0, 0, 4, 4, GraphicsUnit.Pixel);
				g.DrawImage(source, 0, 0, 8, 0);
				Assert.Equal(0u, Raw(target, 1, 1));
			}
		}

		[Fact]
		public void Filtering_does_not_read_pixels_outside_the_source_rectangle()
		{
			// A 1-pixel red column taken from a red|blue image and stretched (as DrawShadow does with its edge strips) must not pick up blue.
			foreach (InterpolationMode mode in new[] { InterpolationMode.NearestNeighbor, InterpolationMode.Bilinear, InterpolationMode.HighQualityBilinear, InterpolationMode.HighQualityBicubic })
			{
				using (Bitmap source = RedBlue(8, 8))
				using (Bitmap target = new Bitmap(12, 12, PixelFormat.Format32bppArgb))
				{
					using (Graphics g = Graphics.FromImage(target))
					{
						g.InterpolationMode = mode;
						g.DrawImage(source, new Rectangle(0, 0, 12, 12), 3, 0, 1, 8, GraphicsUnit.Pixel); // column 3 is the last red one
					}
					for (int y = 0; y < 12; y++)
					{
						for (int x = 0; x < 12; x++)
						{
							AssertColor(Color.Red, target.GetPixel(x, y), 1);
						}
					}
				}
			}
		}

		[Fact]
		public void Only_GraphicsUnit_Pixel_is_supported_as_the_source_unit()
		{
			using (Bitmap source = new Bitmap(4, 4))
			using (Bitmap target = new Bitmap(4, 4))
			using (Graphics g = Graphics.FromImage(target))
			{
				Assert.Throws<NotSupportedException>(() => g.DrawImage(source, new Rectangle(0, 0, 4, 4), new Rectangle(0, 0, 4, 4), GraphicsUnit.Point));
				Assert.Throws<NotSupportedException>(() => g.DrawImage(source, new Rectangle(0, 0, 4, 4), 0, 0, 4, 4, GraphicsUnit.Millimeter));
				Assert.Throws<NotSupportedException>(() => g.DrawImage(source, new Rectangle(0, 0, 4, 4), 0, 0, 4, 4, GraphicsUnit.World, null));
			}
		}

		[Fact]
		public void DrawImage_checks_its_image_and_refuses_mirroring()
		{
			using (Bitmap source = new Bitmap(4, 4))
			using (Bitmap target = new Bitmap(4, 4))
			using (Graphics g = Graphics.FromImage(target))
			{
				Assert.Throws<ArgumentNullException>(() => g.DrawImage(null, 0, 0));
				Assert.Throws<ArgumentNullException>(() => g.DrawImage(null, new Rectangle(0, 0, 1, 1)));
				Assert.Throws<ArgumentNullException>(() => g.DrawImage(null, new RectangleF(0, 0, 1, 1)));
				Assert.Throws<ArgumentNullException>(() => g.DrawImage(null, 0, 0, 1, 1));
				Assert.Throws<NotSupportedException>(() => g.DrawImage(source, 4, 0, -4, 4));
				Bitmap disposed = new Bitmap(4, 4);
				disposed.Dispose();
				Assert.Throws<ObjectDisposedException>(() => g.DrawImage(disposed, 0, 0));
			}
		}

		[Fact]
		public void An_image_can_be_drawn_onto_itself()
		{
			using (Bitmap bitmap = new Bitmap(8, 1, PixelFormat.Format32bppArgb))
			{
				for (int x = 0; x < 8; x++)
				{
					bitmap.SetPixel(x, 0, Color.FromArgb(255, x * 30, 0, 0));
				}
				using (Graphics g = Graphics.FromImage(bitmap))
				{
					g.CompositingMode = CompositingMode.SourceCopy;
					g.InterpolationMode = InterpolationMode.NearestNeighbor;
					g.DrawImage(bitmap, new Rectangle(1, 0, 7, 1), 0, 0, 7, 1, GraphicsUnit.Pixel); // shift right by one
				}
				for (int x = 1; x < 8; x++)
				{
					Assert.Equal((x - 1) * 30, bitmap.GetPixel(x, 0).R);
				}
			}
		}

		// ---- interpolation ---------------------------------------------------------------------------------------------------------

		[Fact]
		public void NearestNeighbor_upscaling_makes_no_new_colours_and_bilinear_does()
		{
			using (Bitmap source = new Bitmap(2, 1, PixelFormat.Format24bppRgb))
			{
				source.SetPixel(0, 0, Color.Black);
				source.SetPixel(1, 0, Color.White);
				using (Bitmap nearest = new Bitmap(8, 1, PixelFormat.Format24bppRgb))
				using (Bitmap linear = new Bitmap(8, 1, PixelFormat.Format24bppRgb))
				{
					using (Graphics g = Graphics.FromImage(nearest)) { g.InterpolationMode = InterpolationMode.NearestNeighbor; g.DrawImage(source, new Rectangle(0, 0, 8, 1)); }
					using (Graphics g = Graphics.FromImage(linear)) { g.InterpolationMode = InterpolationMode.Bilinear; g.DrawImage(source, new Rectangle(0, 0, 8, 1)); }
					int linearMid = 0;
					for (int x = 0; x < 8; x++)
					{
						int n = nearest.GetPixel(x, 0).R;
						Assert.True(n == 0 || n == 255, "nearest produced " + n);
						Assert.Equal(x < 4 ? 0 : 255, n);
						int l = linear.GetPixel(x, 0).R;
						if (l > 0 && l < 255)
						{
							linearMid++;
						}
					}
					Assert.True(linearMid >= 3, "bilinear produced only " + linearMid + " intermediate values");
				}
			}
		}

		[Fact]
		public void Every_interpolation_mode_draws_a_smooth_gradient_without_big_errors()
		{
			using (Bitmap source = GradientBitmap(40, 40))
			{
				foreach (InterpolationMode mode in new[] { InterpolationMode.Default, InterpolationMode.Low, InterpolationMode.High, InterpolationMode.Bilinear, InterpolationMode.Bicubic, InterpolationMode.NearestNeighbor, InterpolationMode.HighQualityBilinear, InterpolationMode.HighQualityBicubic })
				{
					using (Bitmap target = new Bitmap(20, 20, PixelFormat.Format24bppRgb))
					{
						using (Graphics g = Graphics.FromImage(target))
						{
							g.InterpolationMode = mode;
							g.DrawImage(source, new Rectangle(0, 0, 20, 20));
						}
						// a half size gradient: pixel (x, y) sits near source (2x + 0.5, 2y + 0.5); the error stays within a few levels
						Color expected = Gradient(10, 10, 20, 20);
						Color mid = source.GetPixel(20, 20);
						Color actual = target.GetPixel(10, 10);
						Assert.InRange(Math.Abs(actual.R - mid.R), 0, 14);
						Assert.InRange(Math.Abs(actual.G - mid.G), 0, 14);
						Assert.InRange((int)actual.B, 126, 130);
						Assert.NotEqual(Color.Empty, expected);
					}
				}
			}
		}

		[Theory]
		[InlineData(InterpolationMode.HighQualityBicubic)]
		[InlineData(InterpolationMode.HighQualityBilinear)]
		[InlineData(InterpolationMode.Bicubic)]
		[InlineData(InterpolationMode.High)]
		public void Checkerboard_downscaled_4_to_1_gives_mid_grey(InterpolationMode mode)
		{
			// The check of M0.4 ("DrawImage downscale with HighQualityBicubic"): GeneXus ignored the mode and returned nearest neighbour (black or white).
			using (Bitmap source = Checkerboard(64, 64))
			using (Bitmap target = new Bitmap(16, 16, PixelFormat.Format24bppRgb))
			{
				using (Graphics g = Graphics.FromImage(target))
				{
					g.InterpolationMode = mode;
					g.DrawImage(source, new Rectangle(0, 0, 16, 16), 0, 0, 64, 64, GraphicsUnit.Pixel);
				}
				for (int y = 0; y < 16; y++)
				{
					for (int x = 0; x < 16; x++)
					{
						Color c = target.GetPixel(x, y);
						Assert.InRange((int)c.R, 126, 130);
						Assert.Equal(c.R, c.G);
						Assert.Equal(c.R, c.B);
					}
				}
			}
		}

		[Fact]
		public void Checkerboard_downscaled_with_NearestNeighbor_stays_black_and_white()
		{
			using (Bitmap source = Checkerboard(64, 64))
			using (Bitmap target = new Bitmap(16, 16, PixelFormat.Format24bppRgb))
			{
				using (Graphics g = Graphics.FromImage(target))
				{
					g.InterpolationMode = InterpolationMode.NearestNeighbor;
					g.DrawImage(source, new Rectangle(0, 0, 16, 16), 0, 0, 64, 64, GraphicsUnit.Pixel);
				}
				for (int y = 0; y < 16; y++)
				{
					for (int x = 0; x < 16; x++)
					{
						int v = target.GetPixel(x, y).R;
						Assert.True(v == 0 || v == 255, "nearest neighbour produced " + v);
					}
				}
			}
		}

		[Fact]
		public void HighQualityBicubic_on_a_checkerboard_in_every_pixel_format_gives_mid_grey()
		{
			foreach (PixelFormat format in AllFormats)
			{
				using (Bitmap source = Checkerboard(32, 32, format))
				using (Bitmap target = new Bitmap(8, 8, PixelFormat.Format32bppArgb))
				{
					using (Graphics g = Graphics.FromImage(target))
					{
						g.InterpolationMode = InterpolationMode.HighQualityBicubic;
						g.DrawImage(source, new Rectangle(0, 0, 8, 8));
					}
					AssertColor(Color.FromArgb(255, 128, 128, 128), target.GetPixel(3, 3), 2);
				}
			}
		}

		// ---- Bitmap(Image, int, int) -----------------------------------------------------------------------------------------------

		[Fact]
		public void Bitmap_from_an_image_with_a_new_size_is_a_Format32bppArgb_scaled_copy()
		{
			using (Bitmap source = Solid(10, 20, Color.FromArgb(255, 30, 60, 90), PixelFormat.Format24bppRgb))
			using (Bitmap scaled = new Bitmap(source, 25, 5))
			{
				Assert.Equal(25, scaled.Width);
				Assert.Equal(5, scaled.Height);
				Assert.Equal(PixelFormat.Format32bppArgb, scaled.PixelFormat);
				for (int y = 0; y < 5; y++)
				{
					for (int x = 0; x < 25; x++)
					{
						AssertColor(Color.FromArgb(255, 30, 60, 90), scaled.GetPixel(x, y), 1);
					}
				}
			}
		}

		[Fact]
		public void Bitmap_from_an_image_keeps_alpha_and_layout()
		{
			using (Bitmap source = RedBlue(8, 4))
			using (Bitmap scaled = new Bitmap(source, 16, 8))
			{
				AssertColor(Color.Red, scaled.GetPixel(1, 1), 1);
				AssertColor(Color.Blue, scaled.GetPixel(14, 6), 1);
				AssertColor(Color.Red, scaled.GetPixel(0, 7), 1);
			}
			using (Bitmap source = Solid(4, 4, Color.FromArgb(100, 200, 50, 20)))
			using (Bitmap same = new Bitmap(source, 4, 4))
			{
				AssertColor(Color.FromArgb(100, 200, 50, 20), same.GetPixel(2, 2), 2);
			}
		}

		[Fact]
		public void Bitmap_from_an_image_at_the_same_size_copies_the_pixels()
		{
			using (Bitmap source = GradientBitmap(13, 9))
			using (Bitmap copy = new Bitmap(source, 13, 9))
			{
				AssertSameImage(source, copy);
			}
		}

		[Fact]
		public void Bitmap_from_an_image_downscales_with_the_default_bilinear_mode()
		{
			using (Bitmap source = Checkerboard(64, 64))
			using (Bitmap scaled = new Bitmap(source, 16, 16))
			{
				AssertColor(Color.FromArgb(255, 128, 128, 128), scaled.GetPixel(5, 5), 2);
			}
		}

		[Fact]
		public void Bitmap_from_an_image_checks_its_arguments_and_leaks_nothing_when_it_fails()
		{
			int before = LiveSettled();
			using (Bitmap source = new Bitmap(4, 4))
			{
				Assert.Throws<ArgumentNullException>(() => new Bitmap(null, 4, 4));
				Assert.Throws<ArgumentException>(() => new Bitmap(source, 0, 4));
				Assert.Throws<ArgumentException>(() => new Bitmap(source, 4, -1));
				Bitmap disposed = new Bitmap(2, 2);
				disposed.Dispose();
				Assert.Throws<ObjectDisposedException>(() => new Bitmap(disposed, 4, 4));
				Assert.Equal(before + 1, Live);
			}
			Assert.Equal(before, Live);
		}

		[Fact]
		public void A_Bitmap_made_from_an_image_releases_its_storage_on_Dispose()
		{
			int before = LiveSettled();
			Bitmap source = new Bitmap(4, 4);
			Bitmap scaled = new Bitmap(source, 8, 8);
			Assert.Equal(before + 2, Live);
			scaled.Dispose();
			source.Dispose();
			Assert.Equal(before, Live);
		}

		[Fact]
		public void A_failed_or_finished_DrawImage_leaves_no_storage_behind()
		{
			int before = LiveSettled();
			using (Bitmap bitmap = new Bitmap(8, 8))
			{
				using (Graphics g = Graphics.FromImage(bitmap))
				{
					g.DrawImage(bitmap, 0, 0); // self draw makes a temporary copy
					g.DrawImage(bitmap, new Rectangle(1, 1, 4, 4), 0, 0, 2, 2, GraphicsUnit.Pixel);
					Assert.Throws<NotSupportedException>(() => g.DrawImage(bitmap, 0, 0, -1, 1));
				}
				Assert.Equal(before + 1, Live);
			}
			Assert.Equal(before, Live);
		}
	}
}
