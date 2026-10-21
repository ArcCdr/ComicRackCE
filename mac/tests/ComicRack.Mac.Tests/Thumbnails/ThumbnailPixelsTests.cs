using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Styling;
using SkiaSharp;
using ComicRack.Mac.Controls;
using ComicRack.Mac.Thumbnails;
using Xunit;

namespace ComicRack.Mac.Tests.Thumbnails
{
	/// <summary>Decoding of the Core's thumbnail bytes, the placeholder, and the opaque detection of <see cref="PixelBlock"/>.</summary>
	public class ThumbnailPixelsTests
	{
		private static byte[] Encode(SKBitmap bitmap, SKEncodedImageFormat format, int quality = 90)
		{
			using (SKImage image = SKImage.FromBitmap(bitmap))
			using (SKData data = image.Encode(format, quality))
			{
				return data.ToArray();
			}
		}

		private static SKBitmap Gradient(int width, int height, SKAlphaType alpha = SKAlphaType.Opaque)
		{
			var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, alpha));
			for (int y = 0; y < height; y++)
			{
				for (int x = 0; x < width; x++)
				{
					bitmap.SetPixel(x, y, new SKColor((byte)(x * 255 / width), (byte)(y * 255 / height), 128, 255));
				}
			}
			return bitmap;
		}

		[Fact]
		public void A_jpeg_is_decoded_straight_into_an_opaque_bitmap_at_the_target_height()
		{
			using (SKBitmap source = Gradient(340, 512))
			{
				byte[] jpeg = Encode(source, SKEncodedImageFormat.Jpeg);
				Gc.Flush();
				long live = Gc.Live;
				PixelBlock block = ThumbnailPixels.Decode(jpeg, 288);
				try
				{
					Assert.Equal(live + 1, Gc.Live); // the reduction did not leave the full size bitmap behind
					Assert.Equal(288, block.Height);
					Assert.Equal(191, block.Width); // 340 * 288 / 512 = 191.25
					Assert.Equal(SKAlphaType.Opaque, block.AlphaType);
					using (SKImage image = block.CreateImage())
					using (SKPixmap pixmap = image.PeekPixels())
					{
						Assert.Equal(SKColorType.Bgra8888, pixmap.ColorType);
						Assert.Equal(block.PixelsAddress, pixmap.GetPixels()); // no copy between the decode and the image
						SKColor topLeft = pixmap.GetPixelColor(2, 2);
						SKColor bottomRight = pixmap.GetPixelColor(188, 285);
						Assert.True(bottomRight.Red > topLeft.Red + 100);
						Assert.True(bottomRight.Green > topLeft.Green + 100);
					}
				}
				finally
				{
					block.Release();
				}
				Assert.Equal(live, Gc.Live);
			}
		}

		[Fact]
		public void A_picture_not_taller_than_the_target_is_not_enlarged()
		{
			using (SKBitmap source = Gradient(100, 150))
			{
				PixelBlock block = ThumbnailPixels.Decode(Encode(source, SKEncodedImageFormat.Png), 512);
				try
				{
					Assert.Equal(150, block.Height);
					Assert.Equal(100, block.Width);
				}
				finally
				{
					block.Release();
				}
			}
		}

		[Fact]
		public void A_png_with_real_transparency_keeps_it_and_one_without_is_viewed_as_opaque()
		{
			using (SKBitmap translucent = Gradient(40, 60, SKAlphaType.Premul))
			{
				translucent.SetPixel(3, 3, new SKColor(200, 10, 10, 100));
				PixelBlock block = ThumbnailPixels.Decode(Encode(translucent, SKEncodedImageFormat.Png), 60);
				try
				{
					Assert.Equal(SKAlphaType.Premul, block.AlphaType);
				}
				finally
				{
					block.Release();
				}
			}
			using (SKBitmap flat = Gradient(40, 60, SKAlphaType.Premul)) // an alpha channel, but every pixel is opaque
			{
				PixelBlock block = ThumbnailPixels.Decode(Encode(flat, SKEncodedImageFormat.Png), 60);
				try
				{
					Assert.Equal(SKAlphaType.Opaque, block.AlphaType);
				}
				finally
				{
					block.Release();
				}
			}
		}

		[Fact]
		public void Bad_data_is_an_error_not_a_crash_and_leaks_nothing()
		{
			Gc.Flush();
			long live = Gc.Live;
			Assert.Throws<InvalidDataException>(() => ThumbnailPixels.Decode(null, 100));
			Assert.Throws<InvalidDataException>(() => ThumbnailPixels.Decode(new byte[0], 100));
			Assert.Throws<InvalidDataException>(() => ThumbnailPixels.Decode(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }, 100));
			using (SKBitmap source = Gradient(200, 300))
			{
				byte[] jpeg = Encode(source, SKEncodedImageFormat.Jpeg);
				byte[] truncated = new byte[jpeg.Length / 3];
				Array.Copy(jpeg, truncated, truncated.Length);
				Assert.Throws<InvalidDataException>(() => ThumbnailPixels.Decode(truncated, 100));
			}
			Assert.Equal(live, Gc.Live);
		}

		[Fact]
		public void Decoding_can_be_cancelled_and_leaks_nothing()
		{
			Gc.Flush();
			long live = Gc.Live;
			using (SKBitmap source = Gradient(340, 512))
			using (var cts = new System.Threading.CancellationTokenSource())
			{
				cts.Cancel();
				Assert.Throws<OperationCanceledException>(() => ThumbnailPixels.Decode(Encode(source, SKEncodedImageFormat.Jpeg), 200, cts.Token));
			}
			Assert.Equal(live, Gc.Live);
		}

		[Fact]
		public void A_large_reduction_of_fine_stripes_does_not_alias()
		{
			// One pixel stripes with period 3 (two white, one black: mean 170) reduced 8:1. The plain cubic filter of Skia gives 119..255
			// on this kind of picture (see the quality warning in the M1.2c facts); averaging with mipmaps stays close to the mean.
			using (var source = new SKBitmap(new SKImageInfo(192, 384, SKColorType.Bgra8888, SKAlphaType.Opaque)))
			{
				for (int y = 0; y < 384; y++)
				{
					for (int x = 0; x < 192; x++)
					{
						source.SetPixel(x, y, x % 3 == 0 ? SKColors.Black : SKColors.White);
					}
				}
				PixelBlock block = ThumbnailPixels.Decode(Encode(source, SKEncodedImageFormat.Png), 48);
				try
				{
					Assert.Equal(24, block.Width);
					Assert.Equal(48, block.Height);
					using (SKImage image = block.CreateImage())
					using (SKPixmap pixmap = image.PeekPixels())
					{
						int min = 255;
						int max = 0;
						for (int y = 2; y < 46; y++)
						{
							for (int x = 2; x < 22; x++)
							{
								int v = pixmap.GetPixelColor(x, y).Red;
								min = Math.Min(min, v);
								max = Math.Max(max, v);
							}
						}
						Assert.InRange(min, 145, 200);
						Assert.InRange(max, 145, 200);
					}
				}
				finally
				{
					block.Release();
				}
			}
		}

		[Theory]
		[InlineData(216, 144)]
		[InlineData(512, 341)]
		[InlineData(38, 25)]
		public void The_placeholder_is_a_2_to_3_tile_with_a_cross(int height, int width)
		{
			PixelBlock block = ThumbnailPixels.CreatePlaceholder(height);
			try
			{
				Assert.Equal(height, block.Height);
				Assert.Equal(width, block.Width);
				Assert.Equal(SKAlphaType.Opaque, block.AlphaType);
				using (SKImage image = block.CreateImage())
				using (SKPixmap pixmap = image.PeekPixels())
				{
					SKColor corner = pixmap.GetPixelColor(2, 2);
					SKColor centre = pixmap.GetPixelColor(width / 2, height / 2);
					Assert.NotEqual(corner, centre); // the cross crosses in the middle
				}
			}
			finally
			{
				block.Release();
			}
		}

		private static SKColor Inside(PixelBlock block, int x, int y)
		{
			using (SKImage image = block.CreateImage())
			using (SKPixmap pixmap = image.PeekPixels())
			{
				return pixmap.GetPixelColor(x, y);
			}
		}

		/// <summary>RV8: the colours of the placeholder are the chrome layer's, one look per appearance, not literals of the decoder.</summary>
		[Fact]
		public void The_placeholder_is_drawn_in_the_colours_of_the_look_it_is_given()
		{
			PixelBlock light = ThumbnailPixels.CreatePlaceholder(216, ThumbnailPlaceholderLook.Light);
			PixelBlock dark = ThumbnailPixels.CreatePlaceholder(216, ThumbnailPlaceholderLook.Dark);
			try
			{
				// the tile, one unit inside the frame and away from the cross
				Assert.Equal(new SKColor(0xDC, 0xDC, 0xDC), Inside(light, 20, 20));
				Assert.Equal(new SKColor(0x3A, 0x3A, 0x3A), Inside(dark, 20, 20));
				var custom = new ThumbnailPlaceholderLook(0xFF102030, 0xFF405060, 0xFF708090);
				PixelBlock mine = ThumbnailPixels.CreatePlaceholder(216, custom);
				try
				{
					Assert.Equal(new SKColor(0x10, 0x20, 0x30), Inside(mine, 20, 20));
					Assert.Equal(new SKColor(0x40, 0x50, 0x60), Inside(mine, 0, 100)); // the frame is on the first column
				}
				finally
				{
					mine.Release();
				}
			}
			finally
			{
				light.Release();
				dark.Release();
			}
		}

		[Fact]
		public async Task The_theme_defines_the_placeholder_look_for_both_appearances_and_it_follows_the_appearance_of_the_application()
		{
			await Ui.Run(() =>
			{
				Application app = Application.Current;
				ThemeVariant before = app.RequestedThemeVariant;
				try
				{
					// what the theme file says is what the fallback values say (so a process without the theme draws the same)
					AssertSame(ThumbnailPlaceholderLook.Light, ThumbnailPlaceholderLook.For(ThemeVariant.Light));
					AssertSame(ThumbnailPlaceholderLook.Dark, ThumbnailPlaceholderLook.For(ThemeVariant.Dark));
					Assert.NotEqual(ThumbnailPlaceholderLook.Light.Tile, ThumbnailPlaceholderLook.Dark.Tile);
					app.RequestedThemeVariant = ThemeVariant.Dark;
					AssertSame(ThumbnailPlaceholderLook.Dark, ThumbnailPlaceholderLook.Current);
					app.RequestedThemeVariant = ThemeVariant.Light;
					AssertSame(ThumbnailPlaceholderLook.Light, ThumbnailPlaceholderLook.Current);
					app.RequestedThemeVariant = ThemeVariant.Dark;
					PixelBlock block = ThumbnailPixels.CreatePlaceholder(216);
					try
					{
						Assert.Equal(new SKColor(0x3A, 0x3A, 0x3A), Inside(block, 20, 20));
					}
					finally
					{
						block.Release();
					}
				}
				finally
				{
					app.RequestedThemeVariant = before;
				}
				return Task.CompletedTask;
			});
		}

		private static void AssertSame(ThumbnailPlaceholderLook expected, ThumbnailPlaceholderLook actual)
		{
			Assert.Equal(expected.Tile, actual.Tile);
			Assert.Equal(expected.Frame, actual.Frame);
			Assert.Equal(expected.Cross, actual.Cross);
		}

		[Fact]
		public void An_unpremultiplied_bitmap_whose_pixels_are_all_opaque_is_viewed_as_opaque_without_a_copy()
		{
			var bitmap = Gradient(64, 48, SKAlphaType.Unpremul);
			IntPtr pixels = bitmap.GetPixels();
			PixelBlock block = PixelBlock.FromBitmap(bitmap, onFree: null, detectOpaque: true);
			try
			{
				Assert.Equal(SKAlphaType.Opaque, block.AlphaType);
				Assert.Equal(pixels, block.PixelsAddress);
				using (SKImage image = block.CreateImage())
				using (SKPixmap pixmap = image.PeekPixels())
				{
					Assert.Equal(SKAlphaType.Opaque, image.AlphaType);
					Assert.Equal(pixels, pixmap.GetPixels());
				}
			}
			finally
			{
				block.Release();
			}
		}

		[Fact]
		public void One_translucent_pixel_keeps_the_alpha_type_and_detection_is_off_by_default()
		{
			var translucent = Gradient(64, 48, SKAlphaType.Unpremul);
			translucent.SetPixel(10, 47, new SKColor(1, 2, 3, 254)); // the last row is scanned too
			PixelBlock a = PixelBlock.FromBitmap(translucent, onFree: null, detectOpaque: true);
			var flat = Gradient(64, 48, SKAlphaType.Unpremul);
			PixelBlock b = PixelBlock.FromBitmap(flat); // no detection requested
			try
			{
				Assert.Equal(SKAlphaType.Unpremul, a.AlphaType);
				Assert.Equal(SKAlphaType.Unpremul, b.AlphaType);
			}
			finally
			{
				a.Release();
				b.Release();
			}
		}

		[Fact]
		public void The_pixels_are_freed_with_the_last_reference_and_the_on_free_action_runs_once()
		{
			int freed = 0;
			Gc.Flush();
			long live = Gc.Live;
			var block = PixelBlock.FromBitmap(Gradient(10, 10), onFree: () => freed++);
			SKImage first = block.CreateImage();
			SKImage second = block.CreateImage();
			Assert.Equal(3, block.References);
			block.Release(); // the owner is done
			Assert.False(block.IsFreed);
			first.Dispose();
			Assert.False(block.IsFreed);
			Assert.Equal(0, freed);
			second.Dispose();
			Assert.True(block.IsFreed);
			Assert.Equal(1, freed);
			Assert.Equal(live, Gc.Live);
			Assert.Throws<ObjectDisposedException>(() => block.CreateImage());
			first.Dispose(); // disposing again changes nothing
			Assert.Equal(1, freed);
		}
	}
}
