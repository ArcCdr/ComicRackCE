using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using ComicRack.Mac.Browser;
using ComicRack.Mac.Controls;
using ComicRack.Mac.Services;
using ComicRack.Mac.Tests.Reader;
using ComicRack.Mac.ViewModels;
using ComicRack.Mac.Views;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Engine.Database;
using Xunit;

namespace ComicRack.Mac.Tests.Editor
{
	/// <summary>P5.7a: Edit > Info..., the context menu's Info..., the page menu's Info..., and Undo and Redo of the Core (ME.01 to ME.03, BT.12, BC.04, PC.01).</summary>
	public class InfoCommandTests
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

		private static async Task<ComicBook[]> Fill(Launch launch, int count)
		{
			MainViewModel vm = launch.ViewModel;
			ComicDatabase db = launch.Service.Host.DatabaseManager.Database;
			ComicBook[] books = Enumerable.Range(1, count).Select(i => Book("Alpha", i)).ToArray();
			foreach (ComicBook book in books)
			{
				db.Books.Add(book);
			}
			await vm.Sidebar.ReloadAsync(launch.Service.Host);
			await vm.WhenLibraryShownAsync();
			Dispatcher.UIThread.RunJobs();
			vm.RequestLiveRefresh();
			await vm.WhenLiveRefreshedAsync();
			await WaitFor(() => vm.Browser.Items.Length == count && !vm.Browser.IsLoading, "the grid shows the books");
			// the database undo list starts empty for the test
			db.Undo.Clear();
			vm.RefreshUndoState();
			return books;
		}

		[Fact]
		public async Task Info_is_available_with_a_selection_and_the_dialog_gets_the_first_selected_book_and_the_list()
		{
			await Ui.Run(async () =>
			{
				using (var temp = new TempDir())
				using (Launch launch = await Launch.Start(temp.Combine("app")))
				{
					MainViewModel vm = launch.ViewModel;
					await Fill(launch, 4);
					vm.Browser.Selection.Clear();
					Assert.False(vm.CanShowInfo);
					Assert.False(vm.ShowInfoCommand.CanExecute(null));
					vm.Browser.Selection.SelectBooks(new[] { vm.Browser.Items[2].Book });
					Assert.True(vm.ShowInfoCommand.CanExecute(null));
					await vm.ShowInfoCommand.ExecuteAsync(null);
					BookInfoRequest request = Assert.Single(launch.Dialogs.BookInfos);
					Assert.Same(vm.Browser.Items[2].Book, request.Model.Current);
					Assert.Equal(vm.Browser.Items.Select(i => i.Book), request.Model.AllBooks);
					Assert.True(request.Model.HasNavigation);
					// Previous and Next select the book in the browser, as IComicBrowser.SelectComic does
					request.Model.GoNext();
					Assert.Same(vm.Browser.Items[3].Book, vm.Browser.SelectedBooks.Single());
					request.Model.GoPrevious();
					request.Model.GoPrevious();
					Assert.Same(vm.Browser.Items[1].Book, vm.Browser.SelectedBooks.Single());
				}
			});
		}

		[Fact]
		public async Task More_than_one_selected_book_opens_the_multi_edit_dialog_and_one_Undo_takes_back_what_OK_wrote_in_all_of_them()
		{
			await Ui.Run(async () =>
			{
				using (var temp = new TempDir())
				using (Launch launch = await Launch.Start(temp.Combine("app")))
				{
					MainViewModel vm = launch.ViewModel;
					ComicBook[] books = await Fill(launch, 3);
					ComicBook[] chosen = vm.Browser.Items.Take(2).Select(i => i.Book).ToArray();
					vm.Browser.Selection.SelectBooks(chosen);
					string[] was = chosen.Select(b => b.Publisher).ToArray();
					launch.Dialogs.MultipleBooksHandler = request =>
					{
						Assert.Equal(2, request.Model.Count);
						Assert.Equal(chosen, request.Model.Books);
						Assert.True(vm.IsInfoDialogOpen); // Undo, Redo and Info are off while the dialog is open
						request.Model["cbPublisher"].Text = "Multi Press";
						request.Model["txYear"].Text = "1999";
						Assert.True(request.Model.Store() >= 2); // every box the books share is checked and written too, with the value it has
						return Task.FromResult(true);
					};
					await vm.ShowInfoCommand.ExecuteAsync(null);
					Assert.Empty(launch.Dialogs.BookInfos);
					Assert.Single(launch.Dialogs.MultipleBooks);
					Assert.All(chosen, b => Assert.Equal("Multi Press", b.Publisher));
					Assert.All(chosen, b => Assert.Equal(1999, b.Year));
					Assert.NotEqual("Multi Press", books.Except(chosen).Single().Publisher); // the third book was not selected
					await WaitFor(() => vm.CanUndo, "the undo list follows the change");
					Assert.Equal("Undo: Edit multiple Books", vm.UndoText);
					vm.UndoCommand.Execute(null);
					Assert.Equal(was, chosen.Select(b => b.Publisher).ToArray());
					Assert.All(chosen, b => Assert.Equal(-1, b.Year));
					Assert.Equal("Redo: Edit multiple Books", vm.RedoText);
					vm.RedoCommand.Execute(null);
					Assert.All(chosen, b => Assert.Equal("Multi Press", b.Publisher));
				}
			});
		}

