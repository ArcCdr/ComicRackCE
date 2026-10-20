using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using ComicRack.Mac.Browser;
using ComicRack.Mac.Host.Library;
using ComicRack.Mac.Reader;
using ComicRack.Mac.Services;
using ComicRack.Mac.Tests.Browser;
using ComicRack.Mac.Tests.Thumbnails;
using ComicRack.Mac.Thumbnails;
using ComicRack.Mac.ViewModels;
using ComicRack.Mac.Views;
using cYo.Projects.ComicRack.Engine;
using Xunit;
using Xunit.Abstractions;

namespace ComicRack.Mac.Tests.Reader
{
	/// <summary>A launcher that records what the library window asked of it.</summary>
	public sealed class FakeLauncher : IReaderLauncher
	{
		public List<ComicBook> Opened { get; } = new List<ComicBook>();

		public int CloseAllCalls { get; private set; }

		public int OpenCount => Opened.Count;

		public void Open(ComicBook book) => Opened.Add(book);

		public void CloseAll() => CloseAllCalls++;
	}

	/// <summary>
	/// The accept checks of M2.3a through the app: a book opened from the grid reads in a reader window, paging and closing record the
	/// page in the library, and reopening after a quit and relaunch resumes there. Everything on private data folders.
	/// </summary>
	public class ReaderFlowTests
	{
		private readonly ITestOutputHelper output;

