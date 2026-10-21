using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using ComicRack.Mac.Controls;
using ComicRack.Mac.Themes;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;
using static ComicRack.Mac.Tests.Theme.ChromeTestSupport;

namespace ComicRack.Mac.Tests.HiDpi
{
	/// <summary>P5.5: the rating editor at 1x and 2x: the same layout in units, a one unit frame of whole pixels, the same picture reduced to 1x.</summary>
	[Trait("Category", "HiDpi")]
	public class RatingStarsHiDpiTests
	{
		private readonly ITestOutputHelper output;

		public RatingStarsHiDpiTests(ITestOutputHelper output)
		{
			this.output = output;
		}

		private static CrRatingStars Make() => new CrRatingStars { Width = 170, Height = 21, DrawText = true, DrawBorder = true, CenterRating = true, Rating = 3.5 };

		[Theory]
		[MemberData(nameof(Scales.MainData), MemberType = typeof(Scales))]
		public async Task The_layout_is_the_same_in_units_and_the_frame_is_whole_pixels_of_the_token_colour(double scale)
		{
			await InAppearance("Light", () =>
			{
				CrRatingStars stars = Make();
				Window window = ShowIn(stars, 200, 40, scale);
				Raster frame = Dpi.Capture(window, "stars-hidpi@" + Scales.Tag(scale));
				Assert.Equal(new Size(170, 21), stars.Bounds.Size);
				Rect strip = stars.Strip;
				Assert.Equal(21, strip.Y * 2 + strip.Height, 2); // centred
				SKColor border = Token(ThemeTokens.Names.ControlDark, "Light");
				int w = Scales.Device(1, scale);
				// the top frame is `w` rows of the pure colour across the whole width, and the row below is not the frame
				for (int row = 0; row < w; row++)
				{
					Assert.Equal(Scales.Device(170, scale), frame.CountInRow(row, 0, Scales.Device(170, scale), c => Raster.Near(c, border, 1)));
				}
				Assert.False(Raster.Near(frame[Scales.Device(85, scale), w], border, 1));
				window.Close();
			});
		}

		[Fact]
		public async Task The_picture_at_2x_reduced_to_1x_is_the_picture_at_1x()
		{
			await InAppearance("Light", () =>
			{
				Raster one, two;
				CrRatingStars a = Make();
				Window w1 = ShowIn(a, 200, 40, 1.0);
				one = Dpi.Capture(w1, "stars-compare@1x");
				Rect stripOne = a.Strip;
				w1.Close();
				CrRatingStars b = Make();
				Window w2 = ShowIn(b, 200, 40, 2.0);
				two = Dpi.Capture(w2, "stars-compare@2x");
				Assert.Equal(stripOne, b.Strip);
				w2.Close();
				Difference d = two.Downscale(one.Width, one.Height).Compare(one, 64);
				output.WriteLine("2x reduced to 1x against 1x: " + d);
				Assert.True(d.Mean < 1.5, d.ToString()); // the number is drawn with other pixels at 2x
				Assert.True(d.Bad < 0.03, d.ToString());
			});
		}
	}
}
