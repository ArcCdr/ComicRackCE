using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using ComicRack.Mac.Controls;
using ComicRack.Mac.Reader;
using ComicRack.Mac.Tests.Reader;
using ComicRack.Mac.Themes;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;
using static ComicRack.Mac.Tests.Theme.ChromeTestSupport;

namespace ComicRack.Mac.Tests.HiDpi
{
	/// <summary>
	/// P5.32a: the main window with a book in a tab at 1x, 1.5x, 2x and 3x. Constant in logical units: the tab strip 31, the status bar 24, the docked browser 250 with its 6 unit
	/// grip, the tab geometry; what scales: the frames (one unit lines of whole device pixels) and the text of the status segments.
	/// </summary>
	[Trait("Category", "HiDpi")]
	public class MainWindowReaderHiDpiTests
	{
		private readonly ITestOutputHelper output;

		public MainWindowReaderHiDpiTests(ITestOutputHelper output)
		{
			this.output = output;
		}

		private static async Task<MainReaderHarness> Open(double scale, string book = "Book")
		{
			var h = new MainReaderHarness();
			await h.AddBook(book);
			Dpi.Apply(h.Window, scale);
			Dpi.Settle(h.Window);
			return h;
		}

		[Theory]
		[MemberData(nameof(Scales.MainData), MemberType = typeof(Scales))]
		public async Task The_tab_strip_the_status_bar_and_the_browser_dock_keep_their_logical_size_and_the_selected_tab_has_a_one_unit_frame(double scale)
		{
			await Ui.Run(async () =>
			{
				using (MainReaderHarness h = await Open(scale))
				{
					CrTabBar tabs = h.Window.Tabs;
					double unit = 1.0 / scale; // the layout rounds to whole device pixels
					Assert.InRange(tabs.Bounds.Height, 31 - 0.001, 31 + unit);
					Assert.InRange(h.Find<CrStatusBar>("StatusBar").Bounds.Height, 24 - 0.001, 24 + unit);
					CrSizableContainer dock = h.Find<CrSizableContainer>("BrowserDock");
					Assert.InRange(dock.Bounds.Height, 250 - 0.001, 250 + unit);
					Assert.Equal(new Rect(0, 0, dock.Bounds.Width, 6), dock.GripRectangle);
					Assert.Equal(8, tabs.Geometry[0].Bounds.X);
					Assert.Equal(100, tabs.Geometry[0].Bounds.Width); // a book tab is 100 wide at least
					Raster frame = Dpi.Capture(h.Window, "main-reader-hidpi@" + Scales.Tag(scale));
					Rect tab = tabs.Geometry[0].Bounds;
					Point o = Dpi.Origin(tabs, h.Window);
					SKColor border = Token(ThemeTokens.Names.TabPageBorder, "Light");
					int w = Scales.Device(1, scale);
					int x = Scales.Device(o.X + tab.X + 60, scale);
					int top = Scales.Device(o.Y + tab.Y, scale);
					Assert.Equal(w, frame.CountInColumn(x, top, top + w, c => Raster.Near(c, border, 0)));
					Assert.False(Raster.Near(frame[x, top + w], border, 0));
				}
			});
		}

		[Theory]
		[MemberData(nameof(Scales.AllData), MemberType = typeof(Scales))]
		public async Task The_book_segment_of_the_status_bar_has_the_same_logical_box_and_a_device_size_that_follows_the_scale(double scale)
		{
			await Ui.Run(async () =>
			{
				Rect logicalOne;
				int inkOne;
				using (MainReaderHarness one = await Open(1.0))
				{
					(logicalOne, inkOne) = MeasureLabel(one);
				}
				using (MainReaderHarness h = await Open(scale))
				{
					(Rect logical, int ink) = MeasureLabel(h);
					output.WriteLine($"scale {scale}: label {logical}, ink width {ink} device pixels (1x: {inkOne})");
					Assert.InRange(logical.Width, logicalOne.Width - 1, logicalOne.Width + 1);
					Assert.InRange(logical.Height, logicalOne.Height - 1, logicalOne.Height + 1);
					Assert.InRange((double)ink / inkOne, scale * 0.9, scale * 1.1);
				}
			});
		}

		private static (Rect box, int ink) MeasureLabel(MainReaderHarness h)
		{
			double scale = h.Window.RenderScaling;
			var label = h.Find<TextBlock>("BookSegment");
			Rect box = Dpi.Area(label, h.Window);
			Raster frame = Dpi.Capture(h.Window, "main-reader-label");
			RectInt region = RectInt.Snapped(box, scale);
			SKColor ground = frame[region.Left - 2, region.Top + 1];
			RectInt? ink = frame.Bounds(c => !Raster.Near(c, ground, 40), region);
			Assert.NotNull(ink);
			return (box, ink.Value.Width);
		}
	}
}
