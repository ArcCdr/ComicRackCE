using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using ComicRack.Mac.Browser;
using ComicRack.Mac.Controls;
using ComicRack.Mac.Editor;
using ComicRack.Mac.Tests.Editor;
using ComicRack.Mac.Themes;
using SkiaSharp;
using Xunit;
using static ComicRack.Mac.Tests.Theme.ChromeTestSupport;

namespace ComicRack.Mac.Tests.HiDpi
{
	/// <summary>
	/// P5.7c: the page list and the page viewer at 1x and 2x. Constant in logical units: the header (20), the rows, the column widths, the viewer's picture rectangle. What scales: the one unit
	/// selection frame and picture frame, which are <c>scale</c> device pixels wide and of the pure colour (the controls draw lines as filled rectangles), the paper and the picture.
	/// </summary>
	[Trait("Category", "HiDpi")]
	public class PageListHiDpiTests
	{
		private static bool Is(SKColor actual, SKColor expected, int tolerance = 2) => Raster.Near(actual, expected, tolerance);

		private static Window Show(CrPageList list, double width, double height, double scale)
		{
			var window = new Window { Width = width, Height = height, Content = list };
			window.Show();
			if (scale != 1.0)
			{
				Dpi.Apply(window, scale);
			}
			Dpi.Settle(window);
			return window;
		}

		[Theory]
		[MemberData(nameof(Scales.MainData), MemberType = typeof(Scales))]
		public async Task The_rows_and_the_header_have_the_same_logical_size_and_the_paper_is_the_item_view_colour(double scale)
		{
			await InAppearance("Light", () =>
			{
				var source = new FakePageSource(3);
				var list = new CrPageList { Source = source };
				Window window = Show(list, 400, 240, scale);
				Assert.Equal(new Rect(0, 0, 40, 20), list.HeaderRect(0));
				Assert.Equal(20, list.ViewRect.Y);
				double row = list.RowHeight;
				Assert.Equal(new Rect(0, 20 + row, 820, row), list.RowRect(1));
				Raster frame = Dpi.Capture(window, "pagelist-paper@" + Scales.Tag(scale));
				Assert.Equal(Scales.Device(400, scale), frame.Width);
				// below the three rows: the paper of the item view
				SKColor paper = Token(ThemeTokens.Names.ItemViewMainBack, "Light");
				Assert.True(Is(At(frame, 100, 20 + row * 3 + 10, scale), paper, 1), At(frame, 100, 20 + row * 3 + 10, scale).ToString());
				// row 0 is a striped row, row 1 is not
				SKColor stripe = Token(ThemeTokens.Names.DetailRowHighlight, "Light");
				Assert.True(Is(At(frame, 300, 20 + row * 0.5, scale), stripe, 1));
				Assert.True(Is(At(frame, 300, 20 + row * 1.5, scale), paper, 1));
				window.Close();
			});
		}

		[Theory]
		[MemberData(nameof(Scales.MainData), MemberType = typeof(Scales))]
		public async Task A_selected_row_has_a_fill_and_a_one_unit_frame_in_the_selection_colour_that_does_not_blend(double scale)
		{
			await InAppearance("Light", () =>
			{
				var source = new FakePageSource(3);
				var list = new CrPageList { Source = source };
				Window window = Show(list, 400, 240, scale);
				source.SetSelection(new[] { 1 });
				Dpi.Settle(window);
				Raster frame = Dpi.Capture(window, "pagelist-selected@" + Scales.Tag(scale));
				SKColor paper = Token(ThemeTokens.Names.ItemViewMainBack, "Light");
				// the view has no keyboard focus in the test: the unfocused selection colour
				SKColor baseColor = Token(ThemeTokens.Names.SelectionUnfocused, "Light");
				SKColor fill = Over(baseColor, (int)(StyledSelection.AlphaSelected * StyledSelection.BackAlpha), paper);
				SKColor line = Over(baseColor, StyledSelection.AlphaSelected, fill);
				Rect r = list.RowRect(1);
				int w = Scales.Device(1, scale);
				int y = Scales.Device(r.Y + r.Height / 2, scale);
				// the left frame is `scale` device pixels of the frame colour, and the fill starts right behind it
				Assert.Equal(w, frame.CountInRow(y, 0, w, c => Is(c, line)));
				Assert.True(Is(frame[w, y], fill), frame[w, y] + " expected " + fill);
				// the top frame: a full row of the frame colour along the row, and the paper above it
				int x = Scales.Device(300, scale);
				Assert.True(Is(frame[x, Scales.Device(r.Y, scale)], line));
				Assert.True(Is(frame[x, Scales.Device(r.Y, scale) - 1], paper, 1) || Is(frame[x, Scales.Device(r.Y, scale) - 1], Token(ThemeTokens.Names.DetailRowHighlight, "Light"), 1));
				window.Close();
			});
		}

		[Theory]
		[MemberData(nameof(Scales.MainData), MemberType = typeof(Scales))]
		public async Task The_thumbnail_of_a_row_has_a_black_one_unit_frame_and_its_picture_starts_inside_it(double scale)
		{
			await InAppearance("Light", () =>
			{
				var source = new FakePageSource(3);
				var list = new CrPageList { Source = source, RowHeight = 48 };
				Window window = Show(list, 400, 240, scale);
				Raster frame = Dpi.Capture(window, "pagelist-thumbnail@" + Scales.Tag(scale));
				Rect cell = list.CellRect(1, PageColumns.Thumbnail);
				Assert.Equal(new Rect(40, 20 + 48, 80, 48), cell);
				int w = Scales.Device(1, scale);
				int y = Scales.Device(cell.Y + 20, scale);
				int x = Scales.Device(cell.X + 2, scale); // the cell less two units
				Assert.Equal(w, frame.CountInRow(y, x, x + w, c => Is(c, new SKColor(0, 0, 0), 1)));
				// the picture of row 1 follows: a solid colour, not the black of the frame
				SKColor inside = frame[x + w + 2, y];
				Assert.False(Is(inside, new SKColor(0, 0, 0), 4));
				window.Close();
			});
		}

