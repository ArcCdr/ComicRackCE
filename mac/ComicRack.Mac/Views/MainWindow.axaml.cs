using System;
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using ComicRack.Mac.Controls;
using ComicRack.Mac.Diagnostics;
using ComicRack.Mac.Host;
using ComicRack.Mac.Services;
using ComicRack.Mac.ViewModels;

namespace ComicRack.Mac.Views
{
	public partial class MainWindow : Window
	{
		private PerfOverlayController perf;
		private MainViewModel model;
		private readonly List<MenuItem> sortItems = new List<MenuItem>();
		private ReaderWindow readerWindow;
		private WindowFrameTracker readerFrame;
		private BrowserContextMenu contextMenu;
		private MenuItem revealItem, copyPathItem;
		private Separator revealSeparator;

		public MainWindow()
		{
			InitializeComponent();
			ApplyTexts();
			// The button flips the sort order (tbbSort_ButtonClick); only the arrow opens the menu (tbbSort_DropDownOpening)
			ArrangeSplit.Click += (sender, e) => model?.Browser.FlipSortOrder();
			ArrangeSplit.DropDown = BuildSortMenu();
			ArrangeSplit.DropDownOpening += (sender, e) => CheckSortItem();
			BuildTabs();
			// a window that comes up (or comes forward) with a book in it and nothing focused gives the keys to the book
			Opened += (sender, e) => FocusReaderIfIdle();
			Activated += (sender, e) => FocusReaderIfIdle();
			PropertyChanged += OnWindowPropertyChanged;
			DataContextChanged += (sender, e) => Attach(DataContext as MainViewModel);
		}

		private CrSplitButton ArrangeSplit => this.FindControl<CrSplitButton>("ArrangeButton");

		/// <summary>The labels and tool tips of the toolbar, as the original names them (ComicBrowserControl.xml, MainForm.xml, PreferencesDialog.xml).</summary>
		private void ApplyTexts()
		{
			ToolTip.SetTip(this.FindControl<CrToolButton>("SidebarButton"), UiText.Get("ComicBrowserControl", "tbSidebar", "Sidebar"));
			ToolTip.SetTip(ArrangeSplit, UiText.Get("ComicBrowserControl", "tbbSort", "Arrange"));
			// The buttons of the other tasks: icon, text and tool tip are the original's already, the feature enables them (full-port.md P5.4).
			Label("BrowsePreviousButton", "btBrowsePrev", "Previous", false);
			Label("BrowseNextButton", "btBrowseNext", "Next", false);
			Label("ViewsButton", "tbbView", "Views", true);
			Label("GroupButton", "tbbGroup", "Group", true);
			Label("StackButton", "tbbStack", "Stack", true);
			Label("ListLayoutsButton", "tsListLayouts", "List Layouts", false);
			Label("DuplicateButton", "tbbDuplicateList", "Duplicate", false);
			Label("UndoButton", "tbUndo", "Undo", false);
			Label("RedoButton", "tbRedo", "Redo", false);
			this.FindControl<CrSearchBox>("SearchBox").CueText = UiText.Default("Search", "Search") + " " + UiText.Get("ComicBrowserControl", "miSearchAll", "All");
			this.FindControl<Button>("CancelButton").Content = UiText.Get("PreferencesDialog", "btCancel", "&Cancel");
		}

		// Sets the tool tip, and the visible text of a button that shows one (ToolStripSplitButton: image and text; the others: image only).
		private void Label(string name, string key, string english, bool showText)
		{
			var button = this.FindControl<CrToolButton>(name);
			string text = UiText.Get("ComicBrowserControl", key, english);
			ToolTip.SetTip(button, text);
			if (showText)
			{
				button.Text = text;
			}
		}

		/// <summary>Cmd+F (GK.01, <c>ComicBrowserControl.FocusQuickSearch</c>): the keyboard focus goes to the quick search.</summary>
		public void FocusQuickSearch() => this.FindControl<CrSearchBox>("SearchBox").FocusBox();

