using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using ComicRack.Mac.Controls;
using ComicRack.Mac.Host;
using ComicRack.Mac.Reader;
using ComicRack.Mac.Services;
using ComicRack.Mac.Tests.Thumbnails;
using ComicRack.Mac.Thumbnails;
using ComicRack.Mac.ViewModels;
using ComicRack.Mac.Views;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Engine.Display;
using Xunit;

namespace ComicRack.Mac.Tests.Reader
{
	/// <summary>
	/// The main window with a reader (<see cref="MainWindow"/> hosting the reader area: <c>MainForm</c>, P5.32a) on the headless platform: books of generated pages
	/// are put into tabs of a <see cref="ReaderManager"/> that has no library engine behind it (<see cref="ReaderManager.Adopt"/>).
	/// </summary>
	public sealed class MainReaderHarness : IDisposable
	{
		private readonly HostService service;

		public MainReaderHarness(AppPreferences preferences = null, double width = 1100, double height = 720, bool show = true)
		{
			service = new HostService(AppOptions.Parse(new[] { "--data-dir", Path.Combine(Path.GetTempPath(), "crmac-never-started") }));
			Preferences = preferences ?? new AppPreferences();
			Shell = new NullShell();
			Tabs = new ReaderManager(service, Preferences);
			Dialogs = new FakeDialogs();
			Vm = new MainViewModel(Shell, service, Dialogs, Tabs, Preferences);
			Window = new MainWindow { DataContext = Vm, Width = width, Height = height };
			if (show)
			{
				Window.Show();
				Tick();
			}
		}

		public AppPreferences Preferences { get; }

		public NullShell Shell { get; }

		public FakeDialogs Dialogs { get; }

		public ReaderManager Tabs { get; }

		public MainViewModel Vm { get; }

		public MainWindow Window { get; }

		/// <summary>Opens a book of generated pages in a tab, as the browser's Open does: the new book takes the place of the current one unless <paramref name="open"/> says new tab.</summary>
		public async Task<ReaderSlot> AddBook(string name, int pages = 12, int resume = 3, OpenBookOptions open = OpenBookOptions.None)
		{
			var session = new FakeSession(new FakePageDecoder { PageCount = pages, Size = p => (1200, 1800) }) { ResumePage = resume, Title = name };
			var model = new ReaderViewModel(session, ReaderHarness.DefaultOptions());
			ReaderSlot slot = Tabs.Adopt(model, new ComicBook { FilePath = "/books/" + name + ".cbz" }, open);
			await model.OpenAsync();
			await model.WhenSettled;
			Tick();
			return slot;
		}

		public void Tick()
		{
			for (int i = 0; i < 3; i++)
			{
				Dispatcher.UIThread.RunJobs();
				Window.UpdateLayout();
				AvaloniaHeadlessPlatform.ForceRenderTimerTick();
			}
		}

		public T Find<T>(string name) where T : Control => Window.FindControl<T>(name);

		public void Press(Key key, KeyModifiers modifiers = KeyModifiers.None)
		{
			Window.KeyPress(key, ToRaw(modifiers), PhysicalKeyOf(key), null);
			Tick();
		}

		private static RawInputModifiers ToRaw(KeyModifiers m)
		{
			RawInputModifiers raw = RawInputModifiers.None;
			if ((m & KeyModifiers.Shift) != 0) raw |= RawInputModifiers.Shift;
			if ((m & KeyModifiers.Meta) != 0) raw |= RawInputModifiers.Meta;
			if ((m & KeyModifiers.Control) != 0) raw |= RawInputModifiers.Control;
			if ((m & KeyModifiers.Alt) != 0) raw |= RawInputModifiers.Alt;
			return raw;
		}

		private static PhysicalKey PhysicalKeyOf(Key key)
		{
			switch (key)
			{
				case Key.Escape: return PhysicalKey.Escape;
				case Key.Tab: return PhysicalKey.Tab;
				case Key.W: return PhysicalKey.W;
				case Key.F: return PhysicalKey.F;
				case Key.D: return PhysicalKey.D;
				case Key.PageDown: return PhysicalKey.PageDown;
				default: return PhysicalKey.None;
			}
		}

		public void Dispose()
		{
			Window.Close();
			Tabs.CloseAll();
			Tick();
		}
	}

	public class MainWindowReaderTests : IDisposable
	{
		private readonly long liveBefore;

		public MainWindowReaderTests()
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

		private static string[] Captions(MainReaderHarness h) => h.Tabs.Slots.Select(s => s.Caption).ToArray();

		private static string[] TabTexts(MainReaderHarness h) => h.Window.Tabs.Tabs.Where(t => t.Selectable).Select(t => t.Text).ToArray();

		// ---- the layout

		[Fact]
		public async Task Without_a_book_the_window_shows_the_tab_strip_with_the_new_tab_button_an_empty_reader_area_and_the_browser_docked_at_the_bottom()
		{
			await Ui.Run(() =>
			{
				using (var h = new MainReaderHarness())
				{
					CrTabBar tabs = h.Window.Tabs;
					Assert.True(tabs.IsVisible);
					Assert.Equal(31, tabs.Bounds.Height);
					Assert.Single(tabs.Tabs);
					CrTabItem add = tabs.Tabs[0];
					Assert.False(add.Selectable);
					Assert.False(add.ShowText);
					Assert.Equal("AddTab", add.IconName);
					Assert.Equal(-1, tabs.SelectedIndex);
					Assert.Same(h.Window.ReaderToolbar, tabs.ToolbarSlot);
					// the reader items of the toolbar are hidden without a book; Full Screen and Tools stay
					Assert.False(h.Find<CrSplitButton>("PreviousPageButton").IsVisible);
					Assert.False(h.Find<CrSplitButton>("ZoomButton").IsVisible);
					Assert.True(h.Find<CrToolButton>("FullScreenButton").IsVisible);
					Assert.True(h.Find<CrToolButton>("ToolsButton").IsVisible);
					// the reader area shows the Quick Open pane (P5.29) and the browser is docked at the bottom, 250 high with its grip
					Assert.False(h.Window.Reader.IsEffectivelyVisible);
					Assert.True(h.Find<Border>("QuickOpenPane").IsVisible);
					CrSizableContainer dock = h.Find<CrSizableContainer>("BrowserDock");
					Assert.Equal(250, dock.Bounds.Height);
					Assert.Equal(GripPosition.Top, dock.Grip);
					Assert.False(dock.Fill);
					Assert.True(dock.Bounds.Y > h.Window.Tabs.Bounds.Bottom, "the browser is below the reader area");
					Assert.Equal(24, h.Find<CrStatusBar>("StatusBar").Bounds.Height);
					Assert.Equal("None", h.Find<TextBlock>("BookSegment").Text);
					Assert.Equal("NA", h.Find<TextBlock>("PageSegment").Text);
					Assert.Equal("NA", h.Find<TextBlock>("PageCountSegment").Text);
					Assert.Equal("ComicRack", h.Window.Title);
				}
				return Task.FromResult(0);
			});
		}

