using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ComicRack.Mac.Host;
using ComicRack.Mac.Host.Library;
using ComicRack.Mac.Services;
using cYo.Projects.ComicRack.Engine;
using cYo.Common.Text;

namespace ComicRack.Mac.ViewModels
{
	/// <summary>
	/// Writing the library's information into the book files (P5.9, ledger MF.07, BC.16, SB.02 for the write icon): File &gt; Update all Book Files (Cmd+Shift+U,
	/// <c>MainForm.UpdateComics</c>), the book context menu's Update Book Files (<c>ComicBrowserControl.UpdateFiles</c>), the marking of changed books and the automatic update
	/// (<see cref="BookFileUpdates"/>), and the two status bar icons that follow the Core's write queue (<c>UpdateActivityTimerTick</c>).
	/// </summary>
	public sealed partial class MainViewModel
	{
		private BookFileUpdates fileUpdates;

		/// <summary>The file updates of the open library (null without one).</summary>
		public BookFileUpdates FileUpdates => fileUpdates;

		private void StartFileUpdates()
		{
			StopFileUpdates();
			MacHost host = hostService.Host;
			if (host == null)
			{
				RefreshActivity();
				return;
			}
			fileUpdates = new BookFileUpdates(host, book => readers != null && readers.IsOpen(book));
			fileUpdates.Start();
			// MainForm.OnLoad: dirty books are offered to the queue (written at once when the automatic update is on)
			int offered = fileUpdates.QueueDirtyBooks();
			AppLog.Info($"File updates ready: {offered} books looked at. Writing {(Setting(p => p.UpdateComicFiles) ? "on" : "off")}, automatic {(Setting(p => p.AutoUpdateComicsFiles) ? "on" : "off")}.");
			RefreshActivity();
		}

		private void StopFileUpdates()
		{
			fileUpdates?.Dispose();
			fileUpdates = null;
			IsWriteInfoActive = false;
			HasUpdateErrors = false;
		}

		private void WireFileUpdates()
		{
			if (preferences != null)
			{
				preferences.PropertyChanged += (sender, e) =>
				{
					if (e.PropertyName == nameof(AppPreferences.AutoUpdateComicsFiles))
					{
						UiDispatch.Post(() => OnPropertyChanged(nameof(IsUpdateAllVisible)));
					}
				};
			}
		}

		private bool Setting(Func<AppPreferences, bool> read) => preferences != null && read(preferences);

		/// <summary>True while "Update all Book Files" has something to say: the item is hidden when the files are updated automatically (<c>fileMenu_DropDownOpening</c>).</summary>
		public bool IsUpdateAllVisible => preferences == null || !preferences.AutoUpdateComicsFiles;

		/// <summary>File &gt; Update all Book Files (<c>MainForm.UpdateComics</c>): every book is queued for writing at once (the writing setting still has to be on).</summary>
		[RelayCommand]
		private void UpdateAllBookFiles()
		{
			if (fileUpdates == null)
			{
				return;
			}
			int count = fileUpdates.UpdateAll();
			AppLog.Info($"Update all Book Files: {count} books offered to the write queue." + WritingOffNote());
			RefreshActivity();
		}

		/// <summary>The book context menu's Update Book Files (<c>ComicBrowserControl.UpdateFiles</c>): the selected books that have a file are queued for writing at once.</summary>
		public int UpdateBookFiles(IEnumerable<ComicBook> books)
		{
			if (fileUpdates == null)
			{
				return 0;
			}
			int count = fileUpdates.UpdateBooks(books);
			AppLog.Info($"Update Book Files: {count} books offered to the write queue." + WritingOffNote());
			RefreshActivity();
			return count;
		}

		// The queue ignores every book while "Allow writing of Book info into files" is off; the log says so, because nothing else does.
		private string WritingOffNote() => Setting(p => p.UpdateComicFiles) ? string.Empty : " 'Allow writing of Book info into files' is off, so nothing is written.";

		// ---- the activity icons (tsWriteInfoActivity, tsExportActivity as far as update errors go)

		/// <summary>True while the Core writes book files (<c>QueueManager.IsInComicFileUpdate</c>): the icon of <c>tsWriteInfoActivity</c> shows.</summary>
		[ObservableProperty]
		public partial bool IsWriteInfoActive { get; set; }

		/// <summary>The tool tip of the write icon ("Writing info data to files...").</summary>
		public string WriteInfoToolTip => UiText.Tooltip("MainForm", "tsWriteInfoActivity") ?? "Writing info data to files...";

		/// <summary>True when a write failed: the error icon of <c>tsExportActivity</c> shows (the Errors dialog that lists them is P5.26).</summary>
		[ObservableProperty]
		public partial bool HasUpdateErrors { get; set; }

		/// <summary>How many writes failed (<c>QueueManager.UpdateErrors.Count</c>).</summary>
		[ObservableProperty]
		public partial int UpdateErrorCount { get; set; }

		/// <summary>The tool tip of the error icon, as <c>UpdateActivityTimerTick</c> builds it: "Exporting Books: 0 queued" and "N errors. Click for details".</summary>
		[ObservableProperty]
		public partial string UpdateErrorsToolTip { get; set; }

		/// <summary><c>UpdateActivityTimerTick</c> for the write queue: the window calls it every second, the commands call it after they queue books.</summary>
		public void RefreshActivity()
		{
			BookFileUpdates updates = fileUpdates;
			IsWriteInfoActive = updates != null && updates.IsWriting;
			int errors = updates?.ErrorCount ?? 0;
			UpdateErrorCount = errors;
			HasUpdateErrors = errors > 0;
			UpdateErrorsToolTip = errors == 0
				? null
				: StringUtility.Format(UiText.Raw("MainForm", "ExportingComics", "Exporting Books: {0} queued"), 0) + "\n"
					+ StringUtility.Format(UiText.Raw("MainForm", "ExportingErrors", "{0} errors. Click for details"), errors);
		}
	}
}
