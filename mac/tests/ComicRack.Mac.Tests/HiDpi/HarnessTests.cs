using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SkiaSharp;
using Xunit;

namespace ComicRack.Mac.Tests.HiDpi
{
	/// <summary>The harness itself: the render scaling reaches the frame, and the helpers that the other HiDPI tests rely on are exact.</summary>
	[Trait("Category", "HiDpi")]
	public class HarnessTests
	{
		private static readonly IBrush Red = new SolidColorBrush(Color.FromRgb(255, 0, 0));
		private static readonly IBrush Blue = new SolidColorBrush(Color.FromRgb(0, 0, 255));

		[Theory]
		[MemberData(nameof(Scales.AllData), MemberType = typeof(Scales))]
		public async Task The_frame_has_the_logical_size_times_the_scale_and_the_content_keeps_its_logical_size(double scale)
		{
			await Ui.Run(() =>
			{
				var box = new Border { Background = Red, Width = 50, Height = 30, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top, Margin = new Thickness(10, 20, 0, 0) };
				var window = new Window { Width = 200, Height = 100, Content = new Border { Background = Blue, Child = box } };
				window.Show();
				Dpi.Apply(window, scale);
				Assert.Equal(new Size(200, 100), window.ClientSize);
				Assert.Equal(new Rect(10, 20, 50, 30), Dpi.Area(box, window));
				Raster frame = Dpi.Capture(window, "harness-box");
				Assert.Equal(Scales.Device(200, scale), frame.Width);
				Assert.Equal(Scales.Device(100, scale), frame.Height);
				// the box covers 50 x 30 logical units at the scale, from (10, 20)
				RectInt found = frame.Bounds(c => c.Red > 200 && c.Blue < 50).Value;
				Assert.Equal(new RectInt(Scales.Device(10, scale), Scales.Device(20, scale), Scales.Device(60, scale), Scales.Device(50, scale)), found);
				window.Close();
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task Changing_the_scale_of_a_shown_window_keeps_its_logical_layout()
		{
			await Ui.Run(() =>
			{
				var box = new Border { Background = Red, Width = 40, Height = 40 };
				var window = new Window { Width = 160, Height = 120, Content = new Border { Background = Blue, Child = box } };
				window.Show();
				Dpi.Settle(window);
				Rect before = Dpi.Area(box, window);
				Raster one = Dpi.Capture(window, "harness-move-1");
				Assert.Equal(160, one.Width);
				Dpi.Apply(window, 2.0);
				Assert.Equal(before, Dpi.Area(box, window));
				Raster two = Dpi.Capture(window, "harness-move-2");
				Assert.Equal(320, two.Width);
				Dpi.Apply(window, 1.0);
				Assert.Equal(before, Dpi.Area(box, window));
				Assert.Equal(160, Dpi.Capture(window, "harness-move-3").Width);
				window.Close();
				return Task.CompletedTask;
			});
		}

		[Fact]
		public void Downscaling_by_a_whole_factor_averages_the_blocks_and_compare_sees_a_change()
		{
			using (SKImage image = Pictures.Lines(64, 40))
			{
				Raster small = Raster.From(image);
				using (var big = new SKBitmap(new SKImageInfo(128, 80, SKColorType.Rgba8888, SKAlphaType.Opaque)))
				using (var canvas = new SKCanvas(big))
				{
					// every pixel becomes a 2 x 2 block: the exact enlargement
					canvas.DrawImage(image, new SKRect(0, 0, 128, 80), new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None));
					using (SKImage enlarged = SKImage.FromBitmap(big))
					{
						Raster large = Raster.From(enlarged);
						Difference d = large.Downscale(64, 40).Compare(small, 0);
						Assert.Equal(0, d.Max);
					}
				}
				Raster other = Raster.From(Pictures.Lines(64, 40)).Crop(new RectInt(0, 0, 64, 40));
				Assert.Equal(0, other.Compare(small, 0).Max);
				Difference different = small.Compare(small.Downscale(32, 20).Downscale(64, 40), 24);
				Assert.True(different.Max > 24, "a blurred copy must differ: " + different);
			}
		}

		[Theory]
		[InlineData(1.0, 100.0, 100)]
		[InlineData(1.5, 100.0, 150)]
		[InlineData(2.0, 33.3, 67)]
		[InlineData(3.0, 10.5, 32)]
		[InlineData(1.5, 0.5, 1)]
		public void Device_rounds_like_the_painter(double scale, double logical, int expected)
		{
			Assert.Equal(expected, Scales.Device(logical, scale));
		}
	}
}
