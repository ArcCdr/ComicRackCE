using System;
using cYo.Projects.ComicRack.Engine;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using ComicRack.Mac.Browser;
using ComicRack.Mac.Controls;
using ComicRack.Mac.Host;
using ComicRack.Mac.Tests.Browser;
using ComicRack.Mac.Reader;
using ComicRack.Mac.Services;
using ComicRack.Mac.ViewModels;
using ComicRack.Mac.Views;
using cYo.Projects.ComicRack.Engine.Display;
using Xunit;

namespace ComicRack.Mac.Tests.Settings
{
	/// <summary>Where a saved window goes when the screens are not the ones it was saved on (P5.1). Pure arithmetic: no window, no clock.</summary>
	public class WindowPlacementTests
	{
		private static readonly ScreenArea Main = new ScreenArea(0, 25, 1920, 1170, 1);
		private static readonly ScreenArea Retina = new ScreenArea(0, 38, 3024, 1860, 2);
		private static readonly ScreenArea Left = new ScreenArea(-1440, 0, 1440, 875, 1);

		[Fact]
		public void Nothing_saved_or_an_unusable_size_gives_no_frame()
		{
			Assert.Null(WindowPlacement.Correct(null, new[] { Main }));
			Assert.Null(WindowPlacement.Correct(new WindowFrame(10, 40, 0, 500), new[] { Main }));
			Assert.Null(WindowPlacement.Correct(new WindowFrame(10, 40, double.NaN, 500), new[] { Main }));
		}

		[Fact]
		public void A_frame_that_fits_on_a_screen_is_kept_exactly()
		{
			var saved = new WindowFrame(120, 90, 1100, 720);
			Assert.True(saved.Equals(WindowPlacement.Correct(saved, new[] { Main })));
			var zoomed = new WindowFrame(120, 90, 1100, 720, maximized: true);
			Assert.True(zoomed.Equals(WindowPlacement.Correct(zoomed, new[] { Main })));
		}

		[Fact]
		public void A_frame_whose_monitor_is_gone_is_centred_on_the_screen_that_is_left()
		{
			var saved = new WindowFrame(-1300, 100, 1000, 700); // on the monitor to the left
			WindowFrame onLeft = WindowPlacement.Correct(saved, new[] { Left, Main });
			Assert.True(saved.Equals(onLeft));
			WindowFrame moved = WindowPlacement.Correct(saved, new[] { Main });
			Assert.Equal(1000, moved.Width);
			Assert.Equal(700, moved.Height);
			Assert.Equal(460, moved.X); // (1920 - 1000) / 2
			Assert.Equal(25 + (int)Math.Round((1170 - (700 + WindowPlacement.TitleBar)) / 2), moved.Y);
		}

		[Fact]
		public void A_window_below_or_beyond_every_screen_comes_back()
		{
			foreach (var saved in new[] { new WindowFrame(5000, 100, 800, 600), new WindowFrame(100, 4000, 800, 600), new WindowFrame(-9000, -9000, 800, 600) })
			{
				WindowFrame moved = WindowPlacement.Correct(saved, new[] { Main });
				Assert.InRange(moved.X, 0, 1920 - 800);
				Assert.InRange(moved.Y, 25, 25 + 1170 - 600);
			}
		}

		[Fact]
		public void A_frame_larger_than_the_screen_is_shrunk_to_it()
		{
			WindowFrame moved = WindowPlacement.Correct(new WindowFrame(0, 25, 4000, 3000), new[] { Main });
			Assert.Equal(1920, moved.Width);
			Assert.Equal(1170 - WindowPlacement.TitleBar, moved.Height);
		}

		[Fact]
		public void A_hidden_title_bar_is_not_kept_but_a_window_partly_off_the_edge_is()
		{
			// only 40 pixels of the title bar show at the right edge: out of reach
			WindowFrame lost = WindowPlacement.Correct(new WindowFrame(1880, 200, 900, 600), new[] { Main });
			Assert.True(lost.X + 900 <= 1920 && lost.X >= 0);
			// 400 of 900 pixels show: the user put it there
			var edge = new WindowFrame(1520, 200, 900, 600);
			Assert.True(edge.Equals(WindowPlacement.Correct(edge, new[] { Main })));
			// the top above the menu bar is pulled down
			WindowFrame top = WindowPlacement.Correct(new WindowFrame(100, 15, 900, 600), new[] { Main });
			Assert.Equal(25, top.Y);
		}