		[Fact]
		public async Task A_window_without_a_reader_has_no_tab_strip_and_the_browser_fills_it()
		{
			await Ui.Run(() =>
			{
				var service = new HostService(AppOptions.Parse(new[] { "--data-dir", Path.Combine(Path.GetTempPath(), "crmac-never-started") }));
				var vm = new MainViewModel(new NullShell(), service, new FakeDialogs());
				var window = new MainWindow { DataContext = vm, Width = 900, Height = 600 };
				window.Show();
				Assert.False(window.Tabs.IsVisible);
				Assert.False(window.FindControl<Grid>("ReaderHost").IsVisible);
				CrSizableContainer dock = window.FindControl<CrSizableContainer>("BrowserDock");
				Assert.True(dock.Fill);
				Assert.True(dock.Bounds.Height > 500, "the browser fills the window: " + dock.Bounds.Height);
				window.Close();
				return Task.FromResult(0);
			});
		}

		[Fact]
		public async Task A_book_in_a_tab_shows_its_page_in_the_reader_area_its_caption_in_the_tab_and_the_title_and_its_page_in_the_status_segments()
		{
			await Ui.Run(async () =>
			{
				using (var h = new MainReaderHarness())
				{
					ReaderSlot slot = await h.AddBook("Fake Book");
					CrTabBar tabs = h.Window.Tabs;
					Assert.Equal(2, tabs.Tabs.Count);
					Assert.Equal("Fake Book", tabs.Tabs[0].Text);
					Assert.True(tabs.Tabs[0].CanClose);
					Assert.Equal(100, tabs.Tabs[0].MinimumWidth);
					Assert.Same(slot, tabs.Tabs[0].Tag);
					Assert.Equal(0, tabs.SelectedIndex);
					Assert.True(tabs.Tabs[1].ToolTipText.Length > 0);
					Assert.True(h.Window.Reader.IsEffectivelyVisible);
					Assert.False(h.Find<Border>("QuickOpenPane").IsVisible);
					Assert.Same(slot.Model, h.Window.Reader.DataContext);
					Assert.Same(slot.Model, h.Window.Reader.Model);
					Assert.NotNull(slot.Model.Scene);
					Assert.Equal("Fake Book", h.Find<TextBlock>("BookSegment").Text);
					Assert.Equal("4", h.Find<TextBlock>("PageSegment").Text);
					Assert.Equal("12", h.Find<TextBlock>("PageCountSegment").Text);
					Assert.Equal("ComicRack - Fake Book", h.Window.Title);
					// the toolbar of the reader is there and follows the state of the book
					Assert.True(h.Find<CrSplitButton>("PreviousPageButton").IsVisible);
					Assert.Equal("100%", h.Find<CrSplitButton>("ZoomButton").Text);
					Assert.Equal("SinglePage", h.Find<CrSplitButton>("PageLayoutButton").IconName);
					Assert.Equal("FitAll", h.Find<CrSplitButton>("FitButton").IconName);
					slot.Model.TwoPageLayout();
					await slot.Model.WhenSettled;
					slot.Model.FitWidth();
					slot.Model.ZoomIn();
					h.Tick();
					Assert.Equal("TwoPageForced", h.Find<CrSplitButton>("PageLayoutButton").IconName);
					Assert.Equal("FitWidth", h.Find<CrSplitButton>("FitButton").IconName);
					Assert.Equal("125%", h.Find<CrSplitButton>("ZoomButton").Text);
					// a page turn moves the page segment
					slot.Model.NextPage();
					await slot.Model.WhenSettled;
					h.Tick();
					Assert.Equal("6", h.Find<TextBlock>("PageSegment").Text); // two pages: 4 and 5, then 6 and 7
					// the arrow parts have the menus of the original with the items the reader has
					Assert.Equal(1, ((MenuFlyout)h.Find<CrSplitButton>("PreviousPageButton").DropDown).Items.Count);
					Assert.Equal(5, ((MenuFlyout)h.Find<CrSplitButton>("PageLayoutButton").DropDown).Items.Count);
					Assert.Equal(3, ((MenuFlyout)h.Find<CrSplitButton>("FitButton").DropDown).Items.Count);
					var tools = (MenuFlyout)h.Find<CrToolButton>("ToolsButton").Flyout;
					Assert.Equal(new[] { "About...", "Exit" }, tools.Items.OfType<MenuItem>().Select(m => (string)m.Header).ToArray());
					tools.Items.OfType<MenuItem>().Last().Command.Execute(null);
					Assert.Equal(1, h.Shell.Quits);
				}
			});
		}

