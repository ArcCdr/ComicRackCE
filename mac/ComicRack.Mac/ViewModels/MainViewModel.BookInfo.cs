using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using ComicRack.Mac.Editor;
using ComicRack.Mac.Host;
using ComicRack.Mac.Host.Library;
using ComicRack.Mac.Host.Platform;
using ComicRack.Mac.Services;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Engine.Database;

namespace ComicRack.Mac.ViewModels
{
	/// <summary>
	/// The Info dialog and the undo of the Core (P5.7a): Edit &gt; Info... (Cmd+I, <c>MainForm.ShowInfo</c>), the book context menu's Info..., the page menu's Info... and the Edit
	/// menu's and the browser toolbar's Undo and Redo (<c>Database.Undo</c>).
	/// </summary>
	public sealed partial class MainViewModel
	{
		private ComicDatabase undoWatched;
		private int dialogDepth;
		private ReaderViewModel infoWatched;

		private void WireBookInfo()
		{
			Browser.SelectionChanged += (sender, e) => ShowInfoCommand.NotifyCanExecuteChanged();
			ReaderChanged += (sender, e) =>
			{
				ShowInfoCommand.NotifyCanExecuteChanged();
				if (infoWatched != null)
				{
					infoWatched.InfoRequested -= OnReaderInfoRequested;
				}
				infoWatched = Reader;
				if (infoWatched != null)
				{
					infoWatched.InfoRequested += OnReaderInfoRequested;
				}
			};
		}

		private void OnReaderInfoRequested(object sender, EventArgs e) => _ = ShowInfoAsync(tabs?.Current?.Book != null ? new[] { tabs.Current.Book } : null);

		// ---- Info

		/// <summary>True if Info... has something to show (<c>commands.Add(ShowInfo, () => ... GetBookList(Selected) is not empty, miShowInfo, tbShowInfo, cmShowInfo)</c>).</summary>
		public bool CanShowInfo => dialogDepth == 0 && (RatingTargetsReader || Browser.SelectedCount > 0);

		/// <summary>True while the Info dialog is open: Undo, Redo and Info... of the menu are off then, so that Cmd+Z and Cmd+Y stay the text field's own.</summary>
		public bool IsInfoDialogOpen => dialogDepth > 0;

		/// <summary>Edit &gt; Info... (Cmd+I): the Info dialog of the book of the current tab, or of the selected books of the browser.</summary>
		[RelayCommand(CanExecute = nameof(CanShowInfo))]
		private Task ShowInfo() => ShowInfoAsync();

		/// <summary>
		/// <c>MainForm.ShowInfo</c>: more than one selected book (all of which can be edited) opens the multi edit dialog (P5.8); otherwise an undo marker "Show Info", then the Info
		/// dialog of the first selected book, with Previous and Next walking through the books of the shown list. Returns true when the dialog was closed with OK.
		/// </summary>
		public async Task<bool> ShowInfoAsync(IReadOnlyList<ComicBook> books = null)
		{
			bool fromReader = books == null && RatingTargetsReader;
			if (books == null)
			{
				books = fromReader ? new[] { tabs.Current.Book } : Browser.SelectedBooks;
			}
			if (books == null || books.Count == 0 || hostService.Host == null)
			{
				return false;
			}
			if (books.Count > 1 && books.All(b => b.EditMode.CanEditProperties()))
			{
				SetUndoMarker(UiText.Message("UndoEditMultipleComics", "Edit multiple Books"));
				NotYet(UiText.Get("MainForm", "miShowInfo", "Info..."), "P5.8");
				return false;
			}
			ComicBook current = books[0];
			ComicBook[] all = fromReader || books.Count == 1 && !Browser.Items.Any(i => ReferenceEquals(i.Book, current))
				? new[] { current }
				: Browser.Items.Where(i => i.Book != null).Select(i => i.Book).ToArray();
			SetUndoMarker(UiText.Message("UndoShowInfo", "Show Info"));
			BookInfoRequest request = CreateBookInfoRequest(current, all);
			dialogDepth++;
			NotifyDialogState();
			AppLog.Info($"Info dialog opened for {current.Caption} ({all.Length} books in the list).");
			try
			{
				bool ok = await dialogs.ShowBookInfoAsync(request);
				AppLog.Info("Info dialog closed with " + (ok ? "OK" : "Cancel") + ".");
				return ok;
			}
			finally
			{
				// pages that were moved or turned, another custom thumbnail: the cover of the book is another picture now
				if (request.Model.ThumbnailsChanged)
				{
					foreach (ComicBook changed in request.Model.ThumbnailBooks)
					{
						hostService.Thumbnails?.Invalidate(changed);
					}
					Browser.RefreshCovers();
				}
				dialogDepth--;
				NotifyDialogState();
				RefreshUndoState();
			}
		}

