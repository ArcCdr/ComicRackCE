using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ComicRack.Mac.Host.Scripting;
using ComicRack.Mac.Services;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Plugins;

namespace ComicRack.Mac.Scripting
{
	/// <summary>What <see cref="ScriptCommandsViewModel"/> needs from the application.</summary>
	public sealed class ScriptCommandsServices
	{
		/// <summary>The script host of the open library, or null.</summary>
		public Func<ScriptHost> Host { get; set; }

		/// <summary>The books of the library (the <c>Library</c> and <c>NewBooks</c> commands get all of them).</summary>
		public Func<IReadOnlyList<ComicBook>> LibraryBooks { get; set; }

		/// <summary>The selected books of the browser (the <c>Books</c> commands).</summary>
		public Func<IReadOnlyList<ComicBook>> SelectedBooks { get; set; }

		/// <summary>Sets the undo marker (<c>Database.Undo.SetMarker</c>).</summary>
		public Action<string> SetUndoMarker { get; set; }

		/// <summary>Tells the user that a script failed: a system alert with the title and the message (<c>ScriptUtility.ShowError</c>).</summary>
		public Func<string, string, Task> ShowProblem { get; set; }

		/// <summary>Runs <paramref name="work"/> as a long operation of the window: the progress panel with Cancel, the other library operations off. Null runs it plainly.</summary>
		public Func<string, string, Func<CancellationToken, Task<ScriptRunResult>>, Task<ScriptRunResult>> RunWithProgress { get; set; }
	}

	/// <summary>
	/// The scripts in the window (P4.5): the commands of the hooks <c>Library</c>, <c>NewBooks</c>, <c>Books</c> and <c>Editor</c> as the items the menus and the toolbar show
	/// (<c>ScriptUtility.CreateToolItems</c>), running a command on its books with an undo marker, on a worker thread with progress and Cancel, and the hooks that act on the program itself:
	/// <c>Startup</c> once the window and the library are up, <c>BookOpened</c> and <c>ReaderResized</c> as the reader reports them. Everything a script prints goes to <see cref="Console"/>
	/// (the script output window). The hooks run one after the other on a worker, in the order they happened; a <c>ReaderResized</c> that is still waiting is replaced by the newer size.
	/// </summary>
	public sealed class ScriptCommandsViewModel : ObservableObject
	{
		private readonly ScriptCommandsServices services;
		private readonly object hookGate = new object();
		private readonly List<RelayCommand> commands = new List<RelayCommand>();
		private Task hookChain = Task.CompletedTask;
		private (int width, int height)? pendingResize;
		private bool windowShown, startupDone;
		private ScriptHost attached;
		private bool busy;
		private bool dark;

		public ScriptCommandsViewModel(ScriptCommandsServices services)
		{
			this.services = services ?? throw new ArgumentNullException(nameof(services));
		}

		/// <summary>The text of the script output window.</summary>
		public ScriptConsoleLog Console { get; } = new ScriptConsoleLog();

		public IReadOnlyList<ScriptCommandItem> Library { get; private set; } = Array.Empty<ScriptCommandItem>();

		public IReadOnlyList<ScriptCommandItem> NewBooks { get; private set; } = Array.Empty<ScriptCommandItem>();

		public IReadOnlyList<ScriptCommandItem> Books { get; private set; } = Array.Empty<ScriptCommandItem>();

		public IReadOnlyList<ScriptCommandItem> Editor { get; private set; } = Array.Empty<ScriptCommandItem>();

		/// <summary>The commands changed: the menus and the toolbar rebuild their script items.</summary>
		public event EventHandler Changed;

		/// <summary>The script output window should be shown (Window &gt; Script Output, a command that wants to show its output).</summary>
		public event EventHandler ShowConsoleRequested;

		/// <summary>The script host this view model works with, or null while there is no library or scripting is off.</summary>
		public ScriptHost Host => attached;

		public bool IsEnabled => attached != null && attached.Enabled;

		/// <summary>True while a command runs through <see cref="RunAsync"/>: the items of the menus are off then.</summary>
		public bool IsRunning
		{
			get => busy;
			private set
			{
				if (SetProperty(ref busy, value))
				{
					NotifyCommands();
				}
			}
		}

		/// <summary>True while the application shows the dark appearance: scripts look for the images of the Dark theme (<c>MacThemePlugin.Dark</c>).</summary>
		public void SetDark(bool value)
		{
			dark = value;
			if (attached != null)
			{
				attached.Theme.Dark = value;
			}
		}

		/// <summary>Shows the script output window.</summary>
		public void ShowConsole() => ShowConsoleRequested?.Invoke(this, EventArgs.Empty);

		// ---- the host

