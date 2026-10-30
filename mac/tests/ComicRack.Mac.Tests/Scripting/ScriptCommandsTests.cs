using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using ComicRack.Mac.Controls;
using ComicRack.Mac.Host;
using ComicRack.Mac.Host.Scripting;
using ComicRack.Mac.Reader;
using ComicRack.Mac.Scripting;
using ComicRack.Mac.Services;
using ComicRack.Mac.Tests.Reader;
using ComicRack.Mac.Themes;
using ComicRack.Mac.ViewModels;
using ComicRack.Mac.Views;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Engine.Database;
using cYo.Projects.ComicRack.Engine.Display;
using cYo.Projects.ComicRack.Plugins;
using Xunit;

namespace ComicRack.Mac.Tests.Scripting
{
	/// <summary>
	/// P4.5: the script commands in the window (menus, context menu, toolbar, Info dialog), running them on the selected books with progress and Cancel, the hooks that need the window
	/// (Startup, Shutdown, BookOpened, ReaderResized), the script output window and what scripts ask of the windows. The scripts are a generic test plugin in the data folder of the test.
	/// </summary>
	public class ScriptCommandsTests
	{
		private const string TestScript = @"
import clr
import time
from cYo.Projects.ComicRack.Engine.Display import PageLayoutMode

#@Name Mark edited
#@Key MarkEdited
#@Image p45.png
#@Description Marks the books as edited
#@Hook Books
def MarkEdited(books):
	for b in books:
		b.SetCustomValue('p45.edited', 'yes')
	print 'edited', len(books)

#@Name Endless
#@Key Endless
#@Hook Books
def Endless(books):
	while True:
		print 'tick'
		time.sleep(0.005)

#@Name Fails
#@Key Fails
#@Hook Books
def Fails(books):
	raise ValueError('boom from the script')

#@Name Ask
#@Key Ask
#@Hook Books
def Ask(books):
	print 'answer', ComicRack.App.AskQuestion('Really?', 'Yes', 'Option')

#@Name Two page layout
#@Key TwoPages
#@Hook Books
def TwoPages(books):
	ComicRack.ComicDisplay.PageLayout = PageLayoutMode.Double
	print 'layout', ComicRack.ComicDisplay.PageLayout

#@Name Library tool
#@Key LibTool
#@Hook Library
def LibTool(books):
	print 'library', len(books)

#@Name New one
#@Key NewOne
#@Hook NewBooks
def NewOne(books):
	print 'newbooks'

#@Name Fix in editor
#@Key EditorFix
#@Hook Books, Editor
def EditorFix(books):
	books[0].Series = 'Fixed by the script'

#@Name Opened
#@Key Opened
#@Hook BookOpened
def Opened(book):
	print 'opened', book.Series

#@Name Resized
#@Key Resized
#@Hook ReaderResized
def Resized(width, height):
	print 'resized', width, height

#@Name Boot
#@Key Boot
#@Hook Startup
def Boot():
	print 'startup'

#@Name Bye
#@Key Bye
#@Hook Shutdown
def Bye(user_closing):
	print 'bye', user_closing
	return not user_closing
";

		// a 1 x 1 picture for the command with an image
		private static readonly byte[] Png = MakePicture();

		private static byte[] MakePicture()
		{
			using (var bitmap = new SkiaSharp.SKBitmap(16, 16))
			{
				bitmap.Erase(new SkiaSharp.SKColor(200, 30, 30));
				using (SkiaSharp.SKImage image = SkiaSharp.SKImage.FromBitmap(bitmap))
				using (SkiaSharp.SKData data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100))
				{
					return data.ToArray();
				}
			}
		}

		private static async Task<Launch> Start(TempDir temp, bool readers = false)
		{
			string data = temp.Combine("app");
			string scripts = Path.Combine(MacPaths.Under(data).DataFolder, "Scripts");
			Directory.CreateDirectory(scripts);
			File.WriteAllText(Path.Combine(scripts, "p45.py"), TestScript);
			File.WriteAllBytes(Path.Combine(scripts, "p45.png"), Png);
			return await Launch.Start(data, readers ? (svc => new ReaderManager(svc, svc.Preferences.Current)) : (Func<HostService, IReaderLauncher>)null);
		}

		private static ScriptCommandItem Item(IEnumerable<ScriptCommandItem> items, string key) => items.Single(i => i.Key == key);

		private static async Task WaitFor(Func<bool> condition, string what, int timeoutMs = 20000)
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

