using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Input;
using Avalonia.Threading;
using ComicRack.Mac.Browser;
using ComicRack.Mac.Reader;
using ComicRack.Mac.Services;
using ComicRack.Mac.Tests.Browser;
using ComicRack.Mac.Thumbnails;
using ComicRack.Mac.ViewModels;
using ComicRack.Mac.Views;
using ComicRack.Mac.Host.Library;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Engine.IO;
using Xunit;
using Xunit.Abstractions;

namespace ComicRack.Mac.Tests.Reader
{
	/// <summary>
	/// Page-turn measurements (M2.3a accept: "cached page-turn time is measured (log) and recorded"). These run on the headless
	/// platform, where a frame is drawn by Skia on the CPU, so the draw part is an upper bound of what the Metal backend costs and
	/// "presented" is the end of the draw call. The numbers are printed (ITestOutputHelper) and the bounds only catch regressions.
	/// </summary>
	public class ReaderTimingTests
	{
		private readonly ITestOutputHelper output;

		public ReaderTimingTests(ITestOutputHelper output)
		{
			this.output = output;
		}

		private static async Task WaitFor(Func<bool> condition, string what, int timeoutMs = 20000)
		{
			var watch = Stopwatch.StartNew();
			while (!condition())
			{
				if (watch.ElapsedMilliseconds > timeoutMs)
				{
					throw new TimeoutException("Not reached: " + what);
				}
				await Task.Delay(3);
			}
		}

		private static double Percentile(IEnumerable<double> values, double p)
		{
			double[] v = values.OrderBy(x => x).ToArray();
			return v.Length == 0 ? 0 : v[Math.Min(v.Length - 1, (int)Math.Ceiling(v.Length * p) - 1)];
		}