		/// <summary>The request for the Info dialog of <paramref name="current"/> (the browser selects the books the dialog moves to).</summary>
		public BookInfoRequest CreateBookInfoRequest(ComicBook current, IReadOnlyList<ComicBook> all)
		{
			MacHost host = hostService.Host;
			ComicDatabase database = host.DatabaseManager.Database;
			var lists = new EditorValueLists(() => database.Books, EditorValueLists.DefaultLocations(host.Paths.DataFolder));
			var model = new BookEditorModel(current, all, preferences, lists, selectComic: book => Browser.SelectBook(book),
				createBook: file => host.DatabaseManager.BookFactory.Create(file, CreateBookOption.DoNotAdd, RefreshInfoOptions.ForceRefresh));
			model.PageServices = new PageListServices { Thumbnails = hostService.Thumbnails, Pool = host.ImagePool };
			return new BookInfoRequest
			{
				Model = model,
				Thumbnails = hostService.Thumbnails,
				Preferences = preferences,
				AddCustomThumbnail = host.AddCustomThumbnail,
				FindBook = file => database.Books[file],
				OpenUrl = url => MacShell.OpenUrl(url),
			};
		}

		// ---- Undo and Redo (miUndo, miRedo, tbUndo, tbRedo)

		/// <summary>Edit &gt; Undo is available (<c>Database.Undo.CanUndo</c>).</summary>
		public bool CanUndo => dialogDepth == 0 && (hostService.Host?.DatabaseManager.Database.Undo.CanUndo ?? false);

		public bool CanRedo => dialogDepth == 0 && (hostService.Host?.DatabaseManager.Database.Undo.CanRedo ?? false);

		/// <summary>"Undo" with the label of the step it takes back (<c>editMenu_DropDownOpening</c>): "Undo: Change Rating".</summary>
		public string UndoText => WithLabel(UiText.Get("MainForm", "miUndo", "&Undo"), hostService.Host?.DatabaseManager.Database.Undo.UndoLabel);

		/// <summary>"Redo: Change Rating", the label of the first step that can be done again.</summary>
		public string RedoText => WithLabel(UiText.Get("MainForm", "miRedo", "&Redo"), hostService.Host?.DatabaseManager.Database.Undo.RedoEntries.FirstOrDefault());

		/// <summary>The tool tip of the toolbar's Undo button (<c>ComicBrowserControl</c>: the button's text and the label).</summary>
		public string UndoToolTip => WithLabel(UiText.Get("ComicBrowserControl", "tbUndo", "Undo"), hostService.Host?.DatabaseManager.Database.Undo.UndoLabel);

		public string RedoToolTip => WithLabel(UiText.Get("ComicBrowserControl", "tbRedo", "Redo"), hostService.Host?.DatabaseManager.Database.Undo.RedoEntries.FirstOrDefault());

		private static string WithLabel(string text, string label) => text + (string.IsNullOrEmpty(label) ? string.Empty : ": " + label);

		[RelayCommand(CanExecute = nameof(CanUndo))]
		private void Undo()
		{
			hostService.Host?.DatabaseManager.Database.Undo.Undo();
			RefreshUndoState();
		}

		[RelayCommand(CanExecute = nameof(CanRedo))]
		private void Redo()
		{
			hostService.Host?.DatabaseManager.Database.Undo.Redo();
			RefreshUndoState();
		}

		private void NotifyDialogState()
		{
			OnPropertyChanged(nameof(IsInfoDialogOpen));
			OnPropertyChanged(nameof(CanShowInfo));
			ShowInfoCommand.NotifyCanExecuteChanged();
			RefreshUndoState();
		}

		/// <summary>Tells the menu and the toolbar that the undo list changed (the Core has no event for it: a changed book is what adds a step).</summary>
		public void RefreshUndoState()
		{
			OnPropertyChanged(nameof(CanUndo));
			OnPropertyChanged(nameof(CanRedo));
			OnPropertyChanged(nameof(UndoText));
			OnPropertyChanged(nameof(RedoText));
			OnPropertyChanged(nameof(UndoToolTip));
			OnPropertyChanged(nameof(RedoToolTip));
			UndoCommand.NotifyCanExecuteChanged();
			RedoCommand.NotifyCanExecuteChanged();
		}

		// A book that changes adds an undo step; the buttons follow it a moment later on the UI thread.
		private void StartUndoTracking()
		{
			StopUndoTracking();
			MacHost host = hostService.Host;
			if (host == null)
			{
				return;
			}
			undoWatched = host.DatabaseManager.Database;
			undoWatched.BookChanged += OnDatabaseBookChanged;
			RefreshUndoState();
		}

		private void StopUndoTracking()
		{
			if (undoWatched != null)
			{
				undoWatched.BookChanged -= OnDatabaseBookChanged;
				undoWatched = null;
			}
		}

		private void OnDatabaseBookChanged(object sender, ContainerBookChangedEventArgs e) => UiDispatch.Post(RefreshUndoState);
	}
}
