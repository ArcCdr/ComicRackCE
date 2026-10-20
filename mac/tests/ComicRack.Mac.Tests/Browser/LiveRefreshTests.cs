using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using ComicRack.Mac.Browser;
using ComicRack.Mac.Controls;
using ComicRack.Mac.Host;
using ComicRack.Mac.Host.Library;
using ComicRack.Mac.Services;
using ComicRack.Mac.Tests.Reader;
using ComicRack.Mac.ViewModels;
using ComicRack.Mac.Views;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Engine.Database;
using Xunit;
using Xunit.Abstractions;

namespace ComicRack.Mac.Tests.Browser
{
	/// <summary>Holds the live refresh's timer until the test releases it, so that no test depends on a clock.</summary>
	public sealed class ManualLiveScheduler : ISaveScheduler
	{
		private readonly List<(Action action, bool[] done)> actions = new List<(Action, bool[])>();

		public List<TimeSpan> Delays { get; } = new List<TimeSpan>();

		public int Pending => actions.Count(a => !a.done[0]);

		public IDisposable Schedule(TimeSpan delay, Action action)
		{
			Delays.Add(delay);
			var done = new bool[1];
			actions.Add((action, done));
			return new Handle(done);
		}

		/// <summary>Runs the actions that wait (the timer fires).</summary>
		public void RunPending()
		{
			foreach ((Action action, bool[] done) in actions.ToArray())
			{
				if (!done[0])
				{
					done[0] = true;
					action();
				}
			}
		}

		private sealed class Handle : IDisposable
		{
			private readonly bool[] done;

			public Handle(bool[] done) => this.done = done;

			public void Dispose() => done[0] = true;
		}
	}

	/// <summary>P5.2: the library window follows the library while it changes: covers, counters, the shown list, the state icons, with selection and scroll kept.</summary>
	public class LiveRefreshTests
	{
		private readonly ITestOutputHelper output;

		public LiveRefreshTests(ITestOutputHelper output)
		{
			this.output = output;
		}

		private static ComicBook Book(string series, int number, int pages = 10, int lastPageRead = 0, double addedDaysAgo = 100)
		{
			return new ComicBook
			{
				Series = series,
				Number = number.ToString(),
				Title = series + " " + number,
				FilePath = "/nowhere/" + series + number + ".cbz",
				PageCount = pages,
				LastPageRead = lastPageRead,
				AddedTime = DateTime.Now.AddDays(-addedDaysAgo),
			};
		}

		private static async Task WaitFor(Func<bool> condition, string what, int timeoutMs = 20000)
		{
			var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
			while (!condition())
			{
				if (DateTime.UtcNow > deadline)
				{
					throw new TimeoutException("Not reached: " + what);
				}
				await Task.Delay(5);
			}
		}

		/// <summary>The timer fires: the changes are delivered to the window and the refresh they start is done.</summary>
		private static async Task Fire(ManualLiveScheduler scheduler, MainViewModel vm)
		{
			scheduler.RunPending();
			Avalonia.Threading.Dispatcher.UIThread.RunJobs(); // the delivery is posted to the UI thread
			await vm.WhenLiveRefreshedAsync();
			Avalonia.Threading.Dispatcher.UIThread.RunJobs(); // the numbers of the rows are posted to the UI thread
		}

		private static SidebarNode Node(MainViewModel vm, string name)
		{
			SidebarNode[] found = vm.Sidebar.AllNodes().Where(n => n.Name == name).ToArray();
			Assert.True(found.Length == 1, name + " is in the tree " + found.Length + " times: " + string.Join(" | ", vm.Sidebar.AllNodes().Select(n => n.Name)));
			return found[0];
		}

		// ---- the grid keeps the user's place