		private void Report(string title, PageTurnRecord[] records)
		{
			if (records.Length == 0)
			{
				output.WriteLine(title + ": no turns");
				return;
			}
			output.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
				"{0}: {1} turns, total median {2:0.0} ms, p95 {3:0.0} ms, max {4:0.0} ms; ui median {5:0.0} ms, draw median {6:0.0} ms",
				title, records.Length, Percentile(records.Select(r => r.TotalMilliseconds), 0.5), Percentile(records.Select(r => r.TotalMilliseconds), 0.95),
				records.Max(r => r.TotalMilliseconds), Percentile(records.Select(r => r.UiMilliseconds), 0.5), Percentile(records.Select(r => r.RenderMilliseconds), 0.5)));
		}

		[FixtureFact("small")]
		public async Task Page_turns_with_prefetch_on_pages_of_2_to_6_megapixels()
		{
			using (var core = new CoreFixture())
			{
				await Ui.Run(async () =>
				{
					string file = Path.Combine(Fixtures.Root, "small", "Fixture Series 01 001.cbz");
					ComicBook book = core.Book(file);
					var session = new CoreReadingSession(book, core.Host.ImagePool);
					using (ReaderHarness h = await ReaderHarness.Open(session, 1000, 900))
					{
						ReaderViewModel vm = h.Vm;
						// A reader who reads: each page stays up for a moment, which is when the prefetch of the next two runs.
						for (int turn = 0; turn < 20; turn++)
						{
							int next = vm.CurrentPage + 1;
							await WaitFor(() => PrefetchReady(vm, next, 2), "prefetch of the next pages");
							h.Press(Key.PageDown);
						}
						PageTurnRecord[] cached = vm.Meter.Records.Where(r => r.Cached && r.Position > 0).ToArray();
						Report("cached page turns (headless platform, Skia draws on the CPU)", cached);
						Assert.True(cached.Length >= 15, "cached turns recorded: " + cached.Length);

						// A jump to a page that was not prefetched waits for its decode (the previous page stays up meanwhile).
						int before = vm.Meter.Records.Length;
						foreach (int target in new[] { 22, 3, 18, 8, 13, 0 })
						{
							await vm.GoToAsync(target);
							await h.Settled();
						}
						PageTurnRecord[] decoded = vm.Meter.Records.Skip(before).Where(r => !r.Cached).ToArray();
						Report("page turns that had to decode (Core read + Skia decode + draw)", decoded);
						foreach (PageTurnRecord r in decoded)
						{
							output.WriteLine("  " + r);
						}

						double median = Percentile(cached.Select(r => r.TotalMilliseconds), 0.5);
						double p95 = Percentile(cached.Select(r => r.TotalMilliseconds), 0.95);
						Assert.True(median < 60, "cached page turn median " + median + " ms");
						Assert.True(p95 < 150, "cached page turn p95 " + p95 + " ms");
						// The UI-thread part (command to scene) does no decoding at all for a cached page.
						Assert.True(Percentile(cached.Select(r => r.UiMilliseconds), 0.95) < 15, "ui part p95");
					}
				});
			}
		}

		private static bool PrefetchReady(ReaderViewModel vm, int next, int count)
		{
			for (int i = 0; i < count; i++)
			{
				int page = next + i;
				if (page >= vm.PageCount)
				{
					break;
				}
				CoverImage image = vm.Pages.TryGetCached(page);
				if (image == null)
				{
					return false;
				}
				image.Release();
			}
			return true;
		}

		[FixtureFact("small")]
		public void The_decode_of_a_page_split_into_the_core_read_and_the_skia_decode()
		{
			using (var core = new CoreFixture())
			{
				ComicBook book = core.Book(Path.Combine(Fixtures.Root, "small", "Fixture Series 01 001.cbz"));
				var navigator = book.CreateNavigator();
				var opened = new ManualResetEventSlim();
				navigator.Opened += (s, e) => opened.Set();
				navigator.Open(false, 0);
				Assert.True(opened.Wait(10000));
				try
				{
					var decoder = new CorePageDecoder(navigator, core.Host.ImagePool);
					for (int page = 0; page < 6; page++)
					{
						// first run: the Core reads the archive; second run: its page cache has the bytes
						var watch = Stopwatch.StartNew();
						PixelBlock cold = decoder.Decode(page, CancellationToken.None);
						double coldMs = watch.Elapsed.TotalMilliseconds;
						watch.Restart();
						PixelBlock warm = decoder.Decode(page, CancellationToken.None);
						double warmMs = watch.Elapsed.TotalMilliseconds;
						output.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
							"page {0}: {1} x {2} ({3:0.0} MP), first decode {4:0.0} ms (Core read + Skia), again {5:0.0} ms (Core cache + Skia)",
							page + 1, cold.Width, cold.Height, cold.Width * (double)cold.Height / 1e6, coldMs, warmMs));
						cold.Release();
						warm.Release();
					}
				}
				finally
				{
					navigator.Dispose();
				}
			}
		}

		[FixtureFact("BD-reader", "ComicDb.xml")]
		public async Task Recording_the_page_in_the_14706_book_library_costs_the_ui_thread_next_to_nothing()
		{
			using (new RealLibraryGuard())
			using (var temp = new TempDir())
			{
				await Ui.Run(async () =>
				{
					using (var launch = await Launch.Start(temp.Combine("app"), svc => new ReaderManager(svc)))
					{
						var request = new ImportRequest(Fixtures.ComicDb, new[] { new PathRemapRule(@"S:\media\BD\", Fixtures.BDReader + "/") });
						await launch.ViewModel.ImportLibraryAsync(request);
						await FixtureLibrary.WaitForGrid(launch.ViewModel, 14706);
						await launch.ViewModel.Sidebar.CountsReady; // the lists' caches are built: the case where a book change costs the most
						BookItem item = launch.ViewModel.Browser.Items.First(i => i.Book != null && File.Exists(i.Book.FilePath));
						ComicBook book = item.Book;

						// The raw cost of the Core's property change (what ReportPage does).
						var times = new List<double>();
						for (int page = 1; page <= 20; page++)
						{
							var watch = Stopwatch.StartNew();
							book.CurrentPage = page;
							times.Add(watch.Elapsed.TotalMilliseconds);
						}
						output.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
							"ComicBook.CurrentPage = n in a 14,706-book library with all list caches built: median {0:0.00} ms, p95 {1:0.00} ms, max {2:0.00} ms",
							Percentile(times, 0.5), Percentile(times, 0.95), times.Max()));
						book.CurrentPage = 0;

						launch.ViewModel.Browser.SelectedIndex = Array.IndexOf(launch.ViewModel.Browser.Items, item);
						launch.ViewModel.Browser.OpenSelected();
						ReaderManager manager = (ReaderManager)launch.ViewModel.Readers;
						await WaitFor(() => manager.Windows.Count == 1, "reader window");
						ReaderWindow window = manager.Windows.Single();
						await WaitFor(() => window.Model.IsOpen && window.Model.Scene != null, "first page");
						// The command of a cached page turn runs on the UI thread from the key to the new scene: time it directly.
						var commands = new List<double>();
						for (int i = 0; i < 20; i++)
						{
							await WaitFor(() => PrefetchReady(window.Model, window.Model.CurrentPage + 1, 1), "prefetch");
							var watch = Stopwatch.StartNew();
							window.Model.NextPage();
							commands.Add(watch.Elapsed.TotalMilliseconds);
							Assert.True(window.Model.WhenSettled.IsCompleted);
						}
						output.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
							"cached page turn command on the UI thread in the big library (record the page, move the window, publish the scene): median {0:0.00} ms, p95 {1:0.00} ms, max {2:0.00} ms",
							Percentile(commands, 0.5), Percentile(commands, 0.95), commands.Max()));
						Assert.True(Percentile(times, 0.95) < 20, "CurrentPage write p95 " + Percentile(times, 0.95) + " ms");
						Assert.True(Percentile(commands, 0.95) < 30, "page turn command p95 " + Percentile(commands, 0.95) + " ms");
						window.Close();
						launch.Quit();
					}
				});
			}
		}
	}
}