		private static async Task<ComicBook[]> Fill(Launch launch, int count)
		{
			MainViewModel vm = launch.ViewModel;
			ComicDatabase db = launch.Service.Host.DatabaseManager.Database;
			ComicBook[] books = Enumerable.Range(1, count).Select(i => new ComicBook
			{
				Series = "Series " + i,
				Number = i.ToString(),
				FilePath = "/nowhere/p45-" + i + ".cbz",
				PageCount = 10,
				AddedTime = DateTime.Now.AddDays(-100),
			}).ToArray();
			foreach (ComicBook book in books)
			{
				db.Books.Add(book);
			}
			await vm.Sidebar.ReloadAsync(launch.Service.Host);
			await vm.Sidebar.CountsReady;
			await vm.WhenLibraryShownAsync();
			Dispatcher.UIThread.RunJobs();
			vm.RequestLiveRefresh();
			await vm.WhenLiveRefreshedAsync();
			await WaitFor(() => vm.Browser.Items.Length == db.Books.Count && !vm.Browser.IsLoading, "the grid shows the library");
			return books;
		}

		private static async Task<(ReaderViewModel model, ComicBook book)> AdoptBook(Launch launch, string series)
		{
			var tabs = (ReaderManager)launch.ViewModel.Readers;
			var session = new FakeSession(new FakePageDecoder { PageCount = 6 }) { Title = series };
			var model = new ReaderViewModel(session, ReaderHarness.DefaultOptions());
			var book = new ComicBook { Series = series, FilePath = "/books/" + series + ".cbz", PageCount = 6 };
			tabs.Adopt(model, book, OpenBookOptions.None);
			await model.OpenAsync();
			return (model, book);
		}

		// ---- the commands

		[Fact]
		public async Task The_commands_of_the_hooks_are_listed_with_their_names_pictures_and_shortcuts()
		{
			await Ui.Run(async () =>
			{
				using (var temp = new TempDir())
				using (Launch launch = await Start(temp))
				{
					ScriptCommandsViewModel scripts = launch.ViewModel.Scripts;
					Assert.True(scripts.IsEnabled);
					Assert.Equal("Library tool", Item(scripts.Library, "LibTool").Name);
					Assert.Equal("New one", Item(scripts.NewBooks, "NewOne").Name);
					Assert.Equal("Fix in editor", Item(scripts.Editor, "EditorFix").Name);
					string[] books = scripts.Books.Select(i => i.Key).ToArray();
					Assert.Contains("MarkEdited", books);
					Assert.Contains("CommitProposed", books); // the script that ships with the program
					Assert.DoesNotContain("Boot", books); // a hook that is not a book command is not in a menu
					Assert.DoesNotContain("Opened", books);

					// the first twelve enabled commands have Ctrl+Shift+F1 to F12 (PluginEngine.Initialize): Cmd+Shift+F1 to F12
					ScriptCommandItem withKey = scripts.Books.First(i => i.Gesture != null);
					Assert.Equal(KeyModifiers.Meta | KeyModifiers.Shift, withKey.Gesture.KeyModifiers);
					Assert.InRange((int)withKey.Gesture.Key, (int)Key.F1, (int)Key.F12);

					// the command with an image has a picture that views name; one without has none
					ScriptCommandItem marked = Item(scripts.Books, "MarkEdited");
					Assert.NotNull(marked.IconName);
					Assert.NotNull(Icons.TryGet(marked.IconName));
					Assert.Null(Item(scripts.Books, "Endless").IconName);
				}
			});
		}

		[Fact]
		public async Task A_books_command_runs_on_the_selected_books_with_an_undo_marker_and_its_output_reaches_the_console()
		{
			await Ui.Run(async () =>
			{
				using (var temp = new TempDir())
				using (Launch launch = await Start(temp))
				{
					MainViewModel vm = launch.ViewModel;
					ComicBook[] books = await Fill(launch, 3);
					vm.Browser.Selection.SelectBooks(new[] { books[0], books[2] });
					ScriptCommandItem item = Item(vm.Scripts.Books, "MarkEdited");
					IRelayCommandProbe command = new IRelayCommandProbe(vm.Scripts.CommandFor(item, PluginEngine.ScriptTypeBooks));
					Assert.True(command.CanExecute);

					ScriptRunResult result = await vm.RunScriptAsync(item, vm.Browser.SelectedBooks);
					Assert.Equal(ScriptRunOutcome.Completed, result.Outcome);
					Assert.Equal("yes", books[0].GetCustomValue("p45.edited"));
					Assert.Null(books[1].GetCustomValue("p45.edited"));
					Assert.Equal("yes", books[2].GetCustomValue("p45.edited"));
					Assert.Contains("edited 2", vm.Scripts.Console.Text);
					Assert.Equal("Undo: Automation 'Mark edited'", vm.UndoText);
					Assert.False(vm.IsBusy);
					Assert.False(vm.IsProgressVisible);

					// without a selection a Books command is off
					vm.Browser.Selection.Clear();
					Assert.False(command.CanExecute);
				}
			});
		}

