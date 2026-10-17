using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ComicRack.Mac.Host.Library;
using ComicRack.Mac.Services;

namespace ComicRack.Mac.ViewModels
{
	/// <summary>
	/// State and commands of the main window. Every command is wired to a menu item (<see cref="MainMenu"/>); those that
	/// later tasks implement report that they are not available yet.
	///
	/// File > Open Folder and File > Import Windows Library (M2.1) live here: the dialogs come from <see cref="ILibraryDialogs"/>,
	/// the work from <see cref="FolderScanner"/> and <see cref="HostService"/>, and progress comes back through
	/// <see cref="UiProgress{T}"/>. One long operation runs at a time (<see cref="IsBusy"/>); it can be cancelled.
	/// </summary>
	public sealed partial class MainViewModel : ObservableObject
	{
		private readonly IShell shell;
		private readonly HostService hostService;
		private readonly ILibraryDialogs dialogs;
		private CancellationTokenSource operation;

		public MainViewModel(IShell shell, HostService hostService, ILibraryDialogs dialogs)
		{
			this.shell = shell;
			this.hostService = hostService;
			this.dialogs = dialogs;
			StatusText = "Starting the library engine...";
			BookCountText = "Starting the library engine...";
		}

		public string AppName => "ComicRack CE (MVP)";

		[ObservableProperty]
		public partial string StatusText { get; set; }

		[ObservableProperty]
		public partial string HostText { get; set; }

		/// <summary>Number of books in the library, as shown in the middle of the window.</summary>
		[ObservableProperty]
		public partial string BookCountText { get; set; }

		/// <summary>Books in the library, or -1 while there is no open library engine.</summary>
		[ObservableProperty]
		public partial int BookCount { get; set; } = -1;

		/// <summary>True from the start of Open Folder / Import until it ends, including while its dialog is open. Disables both commands.</summary>
		[ObservableProperty]
		[NotifyCanExecuteChangedFor(nameof(OpenFolderCommand))]
		[NotifyCanExecuteChangedFor(nameof(ImportWindowsLibraryCommand))]
		public partial bool IsBusy { get; set; }

		/// <summary>True while a scan or an import shows its progress panel.</summary>
		[ObservableProperty]
		public partial bool IsProgressVisible { get; set; }

		[ObservableProperty]
		public partial string ProgressTitle { get; set; }

		/// <summary>"1,234 files, 1,200 books added".</summary>
		[ObservableProperty]
		public partial string ProgressCountText { get; set; }

		/// <summary>The file being scanned.</summary>
		[ObservableProperty]
		public partial string ProgressFileText { get; set; }

		[ObservableProperty]
		[NotifyCanExecuteChangedFor(nameof(CancelOperationCommand))]
		public partial bool CanCancel { get; set; }

		/// <summary>Called on the UI thread when the host opened or failed.</summary>
		public void OnHostOpened()
		{
			var host = hostService.Host;
			if (host == null)
			{
				HostText = "Engine failed to start";
				StatusText = "The library engine did not start: " + (hostService.Failure?.Message ?? "unknown error") + " (see the log)";
				BookCount = -1;
				BookCountText = "The library engine did not start.";
				return;
			}
			HostText = host.IsNewDatabase ? "New empty library" : "Library opened";
			SetBookCount(host.DatabaseManager.Database.Books.Count);
			StatusText = $"Library engine ready. {BookCount} books. Data: {host.Paths.DataFolder}";
		}

		private void SetBookCount(int count)
		{
			BookCount = count;
			BookCountText = count == 0
				? "The library is empty. Use File › Open Folder… (⌘O) to add comics."
				: count.ToString("N0", CultureInfo.CurrentCulture) + (count == 1 ? " book" : " books") + " in the library";
		}

		// File

		private bool CanStartOperation() => !IsBusy;

		[RelayCommand(CanExecute = nameof(CanStartOperation))]
		private async Task OpenFolderAsync()
		{
			await RunExclusive(async () =>
			{
				string folder = await dialogs.PickFolderAsync("Choose a folder of comics to add to the library");
				if (folder == null)
				{
					StatusText = "Open Folder cancelled.";
					return;
				}
				await ScanFolderCore(folder);
			});
		}