		[Fact]
		public async Task Cancel_in_the_multi_edit_dialog_writes_nothing_and_the_dialog_state_is_off_again()
		{
			await Ui.Run(async () =>
			{
				using (var temp = new TempDir())
				using (Launch launch = await Launch.Start(temp.Combine("app")))
				{
					MainViewModel vm = launch.ViewModel;
					await Fill(launch, 2);
					ComicBook[] chosen = vm.Browser.Items.Select(i => i.Book).ToArray();
					vm.Browser.Selection.SelectBooks(chosen);
					launch.Dialogs.MultipleBooksHandler = request =>
					{
						request.Model["cbPublisher"].Text = "Never written";
						return Task.FromResult(false); // Cancel: OK is what calls Store
					};
					Assert.False(await vm.ShowMultipleBooksAsync(chosen));
					Assert.All(chosen, b => Assert.NotEqual("Never written", b.Publisher));
					Assert.False(vm.IsInfoDialogOpen);
					Assert.Single(launch.Dialogs.MultipleBooks);
				}
			});
		}

		[Fact]
		public async Task One_Undo_takes_back_what_the_dialog_wrote_and_Redo_does_it_again_with_their_labels()
		{
			await Ui.Run(async () =>
			{
				using (var temp = new TempDir())
				using (Launch launch = await Launch.Start(temp.Combine("app")))
				{
					MainViewModel vm = launch.ViewModel;
					ComicBook[] books = await Fill(launch, 2);
					Assert.False(vm.CanUndo);
					Assert.False(vm.UndoCommand.CanExecute(null));
					Assert.Equal("Undo", vm.UndoText);
					vm.Browser.Selection.SelectBooks(new[] { vm.Browser.Items[0].Book });
					ComicBook first = vm.Browser.Items[0].Book;
					string was = first.Series;
					launch.Dialogs.BookInfoHandler = request =>
					{
						request.Model.Series = "Edited in the dialog";
						request.Model.Year = "2012";
						request.Model.Ok();
						return Task.FromResult(true);
					};
					await vm.ShowInfoCommand.ExecuteAsync(null);
					Assert.Equal("Edited in the dialog", first.Series);
					await WaitFor(() => vm.CanUndo, "the undo list follows the change");
					Assert.Equal("Undo: Show Info", vm.UndoText);
					Assert.Equal("Undo: Show Info", vm.UndoToolTip);
					Assert.True(vm.UndoCommand.CanExecute(null));
					Assert.False(vm.RedoCommand.CanExecute(null));
					vm.UndoCommand.Execute(null);
					Assert.Equal(was, first.Series);
					Assert.Equal(-1, first.Year);
					Assert.True(vm.RedoCommand.CanExecute(null));
					Assert.Equal("Redo: Show Info", vm.RedoText);
					Assert.False(vm.UndoCommand.CanExecute(null));
					vm.RedoCommand.Execute(null);
					Assert.Equal("Edited in the dialog", first.Series);
					Assert.Equal(2012, first.Year);
				}
			});
		}

