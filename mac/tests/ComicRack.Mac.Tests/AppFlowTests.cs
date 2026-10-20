using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ComicRack.Mac.Host.Library;
using ComicRack.Mac.Services;
using Xunit;

namespace ComicRack.Mac.Tests
{
	/// <summary>The library commands on small synthetic libraries: dialogs, errors, rollback, save problems. No fixtures needed.</summary>
	public class AppFlowTests
	{
		private static string WriteDb(TempDir temp, string name, string xml)
		{
			string path = temp.Combine(name);
			Directory.CreateDirectory(Path.GetDirectoryName(path));
			File.WriteAllText(path, xml, new UTF8Encoding(false));
			return path;
		}

		[Fact]
		public async Task Open_Folder_scans_the_picked_folder_and_a_cancelled_picker_does_nothing()
		{
			using (new RealLibraryGuard())
			using (var temp = new TempDir())
			{
				await Ui.Run(async () =>
				{
					int errors = AppLog.ErrorCount;
					string books = temp.Combine("books");
					Synthetic.CreateCbzFolder(books, 12);
					using (var launch = await Launch.Start(temp.Combine("app")))
					{
						var vm = launch.ViewModel;
						Assert.True(vm.OpenFolderCommand.CanExecute(null));

						launch.Dialogs.FolderToPick = null; // the user closes the picker
						await vm.OpenFolderCommand.ExecuteAsync(null);
						Assert.Equal(1, launch.Dialogs.FolderPrompts);
						Assert.Equal(0, vm.BookCount);
						Assert.Equal("Add Folder to Library cancelled.", vm.StatusText);
						Assert.False(vm.IsBusy);

						launch.Dialogs.FolderToPick = books;
						await vm.OpenFolderCommand.ExecuteAsync(null);
						Assert.Equal(2, launch.Dialogs.FolderPrompts);
						Assert.Equal(12, vm.BookCount);
						Assert.Contains("12 books", vm.BookCountText);
						Assert.Contains("Scan finished", vm.StatusText);
						Assert.Empty(launch.Dialogs.Messages);
						Assert.False(vm.IsBusy);
						Assert.False(vm.IsProgressVisible);
						Assert.True(vm.OpenFolderCommand.CanExecute(null));
						Assert.True(vm.ImportWindowsLibraryCommand.CanExecute(null));
						Assert.True(launch.Service.LastSave.Ok);
					}
					Assert.Equal(errors, AppLog.ErrorCount);
				});
			}
		}

		[Fact]
		public async Task A_scan_of_a_folder_that_disappeared_shows_a_message_and_the_app_goes_on()
		{
			using (new RealLibraryGuard())
			using (var temp = new TempDir())
			{
				await Ui.Run(async () =>
				{
					using (var launch = await Launch.Start(temp.Combine("app")))
					{
						launch.Dialogs.FolderToPick = temp.Combine("gone");
						int errors = AppLog.ErrorCount;
						await launch.ViewModel.OpenFolderCommand.ExecuteAsync(null);
						Assert.Single(launch.Dialogs.Messages);
						Assert.Contains("could not be scanned", launch.Dialogs.Messages[0].Message);
						Assert.False(launch.ViewModel.IsBusy);
						Assert.True(launch.ViewModel.OpenFolderCommand.CanExecute(null));
						Assert.True(AppLog.ErrorCount > errors, "the failure must be logged as an error");
					}
				});
			}
		}