		// the command object of a menu item
		private sealed class IRelayCommandProbe
		{
			private readonly System.Windows.Input.ICommand command;

			public IRelayCommandProbe(System.Windows.Input.ICommand command)
			{
				this.command = command;
			}

			public bool CanExecute => command.CanExecute(null);
		}

		[Fact]
		public async Task A_running_command_shows_the_progress_panel_with_Cancel_and_stops_at_its_next_print()
		{
			await Ui.Run(async () =>
			{
				using (var temp = new TempDir())
				using (Launch launch = await Start(temp))
				{
					MainViewModel vm = launch.ViewModel;
					ComicBook[] books = await Fill(launch, 2);
					ScriptCommandItem endless = Item(vm.Scripts.Books, "Endless");
					System.Windows.Input.ICommand command = vm.Scripts.CommandFor(endless, PluginEngine.ScriptTypeBooks);
					vm.Browser.Selection.SelectBooks(books);
					Assert.True(command.CanExecute(null));

					Task<ScriptRunResult> run = vm.RunScriptAsync(endless, books);
					await WaitFor(() => vm.Scripts.Console.Text.Contains("tick"), "the script prints");
					Assert.True(vm.IsBusy);
					Assert.True(vm.IsProgressVisible);
					Assert.Equal("Automation 'Endless'", vm.ProgressTitle);
					Assert.Equal("2 books", vm.ProgressCountText);
					Assert.True(vm.CanCancel);
					Assert.True(vm.Scripts.IsRunning);
					Assert.False(command.CanExecute(null), "the items are off while a command runs");

					vm.CancelOperationCommand.Execute(null);
					ScriptRunResult result = await run;
					Assert.Equal(ScriptRunOutcome.Cancelled, result.Outcome);
					Assert.True(result.Detached);
					Assert.False(vm.IsBusy);
					Assert.False(vm.IsProgressVisible);
					Assert.False(vm.Scripts.IsRunning);
					Assert.Contains("'Endless' was cancelled", vm.Scripts.Console.Text);
					// the detached script ends at its next print and prints no more
					await Task.Delay(300);
					int length = vm.Scripts.Console.Length;
					await Task.Delay(150);
					Assert.Equal(length, vm.Scripts.Console.Length);
				}
			});
		}

		[Fact]
		public async Task A_failing_command_is_reported_with_the_original_title_and_the_message_of_the_script()
		{
			await Ui.Run(async () =>
			{
				using (var temp = new TempDir())
				using (Launch launch = await Start(temp))
				{
					MainViewModel vm = launch.ViewModel;
					ComicBook[] books = await Fill(launch, 1);
					ScriptRunResult result = await vm.RunScriptAsync(Item(vm.Scripts.Books, "Fails"), books);
					Assert.Equal(ScriptRunOutcome.Failed, result.Outcome);
					(string title, string message) = Assert.Single(launch.Dialogs.Messages);
					Assert.Equal("Execution of the script failed!", title);
					Assert.Contains("boom from the script", message);
					Assert.False(vm.IsBusy);
				}
			});
		}

		[Fact]
		public async Task A_question_of_a_script_is_asked_in_the_window_and_the_answer_maps_to_0_1_and_2()
		{
			await Ui.Run(async () =>
			{
				using (var temp = new TempDir())
				using (Launch launch = await Start(temp))
				{
					MainViewModel vm = launch.ViewModel;
					ComicBook[] books = await Fill(launch, 1);
					ScriptCommandItem ask = Item(vm.Scripts.Books, "Ask");
					launch.Dialogs.Answers.Enqueue(new QuestionAnswer(true, true, false, false));
					launch.Dialogs.Answers.Enqueue(QuestionAnswer.Ok);
					launch.Dialogs.Answers.Enqueue(QuestionAnswer.Cancel);
					foreach (string expected in new[] { "answer 2", "answer 1", "answer 0" })
					{
						vm.Scripts.Console.Clear();
						await vm.RunScriptAsync(ask, books);
						Assert.Contains(expected, vm.Scripts.Console.Text);
					}
					QuestionRequest first = launch.Dialogs.Questions[0];
					Assert.Equal("Really?", first.Question);
					Assert.Equal("Yes", first.OkText);
					Assert.Equal("Option", first.OptionText);
				}
			});
		}