		[Fact]
		public async Task The_dialog_gets_the_services_of_the_pages_and_a_changed_cover_makes_the_grid_ask_for_its_pictures_again()
		{
			await Ui.Run(async () =>
			{
				using (var temp = new TempDir())
				using (Launch launch = await Launch.Start(temp.Combine("app")))
				{
					MainViewModel vm = launch.ViewModel;
					await Fill(launch, 2);
					vm.Browser.Selection.SelectBooks(new[] { vm.Browser.Items[0].Book });
					ComicBook first = vm.Browser.Items[0].Book;
					var refreshed = new System.Collections.Generic.List<string>();
					vm.Browser.PropertyChanged += (s, e) => refreshed.Add(e.PropertyName);
					launch.Dialogs.BookInfoHandler = request =>
					{
						Assert.NotNull(request.Model.PageServices);
						Assert.Same(launch.Service.Thumbnails, request.Model.PageServices.Thumbnails);
						Assert.Same(launch.Service.Host.ImagePool, request.Model.PageServices.Pool);
						request.Model.Series = "Only the text";
						request.Model.Ok();
						return Task.FromResult(true);
					};
					await vm.ShowInfoCommand.ExecuteAsync(null);
					Assert.DoesNotContain(nameof(BrowserViewModel.Thumbnails), refreshed); // a text does not change the cover

					// a book without a file takes a custom thumbnail: its cover is another picture, so the grid asks again
					first.FilePath = string.Empty;
					Assert.False(first.IsLinked);
					launch.Dialogs.BookInfoHandler = request =>
					{
						request.Model.CustomThumbnailKey = "custom-key-1";
						request.Model.Ok();
						return Task.FromResult(true);
					};
					await vm.ShowInfoCommand.ExecuteAsync(null);
					Assert.Equal("custom-key-1", first.CustomThumbnailKey);
					Assert.Contains(nameof(BrowserViewModel.Thumbnails), refreshed);
				}
			});
		}

		[Fact]
		public async Task While_the_dialog_is_open_Undo_Redo_and_Info_are_off_so_that_Cmd_Z_stays_the_text_fields()
		{
			await Ui.Run(async () =>
			{
				using (var temp = new TempDir())
				using (Launch launch = await Launch.Start(temp.Combine("app")))
				{
					MainViewModel vm = launch.ViewModel;
					await Fill(launch, 2);
					vm.Browser.Selection.SelectBooks(new[] { vm.Browser.Items[0].Book });
					vm.Browser.Items[0].Book.Rating = 2; // an undoable change outside the dialog
					vm.Browser.Items[0].Book.Rating = 3;
					await WaitFor(() => vm.CanUndo, "an undo step");
					bool openOff = false;
					launch.Dialogs.BookInfoHandler = request =>
					{
						openOff = !vm.UndoCommand.CanExecute(null) && !vm.RedoCommand.CanExecute(null) && !vm.ShowInfoCommand.CanExecute(null) && vm.IsInfoDialogOpen;
						return Task.FromResult(false);
					};
					await vm.ShowInfoCommand.ExecuteAsync(null);
					Assert.True(openOff);
					Assert.False(vm.IsInfoDialogOpen);
					Assert.True(vm.UndoCommand.CanExecute(null));
					Assert.True(vm.ShowInfoCommand.CanExecute(null));
				}
			});
		}

