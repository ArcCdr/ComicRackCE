using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ComicRack.Mac.Browser;
using ComicRack.Mac.Diagnostics;
using ComicRack.Mac.Thumbnails;
using ComicRack.Mac.Tests.Thumbnails;
using ComicRack.Mac.Views;
using Xunit;
using Xunit.Abstractions;

namespace ComicRack.Mac.Tests.Browser
{
	/// <summary>The grid over the real 14,706-book fixture library (stand-in covers) with the real thumbnail service.</summary>
	public class FixtureGridTests
	{
		private readonly ITestOutputHelper output;

		public FixtureGridTests(ITestOutputHelper output)
		{
			this.output = output;
		}

		private static void Layout(Window window)
		{
			for (int i = 0; i < 3; i++)
			{
				Dispatcher.UIThread.RunJobs();
				window.UpdateLayout();
				AvaloniaHeadlessPlatform.ForceRenderTimerTick();
			}
		}

		private static string Save(Window window, string name)
		{
			Layout(window);
			WriteableBitmap frame = window.CaptureRenderedFrame();
			Assert.NotNull(frame);
			string dir = Environment.GetEnvironmentVariable("CRMAC_FRAMES_DIR");
			if (!string.IsNullOrEmpty(dir))
			{
				Directory.CreateDirectory(dir);
				string file = Path.Combine(dir, name + ".png");
				frame.Save(file);
				return file;
			}
			return null;
		}

		private static async Task WaitForCovers(BookGridView grid, Window window, int timeoutMs = 30000)
		{
			var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
			while (true)
			{
				Layout(window);
				if (grid.RealizedCount > 0 && grid.BoundCells().All(c => c.Cover.HasImage))
				{
					return;
				}
				if (DateTime.UtcNow > deadline)
				{
					throw new TimeoutException($"{grid.BoundCells().Count(c => !c.Cover.HasImage)} of {grid.RealizedCount} cells have no cover.");
				}
				await Task.Delay(2);
			}
		}

		private static long ResidentBytes()
		{
			using (Process p = Process.GetCurrentProcess())
			{
				return p.WorkingSet64;
			}
		}

		/// <summary>
		/// The memory footprint of this process as Activity Monitor shows it (dirty and compressed pages). Unlike the resident size it does not
		/// drop when the system compresses or swaps pages of the process under memory pressure.
		/// </summary>
		private static long FootprintBytes() => ProcessMemory.Read().FootprintBytes;

