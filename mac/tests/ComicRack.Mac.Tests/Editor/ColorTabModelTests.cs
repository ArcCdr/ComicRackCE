#pragma warning disable xUnit1031 // the tests wait for tasks they started with a synchronous stand-in for the thread pool
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using ComicRack.Mac.Browser;
using ComicRack.Mac.Editor;
using cYo.Common.Drawing;
using cYo.Compat.Drawing;
using cYo.Projects.ComicRack.Engine;
using SkiaSharp;
using Xunit;

namespace ComicRack.Mac.Tests.Editor
{
	/// <summary>P5.7c: the Colors tab without a window (IN.07): the sliders as percent, what is written to the book, the page buttons, and the preview that applies the Core's colour code.</summary>
	public class ColorTabModelTests
	{
		private static ComicBook Book(int pages = 10, int current = 0) => new ComicBook { FilePath = "/nowhere/a.cbz", PageCount = pages, CurrentPage = current };

		[Fact]
		public void The_sliders_are_the_books_adjustment_in_percent_and_Apply_writes_them_back()
		{
			ComicBook book = Book();
			book.ColorAdjustment = new BitmapAdjustment(0.25f, -0.5f, 0.75f, 0.1f, Color.White, BitmapAdjustmentOptions.None, 2);
			var colors = new ColorTabModel();
			colors.Load(book);
			Assert.Equal(25, colors.Saturation);
			Assert.Equal(-50, colors.Brightness);
			Assert.Equal(75, colors.Contrast);
			Assert.Equal(10, colors.Gamma);
			Assert.Equal(2, colors.Sharpening);
			Assert.Equal(book.ColorAdjustment, colors.Adjustment);

			colors.Saturation = -100;
			colors.Sharpening = 3;
			var other = Book();
			colors.ApplyTo(other);
			Assert.Equal(-1f, other.ColorAdjustment.Saturation);
			Assert.Equal(3, other.ColorAdjustment.Sharpen);
			Assert.Equal(-0.5f, other.ColorAdjustment.Brightness);
		}

		[Fact]
		public void The_adjustment_is_truncated_to_whole_percent_as_the_trackbars_do()
		{
			ComicBook book = Book();
			book.ColorAdjustment = new BitmapAdjustment(0.337f, 0f, 0f, 0f);
			var colors = new ColorTabModel();
			colors.Load(book);
			Assert.Equal(33, colors.Saturation); // (int)(0.337 * 100)
		}

		[Fact]
		public void Reset_puts_every_slider_at_zero_and_the_white_point_at_white()
		{
			var colors = new ColorTabModel();
			colors.Load(Book());
			colors.Saturation = 40;
			colors.Brightness = -20;
			colors.Contrast = 10;
			colors.Gamma = 5;
			colors.Sharpening = 1;
			colors.SetWhitePoint(Color.FromArgb(255, 200, 190, 150));
			Assert.True(colors.HasAdjustment);
			var changed = new List<string>();
			colors.PropertyChanged += (s, e) => changed.Add(e.PropertyName);
			colors.Reset();
			Assert.Equal(0, colors.Saturation + colors.Brightness + colors.Contrast + colors.Gamma + colors.Sharpening);
			Assert.Equal(Color.White, colors.WhitePoint);
			Assert.True(colors.Adjustment.IsEmpty);
			Assert.False(colors.HasAdjustment);
			Assert.Contains(nameof(ColorTabModel.Adjustment), changed);
		}

		[Fact]
		public void The_slider_tool_tip_shows_the_percent_with_a_plus_sign_above_zero()
		{
			Assert.Equal("+40%", ColorTabModel.SliderToolTip(40));
			Assert.Equal("0%", ColorTabModel.SliderToolTip(0));
			Assert.Equal("-7%", ColorTabModel.SliderToolTip(-7));
		}

