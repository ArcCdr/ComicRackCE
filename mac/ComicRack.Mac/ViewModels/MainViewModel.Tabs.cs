using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ComicRack.Mac.Host;
using ComicRack.Mac.Services;
using cYo.Projects.ComicRack.Engine;

namespace ComicRack.Mac.ViewModels
{
	/// <summary>
	/// The main window as <c>MainForm</c> (MV.01, P5.32a): the reader area with the book tabs, the browser panel docked at the bottom, the reader in its own window.
	/// The book tabs are the <see cref="ReaderManager"/> (<c>NavigatorManager</c>); this part of the view model follows its current tab (<see cref="Reader"/>: the
	/// status bar segments, the window title, the menu bar commands of the Read and Display menus), keeps the state of the browser dock and of the undocked reader
	/// in the preferences, and implements the commands of the File, Browse and Read menus and of the tab menu.
	/// </summary>
	public sealed partial class MainViewModel
	{
		/// <summary>The smallest height of the browser dock a drag can make: the grip and a sliver (<c>CrSizableContainer.MinimumExtent</c>).</summary>
		public const double MinimumBrowserDockHeight = 26;

		private ReaderManager tabs;
		private ReaderViewModel watched;
		private bool sessionRestored;

		/// <summary>The saved preferences this view model keeps its state in, or null.</summary>
		public AppPreferences Preferences => preferences;

		/// <summary>Tools &gt; Exit (<c>tbExit</c>): quits the application like Cmd+Q.</summary>
		[RelayCommand]
		private void Exit() => shell.Quit();

		/// <summary>The book tabs, or null when the view model was made without a reader (some tests): the browser then fills the window.</summary>
		public ReaderManager Tabs => tabs;

		/// <summary>The reader of the current tab, or null when there is no tab or the tab is empty.</summary>
		public ReaderViewModel Reader => tabs?.CurrentModel;

		/// <summary>Raised on the UI thread when <see cref="Reader"/> changed (another tab, a new book in the tab, the last tab closed).</summary>
		public event EventHandler ReaderChanged;

		/// <summary>Raised for every property change of the current reader (the menu bar's check marks follow it).</summary>
		public event PropertyChangedEventHandler ReaderPropertyChanged;

		/// <summary>Raised when the keyboard focus should go to the page of the reader (a book was opened, another tab was chosen).</summary>
		public event EventHandler FocusReaderRequested;

		/// <summary>Raised when Show in Browser wants the book list to have the keyboard focus.</summary>
		public event EventHandler FocusBrowserRequested;

		private void WireTabs()
		{
			tabs = readers as ReaderManager;
			if (preferences != null)
			{
				IsBrowserVisible = preferences.BrowserDockExpanded;
				double height = preferences.BrowserDockHeight;
				BrowserDockHeight = double.IsNaN(height) || double.IsInfinity(height) ? AppPreferences.DefaultBrowserDockHeight : Math.Max(MinimumBrowserDockHeight, height);
				IsReaderUndocked = preferences.ReaderUndocked;
				preferences.PropertyChanged += (sender, e) =>
				{
					if (e.PropertyName == nameof(AppPreferences.TrackCurrentPage))
					{
						OnPropertyChanged(nameof(IsTrackingCurrentPage));
					}
				};
			}
			if (tabs == null)
			{
				return;
			}
			tabs.SlotsChanged += (sender, e) => NotifyTabCommands();
			tabs.CurrentSlotChanged += (sender, e) => OnCurrentSlotChanged();
			tabs.CaptionsChanged += (sender, e) =>
			{
				OnPropertyChanged(nameof(WindowTitle));
				OnPropertyChanged(nameof(BookSegmentText));
			};
			tabs.ToggleBrowserRequested += (sender, e) => ToggleBrowser();
			tabs.ToggleUndockRequested += (sender, e) => ToggleReaderUndocked();
		}

		private void OnCurrentSlotChanged()
		{
			if (watched != null)
			{
				watched.PropertyChanged -= OnReaderPropertyChanged;
			}
			watched = tabs.CurrentModel;
			if (watched != null)
			{
				watched.PropertyChanged += OnReaderPropertyChanged;
			}
			foreach (string name in new[]
			{
				nameof(Reader), nameof(HasBook), nameof(BookSegmentText), nameof(PageSegmentText), nameof(PageCountSegmentText), nameof(WindowTitle),
				nameof(ShowReaderPage), nameof(ShowQuickOpen), nameof(IsReaderFullScreen), nameof(ShowChrome), nameof(ShowTabBar), nameof(BrowserDockVisible),
			})
			{
				OnPropertyChanged(name);
			}
			NotifyTabCommands();
			ReaderChanged?.Invoke(this, EventArgs.Empty);
			if (watched != null)
			{
				FocusReaderRequested?.Invoke(this, EventArgs.Empty);
			}
		}