		[Fact]
		public async Task A_refresh_with_new_books_keeps_the_selected_book_and_the_scroll_position_and_a_refresh_without_changes_leaves_the_list_alone()
		{
			await Ui.Run(async () =>
			{
				var books = Enumerable.Range(0, 3000).Select(i => Book("s" + i.ToString("D4"), 1)).ToList();
				using (var h = new GridHarness())
				{
					await h.Show(BookCatalog.FromBooks(books));
					h.Model.SelectedIndex = 1500;
					h.ScrollToRow(1500 / h.Grid.Columns - 2);
					double offset = h.Grid.Scroller.Offset.Y;
					ComicBook selected = h.Model.SelectedItem.Book;
					Assert.True(offset > 1000, "scrolled " + offset);

					// nothing changed: the list is not replaced
					BookItem[] before = h.Model.Items;
					await h.Model.RefreshAsync(ct => BookCatalog.FromBooks(books));
					h.Layout();
					Assert.Same(before, h.Model.Items);

					// 30 books arrive, some of them before the selected one, and the user keeps their place
					var arrived = Enumerable.Range(0, 30).Select(i => Book("s" + (i * 90).ToString("D4") + "b", 1)).ToList();
					books.AddRange(arrived);
					await h.Model.RefreshAsync(ct => BookCatalog.FromBooks(books));
					h.Layout();
					Assert.Equal(3030, h.Model.Items.Length);
					Assert.Same(selected, h.Model.SelectedItem.Book);
					Assert.True(h.Model.SelectedIndex > 1500, "the selected book moved down with the new books before it");
					Assert.Equal(offset, h.Grid.Scroller.Offset.Y, 1);
					BookCell cell = h.Grid.CellAt(h.Model.SelectedIndex);
					if (cell != null)
					{
						Assert.True(cell.IsSelectedForTest);
					}
					Assert.True(h.Grid.BoundCells().Count(c => c.IsSelectedForTest) <= 1);

					// the selected book is removed: no selection, same place
					books.Remove(selected);
					await h.Model.RefreshAsync(ct => BookCatalog.FromBooks(books));
					h.Layout();
					Assert.Equal(-1, h.Model.SelectedIndex);
					Assert.Equal(offset, h.Grid.Scroller.Offset.Y, 1);
				}
			});
		}

		[Fact]
		public async Task A_refresh_for_a_list_the_user_left_is_dropped()
		{
			await Ui.Run(async () =>
			{
				var books = Enumerable.Range(0, 20).Select(i => Book("s" + i.ToString("D2"), 1)).ToList();
				using (var h = new GridHarness())
				{
					await h.Show(BookCatalog.FromBooks(books));
					var gate = new System.Threading.ManualResetEventSlim(false);
					Task refresh = h.Model.RefreshAsync(ct =>
					{
						gate.Wait(TimeSpan.FromSeconds(20));
						return BookCatalog.FromBooks(books.Take(5));
					});
					await h.Model.LoadAsync(ct => BookCatalog.FromBooks(books.Take(10)));
					gate.Set();
					await refresh;
					h.Layout();
					Assert.Equal(10, h.Model.Items.Length); // not the 5 of the older refresh
				}
			});
		}

		[Fact]
		public async Task A_book_that_changed_is_drawn_again_with_its_new_state()
		{
			await Ui.Run(async () =>
			{
				var books = Enumerable.Range(0, 12).Select(i => Book("s" + i.ToString("D2"), 1)).ToList();
				using (var h = new GridHarness())
				{
					await h.Show(BookCatalog.FromBooks(books));
					BookCell cell = h.Grid.BoundCells().Single(c => c.Item.Book == books[3]);
					Assert.Equal(0, cell.Badges.LastPageRead);
					books[3].LastPageRead = 6;
					Assert.Equal(0, cell.Badges.LastPageRead); // nothing is drawn again by itself
					h.Model.NotifyBooksChanged(new[] { books[3] });
					Assert.Equal(6, cell.Badges.LastPageRead);
					Assert.Equal(0, h.Grid.BoundCells().Single(c => c.Item.Book == books[4]).Badges.LastPageRead);
				}
			});
		}

		// ---- markers