		[Fact]
		public async Task The_buttons_of_the_toolbar_run_the_commands_of_the_original_on_the_book_of_the_current_tab()
		{
			await Ui.Run(async () =>
			{
				using (var h = new MainReaderHarness())
				{
					ReaderSlot first = await h.AddBook("One", resume: 2);
					ReaderViewModel m = first.Model;
					h.Find<CrToolButton>("NextPageButton").Command.Execute(null);
					await m.WhenSettled;
					Assert.Equal(3, m.CurrentPage);
					h.Find<CrToolButton>("PreviousPageButton").Command.Execute(null);
					await m.WhenSettled;
					Assert.Equal(2, m.CurrentPage);
					// the layout button cycles one page, two pages, two pages (adaptive); the fit button Fit All, Fit Width, Fit Height; the zoom button 100, 125, 150, 200
					var layout = h.Find<CrToolButton>("PageLayoutButton");
					layout.Command.Execute(null);
					await m.WhenSettled;
					Assert.Equal(PageLayoutMode.Double, m.LayoutMode);
					layout.Command.Execute(null);
					await m.WhenSettled;
					Assert.Equal(PageLayoutMode.DoubleAdaptive, m.LayoutMode);
					layout.Command.Execute(null);
					await m.WhenSettled;
					Assert.Equal(PageLayoutMode.Single, m.LayoutMode);
					var fit = h.Find<CrToolButton>("FitButton");
					fit.Command.Execute(null);
					Assert.Equal(ImageFitMode.FitWidth, m.Fit);
					fit.Command.Execute(null);
					Assert.Equal(ImageFitMode.FitHeight, m.Fit);
					fit.Command.Execute(null);
					Assert.Equal(ImageFitMode.Fit, m.Fit);
					var zoom = h.Find<CrToolButton>("ZoomButton");
					var seen = new List<string>();
					for (int i = 0; i < 5; i++)
					{
						zoom.Command.Execute(null);
						seen.Add(m.ZoomText);
					}
					Assert.Equal(new[] { "125%", "150%", "200%", "100%", "125%" }, seen);

					// another book in a new tab: the toolbar now works on it, and the first one stays as it was
					int firstPage = m.CurrentPage;
					ReaderSlot second = await h.AddBook("Two", resume: 0, open: OpenBookOptions.InNewTab);
					h.Find<CrToolButton>("NextPageButton").Command.Execute(null);
					await second.Model.WhenSettled;
					Assert.Equal(1, second.Model.CurrentPage);
					Assert.Equal(firstPage, m.CurrentPage);
					Assert.Equal("100%", h.Find<CrSplitButton>("ZoomButton").Text);
				}
			});
		}

		// ---- full screen

		[Fact]
		public async Task Full_screen_follows_the_F_key_hides_the_tab_strip_the_status_bar_and_the_browser_and_escape_leaves_it()
		{
			await Ui.Run(async () =>
			{
				using (var h = new MainReaderHarness())
				{
					ReaderSlot slot = await h.AddBook("Full");
					h.Window.Reader.FocusReader();
					Control status = h.Find<Control>("StatusBar");
					Control tabs = h.Find<Control>("TabBar");
					Control dock = h.Find<Control>("BrowserDock");
					Assert.True(status.IsVisible && tabs.IsVisible && dock.IsVisible);
					Assert.NotEqual(WindowState.FullScreen, h.Window.WindowState);
					h.Press(Key.F);
					Assert.True(slot.Model.IsFullScreen);
					Assert.Equal(WindowState.FullScreen, h.Window.WindowState);
					Assert.True(h.Vm.IsFullScreen);
					Assert.False(status.IsVisible);
					Assert.False(tabs.IsVisible);
					Assert.False(dock.IsVisible);
					bool browser = false;
					h.Tabs.ToggleBrowserRequested += (s, e) => browser = true;
					h.Press(Key.Escape);
					Assert.False(slot.Model.IsFullScreen);
					Assert.NotEqual(WindowState.FullScreen, h.Window.WindowState);
					Assert.True(status.IsVisible && tabs.IsVisible && dock.IsVisible);
					Assert.False(browser, "the first Esc only leaves full screen");
					Assert.Equal(1, h.Tabs.OpenCount);
					// the user (or macOS) enters and leaves full screen: the reader follows
					h.Window.WindowState = WindowState.FullScreen;
					h.Tick();
					Assert.True(slot.Model.IsFullScreen);
					Assert.False(tabs.IsVisible);
					h.Window.WindowState = WindowState.Normal;
					h.Tick();
					Assert.False(slot.Model.IsFullScreen);
					Assert.True(tabs.IsVisible);
				}
			});
		}

		[Fact]
		public async Task Full_screen_without_a_book_keeps_the_window_as_it_is()
		{
			await Ui.Run(() =>
			{
				using (var h = new MainReaderHarness())
				{
					h.Window.WindowState = WindowState.FullScreen;
					h.Tick();
					Assert.True(h.Vm.IsFullScreen);
					Assert.True(h.Find<Control>("TabBar").IsVisible);
					Assert.True(h.Find<Control>("StatusBar").IsVisible);
					Assert.True(h.Find<Control>("BrowserDock").IsVisible);
					h.Window.WindowState = WindowState.Normal;
				}
				return Task.FromResult(0);
			});
		}

		// ---- the tabs (NavigatorManager)

		[Fact]
		public async Task A_new_tab_is_added_at_the_left_end_a_book_without_a_new_tab_takes_the_place_of_the_current_one_and_a_restored_tab_goes_to_the_right()
		{
			await Ui.Run(async () =>
			{
				using (var h = new MainReaderHarness())
				{
					ReaderSlot a = await h.AddBook("A");
					Assert.Equal(new[] { "A" }, Captions(h));
					// no new tab: A is replaced by B, and A's reader is closed
					ReaderSlot b = await h.AddBook("B");
					Assert.Equal(new[] { "B" }, Captions(h));
					Assert.True(a.Model.IsDisposed);
					// a new tab goes to the left end of the strip and becomes current (slots.Insert(0, ...))
					ReaderSlot c = await h.AddBook("C", open: OpenBookOptions.InNewTab);
					Assert.Equal(new[] { "C", "B" }, Captions(h));
					Assert.Equal(0, h.Tabs.CurrentSlot);
					Assert.Equal(new[] { "C", "B" }, TabTexts(h));
					Assert.Equal(0, h.Window.Tabs.SelectedIndex);
					// AppendNewSlots (the restored session) adds at the right end
					ReaderSlot d = await h.AddBook("D", open: OpenBookOptions.InNewTab | OpenBookOptions.Append);
					Assert.Equal(new[] { "C", "B", "D" }, Captions(h));
					Assert.Equal(2, h.Tabs.CurrentSlot);
					Assert.Equal(3, h.Tabs.OpenCount);
					Assert.Same(d.Model, h.Window.Reader.Model);
					// choosing a tab shows its book
					h.Vm.ShowTab(1);
					Assert.Same(b.Model, h.Window.Reader.Model);
					Assert.Equal(1, h.Window.Tabs.SelectedIndex);
					Assert.Equal("ComicRack - B", h.Window.Title);
					Assert.Equal(new[] { "C", "B", "D" }, h.Vm.OpenBookSlots.Select(s => s.Caption).ToArray());
				}
			});
		}