		[Fact]
		public void The_page_buttons_move_the_viewer_inside_the_book_and_the_label_names_the_page()
		{
			var colors = new ColorTabModel();
			colors.Load(Book(pages: 4, current: 0));
			Assert.Equal(0, colors.ViewerPage);
			Assert.Equal("Page 1", colors.PageLabel);
			Assert.False(colors.CanGoFirst);
			Assert.False(colors.CanGoPrevious);
			Assert.True(colors.CanGoNext);
			colors.GoNext();
			colors.GoNext();
			Assert.Equal("Page 3", colors.PageLabel);
			Assert.True(colors.CanGoFirst);
			colors.GoLast();
			Assert.Equal(3, colors.ViewerPage);
			Assert.False(colors.CanGoNext);
			colors.GoNext(); // past the end: nothing
			Assert.Equal(3, colors.ViewerPage);
			colors.GoFirst();
			Assert.Equal(0, colors.ViewerPage);
			colors.GoPrevious();
			Assert.Equal(0, colors.ViewerPage);
			colors.Load(Book(pages: 4, current: 2)); // another book starts at its current page
			Assert.Equal(2, colors.ViewerPage);
		}

		// ---- the preview

		private sealed class FakeSource : IPageImageSource
		{
			public readonly List<(int Page, int Width)> Asked = new List<(int, int)>();
			public Func<int, int, Bitmap> Make = (page, width) =>
			{
				var bitmap = new Bitmap(width, width * 3 / 2);
				using (var g = Graphics.FromImage(bitmap))
				{
					g.Clear(Color.FromArgb(255, 100, 120, 140));
				}
				bitmap.SetPixel(0, 0, Color.FromArgb(255, 230, 220, 200));
				return bitmap;
			};

			public Bitmap Load(int page, int width)
			{
				Asked.Add((page, width));
				return Make(page, width);
			}
		}

		private static ColorPreview Preview(FakeSource source, ColorTabModel colors) => new ColorPreview(source, () => colors.Adjustment, action => action(), work => Task.FromResult(work()));

		private static SKColor Pixel(ColorPreview preview, int x, int y)
		{
			using (SKPixmap pixmap = preview.Picture.Image.PeekPixels())
			{
				return pixmap.GetPixelColor(x, y);
			}
		}

		[Fact]
		public async Task The_preview_reads_the_page_at_the_width_of_the_viewer_and_shows_it_unchanged_without_an_adjustment()
		{
			var colors = new ColorTabModel();
			colors.Load(Book());
			var source = new FakeSource();
			using (ColorPreview preview = Preview(source, colors))
			{
				int changes = 0;
				preview.Changed += (s, e) => changes++;
				await preview.ShowPageAsync(3, 90);
				Assert.Equal(new[] { (3, 90) }, source.Asked);
				Assert.Equal(1, changes);
				Assert.Equal(3, preview.Page);
				Assert.Equal(90, preview.Picture.Width);
				Assert.Equal(135, preview.Picture.Height);
				Assert.Equal(new SKColor(100, 120, 140), Pixel(preview, 40, 40));
				Assert.Equal(new Size(90, 135), preview.PageSize);
			}
		}

		[Fact]
		public async Task A_slider_makes_the_picture_again_from_the_page_it_has_with_the_adjustment_of_the_core()
		{
			var colors = new ColorTabModel();
			colors.Load(Book());
			var source = new FakeSource();
			using (ColorPreview preview = Preview(source, colors))
			{
				await preview.ShowPageAsync(0, 60);
				SKColor plain = Pixel(preview, 30, 30);
				colors.Brightness = 40;
				await preview.RefreshAsync();
				SKColor brighter = Pixel(preview, 30, 30);
				Assert.True(brighter.Red > plain.Red && brighter.Green > plain.Green && brighter.Blue > plain.Blue, plain + " -> " + brighter);
				colors.Saturation = -100;
				await preview.RefreshAsync();
				SKColor grey = Pixel(preview, 30, 30);
				Assert.InRange(Math.Abs(grey.Red - grey.Green), 0, 2); // no saturation: a grey
				Assert.InRange(Math.Abs(grey.Green - grey.Blue), 0, 2);
				Assert.Single(source.Asked); // the page is read once
				colors.Reset();
				await preview.RefreshAsync();
				Assert.Equal(plain, Pixel(preview, 30, 30));
			}
		}

