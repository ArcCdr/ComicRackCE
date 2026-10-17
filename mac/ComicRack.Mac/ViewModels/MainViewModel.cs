using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ComicRack.Mac.Services;

namespace ComicRack.Mac.ViewModels
{
	/// <summary>
	/// State and commands of the main window. Every command is wired to a menu item (<see cref="MainMenu"/>); those that
	/// later tasks implement report that they are not available yet.
	/// </summary>
	public sealed partial class MainViewModel : ObservableObject
	{
		private readonly IShell shell;
		private readonly HostService hostService;

		public MainViewModel(IShell shell, HostService hostService)
		{
			this.shell = shell;
			this.hostService = hostService;
			StatusText = "Starting the library engine...";
		}

		public string AppName => "ComicRack CE (MVP)";

		[ObservableProperty]
		public partial string StatusText { get; set; }

		[ObservableProperty]
		public partial string HostText { get; set; }

		/// <summary>Called on the UI thread when the host opened or failed.</summary>
		public void OnHostOpened()
		{
			var host = hostService.Host;
			if (host == null)
			{
				HostText = "Engine failed to start";
				StatusText = "The library engine did not start: " + (hostService.Failure?.Message ?? "unknown error") + " (see the log)";
				return;
			}
			HostText = host.IsNewDatabase ? "New empty library" : "Library opened";
			StatusText = $"Library engine ready. {host.DatabaseManager.Database.Books.Count} books. Data: {host.Paths.DataFolder}";
		}

		// File

		[RelayCommand]
		private void OpenFolder() => NotYet("Open Folder", "M2.1");

		[RelayCommand]
		private void ImportWindowsLibrary() => NotYet("Import Windows Library", "M2.1");

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