		[Fact]
		public async Task A_call_from_the_UI_thread_waits_in_a_nested_loop_and_a_worker_waits_for_the_UI_thread()
		{
			await Ui.Run(async () =>
			{
				Assert.Equal(7, UiCall.Run(async () =>
				{
					await Task.Delay(50);
					return 7;
				}));
				int worker = await Task.Run(() => UiCall.Run(async () =>
				{
					await Task.Delay(20);
					return Environment.CurrentManagedThreadId;
				}));
				Assert.Equal(Environment.CurrentManagedThreadId, worker);
			});
		}

		// ---- the hooks that need the window

		[Fact]
		public async Task Startup_runs_once_when_the_window_is_shown_and_the_library_is_open()
		{
			await Ui.Run(async () =>
			{
				using (var temp = new TempDir())
				using (Launch launch = await Start(temp))
				{
					ScriptCommandsViewModel scripts = launch.ViewModel.Scripts;
					await scripts.WhenHooksDoneAsync();
					Assert.DoesNotContain("startup", scripts.Console.Text);
					scripts.WindowShown();
					await WaitFor(() => scripts.Console.Text.Contains("startup"), "the Startup hook");
					scripts.WindowShown();
					await scripts.WhenHooksDoneAsync();
					Assert.Equal(1, scripts.Console.Text.Split(new[] { "startup" }, StringSplitOptions.None).Length - 1);
				}
			});
		}

		[Fact]
		public async Task Shutdown_asks_each_script_and_a_script_can_refuse_a_close_the_user_asked_for()
		{
			await Ui.Run(async () =>
			{
				using (var temp = new TempDir())
				using (Launch launch = await Start(temp))
				{
					ScriptCommandsViewModel scripts = launch.ViewModel.Scripts;
					Assert.False(scripts.RunShutdown(userClosing: true));
					Assert.Contains("bye True", scripts.Console.Text);
					Assert.True(scripts.RunShutdown(userClosing: false));
					Assert.Contains("bye False", scripts.Console.Text);
				}
			});
		}

		[Fact]
		public async Task BookOpened_gets_the_book_and_ReaderResized_the_size_of_the_reading_area()
		{
			await Ui.Run(async () =>
			{
				using (var temp = new TempDir())
				using (Launch launch = await Start(temp, readers: true))
				{
					ScriptCommandsViewModel scripts = launch.ViewModel.Scripts;
					(ReaderViewModel model, ComicBook book) = await AdoptBook(launch, "Opened Series");
					await WaitFor(() => scripts.Console.Text.Contains("opened Opened Series"), "the BookOpened hook");

					model.ViewportSize = new Size(800, 600);
					await WaitFor(() => scripts.Console.Text.Contains("resized 800 600"), "the ReaderResized hook");
					model.ViewportSize = new Size(1024.4, 700.6);
					await WaitFor(() => scripts.Console.Text.Contains("resized 1024 701"), "the second size");

					// a reader that is not the current tab reports nothing
					var tabs = (ReaderManager)launch.ViewModel.Readers;
					(ReaderViewModel other, _) = await AdoptBook(launch, "Second");
					await scripts.WhenHooksDoneAsync();
					tabs.Select(tabs.Slots.ToList().FindIndex(s => ReferenceEquals(s.Model, model)));
					scripts.Console.Clear();
					other.ViewportSize = new Size(333, 222);
					await scripts.WhenHooksDoneAsync();
					Assert.DoesNotContain("resized 333", scripts.Console.Text);

					// a book that is shown again is "opened" again, as NavigatorManager.Open does
					scripts.Console.Clear();
					tabs.Open(book, OpenBookOptions.None);
					await WaitFor(() => scripts.Console.Text.Contains("opened Opened Series"), "BookOpened for a book that was open already");
				}
			});
		}

