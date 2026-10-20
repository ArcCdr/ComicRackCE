using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ComicRack.Mac.Diagnostics;
using ComicRack.Mac.Reader;
using ComicRack.Mac.Services;
using ComicRack.Mac.Tests.Reader;
using ComicRack.Mac.ViewModels;
using ComicRack.Mac.Views;
using Xunit;

namespace ComicRack.Mac.Tests.Diagnostics
{
	/// <summary>The overlay in the two windows on the headless platform; frames are saved as PNG when CRMAC_FRAMES_DIR is set.</summary>
	public class PerfOverlayWindowTests
	{
		private static PerfOverlayController NewController(FakeTicker ticker, Func<TimeSpan> clock = null)
		{
			var sources = new PerfSources
			{
				DecodeMilliseconds = () => 23.4,
				CachedTurnMilliseconds = () => 7.5,
				Thumbnails = () => (12, 2),
			};
			return new PerfOverlayController(sources, ticker, clock ?? StopwatchClock.Now, m => { });
		}

		private static void Tick(Window window)
		{
			for (int i = 0; i < 3; i++)
			{
				Dispatcher.UIThread.RunJobs();
				window.UpdateLayout();
				AvaloniaHeadlessPlatform.ForceRenderTimerTick();
			}
		}

		private static WriteableBitmap Capture(Window window, string name)
		{
			Tick(window);
			WriteableBitmap frame = window.CaptureRenderedFrame();
			Assert.NotNull(frame);
			string dir = Environment.GetEnvironmentVariable("CRMAC_FRAMES_DIR");
			if (!string.IsNullOrEmpty(dir))
			{
				Directory.CreateDirectory(dir);
				frame.Save(Path.Combine(dir, name + ".png"));
			}
			return frame;
		}

		private static PerfOverlay OverlayOf(Window window) => window.GetVisualDescendants().OfType<PerfOverlay>().Single();

		private static int Distinct(WriteableBitmap frame, Avalonia.Rect region)
		{
			return ReaderHarness.DistinctColors(frame, region);
		}

		[Fact]
		public async Task The_main_window_shows_the_overlay_only_while_it_is_on_and_runs_no_frame_loop_when_it_is_off()
		{
			await Ui.Run(async () =>
			{
				var ticker = new FakeTicker();
				PerfOverlayController perf = NewController(ticker);
				var window = new MainWindow { Width = 900, Height = 600 };
				window.AttachPerfOverlay(perf);
				window.Show();
				PerfOverlay overlay = OverlayOf(window);

				Capture(window, "perf-main-0-off");
				Assert.False(overlay.IsVisible);
				Assert.False(overlay.IsPumping);
				Assert.False(ticker.IsRunning);

				// Option+Cmd+P through the window
				window.KeyPress(Key.P, RawInputModifiers.Meta | RawInputModifiers.Alt | RawInputModifiers.Control, PhysicalKey.P, null);
				Tick(window);
				Assert.True(perf.IsVisible);
				Assert.True(overlay.IsVisible);
				Assert.True(overlay.IsPumping);
				Assert.True(ticker.IsRunning);

				// frames flow: the render loop calls back, the counter sees them
				for (int i = 0; i < 40; i++)
				{
					Tick(window);
					await Task.Delay(5);
				}
				ticker.Fire();
				Tick(window);
				Assert.True(overlay.Counter.Fps > 0, "no frame reached the frame counter");
				Assert.Contains("Thumbnails  12 queued, 2 running", overlay.Text);
				Assert.Contains("Decode      23.4 ms", overlay.Text);
				WriteableBitmap on = Capture(window, "perf-main-1-on");
				Assert.True(Distinct(on, new Avalonia.Rect(on.PixelSize.Width - 330, 10, 320, 130)) > 8, "the overlay is not drawn");

				window.KeyPress(Key.P, RawInputModifiers.Meta | RawInputModifiers.Alt | RawInputModifiers.Control, PhysicalKey.P, null);
				Tick(window);
				Assert.False(perf.IsVisible);
				Assert.False(overlay.IsVisible);
				Assert.False(overlay.IsPumping);
				Assert.False(ticker.IsRunning);
				// the pending frame request ends by itself: no more frames are counted
				Tick(window);
				double after = overlay.Counter.Fps;
				for (int i = 0; i < 10; i++)
				{
					Tick(window);
				}
				Capture(window, "perf-main-2-off-again");
				Assert.False(overlay.IsPumping);
				window.Close();
				await Task.CompletedTask;
			});
		}

		[Fact]
		public async Task The_reader_window_shows_the_same_overlay_over_the_page()
		{
			await Ui.Run(async () =>
			{
				var ticker = new FakeTicker();
				PerfOverlayController perf = NewController(ticker);
				var decoder = new FakePageDecoder { PageCount = 6, DrawNumbers = true, Size = p => (1200, 1800) };
				var vm = new ReaderViewModel(new FakeSession(decoder), ReaderHarness.DefaultOptions());
				var window = new ReaderWindow(vm, ownsModel: true, perf: perf) { Width = 800, Height = 700 };
				window.Show();
				await vm.OpenAsync();
				await vm.WhenSettled;
				PerfOverlay overlay = OverlayOf(window);

				WriteableBitmap off = Capture(window, "perf-reader-0-off");
				Assert.False(overlay.IsVisible);
				Assert.False(overlay.IsPumping);

				window.KeyPress(Key.P, RawInputModifiers.Meta | RawInputModifiers.Alt | RawInputModifiers.Control, PhysicalKey.P, null);
				Tick(window);
				Assert.True(perf.IsVisible);
				Assert.True(overlay.IsPumping);
				for (int i = 0; i < 40; i++)
				{
					Tick(window);
					await Task.Delay(5);
				}
				ticker.Fire();
				Assert.True(overlay.Counter.Fps > 0);
				WriteableBitmap on = Capture(window, "perf-reader-1-on");
				Assert.Contains("Page turn   7.5 ms", overlay.Text);
				Assert.NotEqual(Distinct(off, new Avalonia.Rect(480, 10, 310, 130)), Distinct(on, new Avalonia.Rect(480, 10, 310, 130)));

				// the overlay does not take the keys: arrows still turn the page
				window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
				await vm.WhenSettled;
				Assert.Equal(1, vm.CurrentPage);

				window.Close();
			});
		}

		[Fact]
		public async Task Two_windows_share_one_switch_and_each_has_its_own_frame_loop()
		{
			await Ui.Run(async () =>
			{
				var ticker = new FakeTicker();
				PerfOverlayController perf = NewController(ticker);
				var main = new MainWindow { Width = 600, Height = 400 };
				main.AttachPerfOverlay(perf);
				main.Show();
				var vm = new ReaderViewModel(new FakeSession(), ReaderHarness.DefaultOptions());
				var reader = new ReaderWindow(vm, ownsModel: true, perf: perf) { Width = 500, Height = 500 };
				reader.Show();
				await vm.OpenAsync();

				perf.Toggle();
				Tick(main);
				Tick(reader);
				Assert.True(OverlayOf(main).IsPumping && OverlayOf(reader).IsPumping);
				Assert.Equal(1, ticker.Starts); // one timer for both

				reader.Close();
				Assert.True(perf.IsVisible);
				Assert.True(OverlayOf(main).IsPumping);
				perf.Toggle();
				Assert.False(OverlayOf(main).IsPumping);
				Assert.False(ticker.IsRunning);
				main.Close();
			});
		}
	}
}