		[FixtureFact("BD", "ComicDb.xml")]
		public async Task The_main_window_shows_the_imported_library_with_covers_search_sort_and_open()
		{
			using (new RealLibraryGuard())
			using (var temp = new TempDir())
			{
				await Ui.Run(async () =>
				{
					int errorsBefore = Services.AppLog.ErrorCount;
					var import = Stopwatch.StartNew();
					var launcher = new Reader.FakeLauncher();
					using (Launch launch = await FixtureLibrary.Open14k(temp, svc => launcher))
					{
						output.WriteLine($"import and grid ready: {import.ElapsedMilliseconds} ms; catalog build {launch.ViewModel.Browser.LastCatalogMilliseconds:0} ms; first query {launch.ViewModel.Browser.LastQueryMilliseconds:0} ms");
						var vm = launch.ViewModel;
						var window = new MainWindow { DataContext = vm };
						window.Show();
						var grid = window.FindControl<BookGridView>("BookGrid");
						Layout(window);
						Assert.False(window.FindControl<StackPanel>("EmptyState").IsVisible);
						Assert.Equal(14706.ToString("N0") + " books", window.FindControl<TextBlock>("SummaryLabel").Text);

						var clock = Stopwatch.StartNew();
						await WaitForCovers(grid, window);
						output.WriteLine($"covers of the first screen ({grid.RealizedCount} cells), from cold caches: {clock.ElapsedMilliseconds} ms");
						string frame = Save(window, "main-6-fixture-library");
						Assert.All(grid.BoundCells(), c => Assert.False(string.IsNullOrEmpty(c.TitleText)));

						// Search, as typed in the box.
						var box = window.FindControl<TextBox>("SearchBox");
						box.Text = "lester";
						await vm.Browser.WhenIdle();
						await WaitForCovers(grid, window);
						output.WriteLine($"search \"lester\": {vm.Browser.SummaryText}, query {vm.Browser.LastQueryMilliseconds:0.0} ms");
						Assert.Contains(" of " + 14706.ToString("N0") + " books", window.FindControl<TextBlock>("SummaryLabel").Text);
						Assert.All(vm.Browser.Items, i => Assert.True(i.TitleKey.Contains("lester") || i.SeriesKey.Contains("lester")));
						Save(window, "main-7-fixture-search");

						// Sort by date added; clear the search.
						box.Text = string.Empty;
						window.FindControl<ComboBox>("SortBox").SelectedIndex = 2;
						await vm.Browser.WhenIdle();
						await WaitForCovers(grid, window);
						Assert.Equal(2, vm.Browser.SortIndex);
						Assert.Equal(14706, vm.Browser.Items.Length);
						Save(window, "main-8-fixture-sorted-by-date");

						// Size slider.
						window.FindControl<Slider>("SizeSlider").Value = 120;
						Assert.Equal(120, vm.Browser.ThumbnailSize);
						await WaitForCovers(grid, window);
						Save(window, "main-9-fixture-size-120");
						window.FindControl<Slider>("SizeSlider").Value = 300;
						await WaitForCovers(grid, window);
						Save(window, "main-10-fixture-size-300");

						// Enter on a selected book asks the reader launcher to open it (M2.3a).
						grid.Focus();
						window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
						window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
						Assert.Single(launcher.Opened);
						Assert.Equal(vm.Browser.SelectedItem.Book, launcher.Opened[0]);
						Assert.Contains("Reading", vm.StatusText);
						output.WriteLine("status after Enter: " + vm.StatusText);

						window.Close();
						Assert.Equal(errorsBefore, Services.AppLog.ErrorCount);
					}
				});
			}
		}

