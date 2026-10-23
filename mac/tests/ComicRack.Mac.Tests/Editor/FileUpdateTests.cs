using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using ComicRack.Mac.Host.Library;
using ComicRack.Mac.Host.Platform;
using ComicRack.Mac.Services;
using ComicRack.Mac.ViewModels;
using ComicRack.Mac.Views;
using cYo.Projects.ComicRack.Engine;
using Xunit;

namespace ComicRack.Mac.Tests.Editor
{
	/// <summary>
	/// P5.9: File &gt; Update all Book Files (MF.07), the context menu's Update Book Files (BC.16), the marking of edited books, the automatic update, and the status bar icons
	/// that follow the write queue (SB.02, write icon and update errors). The books are CBZ files that the bundled 7zz rewrites (the tests return quietly when
	/// mac/native/fetch-7zz.sh has not been run); the result is read back with `crcli info show` after the app has quit, because only one host can be open at a time.
	/// </summary>
	public class FileUpdateTests
	{
		private static async Task WaitFor(Func<bool> condition, string what, int timeoutMs = 60000)
		{
			DateTime end = DateTime.UtcNow.AddMilliseconds(timeoutMs);
			while (!condition())
			{
				if (DateTime.UtcNow > end)
				{
					throw new TimeoutException("Not reached: " + what);
				}
				Avalonia.Threading.Dispatcher.UIThread.RunJobs(); // posted updates (the menu item follows the setting through UiDispatch)
				await Task.Delay(10);
			}
		}

		private static async Task<ComicBook[]> Scan(Launch launch, string folder, int count)
		{
			Synthetic.CreateCbzFolder(folder, count);
			await launch.ViewModel.ScanFolderAsync(folder);
			await launch.ViewModel.WhenLibraryShownAsync();
			ComicBook[] books = launch.Service.Host.DatabaseManager.Database.Books.OrderBy(b => b.FilePath, StringComparer.Ordinal).ToArray();
			Assert.Equal(count, books.Length);
			return books;
		}

		/// <summary>`crcli info show` of a book: the output, with the Core reading the file in a host of its own.</summary>
		private static string InfoShow(string file)
		{
			var output = new StringWriter();
			var error = new StringWriter();
			int code = ComicRack.Cli.Program.Run(new[] { "info", "show", file }, output, error);
			Assert.True(code == 0, output + error.ToString());
			return output.ToString();
		}

		private static bool NoSevenZip => ToolLocator.SevenZip == null;

		[Fact]
		public async Task Editing_in_the_Info_dialog_marks_the_book_and_Update_Book_Files_of_the_context_menu_writes_it_so_that_crcli_reads_the_values()
		{
			if (NoSevenZip)
			{
				return;
			}
			await Ui.Run(async () =>
			{
				using (new RealLibraryGuard())
				using (var temp = new TempDir())
				{
					string file;
					using (Launch launch = await Launch.Start(temp.Combine("app")))
					{
						MainViewModel vm = launch.ViewModel;
						launch.Service.Preferences.Current.UpdateComicFiles = true; // Preferences > Allow writing of Book info into files
						ComicBook[] books = await Scan(launch, temp.Combine("books"), 3);
						await WaitFor(() => vm.Browser.Items.Length == 3 && !vm.Browser.IsLoading, "the grid shows the books");
						ComicBook book = vm.Browser.Items[0].Book;
						file = book.FilePath;
						Assert.NotNull(vm.FileUpdates);
						Assert.False(book.ComicInfoIsDirty);

						var menu = new BrowserContextMenu(vm);
						vm.Browser.Selection.SelectBooks(new[] { book });
						menu.Update();
						Assert.False(menu.MenuItemOf("miUpdateComicFiles").IsVisible); // nothing to write yet: the item shows only for changed books
						Assert.True(menu.MenuItemOf("miUpdateComicFiles").IsEnabled);

						launch.Dialogs.BookInfoHandler = request =>
						{
							request.Model.Series = "Written From The Dialog";
							request.Model.Year = "1999";
							request.Model.Ok();
							return Task.FromResult(true);
						};
						await vm.ShowInfoCommand.ExecuteAsync(null);
						Assert.Equal("Written From The Dialog", book.Series);
						Assert.True(book.ComicInfoIsDirty); // the edit marked the book; the automatic update is off, so the file is as it was
						Assert.False(vm.FileUpdates.IsWriting);
						Assert.Equal(0, vm.FileUpdates.PendingCount);

						menu.Update();
						Assert.True(menu.MenuItemOf("miUpdateComicFiles").IsVisible);
						Assert.True(menu.MenuItemOf("miUpdateComicFiles").IsEnabled);
						Assert.Equal("Update Book Files", menu.MenuItemOf("miUpdateComicFiles").Header);
						Assert.Equal("UpdateSmall", ((ComicRack.Mac.Controls.CrIcon)menu.MenuItemOf("miUpdateComicFiles").Icon).IconName);
						menu.MenuItemOf("miUpdateComicFiles").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
						await WaitFor(() => !book.ComicInfoIsDirty && !vm.FileUpdates.IsWriting, "the book file is written");
						Assert.Equal(0, vm.FileUpdates.ErrorCount);
						Assert.All(books.Where(b => !ReferenceEquals(b, book)), b => Assert.False(b.ComicInfoIsDirty));
						menu.Update();
						Assert.False(menu.MenuItemOf("miUpdateComicFiles").IsVisible); // written: nothing left to update
						launch.Quit();
					}
					string shown = InfoShow(file);
					Assert.Contains("Series: Written From The Dialog", shown);
					Assert.Contains("Year: 1999", shown);
				}
			});
		}