		[Fact]
		public async Task New_Tab_adds_an_empty_slot_that_the_next_book_takes_and_a_book_that_is_open_is_shown_instead_of_opened_again()
		{
			await Ui.Run(async () =>
			{
				using (var h = new MainReaderHarness())
				{
					ReaderSlot a = await h.AddBook("A");
					h.Vm.NewTabCommand.Execute(null);
					h.Tick();
					Assert.Equal(2, h.Tabs.Slots.Count);
					Assert.True(h.Tabs.Current.IsEmpty);
					Assert.Equal(1, h.Tabs.OpenCount);
					Assert.Equal("None", h.Tabs.Current.Caption);
					Assert.Equal(new[] { "A", "None" }, TabTexts(h));
					Assert.False(h.Window.Reader.IsEffectivelyVisible, "an empty tab shows the Quick Open pane");
					Assert.True(h.Find<Border>("QuickOpenPane").IsVisible);
					Assert.Equal("None", h.Find<TextBlock>("BookSegment").Text);
					Assert.Equal("ComicRack", h.Window.Title);
					// the next book takes the empty slot, as the original's Open without OpenInNewSlot does
					ReaderSlot b = await h.AddBook("B");
					Assert.Equal(new[] { "A", "B" }, Captions(h));
					Assert.Equal(1, h.Tabs.CurrentSlot);
					Assert.False(a.Model.IsDisposed);
					// opening a book that is open already only shows its tab
					var book = a.Book;
					Assert.Same(a, h.Tabs.Open(book, OpenBookOptions.InNewTab));
					Assert.Equal(0, h.Tabs.CurrentSlot);
					Assert.Equal(2, h.Tabs.OpenCount);
				}
			});
		}

		[Fact]
		public async Task Previous_and_Next_Tab_wrap_around_and_Close_shows_the_tab_to_the_right_or_the_last_one()
		{
			await Ui.Run(async () =>
			{
				using (var h = new MainReaderHarness())
				{
					await h.AddBook("A");
					await h.AddBook("B", open: OpenBookOptions.InNewTab | OpenBookOptions.Append);
					await h.AddBook("C", open: OpenBookOptions.InNewTab | OpenBookOptions.Append);
					Assert.Equal(new[] { "A", "B", "C" }, Captions(h));
					Assert.Equal(2, h.Tabs.CurrentSlot);
					h.Vm.NextTabCommand.Execute(null);
					Assert.Equal(0, h.Tabs.CurrentSlot);
					h.Vm.PreviousTabCommand.Execute(null);
					Assert.Equal(2, h.Tabs.CurrentSlot);
					h.Vm.PreviousTabCommand.Execute(null);
					Assert.Equal(1, h.Tabs.CurrentSlot);
					// closing the current tab shows the one to its right (it moves into the place of the closed one)
					ReaderSlot closing = h.Tabs.Current;
					h.Vm.CloseBookCommand.Execute(null);
					Assert.Equal(new[] { "A", "C" }, Captions(h));
					Assert.Equal(1, h.Tabs.CurrentSlot);
					Assert.Equal("C", h.Tabs.Current.Caption);
					Assert.True(closing.Model.IsDisposed);
					// the last tab closes to the left
					h.Vm.CloseBookCommand.Execute(null);
					Assert.Equal(new[] { "A" }, Captions(h));
					Assert.Equal(0, h.Tabs.CurrentSlot);
					h.Vm.CloseBookCommand.Execute(null);
					Assert.Empty(h.Tabs.Slots);
					Assert.Equal(-1, h.Tabs.CurrentSlot);
					Assert.False(h.Vm.CloseBookCommand.CanExecute(null));
					Assert.False(h.Vm.HasBook);
					Assert.True(h.Find<Border>("QuickOpenPane").IsVisible);
					Assert.Equal("ComicRack", h.Window.Title);
				}
			});
		}

		[Fact]
		public async Task Close_All_Close_All_But_This_and_Close_All_to_the_Right_close_the_tabs_and_record_their_pages()
		{
			await Ui.Run(async () =>
			{
				using (var h = new MainReaderHarness())
				{
					ReaderSlot[] slots =
					{
						await h.AddBook("A"),
						await h.AddBook("B", open: OpenBookOptions.InNewTab | OpenBookOptions.Append),
						await h.AddBook("C", open: OpenBookOptions.InNewTab | OpenBookOptions.Append),
						await h.AddBook("D", open: OpenBookOptions.InNewTab | OpenBookOptions.Append),
					};
					h.Tabs.Select(1);
					h.Vm.CloseAllToTheRightCommand.Execute(null);
					Assert.Equal(new[] { "A", "B" }, Captions(h));
					Assert.True(slots[2].Model.IsDisposed && slots[3].Model.IsDisposed);
					Assert.False(h.Vm.CloseAllToTheRightCommand.CanExecute(null), "B is the last tab");
					h.Vm.CloseAllButThisCommand.Execute(null);
					Assert.Equal(new[] { "B" }, Captions(h));
					Assert.True(slots[0].Model.IsDisposed);
					Assert.Equal(0, h.Tabs.CurrentSlot);
					Assert.Same(slots[1].Model, h.Window.Reader.Model);
					h.Vm.CloseAllBooksCommand.Execute(null);
					Assert.Empty(h.Tabs.Slots);
					Assert.True(slots[1].Model.IsDisposed);
					Assert.False(h.Window.Reader.IsEffectivelyVisible);
					Assert.Equal(0, h.Tabs.OpenCount);
				}
			});
		}