		[Fact]
		public void A_frame_larger_than_the_screen_is_shrunk_and_moved_so_that_the_whole_window_is_on_it()
		{
			var small = new ScreenArea(0, 25, 1440, 875, 1);
			WindowFrame moved = WindowPlacement.Correct(new WindowFrame(100, 300, 2400, 1300), new[] { small });
			Assert.Equal(1440, moved.Width);
			Assert.Equal(875 - WindowPlacement.TitleBar, moved.Height);
			Assert.Equal(0, moved.X);
			Assert.Equal(25, moved.Y);
			AssertOnScreen(moved, small);

			// on a 2x screen the pixels of the frame are twice its units
			WindowFrame retina = WindowPlacement.Correct(new WindowFrame(400, 700, 2400, 1300), new[] { Retina });
			Assert.Equal(1512, retina.Width);
			Assert.Equal(0, retina.X);
			Assert.Equal(38, retina.Y);
			AssertOnScreen(retina, Retina);
		}

		[Fact]
		public void A_window_that_is_made_larger_to_reach_its_smallest_size_is_moved_back_onto_the_screen()
		{
			// 120 pixels of the title bar show at the right edge, so the window is where the user left it; at 640 x 400 it would end far beyond the screen
			WindowFrame moved = WindowPlacement.Correct(new WindowFrame(1800, 800, 200, 100), new[] { Main }, 640, 400);
			Assert.Equal(640, moved.Width);
			Assert.Equal(400, moved.Height);
			AssertOnScreen(moved, Main);
			Assert.Equal(1920 - 640, moved.X);
			Assert.Equal(25 + 1170 - (400 + (int)WindowPlacement.TitleBar), moved.Y);
		}

		private static void AssertOnScreen(WindowFrame frame, ScreenArea screen)
		{
			Assert.InRange(frame.X, screen.X, screen.Right);
			Assert.InRange(frame.X + frame.Width * screen.Scale, screen.X, screen.Right);
			Assert.InRange(frame.Y, screen.Y, screen.Bottom);
			Assert.InRange(frame.Y + (frame.Height + WindowPlacement.TitleBar) * screen.Scale, screen.Y, screen.Bottom);
		}

		[Fact]
		public void The_scale_of_the_screen_turns_pixels_into_device_independent_sizes()
		{
			// a 2x screen of 3024 x 1860 pixels holds a client area of 1512 x 902 units at most
			WindowFrame moved = WindowPlacement.Correct(new WindowFrame(0, 38, 2000, 2000), new[] { Retina });
			Assert.Equal(1512, moved.Width);
			Assert.Equal(1860 / 2.0 - WindowPlacement.TitleBar, moved.Height);
			var fits = new WindowFrame(200, 100, 1100, 720);
			Assert.True(fits.Equals(WindowPlacement.Correct(fits, new[] { Retina })));
		}

		[Fact]
		public void The_smallest_size_of_the_window_is_respected_when_the_screen_allows_it()
		{
			WindowFrame moved = WindowPlacement.Correct(new WindowFrame(100, 100, 200, 100), new[] { Main }, 640, 400);
			Assert.Equal(640, moved.Width);
			Assert.Equal(400, moved.Height);
		}

		[Fact]
		public void Without_screens_the_saved_frame_is_returned_as_it_is()
		{
			var saved = new WindowFrame(7000, 7000, 800, 600);
			Assert.Same(saved, WindowPlacement.Correct(saved, new ScreenArea[0]));
		}

		[Fact]
		public void A_second_window_is_moved_aside_by_the_number_of_windows_already_open()
		{
			var saved = new WindowFrame(100, 80, 900, 700);
			Assert.Same(saved, WindowPlacement.Offset(saved, 0));
			WindowFrame third = WindowPlacement.Offset(saved, 2);
			Assert.Equal(152, third.X);
			Assert.Equal(132, third.Y);
			Assert.Equal(900, third.Width);
		}
	}

	/// <summary>The saved preferences reach the window state, the browser, the thumbnail options and the readers, and every change is written back (P5.1).</summary>
	public class PreferencesTests
	{
		private static void Layout(Window window)
		{
			for (int i = 0; i < 4; i++)
			{
				Dispatcher.UIThread.RunJobs();
				window.UpdateLayout();
			}
		}

