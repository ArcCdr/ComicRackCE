using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using ComicRack.Mac.Controls;
using ComicRack.Mac.Services;
using ComicRack.Mac.ViewModels;
using ComicRack.Mac.Views;
using cYo.Projects.ComicRack.Engine;
using Xunit;

namespace ComicRack.Mac.Tests.Browser
{
	/// <summary>P5.5: the menu in the main window: a right click on a book, the empty area, the Edit menu, the quick review after reading, the dialogs.</summary>
	public class ContextMenuWindowTests
	{
		private static ComicBook Book(string series, int number)
		{
			return new ComicBook { Series = series, Number = number.ToString(), Title = series + " " + number, FilePath = "/nowhere/" + series + number + ".cbz", PageCount = 10, AddedTime = DateTime.Now.AddDays(-100) };
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

		private static void Tick(Window window)
		{
			for (int i = 0; i < 3; i++)
			{
				Dispatcher.UIThread.RunJobs();
				window.UpdateLayout();
				AvaloniaHeadlessPlatform.ForceRenderTimerTick();
			}
		}

		private static async Task<MainWindow> ShowLibrary(Launch launch, int books)
		{
			MainViewModel vm = launch.ViewModel;
			cYo.Projects.ComicRack.Engine.Database.ComicDatabase db = launch.Service.Host.DatabaseManager.Database;
			for (int i = 1; i <= books; i++)
			{
				db.Books.Add(Book("Alpha", i));
			}
			await vm.Sidebar.ReloadAsync(launch.Service.Host);
			await vm.WhenLibraryShownAsync();
			Dispatcher.UIThread.RunJobs();
			vm.RequestLiveRefresh();
			await vm.WhenLiveRefreshedAsync();
			await WaitFor(() => vm.Browser.Items.Length == books && !vm.Browser.IsLoading, "the grid shows the books");
			var window = new MainWindow { DataContext = vm, Width = 1100, Height = 720 };
			window.Show();
			Tick(window);
			return window;
		}

		[Fact]
		public async Task The_window_gives_the_grid_the_menu_and_the_menu_follows_the_selection_when_it_opens()
		{
			await Ui.Run(async () =>
			{
				using (var temp = new TempDir())
				using (Launch launch = await Launch.Start(temp.Combine("app")))
				{
					MainWindow window = await ShowLibrary(launch, 6);
					BookGridView grid = window.FindControl<BookGridView>("BookGrid");
					BrowserContextMenu menu = window.BookContextMenu;
					Assert.NotNull(menu);
					Assert.Same(menu.Flyout, grid.ContextFlyout);

					// a book is selected: the menu opens with its items on
					launch.ViewModel.Browser.SelectedIndex = 2;
					menu.Flyout.ShowAt(grid);
					Tick(window);
					Assert.True(menu.Flyout.IsOpen);
					Assert.True(menu.MenuItemOf("miRead").IsEnabled);
					Assert.True(menu.MenuItemOf("miRemove").IsEnabled);
					menu.Flyout.Hide();
					Tick(window);
					Assert.False(menu.Flyout.IsOpen);

					// nothing is selected: it is the menu of books, so it does not open
					launch.ViewModel.Browser.Selection.Clear();
					menu.Flyout.ShowAt(grid);
					Tick(window);
					Assert.False(menu.Flyout.IsOpen);
					window.Close();
				}
			});
		}

		[Fact]
		public async Task A_right_click_on_a_book_selects_it_and_opens_the_menu()
		{
			await Ui.Run(async () =>
			{
				using (var temp = new TempDir())
				using (Launch launch = await Launch.Start(temp.Combine("app")))
				{
					MainWindow window = await ShowLibrary(launch, 6);
					BookGridView grid = window.FindControl<BookGridView>("BookGrid");
					BrowserViewModel browser = launch.ViewModel.Browser;
					BookCell cell = grid.BoundCells().OrderBy(c => c.Index).First(c => c.Index == 1);
					Point at = cell.TranslatePoint(new Point(cell.Bounds.Width / 2, cell.Bounds.Height / 3), window).Value;
					window.MouseMove(at);
					window.MouseDown(at, MouseButton.Right);
					window.MouseUp(at, MouseButton.Right);
					Tick(window);
					Assert.Equal(new[] { 1 }, browser.Selection.SelectedIndices().ToArray());
					Assert.True(window.BookContextMenu.Flyout.IsOpen, "the context menu opened");
					window.BookContextMenu.Flyout.Hide();
					Tick(window);

					// on the empty area under the books: the selection is cleared and no menu opens
					Point empty = grid.TranslatePoint(new Point(grid.Bounds.Width - 30, grid.Bounds.Height - 10), window).Value;
					window.MouseMove(empty);
					window.MouseDown(empty, MouseButton.Right);
					window.MouseUp(empty, MouseButton.Right);
					Tick(window);
					Assert.Equal(0, browser.SelectedCount);
					Assert.False(window.BookContextMenu.Flyout.IsOpen);
					window.Close();
				}
			});
		}

		[Fact]
		public async Task The_Edit_menu_has_My_Rating_that_rates_the_selection_of_the_browser_or_the_book_of_the_reader()
		{
			await Ui.Run(async () =>
			{
				using (var temp = new TempDir())
				using (Launch launch = await Launch.Start(temp.Combine("app")))
				{
					MainWindow window = await ShowLibrary(launch, 3);
					MainViewModel vm = launch.ViewModel;
					NativeMenu bar = ComicRack.Mac.MainMenu.BuildMenuBar(vm);
					NativeMenuItem edit = bar.Items.OfType<NativeMenuItem>().Single(i => i.Header == "Edit");
					// Info..., Undo, Redo and My Rating in the original's order, a separator after Info and after Redo
					Assert.Equal(new[] { "Info...", "-", "Undo", "Redo", "-", "My Rating" },
						edit.Menu.Items.OfType<NativeMenuItem>().Select(i => i is NativeMenuItemSeparator ? "-" : i.Header).ToArray());
					NativeMenuItem rating = edit.Menu.Items.OfType<NativeMenuItem>().Single(i => i.Header == "My Rating");
					var items = rating.Menu.Items.OfType<NativeMenuItem>().Where(i => !(i is NativeMenuItemSeparator)).ToArray();
					Assert.Equal(new[] { "None", "* (1 Star)", "** (2 Stars)", "*** (3 Stars)", "**** (4 Stars)", "***** (5 Stars)", "Quick Rating and Review..." }, items.Select(i => i.Header).ToArray());
					Assert.Equal(new[] { Key.D0, Key.D1, Key.D2, Key.D3, Key.D4, Key.D5, Key.Q }, items.Select(i => i.Gesture.Key).ToArray());
					Assert.All(items, i => Assert.Equal(KeyModifiers.Alt | KeyModifiers.Shift, i.Gesture.KeyModifiers));

					// nothing selected: nothing to rate
					vm.Browser.Selection.Clear();
					Assert.False(vm.CanRate);
					Assert.False(items[3].Command.CanExecute(null));

					vm.Browser.Selection.SelectBooks(vm.Browser.Items.Take(2).Select(i => i.Book).ToArray());
					Assert.True(vm.CanRate);
					Assert.False(vm.RatingTargetsReader);
					items[3].Command.Execute(null);
					Assert.Equal(new[] { 3f, 3f }, vm.Browser.SelectedBooks.Select(b => b.Rating).ToArray());
					Assert.Equal(3f, vm.CurrentRating);
					Assert.Equal(2, vm.Browser.Items.Count(i => i.Book.Rating == 3f));

					// Quick Rating and Review works on the first selected book
					launch.Dialogs.QuickRatingToReturn = new QuickRatingResult { Review = "Fine", Rating = 4, ShowWhenRead = false };
					await vm.QuickRatingCommand.ExecuteAsync(null);
					Assert.Equal(4f, vm.Browser.SelectedBooks[0].Rating);
					Assert.Equal("Fine", vm.Browser.SelectedBooks[0].Review);
					window.Close();
				}
			});
		}
	}
}