		[Fact]
		public async Task The_open_and_the_last_opened_book_get_their_state_icons_as_in_the_original()
		{
			await Ui.Run(async () =>
			{
				var books = Enumerable.Range(0, 6).Select(i => Book("s" + i, 1)).ToList();
				books[2].OpenedTime = new DateTime(2026, 1, 1);
				books[4].OpenedTime = new DateTime(2026, 6, 1);
				using (var h = new GridHarness())
				{
					var open = new HashSet<ComicBook>();
					h.Model.IsBookOpen = open.Contains;
					await h.Show(BookCatalog.FromBooks(books));
					Assert.Same(books[4], h.Model.LastOpenedBook);
					Func<ComicBook, ThumbnailMarker> marker = h.Model.ThumbnailOptions.MarkerOf;
					Assert.Equal(ThumbnailMarker.IsLast, marker(books[4]));
					Assert.Equal(ThumbnailMarker.None, marker(books[2]));
					Assert.Equal(ThumbnailMarker.None, marker(books[0]));
					BookCell cell4 = h.Grid.BoundCells().Single(c => c.Item.Book == books[4]);
					Assert.Contains("LastMarker", cell4.Badges.StateIcons);

					// open in a reader wins over last opened
					open.Add(books[4]);
					open.Add(books[0]);
					h.Model.NotifyMarkersChanged();
					Assert.Equal(ThumbnailMarker.IsOpen, marker(books[4]));
					Assert.Contains("OpenMarker", cell4.Badges.StateIcons);
					Assert.DoesNotContain("LastMarker", cell4.Badges.StateIcons);
					Assert.Contains("OpenMarker", h.Grid.BoundCells().Single(c => c.Item.Book == books[0]).Badges.StateIcons);

					// a book opened later takes the last-opened marker once its change is reported
					open.Clear();
					books[1].OpenedTime = new DateTime(2026, 9, 1);
					h.Model.NotifyBooksChanged(new[] { books[1] });
					Assert.Same(books[1], h.Model.LastOpenedBook);
					h.Model.NotifyMarkersChanged();
					Assert.Contains("LastMarker", h.Grid.BoundCells().Single(c => c.Item.Book == books[1]).Badges.StateIcons);
					Assert.DoesNotContain("LastMarker", cell4.Badges.StateIcons);
				}
			});
		}

		// ---- the whole chain on a real library

		[Fact]
		public async Task Reading_a_book_updates_the_counters_the_lists_and_the_cover_without_a_reload()
		{
			using (var temp = new TempDir())
			{
				await Ui.Run(async () =>
				{
					using (Launch launch = await Launch.Start(temp.Combine("app")))
					{
						MainViewModel vm = launch.ViewModel;
						ComicDatabase db = launch.Service.Host.DatabaseManager.Database;
						var scheduler = new ManualLiveScheduler();
						foreach (ComicBook b in Enumerable.Range(1, 4).Select(i => Book("Alpha", i)).Append(Book("Beta", 1, addedDaysAgo: 2)))
						{
							db.Books.Add(b);
						}
						await vm.Sidebar.ReloadAsync(launch.Service.Host);
						await vm.Sidebar.CountsReady;
						await vm.WhenLibraryShownAsync();
						Avalonia.Threading.Dispatcher.UIThread.RunJobs();
						vm.EnableLiveRefresh(scheduler);
						// the books added above came before the watcher: load the grid and the counts once
						vm.RequestLiveRefresh();
						await vm.WhenLiveRefreshedAsync();
						await WaitFor(() => vm.Browser.Items.Length == 5, "the grid shows the 5 books");

						SidebarNode library = Node(vm, "Library"), reading = Node(vm, "Reading");
						Assert.Equal((5, 4, 1), (library.Count, library.UnreadCount, library.NewCount));
						Assert.Equal(0, reading.Count);

						// a reader reads Alpha 1 up to page 4
						ComicBook book = db.Books.Single(b => b.Series == "Alpha" && b.Number == "1");
						BookItem[] shown = vm.Browser.Items;
						var changed = new List<ComicBook>();
						vm.Browser.BooksChanged += (sender, e) => changed.AddRange(e);
						book.CurrentPage = 3;
						Assert.Equal(1, scheduler.Pending);
						Assert.Equal(LibraryWatcher.DefaultReadingDelay, scheduler.Delays.Last()); // reading alone is not counted at every page
						await Fire(scheduler, vm);
						output.WriteLine($"Library {library.Count}/{library.UnreadCount}/{library.NewCount}, Reading {reading.Count}");
						Assert.Contains(book, changed);
						Assert.Equal(1, reading.Count); // "Reading" +1
						Assert.Equal((5, 4, 1), (library.Count, library.UnreadCount, library.NewCount)); // a book that was started is not read yet
						Assert.Same(shown, vm.Browser.Items); // the books are the same: the list was not replaced

						// and to the end: it is read now
						book.LastPageRead = 9;
						await Fire(scheduler, vm);
						Assert.Equal((5, 3, 1), (library.Count, library.UnreadCount, library.NewCount));
						Assert.Equal(0, reading.Count);
					}
				});
			}
		}