		[RelayCommand(CanExecute = nameof(CanStartOperation))]
		private async Task ImportWindowsLibraryAsync()
		{
			await RunExclusive(async () =>
			{
				ImportRequest request = await dialogs.AskImportAsync();
				if (request == null)
				{
					StatusText = "Import cancelled.";
					return;
				}
				await ImportCore(request);
			});
		}

		/// <summary>Scans a folder and adds its books, as File > Open Folder does after the picker. For tests and scripted use.</summary>
		public Task<ScanResult> ScanFolderAsync(string folder)
		{
			return RunExclusive(() => ScanFolderCore(folder));
		}

		/// <summary>Imports a Windows database, as File > Import does after the dialog.</summary>
		public Task<ImportResult> ImportLibraryAsync(ImportRequest request)
		{
			return RunExclusive(() => ImportCore(request));
		}

		[RelayCommand(CanExecute = nameof(CanCancel))]
		private void CancelOperation()
		{
			CancellationTokenSource cts = operation;
			if (cts == null)
			{
				return;
			}
			CanCancel = false;
			ProgressTitle = "Cancelling...";
			AppLog.Info("Cancel requested by the user.");
			cts.Cancel();
		}

		/// <summary>Cancels a running scan or import (at exit). Does not wait.</summary>
		public void CancelRunningOperation()
		{
			try
			{
				operation?.Cancel();
			}
			catch (ObjectDisposedException)
			{
			}
		}

		private async Task RunExclusive(Func<Task> work)
		{
			await RunExclusive<object>(async () =>
			{
				await work();
				return null;
			});
		}

		private async Task<T> RunExclusive<T>(Func<Task<T>> work)
		{
			if (IsBusy)
			{
				throw new InvalidOperationException("Another library operation is running.");
			}
			IsBusy = true;
			try
			{
				return await work();
			}
			finally
			{
				operation = null;
				IsProgressVisible = false;
				CanCancel = false;
				IsBusy = false;
			}
		}

		private CancellationToken BeginProgress(string title)
		{
			var cts = new CancellationTokenSource();
			operation = cts;
			ProgressTitle = title;
			ProgressCountText = string.Empty;
			ProgressFileText = string.Empty;
			IsProgressVisible = true;
			CanCancel = true;
			return cts.Token;
		}

		private async Task<ScanResult> ScanFolderCore(string folder)
		{
			var host = hostService.Host;
			if (host == null)
			{
				await ReportProblem("Open Folder", "The library engine is not running, so a folder cannot be added.");
				return null;
			}
			CancellationToken token = BeginProgress("Scanning " + Path.GetFileName(folder.TrimEnd('/')) + "...");
			AppLog.Info("Scan of " + folder + " started.");
			ScanResult result;
			try
			{
				result = await new FolderScanner(host).ScanAsync(folder, new UiProgress<ScanProgress>(OnScanProgress), token);
			}
			catch (Exception ex)
			{
				AppLog.Error("The scan failed.", ex);
				await ReportProblem("Open Folder", "The folder could not be scanned: " + ex.Message);
				return null;
			}

			// The books that were added stay, also after a cancel. Save them now, so that a crash does not lose a long scan.
			ProgressTitle = "Saving the library...";
			CanCancel = false;
			SaveVerification saved = await SaveAfterOperation();
			SetBookCount(result.BooksTotal);
			string what = result.Outcome == ScanOutcome.Cancelled ? "Scan cancelled" : "Scan finished";
			StatusText = $"{what}: {result.FilesSeen:N0} files looked at, {result.BooksAdded:N0} books added, {result.BooksTotal:N0} in the library ({result.Elapsed.TotalSeconds:0.0} s).";
			AppLog.Info(StatusText);
			if (saved != null && !saved.Ok)
			{
				await ReportSaveProblem(saved);
			}
			return result;
		}