		[Fact]
		public async Task A_script_changes_the_reader_through_ComicDisplay()
		{
			await Ui.Run(async () =>
			{
				using (var temp = new TempDir())
				using (Launch launch = await Start(temp, readers: true))
				{
					MainViewModel vm = launch.ViewModel;
					ComicBook[] books = await Fill(launch, 1);
					(ReaderViewModel model, _) = await AdoptBook(launch, "Layout Series");
					// Opening the book runs the BookOpened hook on a worker thread. Two scripts that print at the same time write their pieces ("layout", " ", "Double") into one
					// console between each other, so this one starts when the hook has ended.
					await vm.Scripts.WhenHooksDoneAsync();
					Assert.Equal(PageLayoutMode.Single, model.LayoutMode);
					ScriptRunResult result = await vm.RunScriptAsync(Item(vm.Scripts.Books, "TwoPages"), books);
					Assert.True(result.Outcome == ScriptRunOutcome.Completed, "outcome " + result.Outcome + ": " + result.Error);
					Assert.Equal(PageLayoutMode.Double, model.LayoutMode);
					Assert.True(vm.Scripts.Console.Text.Contains("layout Double"), "the script output was: " + vm.Scripts.Console.Text);
				}
			});
		}

		// ---- the Info dialog

		[Fact]
		public async Task The_info_dialog_has_a_script_button_for_the_editor_commands_and_shows_what_the_script_made_of_the_book()
		{
			await Ui.Run(async () =>
			{
				using (var temp = new TempDir())
				using (Launch launch = await Start(temp))
				{
					MainViewModel vm = launch.ViewModel;
					ComicBook[] books = await Fill(launch, 2);
					BookInfoRequest request = vm.CreateBookInfoRequest(books[0], books);
					Assert.Equal("Fix in editor", Assert.Single(request.EditorScripts).Name);
					var dialog = new ComicBookDialog(request);
					dialog.Show();
					try
					{
						Assert.NotNull(dialog.ScriptButton);
						Assert.Equal("Fix in editor", dialog.ScriptButton.Content);
						Assert.Equal(new Size(135, 23), new Size(dialog.ScriptButton.Width, dialog.ScriptButton.Height));
						Assert.Equal("Series 1", request.Model.Series);
						await dialog.ExecuteScriptAsync(request.EditorScripts[0]);
						Assert.True(request.Model.Series == "Fixed by the script", "series " + request.Model.Series + " " + string.Join("|", launch.Dialogs.Messages) + vm.Scripts.Console.Text);
						Assert.Equal("Series 1", books[0].Series); // OK writes it, the script changed the dialog's copy
					}
					finally
					{
						dialog.Close();
					}
				}
			});
		}

		[Fact]
		public async Task Without_an_editor_command_the_info_dialog_has_no_script_button()
		{
			await Ui.Run(async () =>
			{
				using (var temp = new TempDir())
				using (Launch launch = await Launch.Start(temp.Combine("app")))
				{
					MainViewModel vm = launch.ViewModel;
					ComicBook[] books = await Fill(launch, 1);
					BookInfoRequest request = vm.CreateBookInfoRequest(books[0], books);
					var dialog = new ComicBookDialog(request);
					dialog.Show();
					Assert.Null(dialog.ScriptButton);
					dialog.Close();
				}
			});
		}

		// ---- the menus, the toolbar and the script output window

		[Fact]
		public async Task The_menus_list_the_commands_where_the_original_does()
		{
			await Ui.Run(async () =>
			{
				using (var temp = new TempDir())
				using (Launch launch = await Start(temp))
				{
					MainViewModel vm = launch.ViewModel;
					await Fill(launch, 2);
					NativeMenu bar = MainMenu.BuildMenuBar(vm);
					NativeMenu file = ((NativeMenuItem)bar.Items[0]).Menu;
					NativeMenuItem automation = file.Items.OfType<NativeMenuItem>().Single(i => i.Header == "Automation");
					Assert.True(automation.IsVisible);
					Assert.Contains("Library tool", automation.Menu.Items.OfType<NativeMenuItem>().Select(i => i.Header));
					// the NewBooks commands follow the book entry commands of the file menu
					int import = file.Items.ToList().FindIndex(i => i is NativeMenuItem n && n.Header == "Import Windows Library…");
					Assert.IsType<NativeMenuItemSeparator>(file.Items[import + 1]);
					string[] newBooks = file.Items.Skip(import + 2).TakeWhile(i => i is NativeMenuItem n && n.Command != null && (string)n.Header != "Open Books").Select(i => ((NativeMenuItem)i).Header).ToArray();
					Assert.Contains("New one", newBooks);
					// Window > Script Output (the original shows the window only with -ssc)
					NativeMenu window = bar.Items.OfType<NativeMenuItem>().Single(i => i.Header == "Window").Menu;
					NativeMenuItem output = window.Items.OfType<NativeMenuItem>().Single(i => i.Header == "Script Output");
					bool asked = false;
					vm.Scripts.ShowConsoleRequested += (sender, e) => asked = true;
					output.Command.Execute(null);
					Assert.True(asked);

					// the context menu: Automation with the Books commands, shown for books
					var context = new BrowserContextMenu(vm);
					MenuItem item = context.MenuItemOf("miAutomation");
					Assert.True(item.IsVisible);
					string[] names = item.Items.OfType<MenuItem>().Select(i => (string)i.Header).ToArray();
					Assert.Contains("Mark edited", names);
					Assert.DoesNotContain("Library tool", names);
					Assert.NotNull(item.Items.OfType<MenuItem>().Single(i => (string)i.Header == "Mark edited").Icon);
				}
			});
		}

