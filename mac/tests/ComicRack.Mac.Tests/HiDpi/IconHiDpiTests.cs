using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ComicRack.Mac.Themes;
using SkiaSharp;
using Xunit;

namespace ComicRack.Mac.Tests.HiDpi
{
	/// <summary>
	/// P5.T2: an original image shown through <see cref="Icons.CreateImage"/>. There are no high-DPI variants, so a 16 pixel image is 16
	/// device independent units at every scale and covers 16 x scale device pixels (Retina scales it by two with high-quality
	/// filtering, as the original does at 200 %). Constant in logical units: size and position. Scales with the display: the frame.
	/// </summary>
	[Trait("Category", "HiDpi")]
	public class IconHiDpiTests
	{
		private static readonly IBrush White = new SolidColorBrush(Color.FromRgb(255, 255, 255));

		private static bool Ink(SKColor c) => c.Red < 235 || c.Green < 235 || c.Blue < 235;

		[Theory]
		[MemberData(nameof(Scales.AllData), MemberType = typeof(Scales))]
		public async Task A_16_pixel_original_image_keeps_16_units_and_covers_16_times_the_scale_in_device_pixels(double scale)
		{
			await Ui.Run(() =>
			{
				Image icon = Icons.CreateImage("FitWidth", Avalonia.Styling.ThemeVariant.Light);
				icon.HorizontalAlignment = HorizontalAlignment.Left;
				icon.VerticalAlignment = VerticalAlignment.Top;
				icon.Margin = new Thickness(12, 8, 0, 0);
				var window = new Window { Width = 64, Height = 48, Content = new Border { Background = White, Child = icon } };
				window.Show();
				Dpi.Apply(window, scale);
				Assert.Equal(new Rect(12, 8, 16, 16), Dpi.Area(icon, window));
				Raster frame = Dpi.Capture(window, "icon-fitwidth");
				Assert.Equal(Scales.Device(64, scale), frame.Width);
				RectInt ink = frame.Bounds(Ink).Value;
				RectInt box = RectInt.Snapped(new Rect(12, 8, 16, 16), scale);
				// the picture has transparent margins, so its ink lies inside the box and never outside it
				Assert.True(ink.Left >= box.Left && ink.Top >= box.Top && ink.Right <= box.Right && ink.Bottom <= box.Bottom, "ink " + ink + " is not inside " + box);
				Assert.Equal(Scales.Device(16, scale), box.Width);
				// nothing is drawn around the image
				Assert.Null(frame.Bounds(Ink, new RectInt(0, 0, frame.Width, box.Top)));
				Assert.Null(frame.Bounds(Ink, new RectInt(0, 0, box.Left, frame.Height)));
				Assert.Null(frame.Bounds(Ink, new RectInt(box.Right, 0, frame.Width, frame.Height)));
				Assert.Null(frame.Bounds(Ink, new RectInt(0, box.Bottom, frame.Width, frame.Height)));
				window.Close();
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task The_2x_frame_is_the_1x_frame_enlarged_with_filtering_not_another_picture()
		{
			await Ui.Run(() =>
			{
				Raster one = Shown(1.0);
				Raster two = Shown(2.0);
				Assert.Equal(one.Width * 2, two.Width);
				Difference d = two.Downscale(one.Width, one.Height).Compare(one, 48);
				Assert.True(d.Mean < 6 && d.Bad < 0.02, "the 2x frame differs from the 1x frame: " + d);
				return Task.CompletedTask;
			});
		}

		private static Raster Shown(double scale)
		{
			Image icon = Icons.CreateImage("Browser", Avalonia.Styling.ThemeVariant.Light);
			icon.HorizontalAlignment = HorizontalAlignment.Left;
			icon.VerticalAlignment = VerticalAlignment.Top;
			icon.Margin = new Thickness(8, 8, 0, 0);
			var window = new Window { Width = 48, Height = 40, Content = new Border { Background = White, Child = icon } };
			window.Show();
			Dpi.Apply(window, scale);
			Raster frame = Dpi.Capture(window, "icon-browser");
			window.Close();
			return frame;
		}
	}
}