		private void OnReaderPropertyChanged(object sender, PropertyChangedEventArgs e)
		{
			switch (e.PropertyName)
			{
				case nameof(ReaderViewModel.Title):
					OnPropertyChanged(nameof(BookSegmentText));
					OnPropertyChanged(nameof(WindowTitle));
					break;
				case nameof(ReaderViewModel.PageSegmentText):
					OnPropertyChanged(nameof(PageSegmentText));
					break;
				case nameof(ReaderViewModel.PageCountSegmentText):
					OnPropertyChanged(nameof(PageCountSegmentText));
					break;
			}
			ReaderPropertyChanged?.Invoke(this, e);
		}

		/// <summary>A command of the menu bar that runs the matching command of the current reader and is disabled without one.</summary>
		public System.Windows.Input.ICommand ReaderCommand(Func<ReaderViewModel, System.Windows.Input.ICommand> pick) => new CurrentReaderCommand(this, pick);

		private void NotifyTabCommands()
		{
			CloseBookCommand.NotifyCanExecuteChanged();
			CloseAllBooksCommand.NotifyCanExecuteChanged();
			PreviousTabCommand.NotifyCanExecuteChanged();
			NextTabCommand.NotifyCanExecuteChanged();
			ShowInBrowserCommand.NotifyCanExecuteChanged();
			CloseAllButThisCommand.NotifyCanExecuteChanged();
			CloseAllToTheRightCommand.NotifyCanExecuteChanged();
			RevealInFinderCommand.NotifyCanExecuteChanged();
			CopyFullPathCommand.NotifyCanExecuteChanged();
			OnPropertyChanged(nameof(OpenBookSlots));
		}

		// ---- what the window shows

		/// <summary>True while a book is open in the current tab.</summary>
		public bool HasBook => Reader != null;

		/// <summary>The window has a reader area (a reader is wired) and it is in the window, not in its own window: the tab strip and the area below it are shown.</summary>
		public bool HasReaderHost => tabs != null;

		/// <summary>The reader area is in the main window (<c>!ReaderUndocked</c>).</summary>
		public bool ShowReaderArea => tabs != null && !IsReaderUndocked;

		/// <summary>The page of the book of the current tab is shown in the window.</summary>
		public bool ShowReaderPage => ShowReaderArea && HasBook;

		/// <summary>No book is open in the window: the area shows the Quick Open pane (P5.29).</summary>
		public bool ShowQuickOpen => ShowReaderArea && !HasBook;

		/// <summary>The browser fills the window: there is no reader area, or the reader is in its own window (<c>BrowserDock = Fill</c> while undocked).</summary>
		public bool BrowserFills => !ShowReaderArea;

		/// <summary>The reader fills the screen: full screen with a book in the window hides the tab strip, the status bar and the browser (the minimal GUI of the original).</summary>
		public bool IsReaderFullScreen => IsFullScreen && ShowReaderPage;

		/// <summary>The tab strip and the status bar are shown.</summary>
		public bool ShowChrome => !IsReaderFullScreen;

		/// <summary>The tab strip with the reader toolbar is shown: the window has a reader, and it is not full screen.</summary>
		public bool ShowTabBar => HasReaderHost && ShowChrome;

		/// <summary>The grip of the closed browser dock stays (<c>AlwaysDisplayBrowserDockingGrip</c>, PB.29).</summary>
		public bool KeepBrowserGrip => preferences != null && preferences.AlwaysDisplayBrowserDockingGrip;

		/// <summary>The browser dock is shown: it is open, or its grip stays (<c>AlwaysDisplayBrowserDockingGrip</c>), or it fills the window.</summary>
		public bool BrowserDockVisible => ShowChrome && (BrowserFills || IsBrowserVisible || (preferences != null && preferences.AlwaysDisplayBrowserDockingGrip));

		partial void OnIsFullScreenChanged(bool value)
		{
			OnPropertyChanged(nameof(IsReaderFullScreen));
			OnPropertyChanged(nameof(ShowChrome));
			OnPropertyChanged(nameof(ShowTabBar));
			OnPropertyChanged(nameof(BrowserDockVisible));
		}

