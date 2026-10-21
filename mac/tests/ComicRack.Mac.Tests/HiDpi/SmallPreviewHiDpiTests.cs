using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Threading;
using ComicRack.Mac.Controls;
using ComicRack.Mac.Host;
using ComicRack.Mac.Reader;
using ComicRack.Mac.Tests.Reader;
using ComicRack.Mac.Tests.Thumbnails;
using ComicRack.Mac.Themes;
using ComicRack.Mac.Thumbnails;
using ComicRack.Mac.ViewModels;
using ComicRack.Mac.Views;
using cYo.Projects.ComicRack.Engine;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;
using static ComicRack.Mac.Tests.Theme.ChromeTestSupport;

namespace ComicRack.Mac.Tests.HiDpi
{
	/// <summary>
	/// P5.6a: the small preview at 1x and 2x (and the fractional scales of the cheap test). Constant in logical units: the pane (207 high, 6 unit grip, 2 unit border inset), the toolbar
	/// (25), the viewer; what scales: the one unit frame (whole device pixels, nothing blended), the message of the page viewer (its ink and its centre) and the page.
	/// </summary>
	[Trait("Category", "HiDpi")]
	public class SmallPreviewHiDpiTests : IDisposable
	{
		private readonly ITestOutputHelper output;
		private readonly long liveBefore;
		private readonly bool animation;

		public SmallPreviewHiDpiTests(ITestOutputHelper output)
		{
			this.output = output;
			liveBefore = Gc.SettleLive();
			Ui.BeginTest();
			animation = CrSizableContainer.EnableAnimation;
			CrSizableContainer.EnableAnimation = false;
		}

		public void Dispose()
		{
			CrSizableContainer.EnableAnimation = animation;
			long expected = Ui.LiveBaseline(liveBefore);
			Gc.WaitForLive(expected);
			Assert.Equal(expected, PixelBlock.LiveCount);
		}

		private static MainReaderHarness Open(double scale)
		{
			var h = new MainReaderHarness(new AppPreferences());
			SmallPreviewViewModel preview = h.Vm.SmallPreview;
			preview.Scheduler = new Browser.ManualLiveScheduler();
			preview.SessionFactory = book => new FakeSession(new FakePageDecoder { PageCount = 6, Size = p => (300, 450) }) { Title = book.Title };
			preview.IsVisible = true;
			h.Tick();
			Dpi.Apply(h.Window, scale);
			Dpi.Settle(h.Window);
			return h;
		}

		[Theory]
		[MemberData(nameof(Scales.AllData), MemberType = typeof(Scales))]
		public async Task The_pane_keeps_its_logical_geometry_and_the_border_is_one_unit_of_whole_device_pixels(double scale)
		{
			await Ui.Run(() =>
			{
				using (MainReaderHarness h = Open(scale))
				{
					try
					{
						CrSizableContainer dock = h.Window.PreviewDock;
						double unit = 1.0 / scale;
						Assert.InRange(dock.Bounds.Height, 207 - 0.001, 207 + unit);
						Assert.InRange(h.Window.Preview.Toolbar.Bounds.Height, 25 - 0.001, 25 + unit);
						Rect frameArea = dock.FrameRectangle;
						Assert.InRange(frameArea.Top, 6 - 0.001, 6 + unit);
						Assert.Equal(dock.Bounds.Width, frameArea.Width);
						Assert.InRange(frameArea.Bottom, dock.Bounds.Height - 0.001, dock.Bounds.Height + 0.001);
						Assert.InRange(h.Window.Preview.Bounds.X, 2 - 0.001, 2 + unit);
						Assert.InRange(h.Window.Preview.Bounds.Y, 8 - 0.001, 8 + unit);

						if (scale != Math.Floor(scale))
						{
							return Task.CompletedTask; // a one unit line is not a whole number of device pixels at 1.5x: the crisp checks are for 1x and 2x
						}
						Raster frame = Dpi.Capture(h.Window, "small-preview-hidpi");
						SKColor border = Token(ThemeTokens.Names.ControlDark, "Light");
						Rect frameRect = dock.FrameRectangle;
						Point o = Dpi.Origin(dock, h.Window);
						int w = Scales.Device(1, scale);
						int left = Scales.Device(o.X + frameRect.Left, scale), top = Scales.Device(o.Y + frameRect.Top, scale);
						int middleY = Scales.Device(o.Y + frameRect.Top + frameRect.Height / 2, scale), middleX = Scales.Device(o.X + frameRect.Left + frameRect.Width / 2, scale);
						// the left edge of the frame: exactly w device pixels, and the pixel after them is not the border
						Assert.Equal(w, frame.CountInRow(middleY, left, left + w + 2, c => Raster.Near(c, border, 0)));
						Assert.False(Raster.Near(frame[left + w, middleY], border, 0));
						// the top edge likewise
						Assert.Equal(w, frame.CountInColumn(middleX, top, top + w + 2, c => Raster.Near(c, border, 0)));
						Assert.False(Raster.Near(frame[middleX, top + w], border, 0));
					}
					finally
					{
						h.Vm.SmallPreview.Close();
					}
				}
				return Task.CompletedTask;
			});
		}