		[Theory]
		[MemberData(nameof(Scales.MainData), MemberType = typeof(Scales))]
		public async Task The_thumbnail_view_draws_the_picture_with_a_frame_the_badge_and_the_flags_in_whole_pixels(double scale)
		{
			await InAppearance("Light", () =>
			{
				var source = new FakePageSource(6);
				source.Bookmarked.Add(0);
				source.Deleted.Add(1);
				var list = new CrPageList { Source = source, Mode = PageListMode.Thumbnail };
				Window window = Show(list, 600, 300, scale);
				Raster frame = Dpi.Capture(window, "pagelist-cells@" + Scales.Tag(scale));
				Size cell = list.CellSize;
				Assert.Equal(new Size(140, 140), cell);
				Rect first = list.RowRect(0);
				Assert.Equal(new Rect(0, 0, 140, 140), first);
				// the picture: 60 x 90 fitted into the square of 128 less the shadow, centred; its frame is black, one unit wide
				int w = Scales.Device(1, scale);
				int y = Scales.Device(first.Y + 4 + 60, scale);
				int blackInRow = frame.CountInRow(y, 0, Scales.Device(140, scale), c => Is(c, new SKColor(0, 0, 0), 1));
				Assert.True(blackInRow >= 2 * w, "the two vertical sides of the frame: " + blackInRow);
				// the page number badge is the dark box with alpha 192 near the top right corner of the picture
				Assert.NotNull(frame.Bounds(c => c.Red < 60 && c.Green < 60 && c.Blue < 60 && c.Alpha == 255, new RectInt(0, 0, Scales.Device(140, scale), Scales.Device(40, scale))));
				window.Close();
			});
		}

		[Theory]
		[MemberData(nameof(Scales.MainData), MemberType = typeof(Scales))]
		public async Task The_page_viewer_fits_the_width_and_its_picture_edges_fall_on_device_pixels(double scale)
		{
			await InAppearance("Light", () =>
			{
				var viewer = new CrPageViewer { Width = 240, Height = 200, Text = "Double Click on Color to set White Point" };
				using (var bitmap = new SKBitmap(10, 15))
				{
					bitmap.Erase(new SKColor(200, 30, 30));
					var picture = new CoverImage(SKImage.FromBitmap(bitmap));
					viewer.SetPicture(picture);
					picture.Release();
				}
				Window window = ShowIn(viewer, 260, 220, scale);
				Assert.True(viewer.VerticalBar.IsVisible); // the picture is taller than the view
				Assert.Equal(240 - viewer.VerticalBar.DesiredSize.Width, viewer.PictureSize.Width); // fit to the width the scroll bar leaves
				Assert.Equal(Math.Floor(viewer.PictureSize.Width * 1.5), viewer.PictureSize.Height);
				Raster frame = Dpi.Capture(window, "pageviewer@" + Scales.Tag(scale));
				// the red picture starts at the left edge of the viewer and covers the view up to the scroll bar
				Point origin = Dpi.Origin(viewer, window);
				SKColor red = new SKColor(200, 30, 30);
				int y = Scales.Device(origin.Y + 20, scale);
				int x0 = Scales.Device(origin.X, scale);
				Assert.True(Is(frame[x0, y], red, 2), frame[x0, y].ToString());
				Assert.True(Is(frame[x0 + Scales.Device(100, scale), y], red, 2));
				// the right edge of the picture is where the scroll bar starts, on a whole device pixel
				int edge = Scales.Device(origin.X + viewer.PictureSize.Width, scale);
				Assert.True(Is(frame[edge - 1, y], red, 2), frame[edge - 1, y].ToString());
				Assert.False(Is(frame[edge, y], red, 8));
				// scrolling to the end shows the bottom of the picture and then the paper below it never appears
				viewer.VerticalBar.Value = viewer.VerticalBar.Maximum;
				Dpi.Settle(window);
				Assert.Equal(viewer.VerticalBar.Maximum, viewer.Offset);
				window.Close();
				viewer.Clear();
			});
		}

		[Theory]
		[MemberData(nameof(Scales.MainData), MemberType = typeof(Scales))]
		public async Task A_double_click_position_is_a_fraction_of_the_picture_whatever_the_scale(double scale)
		{
			await InAppearance("Light", () =>
			{
				var viewer = new CrPageViewer { Width = 200, Height = 400 };
				using (var bitmap = new SKBitmap(100, 100))
				{
					bitmap.Erase(new SKColor(10, 120, 10));
					var picture = new CoverImage(SKImage.FromBitmap(bitmap));
					viewer.SetPicture(picture);
					picture.Release();
				}
				Window window = ShowIn(viewer, 220, 420, scale);
				Point? at = viewer.PictureFractionAt(new Point(50, 100));
				Assert.NotNull(at);
				Assert.Equal(0.25, at.Value.X, 3);
				Assert.Equal(0.5, at.Value.Y, 3);
				Assert.Null(viewer.PictureFractionAt(new Point(50, 250))); // under the picture (200 high)
				window.Close();
				viewer.Clear();
			});
		}
	}
}
