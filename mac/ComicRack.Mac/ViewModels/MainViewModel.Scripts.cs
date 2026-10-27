using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ComicRack.Mac.Host;
using ComicRack.Mac.Host.Scripting;
using ComicRack.Mac.Scripting;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Engine.Database;

namespace ComicRack.Mac.ViewModels
{
	/// <summary>
	/// The scripts in the main window (P4.5, <c>ScriptUtility</c> and the script parts of <c>MainForm</c>): <see cref="Scripts"/> lists the commands for the menus and the toolbar and runs them,
	/// the reader's book and size changes reach the <c>BookOpened</c> and <c>ReaderResized</c> hooks, the window's start the <c>Startup</c> hook, and <see cref="AppScriptUi"/> answers what the
	/// scripts ask of the windows.
	/// </summary>
	public sealed partial class MainViewModel
	{
		private AppScriptUi scriptUi;

		/// <summary>The script commands, the hooks and the script output.</summary>
		public ScriptCommandsViewModel Scripts { get; private set; }

		private void WireScripts()
		{
			Scripts = new ScriptCommandsViewModel(new ScriptCommandsServices
			{
				Host = () => hostService.Host?.Scripts,
				LibraryBooks = () => hostService.Host?.DatabaseManager.Database.Books.ToArray() ?? Array.Empty<ComicBook>(),
				SelectedBooks = () => Browser.SelectedBooks,
				SetUndoMarker = SetUndoMarker,
				ShowProblem = (title, message) => dialogs.ShowMessageAsync(title, message),
				RunWithProgress = RunScriptWithProgress,
			});
			Browser.SelectionChanged += (sender, e) => Scripts.NotifyCanRunChanged();
			if (tabs != null)
			{
				tabs.BookOpened += (sender, book) => Scripts.BookOpened(book);
				tabs.ReaderResized += (sender, e) => Scripts.ReaderResized(e.Width, e.Height);
			}
		}

		// The library opened: the scripts of the host talk to this window from now on and the menus list their commands.
		private void OpenScripts(MacHost host)
		{
			scriptUi = scriptUi ?? new AppScriptUi(this, dialogs);
			Scripts.OnHostOpened(host.Scripts, scriptUi);
		}

		partial void OnIsBusyChanged(bool value) => Scripts?.NotifyCanRunChanged();

		// A command runs as a long operation of the window: the progress panel with its title and the number of books, and Cancel (a script stops at its next print or call into ComicRack.App).
		private async Task<ScriptRunResult> RunScriptWithProgress(string title, string count, Func<CancellationToken, Task<ScriptRunResult>> work)
		{
			if (IsBusy)
			{
				StatusText = title + ": another library operation is running.";
				return null;
			}
			return await RunExclusive(async () =>
			{
				CancellationToken token = BeginProgress(title);
				ProgressCountText = count;
				return await work(token);
			});
		}

		/// <summary>Runs a script command on a set of books as the window does (undo marker, progress, errors). For tests and scripted use.</summary>
		public Task<ScriptRunResult> RunScriptAsync(ScriptCommandItem item, IReadOnlyList<ComicBook> books) => Scripts.RunAsync(item, books);

		/// <summary>The book of a file for a script that opens it (<c>IOpenBooksManager.OpenFile</c>): the Core's temporary book, or null when the file is not a book.</summary>
		public ComicBook CreateBookForFile(string file)
		{
			MacHost host = hostService.Host;
			if (host == null || string.IsNullOrEmpty(file))
			{
				return null;
			}
			return host.DatabaseManager.BookFactory.Create(file, CreateBookOption.AddToTemporary);
		}
	}
}
