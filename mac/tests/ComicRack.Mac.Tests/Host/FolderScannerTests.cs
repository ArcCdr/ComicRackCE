using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ComicRack.Mac.Host;
using ComicRack.Mac.Host.Library;
using cYo.Common.Threading;
using cYo.Projects.ComicRack.Engine;
using Xunit;

namespace ComicRack.Mac.Tests.Host
{
	public class FolderScannerTests
	{
		private sealed class Collector : IProgress<ScanProgress>
		{
			public readonly List<ScanProgress> Reports = new List<ScanProgress>();

			public void Report(ScanProgress value)
			{
				lock (Reports)
				{
					Reports.Add(value);
				}
			}
		}

		private static MacHost OpenHost(TempDir temp, string name = "host")
		{
			return MacHost.Open(new MacHostOptions { Paths = MacPaths.Under(temp.Combine(name)), DatabaseBackgroundSaveSeconds = 0 });
		}

		[Fact]
		public async Task Scans_a_folder_and_reports_a_count_and_the_current_file()
		{
			using (var temp = new TempDir())
			{
				string books = temp.Combine("books");
				Synthetic.CreateCbzFolder(books, 25);
				using (MacHost host = OpenHost(temp))
				{
					var progress = new Collector();
					var scanner = new FolderScanner(host) { ReportInterval = TimeSpan.Zero };
					ScanResult r = await scanner.ScanAsync(books, progress, CancellationToken.None);

					Assert.Equal(ScanOutcome.Completed, r.Outcome);
					Assert.Equal(25, r.FilesSeen);
					Assert.Equal(25, r.BooksAdded);
					Assert.Equal(25, r.BooksTotal);
					Assert.Equal(25, host.DatabaseManager.Database.Books.Count);
					Assert.False(scanner.IsRunning);

					// Counts only go up, the last report is the final one, files are real paths inside the folder.
					Assert.True(progress.Reports.Count >= 3);
					Assert.True(progress.Reports.Last().IsFinal);
					Assert.Single(progress.Reports.Where(p => p.IsFinal));
					Assert.Equal(25, progress.Reports.Last().FilesSeen);
					for (int i = 1; i < progress.Reports.Count; i++)
					{
						Assert.True(progress.Reports[i].FilesSeen >= progress.Reports[i - 1].FilesSeen);
						Assert.True(progress.Reports[i].BooksAdded >= progress.Reports[i - 1].BooksAdded);
					}
					Assert.All(progress.Reports.Where(p => p.CurrentFile != null), p => Assert.StartsWith(books, p.CurrentFile));
					Assert.Contains(progress.Reports, p => p.CurrentFile != null && p.CurrentFile.EndsWith(".cbz"));
					Assert.Equal(75, host.DatabaseManager.Database.Books.Sum(b => b.PageCount));
				}
			}
		}

		[Fact]
		public async Task Progress_is_throttled_to_the_interval_but_the_last_report_always_comes()
		{
			using (var temp = new TempDir())
			{
				string books = temp.Combine("books");
				Synthetic.CreateCbzFolder(books, 40);
				using (MacHost host = OpenHost(temp))
				{
					var progress = new Collector();
					var scanner = new FolderScanner(host) { ReportInterval = TimeSpan.FromMinutes(5) };
					ScanResult r = await scanner.ScanAsync(books, progress, CancellationToken.None);
					Assert.Equal(40, r.BooksAdded);
					// With a huge interval only the first notification and the final report get through.
					Assert.InRange(progress.Reports.Count, 1, 3);
					Assert.True(progress.Reports.Last().IsFinal);
					Assert.Equal(40, progress.Reports.Last().FilesSeen);
				}
			}
		}

		[Fact]
		public async Task A_cancel_stops_the_scan_and_keeps_what_was_added_and_a_second_scan_finishes_the_job()
		{
			using (var temp = new TempDir())
			{
				string books = temp.Combine("books");
				Synthetic.CreateCbzFolder(books, 60);
				using (MacHost host = OpenHost(temp))
				{
					var scanner = new FolderScanner(host) { ReportInterval = TimeSpan.Zero };
					using (var cts = new CancellationTokenSource())
					{
						ScanResult r = await scanner.ScanAsync(books, new CancelAt(10, cts), cts.Token);
						Assert.Equal(ScanOutcome.Cancelled, r.Outcome);
						Assert.InRange(r.BooksAdded, 10, 59);
						Assert.Equal(r.BooksAdded, host.DatabaseManager.Database.Books.Count);
						Assert.DoesNotContain(ThreadUtility.ActiveThreads, t => t.Name == "Book Scanner");
						Assert.False(scanner.IsRunning);
					}
					int first = host.DatabaseManager.Database.Books.Count;
					ScanResult rest = await scanner.ScanAsync(books, null, CancellationToken.None);
					Assert.Equal(ScanOutcome.Completed, rest.Outcome);
					Assert.Equal(60 - first, rest.BooksAdded);
					Assert.Equal(60, host.DatabaseManager.Database.Books.Count);
					Assert.Equal(60, host.DatabaseManager.Database.Books.Select(b => b.FilePath).Distinct().Count());
				}
			}
		}