		[Fact]
		public async Task The_toolbar_buttons_and_the_Edit_menu_follow_the_undo_list()
		{
			await Ui.Run(async () =>
			{
				using (var temp = new TempDir())
				using (Launch launch = await Launch.Start(temp.Combine("app")))
				{
					MainViewModel vm = launch.ViewModel;
					await Fill(launch, 3);
					var window = new MainWindow { DataContext = vm, Width = 1100, Height = 720 };
					window.Show();
					for (int i = 0; i < 3; i++)
					{
						Dispatcher.UIThread.RunJobs();
						window.UpdateLayout();
						AvaloniaHeadlessPlatform.ForceRenderTimerTick();
					}
					CrToolButton undo = window.FindControl<CrToolButton>("UndoButton"), redo = window.FindControl<CrToolButton>("RedoButton");
					Assert.False(undo.IsEffectivelyEnabled);
					Assert.False(redo.IsEffectivelyEnabled);
					NativeMenu bar = ComicRack.Mac.MainMenu.BuildMenuBar(vm);
					NativeMenuItem edit = bar.Items.OfType<NativeMenuItem>().Single(i => i.Header == "Edit");
					NativeMenuItem[] items = edit.Menu.Items.OfType<NativeMenuItem>().ToArray();
					NativeMenuItem info = items[0], undoItem = items[2], redoItem = items[3];
					Assert.Equal(Avalonia.Input.Key.I, info.Gesture.Key);
					Assert.Equal(Avalonia.Input.KeyModifiers.Meta, info.Gesture.KeyModifiers);
					Assert.Equal(Avalonia.Input.Key.Z, undoItem.Gesture.Key);
					Assert.Equal(Avalonia.Input.Key.Y, redoItem.Gesture.Key);
					Assert.Equal("Undo", undoItem.Header);

					vm.RateBooks(new[] { vm.Browser.Items[0].Book }, 4);
					await WaitFor(() => vm.CanUndo, "the undo list follows the rating");
					Dispatcher.UIThread.RunJobs();
					Assert.True(undo.IsEffectivelyEnabled);
					Assert.False(redo.IsEffectivelyEnabled);
					Assert.Equal("Undo: Change Rating", undoItem.Header);
					Assert.Equal("Undo: Change Rating", Avalonia.Controls.ToolTip.GetTip(undo));
					Click(undo);
					Assert.Equal(0f, vm.Browser.Items[0].Book.Rating);
					Dispatcher.UIThread.RunJobs();
					Assert.True(redo.IsEffectivelyEnabled);
					Assert.Equal("Redo: Change Rating", redoItem.Header);
					// the keys: Cmd+Y on the item, Cmd+Shift+Z as an alias of the window
					Assert.Contains(window.KeyBindings, k => k.Gesture.Key == Avalonia.Input.Key.Z && k.Gesture.KeyModifiers == (Avalonia.Input.KeyModifiers.Meta | Avalonia.Input.KeyModifiers.Shift));
					window.Close();
				}
			});
		}

		private static void Click(CrToolButton button) => button.Command.Execute(null);

		[Fact]
		public async Task The_page_menu_of_the_reader_opens_the_info_of_the_book_of_the_tab_alone()
		{
			await Ui.Run(async () =>
			{
				using (var temp = new TempDir())
				using (Launch launch = await Launch.Start(temp.Combine("app"), svc => new ReaderManager(svc, svc.Preferences.Current)))
				{
					MainViewModel vm = launch.ViewModel;
					await Fill(launch, 3);
					var tabs = (ReaderManager)vm.Readers;
					var session = new FakeSession(new FakePageDecoder { PageCount = 4 }) { Title = "tab" };
					var model = new ReaderViewModel(session, ReaderHarness.DefaultOptions());
					var book = new ComicBook { Series = "Open", FilePath = "/books/open.cbz", PageCount = 4 };
					tabs.Adopt(model, book, OpenBookOptions.None);
					await model.OpenAsync();
					Assert.Same(model, vm.Reader);
					model.ShowInfo(); // the page menu's Info... item
					BookInfoRequest request = Assert.Single(launch.Dialogs.BookInfos);
					Assert.Same(book, request.Model.Current);
					Assert.Equal(new[] { book }, request.Model.AllBooks); // the book of the tab alone: no Previous and Next
					Assert.False(request.Model.HasNavigation);
					// with the reader focused the Edit menu's Info... is for that book too
					Assert.True(vm.CanShowInfo);
				}
			});
		}

		[Fact]
		public async Task The_view_registry_opens_the_dialog_of_the_first_book_without_blocking()
		{
			await Ui.Run(async () =>
			{
				using (var temp = new TempDir())
				using (Launch launch = await Launch.Start(temp.Combine("app")))
				{
					await Fill(launch, 2);
					Assert.Contains("ComicBookDialog", ViewRegistry.Names);
					ViewRegistry.Library = launch.ViewModel;
					try
					{
						Window window = ViewRegistry.Show("ComicBookDialog", null);
						Assert.IsType<ComicBookDialog>(window);
						Assert.True(window.IsVisible);
						Assert.Equal(launch.ViewModel.Browser.Items[0].Book.Caption, window.Title);
						window.Close();
					}
					finally
					{
						ViewRegistry.Library = null;
					}
				}
			});
		}
	}
}
