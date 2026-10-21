using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using ComicRack.Mac.Controls;
using ComicRack.Mac.Tests.HiDpi;
using ComicRack.Mac.Themes;
using SkiaSharp;
using Xunit;
using static ComicRack.Mac.Tests.Theme.ChromeTestSupport;

namespace ComicRack.Mac.Tests.Theme
{
	/// <summary>P5.5: the rating editor (<c>RatingControl</c>), used in the rating menu and in the Quick Rating dialog.</summary>
	public class RatingStarsTests
	{
		private static CrRatingStars Stars(bool text = true, bool border = false, bool center = false, double width = 200, double height = 18)
		{
			return new CrRatingStars { Width = width, Height = height, DrawText = text, DrawBorder = border, CenterRating = center };
		}

		[Fact]
		public void The_strip_is_the_largest_row_of_five_that_fits_and_a_point_on_it_means_a_share_of_the_maximum()
		{
			// five 32 x 32 pictures in 100 x 20: scaled to 20 high, 100 wide, left top or centred
			Rect none = RatingStrip.Compute(new Rect(10, 5, 100, 20), new Size(32, 32), 5, center: false);
			Assert.Equal(new Rect(10, 5, 100, 20), none);
			Rect wide = RatingStrip.Compute(new Rect(0, 0, 300, 20), new Size(32, 32), 5, center: false);
			Assert.Equal(new Rect(0, 0, 100, 20), wide);
			Assert.Equal(new Rect(100, 0, 100, 20), RatingStrip.Compute(new Rect(0, 0, 300, 20), new Size(32, 32), 5, center: true));
			Assert.Equal(new Rect(0, 5, 50, 10), RatingStrip.Compute(new Rect(0, 0, 50, 20), new Size(32, 32), 5, center: true));
			Assert.Equal(default, RatingStrip.Compute(new Rect(0, 0, 0, 20), new Size(32, 32), 5, center: true));
			Assert.Equal(default, RatingStrip.Compute(new Rect(0, 0, 100, 20), new Size(32, 32), 0, center: true));

			Assert.Equal(0, RatingStrip.RatingAt(wide, -3, 5));
			Assert.Equal(0, RatingStrip.RatingAt(wide, 0, 5));
			Assert.Equal(2.5, RatingStrip.RatingAt(wide, 50, 5));
			Assert.Equal(5, RatingStrip.RatingAt(wide, 100, 5));
			Assert.Equal(5, RatingStrip.RatingAt(wide, 140, 5));
		}