		public ReaderFlowTests(ITestOutputHelper output)
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
				await Task.Delay(5);
			}
		}

		/// <summary>Three stand-in books copied into a folder of their own (a small library that scans in a moment).</summary>
		private static string MakeLibrary(TempDir temp)
		{
			string folder = temp.Combine("books");
			Directory.CreateDirectory(folder);
			foreach (string file in Directory.EnumerateFiles(Fixtures.BDReader, "*.cbz", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal).Take(3))
			{
				File.Copy(file, Path.Combine(folder, Path.GetFileName(file)));
			}
			return folder;
		}

		private static ReaderManager Manager(Launch launch) => (ReaderManager)launch.ViewModel.Readers;

		private static void Press(Window window, Key key)
		{
			window.KeyPress(key, RawInputModifiers.None, key == Key.PageDown ? PhysicalKey.PageDown : key == Key.Escape ? PhysicalKey.Escape : PhysicalKey.None, null);
			Dispatcher.UIThread.RunJobs();
		}

		private static async Task<ReaderWindow> OpenFirstBook(Launch launch, int expectedBooks)
		{
			await FixtureLibrary.WaitForGrid(launch.ViewModel, expectedBooks);
			launch.ViewModel.Browser.SelectedIndex = 0;
			launch.ViewModel.Browser.OpenSelected();
			ReaderManager manager = Manager(launch);
			await WaitFor(() => manager.Windows.Count == 1, "reader window");
			ReaderWindow window = manager.Windows.Single();
			await WaitFor(() => window.Model.IsOpen && window.Model.Scene != null, "first page shown");
			return window;
		}

		[FixtureFact("BD-reader")]
		public async Task A_book_opened_from_the_grid_reads_pages_and_after_a_quit_and_relaunch_resumes_on_the_same_page()
		{
			using (new RealLibraryGuard())
			using (var temp = new TempDir())
			{
				await Ui.Run(async () =>
				{
					int errorsBefore = AppLog.ErrorCount;
					string data = temp.Combine("app");
					string library = MakeLibrary(temp);
					string bookFile;
					using (var first = await Launch.Start(data, svc => new ReaderManager(svc)))
					{
						await first.ViewModel.ScanFolderAsync(library);
						Assert.Equal(3, first.ViewModel.BookCount);
						ReaderWindow window = await OpenFirstBook(first, 3);
						ComicBook book = first.ViewModel.Browser.SelectedItem.Book;
						bookFile = book.FilePath;
						Assert.Equal(0, window.Model.CurrentPage);
						Assert.Equal(1, book.OpenedCount);
						Assert.Contains("Reading", first.ViewModel.StatusText);

						for (int i = 0; i < 4; i++)
						{
							Press(window, Key.PageDown);
						}
						await window.Model.WhenSettled;
						Assert.Equal(4, window.Model.CurrentPage);
						Assert.Equal("5 / 24", window.Model.PageText);
						Assert.Equal(4, book.CurrentPage);

						Press(window, Key.Escape); // closes the reader
						Assert.Equal(0, Manager(first).OpenCount);
						Assert.True(window.Model.IsDisposed);
						Assert.Equal(4, book.LastPageRead);

						SaveVerification quit = first.Quit();
						Assert.True(quit.Ok, quit.ToString());
					}

					using (var second = await Launch.Start(data, svc => new ReaderManager(svc)))
					{
						Assert.Equal(3, second.ViewModel.BookCount);
						ReaderWindow window = await OpenFirstBook(second, 3);
						ComicBook book = second.ViewModel.Browser.SelectedItem.Book;
						Assert.Equal(bookFile, book.FilePath);
						Assert.Equal(4, window.Model.CurrentPage); // resumed
						Assert.Equal(4, window.Model.Scene.Position);
						Assert.Equal("5 / 24", window.Model.PageText);
						Assert.Equal(2, book.OpenedCount);
						Assert.Equal(4, book.LastPageRead);
						Press(window, Key.Escape);
						second.Quit();
					}
					Assert.Equal(errorsBefore, AppLog.ErrorCount);
				});
			}
		}

		[FixtureFact("BD-reader")]
		public async Task Quitting_with_a_reader_open_closes_it_first_so_its_page_is_in_the_saved_library()
		{
			using (new RealLibraryGuard())
			using (var temp = new TempDir())
			{
				await Ui.Run(async () =>
				{
					string data = temp.Combine("app");
					string library = MakeLibrary(temp);
					using (var first = await Launch.Start(data, svc => new ReaderManager(svc)))
					{
						await first.ViewModel.ScanFolderAsync(library);
						ReaderWindow window = await OpenFirstBook(first, 3);
						Press(window, Key.PageDown);
						Press(window, Key.PageDown);
						await window.Model.WhenSettled;
						Assert.Equal(1, Manager(first).OpenCount);
						Assert.True(first.Quit().Ok); // Cmd+Q with the reader still open
						Assert.Equal(0, Manager(first).OpenCount);
						Assert.True(window.Model.IsDisposed);
					}
					using (var second = await Launch.Start(data, svc => new ReaderManager(svc)))
					{
						ReaderWindow window = await OpenFirstBook(second, 3);
						Assert.Equal(2, window.Model.CurrentPage);
						Press(window, Key.Escape);
						second.Quit();
					}
				});
			}
		}

		[FixtureFact("BD-reader")]
		public async Task Opening_a_book_that_is_already_open_brings_its_window_forward_instead_of_making_another()
		{
			using (new RealLibraryGuard())
			using (var temp = new TempDir())
			{
				await Ui.Run(async () =>
				{
					using (var launch = await Launch.Start(temp.Combine("app"), svc => new ReaderManager(svc)))
					{
						await launch.ViewModel.ScanFolderAsync(MakeLibrary(temp));
						ReaderWindow window = await OpenFirstBook(launch, 3);
						launch.ViewModel.Browser.OpenSelected();
						launch.ViewModel.Browser.OpenSelected();
						await Task.Delay(50);
						Assert.Equal(1, Manager(launch).OpenCount);
						Assert.Equal(1, window.Model.Session is CoreReadingSession s ? s.Book.OpenedCount : -1); // counted once

						// a second book gets a second window
						launch.ViewModel.Browser.SelectedIndex = 1;
						launch.ViewModel.Browser.OpenSelected();
						await WaitFor(() => Manager(launch).OpenCount == 2, "second window");
						launch.Quit();
						Assert.Equal(0, Manager(launch).OpenCount);
					}
				});
			}
		}

		[FixtureFact("BD-reader", "ComicDb.xml")]
		public async Task Importing_a_library_closes_the_readers_first_while_the_old_library_is_still_open()
		{
			using (new RealLibraryGuard())
			using (var temp = new TempDir())
			{
				await Ui.Run(async () =>
				{
					using (var launch = await Launch.Start(temp.Combine("app"), svc => new ReaderManager(svc)))
					{
						await launch.ViewModel.ScanFolderAsync(MakeLibrary(temp));
						ReaderWindow window = await OpenFirstBook(launch, 3);
						Press(window, Key.PageDown);
						await window.Model.WhenSettled;
						ComicBook book = ((CoreReadingSession)window.Model.Session).Book;

						var request = new ImportRequest(Fixtures.ComicDb, new[] { new PathRemapRule(@"S:\media\BD\", Fixtures.BD + "/") });
						await launch.ViewModel.ImportLibraryAsync(request);
						Assert.Equal(0, Manager(launch).OpenCount);
						Assert.True(window.Model.IsDisposed);
						Assert.Equal(1, book.CurrentPage); // recorded before the old library was saved away
						Assert.Equal(14706, launch.ViewModel.BookCount);
						launch.Quit();
					}
				});
			}
		}

		[Fact]
		public async Task A_book_that_cannot_be_opened_is_reported_in_the_status_bar_and_nothing_is_launched()
		{
			await Ui.Run(async () =>
			{
				var service = new HostService(AppOptions.Parse(new[] { "--data-dir", Path.Combine(Path.GetTempPath(), "crmac-never-started") }));
				var launcher = new FakeLauncher();
				var vm = new MainViewModel(new NullShell(), service, new FakeDialogs(), launcher);
				var missing = new BookItem(new ComicBook { FilePath = "/no/such/folder/gone.cbz" }, "Gone", "Series", "1", DateTime.Now);
				vm.Browser.SetCatalog(new BookCatalog(new[] { missing }));
				await vm.Browser.WhenIdle();
				vm.Browser.SelectedIndex = 0;
				vm.Browser.OpenSelected();
				Assert.Empty(launcher.Opened);
				Assert.Contains("the file is missing", vm.StatusText);

				string existing = Path.GetTempFileName();
				try
				{
					var present = new BookItem(new ComicBook { FilePath = existing }, "Here", "S", "2", DateTime.Now);
					vm.Browser.SetCatalog(new BookCatalog(new[] { present }));
					await vm.Browser.WhenIdle();
					vm.Browser.SelectedIndex = 0;
					vm.Browser.OpenSelected();
					Assert.Single(launcher.Opened);
					Assert.Equal(existing, launcher.Opened[0].FilePath);
					Assert.Contains("Reading", vm.StatusText);
				}
				finally
				{
					File.Delete(existing);
				}

				// without a launcher (older wiring) the request is only reported
				var bare = new MainViewModel(new NullShell(), service, new FakeDialogs());
				bare.Browser.SetCatalog(new BookCatalog(new[] { missing }));
				await bare.Browser.WhenIdle();
				bare.Browser.SelectedIndex = 0;
				bare.Browser.OpenSelected();
				Assert.Contains("cannot be opened", bare.StatusText);
			});
		}
	}
}
