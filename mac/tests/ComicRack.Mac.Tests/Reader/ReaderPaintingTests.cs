using System;
using System.IO;
using Avalonia;
using ComicRack.Mac.Reader;
using SkiaSharp;
using Xunit;

namespace ComicRack.Mac.Tests.Reader
{
	/// <summary>How a page is resampled: the filter the scale selects, and what it does to pixels (on a raster canvas, as Skia draws without a GPU).</summary>
	public class ReaderPaintingTests
	{
		[Theory]
		[InlineData(1.0, "nearest")]
		[InlineData(1.0002, "nearest")]
		[InlineData(2.0, "mitchell")]
		[InlineData(1.2, "mitchell")]
		[InlineData(0.99, "catmull-rom")]
		[InlineData(0.5, "catmull-rom")]
		[InlineData(0.49, "mipmap")]
		[InlineData(0.125, "mipmap")]
		public void The_filter_depends_on_the_scale(double scale, string expected)
		{
			Assert.Equal(expected, ReaderSampling.Describe(scale));
			SKSamplingOptions options = ReaderSampling.For(scale);
			switch (expected)
			{
				case "nearest":
					Assert.Equal(SKFilterMode.Nearest, options.Filter);
					Assert.False(options.UseCubic);
					break;
				case "mitchell":
					Assert.True(options.UseCubic);
					Assert.Equal(1f / 3, options.Cubic.B, 3);
					break;
				case "catmull-rom":
					Assert.True(options.UseCubic);
					Assert.Equal(0f, options.Cubic.B, 3);
					Assert.Equal(0.5f, options.Cubic.C, 3);
					break;
				default:
					Assert.False(options.UseCubic);
					Assert.Equal(SKFilterMode.Linear, options.Filter);
					Assert.Equal(SKMipmapMode.Linear, options.Mipmap);
					break;
			}
		}

		private static SKImage Stripes(int size, int period)
		{
			// vertical stripes: 'period' pixels, one of them white and the others black, so the true mean is 255 / period
			using (var bitmap = new SKBitmap(new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Opaque)))
			{
				for (int y = 0; y < size; y++)
				{
					for (int x = 0; x < size; x++)
					{
						bitmap.SetPixel(x, y, x % period == 0 ? SKColors.White : SKColors.Black);
					}
				}
				return SKImage.FromBitmap(bitmap);
			}
		}

		private static (int min, int max) RangeOfRow(SKBitmap bitmap, int y, int from, int to)
		{
			int min = 255, max = 0;
			for (int x = from; x < to; x++)
			{
				int v = bitmap.GetPixel(x, y).Red;
				min = Math.Min(min, v);
				max = Math.Max(max, v);
			}
			return (min, max);
		}

		/// <summary>Draws the stripes reduced by <paramref name="ratio"/> with the given sampling and returns the range of one row (away from the edges).</summary>
		private static (int min, int max) Reduce(SKImage stripes, int ratio, SKSamplingOptions sampling)
		{
			int dest = stripes.Width / ratio;
			using (var target = new SKBitmap(new SKImageInfo(dest, dest, SKColorType.Bgra8888, SKAlphaType.Opaque)))
			using (var canvas = new SKCanvas(target))
			{
				canvas.DrawImage(stripes, new SKRect(0, 0, dest, dest), sampling);
				return RangeOfRow(target, dest / 2, 4, dest - 4);
			}
		}

		[Fact]
		public void A_large_reduction_is_averaged_where_the_cubic_filters_alias()
		{
			// M1.2c measured this with the Drawing shim: stripes of period 3 (mean 85 for one white pixel in three) reduced 8:1.
			using (SKImage stripes = Stripes(960, 3))
			{
				var mitchell = Reduce(stripes, 8, new SKSamplingOptions(SKCubicResampler.Mitchell));
				var chosen = Reduce(stripes, 8, ReaderSampling.For(1.0 / 8));
				// the cubic filter skips source pixels, so the stripes survive as a visible pattern ...
				Assert.True(mitchell.max - mitchell.min >= 60, $"Mitchell range {mitchell.min}..{mitchell.max}");
				// ... while the chosen filter gives a grey close to the true mean (85); a box of 8 pixels over a pattern of period 3 holds
				// 2 or 3 white pixels, so it is not constant: 64..96 at best
				Assert.InRange(chosen.min, 55, 110);
				Assert.InRange(chosen.max, 55, 110);
				Assert.True(chosen.max - chosen.min < mitchell.max - mitchell.min);
			}
		}

