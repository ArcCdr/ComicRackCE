using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ComicRack.Mac.Host;
using ComicRack.Mac.Reader;
using ComicRack.Mac.Tests.Reader;
using ComicRack.Mac.Tests.Thumbnails;
using ComicRack.Mac.Thumbnails;
using ComicRack.Mac.ViewModels;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Engine.Display;
using Xunit;

namespace ComicRack.Mac.Tests.Browser
{
	/// <summary>
	/// P5.6a: the small preview (<c>SmallComicPreview</c> and its host in <c>ComicExplorerView</c>) without a window: what it shows and when (500 ms after the selection settled, a
	/// copy of the first selected book, the same file stays open), its buttons and their enabled states, the settings it keeps, and that previewing writes nothing to the book.
	/// </summary>
	public class SmallPreviewTests : IDisposable
	{
		private readonly long liveBefore;

		public SmallPreviewTests()
		{
			liveBefore = Gc.SettleLive();
			Ui.BeginTest();
		}

		public void Dispose()
		{
			long expected = Ui.LiveBaseline(liveBefore);
			Gc.WaitForLive(expected);
			Assert.Equal(expected, PixelBlock.LiveCount);
		}

		private static ComicBook Book(string name, string file = null)
		{
			return new ComicBook { Title = name, FilePath = file ?? "/books/" + name + ".cbz", PageCount = 10 };
		}

		/// <summary>A preview view model with a timer the test releases, books of generated pages and a record of what it opened.</summary>
		private sealed class Rig
		{
			public Rig(AppPreferences preferences = null, int pages = 10)
			{
				Preferences = preferences ?? new AppPreferences();
				Scheduler = new ManualLiveScheduler();
				Pages = pages;
				Model = new SmallPreviewViewModel(Preferences, Scheduler)
				{
					FirstSelectedBook = () => Selected,
					SidebarAvailable = () => SidebarOpen,
					SessionFactory = book =>
					{
						Copies.Add(book);
						var session = new FakeSession(new FakePageDecoder { PageCount = Pages, Size = p => (300, 450) }) { Title = book.Title };
						Sessions.Add(session);
						return session;
					},
					OpenBook = book => Opened.Add(book),
				};
			}

			public AppPreferences Preferences { get; }

			public ManualLiveScheduler Scheduler { get; }

			public SmallPreviewViewModel Model { get; }

			public int Pages { get; }

			public ComicBook Selected { get; set; }

			public bool SidebarOpen { get; set; } = true;

			public List<ComicBook> Copies { get; } = new List<ComicBook>();

			public List<FakeSession> Sessions { get; } = new List<FakeSession>();

			public List<ComicBook> Opened { get; } = new List<ComicBook>();

			/// <summary>The selection changed and the timer ran out.</summary>
			public void SelectAndWait(ComicBook book)
			{
				Selected = book;
				Model.SelectionChanged();
				Scheduler.RunPending();
				Dispatcher.UIThread.RunJobs();
			}

			public async Task Opened1()
			{
				var deadline = DateTime.UtcNow.AddSeconds(20);
				while (Model.Reader == null || !(Model.Reader.IsOpen || Model.Reader.OpenFailed))
				{
					if (DateTime.UtcNow > deadline)
					{
						throw new TimeoutException("The preview did not open its book.");
					}
					Dispatcher.UIThread.RunJobs();
					await Task.Delay(5);
				}
				await Model.Reader.WhenSettled;
				Dispatcher.UIThread.RunJobs();
			}
		}

