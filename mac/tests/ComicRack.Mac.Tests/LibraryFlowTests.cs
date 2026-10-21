using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using ComicRack.Mac.Host.Library;
using ComicRack.Mac.Services;
using cYo.Common.Threading;
using Xunit;
using Xunit.Abstractions;

namespace ComicRack.Mac.Tests
{
	/// <summary>
	/// The accept checks of M2.1 without a visible window: Open Folder (scan), cancel, Import, and save on quit, driven through
	/// the view model and HostService on the Avalonia headless platform, on private data folders.
	/// </summary>
	public class LibraryFlowTests
	{
		private readonly ITestOutputHelper output;

		public LibraryFlowTests(ITestOutputHelper output)
		{
			this.output = output;
		}

		[Fact]
		public async Task A_launch_announces_the_opened_host_to_the_view_model_exactly_once()
		{
			using (new RealLibraryGuard())
			using (var temp = new TempDir())
			{
				await Ui.Run(async () =>
				{
					using (Launch launch = await Launch.Start(temp.Combine("app")))
					{
						await Dispatcher.UIThread.InvokeAsync(() => { }); // anything still posted would run now
						Assert.Equal(1, launch.ViewModel.HostOpenedRuns);
					}
				});
			}
		}

		[FixtureFact("BD-reader")]
		public async Task Scan_save_quit_relaunch_shows_the_same_book_count()
		{
			using (new RealLibraryGuard())
			using (var temp = new TempDir())
			{
				await Ui.Run(async () =>
				{
					int errorsBefore = AppLog.ErrorCount;
					string data = temp.Combine("app");
					int count;
					var clock = Stopwatch.StartNew();
					using (var first = await Launch.Start(data))
					{
						output.WriteLine("first launch ready after " + clock.ElapsedMilliseconds + " ms");
						Assert.Equal(0, first.ViewModel.BookCount);
						var result = await first.ViewModel.ScanFolderAsync(Fixtures.BDReader);
						output.WriteLine("scan returned after " + clock.ElapsedMilliseconds + " ms: " + result.FilesSeen + " files, scan time " + result.Elapsed.TotalMilliseconds + " ms");
						Assert.Equal(ScanOutcome.Completed, result.Outcome);
						Assert.Equal(200, result.BooksAdded);
						count = first.Service.Host.DatabaseManager.Database.Books.Count;
						Assert.Equal(200, count);
						Assert.Equal(200, first.ViewModel.BookCount);
						Assert.False(first.ViewModel.IsBusy);
						Assert.False(first.ViewModel.IsProgressVisible);

						// The scan saved the library itself, and checked the file.
						Assert.True(first.Service.LastSave.Ok, first.Service.LastSave.ToString());
						Assert.Equal(200, first.Service.LastSave.BooksInFile);

						SaveVerification quit = first.Quit();
						output.WriteLine("quit done after " + clock.ElapsedMilliseconds + " ms");
						Assert.True(quit.Ok, quit.ToString());
						Assert.Equal(200, quit.BooksInFile);
						output.WriteLine("quit: " + quit);
					}
					Assert.True(File.Exists(Path.Combine(data, "Data", "ComicDb.xml")));

					using (var second = await Launch.Start(data))
					{
						Assert.Equal(count, second.ViewModel.BookCount);
						Assert.Equal(count, second.Service.Host.DatabaseManager.Database.Books.Count);
						Assert.Contains("200 books", second.ViewModel.BookCountText);
						Assert.False(second.Service.Host.IsNewDatabase);
						second.Quit();
					}
					Assert.Equal(errorsBefore, AppLog.ErrorCount);
				});
			}
		}