		// The sort menu of the Arrange button: the choices of the MVP sort, one of them checked. Choosing the sort the list has flips its order (ArrangeMenuItemClicked).
		private Avalonia.Controls.Primitives.FlyoutBase BuildSortMenu()
		{
			var flyout = new MenuFlyout();
			string[] names = BrowserViewModel.SortNames;
			for (int i = 0; i < names.Length; i++)
			{
				int index = i;
				var item = new MenuItem { Header = names[i], ToggleType = MenuItemToggleType.Radio, GroupName = "arrange" };
				item.Click += (sender, e) =>
				{
					model?.Browser.ArrangeBy(index);
				};
				sortItems.Add(item);
				flyout.Items.Add(item);
			}
			return flyout;
		}

		private void CheckSortItem()
		{
			int selected = model?.Browser.SortIndex ?? -1;
			for (int i = 0; i < sortItems.Count; i++)
			{
				sortItems[i].IsChecked = i == selected;
			}
		}

		private void Attach(MainViewModel next)
		{
			if (model != null)
			{
				model.PropertyChanged -= OnModelPropertyChanged;
				model.ReaderChanged -= OnReaderChanged;
				model.FocusReaderRequested -= OnFocusReaderRequested;
				model.FocusBrowserRequested -= OnFocusBrowserRequested;
				if (model.Tabs != null)
				{
					model.Tabs.SlotsChanged -= OnSlotsChanged;
					model.Tabs.CurrentSlotChanged -= OnSlotsChanged;
					model.Tabs.CaptionsChanged -= OnCaptionsChanged;
				}
			}
			model = next;
			if (model != null)
			{
				model.IsFullScreen = WindowState == WindowState.FullScreen;
				KeyBindings.Clear();
				KeyBindings.AddRange(MainMenu.Aliases(model));
				KeyBindings.Add(new KeyBinding { Gesture = MainMenu.FocusQuickSearchGesture, Command = new CommunityToolkit.Mvvm.Input.RelayCommand(FocusQuickSearch) });
				model.PropertyChanged += OnModelPropertyChanged;
				model.ReaderChanged += OnReaderChanged;
				model.FocusReaderRequested += OnFocusReaderRequested;
				model.FocusBrowserRequested += OnFocusBrowserRequested;
				// the context menu of the book grid (BC.*) and the rating target of the Edit menu
				contextMenu = new BrowserContextMenu(model);
				BookGrid.ContextFlyout = contextMenu.Flyout;
				model.BrowserHasFocus = () => BookGrid.IsKeyboardFocusWithin;
				BrowserDock.KeepGripVisible = model.KeepBrowserGrip;
				if (model.Tabs != null)
				{
					model.Tabs.SlotsChanged += OnSlotsChanged;
					model.Tabs.CurrentSlotChanged += OnSlotsChanged;
					model.Tabs.CaptionsChanged += OnCaptionsChanged;
					BuildReaderMenus(model.Reader);
					RebuildTabs();
					SyncReaderHost();
				}
			}
		}

		// Display > Full Screen is checked while the window is full screen, however it got there (the green button, the menu, the key).
		private void OnWindowPropertyChanged(object sender, AvaloniaPropertyChangedEventArgs e)
		{
			if (e.Property == WindowStateProperty && model != null)
			{
				model.IsFullScreen = WindowState == WindowState.FullScreen;
			}
		}

		// ---- the book tabs (fileTabs)

		/// <summary>The tab strip with one tab per open book and the New Tab button (<c>fileTabs</c>).</summary>
		public CrTabBar Tabs => TabBar;

		/// <summary>The reader toolbar at the right of the tab strip (<c>mainToolStrip</c>).</summary>
		public CrToolBar ReaderToolbar => ReaderToolBar;

		/// <summary>The reader area in the window; its DataContext is the reader of the current tab while the reader is docked.</summary>
		public ReaderArea Reader => ReaderPane;

		/// <summary>The small preview under the tree (<c>smallComicPreview</c>).</summary>
		public SmallPreviewPane Preview => PreviewPane;

		/// <summary>The container of the small preview, with its grip and flat border (<c>previewPane</c>).</summary>
		public CrSizableContainer PreviewDock => PreviewPanel;

		/// <summary>The info panel under the browser (<c>comicInfo</c>).</summary>
		public InfoPanelPane Info => InfoPane;

