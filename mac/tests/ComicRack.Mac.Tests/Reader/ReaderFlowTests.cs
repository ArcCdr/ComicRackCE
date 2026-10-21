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

		public void Open(ComicBook book)
		{
			Opened.Add(book);
			OpenBooksChanged?.Invoke(this, EventArgs.Empty);
		}

		public void CloseAll() => CloseAllCalls++;

		public void Close(ComicBook book)
		{
			Opened.Remove(book);
			OpenBooksChanged?.Invoke(this, EventArgs.Empty);
		}

		public bool IsOpen(ComicBook book) => Opened.Contains(book);

		public event EventHandler OpenBooksChanged;
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

		private static void Press(ReaderViewModel model, Key key)
		{
			model.HandleKey(key, KeyModifiers.None);
			Dispatcher.UIThread.RunJobs();
		}

		private static async Task<ReaderViewModel> OpenFirstBook(Launch launch, int expectedBooks)
		{
			await FixtureLibrary.WaitForGrid(launch.ViewModel, expectedBooks);
			launch.ViewModel.Browser.SelectedIndex = 0;
			launch.ViewModel.Browser.OpenSelected();
			ReaderManager manager = Manager(launch);
			await WaitFor(() => manager.CurrentModel != null, "reader tab");
			ReaderViewModel model = manager.CurrentModel;
			await WaitFor(() => model.IsOpen && model.Scene != null, "first page shown");
			return model;
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
						ReaderViewModel window = await OpenFirstBook(first, 3);
						ComicBook book = first.ViewModel.Browser.SelectedItem.Book;
						bookFile = book.FilePath;
						Assert.Equal(0, window.CurrentPage);
						Assert.Equal(1, book.OpenedCount);
						Assert.Contains("Reading", first.ViewModel.StatusText);

						for (int i = 0; i < 4; i++)
						{
							Press(window, Key.PageDown);
						}
						await window.WhenSettled;
						Assert.Equal(4, window.CurrentPage);
						Assert.Equal("5 / 24", window.PageText);
						Assert.Equal(4, book.CurrentPage);

						window.Close(); // closes the book's tab
						Assert.Equal(0, Manager(first).OpenCount);
						Assert.True(window.IsDisposed);
						Assert.Equal(4, book.LastPageRead);

						SaveVerification quit = first.Quit();
						Assert.True(quit.Ok, quit.ToString());
					}

					using (var second = await Launch.Start(data, svc => new ReaderManager(svc)))
					{
						Assert.Equal(3, second.ViewModel.BookCount);
						ReaderViewModel window = await OpenFirstBook(second, 3);
						ComicBook book = second.ViewModel.Browser.SelectedItem.Book;
						Assert.Equal(bookFile, book.FilePath);
						Assert.Equal(4, window.CurrentPage); // resumed
						Assert.Equal(4, window.Scene.Position);
						Assert.Equal("5 / 24", window.PageText);
						Assert.Equal(2, book.OpenedCount);
						Assert.Equal(4, book.LastPageRead);
						window.Close();
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
						ReaderViewModel window = await OpenFirstBook(first, 3);
						Press(window, Key.PageDown);
						Press(window, Key.PageDown);
						await window.WhenSettled;
						Assert.Equal(1, Manager(first).OpenCount);
						Assert.True(first.Quit().Ok); // Cmd+Q with the reader still open
						Assert.Equal(0, Manager(first).OpenCount);
						Assert.True(window.IsDisposed);
					}
					using (var second = await Launch.Start(data, svc => new ReaderManager(svc)))
					{
						ReaderViewModel window = await OpenFirstBook(second, 3);
						Assert.Equal(2, window.CurrentPage);
						window.Close();
						second.Quit();
					}
				});
			}
		}

		[FixtureFact("BD-reader")]
		public async Task Opening_a_book_that_is_already_open_shows_its_tab_a_second_book_takes_the_place_of_the_current_one_and_Open_in_new_Tab_adds_a_tab_at_the_left()
		{
			using (new RealLibraryGuard())
			using (var temp = new TempDir())
			{
				await Ui.Run(async () =>
				{
					using (var launch = await Launch.Start(temp.Combine("app"), svc => new ReaderManager(svc)))
					{
						await launch.ViewModel.ScanFolderAsync(MakeLibrary(temp));
						ReaderViewModel first = await OpenFirstBook(launch, 3);
						ReaderManager manager = Manager(launch);
						launch.ViewModel.Browser.OpenSelected();
						launch.ViewModel.Browser.OpenSelected();
						await Task.Delay(50);
						Assert.Equal(1, manager.OpenCount);
						Assert.Equal(1, first.Session is CoreReadingSession s ? s.Book.OpenedCount : -1); // counted once

						// a second book takes the place of the book of the current tab (NavigatorManager.Open without OpenInNewSlot): the first reader is closed
						launch.ViewModel.Browser.SelectedIndex = 1;
						ComicBook second = launch.ViewModel.Browser.SelectedItem.Book;
						launch.ViewModel.Browser.OpenSelected();
						await WaitFor(() => ReferenceEquals(manager.Current?.Book, second), "second book in the tab");
						Assert.Equal(1, manager.OpenCount);
						Assert.True(first.IsDisposed);

						// Open in new Tab (Cmd+Shift+O) adds a tab at the left end, which becomes the current one
						launch.ViewModel.Browser.SelectedIndex = 2;
						ComicBook third = launch.ViewModel.Browser.SelectedItem.Book;
						launch.ViewModel.Browser.OpenSelectedInNewTab();
						await WaitFor(() => manager.OpenCount == 2, "second tab");
						Assert.Same(third, manager.Slots[0].Book);
						Assert.Same(second, manager.Slots[1].Book);
						Assert.Equal(0, manager.CurrentSlot);

						// with Cmd or Shift the setting is reversed: a plain Open now (setting off) opens in a new tab
						launch.ViewModel.Browser.SelectedIndex = 0;
						launch.ViewModel.Browser.OpenSelected(OpenTabMode.Invert);
						await WaitFor(() => manager.OpenCount == 3, "third tab");
						launch.Quit();
						Assert.Equal(0, manager.OpenCount);
					}
				});
			}
		}

		[FixtureFact("BD-reader")]
		public async Task The_open_books_are_remembered_at_quit_and_reopened_in_their_tabs_at_the_next_start_unless_Reopen_Books_is_off()
		{
			using (new RealLibraryGuard())
			using (var temp = new TempDir())
			{
				await Ui.Run(async () =>
				{
					string data = temp.Combine("app");
					string[] files;
					using (var first = await Launch.Start(data, svc => new ReaderManager(svc, svc.Preferences.Current)))
					{
						await first.ViewModel.ScanFolderAsync(MakeLibrary(temp));
						await OpenFirstBook(first, 3);
						first.ViewModel.Browser.SelectedIndex = 1;
						first.ViewModel.Browser.OpenSelectedInNewTab();
						ReaderManager manager = Manager(first);
						await WaitFor(() => manager.OpenCount == 2, "second tab");
						await WaitFor(() => manager.Slots.All(s => s.Model.IsOpen), "both books open");
						files = manager.OpenFiles.ToArray();
						Assert.Equal(2, files.Length);
						Assert.True(first.Quit().Ok);
						Assert.Equal(files, first.Service.Preferences.Current.LastOpenBooks);
					}
					// the next start reopens them in the same order, the last one current, without counting another opening
					using (var second = await Launch.Start(data, svc => new ReaderManager(svc, svc.Preferences.Current)))
					{
						ReaderManager manager = Manager(second);
						Assert.Equal(files, manager.OpenFiles.ToArray());
						Assert.Equal(1, manager.CurrentSlot);
						await WaitFor(() => manager.Slots.All(s => s.Model.IsOpen), "both books open");
						Assert.All(manager.Slots, s => Assert.Equal(1, ((CoreReadingSession)s.Model.Session).Book.OpenedCount));
						second.Service.Preferences.Current.OpenLastFile = false;
						Assert.True(second.Quit().Ok);
					}
					// Reopen Books from last session off: nothing is opened
					using (var third = await Launch.Start(data, svc => new ReaderManager(svc, svc.Preferences.Current)))
					{
						Assert.Equal(0, Manager(third).OpenCount);
						Assert.False(third.ViewModel.HasBook);
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
						ReaderViewModel window = await OpenFirstBook(launch, 3);
						Press(window, Key.PageDown);
						await window.WhenSettled;
						ComicBook book = ((CoreReadingSession)window.Session).Book;

						var request = new ImportRequest(Fixtures.ComicDb, new[] { new PathRemapRule(@"S:\media\BD\", Fixtures.BD + "/") });
						await launch.ViewModel.ImportLibraryAsync(request);
						Assert.Equal(0, Manager(launch).OpenCount);
						Assert.True(window.IsDisposed);
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