		// ---- the browser dock

		/// <summary>The browser dock is open (<c>BrowserVisible</c>, <c>mainViewContainer.Expanded</c>): false collapses it to its grip, or hides it.</summary>
		[ObservableProperty]
		[NotifyPropertyChangedFor(nameof(BrowserDockVisible))]
		public partial bool IsBrowserVisible { get; set; } = true;

		partial void OnIsBrowserVisibleChanged(bool value)
		{
			if (preferences != null)
			{
				preferences.BrowserDockExpanded = value;
			}
		}

		/// <summary>The height of the browser dock when it is open (250; a drag of its grip sets it).</summary>
		[ObservableProperty]
		public partial double BrowserDockHeight { get; set; } = AppPreferences.DefaultBrowserDockHeight;

		partial void OnBrowserDockHeightChanged(double value)
		{
			if (preferences != null && !double.IsNaN(value) && !double.IsInfinity(value))
			{
				preferences.BrowserDockHeight = Math.Max(MinimumBrowserDockHeight, value);
			}
		}

		/// <summary>Browse &gt; Browser (F3), Esc in the reader (<c>ToggleBrowser</c>): opens or closes the browser dock.</summary>
		[RelayCommand]
		public void ToggleBrowser()
		{
			if (!BrowserFills)
			{
				IsBrowserVisible = !IsBrowserVisible;
			}
		}

		// ---- the reader in its own window

		/// <summary>The reader is in its own window (<c>ReaderUndocked</c>); the browser then fills the main window.</summary>
		[ObservableProperty]
		[NotifyPropertyChangedFor(nameof(ShowReaderArea), nameof(ShowReaderPage), nameof(ShowQuickOpen), nameof(BrowserFills), nameof(IsReaderFullScreen), nameof(ShowChrome), nameof(ShowTabBar), nameof(BrowserDockVisible))]
		public partial bool IsReaderUndocked { get; set; }

		partial void OnIsReaderUndockedChanged(bool value)
		{
			if (preferences != null)
			{
				preferences.ReaderUndocked = value;
			}
		}

		/// <summary>Display &gt; Reader in own Window (F12), the key D (<c>ToggleUndockReader</c>).</summary>
		[RelayCommand]
		public void ToggleReaderUndocked()
		{
			if (tabs != null)
			{
				IsReaderUndocked = !IsReaderUndocked;
			}
		}

		// ---- the book tabs

		private bool HasTabs => tabs != null && tabs.Slots.Count > 0;

		private bool HasSeveralTabs => tabs != null && tabs.Slots.Count > 1;

		private bool HasTabToTheRight => tabs != null && tabs.CurrentSlot >= 0 && tabs.CurrentSlot < tabs.Slots.Count - 1;

		private bool CurrentBookIsLocal => tabs?.Current?.Book != null && tabs.Current.Book.EditMode.IsLocalComic();

		/// <summary>File &gt; Close (<c>miCloseComic</c>): the current tab.</summary>
		[RelayCommand(CanExecute = nameof(HasTabs))]
		private void CloseBook() => tabs?.Close();

		/// <summary>File &gt; Close All (<c>miCloseAllComics</c>).</summary>
		[RelayCommand(CanExecute = nameof(HasTabs))]
		private void CloseAllBooks() => tabs?.CloseAll();

		/// <summary>File &gt; New Tab (<c>miAddTab</c>): an empty tab; the next book opened takes its place.</summary>
		[RelayCommand]
		private void NewTab() => tabs?.AddSlot();

		/// <summary>Read &gt; Previous Tab (<c>miPrevTab</c>).</summary>
		[RelayCommand(CanExecute = nameof(HasSeveralTabs))]
		private void PreviousTab() => tabs?.Previous();

		/// <summary>Read &gt; Next Tab (<c>miNextTab</c>).</summary>
		[RelayCommand(CanExecute = nameof(HasSeveralTabs))]
		private void NextTab() => tabs?.Next();

		/// <summary>Tab menu &gt; Close All But This (<c>cmCloseAllButThis</c>).</summary>
		[RelayCommand(CanExecute = nameof(HasTabs))]
		private void CloseAllButThis() => tabs?.CloseAllButCurrent();