		/// <summary>The container of the info panel, with its grip (<c>pluginContainer</c>).</summary>
		public CrSizableContainer InfoDock => InfoPanelDock;

		/// <summary>The context menu of the book grid (<c>contextMenuItems</c>).</summary>
		public BrowserContextMenu BookContextMenu => contextMenu;

		/// <summary>The reader window while the reader is in its own window, else null.</summary>
		public ReaderWindow UndockedReader => readerWindow;

		private static string Text(string key, string english) => UiText.Get("MainForm", key, english);

		private void BuildTabs()
		{
			TabBar.SelectedTabChanged += (sender, e) => model?.ShowTab(e.Index);
			TabBar.TabCloseRequested += (sender, e) => model?.Tabs?.Close(e.Index);
			// tabContextMenu (TC.02 to TC.07)
			var menu = new MenuFlyout();
			menu.Items.Add(TabMenuItem(Text("cmClose", "&Close"), () => model?.CloseBookCommand, Key.X, KeyModifiers.Meta | KeyModifiers.Alt));
			menu.Items.Add(TabMenuItem(Text("cmCloseAllButThis", "Close All &But This"), () => model?.CloseAllButThisCommand));
			menu.Items.Add(TabMenuItem(Text("cmCloseAllToTheRight", "Close All to the &Right"), () => model?.CloseAllToTheRightCommand));
			menu.Items.Add(new Separator());
			menu.Items.Add(TabMenuItem(Text("cmSyncBrowser", "Show in &Browser"), () => model?.ShowInBrowserCommand, Key.F3, KeyModifiers.Meta | KeyModifiers.Alt));
			revealSeparator = new Separator();
			menu.Items.Add(revealSeparator);
			// the platform name replaces Explorer (ledger TC.06); Cmd+G is the shortcut of the original's Ctrl+G
			revealItem = TabMenuItem(UiText.Get("MacOnly", "RevealInFinder", "Reveal in Finder"), () => model?.RevealInFinderCommand, Key.G, KeyModifiers.Meta);
			copyPathItem = TabMenuItem(Text("cmCopyPath", "Copy Full &Path to Clipboard"), () => model?.CopyFullPathCommand);
			menu.Items.Add(revealItem);
			menu.Items.Add(copyPathItem);
			TabBar.TabMenu = menu;
			// tabContextMenu_Opening: the file commands are for books that are local files
			TabBar.TabMenuOpening += (sender, e) =>
			{
				bool local = model?.Tabs?.Current?.Book != null && model.RevealInFinderCommand.CanExecute(null);
				revealSeparator.IsVisible = local;
				revealItem.IsVisible = local;
				copyPathItem.IsVisible = local;
			};
		}

		private static MenuItem TabMenuItem(string header, Func<System.Windows.Input.ICommand> command, Key key = Key.None, KeyModifiers modifiers = KeyModifiers.None)
		{
			var item = new MenuItem { Header = header };
			item.Click += (sender, e) =>
			{
				System.Windows.Input.ICommand c = command();
				if (c != null && c.CanExecute(null))
				{
					c.Execute(null);
				}
			};
			if (key != Key.None)
			{
				item.InputGesture = new KeyGesture(key, modifiers);
			}
			return item;
		}

		// The tab strip follows the tabs (RebuildBookTabs): one tab per slot with the caption of its book, then the New Tab button. The current tab is selected.
		private void RebuildTabs()
		{
			ReaderManager tabs = model?.Tabs;
			if (tabs == null)
			{
				return;
			}
			TabBar.Tabs.Clear();
			for (int i = 0; i < tabs.Slots.Count; i++)
			{
				ReaderSlot slot = tabs.Slots[i];
				string shortcut = i < OpenBookShortcuts.Length ? OpenBookShortcuts[i] : null;
				TabBar.Tabs.Add(new CrTabItem
				{
					Text = slot.Caption,
					CanClose = true,
					MinimumWidth = 100,
					IconName = "Original",
					// an empty tab has no tool tip (ComicReaderTab.ShowToolTip: only for an open book)
					ToolTipText = slot.IsEmpty ? null : shortcut == null ? slot.Caption : slot.Caption + "\n(" + shortcut + ")",
					Tag = slot,
				});
			}
			string newTab = UiText.Get("MainForm", "miAddTab", "New &Tab");
			var add = new CrTabItem
			{
				Text = newTab,
				ShowText = false,
				IconName = "AddTab",
				CanClose = false,
				MinimumWidth = 32,
				AdjustWidth = false,
				Selectable = false,
				ToolTipText = newTab,
			};
			add.Clicked += (sender, e) => model?.NewTabCommand.Execute(null);
			TabBar.Tabs.Add(add);
			TabBar.SelectedIndex = tabs.CurrentSlot;
		}

