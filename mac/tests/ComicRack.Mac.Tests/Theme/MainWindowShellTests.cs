using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using ComicRack.Mac.Browser;
using ComicRack.Mac.Controls;
using ComicRack.Mac.Services;
using ComicRack.Mac.Themes;
using ComicRack.Mac.Tests.HiDpi;
using ComicRack.Mac.ViewModels;
using ComicRack.Mac.Views;
using SkiaSharp;
using Xunit;
using static ComicRack.Mac.Tests.Theme.ChromeTestSupport;

namespace ComicRack.Mac.Tests.Theme
{
	/// <summary>
	/// P5.R1: the library window as the original's browser shell (toolbar 25, side panel 252 with a 6 unit grip, status bar 24, quick search 130 x 24),
	/// the native menu bar with the original's menus and the ledger's shortcuts, the status text of the browser, the tree rows. Headless frames; the
	/// real menu bar is checked by the e2e case menu-structure.
	/// </summary>
	public class MainWindowShellTests
	{
		private static (MainWindow window, MainViewModel vm) NewWindow()
		{
			var service = new HostService(AppOptions.Parse(new[] { "--data-dir", Path.Combine(Path.GetTempPath(), "crmac-never-started") }));
			var vm = new MainViewModel(new NullShell(), service, new FakeDialogs());
			return (new MainWindow { DataContext = vm }, vm);
		}

		private static T Part<T>(Window window, string name) where T : Control => window.FindControl<T>(name);

		private static void Layout(Window window)
		{
			Dpi.Settle(window);
			Dispatcher.UIThread.RunJobs();
		}