		[Fact]
		public async Task The_toolbar_gets_a_button_for_a_command_with_a_picture_and_the_script_output_window_follows_the_log()
		{
			await Ui.Run(async () =>
			{
				using (var temp = new TempDir())
				using (Launch launch = await Start(temp))
				{
					MainViewModel vm = launch.ViewModel;
					var window = new MainWindow { DataContext = vm, Width = 1100, Height = 720 };
					window.Show();
					try
					{
						Assert.IsType<CrToolSeparator>(window.ScriptButtons[0]);
						string icon = Item(vm.Scripts.Books, "MarkEdited").IconName;
						CrToolButton button = window.ScriptButtons.Skip(1).OfType<CrToolButton>().Single(b => b.IconName == icon);
						Assert.Equal(icon, button.IconName);
						Assert.Equal("Marks the books as edited", ToolTip.GetTip(button));
						CrToolBar bar = window.FindControl<CrToolBar>("BrowserToolBar");
						CrSearchBox search = window.FindControl<CrSearchBox>("SearchBox");
						Assert.True(bar.Children.IndexOf(button) < bar.Children.IndexOf(search), "the buttons come before the quick search at the right end");

						// the script output window
						Assert.Null(window.ScriptConsole);
						vm.Scripts.ShowConsole();
						ScriptConsoleWindow output = window.ScriptConsole;
						Assert.NotNull(output);
						Assert.Equal("Script Output", output.Title);
						Assert.Equal(new Size(606, 471), output.ClientSize);
						Assert.True(output.Log.Classes.Contains("cr-console"));
						Assert.True(output.Log.IsReadOnly);
						vm.Scripts.ShowConsole();
						Assert.Same(output, window.ScriptConsole);
						vm.Scripts.Console.WriteLine("first line");
						output.Update();
						Assert.Equal("first line" + Environment.NewLine, output.Log.Text);
						vm.Scripts.Console.WriteLine("second line");
						output.Update();
						Assert.EndsWith("second line" + Environment.NewLine, output.Log.Text);
						output.Close();
						Assert.Null(window.ScriptConsole);
					}
					finally
					{
						window.Close();
					}
				}
			});
		}

		[Fact]
		public async Task A_picture_of_a_script_is_registered_under_a_name_of_its_own_and_can_be_taken_out_again()
		{
			await Ui.Run(() =>
			{
				PictureRegistration();
				return Task.CompletedTask;
			});
		}

		private static void PictureRegistration()
		{
			string file = Path.Combine(Path.GetTempPath(), "crmac-p45-" + Guid.NewGuid().ToString("N") + ".png");
			File.WriteAllBytes(file, Png);
			try
			{
				Assert.True(Icons.RegisterFile("script:test:picture", file));
				Assert.NotNull(Icons.TryGet("script:test:picture"));
				Assert.False(Icons.Contains("script:test:picture"), "the pictures of the original are the only ones the map knows");
				Icons.Unregister("script:test:picture");
				Assert.Null(Icons.TryGet("script:test:picture"));
				Assert.False(Icons.RegisterFile("script:test:broken", file + ".missing"));
				Assert.Throws<ArgumentException>(() => Icons.RegisterFile(Icons.Names[0], file));
			}
			finally
			{
				File.Delete(file);
			}
		}
	}
}
