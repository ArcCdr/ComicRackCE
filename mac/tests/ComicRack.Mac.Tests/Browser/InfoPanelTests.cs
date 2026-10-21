using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using ComicRack.Mac.Host;
using ComicRack.Mac.Info;
using ComicRack.Mac.ViewModels;
using cYo.Common.Net.Search;
using cYo.Projects.ComicRack.Engine;
using Xunit;

namespace ComicRack.Mac.Tests.Browser
{
	/// <summary>A page that records what it was given, as a script's page would.</summary>
	public sealed class FakeInfoPage : IInfoPage
	{
		private string title;

		public FakeInfoPage(string title) => this.title = title;

		public string Title
		{
			get => title;
			set
			{
				title = value;
				TitleChanged?.Invoke(this, EventArgs.Empty);
			}
		}

		public string IconName => null;

		public Control View { get; } = new Border();

		public List<IReadOnlyList<ComicBook>> Calls { get; } = new List<IReadOnlyList<ComicBook>>();

		public event EventHandler TitleChanged;

		public void ShowInfo(IReadOnlyList<ComicBook> books) => Calls.Add(books);
	}

	/// <summary>
	/// P5.6b: the info panel container (<c>pluginContainer</c> and <c>ComicPageContainerControl</c>) without a window: pages and tabs, when the menu item is available, what the
	/// pages are given and when, and the settings it keeps. The original has no built-in page: every page here is a fake.
	/// </summary>
	public class InfoPanelTests : IDisposable
	{
				public void Dispose()
		{
		}

		private sealed class Rig
		{
			public Rig(AppPreferences preferences = null)
			{
				Preferences = preferences ?? new AppPreferences();
				Scheduler = new ManualLiveScheduler();
				Model = new InfoPanelViewModel(Preferences, Scheduler) { SelectedBooks = () => Selected };
			}

			public AppPreferences Preferences { get; }

			public ManualLiveScheduler Scheduler { get; }

			public InfoPanelViewModel Model { get; }

			public IReadOnlyList<ComicBook> Selected { get; set; } = new ComicBook[0];

			public void SelectAndWait(params ComicBook[] books)
			{
				Selected = books;
				Model.SelectionChanged();
				Scheduler.RunPending();
				Dispatcher.UIThread.RunJobs();
			}
		}

		private static ComicBook Book(string name) => new ComicBook { Title = name, FilePath = "/books/" + name + ".cbz" };