		[Fact]
		public async Task Books_that_arrive_show_in_the_grid_and_the_counters_and_a_list_that_gains_a_book_is_refilled()
		{
			using (var temp = new TempDir())
			{
				await Ui.Run(async () =>
				{
					using (Launch launch = await Launch.Start(temp.Combine("app")))
					{
						MainViewModel vm = launch.ViewModel;
						ComicDatabase db = launch.Service.Host.DatabaseManager.Database;
						var scheduler = new ManualLiveScheduler();
						vm.EnableLiveRefresh(scheduler);
						await vm.Sidebar.ReloadAsync(launch.Service.Host);
						await vm.WhenLibraryShownAsync();

						for (int i = 1; i <= 3; i++)
						{
							db.Books.Add(Book("Alpha", i));
						}
						Assert.Equal(1, scheduler.Pending); // one timer for all the events
						Assert.Equal(LibraryWatcher.DefaultDelay, scheduler.Delays.Last());
						await Fire(scheduler, vm);
						await WaitFor(() => vm.Browser.Items.Length == 3, "3 books in the grid");
						Assert.Equal(3, Node(vm, "Library").Count);
						Assert.Equal(3, Node(vm, "Library").UnreadCount);
						Assert.Contains("3", vm.BookCountText);

						// select the second book, scan in more
						vm.Browser.SelectedIndex = 1;
						ComicBook selected = vm.Browser.SelectedItem.Book;
						for (int i = 4; i <= 9; i++)
						{
							db.Books.Add(Book("Alpha", i));
						}
						await Fire(scheduler, vm);
						await WaitFor(() => vm.Browser.Items.Length == 9, "9 books in the grid");
						Assert.Same(selected, vm.Browser.SelectedItem.Book);
						Assert.Equal(9, Node(vm, "Library").Count);

						// the selected list gains a book that is being read: it is refilled
						SidebarNode readingNode = Node(vm, "Reading");
						vm.Sidebar.Select(readingNode);
						await WaitFor(() => !vm.Browser.IsLoading, "the Reading list is shown");
						Assert.Empty(vm.Browser.Items);
						ComicBook started = db.Books.First();
						started.CurrentPage = 2;
						started.LastPageRead = 2;
						await Fire(scheduler, vm);
						await WaitFor(() => vm.Browser.Items.Length == 1, "the started book is in the Reading list");
						Assert.Same(started, vm.Browser.Items[0].Book);
						Assert.Equal(1, readingNode.Count);
						Assert.Null(vm.Sidebar.Notice); // "This list has no books." is gone
					}
				});
			}
		}

		[Fact]
		public async Task Opening_and_closing_a_reader_moves_the_state_icons_and_a_closed_reader_counts_the_lists_at_once()
		{
			using (var temp = new TempDir())
			{
				await Ui.Run(async () =>
				{
					var launcher = new FakeLauncher();
					using (Launch launch = await Launch.Start(temp.Combine("app"), service => launcher))
					{
						MainViewModel vm = launch.ViewModel;
						ComicDatabase db = launch.Service.Host.DatabaseManager.Database;
						var scheduler = new ManualLiveScheduler();
						vm.EnableLiveRefresh(scheduler);
						ComicBook a = Book("Alpha", 1), b = Book("Alpha", 2);
						db.Books.Add(a);
						db.Books.Add(b);
						await Fire(scheduler, vm);
						await WaitFor(() => vm.Browser.Items.Length == 2, "2 books in the grid");

						int redraws = 0;
						vm.Browser.ThumbnailOptions.Changed += (sender, e) => redraws++;
						launcher.Open(a);
						Assert.Equal(1, redraws);
						Assert.Equal(ThumbnailMarker.IsOpen, vm.Browser.ThumbnailOptions.MarkerOf(a));
						Assert.Equal(ThumbnailMarker.None, vm.Browser.ThumbnailOptions.MarkerOf(b));

						// the reader reads three pages (counted later), then it is closed
						a.OpenedTime = DateTime.Now;
						a.CurrentPage = 3;
						Assert.Equal(1, scheduler.Pending);
						launcher.Close(a);
						Assert.Equal(2, redraws);
						Assert.Equal(0, scheduler.Pending); // closing flushed what was waiting
						await WaitFor(() => vm.Browser.LastOpenedBook == a, "the book that was read last has the marker");
						await vm.WhenLiveRefreshedAsync();
						Assert.Equal(ThumbnailMarker.IsLast, vm.Browser.ThumbnailOptions.MarkerOf(a));
						Assert.Equal(ThumbnailMarker.None, vm.Browser.ThumbnailOptions.MarkerOf(b));
					}
				});
			}
		}
	}
}