		[Fact]
		public async Task Gamma_and_sharpening_go_through_the_cores_code_too()
		{
			var colors = new ColorTabModel();
			colors.Load(Book());
			using (ColorPreview preview = Preview(new FakeSource(), colors))
			{
				await preview.ShowPageAsync(0, 60);
				SKColor plain = Pixel(preview, 30, 30);
				colors.Gamma = 50;
				await preview.RefreshAsync();
				Assert.NotEqual(plain, Pixel(preview, 30, 30));
				colors.Gamma = 0;
				colors.Sharpening = 3;
				await preview.RefreshAsync();
				Assert.Equal(plain, Pixel(preview, 30, 30)); // a flat area does not change when it is sharpened
				Assert.Equal(60, preview.Picture.Width);
			}
		}

		[Fact]
		public async Task The_white_point_is_read_from_the_page_as_it_is_and_turns_that_colour_white()
		{
			var colors = new ColorTabModel();
			colors.Load(Book());
			using (ColorPreview preview = Preview(new FakeSource(), colors))
			{
				await preview.ShowPageAsync(0, 60);
				Color? picked = preview.PixelAt(0, 0); // the light corner
				Assert.Equal(Color.FromArgb(255, 230, 220, 200), picked);
				Assert.Null(preview.PixelAt(-1, 0));
				Assert.Null(preview.PixelAt(60, 0));
				colors.SetWhitePoint(picked.Value);
				await preview.RefreshAsync();
				SKColor corner = Pixel(preview, 0, 0);
				Assert.NotEqual(new SKColor(230, 220, 200), corner); // the light colour of the page is not what it was
				// ... and it is what the Core's own colour code makes of that pixel
				using (Bitmap expected = new FakeSource().Load(0, 60).CreateAdjustedBitmap(colors.Adjustment, System.Drawing.Imaging.PixelFormat.Format32bppArgb, alwaysClone: true))
				{
					Color c = expected.GetPixel(0, 0);
					Assert.Equal(new SKColor(c.R, c.G, c.B), corner);
				}
				// the pixel read still comes from the page as read, not from the picture shown
				Assert.Equal(picked, preview.PixelAt(0, 0));
			}
		}

		[Fact]
		public async Task A_request_that_a_newer_one_overtakes_shows_nothing_and_the_last_one_wins()
		{
			var colors = new ColorTabModel();
			colors.Load(Book());
			var gate = new TaskCompletionSource<bool>();
			var posted = new List<Action>();
			int runs = 0;
			var source = new FakeSource();
			using (var preview = new ColorPreview(source, () => colors.Adjustment, posted.Add, async work =>
			{
				runs++;
				if (runs == 1)
				{
					await gate.Task; // the first adjustment is slow
				}
				return work();
			}))
			{
				Task first = preview.ShowPageAsync(0, 60);
				colors.Brightness = 50;
				Task second = preview.RefreshAsync(); // overtakes: the gate lets one in at a time, so this waits for the first
				gate.SetResult(true);
				await Task.WhenAny(Task.WhenAll(first, second), Task.Delay(100));
				foreach (Action action in posted.ToArray())
				{
					action();
				}
				await Task.WhenAll(first, second);
				// the newest request is the one that shows, with the brighter adjustment
				SKColor shown = Pixel(preview, 30, 30);
				Assert.True(shown.Red > 100);
			}
		}

		[Fact]
		public async Task A_page_that_cannot_be_read_leaves_no_picture_and_closing_lets_go_of_everything()
		{
			var colors = new ColorTabModel();
			colors.Load(Book());
			var source = new FakeSource { Make = (page, width) => null };
			ColorPreview preview = Preview(source, colors);
			await preview.ShowPageAsync(0, 60);
			Assert.Null(preview.Picture);
			Assert.Equal(Size.Empty, preview.PageSize);
			source.Make = (page, width) => new Bitmap(width, width);
			await preview.ShowPageAsync(1, 40);
			CoverImage picture = preview.Picture;
			Assert.NotNull(picture);
			picture.Retain();
			preview.Dispose();
			Assert.Null(preview.Picture);
			Assert.Equal(1, picture.References); // only ours is left
			picture.Release();
			await preview.RefreshAsync(); // after Dispose: nothing happens
		}
	}
}