		[Fact]
		public async Task A_scan_does_not_wait_for_a_thread_with_the_scanner_name_that_it_did_not_start()
		{
			using (var temp = new TempDir())
			using (var release = new ManualResetEventSlim())
			{
				string books = temp.Combine("books");
				Synthetic.CreateCbzFolder(books, 3);
				// Another host or a test can run a thread with the same name; the old code waited up to 15 s for it.
				Thread foreign = ThreadUtility.CreateWorkerThread("Book Scanner", () => release.Wait(), ThreadPriority.Lowest);
				foreign.Start();
				try
				{
					SpinWait.SpinUntil(() => ThreadUtility.ActiveThreads.Any(t => t.Name == "Book Scanner"), 5000);
					using (MacHost host = OpenHost(temp))
					{
						var watch = System.Diagnostics.Stopwatch.StartNew();
						ScanResult r = await new FolderScanner(host).ScanAsync(books, null, CancellationToken.None);
						Assert.Equal(3, r.BooksAdded);
						Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), "the scan waited for a thread of another owner: " + watch.Elapsed);
					}
				}
				finally
				{
					release.Set();
					foreign.Join(5000);
				}
			}
		}

		private sealed class OnReport : IProgress<ScanProgress>
		{
			private readonly Action<ScanProgress> action;

			public OnReport(Action<ScanProgress> action)
			{
				this.action = action;
			}

			public void Report(ScanProgress value) => action(value);
		}

		[Fact]
		public async Task A_second_scan_waits_for_the_previous_scanner_thread_of_the_same_scanner_to_exit()
		{
			using (var temp = new TempDir())
			{
				string books = temp.Combine("books");
				Synthetic.CreateCbzFolder(books, 5);
				using (MacHost host = OpenHost(temp))
				{
					ComicScanner coreScanner = host.QueueManager.Scanner;
					var scanner = new FolderScanner(host) { ReportInterval = TimeSpan.Zero };
					Assert.Null(coreScanner.LastScanThread);

					// A handler that runs after the one of the FolderScanner and keeps the scanner thread alive for a while after the
					// final notification (the thread still has its queue to clear then). It is added during the first scan, so that it comes later in the invocation list.
					EventHandler<ComicScanNotifyEventArgs> linger = (s, e) =>
					{
						if (e.File.Length == 0)
						{
							Thread.Sleep(700);
						}
					};
					bool added = false;
					await scanner.ScanAsync(books, new OnReport(p =>
					{
						if (!added)
						{
							added = true;
							coreScanner.ScanNotify += linger;
						}
					}), CancellationToken.None);
					coreScanner.ScanNotify -= linger;
					Thread first = coreScanner.LastScanThread;
					Assert.NotNull(first);
					Assert.True(first.IsAlive, "the first scanner thread is still on its way out when its scan has been reported complete");

					bool firstAliveWhenSecondScanStarted = true;
					bool seen = false;
					await scanner.ScanAsync(books, new OnReport(p =>
					{
						if (!seen && p.FilesSeen > 0)
						{
							seen = true;
							firstAliveWhenSecondScanStarted = first.IsAlive;
						}
					}), CancellationToken.None);
					Assert.True(seen);
					Assert.False(firstAliveWhenSecondScanStarted, "the second scan started while the thread of the first one was still running");
					Assert.NotSame(first, coreScanner.LastScanThread);
				}
			}
		}

		[Fact]
		public async Task A_cancel_requested_before_the_start_adds_nothing()
		{
			using (var temp = new TempDir())
			{
				string books = temp.Combine("books");
				Synthetic.CreateCbzFolder(books, 10);
				using (MacHost host = OpenHost(temp))
				{
					using (var cts = new CancellationTokenSource())
					{
						cts.Cancel();
						ScanResult r = await new FolderScanner(host).ScanAsync(books, null, cts.Token);
						Assert.Equal(ScanOutcome.Cancelled, r.Outcome);
						Assert.Equal(0, r.BooksAdded);
						Assert.Empty(host.DatabaseManager.Database.Books);
					}
					// The cancel did not leave the scanner unusable (ComicScanner.Stop would have).
					ScanResult again = await new FolderScanner(host).ScanAsync(books, null, CancellationToken.None);
					Assert.Equal(10, again.BooksAdded);
				}
			}
		}

		[Fact]
		public async Task Only_one_scan_runs_at_a_time_and_a_missing_folder_is_an_error()
		{
			using (var temp = new TempDir())
			{
				string books = temp.Combine("books");
				Synthetic.CreateCbzFolder(books, 30);
				using (MacHost host = OpenHost(temp))
				{
					var scanner = new FolderScanner(host);
					await Assert.ThrowsAsync<DirectoryNotFoundException>(() => scanner.ScanAsync(temp.Combine("missing"), null, CancellationToken.None));
					Assert.False(scanner.IsRunning);
					var first = scanner.ScanAsync(books, null, CancellationToken.None);
					await Assert.ThrowsAsync<InvalidOperationException>(() => scanner.ScanAsync(books, null, CancellationToken.None));
					Assert.Equal(30, (await first).BooksAdded);
				}
			}
		}

		[Fact]
		public async Task Files_that_are_not_comics_are_ignored()
		{
			using (var temp = new TempDir())
			{
				string books = temp.Combine("books");
				Synthetic.CreateCbzFolder(books, 3);
				File.WriteAllText(Path.Combine(books, "notes.txt"), "x");
				File.WriteAllText(Path.Combine(books, "cover.jpg"), "x");
				using (MacHost host = OpenHost(temp))
				{
					ScanResult r = await new FolderScanner(host).ScanAsync(books, null, CancellationToken.None);
					Assert.Equal(3, r.BooksAdded);
				}
			}
		}

		private sealed class CancelAt : IProgress<ScanProgress>
		{
			private readonly int files;
			private readonly CancellationTokenSource cts;

			public CancelAt(int files, CancellationTokenSource cts)
			{
				this.files = files;
				this.cts = cts;
			}

			public void Report(ScanProgress value)
			{
				if (value.FilesSeen >= files)
				{
					cts.Cancel();
				}
			}
		}
	}
}