		private static (MainWindow window, MainViewModel vm) NewWindow(AppPreferences preferences, ILibraryDialogs dialogs = null)
		{
			var service = new HostService(AppOptions.Parse(new[] { "--data-dir", Path.Combine(Path.GetTempPath(), "crmac-never-started") }));
			var vm = new MainViewModel(new NullShell(), service, dialogs ?? new FakeDialogs(), null, preferences);
			return (new MainWindow { DataContext = vm }, vm);
		}

		[Fact]
		public void The_library_view_starts_from_the_saved_state_and_writes_every_change_back()
		{
			var saved = new AppPreferences { SidebarVisible = false, SidebarWidth = 310, ThumbnailSize = 144, SortBy = "Title", SortDescending = true, ShowToolTips = true, UpdateComicFiles = true };
			var service = new HostService(AppOptions.Parse(new[] { "--data-dir", Path.Combine(Path.GetTempPath(), "crmac-never-started") }));
			var vm = new MainViewModel(new NullShell(), service, new FakeDialogs(), null, saved);
			Assert.False(vm.Sidebar.IsVisible);
			Assert.Equal(310, vm.Sidebar.Width);
			Assert.Equal(144, vm.Browser.ThumbnailSize);
			Assert.Equal(BookSort.Title, vm.Browser.Sort);
			Assert.True(vm.Browser.SortDescending);
			Assert.True(vm.Browser.ThumbnailOptions.ShowToolTips);
			Assert.True(vm.Browser.ThumbnailOptions.UpdateComicFiles);

			vm.Sidebar.IsVisible = true;
			vm.Sidebar.Width = 280;
			vm.Browser.ThumbnailSize = 220;
			vm.Browser.SortIndex = (int)BookSort.Added;
			vm.Browser.SortDescending = true;
			vm.Browser.ThumbnailOptions.DogEarThumbnails = false;
			Assert.True(saved.SidebarVisible);
			Assert.Equal(280, saved.SidebarWidth);
			Assert.Equal(220, saved.ThumbnailSize);
			Assert.Equal("Added", saved.SortBy);
			Assert.True(saved.SortDescending);
			Assert.False(saved.DogEarThumbnails);

			// the other way: the preferences change the browser's options (Preferences > Behavior, P5.22a)
			saved.FadeInThumbnails = false;
			Assert.False(vm.Browser.ThumbnailOptions.FadeInThumbnails);
		}

		[Fact]
		public void A_saved_value_that_cannot_be_used_falls_back_to_the_default()
		{
			var saved = new AppPreferences { ThumbnailSize = 5000, SortBy = "NoSuchSort" };
			var service = new HostService(AppOptions.Parse(new[] { "--data-dir", Path.Combine(Path.GetTempPath(), "crmac-never-started") }));
			var vm = new MainViewModel(new NullShell(), service, new FakeDialogs(), null, saved);
			Assert.Equal(BrowserViewModel.MaxThumbnailSize, vm.Browser.ThumbnailSize);
			Assert.Equal(BookSort.Series, vm.Browser.Sort);
			Assert.Equal(ImageFitMode.Fit, ReaderOptions.ParseFit("Original"));
			Assert.Equal(ImageFitMode.Fit, ReaderOptions.ParseFit("nonsense"));
			Assert.Equal(ImageFitMode.FitHeight, ReaderOptions.ParseFit("FitHeight"));
			Assert.Equal(PageLayoutMode.Single, ReaderOptions.ParseMode("Continuous"));
			Assert.Equal(PageLayoutMode.DoubleAdaptive, ReaderOptions.ParseMode("DoubleAdaptive"));
		}