		[Fact]
		public async Task The_tab_strip_follows_clicks_the_close_box_the_new_tab_button_and_the_right_click_menu()
		{
			await Ui.Run(async () =>
			{
				using (var h = new MainReaderHarness())
				{
					await h.AddBook("First");
					await h.AddBook("Second", open: OpenBookOptions.InNewTab | OpenBookOptions.Append);
					await h.AddBook("Third", open: OpenBookOptions.InNewTab | OpenBookOptions.Append);
					CrTabBar bar = h.Window.Tabs;
					Assert.Equal(new[] { "First", "Second", "Third" }, TabTexts(h));
					Assert.Equal(2, bar.SelectedIndex);
					// a click on a tab shows its book
					Rect first = bar.Geometry[0].Bounds;
					bar.Click(new Point(first.X + 10, first.Y + 10));
					Assert.Equal(0, h.Tabs.CurrentSlot);
					Assert.Equal("First", h.Window.Reader.Model.BookSegmentText);
					// the close box of the hot tab closes it
					Rect second = bar.Geometry[1].Bounds;
					bar.UpdateHot(new Point(second.X + 20, second.Y + 10));
					bar.Click(new Point(bar.Geometry[1].CloseBounds.X + 4, bar.Geometry[1].CloseBounds.Y + 4));
					h.Tick();
					Assert.Equal(new[] { "First", "Third" }, Captions(h));
					// the New Tab button adds an empty tab at the right end and makes it current
					Rect add = bar.Geometry.Last().Bounds;
					bar.Click(new Point(add.X + 10, add.Y + 10));
					h.Tick();
					Assert.Equal(3, h.Tabs.Slots.Count);
					Assert.True(h.Tabs.Current.IsEmpty);
					Assert.Equal(2, bar.SelectedIndex);
					// a right click selects the tab and opens the tab menu with the commands of the original
					Rect third = bar.Geometry[1].Bounds;
					bool opening = false;
					bar.TabMenuOpening += (s, e) => opening = e.Index == 1;
					Assert.True(bar.OpenTabMenu(new Point(third.X + 10, third.Y + 10)));
					Assert.True(opening);
					Assert.Equal(1, h.Tabs.CurrentSlot);
					string[] headers = bar.TabMenu.Items.OfType<MenuItem>().Select(m => (string)m.Header).ToArray();
					Assert.Equal(new[] { "Close", "Close All But This", "Close All to the Right", "Show in Browser", "Reveal in Finder", "Copy Full Path to Clipboard" }, headers);
					// the menu items run the commands
					bar.TabMenu.Items.OfType<MenuItem>().Single(m => (string)m.Header == "Close All to the Right").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
					Assert.Equal(new[] { "First", "Third" }, Captions(h));
					// right click on the New Tab button or on empty space does nothing
					Assert.False(bar.OpenTabMenu(new Point(bar.Bounds.Width - 5, 10)));
				}
			});
		}