		// About 75 s: excluded from the fast gate (mac/tools/verify.sh), run by the full gate.
		[FixtureFact("BD", "ComicDb.xml")]
		[Trait("Category", "Slow")]
		public async Task A_full_scroll_over_the_fixture_library_stays_under_the_cache_bound()
		{
			using (new RealLibraryGuard())
			using (var temp = new TempDir())
			{
				await Ui.Run(async () =>
				{
					using (Launch launch = await FixtureLibrary.Open14k(temp))
					{
						var vm = launch.ViewModel;
						var window = new MainWindow { DataContext = vm };
						window.Show();
						var grid = window.FindControl<BookGridView>("BookGrid");
						await WaitForCovers(grid, window);
						await vm.Sidebar.CountsReady; // M2.2c: the lists' counts fill the Core's list caches; they belong to the base, not to the scroll
						ThumbnailService service = launch.Service.Thumbnails;
						Assert.NotNull(service);
						long bound = service.GetStats().MaxCacheBytes;

						double Mb(long b) => b / 1048576.0;
						Gc.Flush();
						long rssBase = ResidentBytes();
						long footBase = FootprintBytes();
						long peakFoot = footBase;
						long footWarm = -1, peakAfterWarm = 0; // taken once the cache is full (30% of the rows)
						long managedBase = GC.GetTotalMemory(true);
						long liveBase = PixelBlock.LiveBytes;
						long peakRss = rssBase, peakLive = liveBase, peakCached = 0;
						int rows = grid.RowCount;
						int viewRows = Math.Max(1, (int)(grid.Scroller.Viewport.Height / (grid.CellHeight + BookGridView.Spacing)));
						int step = Math.Max(1, viewRows - 1); // every row is on screen in at least two positions
						var stepMs = new List<double>();
						var deciles = new List<string>();
						int maxRealized = 0;
						int positions = 0;
						var total = Stopwatch.StartNew();
						for (int row = 0; row < rows; row += step)
						{
							if (positions > 0 && row * 10 / rows != (row - step) * 10 / rows)
							{
								// Where the memory goes while the scroll runs: it must level off once the cache is full, not grow with the library.
								deciles.Add($"{row * 100 / rows}%: RSS {Mb(ResidentBytes()):0} MB, managed {Mb(GC.GetTotalMemory(true)):0} MB, pixels {Mb(PixelBlock.LiveBytes):0} MB, cached {service.GetStats().CachedCount} images");
							}
							if (footWarm < 0 && row >= rows * 3 / 10)
							{
								Gc.Flush();
								footWarm = FootprintBytes();
								peakAfterWarm = footWarm;
							}
							var sw = Stopwatch.StartNew();
							grid.Scroller.Offset = new Avalonia.Vector(0, grid.OffsetOfRow(row));
							await WaitForCovers(grid, window, 60000);
							stepMs.Add(sw.Elapsed.TotalMilliseconds);
							positions++;
							maxRealized = Math.Max(maxRealized, grid.RealizedCount);
							if (positions % 25 == 0)
							{
								peakRss = Math.Max(peakRss, ResidentBytes());
								long foot = FootprintBytes();
								peakFoot = Math.Max(peakFoot, foot);
								peakAfterWarm = Math.Max(peakAfterWarm, foot);
							}
							ThumbnailServiceStats s = service.GetStats();
							peakCached = Math.Max(peakCached, s.CachedBytes);
							peakLive = Math.Max(peakLive, PixelBlock.LiveBytes);
						}
						total.Stop();
						Save(window, "main-11-fixture-end-of-scroll");

						ThumbnailServiceStats end = service.GetStats();
						long rssEnd = ResidentBytes();
						peakRss = Math.Max(peakRss, rssEnd);
						Gc.Flush();
						long rssAfterGc = ResidentBytes();
						long footEnd = FootprintBytes();
						long managedEnd = GC.GetTotalMemory(true);
						long liveEnd = PixelBlock.LiveBytes;
						stepMs.Sort();
						output.WriteLine($"scrolled {rows:N0} rows in {positions:N0} positions of {step} rows ({total.Elapsed.TotalSeconds:0.0} s); at most {maxRealized} cells realized");
						output.WriteLine($"service: {end.Requests:N0} requests, {end.DecodesCompleted:N0} decodes completed, {end.DecodesCancelled:N0} cancelled, {end.DroppedFromQueue:N0} dropped from the queue, {end.CacheHits:N0} hits, peak in flight {end.PeakInFlight}, cached {end.CachedCount:N0} images / {Mb(end.CachedBytes):0.0} MB (bound {Mb(bound):0} MB), failures {end.Failures}");
						output.WriteLine($"time per position (move, layout, render, covers on screen): median {stepMs[stepMs.Count / 2]:0.0} ms, p95 {stepMs[(int)(stepMs.Count * 0.95)]:0.0} ms, max {stepMs.Last():0.0} ms");
						output.WriteLine($"pixels alive (PixelBlock.LiveBytes): base {Mb(liveBase):0.0} MB, peak {Mb(peakLive):0.0} MB, end {Mb(liveEnd):0.0} MB");
						output.WriteLine($"footprint: base {Mb(footBase):0} MB, cache full (30%) {Mb(footWarm):0} MB, peak {Mb(peakFoot):0} MB (+{Mb(peakFoot - footBase):0}), peak after 30% +{Mb(peakAfterWarm - footWarm):0} MB, end after GC {Mb(footEnd):0} MB (+{Mb(footEnd - footWarm):0} from 30%)");
						foreach (string d in deciles)
						{
							output.WriteLine("  at " + d);
						}
						output.WriteLine($"RSS: base {Mb(rssBase):0} MB, peak {Mb(peakRss):0} MB (+{Mb(peakRss - rssBase):0}), end {Mb(rssEnd):0} MB, after GC {Mb(rssAfterGc):0} MB (+{Mb(rssAfterGc - rssBase):0}); managed heap base {Mb(managedBase):0} MB, end {Mb(managedEnd):0} MB");

						Assert.True(end.CachedBytes <= bound, $"the cache holds {Mb(end.CachedBytes):0} MB, bound {Mb(bound):0} MB");
						long onScreen = (long)maxRealized * 512 * 512 * 4;
						Assert.True(peakLive <= bound + onScreen, $"pixels alive peaked at {Mb(peakLive):0} MB; bound {Mb(bound):0} MB plus the cells {Mb(onScreen):0} MB");
						// The 256 MB bound is for the pictures (asserted above). The process also holds the Core's own caches (a 50 MB memory cache of
						// thumbnail data, the disk cache index), Skia's caches and malloc's free lists, and how much of that is already there when the
						// test starts depends on the tests that ran before it: the managed heap at the start ranged from 150 to 790 MB, the memory above
						// the baseline after the scroll from -210 to +570 MB, and the unflushed peak (garbage the GC has not collected yet) from +190
						// to +520 MB for the same code. So the memory is not compared with the start. What matters is that it levels off once the cache
						// is full instead of growing with the library, and that is measured between two points that were both taken after a full
						// GC: a leak of one cover in twenty adds more than 500 MB in the last 70% of the scroll, and the same code measured between
						// -100 and +90 MB, so one cache bound (256 MB) leaves the noise out and keeps every real leak in.
						Assert.True(footEnd - footWarm <= bound, $"after the scroll and a GC the footprint is {Mb(footEnd - footWarm):0} MB above the one with the cache full; allowed {Mb(bound):0} MB");
						// The peak between the two points is sampled every 25 positions without a GC, so it also holds garbage that is not collected yet
						// (the unflushed peak above ranged from +190 to +520 MB over the baseline). A leak that is later hidden by a lucky GC at the end
						// still shows up here; two cache bounds (512 MB) leave the garbage out.
						Assert.True(footWarm >= 0, "the cache never filled up (the warm point was not taken)");
						Assert.True(peakAfterWarm - footWarm <= 2 * bound, $"between the cache being full and the end the footprint peaked {Mb(peakAfterWarm - footWarm):0} MB above the full-cache point; allowed {Mb(2 * bound):0} MB");
						Assert.True(end.DecodesCompleted > 10000, "the scroll must have decoded most covers: " + end.DecodesCompleted);
						Assert.True(maxRealized < 100);
						Assert.True(end.PeakInFlight <= 4);

						// A flick: jump 6 rows at a time without waiting for any cover. Cells that are scrolled past withdraw their requests.
						grid.Scroller.Offset = new Avalonia.Vector(0, 0);
						await WaitForCovers(grid, window, 60000);
						service.Clear(); // so that the covers have to be fetched again (from the Core's caches, which are warm)
						ThumbnailServiceStats before = service.GetStats();
						var flickMs = new List<double>();
						for (int row = 0; row < rows - 20; row += 6)
						{
							var sw = Stopwatch.StartNew();
							grid.Scroller.Offset = new Avalonia.Vector(0, grid.OffsetOfRow(row));
							Layout(window);
							flickMs.Add(sw.Elapsed.TotalMilliseconds);
							await Task.Yield();
						}
						await WaitForCovers(grid, window, 60000);
						ThumbnailServiceStats after = service.GetStats();
						flickMs.Sort();
						output.WriteLine($"flick over {flickMs.Count} positions without waiting: UI time per position (move, layout, render) median {flickMs[flickMs.Count / 2]:0.0} ms, p95 {flickMs[(int)(flickMs.Count * 0.95)]:0.0} ms, max {flickMs.Last():0.0} ms; {after.Requests - before.Requests:N0} requests, {after.DroppedFromQueue - before.DroppedFromQueue:N0} dropped from the queue, {after.DecodesCompleted - before.DecodesCompleted:N0} decodes completed, {after.DecodesCancelled - before.DecodesCancelled:N0} cancelled");
						Assert.True(after.DroppedFromQueue - before.DroppedFromQueue > 100, "a flick must withdraw requests of cells that were passed");
						Assert.True(after.DecodesCompleted - before.DecodesCompleted < (after.Requests - before.Requests) / 2, "a flick must not decode every cover it passes");
						window.Close();
					}
				});
			}
		}
	}
}