		[Fact]
		public async Task A_press_and_a_drag_set_the_rating_rounded_to_a_tenth_and_say_that_the_user_did_it()
		{
			await Ui.Run(() =>
			{
				CrRatingStars stars = Stars();
				Window window = ShowIn(stars, 220, 40);
				Rect strip = stars.Strip;
				Assert.Equal(new Rect(2, 2, 70, 14), strip); // 32 pixel stars in the 14 units of height that the frame of 2 leaves; the number takes the rest
				int changes = 0;
				stars.RatingChanged += (s, e) => changes++;
				Assert.False(stars.UserChanged);
				stars.Rating = 1; // not the user
				Assert.False(stars.UserChanged);
				Assert.Equal(0, changes);

				window.MouseDown(new Point(strip.X + 35, 9), MouseButton.Left);
				Assert.Equal(2.5, stars.Rating);
				Assert.True(stars.UserChanged);
				window.MouseMove(new Point(strip.X + 36, 9));
				Assert.Equal(2.6, stars.Rating, 6);
				window.MouseMove(new Point(strip.Right + 40, 9));
				Assert.Equal(5, stars.Rating);
				window.MouseMove(new Point(strip.X - 1, 9));
				Assert.Equal(0, stars.Rating);
				window.MouseUp(new Point(strip.X - 1, 9), MouseButton.Left);
				window.MouseMove(new Point(strip.X + 20, 9));
				Assert.Equal(0, stars.Rating); // the button is up
				Assert.True(changes >= 4);
				window.Close();
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task Keys_and_the_wheel_change_the_rating_as_the_original_does()
		{
			await Ui.Run(() =>
			{
				CrRatingStars stars = Stars();
				Window window = ShowIn(stars, 220, 40);
				stars.Focus();
				void Press(Key key) => window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
				Press(Key.Right);
				Assert.Equal(0.1, stars.Rating, 6);
				Press(Key.Up);
				Assert.Equal(0.2, stars.Rating, 6);
				Press(Key.Left);
				Assert.Equal(0.1, stars.Rating, 6);
				Press(Key.PageUp);
				Assert.Equal(1.1, stars.Rating, 6);
				Press(Key.PageDown);
				Press(Key.PageDown);
				Assert.Equal(0, stars.Rating); // never below 0
				Press(Key.End);
				Assert.Equal(5, stars.Rating);
				Press(Key.Right);
				Assert.Equal(5, stars.Rating); // nor above the maximum
				Press(Key.D3);
				Assert.Equal(3, stars.Rating);
				Press(Key.Home);
				Assert.Equal(0, stars.Rating);
				Assert.True(stars.UserChanged);

				stars.Rating = 2;
				window.MouseMove(new Point(30, 9));
				window.MouseWheel(new Point(30, 9), new Vector(0, 1));
				Assert.Equal(2.1, stars.Rating, 6);
				window.MouseWheel(new Point(30, 9), new Vector(0, -1));
				window.MouseWheel(new Point(30, 9), new Vector(0, -1));
				Assert.Equal(1.9, stars.Rating, 6);
				window.Close();
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task The_rating_is_clamped_to_the_stars_and_rounded()
		{
			await Ui.Run(() =>
			{
				CrRatingStars stars = Stars();
				stars.Rating = 7;
				Assert.Equal(5, stars.Rating);
				stars.Rating = -2;
				Assert.Equal(0, stars.Rating);
				stars.Rating = 3.14159;
				Assert.Equal(3.1, stars.Rating, 6);
				stars.Rating = double.NaN;
				Assert.Equal(0, stars.Rating);
				stars.MaximumRating = 3;
				stars.Rating = 4;
				Assert.Equal(3, stars.Rating);
				return Task.CompletedTask;
			});
		}

		private static long Ink(Raster frame, Rect area, SKColor background)
		{
			long sum = 0;
			for (int y = (int)area.Y; y < (int)area.Bottom; y++)
			{
				for (int x = (int)area.X; x < (int)area.Right; x++)
				{
					SKColor c = frame[x, y];
					sum += Math.Abs(c.Red - background.Red) + Math.Abs(c.Green - background.Green) + Math.Abs(c.Blue - background.Blue);
				}
			}
			return sum;
		}

		[Fact]
		public async Task The_rated_part_is_drawn_opaque_and_the_rest_at_a_quarter()
		{
			await InAppearance("Light", () =>
			{
				CrRatingStars stars = Stars(text: false, border: false, center: false, width: 100, height: 20);
				Window window = ShowIn(stars, 120, 40);
				SKColor background = Token(ThemeTokens.Names.Control, "Light");
				Rect strip = stars.Strip;
				Assert.Equal(new Rect(2, 2, 80, 16), strip);
				var left = new Rect(strip.X, strip.Y, strip.Width / 2, strip.Height);
				var right = new Rect(strip.X + strip.Width / 2, strip.Y, strip.Width / 2, strip.Height);

				stars.Rating = 0;
				Raster none = Dpi.Capture(window, "stars-0");
				stars.Rating = 5;
				Raster all = Dpi.Capture(window, "stars-5");
				stars.Rating = 2.5;
				Raster half = Dpi.Capture(window, "stars-2.5");
				long unrated = Ink(none, strip, background), rated = Ink(all, strip, background);
				Assert.True(rated > unrated * 2.5, $"five stars {rated} against none {unrated}");
				// half: the left side is like five stars, the right side like none
				Assert.InRange(Ink(half, left, background), Ink(all, left, background) * 0.9, Ink(all, left, background) * 1.1);
				Assert.InRange(Ink(half, right, background), Ink(none, right, background) * 0.9, Ink(none, right, background) * 1.1);
				window.Close();
			});
		}

		[Fact]
		public async Task The_number_and_the_frame_follow_the_options()
		{
			await InAppearance("Light", () =>
			{
				CrRatingStars stars = Stars(text: true, border: true, center: true, width: 170, height: 21);
				Window window = ShowIn(stars, 200, 40);
				SKColor background = Token(ThemeTokens.Names.Window, "Light");
				SKColor frameColor = Token(ThemeTokens.Names.ControlDark, "Light");
				Raster frame = Dpi.Capture(window, "stars-border");
				Assert.Equal(170, frame.CountInRow(0, 0, 170, c => Raster.Near(c, frameColor, 1))); // the top line is the frame, in the token colour
				Assert.True(Raster.Near(frame[85, 10], background, 1) || Raster.Near(frame[170 - 4, 4], background, 1));
				Rect strip = stars.Strip;
				Assert.True(strip.Width > 0 && strip.Right < 170 - 20, "the number takes room at the right: " + strip);
				Assert.True(strip.X > 2 && strip.Height == 17, "centred in the room, 17 units high: " + strip);
				stars.DrawText = false;
				Assert.True(stars.Strip.X > strip.X, "without the number the stars are centred in the whole width");
				window.Close();
			});
		}
	}
}