		[Fact]
		public void Without_preferences_nothing_is_kept()
		{
			var service = new HostService(AppOptions.Parse(new[] { "--data-dir", Path.Combine(Path.GetTempPath(), "crmac-never-started") }));
			var vm = new MainViewModel(new NullShell(), service, new FakeDialogs());
			vm.Sidebar.Width = 400;
			vm.Browser.ThumbnailSize = 100;
			vm.Browser.SortDescending = true;
			Assert.Equal(100, vm.Browser.ThumbnailSize); // the view model works with what it is given
			Assert.Null(vm.LastFolder);
			// but nothing is kept: a second view model starts from the defaults, not from what the first one was given
			var other = new MainViewModel(new NullShell(), service, new FakeDialogs());
			Assert.Equal(BrowserViewModel.DefaultThumbnailSize, other.Browser.ThumbnailSize);
			Assert.Equal(AppPreferences.DefaultSidebarWidth, other.Sidebar.Width);
			Assert.False(other.Browser.SortDescending);
			Assert.Equal(BookSort.Series, other.Browser.Sort);
		}

		[Fact]
		public void The_reader_starts_from_the_saved_overlays_fit_layout_and_direction()
		{
			var saved = new AppPreferences
			{
				ShowCurrentPageOverlay = false,
				ShowNavigationOverlay = false,
				NavigationOverlayOnTop = true,
				CurrentPageShowsName = true,
				HideCursorFullScreen = false,
				ReaderFit = "FitWidth",
				ReaderLayout = "Double",
				RightToLeftDefault = true,
			};
			ReaderOptions options = ReaderOptions.From(saved);
			Assert.False(options.ShowCurrentPageOverlay);
			Assert.False(options.ShowNavigationOverlay);
			Assert.True(options.NavigationOverlayOnTop);
			Assert.True(options.CurrentPageShowsName);
			Assert.False(options.HideCursorFullScreen);
			Assert.Equal(ImageFitMode.FitWidth, options.Fit);
			Assert.Equal(PageLayoutMode.Double, options.Mode);
			Assert.True(options.RightToLeft);
			Assert.True(options.ShowStatusOverlay); // an untouched option keeps the original's default
			Assert.Equal(ImageFitMode.Fit, ReaderOptions.From(null).Fit);
		}

		[Fact]
		public void The_folder_picker_starts_in_the_last_folder_that_still_exists()
		{
			using (var temp = new TempDir())
			{
				string folder = temp.Combine("comics");
				Directory.CreateDirectory(folder);
				var saved = new AppPreferences { LastFolder = folder };
				var service = new HostService(AppOptions.Parse(new[] { "--data-dir", Path.Combine(Path.GetTempPath(), "crmac-never-started") }));
				var dialogs = new FakeDialogs { FolderToPick = null };
				var vm = new MainViewModel(new NullShell(), service, dialogs, null, saved);
				Assert.Equal(folder, vm.LastFolder);
				vm.OpenFolderCommand.Execute(null);
				Assert.Equal(folder, dialogs.LastStartFolder);

				saved.LastFolder = temp.Combine("gone");
				Assert.Null(vm.LastFolder);
			}
		}

		[Theory]
		[InlineData(double.NaN, 252)]
		[InlineData(double.PositiveInfinity, 252)]
		[InlineData(double.NegativeInfinity, 252)]
		[InlineData(-50, 26)]
		[InlineData(0, 26)]
		[InlineData(1e9, 1000)]
		[InlineData(300, 300)]
		public void A_saved_side_panel_width_that_is_not_usable_is_clamped_on_restore(double savedWidth, double expected)
		{
			var saved = new AppPreferences { SidebarWidth = savedWidth };
			var service = new HostService(AppOptions.Parse(new[] { "--data-dir", Path.Combine(Path.GetTempPath(), "crmac-never-started") }));
			var vm = new MainViewModel(new NullShell(), service, new FakeDialogs(), null, saved);
			Assert.Equal(expected, vm.Sidebar.Width);
			Assert.Equal(savedWidth, saved.SidebarWidth); // restoring does not rewrite the saved value
		}