		[Fact]
		public async Task The_preview_starts_closed_207_high_with_one_page_and_says_Nothing_Selected()
		{
			await Ui.Run(() =>
			{
				var rig = new Rig();
				Assert.False(rig.Model.IsVisible);
				Assert.Equal(207, rig.Model.Extent);
				Assert.False(rig.Model.TwoPageDisplay);
				Assert.Null(rig.Model.Reader);
				Assert.Equal("Nothing Selected", rig.Model.Text);
				Assert.Equal(500, SmallPreviewViewModel.ShowDelay.TotalMilliseconds);
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task Show_the_height_and_Two_Pages_are_kept_in_the_preferences_and_come_back()
		{
			await Ui.Run(() =>
			{
				var prefs = new AppPreferences();
				var first = new Rig(prefs);
				first.Model.IsVisible = true;
				first.Model.Extent = 300;
				first.Model.TwoPageDisplay = true;
				Assert.True(prefs.ShowPreview);
				Assert.Equal(300, prefs.PreviewSplit);
				Assert.True(prefs.TwoPagePreview);

				var second = new Rig(prefs);
				Assert.True(second.Model.IsVisible);
				Assert.Equal(300, second.Model.Extent);
				Assert.True(second.Model.TwoPageDisplay);

				// the defaults are not written, and a hand edited nonsense value is the default
				var plain = new AppPreferences { PreviewSplit = double.NaN };
				Assert.Equal(207, new Rig(plain).Model.Extent);
				Assert.Equal(AppPreferences.DefaultPreviewSplit, 207);
				Assert.Equal(26, new Rig(new AppPreferences { PreviewSplit = 3 }).Model.Extent); // never smaller than the grip and a sliver
				second.Model.Extent = 2;
				Assert.Equal(SmallPreviewViewModel.MinimumExtent, second.Model.Extent);
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task A_selection_change_starts_a_500_ms_timer_that_the_next_change_replaces_and_nothing_opens_before_it_runs()
		{
			await Ui.Run(async () =>
			{
				var rig = new Rig();
				rig.Model.IsVisible = true;
				ComicBook a = Book("A"), b = Book("B");
				rig.Selected = a;
				rig.Model.SelectionChanged();
				rig.Selected = b;
				rig.Model.SelectionChanged();
				Assert.Equal(new[] { TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(500) }, rig.Scheduler.Delays);
				Assert.Equal(1, rig.Scheduler.Pending); // the first timer was thrown away
				Assert.True(rig.Model.IsPending);
				Assert.Null(rig.Model.Reader);
				Assert.Empty(rig.Sessions);

				rig.Scheduler.RunPending();
				Dispatcher.UIThread.RunJobs();
				Assert.False(rig.Model.IsPending);
				await rig.Opened1();
				Assert.Single(rig.Sessions); // only B was opened
				Assert.Equal("B", rig.Copies[0].Title);
			});
		}

		[Fact]
		public async Task It_shows_a_copy_of_the_first_selected_book_from_its_first_page_and_keeps_the_same_file_open()
		{
			await Ui.Run(async () =>
			{
				var rig = new Rig();
				rig.Model.IsVisible = true;
				ComicBook a = Book("A");
				rig.SelectAndWait(a);
				await rig.Opened1();
				Assert.Single(rig.Copies);
				Assert.NotSame(a, rig.Copies[0]); // new ComicBook(first)
				Assert.Equal(a.FilePath, rig.Copies[0].FilePath);
				Assert.Same(a, rig.Model.Source);
				Assert.Equal(string.Empty, rig.Model.Text);
				Assert.Equal(0, rig.Model.Reader.CurrentPage);
				Assert.Equal(10, rig.Model.Reader.PageCount);
				Assert.NotNull(rig.Model.Reader.Scene);
				ReaderViewModel firstReader = rig.Model.Reader;

				// the same selection again (a refresh of the list) keeps the open book and its page
				rig.Model.NextCommand.Execute(null);
				await rig.Model.Reader.WhenSettled;
				Assert.Equal(1, rig.Model.Reader.CurrentPage);
				rig.SelectAndWait(Book("A2", file: a.FilePath));
				Assert.Same(firstReader, rig.Model.Reader);
				Assert.Equal(1, rig.Model.Reader.CurrentPage);
				Assert.Single(rig.Sessions);

				// another file replaces it, and the old reader is closed
				rig.SelectAndWait(Book("B"));
				await rig.Opened1();
				Assert.NotSame(firstReader, rig.Model.Reader);
				Assert.True(firstReader.IsDisposed);
				Assert.True(rig.Sessions[0].IsDisposed);
				Assert.Equal(2, rig.Sessions.Count);
				Assert.Equal(0, rig.Model.Reader.CurrentPage);
			});
		}

		[Fact]
		public async Task Nothing_selected_closes_the_book_and_a_book_without_a_file_says_it_is_only_for_books()
		{
			await Ui.Run(async () =>
			{
				var rig = new Rig();
				rig.Model.IsVisible = true;
				rig.SelectAndWait(Book("A"));
				await rig.Opened1();
				ReaderViewModel reader = rig.Model.Reader;
				Assert.Equal(string.Empty, rig.Model.Text);

				rig.SelectAndWait(null);
				Assert.Null(rig.Model.Reader);
				Assert.True(reader.IsDisposed);
				Assert.Equal("Nothing Selected", rig.Model.Text);
				Assert.Null(rig.Model.Source);

				ComicBook nofile = Book("Virtual", file: "");
				Assert.False(nofile.IsLinked);
				rig.SelectAndWait(nofile);
				Assert.Null(rig.Model.Reader);
				Assert.Equal("Preview is only available for Books", rig.Model.Text);
				Assert.Single(rig.Sessions); // no session was made for it
			});
		}

		[Fact]
		public async Task A_closed_preview_or_a_closed_side_panel_opens_nothing_and_opening_it_shows_the_selection_at_once()
		{
			await Ui.Run(async () =>
			{
				var rig = new Rig();
				ComicBook a = Book("A");
				rig.SelectAndWait(a); // the preview is off
				Assert.Null(rig.Model.Reader);
				Assert.Empty(rig.Sessions);

				// Browse > Small Preview: no timer, the preview takes the selection
				rig.Model.IsVisible = true;
				Assert.Equal(1, rig.Sessions.Count);
				await rig.Opened1();
				ReaderViewModel reader = rig.Model.Reader;

				// the side panel closes: a new selection is not opened, the open book stays as it is
				rig.SidebarOpen = false;
				rig.SelectAndWait(Book("B"));
				Assert.Same(reader, rig.Model.Reader);
				Assert.Single(rig.Sessions);

				// the side panel opens again: the preview shows what is selected now
				rig.SidebarOpen = true;
				rig.Model.UpdateNow();
				await rig.Opened1();
				Assert.NotSame(reader, rig.Model.Reader);
				Assert.Equal("B", rig.Copies[1].Title);

				// closing the preview (the Close button) keeps the book open and writes the setting
				rig.Model.CloseCommand.Execute(null);
				Assert.False(rig.Model.IsVisible);
				Assert.False(rig.Preferences.ShowPreview);
				Assert.NotNull(rig.Model.Reader);
			});
		}

		[Fact]
		public async Task The_buttons_follow_the_page_and_move_by_a_page_or_by_two_in_the_double_layout()
		{
			await Ui.Run(async () =>
			{
				var rig = new Rig(pages: 8);
				SmallPreviewViewModel m = rig.Model;
				// nothing shown: only Close (and the Two Pages setting stays off) work
				Assert.False(m.OpenCommand.CanExecute(null));
				Assert.False(m.FirstCommand.CanExecute(null));
				Assert.False(m.PreviousCommand.CanExecute(null));
				Assert.False(m.NextCommand.CanExecute(null));
				Assert.False(m.LastCommand.CanExecute(null));
				Assert.False(m.TwoPagesCommand.CanExecute(null));
				Assert.False(m.RefreshCommand.CanExecute(null));
				Assert.True(m.CloseCommand.CanExecute(null));

				m.IsVisible = true;
				rig.SelectAndWait(Book("A"));
				await rig.Opened1();
				Assert.True(m.OpenCommand.CanExecute(null));
				Assert.False(m.FirstCommand.CanExecute(null)); // CanNavigate(-1)
				Assert.False(m.PreviousCommand.CanExecute(null));
				Assert.True(m.NextCommand.CanExecute(null));
				Assert.True(m.LastCommand.CanExecute(null));
				Assert.True(m.TwoPagesCommand.CanExecute(null));
				Assert.True(m.RefreshCommand.CanExecute(null));

				m.NextCommand.Execute(null);
				await m.Reader.WhenSettled;
				Assert.Equal(1, m.Reader.CurrentPage);
				Assert.True(m.FirstCommand.CanExecute(null));
				Assert.True(m.PreviousCommand.CanExecute(null));
				m.LastCommand.Execute(null);
				await m.Reader.WhenSettled;
				Assert.Equal(7, m.Reader.CurrentPage);
				Assert.False(m.NextCommand.CanExecute(null));
				Assert.False(m.LastCommand.CanExecute(null));
				m.FirstCommand.Execute(null);
				await m.Reader.WhenSettled;
				Assert.Equal(0, m.Reader.CurrentPage);

				// Two Pages: an adaptive double page (the front cover stays alone); Next and Previous move by two
				m.TwoPagesCommand.Execute(null);
				await m.Reader.WhenSettled;
				Assert.True(m.TwoPageDisplay);
				Assert.True(rig.Preferences.TwoPagePreview);
				Assert.Equal(PageLayoutMode.DoubleAdaptive, m.Reader.LayoutMode);
				Assert.Single(m.Reader.Scene.Pages);
				m.NextCommand.Execute(null);
				await m.Reader.WhenSettled;
				Assert.Equal(2, m.Reader.Scene.Pages.Count);
				int before = m.Reader.CurrentPage;
				Assert.Equal(1, before);
				m.NextCommand.Execute(null);
				await m.Reader.WhenSettled;
				Assert.Equal(before + 2, m.Reader.CurrentPage);
				m.PreviousCommand.Execute(null);
				await m.Reader.WhenSettled;
				Assert.Equal(before, m.Reader.CurrentPage);
				m.TwoPagesCommand.Execute(null);
				await m.Reader.WhenSettled;
				Assert.False(m.TwoPageDisplay);
				Assert.Equal(PageLayoutMode.Single, m.Reader.LayoutMode);
				Assert.Single(m.Reader.Scene.Pages);

				// Refresh shows the page again; Open asks for the library's book, not the copy
				m.RefreshCommand.Execute(null);
				await m.Reader.WhenSettled;
				Assert.Equal(before, m.Reader.CurrentPage);
				m.OpenCommand.Execute(null);
				Assert.Single(rig.Opened);
				Assert.Same(m.Source, rig.Opened[0]);
				Assert.NotSame(rig.Copies[0], rig.Opened[0]);
			});
		}

		[Fact]
		public async Task A_new_preview_starts_with_the_saved_two_page_setting_and_a_book_that_cannot_be_opened_says_why()
		{
			await Ui.Run(async () =>
			{
				var rig = new Rig(new AppPreferences { TwoPagePreview = true, ShowPreview = true });
				rig.SelectAndWait(Book("A"));
				await rig.Opened1();
				Assert.Equal(PageLayoutMode.DoubleAdaptive, rig.Model.Reader.LayoutMode);
				Assert.Equal(ImageFitMode.Fit, rig.Model.Reader.Fit);
				Assert.False(rig.Model.Reader.RightToLeft);

				var broken = new Rig(new AppPreferences { ShowPreview = true });
				var bad = new FakeSession { OpenFailure = new InvalidDataException("The book could not be opened: x.cbz") };
				broken.Model.SessionFactory = book => bad;
				broken.SelectAndWait(Book("X"));
				await broken.Opened1();
				Assert.True(broken.Model.Reader.OpenFailed);
				for (int i = 0; i < 400 && string.IsNullOrEmpty(broken.Model.Text); i++)
				{
					Dispatcher.UIThread.RunJobs();
					await Task.Delay(5);
				}
				Assert.Equal("The book could not be opened: x.cbz", broken.Model.Text);
				Assert.False(broken.Model.NextCommand.CanExecute(null));
			});
		}

		[Fact]
		public async Task Close_releases_the_book_the_pages_and_the_timer()
		{
			await Ui.Run(async () =>
			{
				var rig = new Rig();
				rig.Model.IsVisible = true;
				rig.SelectAndWait(Book("A"));
				await rig.Opened1();
				Assert.True(PixelBlock.LiveCount > Ui.LiveBaseline(liveBefore));
				rig.Model.SelectionChanged();
				Assert.Equal(1, rig.Scheduler.Pending);
				rig.Model.Close();
				Assert.Equal(0, rig.Scheduler.Pending);
				Assert.Null(rig.Model.Reader);
				Assert.True(rig.Sessions[0].IsDisposed);
				Assert.False(rig.Model.OpenCommand.CanExecute(null));
				rig.Model.Dispose();
				rig.Model.SelectionChanged(); // after the dispose nothing is scheduled
				Assert.Equal(0, rig.Scheduler.Pending);
			});
		}

		[Fact]
		public async Task The_session_of_a_preview_starts_at_page_one_and_writes_nothing_to_the_book()
		{
			await Ui.Run(async () =>
			{
				using (var temp = new TempDir())
				using (var core = new CoreFixture())
				{
					string file = MakeCbz(temp.Combine("preview.cbz"), 6);
					ComicBook book = core.Book(file);
					book.CurrentPage = 3;
					book.LastPageRead = 3;
					int count = book.OpenedCount;
					DateTime opened = book.OpenedTime;
					int newPages = book.NewPages;
					using (var session = new CoreReadingSession(book, core.Host.ImagePool) { IsPreview = true })
					{
						await session.OpenAsync(CancellationToken.None);
						Assert.Equal(6, session.PageCount);
						Assert.Equal(0, session.ResumePage); // OpenComicOptions.NoMoveToLastPage
						session.ReportPage(4);
						session.ReportFurthest(5);
						session.ReportEndReached();
					}
					Assert.Equal(3, book.CurrentPage);
					Assert.Equal(3, book.LastPageRead);
					Assert.Equal(count, book.OpenedCount);
					Assert.Equal(opened, book.OpenedTime);
					Assert.Equal(newPages, book.NewPages);

					// the reading session of a tab, for contrast, resumes where the reader was left and counts the opening
					using (var session = new CoreReadingSession(book, core.Host.ImagePool))
					{
						await session.OpenAsync(CancellationToken.None);
						Assert.Equal(3, session.ResumePage);
					}
					Assert.Equal(count + 1, book.OpenedCount);
				}
			});
		}

		private static string MakeCbz(string path, int pages)
		{
			using (FileStream stream = File.Create(path))
			using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
			{
				for (int i = 0; i < pages; i++)
				{
					ZipArchiveEntry entry = zip.CreateEntry((i + 1).ToString("000") + ".jpg", CompressionLevel.NoCompression);
					using (Stream s = entry.Open())
					{
						byte[] jpeg = PageImages.Jpeg(300, 400);
						s.Write(jpeg, 0, jpeg.Length);
					}
				}
			}
			return path;
		}
	}

	/// <summary>The preview in the main view model: the selection of the browser feeds it, the library and the readers take it down, the real session is the Core's.</summary>
	public class SmallPreviewWiringTests : IDisposable
	{
		private readonly long liveBefore;

		public SmallPreviewWiringTests()
		{
			liveBefore = Gc.SettleLive();
			Ui.BeginTest();
		}

		public void Dispose()
		{
			long expected = Ui.LiveBaseline(liveBefore);
			Gc.WaitForLive(expected);
			Assert.Equal(expected, PixelBlock.LiveCount);
		}

		private static async Task WaitFor(Func<bool> condition, string what, int timeoutMs = 30000)
		{
			var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
			while (!condition())
			{
				if (DateTime.UtcNow > deadline)
				{
					throw new TimeoutException("Not reached: " + what);
				}
				Dispatcher.UIThread.RunJobs();
				await Task.Delay(5);
			}
		}

		[Fact]
		public async Task The_browser_selection_feeds_the_preview_through_the_real_session_and_previewing_does_not_touch_the_library_book()
		{
			await Ui.Run(async () =>
			{
				using (new RealLibraryGuard())
				using (var temp = new TempDir())
				using (Launch launch = await Launch.Start(temp.Combine("app")))
				{
					MainViewModel vm = launch.ViewModel;
					string books = temp.Combine("books");
					Synthetic.CreateCbzFolder(books, 6);
					launch.Dialogs.FolderToPick = books;
					await vm.OpenFolderCommand.ExecuteAsync(null);
					await vm.WhenLibraryShownAsync();
					await WaitFor(() => vm.Browser.Items.Length == 6 && !vm.Browser.IsLoading, "the grid shows the six books");

					var scheduler = new ManualLiveScheduler();
					vm.SmallPreview.Scheduler = scheduler;
					Assert.False(vm.SmallPreview.IsVisible);
					Assert.False(vm.IsSmallPreviewVisible);

					// Browse > Small Preview (the command of the menu item) turns it on and saves the setting
					var changed = new List<string>();
					vm.PropertyChanged += (sender, e) => changed.Add(e.PropertyName);
					vm.ToggleSmallPreviewCommand.Execute(null);
					Assert.True(vm.SmallPreview.IsVisible);
					Assert.Contains(nameof(MainViewModel.IsSmallPreviewVisible), changed);
					Assert.True(launch.Service.Preferences.Current.ShowPreview);

					// a selection starts the 500 ms timer; when it runs the first selected book is shown
					ComicBook book = vm.Browser.Items[2].Book;
					vm.Browser.Selection.SelectBooks(new[] { book });
					Assert.Equal(1, scheduler.Pending);
					Assert.Null(vm.SmallPreview.Reader);
					int openedCount = book.OpenedCount;
					int current = book.CurrentPage;
					scheduler.RunPending();
					Dispatcher.UIThread.RunJobs();
					await WaitFor(() => vm.SmallPreview.Reader != null && vm.SmallPreview.Reader.IsOpen, "the preview opened the book");
					await vm.SmallPreview.Reader.WhenSettled;
					Assert.Same(book, vm.SmallPreview.Source);
					Assert.Equal(3, vm.SmallPreview.Reader.PageCount);
					Assert.Equal(0, vm.SmallPreview.Reader.CurrentPage);
					Assert.Equal(string.Empty, vm.SmallPreview.Text);
					vm.SmallPreview.NextCommand.Execute(null);
					await vm.SmallPreview.Reader.WhenSettled;
					Assert.Equal(1, vm.SmallPreview.Reader.CurrentPage);
					Assert.Equal(openedCount, book.OpenedCount);
					Assert.Equal(current, book.CurrentPage);

					// the side panel closes: a new selection waits; opening the side panel shows it
					vm.Sidebar.IsVisible = false;
					vm.Browser.Selection.SelectBooks(new[] { vm.Browser.Items[4].Book });
					scheduler.RunPending();
					Dispatcher.UIThread.RunJobs();
					Assert.Same(book, vm.SmallPreview.Source);
					vm.Sidebar.IsVisible = true;
					await WaitFor(() => vm.SmallPreview.Reader != null && vm.SmallPreview.Reader.IsOpen && !ReferenceEquals(vm.SmallPreview.Source, book), "the preview follows the side panel");

					// Open puts the book into a new tab (none here: the launch has no reader manager) and the readers going down take the preview with them
					ReaderViewModel shown = vm.SmallPreview.Reader;
					vm.CloseReaders();
					Assert.Null(vm.SmallPreview.Reader);
					Assert.True(shown.IsDisposed);
				}
			});
		}
	}
}