		/// <summary>
		/// Starts a scan through the view model and cancels it, through the Cancel command, once the library holds
		/// <paramref name="cancelAtBooks"/> books. Meanwhile a heartbeat on the UI thread measures how late it comes back.
		/// </summary>
		private async Task<(ScanResult Result, long MaxGapMs, int Beats, int ProgressTexts)> ScanAndCancel(Launch launch, string folder, int cancelAtBooks)
		{
			var vm = launch.ViewModel;
			var scan = vm.ScanFolderAsync(folder);
			long maxGapMs = 0;
			int beats = 0;
			bool cancelled = false;
			var clock = Stopwatch.StartNew();
			var progressTexts = new List<string>();
			while (!scan.IsCompleted)
			{
				long t0 = clock.ElapsedMilliseconds;
				await Task.Delay(10);
				maxGapMs = Math.Max(maxGapMs, clock.ElapsedMilliseconds - t0);
				beats++;
				if (progressTexts.Count == 0 || progressTexts[progressTexts.Count - 1] != vm.ProgressCountText)
				{
					progressTexts.Add(vm.ProgressCountText);
				}
				if (!cancelled && vm.BookCount >= cancelAtBooks)
				{
					Assert.True(vm.IsProgressVisible);
					Assert.True(vm.CancelOperationCommand.CanExecute(null));
					Assert.False(vm.OpenFolderCommand.CanExecute(null), "Open Folder must be disabled while a scan runs.");
					Assert.False(vm.ImportWindowsLibraryCommand.CanExecute(null));
					Assert.False(string.IsNullOrEmpty(vm.ProgressFileText));
					vm.CancelOperationCommand.Execute(null);
					Assert.False(vm.CancelOperationCommand.CanExecute(null), "A second Cancel must not be possible.");
					cancelled = true;
				}
			}
			var result = await scan;
			Assert.True(cancelled, "The scan ended before the library held " + cancelAtBooks + " books; nothing was cancelled.");
			return (result, maxGapMs, beats, progressTexts.Count);
		}

		[FixtureFact("BD")]
		public async Task Cancelling_a_big_scan_keeps_the_ui_responsive_and_the_database_consistent()
		{
			using (new RealLibraryGuard())
			using (var temp = new TempDir())
			{
				await Ui.Run(async () =>
				{
					int errorsBefore = AppLog.ErrorCount;
					string data = temp.Combine("app");
					const int totalFiles = 14706;
					using (var launch = await Launch.Start(data))
					{
						var vm = launch.ViewModel;
						var (result, maxGapMs, beats, texts) = await ScanAndCancel(launch, Fixtures.BD, 300);
						output.WriteLine($"cancelled scan 1: {result.FilesSeen} files, {result.BooksAdded} books, {result.Elapsed.TotalSeconds:0.0} s, {beats} UI beats, max UI gap {maxGapMs} ms, {texts} distinct progress texts");

						Assert.Equal(ScanOutcome.Cancelled, result.Outcome);
						Assert.InRange(result.BooksAdded, 300, totalFiles - 1);
						Assert.True(maxGapMs < 500, "The UI thread was blocked for " + maxGapMs + " ms.");
						Assert.True(texts > 2, "Progress did not change while scanning.");

						// The app is usable again.
						Assert.False(vm.IsBusy);
						Assert.False(vm.IsProgressVisible);
						Assert.True(vm.OpenFolderCommand.CanExecute(null));
						Assert.Contains("Scan cancelled", vm.StatusText);

						// The database holds exactly what was added, once each, and it reached the disk.
						var books = launch.Service.Host.DatabaseManager.Database.Books.ToArray();
						Assert.Equal(result.BooksAdded, books.Length);
						Assert.Equal(books.Length, vm.BookCount);
						Assert.Equal(books.Length, books.Select(b => b.FilePath).Distinct(StringComparer.OrdinalIgnoreCase).Count());
						Assert.All(books, b => Assert.True(File.Exists(b.FilePath), b.FilePath));
						Assert.True(launch.Service.LastSave.Ok, launch.Service.LastSave.ToString());
						Assert.Equal(books.Length, launch.Service.LastSave.BooksInFile);
						Assert.DoesNotContain(ThreadUtility.ActiveThreads, t => t.Name == "Book Scanner");
						Assert.False(vm.CancelOperationCommand.CanExecute(null));

						// Scanning the same folder again continues where it stopped: no book twice, the count grows.
						int first = result.BooksAdded;
						var (second, gap2, _, _) = await ScanAndCancel(launch, Fixtures.BD, first + 200);
						output.WriteLine($"cancelled scan 2: {second.FilesSeen} files, {second.BooksAdded} books added, {second.BooksTotal} in the library, max UI gap {gap2} ms");
						Assert.Equal(ScanOutcome.Cancelled, second.Outcome);
						Assert.True(second.BooksAdded >= 200);
						Assert.Equal(first + second.BooksAdded, second.BooksTotal);
						var books2 = launch.Service.Host.DatabaseManager.Database.Books.ToArray();
						Assert.Equal(second.BooksTotal, books2.Length);
						Assert.Equal(books2.Length, books2.Select(b => b.FilePath).Distinct(StringComparer.OrdinalIgnoreCase).Count());

						// Quit while a third scan is running: the scan stops, the library is saved and the file checks out.
						var third = vm.ScanFolderAsync(Fixtures.BD);
						while (vm.BookCount < second.BooksTotal + 50)
						{
							await Task.Delay(10);
						}
						SaveVerification quit = launch.Quit();
						output.WriteLine("quit during a scan: " + quit);
						Assert.True(quit.Ok, quit.ToString());
						Assert.True(quit.BooksInFile >= second.BooksTotal + 50);
						await Task.WhenAny(third, Task.Delay(30000));
					}
					Assert.Equal(errorsBefore, AppLog.ErrorCount);
				});
			}
		}