		[Fact]
		public async Task The_side_panel_shows_the_saved_width_and_a_drag_of_the_grip_is_kept()
		{
			await Ui.Run(() =>
			{
				var saved = new AppPreferences { SidebarWidth = 300 };
				var (window, vm) = NewWindow(saved);
				window.Show();
				Layout(window);
				var panel = window.FindControl<CrSizableContainer>("SidebarPanel");
				Assert.Equal(300, panel.Bounds.Width);
				panel.SetExtent(340); // what a drag of the grip does
				Layout(window);
				Assert.Equal(340, vm.Sidebar.Width);
				Assert.Equal(340, saved.SidebarWidth);
				window.Close();
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task A_side_panel_that_starts_hidden_is_also_out_of_reach_of_the_keyboard()
		{
			await Ui.Run(() =>
			{
				bool before = CrSizableContainer.EnableAnimation;
				CrSizableContainer.EnableAnimation = false;
				try
				{
					var saved = new AppPreferences { SidebarVisible = false };
					var (window, vm) = NewWindow(saved);
					window.Show();
					Layout(window);
					var panel = window.FindControl<CrSizableContainer>("SidebarPanel");
					Assert.False(panel.IsExpanded);
					Assert.Equal(0, panel.CurrentExtent);
					Assert.False(panel.Child.IsVisible);
					vm.ToggleSidebarCommand.Execute(null);
					Layout(window);
					Assert.True(panel.Child.IsVisible);
					vm.ToggleSidebarCommand.Execute(null);
					Layout(window);
					Assert.False(panel.Child.IsVisible);
					window.Close();
				}
				finally
				{
					CrSizableContainer.EnableAnimation = before;
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task A_panel_that_slides_shut_stays_in_reach_until_the_slide_ends()
		{
			await Ui.Run(() =>
			{
				var own = new Border { IsVisible = false };
				var panel = new CrSizableContainer { Grip = GripPosition.Left, ExpandedExtent = 100, Width = 120, Child = new Border() };
				var window = new Window { Content = panel, Width = 300, Height = 200 };
				window.Show();
				Layout(window);
				bool before = CrSizableContainer.EnableAnimation;
				CrSizableContainer.EnableAnimation = true;
				try
				{
					panel.IsExpanded = false;
					Assert.True(panel.IsSliding);
					Assert.True(panel.Child.IsVisible);
					panel.AdvanceAnimation(TimeSpan.FromMilliseconds(500));
					Assert.False(panel.IsSliding);
					Assert.False(panel.Child.IsVisible);
					panel.IsExpanded = true;
					Assert.True(panel.Child.IsVisible);
					panel.AdvanceAnimation(TimeSpan.FromMilliseconds(500));
					Assert.True(panel.Child.IsVisible);
				}
				finally
				{
					CrSizableContainer.EnableAnimation = before;
				}
				// a child that the owner hid itself stays hidden
				panel.Child = own;
				panel.AnimateExpand = false;
				panel.IsExpanded = false;
				panel.IsExpanded = true;
				Assert.False(own.IsVisible);
				window.Close();
				return Task.CompletedTask;
			});
		}
	}

	/// <summary>The frame of a real (headless) window: restored before it shows, followed once it is open.</summary>
	public class WindowFrameTrackerTests
	{
		private static void Layout(Window window)
		{
			for (int i = 0; i < 4; i++)
			{
				Dispatcher.UIThread.RunJobs();
				window.UpdateLayout();
			}
		}

		[Fact]
		public async Task A_saved_frame_is_applied_before_the_window_shows_and_moves_and_resizes_are_reported()
		{
			await Ui.Run(() =>
			{
				var window = new Window { Width = 500, Height = 400, MinWidth = 200, MinHeight = 150 };
				WindowFrame restored = WindowFrameTracker.Restore(window, new WindowFrame(60, 70, 640, 480));
				Assert.NotNull(restored);
				Assert.Equal(640, window.Width);
				Assert.Equal(480, window.Height);
				Assert.Equal(WindowStartupLocation.Manual, window.WindowStartupLocation);
				var written = new System.Collections.Generic.List<WindowFrame>();
				using (WindowFrameTracker.Attach(window, written.Add, restored))
				{
					window.Show();
					Layout(window);
					Assert.Empty(written); // the frame it opened with is the saved one: nothing to say yet

					window.Width = 700;
					window.Height = 520;
					Layout(window);
					Assert.Equal(700, written.Last().Width);
					Assert.Equal(520, written.Last().Height);

					window.Position = new PixelPoint(90, 110);
					Layout(window);
					Assert.Equal(new PixelPoint(90, 110), new PixelPoint(written.Last().X, written.Last().Y));
				}
				int count = written.Count;
				window.Width = 760;
				Layout(window);
				Assert.Equal(count, written.Count); // detached: nothing more is reported
				window.Close();
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task Without_a_saved_frame_the_window_keeps_what_its_xaml_asks_for()
		{
			await Ui.Run(() =>
			{
				var window = new Window { Width = 500, Height = 400, WindowStartupLocation = WindowStartupLocation.CenterScreen };
				Assert.Null(WindowFrameTracker.Restore(window, null));
				Assert.Equal(500, window.Width);
				Assert.Equal(WindowStartupLocation.CenterScreen, window.WindowStartupLocation);
				window.Close();
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task A_window_that_opened_moved_aside_or_untouched_does_not_become_the_saved_frame_until_the_user_changes_it()
		{
			await Ui.Run(() =>
			{
				var window = new Window { Width = 500, Height = 400 };
				WindowFrame restored = WindowFrameTracker.Restore(window, new WindowFrame(60, 70, 640, 480), offsetCount: 1);
				Assert.Equal(60 + 26, restored.X);
				var written = new System.Collections.Generic.List<WindowFrame>();
				using (WindowFrameTracker.Attach(window, written.Add, restored))
				{
					window.Show();
					Layout(window);
					Assert.Empty(written);
					window.Width = 650;
					Layout(window);
					Assert.NotEmpty(written);
				}
				window.Close();
				return Task.CompletedTask;
			});
		}
	}

	/// <summary>The whole path through the host service: Settings.xml in the data folder, written at quit, read back at the next launch.</summary>
	public class SettingsFileFlowTests
	{
		[Fact]
		public async Task What_the_app_keeps_is_written_at_quit_and_restored_at_the_next_launch()
		{
			using (var temp = new TempDir())
			{
				string data = temp.Combine("app");
				using (Launch first = await Launch.Start(data))
				{
					AppPreferences p = first.Service.Preferences.Current;
					first.ViewModel.Sidebar.Width = 333;
					first.ViewModel.Sidebar.IsVisible = false;
					first.ViewModel.Browser.ThumbnailSize = 177;
					first.ViewModel.Browser.SortIndex = (int)BookSort.Title;
					p.LibraryWindow = new WindowFrame(41, 52, 1001, 702);
					p.ReaderWindow = new WindowFrame(11, 22, 801, 902);
					p.ShowToolTips = true;
					p.ThumbCacheSizeMB = 321;
					first.ViewModel.Browser.ThumbnailOptions.NumericRatingThumbnails = false;
					first.Quit();
				}
				string file = Path.Combine(data, "Data", SettingsStore.FileName);
				Assert.True(File.Exists(file));
				Assert.Empty(Directory.GetFiles(Path.Combine(data, "Data"), "*.tmp"));

				using (Launch second = await Launch.Start(data))
				{
					AppPreferences p = second.Service.Preferences.Current;
					Assert.Null(second.Service.Preferences.LoadNote);
					Assert.Equal(333, second.ViewModel.Sidebar.Width);
					Assert.False(second.ViewModel.Sidebar.IsVisible);
					Assert.Equal(177, second.ViewModel.Browser.ThumbnailSize);
					Assert.Equal(BookSort.Title, second.ViewModel.Browser.Sort);
					Assert.True(new WindowFrame(41, 52, 1001, 702).Equals(p.LibraryWindow));
					Assert.True(new WindowFrame(11, 22, 801, 902).Equals(p.ReaderWindow));
					Assert.True(second.ViewModel.Browser.ThumbnailOptions.ShowToolTips);
					Assert.False(second.ViewModel.Browser.ThumbnailOptions.NumericRatingThumbnails);
					// the Core's view of the caches is the same value
					Assert.Equal(321, second.Service.Host.Settings.ThumbCacheSizeMB);
				}
			}
		}

		[Fact]
		public async Task A_damaged_settings_file_starts_the_app_with_the_defaults_and_keeps_a_copy()
		{
			using (var temp = new TempDir())
			{
				string data = temp.Combine("app");
				Directory.CreateDirectory(Path.Combine(data, "Data"));
				string file = Path.Combine(data, "Data", SettingsStore.FileName);
				File.WriteAllText(file, "<Settings><SidebarWidth>wide</SidebarWidth>");
				int errorsBefore = AppLog.ErrorCount;
				using (Launch launch = await Launch.Start(data))
				{
					Assert.NotNull(launch.Service.Preferences.LoadNote);
					Assert.Equal(AppPreferences.DefaultSidebarWidth, launch.ViewModel.Sidebar.Width);
					Assert.True(launch.ViewModel.Sidebar.IsVisible);
					Assert.Equal("<Settings><SidebarWidth>wide</SidebarWidth>", File.ReadAllText(file + ".bad"));
					Assert.Equal(errorsBefore, AppLog.ErrorCount); // a damaged file is no error of the app
					launch.ViewModel.Sidebar.Width = 280;
					launch.Quit();
				}
				Assert.Equal(280, SettingsStore.Load(file, out string note).SidebarWidth);
				Assert.Null(note);
			}
		}

		[Fact]
		public async Task A_book_folder_that_is_added_is_remembered_for_the_picker()
		{
			using (var temp = new TempDir())
			{
				string data = temp.Combine("app");
				string books = temp.Combine("books");
				Directory.CreateDirectory(books);
				using (Launch first = await Launch.Start(data))
				{
					await first.ViewModel.ScanFolderAsync(books);
					Assert.Equal(books, first.Service.Preferences.Current.LastFolder);
					first.Quit();
				}
				using (Launch second = await Launch.Start(data))
				{
					Assert.Equal(books, second.ViewModel.LastFolder);
				}
			}
		}
	}

	/// <summary>The readers of the app keep their fit, page layout, direction and frame in the preferences, and the next reader starts from them (needs the stand-in books).</summary>
	public class ReaderPreferencesTests
	{
		private static async Task WaitFor(Func<bool> condition, string what, int timeoutMs = 20000)
		{
			var watch = System.Diagnostics.Stopwatch.StartNew();
			while (!condition())
			{
				if (watch.ElapsedMilliseconds > timeoutMs)
				{
					throw new TimeoutException("Not reached: " + what);
				}
				await Task.Delay(5);
			}
		}

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

		private static async Task<ReaderViewModel> Open(Launch launch, ReaderManager manager, int index)
		{
			launch.ViewModel.Browser.SelectedIndex = index;
			ComicBook book = launch.ViewModel.Browser.SelectedItem.Book;
			launch.ViewModel.Browser.OpenSelected();
			await WaitFor(() => ReferenceEquals(manager.Current?.Book, book), "reader tab");
			ReaderViewModel model = manager.CurrentModel;
			await WaitFor(() => model.IsOpen && model.Scene != null, "first page shown");
			return model;
		}

		[FixtureFact("BD-reader")]
		public async Task Fit_and_layout_of_a_reader_are_kept_and_the_next_reader_starts_from_them()
		{
			using (new RealLibraryGuard())
			using (var temp = new TempDir())
			{
				await Ui.Run(async () =>
				{
					string data = temp.Combine("app");
					using (var launch = await Launch.Start(data, svc => new ReaderManager(svc, svc.Preferences.Current)))
					{
						AppPreferences prefs = launch.Service.Preferences.Current;
						var manager = (ReaderManager)launch.ViewModel.Readers;
						await launch.ViewModel.ScanFolderAsync(MakeLibrary(temp));
						await FixtureLibrary.WaitForGrid(launch.ViewModel, 3);
						Assert.Equal("Fit", prefs.ReaderFit);
						Assert.Equal("Single", prefs.ReaderLayout);

						ReaderViewModel first = await Open(launch, manager, 0);
						first.FitWidth();
						first.TwoPageLayout();
						Assert.Equal("FitWidth", prefs.ReaderFit);
						Assert.Equal("Double", prefs.ReaderLayout);

						ReaderViewModel second = await Open(launch, manager, 1);
						Assert.Equal(ImageFitMode.FitWidth, second.Fit);
						Assert.Equal(PageLayoutMode.Double, second.LayoutMode);
						launch.Quit();
					}
					// and after a relaunch the first reader starts like the last one ended
					using (var again = await Launch.Start(data, svc => new ReaderManager(svc, svc.Preferences.Current)))
					{
						var manager = (ReaderManager)again.ViewModel.Readers;
						await FixtureLibrary.WaitForGrid(again.ViewModel, 3);
						ReaderViewModel model = await Open(again, manager, 0);
						Assert.Equal(ImageFitMode.FitWidth, model.Fit);
						Assert.Equal(PageLayoutMode.Double, model.LayoutMode);
						again.Quit();
					}
				});
			}
		}
	}
}