		/// <summary>The shortcuts of the first twelve tabs: Ctrl+Alt+F1 to F12 of the original, Ctrl+Cmd+F1 to F12 here (Option+Cmd+F3 is Show in Browser).</summary>
		public static readonly string[] OpenBookShortcuts =
		{
			"\u2303\u2318F1", "\u2303\u2318F2", "\u2303\u2318F3", "\u2303\u2318F4", "\u2303\u2318F5", "\u2303\u2318F6",
			"\u2303\u2318F7", "\u2303\u2318F8", "\u2303\u2318F9", "\u2303\u2318F10", "\u2303\u2318F11", "\u2303\u2318F12",
		};

		private void OnSlotsChanged(object sender, EventArgs e)
		{
			RebuildTabs();
		}

		private void OnCaptionsChanged(object sender, EventArgs e)
		{
			ReaderManager tabs = model?.Tabs;
			if (tabs == null || TabBar.Tabs.Count != tabs.Slots.Count + 1)
			{
				return;
			}
			for (int i = 0; i < tabs.Slots.Count; i++)
			{
				TabBar.Tabs[i].Text = tabs.Slots[i].Caption;
			}
		}

		private void OnModelPropertyChanged(object sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName == nameof(MainViewModel.IsReaderUndocked))
			{
				SyncReaderHost();
			}
			else if (e.PropertyName == nameof(MainViewModel.KeepBrowserGrip))
			{
				BrowserDock.KeepGripVisible = model.KeepBrowserGrip;
			}
		}

		private void OnReaderChanged(object sender, EventArgs e)
		{
			BuildReaderMenus(model.Reader);
			SyncReaderHost();
		}

		// The drop-downs of the reader toolbar belong to the book of the current tab.
		private void BuildReaderMenus(ReaderViewModel reader)
		{
			if (reader != null)
			{
				this.FindControl<CrSplitButton>("PreviousPageButton").DropDown = ReaderPopupMenus.PreviousPage(reader);
				this.FindControl<CrSplitButton>("NextPageButton").DropDown = ReaderPopupMenus.NextPage(reader);
				this.FindControl<CrSplitButton>("PageLayoutButton").DropDown = ReaderPopupMenus.PageLayout(reader);
				this.FindControl<CrSplitButton>("FitButton").DropDown = ReaderPopupMenus.Fit(reader);
				this.FindControl<CrSplitButton>("ZoomButton").DropDown = ReaderPopupMenus.Zoom(reader);
			}
			this.FindControl<CrToolButton>("ToolsButton").Flyout = ReaderPopupMenus.Tools(model);
			ReaderToolTips();
		}

		private void ReaderToolTips()
		{
			Tip("PreviousPageButton", "tbPrevPage", "Previous Page");
			Tip("NextPageButton", "tbNextPage", "Next Page");
			Tip("PageLayoutButton", "tbPageLayout", "Page Layout");
			Tip("FitButton", "tbFit", "Toggle Fit Mode");
			Tip("ZoomButton", "tbZoom", "Change the page zoom");
			Tip("FullScreenButton", "tbFullScreen", "Full Screen");
			Tip("ToolsButton", "tbTools", "Tools");
		}

		private void Tip(string name, string key, string english)
		{
			ToolTip.SetTip(this.FindControl<Control>(name), UiText.Tooltip("MainForm", key) ?? UiText.Get("MainForm", key, english));
		}