		[Fact]
		public async Task Import_through_the_menu_command_copies_remaps_and_opens_the_copy()
		{
			using (new RealLibraryGuard())
			using (var temp = new TempDir())
			{
				await Ui.Run(async () =>
				{
					int errors = AppLog.ErrorCount;
					string books = temp.Combine("books");
					Synthetic.CreateCbzFolder(books, 20);
					string source = WriteDb(temp, "Windows/ComicDb.xml", Synthetic.WindowsDb(20, decoy: false));
					string hash = Hashes.Sha256(source);
					using (var launch = await Launch.Start(temp.Combine("app")))
					{
						var vm = launch.ViewModel;
						launch.Dialogs.ImportToReturn = null;
						await vm.ImportWindowsLibraryCommand.ExecuteAsync(null);
						Assert.Equal("Import cancelled.", vm.StatusText);
						Assert.Equal(0, vm.BookCount);

						launch.Dialogs.ImportToReturn = new ImportRequest(source, PathRemapRule.ParseLines(@"S:\media\BD\=" + books + "/"));
						await vm.ImportWindowsLibraryCommand.ExecuteAsync(null);
						Assert.Equal(21, vm.BookCount); // 20 + the book outside the prefix
						Assert.Contains("Imported 21", vm.StatusText);
						Assert.Contains("20", vm.StatusText);
						Assert.DoesNotContain("Previous library kept", vm.StatusText); // the library it replaced had no books
						Assert.Empty(Directory.GetFiles(launch.Service.Paths.DataFolder, "ComicDb.before-import-*"));
						Assert.Empty(launch.Dialogs.Messages);
						Assert.Equal(hash, Hashes.Sha256(source));
						Assert.Equal(21, launch.Service.Host.DatabaseManager.Database.Books.Count);
						Assert.Equal(20, launch.Service.Host.DatabaseManager.Database.Books.Count(b => File.Exists(b.FilePath)));
						Assert.False(vm.IsBusy);

						// The imported library is the one that is saved and relaunched.
						var quit = launch.Quit();
						Assert.True(quit.Ok, quit.ToString());
						Assert.Equal(21, quit.BooksInFile);
					}
					using (var again = await Launch.Start(temp.Combine("app")))
					{
						Assert.Equal(21, again.ViewModel.BookCount);
					}
					Assert.Equal(errors, AppLog.ErrorCount);
				});
			}
		}

		[Fact]
		public async Task Import_with_a_mapping_that_matches_nothing_found_on_disk_warns()
		{
			using (new RealLibraryGuard())
			using (var temp = new TempDir())
			{
				await Ui.Run(async () =>
				{
					string source = WriteDb(temp, "ComicDb.xml", Synthetic.WindowsDb(5, decoy: false));
					using (var launch = await Launch.Start(temp.Combine("app")))
					{
						var result = await launch.ViewModel.ImportLibraryAsync(new ImportRequest(source, PathRemapRule.ParseLines(@"S:\media\BD\=" + temp.Combine("nowhere") + "/")));
						Assert.Equal(6, result.Books);
						Assert.Equal(0, result.BooksFound);
						Assert.Equal(6, launch.ViewModel.BookCount);
						Assert.Single(launch.Dialogs.Messages);
						Assert.Contains("none of their files exists", launch.Dialogs.Messages[0].Message);
					}
				});
			}
		}

		[Fact]
		public async Task Import_of_something_that_is_not_a_library_changes_nothing()
		{
			using (new RealLibraryGuard())
			using (var temp = new TempDir())
			{
				await Ui.Run(async () =>
				{
					string books = temp.Combine("books");
					Synthetic.CreateCbzFolder(books, 8);
					string bad = WriteDb(temp, "bad.xml", "<Settings><Books /></Settings>");
					using (var launch = await Launch.Start(temp.Combine("app")))
					{
						var vm = launch.ViewModel;
						await vm.ScanFolderAsync(books);
						Assert.Equal(8, vm.BookCount);
						int errors = AppLog.ErrorCount;
						var hostBefore = launch.Service.Host;

						await vm.ImportLibraryAsync(new ImportRequest(bad, null));

						Assert.Single(launch.Dialogs.Messages);
						Assert.Contains("not a ComicRack database", launch.Dialogs.Messages[0].Message);
						Assert.Same(hostBefore, launch.Service.Host); // not even restarted
						Assert.Equal(8, vm.BookCount);
						Assert.Equal(8, launch.Service.Host.DatabaseManager.Database.Books.Count);
						Assert.False(vm.IsBusy);
						Assert.Empty(Directory.GetFiles(launch.Service.Paths.DataFolder, "*import*"));
						Assert.True(AppLog.ErrorCount > errors);
					}
				});
			}
		}