		[Fact]
		public void A_mild_reduction_stays_sharp_a_one_pixel_line_is_still_a_line()
		{
			// 1000 -> 800 (scale 0.8): a black line of one pixel on white must stay clearly darker than the paper.
			using (var bitmap = new SKBitmap(new SKImageInfo(1000, 1000, SKColorType.Bgra8888, SKAlphaType.Opaque)))
			{
				bitmap.Erase(SKColors.White);
				for (int y = 0; y < 1000; y++)
				{
					bitmap.SetPixel(500, y, SKColors.Black);
				}
				using (SKImage image = SKImage.FromBitmap(bitmap))
				using (var target = new SKBitmap(new SKImageInfo(800, 800, SKColorType.Bgra8888, SKAlphaType.Opaque)))
				using (var canvas = new SKCanvas(target))
				{
					double scale = ReaderPainter.DrawPage(canvas, image, new Rect(0, 0, 800, 800));
					Assert.Equal(0.8, scale, 6);
					(int min, int max) = RangeOfRow(target, 400, 390, 410);
					Assert.True(min < 120, "the line keeps its contrast: darkest " + min);
					Assert.True(max > 250, "the paper stays white: brightest " + max);
				}
			}
		}

		[Fact]
		public void A_page_at_scale_one_is_copied_exactly_even_if_the_rectangle_has_a_fractional_position()
		{
			using (var bitmap = new SKBitmap(new SKImageInfo(64, 48, SKColorType.Bgra8888, SKAlphaType.Opaque)))
			{
				for (int y = 0; y < 48; y++)
				{
					for (int x = 0; x < 64; x++)
					{
						bitmap.SetPixel(x, y, new SKColor((byte)(x * 4), (byte)(y * 5), (byte)((x ^ y) * 3), 255));
					}
				}
				using (SKImage image = SKImage.FromBitmap(bitmap))
				using (var target = new SKBitmap(new SKImageInfo(100, 80, SKColorType.Bgra8888, SKAlphaType.Opaque)))
				using (var canvas = new SKCanvas(target))
				{
					canvas.Clear(SKColors.Black);
					double scale = ReaderPainter.DrawPage(canvas, image, new Rect(10.4, 7.45, 64, 48));
					Assert.Equal(1.0, scale, 6);
					for (int y = 0; y < 48; y++)
					{
						for (int x = 0; x < 64; x++)
						{
							Assert.Equal(bitmap.GetPixel(x, y), target.GetPixel(10 + x, 7 + y));
						}
					}
				}
			}
		}

		[Fact]
		public void On_a_retina_canvas_the_rectangle_is_in_points_and_the_scale_in_device_pixels()
		{
			using (SKImage image = Stripes(400, 4))
			using (var target = new SKBitmap(new SKImageInfo(400, 400, SKColorType.Bgra8888, SKAlphaType.Opaque)))
			using (var canvas = new SKCanvas(target))
			{
				canvas.Scale(2f); // a 200 x 200 point window at 2x
				double scale = ReaderPainter.DrawPage(canvas, image, new Rect(0, 0, 200, 200));
				Assert.Equal(1.0, scale, 6); // 200 points = 400 device pixels for a 400 pixel page: no resampling at all
				Assert.Equal(SKColors.White, target.GetPixel(0, 5));
				Assert.Equal(SKColors.Black, target.GetPixel(1, 5));
				Assert.Equal(2f, canvas.TotalMatrix.ScaleX); // the canvas is left as it was
			}
		}

		[Fact]
		public void Drawing_a_page_leaves_the_canvas_state_alone()
		{
			using (SKImage image = Stripes(100, 2))
			using (var target = new SKBitmap(new SKImageInfo(100, 100, SKColorType.Bgra8888, SKAlphaType.Opaque)))
			using (var canvas = new SKCanvas(target))
			{
				canvas.Translate(5, 5);
				int saves = canvas.SaveCount;
				SKMatrix before = canvas.TotalMatrix;
				ReaderPainter.DrawPage(canvas, image, new Rect(0, 0, 50, 50));
				Assert.Equal(saves, canvas.SaveCount);
				Assert.Equal(before, canvas.TotalMatrix);
			}
		}

		[Fact]
		public void An_empty_rectangle_draws_nothing()
		{
			using (SKImage image = Stripes(100, 2))
			using (var target = new SKBitmap(new SKImageInfo(20, 20, SKColorType.Bgra8888, SKAlphaType.Opaque)))
			using (var canvas = new SKCanvas(target))
			{
				canvas.Clear(SKColors.Black);
				Assert.Equal(0, ReaderPainter.DrawPage(canvas, image, new Rect(3, 3, 0.2, 0.2)));
				Assert.Equal(SKColors.Black, target.GetPixel(3, 3));
			}
		}
	}
}