		// The reader area shows the book of the current tab while the reader is docked; while it is in its own window the window shows it (and the main window
		// shows the browser alone). The window of an undocked reader exists while a book is open (readerForm.Visible = books.OpenCount > 0).
		private void SyncReaderHost()
		{
			ReaderManager tabs = model?.Tabs;
			if (tabs == null)
			{
				return;
			}
			ReaderSlot slot = tabs.Current;
			ReaderViewModel reader = slot?.Model;
			bool undocked = model.IsReaderUndocked;
			ReaderPane.Thumbnails = undocked ? null : slot?.Thumbnails;
			ReaderPane.DataContext = undocked ? null : reader;
			if (undocked && reader != null)
			{
				if (readerWindow == null)
				{
					OpenReaderWindow(reader, slot);
				}
				else
				{
					readerWindow.Display(reader, slot.Thumbnails);
				}
			}
			else
			{
				CloseReaderWindow();
			}
		}

		private void OpenReaderWindow(ReaderViewModel reader, ReaderSlot slot)
		{
			var window = new ReaderWindow(reader, false, perf, slot.Thumbnails);
			AppPreferences preferences = model.Preferences;
			if (preferences != null)
			{
				WindowFrame placed = WindowFrameTracker.Restore(window, preferences.ReaderWindow);
				readerFrame = WindowFrameTracker.Attach(window, frame => preferences.ReaderWindow = frame, placed);
			}
			NativeMenu.SetMenu(window, MainMenu.BuildMenuBar(model, perf));
			readerWindow = window;
			// only the user closing the window docks the reader again; the application shutting down keeps the state (the reader is still undocked at the next start)
			bool closedByUser = false;
			window.Closing += (sender, e) => closedByUser = e.CloseReason == WindowCloseReason.WindowClosing;
			window.Closed += (sender, e) =>
			{
				readerFrame?.Dispose();
				readerFrame = null;
				if (ReferenceEquals(readerWindow, window) && closedByUser)
				{
					// the reader form closing puts the reader back into the main window (ReaderFormFormClosing)
					readerWindow = null;
					if (model != null && model.IsReaderUndocked)
					{
						model.IsReaderUndocked = false;
					}
				}
			};
			window.Show();
			AppLog.Info("Reader in own window.");
		}

		private void CloseReaderWindow()
		{
			ReaderWindow window = readerWindow;
			if (window == null)
			{
				return;
			}
			readerWindow = null;
			readerFrame?.Dispose();
			readerFrame = null;
			window.Close();
		}

		private void FocusReaderIfIdle()
		{
			if (readerWindow == null && model != null && model.HasBook && ReaderPane.IsEffectivelyVisible && FocusManager?.GetFocusedElement() == null)
			{
				ReaderPane.FocusReader();
			}
		}

		private void OnFocusReaderRequested(object sender, EventArgs e)
		{
			Dispatcher.UIThread.Post(() =>
			{
				if (readerWindow != null)
				{
					readerWindow.Activate();
					readerWindow.ReadingArea.Focus();
				}
				else if (ReaderPane.IsEffectivelyVisible)
				{
					ReaderPane.FocusReader();
				}
			});
		}

		private void OnFocusBrowserRequested(object sender, EventArgs e)
		{
			Dispatcher.UIThread.Post(() => BookGrid.Focus());
		}

		/// <summary>Adds the performance overlay (Ctrl+Option+Cmd+P) over the content. Call once.</summary>
		public PerfOverlay AttachPerfOverlay(PerfOverlayController controller)
		{
			perf = controller;
			var overlay = new PerfOverlay(controller, StopwatchClock.Now);
			this.FindControl<Grid>("ContentArea").Children.Add(overlay);
			return overlay;
		}

		protected override void OnClosed(EventArgs e)
		{
			readerFrame?.Dispose();
			if (readerWindow != null)
			{
				ReaderWindow window = readerWindow;
				readerWindow = null;
				window.Close();
			}
			base.OnClosed(e);
		}

		protected override void OnKeyDown(KeyEventArgs e)
		{
			base.OnKeyDown(e);
			if (!e.Handled && perf != null && e.Key == MainMenu.PerfKey && e.KeyModifiers == MainMenu.PerfModifiers)
			{
				perf.Toggle();
				e.Handled = true;
			}
		}
	}
}