		[Fact]
		public async Task Import_of_a_file_the_core_cannot_load_restores_the_previous_library()
		{
			using (new RealLibraryGuard())
			using (var temp = new TempDir())
			{
				await Ui.Run(async () =>
				{
					string books = temp.Combine("books");
					Synthetic.CreateCbzFolder(books, 9);
					// Well formed XML with the right root, but a book whose Id is not a GUID: the importer passes it, the Core's deserializer does not.
					string broken = WriteDb(temp, "broken.xml", "<ComicDatabase><Books><Book Id=\"not-a-guid\" File=\"S:\\x\\a.cbz\"><Title>A</Title></Book></Books></ComicDatabase>");
					using (var launch = await Launch.Start(temp.Combine("app")))
					{
						var vm = launch.ViewModel;
						await vm.ScanFolderAsync(books);
						Assert.Equal(9, vm.BookCount);

						await vm.ImportLibraryAsync(new ImportRequest(broken, PathRemapRule.ParseLines(@"S:\x\=/y/")));

						Assert.Single(launch.Dialogs.Messages);
						Assert.Contains("previous library was restored", launch.Dialogs.Messages[0].Message);
						Assert.NotNull(launch.Service.Host);
						Assert.Equal(9, launch.Service.Host.DatabaseManager.Database.Books.Count);
						Assert.Equal(9, vm.BookCount);
						Assert.Null(launch.Service.Host.OpenMessage);
						string data = launch.Service.Paths.DataFolder;
						Assert.False(File.Exists(Path.Combine(data, "ComicDb.import.xml")));
						Assert.Empty(Directory.GetFiles(data, "ComicDb.before-import-*"));
						var quit = launch.Quit();
						Assert.True(quit.Ok, quit.ToString());
						Assert.Equal(9, quit.BooksInFile);
					}
				});
			}
		}

		[Fact]
		public async Task Cancelling_an_import_while_it_copies_leaves_the_open_library_alone()
		{
			using (new RealLibraryGuard())
			using (var temp = new TempDir())
			{
				await Ui.Run(async () =>
				{
					string books = temp.Combine("books");
					Synthetic.CreateCbzFolder(books, 6);
					string big = WriteDb(temp, "big.xml", Synthetic.WindowsDb(3000, decoy: false));
					using (var launch = await Launch.Start(temp.Combine("app")))
					{
						await launch.ViewModel.ScanFolderAsync(books);
						var hostBefore = launch.Service.Host;
						using (var cts = new CancellationTokenSource())
						{
							// Reported on the importer's thread, so the cancel comes before the copy can finish.
							var progress = new SyncProgress(n =>
							{
								if (n >= 512)
								{
									cts.Cancel();
								}
							});
							await Assert.ThrowsAnyAsync<OperationCanceledException>(() => launch.Service.ImportAsync(new ImportRequest(big, null), progress, cts.Token));
						}
						Assert.Same(hostBefore, launch.Service.Host);
						Assert.Equal(6, launch.Service.Host.DatabaseManager.Database.Books.Count);
						Assert.Empty(Directory.GetFiles(launch.Service.Paths.DataFolder, "*import*"));
						Assert.Empty(Directory.GetFiles(launch.Service.Paths.DataFolder, "*.part"));
					}
				});
			}
		}

		[Fact]
		public async Task A_save_that_does_not_reach_the_disk_is_reported_after_a_scan_and_at_quit()
		{
			using (new RealLibraryGuard())
			using (var temp = new TempDir())
			{
				await Ui.Run(async () =>
				{
					string books = temp.Combine("books");
					Synthetic.CreateCbzFolder(books, 5);
					var launch = await Launch.Start(temp.Combine("app"));
					string dbFile = launch.Service.Host.DatabaseFile;
					try
					{
						// Something saved first, then the file becomes read-only: the Core's save fails silently from here on.
						Assert.True((await launch.Service.SaveAsync()).Ok);
						File.SetUnixFileMode(dbFile, UnixFileMode.UserRead);

						int errors = AppLog.ErrorCount;
						await launch.ViewModel.ScanFolderAsync(books);
						Assert.False(launch.Service.LastSave.Ok);
						Assert.Contains(launch.Dialogs.Messages, m => m.Title == "The library was not saved");
						Assert.Contains("may be lost", launch.Dialogs.Messages.Last().Message);
						Assert.True(AppLog.ErrorCount > errors);

						errors = AppLog.ErrorCount;
						SaveVerification quit = launch.Quit();
						Assert.False(quit.Ok, "quit must report that the file was not written: " + quit);
						Assert.True(AppLog.ErrorCount > errors, "a failed save at quit must be logged as an error");
					}
					finally
					{
						File.SetUnixFileMode(dbFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
						launch.Dispose();
					}
				});
			}
		}

		private sealed class SyncProgress : IProgress<int>
		{
			private readonly Action<int> handler;

			public SyncProgress(Action<int> handler)
			{
				this.handler = handler;
			}

			public void Report(int value) => handler(value);
		}
	}
}