		[Fact]
		public async Task The_panel_starts_hidden_162_high_without_a_page_and_the_menu_item_is_not_available()
		{
			await Ui.Run(() =>
			{
				var rig = new Rig();
				Assert.False(rig.Model.IsVisible);
				Assert.Equal(162, rig.Model.Extent);
				Assert.False(rig.Model.HasPages); // ISidebar.HasInfoPanels
				Assert.False(rig.Model.ContainerVisible);
				Assert.False(rig.Model.TabsVisible);
				Assert.Null(rig.Model.SelectedPage);
				Assert.Equal(-1, rig.Model.SelectedIndex);
				Assert.False(rig.Model.ToggleCommand.CanExecute(null)); // CheckInfoPanelAvailable
				Assert.Equal(500, InfoPanelViewModel.ShowDelay.TotalMilliseconds);
				Assert.False(new AppPreferences().ShowInfo);
				Assert.Equal(162, new AppPreferences().InfoBrowserSize);
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task The_first_page_makes_the_container_and_the_menu_item_available_and_the_tab_strip_shows_with_two_pages()
		{
			await Ui.Run(() =>
			{
				var rig = new Rig();
				int changes = 0;
				rig.Model.ToggleCommand.CanExecuteChanged += (s, e) => changes++;
				var one = new FakeInfoPage("One");
				rig.Model.AddPage(one);
				Assert.True(rig.Model.HasPages);
				Assert.True(rig.Model.ContainerVisible);
				Assert.False(rig.Model.TabsVisible); // tabBar.Visible = Items.Count > 1
				Assert.Equal(0, rig.Model.SelectedIndex); // the first tab is selected
				Assert.True(rig.Model.ToggleCommand.CanExecute(null));
				Assert.True(changes > 0);
				var two = new FakeInfoPage("Two");
				rig.Model.AddPage(two);
				Assert.True(rig.Model.TabsVisible);
				Assert.Equal(0, rig.Model.SelectedIndex); // later pages do not take the selection
				Assert.Same(one, rig.Model.SelectedPage.Page);
				Assert.Equal(new[] { "One", "Two" }, rig.Model.Pages.Select(p => p.Title).ToArray());
				two.Title = "Second"; // c.TextChanged
				Assert.Equal("Second", rig.Model.Pages[1].Title);
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task Removing_pages_moves_the_selection_and_the_last_one_makes_the_item_unavailable_again()
		{
			await Ui.Run(() =>
			{
				var rig = new Rig();
				var a = new FakeInfoPage("A");
				var b = new FakeInfoPage("B");
				var c = new FakeInfoPage("C");
				rig.Model.AddPage(a);
				rig.Model.AddPage(b);
				rig.Model.AddPage(c);
				rig.Model.SelectedIndex = 2;
				Assert.True(rig.Model.RemovePage(a));
				Assert.Equal(1, rig.Model.SelectedIndex); // still C
				Assert.Same(c, rig.Model.SelectedPage.Page);
				Assert.True(rig.Model.RemovePage(c));
				Assert.Same(b, rig.Model.SelectedPage.Page);
				Assert.False(rig.Model.TabsVisible);
				Assert.False(rig.Model.RemovePage(c)); // not there
				Assert.True(rig.Model.RemovePage(b));
				Assert.False(rig.Model.HasPages);
				Assert.False(rig.Model.ContainerVisible);
				Assert.False(rig.Model.ToggleCommand.CanExecute(null));
				Assert.Equal(-1, rig.Model.SelectedIndex);
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task Show_and_the_height_are_kept_in_the_preferences_and_come_back_and_a_height_cannot_get_below_the_grip()
		{
			await Ui.Run(() =>
			{
				var prefs = new AppPreferences();
				var first = new Rig(prefs);
				first.Model.AddPage(new FakeInfoPage("A"));
				first.Model.ToggleCommand.Execute(null);
				first.Model.Extent = 300;
				Assert.True(prefs.ShowInfo);
				Assert.Equal(300, prefs.InfoBrowserSize);
				var again = new Rig(prefs);
				Assert.True(again.Model.IsVisible);
				Assert.Equal(300, again.Model.Extent);
				again.Model.Extent = 3;
				Assert.Equal(InfoPanelViewModel.MinimumExtent, again.Model.Extent);
				again.Model.Extent = double.NaN;
				Assert.Equal(162, again.Model.Extent);
				again.Model.AddPage(new FakeInfoPage("A"));
				again.Model.ToggleCommand.Execute(null);
				Assert.False(prefs.ShowInfo);
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task The_page_that_shows_gets_the_selected_books_500_ms_after_the_selection_settled_and_the_other_pages_when_they_show()
		{
			await Ui.Run(() =>
			{
				var rig = new Rig();
				var a = new FakeInfoPage("A");
				var b = new FakeInfoPage("B");
				rig.Model.AddPage(a);
				rig.Model.AddPage(b);
				rig.Model.IsVisible = true;
				a.Calls.Clear();
				ComicBook x = Book("x"), y = Book("y");
				rig.Selected = new[] { x, y };
				rig.Model.SelectionChanged();
				Assert.True(rig.Model.IsPending);
				Assert.Equal(InfoPanelViewModel.ShowDelay, rig.Scheduler.Delays.Last());
				Assert.Empty(a.Calls); // not before the timer
				rig.Scheduler.RunPending();
				Dispatcher.UIThread.RunJobs();
				Assert.Single(a.Calls);
				Assert.Equal(new[] { x, y }, a.Calls[0]);
				Assert.Empty(b.Calls); // B does not show: it waits
				Assert.True(rig.Model.Pages[1].HasPendingUpdate);
				rig.Model.SelectedIndex = 1; // its tab is selected: it gets the books it waited for
				Assert.Single(b.Calls);
				Assert.Equal(new[] { x, y }, b.Calls[0]);
				Assert.False(rig.Model.Pages[1].HasPendingUpdate);
				rig.SelectAndWait(y);
				Assert.Equal(2, b.Calls.Count);
				Assert.Single(a.Calls); // A is hidden now
				rig.Model.SelectedIndex = 0;
				Assert.Equal(2, a.Calls.Count);
				Assert.Equal(new[] { y }, a.Calls[1]);
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task A_page_marked_as_dirty_shows_an_empty_list_when_it_shows_and_a_closed_panel_gives_nothing_to_the_pages()
		{
			await Ui.Run(() =>
			{
				var rig = new Rig();
				var a = new FakeInfoPage("A");
				var b = new FakeInfoPage("B");
				rig.Model.AddPage(a);
				rig.Model.AddPage(b);
				rig.SelectAndWait(Book("x")); // closed: nothing
				Assert.Empty(a.Calls);
				Assert.Empty(b.Calls);
				rig.Model.Pages[1].MarkAsDirty();
				rig.Model.IsVisible = true; // opening does not wait for the timer
				Assert.Single(a.Calls);
				Assert.Single(a.Calls[0]);
				rig.Model.SelectedIndex = 1;
				Assert.Single(b.Calls);
				Assert.Single(b.Calls[0]); // the books the panel had given it before it showed
				rig.Model.Pages[0].MarkAsDirty();
				rig.Model.SelectedIndex = 0;
				Assert.Equal(2, a.Calls.Count);
				Assert.Empty(a.Calls[1]); // MarkAsDirty alone: OnShowInfo(new ComicBook[0])
				rig.Model.IsVisible = false;
				a.Calls.Clear();
				rig.SelectAndWait(Book("y"));
				Assert.Empty(a.Calls);
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task A_change_of_a_selected_book_restarts_the_timer_and_the_books_are_let_go_when_the_panel_closes()
		{
			await Ui.Run(() =>
			{
				var rig = new Rig();
				var a = new FakeInfoPage("A");
				rig.Model.AddPage(a);
				rig.Model.IsVisible = true;
				ComicBook x = Book("x");
				rig.SelectAndWait(x);
				int before = a.Calls.Count;
				Assert.False(rig.Model.IsPending);
				x.Title = "changed"; // previewBookChanged
				Assert.True(rig.Model.IsPending);
				rig.Scheduler.RunPending();
				Dispatcher.UIThread.RunJobs();
				Assert.Equal(before + 1, a.Calls.Count);
				rig.Model.IsVisible = false;
				x.Title = "changed again";
				Assert.False(rig.Model.IsPending);
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task A_page_added_while_the_panel_is_open_gets_the_selected_books_at_once()
		{
			await Ui.Run(() =>
			{
				var rig = new Rig();
				rig.Model.IsVisible = true; // the panel may be open (saved) before a script adds its page
				rig.Selected = new[] { Book("x") };
				var a = new FakeInfoPage("A");
				rig.Model.AddPage(a);
				Assert.Single(a.Calls);
				Assert.Single(a.Calls[0]);
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task The_test_page_counts_the_books_it_is_given()
		{
			await Ui.Run(() =>
			{
				var page = new TestInfoPage();
				Assert.Equal("Test Page", page.Title);
				Assert.Equal(-1, page.BookCount);
				Assert.NotNull(page.View);
				page.ShowInfo(new[] { Book("a"), Book("b") });
				Assert.Equal(2, page.BookCount);
				Assert.Equal(1, page.Calls);
				return Task.CompletedTask;
			});
		}

		[Fact]
		public void The_hidden_option_needs_a_private_data_folder()
		{
			Assert.True(AppOptions.Parse(new[] { "--data-dir", "/tmp/crmac-x", "--test-info-page" }).TestInfoPage);
			Assert.False(AppOptions.Parse(new[] { "--data-dir", "/tmp/crmac-x" }).TestInfoPage);
			Assert.Contains("--test-info-page", AppOptions.Parse(new[] { "--test-info-page" }).Error);
		}
	}

	/// <summary>P5.6b: the search links (<c>SearchContextMenuBuilder</c>, <c>ShowSearchLinks</c>) with an engine that never reads the network.</summary>
	public class SearchLinkMenuTests
	{
		private sealed class FakeSearch : INetSearch
		{
			public string Name => "Fake";

			public System.Drawing.Image Image => null;

			public Func<string, int, SearchResult[]> Handler { get; set; } = (text, limit) => new SearchResult[0];

			public List<(string text, int limit)> Calls { get; } = new List<(string, int)>();

			public string Generic { get; set; } = "https://example.org/search?q=";

			public IEnumerable<SearchResult> Search(string hint, string text, int limit)
			{
				Calls.Add((text, limit));
				return Handler(text, limit);
			}

			public string GenericSearchLink(string hint, string text) => Generic == null ? null : Generic + text;
		}

		private static SearchResult R(string name) => new SearchResult { Name = name, Result = "https://example.org/wiki/" + name.Replace(' ', '_') };

		[Fact]
		public void The_arrow_needs_the_option_a_text_and_a_searchable_field()
		{
			bool on = true;
			var menu = new SearchLinkMenu(new[] { new FakeSearch() }, () => on);
			var book = new ComicBook { Series = "Batman" };
			Assert.True(menu.IsAvailable(book, "Series", "Batman"));
			Assert.False(menu.IsAvailable(book, "Series", ""));
			Assert.False(menu.IsAvailable(book, "Series", null));
			Assert.False(menu.IsAvailable(book, "PageCount", "12")); // not a searchable field
			Assert.False(menu.IsAvailable(null, "Series", "Batman"));
			on = false;
			Assert.False(menu.IsAvailable(book, "Series", "Batman")); // ShowSearchLinks off
		}

		[Fact]
		public async Task One_engine_gives_its_results_then_a_separator_and_Search_with_the_web_address_of_the_book_last()
		{
			var engine = new FakeSearch { Handler = (text, limit) => new[] { R("Batman"), R("Batman Begins") } };
			var menu = new SearchLinkMenu(new[] { engine });
			IReadOnlyList<SearchMenuEntry> items = await menu.BuildAsync("Series", " Batman ", "https://book.example/web");
			Assert.Equal(new[] { "Batman", "Batman Begins", null, "Search...", null, "https://book.example/web" }, items.Select(i => i.IsSeparator ? null : i.Header).ToArray());
			Assert.Equal(("Batman", 10), engine.Calls.Single()); // trimmed, 10 results at most
			Assert.Equal("https://example.org/search?q=Batman", items[3].Address);
		}

		[Fact]
		public void Without_a_result_for_the_whole_text_each_part_between_commas_is_searched_for_two_results()
		{
			var engine = new FakeSearch
			{
				Handler = (text, limit) => text == "Bob, Alice" ? new SearchResult[0] : text == "Bob" ? new[] { R("Bob Smith"), R("Bob Jones") } : text == "Alice" ? new[] { R("Alice A") } : new SearchResult[0],
			};
			var menu = new SearchLinkMenu(new[] { engine });
			IReadOnlyList<SearchMenuEntry> items = menu.BuildItems(engine, "Writer", "Bob, Alice, Bob, ,Nobody");
			Assert.Equal(new[] { "Bob Smith", "Bob Jones", null, "Alice A", null, "Search..." }, items.Select(i => i.IsSeparator ? null : i.Header).ToArray());
			Assert.Equal(new[] { 10, 2, 2, 2 }, engine.Calls.Select(c => c.limit).ToArray()); // Bob once (Distinct)
		}

		[Fact]
		public void No_result_says_No_Results_disabled_and_a_failing_engine_is_the_same()
		{
			var engine = new FakeSearch { Handler = (text, limit) => throw new InvalidOperationException("offline") };
			var menu = new SearchLinkMenu(new[] { engine });
			IReadOnlyList<SearchMenuEntry> items = menu.BuildItems(engine, "Series", "x");
			Assert.Equal("No Results", items[0].Header);
			Assert.False(items[0].IsEnabled);
			Assert.True(items[1].IsSeparator);
			Assert.Equal("Search...", items[2].Header);
			engine.Generic = null; // no generic link: no Search entry
			Assert.Single(menu.BuildItems(engine, "Series", "x"));
			Assert.Single(menu.BuildItems(engine, "Series", "  ")); // nothing to search for
		}

		[Fact]
		public async Task Several_engines_give_one_entry_each_and_an_entry_opens_its_address_in_the_browser()
		{
			var a = new FakeSearch();
			var opened = new List<string>();
			var menu = new SearchLinkMenu(new INetSearch[] { a, new FakeSearch() }, null, address =>
			{
				opened.Add(address);
				return true;
			});
			IReadOnlyList<SearchMenuEntry> items = await menu.BuildAsync("Series", "x");
			Assert.Equal(2, items.Count);
			Assert.All(items, i => Assert.NotNull(i.Engine));
			IReadOnlyList<SearchMenuEntry> sub = await menu.BuildItemsAsync(items[0].Engine, "Series", "x");
			Assert.True(menu.Open(sub.Last()));
			Assert.Equal(new[] { "https://example.org/search?q=x" }, opened);
			Assert.False(menu.Open(sub[0])); // "No Results" is disabled
			Assert.False(menu.Open(null));
			menu.Opener = address => throw new InvalidOperationException();
			Assert.False(menu.Open(sub.Last())); // SafeStart ignores a failure
		}

		[Fact]
		public void Only_web_addresses_reach_the_system()
		{
			Assert.False(MacShellProbe("file:///etc/passwd"));
			Assert.False(MacShellProbe("/Applications"));
			Assert.False(MacShellProbe("javascript:alert(1)"));
			Assert.False(MacShellProbe(""));
			Assert.False(MacShellProbe(null));
		}

		private static bool MacShellProbe(string address) => ComicRack.Mac.Host.Platform.MacShell.OpenUrl(address);
	}
}