		/// <summary>A library was opened: the script output is attached, the user interface that scripts talk to is set, and the commands are read.</summary>
		public void OnHostOpened(ScriptHost host, IScriptUi ui)
		{
			attached = host;
			if (host == null)
			{
				Refresh();
				return;
			}
			host.Ui = ui ?? new HeadlessScriptUi();
			host.Theme.Dark = dark;
			// PythonCommand.Output is read when a script engine is made: before the first script runs. EnableLog is on, as the original does for its console
			// (Program.cs: PythonCommand.EnableLog = true), so the log of the commands is in the output too.
			host.AttachConsole(Console, log: true);
			Refresh();
			RunStartupWhenReady();
		}

		/// <summary>The library is about to be closed (an import): nothing of it is used any more.</summary>
		public void OnHostClosing()
		{
			attached = null;
			Refresh();
		}

		/// <summary>Reads the commands of the hooks again (a command was switched on or off, a plugin was installed).</summary>
		public void Refresh()
		{
			ScriptHost host = attached;
			Library = Items(host, PluginEngine.ScriptTypeLibrary);
			NewBooks = Items(host, PluginEngine.ScriptTypeNewBooks);
			Books = Items(host, PluginEngine.ScriptTypeBooks);
			Editor = Items(host, PluginEngine.ScriptTypeEditor);
			OnPropertyChanged(nameof(IsEnabled));
			Changed?.Invoke(this, EventArgs.Empty);
		}

		private static IReadOnlyList<ScriptCommandItem> Items(ScriptHost host, string hook)
		{
			if (host == null || !host.Enabled)
			{
				return Array.Empty<ScriptCommandItem>();
			}
			return host.GetCommands(hook).Select(c => new ScriptCommandItem(c)).ToArray();
		}

		// ---- running a command

		/// <summary>The books a command of this kind works on: all books for <c>Library</c> and <c>NewBooks</c>, the selected books for <c>Books</c>.</summary>
		public IReadOnlyList<ComicBook> BooksFor(string hook)
		{
			Func<IReadOnlyList<ComicBook>> source = hook == PluginEngine.ScriptTypeBooks ? services.SelectedBooks : services.LibraryBooks;
			return source?.Invoke() ?? Array.Empty<ComicBook>();
		}

		/// <summary>
		/// A menu or toolbar command that runs <paramref name="item"/> on the books of <paramref name="hook"/> (<c>ScriptUtility.CreateBookCode</c>). It is off while another command runs, and for
		/// <c>Books</c> without a selection.
		/// </summary>
		public IRelayCommand CommandFor(ScriptCommandItem item, string hook)
		{
			var command = new RelayCommand(() => _ = RunOnBooksAsync(item, hook), () => !IsRunning && (hook != PluginEngine.ScriptTypeBooks || BooksFor(hook).Count > 0));
			commands.Add(command);
			return command;
		}

		/// <summary>Runs a command on the books of its hook with progress and Cancel (the work of the menu item).</summary>
		public Task<ScriptRunResult> RunOnBooksAsync(ScriptCommandItem item, string hook)
		{
			IReadOnlyList<ComicBook> books = BooksFor(hook);
			return RunAsync(item, books);
		}

		/// <summary>
		/// <c>ScriptUtility.CreateBookCode</c>: the undo marker "Automation '<paramref name="item"/>'", then the command on <paramref name="books"/> on a worker thread. The window shows the
		/// progress panel with Cancel meanwhile. A failed command is reported with a system alert; a cancelled one stops at its next <c>print</c> or call of <c>ComicRack.App</c>.
		/// </summary>
		public async Task<ScriptRunResult> RunAsync(ScriptCommandItem item, IReadOnlyList<ComicBook> books)
		{
			if (item == null)
			{
				throw new ArgumentNullException(nameof(item));
			}
			if (IsRunning)
			{
				return null;
			}
			services.SetUndoMarker?.Invoke(cYo.Common.Text.StringUtility.Format(UiText.Message("UndoScript", "Automation '{0}'"), item.Name));
			ComicBook[] array = (books ?? Array.Empty<ComicBook>()).ToArray();
			IsRunning = true;
			ScriptRunResult result;
			try
			{
				AppLog.Info("Script command " + item.Key + " started on " + array.Length + " books.");
				Func<CancellationToken, Task<ScriptRunResult>> work = token => ScriptCommandRunner.RunAsync(item.Command, new object[] { array }, token);
				string title = cYo.Common.Text.StringUtility.Format(UiText.Message("UndoScript", "Automation '{0}'"), item.Name);
				string count = array.Length.ToString("N0", System.Globalization.CultureInfo.CurrentCulture) + (array.Length == 1 ? " book" : " books");
				result = services.RunWithProgress != null ? await services.RunWithProgress(title, count, work) : await work(CancellationToken.None);
			}
			finally
			{
				IsRunning = false;
			}
			await ReportAsync(item, result);
			return result;
		}

		/// <summary>The configuration command of a script (<c>Command.Configure</c>, the "Configure..." entry of a toolbar button's drop-down): run on a worker, errors ignored as the original does (<c>catchErrors</c>).</summary>
		public Task ConfigureAsync(ScriptCommandItem item)
		{
			Command configure = item?.Command.Configure;
			return configure == null ? Task.CompletedTask : Task.Run(() => configure.Invoke(null, catchErrors: true));
		}