		[FixtureFact("BD", "ComicDb.xml")]
		public async Task Import_with_a_remap_opens_a_copy_and_leaves_the_source_alone()
		{
			string source = Fixtures.ComicDb;
			string hashBefore = Hashes.Sha256(source);
			DateTime mtimeBefore = File.GetLastWriteTimeUtc(source);
			using (new RealLibraryGuard())
			using (var temp = new TempDir())
			{
				await Ui.Run(async () =>
				{
					int errorsBefore = AppLog.ErrorCount;
					string data = temp.Combine("app");
					using (var launch = await Launch.Start(data))
					{
						var vm = launch.ViewModel;
						launch.Dialogs.ImportToReturn = new ImportRequest(source, new[] { new PathRemapRule(@"S:\media\BD\", Fixtures.BD + "/") });

						// Through the command, as the menu item does.
						var importClock = Stopwatch.StartNew();
						await vm.ImportWindowsLibraryCommand.ExecuteAsync(null);
						output.WriteLine("import command (copy, close, replace, open the copy): " + importClock.ElapsedMilliseconds + " ms; " + vm.StatusText);
						Assert.Equal(1, launch.Dialogs.ImportPrompts);
						Assert.Empty(launch.Dialogs.Messages);

						var host = launch.Service.Host;
						Assert.NotNull(host);
						Assert.Equal(14706, host.DatabaseManager.Database.Books.Count);
						Assert.Equal(14706, vm.BookCount);
						Assert.Contains("Imported " + 14706.ToString("N0"), vm.StatusText);
						Assert.Equal(Path.Combine(data, "Data", "ComicDb.xml"), host.DatabaseFile);
						Assert.All(host.DatabaseManager.Database.Books.Take(500), b => Assert.StartsWith(Fixtures.BD + "/", b.FilePath));
						Assert.All(host.DatabaseManager.Database.Books.Take(500), b => Assert.True(File.Exists(b.FilePath), b.FilePath));
						Assert.Equal(2, host.DatabaseManager.Database.WatchFolders.Count);
						Assert.All(host.DatabaseManager.Database.WatchFolders, w => Assert.False(w.Watch));
						Assert.False(File.Exists(Path.Combine(data, "Data", "ComicDb.import.xml")), "the staged copy must be gone");

						// The source was only read.
						Assert.Equal(hashBefore, Hashes.Sha256(source));
						Assert.Equal(mtimeBefore, File.GetLastWriteTimeUtc(source));

						Assert.Equal(3, host.DatabaseManager.Database.ComicLists.Count);
						var quitClock = Stopwatch.StartNew();
						SaveVerification quit = launch.Quit();
						output.WriteLine("quit with 14,706 books (save + check): " + quitClock.ElapsedMilliseconds + " ms; " + quit);
						Assert.True(quit.Ok, quit.ToString());
						Assert.Equal(14706, quit.BooksInFile);
					}
					using (var again = await Launch.Start(data))
					{
						Assert.Equal(14706, again.ViewModel.BookCount);
						again.Quit();
					}
					Assert.Equal(hashBefore, Hashes.Sha256(source));
					Assert.Equal(errorsBefore, AppLog.ErrorCount);
				});
			}
		}

		[FixtureFact("BD-reader", "ComicDb.xml")]
		public async Task A_second_import_keeps_the_previous_library_as_a_backup()
		{
			using (new RealLibraryGuard())
			using (var temp = new TempDir())
			{
				await Ui.Run(async () =>
				{
					string data = temp.Combine("app");
					using (var launch = await Launch.Start(data))
					{
						await launch.ViewModel.ScanFolderAsync(Fixtures.BDReader);
						Assert.Equal(200, launch.ViewModel.BookCount);

						var result = await launch.ViewModel.ImportLibraryAsync(new ImportRequest(Fixtures.ComicDb, new[] { new PathRemapRule(@"S:\media\BD\", Fixtures.BD + "/") }));
						Assert.Equal(14706, launch.ViewModel.BookCount);
						Assert.NotNull(result.BackupFile);
						Assert.True(File.Exists(result.BackupFile));
						Assert.Equal(200, SaveVerification.CountBooks(result.BackupFile));
						Assert.Contains(Path.GetFileName(result.BackupFile), launch.ViewModel.StatusText);
						launch.Quit();
					}
				});
			}
		}
	}
}
