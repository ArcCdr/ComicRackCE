using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using ComicRack.Mac.Browser;
using ComicRack.Mac.Controls;
using ComicRack.Mac.Tests.Browser;
using ComicRack.Mac.Themes;
using ComicRack.Mac.Views;
using SkiaSharp;
using Xunit;
using static ComicRack.Mac.Tests.Theme.ChromeTestSupport;

namespace ComicRack.Mac.Tests.HiDpi
{
	/// <summary>
	/// RV6: the rubber band at 1x and 2x. Constant in logical units: the band's rectangle, what it selects. What scales: the one unit frame is exactly
	/// <c>scale</c> device pixels wide and the pure Highlight colour (it is drawn as filled rectangles, so nothing blends), the fill is Highlight at alpha 128,
	/// two units inside the band, and nothing is drawn outside the frame.
	/// </summary>
	[Trait("Category", "HiDpi")]
	public class RubberBandHiDpiTests
	{
		private static bool Is(SKColor actual, SKColor expected, int tolerance = 1) => Raster.Near(actual, expected, tolerance);

		[Theory]
		[InlineData("Light", 1.0)]
		[InlineData("Light", 2.0)]
		[InlineData("Dark", 1.0)]
		[InlineData("Dark", 2.0)]
		public async Task The_band_is_a_translucent_fill_two_units_inside_and_a_crisp_one_unit_frame_one_unit_wider(string appearance, double scale)
		{
			await InAppearance(appearance, () =>
			{
				var band = new CrSelectionRectangle { Width = 200, Height = 150, Rectangle = new Rect(10, 12, 80, 50) };
				Window window = ShowIn(band, 220, 170, scale);
				Raster frame = Dpi.Capture(window, "rubberband-" + appearance + "@" + Scales.Tag(scale));
				SKColor highlight = Token(ThemeTokens.Names.Highlight, appearance);
				SKColor ground = Token(ThemeTokens.Names.Control, appearance);
				SKColor fill = Over(highlight, CrSelectionRectangle.FillAlpha, ground);
				int w = Scales.Device(1, scale);
				// logical geometry: fill (12, 14) 76 x 46; frame (12, 14) 77 x 47, i.e. the frame runs from 12 to 89 and from 14 to 61
				CrSelectionRectangle.Shapes(new Rect(10, 12, 80, 50), out Rect fillRect, out Rect frameRect);
				Assert.Equal(new Rect(12, 14, 76, 46), fillRect);
				Assert.Equal(new Rect(12, 14, 77, 47), frameRect);
				int midY = Scales.Device(40, scale), midX = Scales.Device(50, scale);
				int left = Scales.Device(12, scale), right = Scales.Device(89, scale) - w, top = Scales.Device(14, scale), bottom = Scales.Device(61, scale) - w;

				// the left and right frame lines: exactly `w` device pixels of the pure colour, with the ground outside and the fill inside
				Assert.Equal(w, frame.CountInRow(midY, left, left + w, c => Is(c, highlight)));
				Assert.True(Is(frame[left - 1, midY], ground), "outside the left line");
				Assert.True(Is(frame[left + w, midY], fill, 2), "inside the left line: " + frame[left + w, midY]);
				Assert.Equal(w, frame.CountInRow(midY, right, right + w, c => Is(c, highlight)));
				Assert.True(Is(frame[right + w, midY], ground), "outside the right line");
				Assert.True(Is(frame[right - 1, midY], fill, 2), "inside the right line");
				// the top and bottom lines
				Assert.Equal(w, frame.CountInColumn(midX, top, top + w, c => Is(c, highlight)));
				Assert.True(Is(frame[midX, top - 1], ground));
				Assert.True(Is(frame[midX, top + w], fill, 2));
				Assert.Equal(w, frame.CountInColumn(midX, bottom, bottom + w, c => Is(c, highlight)));
				Assert.True(Is(frame[midX, bottom + w], ground));
				// the band itself (10..90, 12..62) is not painted outside the frame: the two units between band and frame are the ground
				Assert.True(Is(frame[Scales.Device(11, scale), midY], ground));
				Assert.True(Is(frame[midX, Scales.Device(13, scale)], ground));
				// the fill is flat
				Assert.True(Is(frame[Scales.Device(30, scale), Scales.Device(30, scale)], frame[Scales.Device(70, scale), Scales.Device(50, scale)], 1));
				window.Close();
			});
		}

		[Theory]
		[MemberData(nameof(Scales.MainData), MemberType = typeof(Scales))]
		public async Task The_band_over_the_grid_has_the_same_logical_rectangle_and_selection_at_every_scale(double scale)
		{
			await Ui.Run(async () =>
			{
				using (var h = new GridHarness(900, 520))
				{
					await h.Show(new BookCatalog(RubberBandTests.Books(40)));
					Dpi.Apply(h.Window, scale);
					h.Layout();
					int columns = h.Grid.Columns;
					BookCell first = h.Grid.CellAt(0), last = h.Grid.CellAt(columns + 2);
					Point from = first.TranslatePoint(new Point(1, 1), h.Window).Value;
					Point to = last.TranslatePoint(new Point(last.Bounds.Width / 2, last.Bounds.Height / 2), h.Window).Value;
					h.Window.MouseMove(from);
					h.Window.MouseDown(from, MouseButton.Left);
					h.Window.MouseMove(to, RawInputModifiers.LeftMouseButton);
					h.Layout();
					Assert.Equal(new[] { 0, 1, 2, columns, columns + 1, columns + 2 }, h.Model.Selection.SelectedIndices().ToArray());
					Rect band = h.Grid.Band.Rectangle;
					Assert.Equal(Math.Floor(from.X), band.X, 1);
					Assert.Equal(Math.Floor(from.Y), band.Y, 1);
					Assert.Equal(Math.Floor(to.X), band.Right, 1);
					Assert.Equal(Math.Floor(to.Y), band.Bottom, 1);

					Raster frame = Dpi.Capture(h.Window, "rubberband-grid@" + Scales.Tag(scale));
					SKColor highlight = Token(ThemeTokens.Names.Highlight, "Light");
					int w = Scales.Device(1, scale);
					// the left line of the frame: the band's left edge plus two units, `w` device pixels of the pure Highlight, on a row below the first cover's frame
					Rect fill, line;
					CrSelectionRectangle.Shapes(band, out fill, out line);
					int x = Scales.Device(line.X, scale);
					int y = Scales.Device(line.Y + 20, scale);
					// find the pure Highlight run at that row: the selected covers also draw Highlight, but not at this x (between the cover and the band edge)
					Assert.Equal(w, frame.CountInRow(y, x, x + w, c => Is(c, highlight)));
					int xr = Scales.Device(line.Right, scale) - w;
					Assert.Equal(w, frame.CountInRow(y, xr, xr + w, c => Is(c, highlight)));
					h.Window.MouseUp(to, MouseButton.Left);
					h.Layout();
					Assert.True(h.Grid.Band.Rectangle.Width <= 0);
				}
			});
		}
	}
}