		private async Task ReportAsync(ScriptCommandItem item, ScriptRunResult result)
		{
			if (result == null)
			{
				return;
			}
			switch (result.Outcome)
			{
				case ScriptRunOutcome.Failed:
					AppLog.Error("Script command " + item.Key + " failed: " + result.Message);
					if (services.ShowProblem != null)
					{
						await services.ShowProblem(UiText.Message("ScriptFailed", "Execution of the script failed!"), result.Message);
					}
					break;
				case ScriptRunOutcome.Cancelled:
					AppLog.Info("Script command " + item.Key + " cancelled" + (result.Detached ? " (it stops at its next call into ComicRack)." : "."));
					Console.WriteLine("'" + item.Name + "' was cancelled" + (result.Detached ? "; it stops at its next print or call of ComicRack.App." : "."));
					break;
				default:
					AppLog.Info("Script command " + item.Key + " finished in " + result.Elapsed.TotalSeconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " s.");
					break;
			}
		}

		private void NotifyCommands()
		{
			foreach (RelayCommand command in commands.ToArray())
			{
				command.NotifyCanExecuteChanged();
			}
		}

		/// <summary>Tells the item commands that the selection or the busy state of the window changed.</summary>
		public void NotifyCanRunChanged() => NotifyCommands();

		// ---- the editor commands

		/// <summary>
		/// <c>ComicBookDialog.ExecuteScript</c>: an editor command (<c>Editor</c> hook) gets an array of one book, the dialog's copy with its fields written into it. This runs the command
		/// on a worker thread and reports a failure like any command; true when it ended normally (the dialog then shows what the script made of the book).
		/// </summary>
		public async Task<bool> RunEditorAsync(ScriptCommandItem item, ComicBook book)
		{
			ScriptRunResult result = await ScriptCommandRunner.RunAsync(item.Command, new object[] { new[] { book } });
			await ReportAsync(item, result);
			return result.Outcome == ScriptRunOutcome.Completed;
		}

		// ---- hooks

		/// <summary>
		/// The <c>Startup</c> hook: once, when the window is shown and a library is open (<c>MainForm</c> calls it at the end of its load). Called by the window when it opened; the host side calls
		/// it from <see cref="OnHostOpened"/>.
		/// </summary>
		public void WindowShown()
		{
			windowShown = true;
			RunStartupWhenReady();
		}

		private void RunStartupWhenReady()
		{
			if (startupDone || !windowShown || attached == null)
			{
				return;
			}
			startupDone = true;
			ScriptHost host = attached;
			Enqueue(() => host.RunStartup());
		}

		/// <summary>The <c>BookOpened</c> hook: every command gets the book that was opened.</summary>
		public void BookOpened(ComicBook book)
		{
			ScriptHost host = attached;
			if (host == null || !host.Enabled || book == null)
			{
				return;
			}
			Enqueue(() => host.Invoke(PluginEngine.ScriptTypeBookOpened, book));
		}

		/// <summary>The <c>ReaderResized</c> hook: every command gets the width and the height of the reading area. A size that waits for an earlier hook to end is replaced by a newer one.</summary>
		public void ReaderResized(int width, int height)
		{
			ScriptHost host = attached;
			if (host == null || !host.Enabled)
			{
				return;
			}
			bool queue;
			lock (hookGate)
			{
				queue = pendingResize == null;
				pendingResize = (width, height);
			}
			if (queue)
			{
				Enqueue(() =>
				{
					(int width, int height) size;
					lock (hookGate)
					{
						size = pendingResize.Value;
						pendingResize = null;
					}
					host.Invoke(PluginEngine.ScriptTypeReaderResized, size.width, size.height);
				});
			}
		}

		/// <summary>The <c>Shutdown</c> hook: false when a script vetoed a close that the user asked for (<see cref="ScriptHost.RunShutdown"/>).</summary>
		public bool RunShutdown(bool userClosing)
		{
			ScriptHost host = attached;
			return host == null || host.RunShutdown(userClosing);
		}

		/// <summary>Writes the on and off switches of the commands into the preferences (the original does it when the main window closes).</summary>
		public void StoreCommandStates() => attached?.StoreCommandStates();

		private void Enqueue(Action work)
		{
			lock (hookGate)
			{
				hookChain = hookChain.ContinueWith(_ =>
				{
					try
					{
						work();
					}
					catch (Exception ex)
					{
						AppLog.Error("A script hook failed.", ex);
					}
				}, TaskScheduler.Default);
			}
		}

		/// <summary>Completes when every hook that was asked for has run (tests).</summary>
		public Task WhenHooksDoneAsync()
		{
			lock (hookGate)
			{
				return hookChain;
			}
		}
	}
}