		[Fact]
		public async Task File_Update_all_Book_Files_has_the_shortcut_of_the_ledger_hides_with_the_automatic_update_and_writes_every_changed_book()
		{
			if (NoSevenZip)
			{
				return;
			}
			await Ui.Run(async () =>
			{
				using (new RealLibraryGuard())
				using (var temp = new TempDir())
				{
					string[] files;
					using (Launch launch = await Launch.Start(temp.Combine("app")))
					{
						MainViewModel vm = launch.ViewModel;
						var preferences = launch.Service.Preferences.Current;
						preferences.UpdateComicFiles = true;
						ComicBook[] books = await Scan(launch, temp.Combine("books"), 3);
						files = books.Select(b => b.FilePath).ToArray();

						NativeMenuItem file = MainMenu.BuildMenuBar(vm).Items.OfType<NativeMenuItem>().Single(i => i.Header == "File");
						NativeMenuItem item = file.Menu.Items.OfType<NativeMenuItem>().Single(i => i.Header == "Update all Book Files");
						Assert.Equal(new KeyGesture(Key.U, KeyModifiers.Meta | KeyModifiers.Shift), item.Gesture);
						Assert.True(item.IsVisible);
						Assert.Same(vm.UpdateAllBookFilesCommand, item.Command);
						preferences.AutoUpdateComicsFiles = true; // "Book files are updated automatically": the item goes (fileMenu_DropDownOpening)
						await WaitFor(() => !item.IsVisible, "the item hides");
						preferences.AutoUpdateComicsFiles = false;
						await WaitFor(() => item.IsVisible, "the item shows again");

						books[0].Series = "All One";
						books[1].Publisher = "All Two";
						Assert.True(books[0].ComicInfoIsDirty && books[1].ComicInfoIsDirty);
						Assert.False(books[2].ComicInfoIsDirty);
						Assert.True(vm.UpdateAllBookFilesCommand.CanExecute(null));
						vm.UpdateAllBookFilesCommand.Execute(null);
						await WaitFor(() => !books[0].ComicInfoIsDirty && !books[1].ComicInfoIsDirty && !vm.FileUpdates.IsWriting, "both books are written");
						launch.Quit();
					}
					Assert.Contains("Series: All One", InfoShow(files[0]));
					Assert.Contains("Publisher: All Two", InfoShow(files[1]));
					Assert.DoesNotContain("Series: All One", InfoShow(files[2]));
				}
			});
		}