		private void OnScanProgress(ScanProgress p)
		{
			if (!IsProgressVisible && !p.IsFinal)
			{
				return;
			}
			ProgressCountText = $"{p.FilesSeen:N0} files, {p.BooksAdded:N0} books added";
			ProgressFileText = p.CurrentFile ?? string.Empty;
			if (p.BooksTotal >= 0)
			{
				SetBookCount(p.BooksTotal);
			}
		}

		private async Task<ImportResult> ImportCore(ImportRequest request)
		{
			if (hostService.Paths == null)
			{
				await ReportProblem("Import", "The library engine was not started.");
				return null;
			}
			CancellationToken token = BeginProgress("Importing " + Path.GetFileName(request.DatabasePath) + "...");
			AppLog.Info("Import of " + request.DatabasePath + " started.");
			ImportResult result;
			try
			{
				result = await hostService.ImportAsync(request, new UiProgress<int>(books =>
				{
					ProgressCountText = books.ToString("N0", CultureInfo.CurrentCulture) + " books copied";
				}), token);
			}
			catch (OperationCanceledException)
			{
				StatusText = "Import cancelled. The library was not changed.";
				AppLog.Info(StatusText);
				return null;
			}
			catch (Exception ex)
			{
				AppLog.Error("The import failed.", ex);
				await ReportProblem("Import", "The library could not be imported: " + ex.Message);
				RefreshFromHost();
				return null;
			}
			RefreshFromHost();
			StatusText = $"Imported {result.Books:N0} books ({result.BooksRemapped:N0} remapped, {result.BooksFound:N0} of them found on this Mac)."
				+ (result.BackupFile != null ? " Previous library kept as " + Path.GetFileName(result.BackupFile) + "." : string.Empty);
			AppLog.Info(StatusText);
			if (result.Books > 0 && result.BooksFound == 0)
			{
				await ReportProblem("Import", $"{result.Books:N0} books were imported, but none of their files exists on this Mac. Check the path mappings: the first part of the Windows path must be replaced by the folder where the comics are now. Use File › Import Windows Library again with the right mapping.");
			}
			return result;
		}

		private void RefreshFromHost()
		{
			if (hostService.Host != null)
			{
				SetBookCount(hostService.Host.DatabaseManager.Database.Books.Count);
			}
		}

		private async Task<SaveVerification> SaveAfterOperation()
		{
			if (hostService.Host == null)
			{
				return null; // the app is quitting: the host has been closed and saved already
			}
			try
			{
				return await hostService.SaveAsync();
			}
			catch (Exception ex)
			{
				AppLog.Error("Saving the library failed.", ex);
				return new SaveVerification { Ok = false, Problem = ex.Message };
			}
		}

		private Task ReportSaveProblem(SaveVerification v)
		{
			return ReportProblem("The library was not saved", "ComicRack could not write the library file: " + (v.Problem ?? "unknown error") + "\n\nThe books added in this session may be lost if the app quits now.");
		}

		private async Task ReportProblem(string title, string message)
		{
			StatusText = title + ": " + message;
			try
			{
				await dialogs.ShowMessageAsync(title, message);
			}
			catch (Exception ex)
			{
				AppLog.Error("Showing a message failed.", ex);
			}
		}

		// View

		[RelayCommand]
		private void ToggleSidebar() => NotYet("Show Sidebar", "M2.2c");

		[RelayCommand]
		private void ToggleFullScreen() => shell.ToggleFullScreen();

		// Window

		[RelayCommand]
		private void Minimize() => shell.Minimize();

		[RelayCommand]
		private void Zoom() => shell.Zoom();

		// Help

		[RelayCommand]
		private void ShowHelp() => NotYet("ComicRack CE Help", "M2.5");

		// App

		[RelayCommand]
		private void About() => shell.ShowAbout();

		private void NotYet(string what, string task)
		{
			AppLog.Info(what + " chosen (not implemented before " + task + ").");
			StatusText = $"{what} is not available yet ({task}).";
		}
	}
}