		[Fact]
		public async Task The_tab_menu_reveals_and_copies_the_path_of_the_book_and_Show_in_Browser_opens_the_dock_and_selects_the_book()
		{
			await Ui.Run(async () =>
			{
				using (var h = new MainReaderHarness())
				{
					ReaderSlot slot = await h.AddBook("Path Book");
					h.Vm.RevealInFinderCommand.Execute(null);
					Assert.Equal(new[] { "/books/Path Book.cbz" }, h.Shell.Revealed);
					await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)h.Vm.CopyFullPathCommand).ExecuteAsync(null);
					Assert.Equal(new[] { "/books/Path Book.cbz" }, h.Shell.Copied);
					// Show in Browser: the dock opens and the library window comes forward; the book is selected when the shown list has it
					h.Vm.IsBrowserVisible = false;
					bool focus = false;
					h.Vm.FocusBrowserRequested += (s, e) => focus = true;
					Assert.True(h.Vm.ShowInBrowserCommand.CanExecute(null));
					await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)h.Vm.ShowInBrowserCommand).ExecuteAsync(null);
					Assert.True(h.Vm.IsBrowserVisible);
					Assert.True(focus);
					Assert.Equal(1, h.Shell.Activations);
					var item = new BookItemFor(slot.Book);
					h.Vm.Browser.SetCatalog(new ComicRack.Mac.Browser.BookCatalog(new[] { item.Item }));
					await h.Vm.Browser.WhenIdle();
					h.Vm.Browser.SelectedIndex = -1;
					await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)h.Vm.ShowInBrowserCommand).ExecuteAsync(null);
					Assert.Equal(0, h.Vm.Browser.SelectedIndex);
					// without a book the command is off
					h.Tabs.CloseAll();
					Assert.False(h.Vm.ShowInBrowserCommand.CanExecute(null));
					Assert.False(h.Vm.RevealInFinderCommand.CanExecute(null));
				}
			});
		}

		private sealed class BookItemFor
		{
			public BookItemFor(ComicBook book)
			{
				Item = new ComicRack.Mac.Browser.BookItem(book, "Path Book", "S", "1", DateTime.Now);
			}

			public ComicRack.Mac.Browser.BookItem Item { get; }
		}

		// ---- keys

		[Fact]
		public async Task Tab_Shift_Tab_D_Escape_and_Cmd_W_of_the_reader_work_on_the_tabs_the_browser_and_the_window()
		{
			await Ui.Run(async () =>
			{
				using (var h = new MainReaderHarness())
				{
					await h.AddBook("A");
					await h.AddBook("B", open: OpenBookOptions.InNewTab | OpenBookOptions.Append);
					await h.AddBook("C", open: OpenBookOptions.InNewTab | OpenBookOptions.Append);
					h.Window.Reader.FocusReader();
					Assert.Equal(2, h.Tabs.CurrentSlot);
					h.Press(Key.Tab); // NextTab wraps to the first
					Assert.Equal(0, h.Tabs.CurrentSlot);
					h.Press(Key.Tab, KeyModifiers.Shift);
					Assert.Equal(2, h.Tabs.CurrentSlot);
					// Esc (ShowBrowser) closes the browser dock and opens it again
					h.Window.Reader.FocusReader();
					Assert.True(h.Vm.IsBrowserVisible);
					h.Press(Key.Escape);
					Assert.False(h.Vm.IsBrowserVisible);
					Assert.False(h.Find<Control>("BrowserDock").IsVisible, "a closed dock without the always visible grip is hidden");
					h.Press(Key.Escape);
					Assert.True(h.Vm.IsBrowserVisible);
					Assert.Equal(3, h.Tabs.OpenCount);
					// Cmd+W closes exactly one tab, from the page and from anywhere else in the window (the alias of Close)
					h.Window.Reader.FocusReader();
					h.Press(Key.W, KeyModifiers.Meta);
					Assert.Equal(2, h.Tabs.OpenCount);
					Assert.Contains(h.Window.KeyBindings, b => b.Gesture.Equals(new KeyGesture(Key.W, KeyModifiers.Meta)));
					// D moves the reader into its own window
					h.Window.Reader.FocusReader();
					Assert.False(h.Vm.IsReaderUndocked);
					h.Press(Key.D);
					Assert.True(h.Vm.IsReaderUndocked);
					Assert.NotNull(h.Window.UndockedReader);
				}
			});
		}

		// ---- the reader in its own window

		[Fact]
		public async Task The_reader_in_its_own_window_shows_the_current_book_while_the_main_window_shows_the_browser_alone_and_closing_it_docks_the_reader_again()
		{
			await Ui.Run(async () =>
			{
				var prefs = new AppPreferences();
				using (var h = new MainReaderHarness(prefs))
				{
					// without a book there is no window to show
					h.Vm.ToggleReaderUndockedCommand.Execute(null);
					h.Tick();
					Assert.True(h.Vm.IsReaderUndocked);
					Assert.Null(h.Window.UndockedReader);
					Assert.True(prefs.ReaderUndocked);
					CrSizableContainer dock = h.Find<CrSizableContainer>("BrowserDock");
					Assert.True(dock.Fill, "the browser fills the main window while the reader is in its own window");
					Assert.False(h.Window.Reader.IsEffectivelyVisible);
					Assert.False(h.Find<Grid>("ReaderHost").IsVisible);
					Assert.True(h.Window.Tabs.IsVisible, "the tab strip stays in the main window");

					ReaderSlot a = await h.AddBook("Undocked A");
					ReaderWindow reader = h.Window.UndockedReader;
					Assert.NotNull(reader);
					Assert.True(reader.IsVisible);
					Assert.Same(a.Model, reader.Model);
					Assert.Same(a.Model, reader.Area.Model);
					Assert.Null(h.Window.Reader.DataContext);
					Assert.Equal("Undocked A", reader.Title);
					Assert.NotNull(NativeMenu.GetMenu(reader));
					reader.UpdateLayout();
					Assert.True(reader.ReadingArea.Bounds.Width > 100);

					// the window follows the current tab and its caption
					ReaderSlot b = await h.AddBook("Undocked B", open: OpenBookOptions.InNewTab);
					Assert.Same(reader, h.Window.UndockedReader);
					Assert.Same(b.Model, reader.Model);
					Assert.Equal("Undocked B", reader.Title);
					h.Vm.ShowTab(1);
					Assert.Same(a.Model, reader.Model);
					// the keys of the page work in the window
					reader.ReadingArea.Focus();
					Assert.True(a.Model.HandleKey(Key.PageDown, KeyModifiers.None));
					await a.Model.WhenSettled;
					Assert.Equal(4, a.Model.CurrentPage);
					Assert.Equal("5", h.Find<TextBlock>("PageSegment").Text);

					// closing the reader window docks the reader again (ReaderFormFormClosing)
					reader.Close();
					h.Tick();
					Assert.False(h.Vm.IsReaderUndocked);
					Assert.Null(h.Window.UndockedReader);
					Assert.False(prefs.ReaderUndocked);
					Assert.Same(a.Model, h.Window.Reader.DataContext);
					Assert.True(h.Window.Reader.IsEffectivelyVisible);
					Assert.False(dock.Fill);
					Assert.Equal(2, h.Tabs.OpenCount);

					// and the other way: the toggle command undocks it again, closing the last tab ends the window
					h.Vm.ToggleReaderUndockedCommand.Execute(null);
					h.Tick();
					Assert.NotNull(h.Window.UndockedReader);
					h.Tabs.CloseAll();
					h.Tick();
					Assert.Null(h.Window.UndockedReader);
					Assert.True(h.Vm.IsReaderUndocked);
				}
			});
		}

		[Fact]
		public async Task The_frame_of_the_undocked_reader_window_is_kept_in_the_preferences()
		{
			await Ui.Run(async () =>
			{
				var prefs = new AppPreferences { ReaderWindow = new WindowFrame(120, 90, 640, 480), ReaderUndocked = true };
				using (var h = new MainReaderHarness(prefs))
				{
					Assert.True(h.Vm.IsReaderUndocked);
					await h.AddBook("Framed");
					ReaderWindow reader = h.Window.UndockedReader;
					Assert.NotNull(reader);
					Assert.Equal(640, reader.Width);
					Assert.Equal(480, reader.Height);
					reader.Width = 700;
					reader.Height = 520;
					h.Tick();
					for (int i = 0; i < 50 && prefs.ReaderWindow.Width != 700; i++)
					{
						await Task.Delay(10);
						h.Tick();
					}
					Assert.Equal(700, prefs.ReaderWindow.Width);
					Assert.Equal(520, prefs.ReaderWindow.Height);
					// the application closing its windows is not the user docking the reader: the next start finds it undocked
					h.Window.Close();
					h.Tick();
					Assert.True(prefs.ReaderUndocked);
					Assert.Null(h.Window.UndockedReader);
				}
			});
		}

		// ---- the browser dock

		[Fact]
		public async Task The_browser_dock_is_250_high_collapses_with_a_click_on_its_grip_keeps_its_grip_when_asked_and_remembers_its_height()
		{
			await Ui.Run(() =>
			{
				var prefs = new AppPreferences();
				using (var h = new MainReaderHarness(prefs))
				{
					CrSizableContainer dock = h.Find<CrSizableContainer>("BrowserDock");
					Assert.Equal(250, dock.CurrentExtent);
					// a drag of the grip sets the height, which is kept
					dock.DragBy(100);
					h.Tick();
					Assert.Equal(350, h.Vm.BrowserDockHeight);
					Assert.Equal(350, prefs.BrowserDockHeight);
					Assert.Equal(350, dock.Bounds.Height);
					// a click on the grip (the pointer does not move) collapses the dock; without the always visible grip the whole dock goes
					var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
					h.Window.MouseMove(new Point(500, dock.Bounds.Y + 3));
					h.Window.MouseDown(new Point(500, dock.Bounds.Y + 3), MouseButton.Left);
					h.Window.MouseUp(new Point(500, dock.Bounds.Y + 3), MouseButton.Left);
					h.Tick();
					Assert.False(h.Vm.IsBrowserVisible);
					Assert.False(prefs.BrowserDockExpanded);
					Assert.False(dock.IsVisible);
					Assert.Equal(350, prefs.BrowserDockHeight, 3);
					h.Vm.ToggleBrowserCommand.Execute(null);
					h.Tick();
					Assert.True(dock.IsVisible);
					Assert.True(prefs.BrowserDockExpanded);
				}
				// a new window starts from what was kept
				var again = new AppPreferences { BrowserDockHeight = 300, BrowserDockExpanded = false, AlwaysDisplayBrowserDockingGrip = true };
				using (var h = new MainReaderHarness(again))
				{
					CrSizableContainer dock = h.Find<CrSizableContainer>("BrowserDock");
					Assert.False(h.Vm.IsBrowserVisible);
					Assert.Equal(300, h.Vm.BrowserDockHeight);
					Assert.True(dock.KeepGripVisible);
					Assert.True(dock.IsVisible, "the grip stays");
				}
				return Task.FromResult(0);
			});
		}

		// ---- Read > Track current Page

		[Fact]
		public async Task Track_current_Page_switches_the_Core_flag_the_preference_and_the_padlock_of_the_page_segment()
		{
			await Ui.Run(() =>
			{
				bool before = cYo.Projects.ComicRack.Engine.ComicBookNavigator.TrackCurrentPage;
				try
				{
					var prefs = new AppPreferences();
					using (var h = new MainReaderHarness(prefs))
					{
						Assert.True(h.Vm.IsTrackingCurrentPage);
						Assert.False(h.Find<CrIcon>("PageLockIcon").IsVisible);
						h.Vm.ToggleTrackCurrentPageCommand.Execute(null);
						h.Tick();
						Assert.False(prefs.TrackCurrentPage);
						Assert.False(cYo.Projects.ComicRack.Engine.ComicBookNavigator.TrackCurrentPage);
						Assert.True(h.Find<CrIcon>("PageLockIcon").IsVisible);
						h.Vm.ToggleTrackCurrentPageCommand.Execute(null);
						h.Tick();
						Assert.True(prefs.TrackCurrentPage);
						Assert.True(cYo.Projects.ComicRack.Engine.ComicBookNavigator.TrackCurrentPage);
						Assert.False(h.Find<CrIcon>("PageLockIcon").IsVisible);
					}
				}
				finally
				{
					cYo.Projects.ComicRack.Engine.ComicBookNavigator.TrackCurrentPage = before;
				}
				return Task.FromResult(0);
			});
		}

		// ---- the menu bar

		private static NativeMenuItem Top(NativeMenu menu, string header) => menu.Items.OfType<NativeMenuItem>().Single(i => i.Header == header);

		private static NativeMenuItem Sub(NativeMenuItem parent, string header) => parent.Menu.Items.OfType<NativeMenuItem>().Single(i => i.Header == header);

		[Fact]
		public async Task The_menu_bar_has_the_close_tab_and_read_and_display_items_of_the_original_with_their_shortcuts_and_works_on_the_current_book()
		{
			await Ui.Run(async () =>
			{
				using (var h = new MainReaderHarness())
				{
					NativeMenu menu = MainMenu.BuildMenuBar(h.Vm);
					Assert.Equal(new[] { "File", "Edit", "Browse", "Read", "Display", "Window", "Help" }, menu.Items.OfType<NativeMenuItem>().Select(i => i.Header).ToArray());
					NativeMenuItem file = Top(menu, "File");
					NativeMenuItem read = Top(menu, "Read");
					NativeMenuItem display = Top(menu, "Display");
					Assert.Equal(new KeyGesture(Key.X, KeyModifiers.Meta | KeyModifiers.Alt), Sub(file, "Close").Gesture);
					Assert.Equal(new KeyGesture(Key.X, KeyModifiers.Meta | KeyModifiers.Shift), Sub(file, "Close All").Gesture);
					Assert.Equal(new KeyGesture(Key.T, KeyModifiers.Meta), Sub(file, "New Tab").Gesture);
					Assert.Equal(new KeyGesture(Key.B, KeyModifiers.Meta), Sub(read, "First Page").Gesture);
					Assert.Equal(new KeyGesture(Key.P, KeyModifiers.Meta), Sub(read, "Previous Page").Gesture);
					Assert.Equal(new KeyGesture(Key.N, KeyModifiers.Meta), Sub(read, "Next Page").Gesture);
					Assert.Equal(new KeyGesture(Key.E, KeyModifiers.Meta), Sub(read, "Last Page").Gesture);
					Assert.Equal(new KeyGesture(Key.F3, KeyModifiers.Meta | KeyModifiers.Alt), Sub(read, "Show in Browser").Gesture);
					Assert.Equal(new KeyGesture(Key.J, KeyModifiers.Meta | KeyModifiers.Shift), Sub(read, "Previous Tab").Gesture);
					Assert.Equal(new KeyGesture(Key.K, KeyModifiers.Meta | KeyModifiers.Shift), Sub(read, "Next Tab").Gesture);
					Assert.Equal(new KeyGesture(Key.T, KeyModifiers.Alt | KeyModifiers.Shift), Sub(read, "Track current Page").Gesture);
					Assert.Equal(new KeyGesture(Key.F11, KeyModifiers.None), Sub(display, "Full Screen").Gesture);
					Assert.Equal(new KeyGesture(Key.F12, KeyModifiers.None), Sub(display, "Reader in own Window").Gesture);
					Assert.Equal(new KeyGesture(Key.F3, KeyModifiers.None), Sub(Top(menu, "Browse"), "Browser").Gesture);

					// without a book the commands of the reader are off
					Assert.False(Sub(read, "Next Page").Command.CanExecute(null));
					Assert.False(Sub(file, "Close").Command.CanExecute(null));
					Assert.False(Sub(read, "Show in Browser").Command.CanExecute(null));
					Assert.False(Sub(read, "Next Tab").Command.CanExecute(null));
					Assert.True(Sub(file, "New Tab").Command.CanExecute(null));
					Assert.False(Sub(file, "Open Books").IsEnabled);
					Assert.False(Sub(Sub(display, "Page Layout"), "Single Page").IsChecked);

					ReaderSlot a = await h.AddBook("Menu A");
					Assert.True(Sub(read, "Next Page").Command.CanExecute(null));
					Assert.True(Sub(file, "Close").Command.CanExecute(null));
					Assert.True(Sub(read, "Show in Browser").Command.CanExecute(null));
					Assert.False(Sub(read, "Next Tab").Command.CanExecute(null), "one tab only");
					Sub(read, "Next Page").Command.Execute(null);
					await a.Model.WhenSettled;
					Assert.Equal(4, a.Model.CurrentPage);

					NativeMenuItem layout = Sub(display, "Page Layout");
					Assert.True(Sub(layout, "Single Page").IsChecked);
					Assert.True(Sub(layout, "Fit All").IsChecked);
					Sub(layout, "Two Pages").Command.Execute(null);
					await a.Model.WhenSettled;
					Assert.True(Sub(layout, "Two Pages").IsChecked);
					Assert.False(Sub(layout, "Single Page").IsChecked);
					Sub(layout, "Right to Left").Command.Execute(null);
					Assert.True(Sub(layout, "Right to Left").IsChecked);
					Sub(layout, "Fit Width").Command.Execute(null);
					Assert.True(Sub(layout, "Fit Width").IsChecked);
					Assert.False(Sub(layout, "Fit All").IsChecked);
					NativeMenuItem zoom = Sub(display, "Zoom");
					Sub(zoom, "Zoom In").Command.Execute(null);
					Assert.Equal(1.25, a.Model.Zoom, 9);
					Sub(zoom, "100%").Command.Execute(null);
					Assert.Equal(1.0, a.Model.Zoom, 9);

					// a second tab with its own state: the marks follow the current tab
					ReaderSlot b = await h.AddBook("Menu B", open: OpenBookOptions.InNewTab);
					Assert.True(Sub(read, "Next Tab").Command.CanExecute(null));
					Assert.True(Sub(layout, "Single Page").IsChecked);
					Assert.False(Sub(layout, "Right to Left").IsChecked);
					Sub(layout, "Fit Height").Command.Execute(null);
					Assert.Equal(ImageFitMode.FitHeight, b.Model.Fit);
					Assert.Equal(ImageFitMode.FitWidth, a.Model.Fit);
					Sub(read, "Next Tab").Command.Execute(null);
					Assert.True(Sub(layout, "Two Pages").IsChecked);
					Assert.True(Sub(layout, "Right to Left").IsChecked);
					Assert.True(Sub(layout, "Fit Width").IsChecked);

					// File > Open Books lists the tabs with the original's shortcuts (Ctrl+Cmd+F1 to F12 here), the current one marked
					NativeMenuItem books = Sub(file, "Open Books");
					Assert.True(books.IsEnabled);
					NativeMenuItem[] entries = books.Menu.Items.OfType<NativeMenuItem>().ToArray();
					Assert.Equal(new[] { "Menu B", "Menu A" }, entries.Select(e => e.Header).ToArray());
					Assert.Equal(new KeyGesture(Key.F1, KeyModifiers.Meta | KeyModifiers.Control), entries[0].Gesture);
					Assert.Equal(new KeyGesture(Key.F2, KeyModifiers.Meta | KeyModifiers.Control), entries[1].Gesture);
					Assert.False(entries[0].IsChecked);
					Assert.True(entries[1].IsChecked);
					entries[0].Command.Execute(null);
					Assert.Equal(0, h.Tabs.CurrentSlot);
					Assert.True(books.Menu.Items.OfType<NativeMenuItem>().First().IsChecked);

					// Display > Reader in own Window and Browse > Browser carry their marks
					NativeMenuItem undock = Sub(display, "Reader in own Window");
					NativeMenuItem browser = Sub(Top(menu, "Browse"), "Browser");
					Assert.False(undock.IsChecked);
					Assert.True(browser.IsChecked);
					undock.Command.Execute(null);
					Assert.True(undock.IsChecked);
					browser.Command.Execute(null);
					Assert.True(browser.IsChecked, "the browser fills the window while the reader is in its own window: nothing to toggle");
					h.Vm.ToggleReaderUndockedCommand.Execute(null);
					browser.Command.Execute(null);
					Assert.False(browser.IsChecked);
				}
			});
		}

		[Fact]
		public async Task The_aliases_of_the_menu_close_the_current_book_open_it_in_a_new_tab_and_full_screen_keeps_working()
		{
			await Ui.Run(() =>
			{
				using (var h = new MainReaderHarness())
				{
					IReadOnlyList<KeyBinding> aliases = MainMenu.Aliases(h.Vm);
					KeyBinding close = aliases.Single(b => b.Gesture.Equals(new KeyGesture(Key.W, KeyModifiers.Meta)));
					Assert.Same(h.Vm.CloseBookCommand, close.Command);
					KeyBinding newTab = aliases.Single(b => b.Gesture.Equals(new KeyGesture(Key.O, KeyModifiers.Meta | KeyModifiers.Shift)));
					Assert.Same(h.Vm.Browser.OpenSelectedInNewTabCommand, newTab.Command);
					Assert.Contains(aliases, b => b.Gesture.Equals(new KeyGesture(Key.F, KeyModifiers.Meta | KeyModifiers.Control)));
				}
				return Task.FromResult(0);
			});
		}
	}
}