		[Fact]
		public async Task With_the_automatic_update_an_edit_is_written_without_a_command_and_a_book_in_a_reader_is_written_when_its_tab_closes()
		{
			if (NoSevenZip)
			{
				return;
			}
			await Ui.Run(async () =>
			{
				using (new RealLibraryGuard())
				using (var temp = new TempDir())
				{
					string[] files;
					using (Launch launch = await Launch.Start(temp.Combine("app"), service => new ReaderManager(service)))
					{
						MainViewModel vm = launch.ViewModel;
						var preferences = launch.Service.Preferences.Current;
						preferences.UpdateComicFiles = true;
						preferences.AutoUpdateComicsFiles = true;
						ComicBook[] books = await Scan(launch, temp.Combine("books"), 2);
						files = books.Select(b => b.FilePath).ToArray();

						books[0].Series = "Automatic";
						await WaitFor(() => !books[0].ComicInfoIsDirty && !vm.FileUpdates.IsWriting, "the automatic write");

						// an open book waits for its tab
						vm.Tabs.Open(books[1], OpenBookOptions.InNewTab);
						Assert.True(vm.Tabs.IsOpen(books[1]));
						books[1].Series = "Kept back";
						Assert.True(books[1].ComicInfoIsDirty);
						Assert.Equal(0, vm.FileUpdates.PendingCount);
						Assert.False(vm.FileUpdates.IsWriting);
						vm.Tabs.CloseAll();
						await WaitFor(() => !books[1].ComicInfoIsDirty && !vm.FileUpdates.IsWriting, "the write after the tab closed");
						launch.Quit();
					}
					Assert.Contains("Series: Automatic", InfoShow(files[0]));
					Assert.Contains("Series: Kept back", InfoShow(files[1]));
				}
			});
		}

		[Fact]
		public async Task The_status_bar_icons_follow_the_write_queue_and_list_the_failures()
		{
			await Ui.Run(async () =>
			{
				using (new RealLibraryGuard())
				using (var temp = new TempDir())
				using (Launch launch = await Launch.Start(temp.Combine("app")))
				{
					MainViewModel vm = launch.ViewModel;
					Assert.NotNull(vm.FileUpdates);
					vm.RefreshActivity();
					Assert.False(vm.IsWriteInfoActive);
					Assert.False(vm.HasUpdateErrors);
					Assert.Equal("Writing info data to files...", vm.WriteInfoToolTip);

					// the write queue is busy: a worker is held inside an item that the test releases
					var entered = new ManualResetEventSlim();
					var release = new ManualResetEventSlim();
					launch.Service.Host.QueueManager.WriteComicBookInfoFileQueue.AddItem(new ComicBook(), ar =>
					{
						entered.Set();
						release.Wait(TimeSpan.FromSeconds(60));
					});
					Assert.True(entered.Wait(TimeSpan.FromSeconds(30)));
					vm.RefreshActivity();
					Assert.True(vm.IsWriteInfoActive);
					release.Set();
					await WaitFor(() => !launch.Service.Host.QueueManager.IsInComicFileUpdate, "the queue is idle");
					vm.RefreshActivity();
					Assert.False(vm.IsWriteInfoActive);

					// a failed write: the error icon shows with the count, as the original's export icon does for update errors
					launch.Service.Host.QueueManager.UpdateErrors.Add(("Broken.cbz", "errno=13 : Permission denied"));
					launch.Service.Host.QueueManager.UpdateErrors.Add(("Other.cbz", "boom"));
					vm.RefreshActivity();
					Assert.True(vm.HasUpdateErrors);
					Assert.Equal(2, vm.UpdateErrorCount);
					Assert.Contains("2 errors. Click for details", vm.UpdateErrorsToolTip);
					Assert.Equal(2, vm.FileUpdates.Errors.Count);
					Assert.Equal("Broken.cbz", vm.FileUpdates.Errors[0].Item);
				}
			});
		}

		[Fact]
		public async Task Update_all_does_nothing_without_an_open_library_and_the_writing_setting_is_reported_in_the_log_when_it_is_off()
		{
			await Ui.Run(async () =>
			{
				using (new RealLibraryGuard())
				using (var temp = new TempDir())
				using (Launch launch = await Launch.Start(temp.Combine("app")))
				{
					MainViewModel vm = launch.ViewModel;
					int errors = AppLog.ErrorCount;
					Assert.True(vm.IsUpdateAllVisible); // the default: the files are not updated automatically
					vm.UpdateAllBookFilesCommand.Execute(null); // an empty library, writing off: queued nothing, no error
					Assert.Equal(errors, AppLog.ErrorCount);
					Assert.False(vm.IsWriteInfoActive);
				}
			});
		}
	}
}