		private static (RectInt? ink, int pixels) MessageInk(MainReaderHarness h, double scale)
		{
			ReaderView viewer = h.Window.Preview.Viewer;
			Raster frame = Dpi.Capture(h.Window, "small-preview-message");
			SKColor ink = Token(ThemeTokens.Names.SmallPreviewPageViewerText, "Light");
			RectInt region = RectInt.Snapped(Dpi.Area(viewer, h.Window), scale);
			RectInt? bounds = frame.Bounds(c => Raster.Near(c, ink, 60), region);
			int count = 0;
			for (int y = region.Top; y < region.Bottom; y++)
			{
				count += frame.CountInRow(y, region.Left, region.Right, c => Raster.Near(c, ink, 60));
			}
			return (bounds, count);
		}

		[Theory]
		[MemberData(nameof(Scales.MainData), MemberType = typeof(Scales))]
		public async Task The_message_of_the_page_viewer_is_centred_and_its_ink_follows_the_scale(double scale)
		{
			await Ui.Run(() =>
			{
				RectInt? one;
				int onePixels;
				using (MainReaderHarness h = Open(1.0))
				{
					(one, onePixels) = MessageInk(h, 1.0);
					h.Vm.SmallPreview.Close();
				}
				using (MainReaderHarness h = Open(scale))
				{
					(RectInt? ink, int pixels) = MessageInk(h, scale);
					output.WriteLine($"scale {scale}: message {ink}, {pixels} ink pixels (1x: {onePixels})");
					Assert.NotNull(ink);
					Assert.NotNull(one);
					ReaderView viewer = h.Window.Preview.Viewer;
					RectInt region = RectInt.Snapped(Dpi.Area(viewer, h.Window), scale);
					double centreX = (ink.Value.Left + ink.Value.Right) / 2.0 / scale, regionX = (region.Left + region.Right) / 2.0 / scale;
					double centreY = (ink.Value.Top + ink.Value.Bottom) / 2.0 / scale, regionY = (region.Top + region.Bottom) / 2.0 / scale;
					Assert.InRange(centreX, regionX - 10, regionX + 10);
					Assert.InRange(centreY, regionY - 10, regionY + 10);
					// the same text in logical units: the box has the same width at every scale (within the glyph rounding), the ink area grows with the square of the scale
					Assert.InRange(ink.Value.Width / scale, one.Value.Width - 4, one.Value.Width + 4);
					Assert.InRange((double)pixels / onePixels, scale * scale * 0.7, scale * scale * 1.3);
					h.Vm.SmallPreview.Close();
				}
				return Task.CompletedTask;
			});
		}

		[Theory]
		[MemberData(nameof(Scales.MainData), MemberType = typeof(Scales))]
		public async Task The_page_in_the_viewer_covers_the_same_logical_area_at_every_scale(double scale)
		{
			await Ui.Run(async () =>
			{
				using (MainReaderHarness h = Open(scale))
				{
					SmallPreviewViewModel preview = h.Vm.SmallPreview;
					var scheduler = (Browser.ManualLiveScheduler)preview.Scheduler;
					preview.FirstSelectedBook = () => new ComicBook { Title = "A", FilePath = "/books/A.cbz", PageCount = 6 };
					preview.SelectionChanged();
					scheduler.RunPending();
					Dispatcher.UIThread.RunJobs();
					for (int i = 0; i < 4000 && (preview.Reader == null || !preview.Reader.IsOpen); i++)
					{
						Dispatcher.UIThread.RunJobs();
						await Task.Delay(5);
					}
					await preview.Reader.WhenSettled;
					h.Tick();
					Raster frame = Dpi.Capture(h.Window, "small-preview-page");
					ReaderView viewer = h.Window.Preview.Viewer;
					RectInt region = RectInt.Snapped(Dpi.Area(viewer, h.Window), scale);
					SKColor page = FakePageDecoder.ColorFor(0);
					RectInt? bounds = frame.Bounds(c => Raster.Near(c, page, 2), region);
					Assert.NotNull(bounds);
					// 300 x 450 fitted into the viewer (about 242 x 172): 115 wide, about the full height
					double widthUnits = bounds.Value.Width / scale, heightUnits = bounds.Value.Height / scale;
					output.WriteLine($"scale {scale}: page {widthUnits} x {heightUnits} units");
					Assert.InRange(widthUnits, 108, 118);
					Assert.InRange(heightUnits, 160, 175);
					preview.Close();
				}
			});
		}
	}
}