		/// <summary>Tab menu &gt; Close All to the Right (<c>cmCloseAllToTheRight</c>).</summary>
		[RelayCommand(CanExecute = nameof(HasTabToTheRight))]
		private void CloseAllToTheRight() => tabs?.CloseAllToTheRight();

		/// <summary>Shows tab <paramref name="index"/> (a click on the tab, an item of File &gt; Open Books).</summary>
		public void ShowTab(int index) => tabs?.Select(index);

		/// <summary>The tab menu's Reveal in Finder (<c>cmRevealInExplorer</c>, Cmd+G): shows the file of the current book in Finder.</summary>
		[RelayCommand(CanExecute = nameof(CurrentBookIsLocal))]
		private void RevealInFinder() => shell.RevealInFinder(tabs?.Current?.Book?.FilePath);

		/// <summary>The tab menu's Copy Full Path to Clipboard (<c>cmCopyPath</c>).</summary>
		[RelayCommand(CanExecute = nameof(CurrentBookIsLocal))]
		private Task CopyFullPath() => shell.CopyTextAsync(tabs?.Current?.Book?.FilePath);

		/// <summary>The books that are open with the key of their menu item, for File &gt; Open Books (<c>miOpenNow</c>): (tab index, caption).</summary>
		public System.Collections.Generic.IReadOnlyList<(int Index, string Caption)> OpenBookSlots
			=> tabs == null ? Array.Empty<(int, string)>() : tabs.Slots.Select((s, i) => (i, s.Caption)).ToArray();

		/// <summary>
		/// Read &gt; Show in Browser (<c>miSyncBrowser</c>, <c>SyncBrowser</c>): opens the browser dock, and selects the book of the current tab in the list that
		/// shows it; when that list does not have it, in the list the book was last opened from.
		/// </summary>
		[RelayCommand(CanExecute = nameof(HasBook))]
		private async Task ShowInBrowser()
		{
			ComicBook book = tabs?.Current?.Book;
			if (book == null)
			{
				return;
			}
			IsBrowserVisible = true;
			shell.ActivateLibrary();
			FocusBrowserRequested?.Invoke(this, EventArgs.Empty);
			if (Browser.SelectBook(book))
			{
				return;
			}
			Guid listId = book.LastOpenedFromListId;
			SidebarNode node = listId == Guid.Empty ? null : Sidebar.AllNodes().FirstOrDefault(n => n.Id == listId);
			if (node != null)
			{
				Sidebar.Select(node);
				await Browser.WhenIdle();
				Browser.SelectBook(book);
			}
		}

		// ---- Read > Track current Page

		/// <summary>Track current Page is on (<c>Settings.TrackCurrentPage</c>, MR.13): turning pages moves the current page and the last page read of the book.</summary>
		public bool IsTrackingCurrentPage => preferences?.TrackCurrentPage ?? ComicBookNavigator.TrackCurrentPage;

		/// <summary>The padlock of the current page segment (<c>trackPagesLockedImage</c>) shows while the page is not tracked.</summary>
		public bool ShowPageLock => !IsTrackingCurrentPage;

		/// <summary>Read &gt; Track current Page (Option+Shift+T).</summary>
		[RelayCommand]
		private void ToggleTrackCurrentPage()
		{
			if (preferences != null)
			{
				preferences.TrackCurrentPage = !preferences.TrackCurrentPage;
			}
			else
			{
				ComicBookNavigator.TrackCurrentPage = !ComicBookNavigator.TrackCurrentPage;
				OnPropertyChanged(nameof(IsTrackingCurrentPage));
			}
			OnPropertyChanged(nameof(ShowPageLock));
		}

		// ---- the last session

		/// <summary>Remembers the files of the open books for the next start (<c>Settings.LastOpenFiles</c>, written when the window closes). Call before the tabs are closed.</summary>
		public void RememberOpenBooks()
		{
			if (preferences != null && tabs != null)
			{
				preferences.LastOpenBooks = tabs.OpenFiles.ToArray();
			}
		}

		// Reopen Books from last session (PB.02): the books of the last run are opened in their tabs once, when the first library is open.
		private void RestoreSession()
		{
			if (sessionRestored || tabs == null || preferences == null || hostService.Host == null)
			{
				return;
			}
			sessionRestored = true;
			if (preferences.OpenLastFile && tabs.OpenCount == 0 && preferences.LastOpenBooks != null)
			{
				tabs.Restore(preferences.LastOpenBooks);
			}
		}
	}
}