		[Fact]
		public async Task The_window_has_the_metrics_and_the_order_of_the_original_shell()
		{
			await Ui.Run(() =>
			{
				var (window, vm) = NewWindow();
				window.Show();
				Layout(window);
				Assert.Equal("ComicRack", window.Title);
				var toolbar = Part<CrToolBar>(window, "BrowserToolBar");
				var status = Part<CrStatusBar>(window, "StatusBar");
				var side = Part<CrSizableContainer>(window, "SidebarPanel");
				Assert.Equal(25, toolbar.Bounds.Height);
				Assert.Equal(24, status.Bounds.Height);
				Assert.Equal(252, side.Bounds.Width);
				Assert.Equal(CrSearchBox.RestWidth, Part<CrSearchBox>(window, "SearchBox").Bounds.Width);
				Assert.Equal(CrSearchBox.BoxHeight, Part<CrSearchBox>(window, "SearchBox").Bounds.Height);
				// the toolbar runs Sidebar, Arrange, quick search (the original's order), the status bar Ready text, Book, Page, page count, the slider
				var buttons = toolbar.Children;
				Assert.IsType<CrToolButton>(buttons[0]);
				Assert.IsType<CrSplitButton>(buttons[1]);
				Assert.IsType<CrSearchBox>(buttons[2]);
				Assert.True(Part<CrToolButton>(window, "SidebarButton").IsChecked);
				Assert.True(Part<TextBlock>(window, "BookSegment").Bounds.X < Part<TextBlock>(window, "PageSegment").Bounds.X);
				Assert.True(Part<TextBlock>(window, "PageSegment").Bounds.X < Part<TextBlock>(window, "PageCountSegment").Bounds.X);
				Assert.True(Part<TextBlock>(window, "PageCountSegment").Bounds.X < Part<CrStatusSlot>(window, "SizeSlot").Bounds.X);
				Assert.Equal("None", Part<TextBlock>(window, "BookSegment").Text);
				Assert.Equal("NA", Part<TextBlock>(window, "PageSegment").Text);
				Assert.Equal("Starting the library engine...", Part<TextBlock>(window, "StatusLabel").Text);
				Assert.Equal("Search All", Part<CrSearchBox>(window, "SearchBox").CueText);
				window.Close();
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task The_sidebar_button_the_menu_state_and_the_panel_follow_one_setting()
		{
			await Ui.Run(() =>
			{
				var (window, vm) = NewWindow();
				window.Show();
				Layout(window);
				var side = Part<CrSizableContainer>(window, "SidebarPanel");
				var button = Part<CrToolButton>(window, "SidebarButton");
				bool before = CrSizableContainer.EnableAnimation;
				CrSizableContainer.EnableAnimation = false;
				try
				{
					vm.ToggleSidebarCommand.Execute(null);
					Layout(window);
					Assert.False(vm.Sidebar.IsVisible);
					Assert.False(side.IsExpanded);
					Assert.False(button.IsChecked);
					Assert.Equal(0, side.CurrentExtent);
					button.IsChecked = true; // the button (CheckOnClick) writes the setting back
					Layout(window);
					Assert.True(vm.Sidebar.IsVisible);
					Assert.Equal(252, side.Bounds.Width);
				}
				finally
				{
					CrSizableContainer.EnableAnimation = before;
				}
				window.Close();
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task The_quick_search_box_doubles_while_it_has_the_focus_and_shows_the_clear_button_with_text()
		{
			await Ui.Run(() =>
			{
				var (window, vm) = NewWindow();
				window.Show();
				Layout(window);
				var box = Part<CrSearchBox>(window, "SearchBox");
				box.Box.Focus();
				Layout(window);
				Assert.Equal(260, box.Bounds.Width);
				box.Box.Text = "lester";
				Assert.Equal("lester", vm.Browser.SearchText);
				vm.Browser.SearchText = "x"; // from the model
				Assert.Equal("x", box.Box.Text);
				Part<Slider>(window, "SizeSlider").Focus();
				Layout(window);
				Assert.Equal(130, box.Bounds.Width);
				window.Close();
				return Task.CompletedTask;
			});
		}

		[Theory]
		[InlineData("Light")]
		[InlineData("Dark")]
		public async Task The_tree_row_has_the_original_icon_and_a_green_counter_with_white_text(string appearance)
		{
			await InAppearance(appearance, () =>
			{
				var pill = new CrGaugePill { Text = "50" };
				var host = new Border { Child = pill, Width = 60, Height = 20 };
				host.Background = null;
				Window shell = ShowIn(host, 80, 40);
				Dpi.Settle(shell);
				Assert.Equal(GaugeKind.Total, pill.Kind);
				Raster frame = Dpi.Capture(shell, "r1-gauge-" + appearance);
				SKColor green = Token(ThemeTokens.Names.GaugeTotal, appearance);
				// the middle of the pill is the green of the counter blended with the surface (alpha between 128 and 192)
				SKColor surface = Token(ThemeTokens.Names.Control, appearance);
				Point o = Dpi.Origin(pill, shell);
				SKColor middleTop = frame[(int)(o.X + 2), (int)(o.Y + 2)];
				Assert.True(Math.Abs(middleTop.Green - (green.Green * 0.75 + surface.Green * 0.25)) < 40, "pill colour " + Describe(middleTop));
				shell.Close();
			});
		}

		[Fact]
		public void The_rows_of_the_tree_use_the_images_of_the_original_list_types()
		{
			Assert.Equal("Library", SidebarNode.IconNameOf("Library"));
			Assert.Equal("SearchFolder", SidebarNode.IconNameOf("Folder"));
			Assert.Equal("SearchDocument", SidebarNode.IconNameOf("Search"));
			Assert.Equal("List", SidebarNode.IconNameOf("List"));
			Assert.Equal("TempFolder", SidebarNode.IconNameOf("TempFolder"));
			foreach (string name in new[] { "Library", "SearchFolder", "SearchDocument", "List", "TempFolder", "Sidebar", "SortUp", "Search", "SmallCloseGray" })
			{
				Assert.True(Icons.Contains(name), name);
			}
		}

		[Fact]
		public void The_status_text_is_the_selection_info_of_the_original()
		{
			Assert.Equal("0 Books", SelectionInfo.Build(null, 0, 0, 0, 0, null, 0));
			Assert.Equal("1 Book", SelectionInfo.Build(null, 1, 1, 0, 0, null, 0));
			Assert.Equal("Library: 312 Books (14394 filtered) / 1.50 MB - 2 selected / 300.00 kB",
				SelectionInfo.Build("Library", 312, 14706, 1572864, 2, null, 307200).Replace(',', '.'));
			Assert.Equal("Alphas: 3 Books - /books/a.cbz", SelectionInfo.Build("Alphas", 3, 3, 0, 1, "/books/a.cbz", 0));
			Assert.Equal("3 Books - 1 selected", SelectionInfo.Build(null, 3, 3, 0, 1, null, 0));
		}

		[Fact]
		public void Labels_come_from_the_original_texts_without_the_mnemonic()
		{
			Assert.Equal("Add Folder to Library...", UiText.Get("MainForm", "miAddFolderToLibrary", "&Add Folder to Library..."));
			Assert.Equal("File", UiText.Get("MainForm", "fileMenu", "&File"));
			Assert.Equal("Fish & Chips", UiText.Get("NoSuchForm", "k", "Fish && Chips"));
			Assert.Equal("File › Add Folder to Library...", MainViewModel.AddFolderMenuPath);
		}

		[Fact]
		public async Task The_menu_bar_has_the_original_menus_in_order_with_the_ledger_shortcuts()
		{
			await Ui.Run(() =>
			{
				var (window, vm) = NewWindow();
				NativeMenu bar = MainMenu.BuildMenuBar(vm);
				string[] titles = bar.Items.OfType<NativeMenuItem>().Select(i => i.Header).ToArray();
				Assert.Equal(new[] { "File", "Edit", "Browse", "Read", "Display", "Window", "Help" }, titles);
				NativeMenuItem Item(string menu, string header) => bar.Items.OfType<NativeMenuItem>().Single(m => m.Header == menu).Menu.Items.OfType<NativeMenuItem>().Single(i => i.Header == header);
				Assert.Equal(new KeyGesture(Key.A, KeyModifiers.Meta | KeyModifiers.Shift), Item("File", "Add Folder to Library...").Gesture);
				Assert.Equal(new KeyGesture(Key.F6, KeyModifiers.Shift), Item("Browse", "Sidebar").Gesture);
				Assert.Equal(new KeyGesture(Key.F11), Item("Display", "Full Screen").Gesture);
				Assert.Equal(new KeyGesture(Key.F1), Item("Help", "ComicRack Documentation...").Gesture);
				Assert.Equal(KeyModifiers.Meta | KeyModifiers.Control | KeyModifiers.Alt, MainMenu.PerfModifiers); // Option+Cmd+P is Previous Book (MR.05)
				Assert.Equal(Key.P, MainMenu.PerfKey);
				// the aliases of the ledger: Full Screen Ctrl+Cmd+F, Help Cmd+?
				var aliases = MainMenu.Aliases(vm);
				Assert.Contains(aliases, a => a.Gesture.Equals(new KeyGesture(Key.F, KeyModifiers.Meta | KeyModifiers.Control)));
				Assert.Contains(aliases, a => a.Gesture.Equals(new KeyGesture(Key.OemQuestion, KeyModifiers.Meta | KeyModifiers.Shift)));
				// Display > Full Screen is a check item that follows the window
				NativeMenuItem full = Item("Display", "Full Screen");
				Assert.False(full.IsChecked);
				vm.IsFullScreen = true;
				Assert.True(full.IsChecked);
				window.Close();
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task The_app_menu_gets_About_with_the_app_name_and_a_disabled_Settings_item_in_place_of_the_default_About()
		{
			await Ui.Run(() =>
			{
				var (window, vm) = NewWindow();
				// the menu Avalonia.Native makes: About Avalonia, Services, Hide, Hide Others, Quit
				var menu = new NativeMenu
				{
					new NativeMenuItem("About Avalonia"),
					new NativeMenuItemSeparator(),
					new NativeMenuItem("Hide Others") { Gesture = new KeyGesture(Key.Q, KeyModifiers.Meta | KeyModifiers.Alt) },
					new NativeMenuItem("Quit") { Gesture = new KeyGesture(Key.Q, KeyModifiers.Meta) },
				};
				MainMenu.FillAppMenu(menu, vm);
				string[] headers = menu.Items.OfType<NativeMenuItem>().Where(i => !(i is NativeMenuItemSeparator)).Select(i => i.Header).ToArray();
				Assert.Equal(new[] { "About ComicRack CE (MVP)", "Settings…", "Hide Others", "Quit" }, headers);
				Assert.Equal(1, headers.Count(h => h.StartsWith("About ")));
				NativeMenuItem settings = menu.Items.OfType<NativeMenuItem>().Single(i => i.Header == "Settings…");
				Assert.False(settings.IsEnabled);
				Assert.Equal(new KeyGesture(Key.OemComma, KeyModifiers.Meta), settings.Gesture);
				Assert.Equal(new KeyGesture(Key.H, KeyModifiers.Meta | KeyModifiers.Alt), menu.Items.OfType<NativeMenuItem>().Single(i => i.Header == "Hide Others").Gesture);
				window.Close();
				return Task.CompletedTask;
			});
		}
	}
}
