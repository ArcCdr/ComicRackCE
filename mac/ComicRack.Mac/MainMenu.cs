using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input;
using ComicRack.Mac.Diagnostics;
using ComicRack.Mac.ViewModels;
using CommunityToolkit.Mvvm.Input;
using cYo.Projects.ComicRack.Engine.Display;

namespace ComicRack.Mac
{
	/// <summary>
	/// The native macOS menus (C-MENU-1). The menu bar is the original's (<c>MainForm.Designer.cs</c>, <c>mainMenuStrip</c>): File, Edit, Browse,
	/// Read, Display, Help, with the original's texts through TR (section MainForm, key = the item's name), their order and the shortcuts of the
	/// parity ledger (Ctrl becomes Cmd, Alt Option, DD20); Window is the macOS window menu (C-MENU-2). Only the items the app can do today are
	/// here, so a menu can still be empty: the items of the other menus appear with the tasks that implement them (the ledger names the task of
	/// every item). About, Settings and Quit are in the app menu, as macOS has them.
	/// </summary>
	public static class MainMenu
	{
		/// <summary>Show Performance Overlay: Ctrl+Option+Cmd+P (Option+Cmd+P is the original's Previous Book, ledger MR.05).</summary>
		public const Key PerfKey = Key.P;

		public const KeyModifiers PerfModifiers = KeyModifiers.Meta | KeyModifiers.Control | KeyModifiers.Alt;

		/// <summary>Cmd+F: focus the quick search (GK.01, the original's Ctrl+F). The original has no menu item for it; the window binds it.</summary>
		public static readonly KeyGesture FocusQuickSearchGesture = new KeyGesture(Key.F, KeyModifiers.Meta);

		/// <summary>The key bindings of shortcuts that a native menu item cannot carry because the item has another one (aliases of the ledger).</summary>
		public static IReadOnlyList<KeyBinding> Aliases(MainViewModel vm)
		{
			return new[]
			{
				// Full Screen: F11, alias Ctrl+Cmd+F (MD.25).
				new KeyBinding { Gesture = new KeyGesture(Key.F, KeyModifiers.Meta | KeyModifiers.Control), Command = vm.ToggleFullScreenCommand },
				// ComicRack Documentation: F1, alias Cmd+? (MH.02).
				new KeyBinding { Gesture = new KeyGesture(Key.OemQuestion, KeyModifiers.Meta | KeyModifiers.Shift), Command = vm.ShowHelpCommand },
				// Settings: Cmd+, on the app menu item, alias Cmd+F9 (the original's Ctrl+F9, ME.16); Preferences arrive with P5.22a.
				new KeyBinding { Gesture = new KeyGesture(Key.F9, KeyModifiers.Meta), Command = vm.ShowSettingsCommand },
				// Close (the book tab): Option+Cmd+X on the menu item, alias Cmd+W (MF.02)
				new KeyBinding { Gesture = new KeyGesture(Key.W, KeyModifiers.Meta), Command = vm.CloseBookCommand },
				// Open in new Tab (BC.02, Ctrl+Shift+O of the original): the focused book of the browser in a tab of its own
				new KeyBinding { Gesture = new KeyGesture(Key.O, KeyModifiers.Meta | KeyModifiers.Shift), Command = vm.Browser.OpenSelectedInNewTabCommand },
			};
		}

		/// <summary>
		/// The first items of the app menu: About and Settings. Avalonia.Native owns the app menu: it has its default menu (About Avalonia, Services,
		/// Hide, Hide Others, Show All and Quit with Cmd+Q) in place before our code runs, and does not read a menu that is set later. So the items are
		/// put into that menu: its About item is replaced and Settings is added behind it; Quit stays Avalonia's (it appears once).
		/// </summary>
		public static void FillAppMenu(NativeMenu appMenu, MainViewModel vm)
		{
			// Avalonia.Native gives Hide Others Option+Cmd+Q (a mistake: the system's is Option+Cmd+H, MA.01).
			NativeMenuItem hideOthers = appMenu.Items.OfType<NativeMenuItem>().FirstOrDefault(i => i.Header == "Hide Others");
			if (hideOthers != null)
			{
				hideOthers.Gesture = new KeyGesture(Key.H, KeyModifiers.Meta | KeyModifiers.Alt);
			}
			NativeMenuItem existingAbout = appMenu.Items.OfType<NativeMenuItem>().FirstOrDefault(i => i.Header != null && i.Header.StartsWith("About "));
			int at = existingAbout != null ? appMenu.Items.IndexOf(existingAbout) : 0;
			if (existingAbout != null)
			{
				appMenu.Items.RemoveAt(at);
			}
			appMenu.Items.Insert(at, Item("About " + vm.AppName, vm.AboutCommand, Key.None));
			appMenu.Items.Insert(at + 1, new NativeMenuItemSeparator());
			// Settings... (Cmd+, ledger ME.16) opens Preferences, which is P5.22a; the item is there and disabled until then.
			NativeMenuItem settings = Item(UiText.Get("MacOnly", "Settings", "Settings…"), null, Key.OemComma);
			settings.IsEnabled = false;
			appMenu.Items.Insert(at + 2, settings);
		}

		/// <summary>The app menu for an application that has none yet (tests, headless): About and Settings.</summary>
		public static NativeMenu BuildAppMenu(MainViewModel vm)
		{
			var menu = new NativeMenu();
			FillAppMenu(menu, vm);
			return menu;
		}

		/// <summary>The menu bar: File, Edit, Browse, Read, Display, Window, Help. The Read and Display items work on the book of the current tab.</summary>
		public static NativeMenu BuildMenuBar(MainViewModel vm, PerfOverlayController perf = null)
		{
			return new NativeMenu
			{
				Submenu(Text("fileMenu", "&File"), new NativeMenu
				{
					// MF.02, MF.03: Close is Ctrl+X in the original (Cmd+X is Cut on macOS: Option+Cmd+X, alias Cmd+W through Aliases)
					Item(Text("miCloseComic", "&Close"), vm.CloseBookCommand, Key.X, KeyModifiers.Meta | KeyModifiers.Alt),
					Item(Text("miCloseAllComics", "Close A&ll"), vm.CloseAllBooksCommand, Key.X, KeyModifiers.Meta | KeyModifiers.Shift),
					new NativeMenuItemSeparator(),
					// MF.04: an empty tab; the next book opened takes its place
					Item(Text("miAddTab", "New &Tab"), vm.NewTabCommand, Key.T),
					new NativeMenuItemSeparator(),
					// MF.05: the MVP's Open Folder. The original asks for a name or path with a Browse button and scans in the background (P5.10a).
					Item(Text("miAddFolderToLibrary", "&Add Folder to Library..."), vm.OpenFolderCommand, Key.A, KeyModifiers.Meta | KeyModifiers.Shift),
					// Import from a Windows installation has no counterpart in the original: a Mac-only addition (ledger MA.04).
					Item(UiText.Get("MacOnly", "ImportWindowsLibrary", "Import Windows Library…"), vm.ImportWindowsLibraryCommand),
					new NativeMenuItemSeparator(),
					// MF.15: the open books, Ctrl+Alt+F1 to F12 of the original on the first twelve
					OpenBooksItem(vm),
				}),
				Submenu(Text("editMenu", "&Edit"), new NativeMenu
				{
					// ME.04: the rating of the book of the current tab, or of the selected books of the browser
					RatingItem(vm),
				}),
				Submenu(Text("browseMenu", "&Browse"), new NativeMenu
				{
					BrowserItem(vm),
					new NativeMenuItemSeparator(),
					SidebarItem(vm),
				}),
				Submenu(Text("readMenu", "&Read"), new NativeMenu
				{
					// MR.01 to MR.04
					Item(Text("miFirstPage", "&First Page"), vm.ReaderCommand(m => m.FirstPageCommand), Key.B),
					Item(Text("miPrevPage", "&Previous Page"), vm.ReaderCommand(m => m.PreviousPageCommand), Key.P),
					Item(Text("miNextPage", "&Next Page"), vm.ReaderCommand(m => m.NextPageCommand), Key.N),
					Item(Text("miLastPage", "&Last Page"), vm.ReaderCommand(m => m.LastPageCommand), Key.E),
					new NativeMenuItemSeparator(),
					// MR.08: Ctrl+F3 of the original (Cmd+F3 shows the desktop)
					Item(Text("miSyncBrowser", "Show in &Browser"), vm.ShowInBrowserCommand, Key.F3, KeyModifiers.Meta | KeyModifiers.Alt),
					new NativeMenuItemSeparator(),
					// MR.09, MR.10
					Item(Text("miPrevTab", "&Previous Tab"), vm.PreviousTabCommand, Key.J, KeyModifiers.Meta | KeyModifiers.Shift),
					Item(Text("miNextTab", "Next &Tab"), vm.NextTabCommand, Key.K, KeyModifiers.Meta | KeyModifiers.Shift),
					new NativeMenuItemSeparator(),
					// MR.13
					TrackItem(vm),
				}),
				Submenu(Text("displayMenu", "&Display"), new NativeMenu
				{
					Submenu(Text("miPageLayout", "&Page Layout"), new NativeMenu
					{
						// MD.03, MD.04, MD.06, MD.08 to MD.10, MD.12 with Cmd+2, 3, 5, 7, 8, 9, 0
						FitItem(vm, Text("miFitAll", "Fit &All"), m => m.FitAllCommand, Key.D2, ImageFitMode.Fit),
						FitItem(vm, Text("miFitWidth", "Fit &Width"), m => m.FitWidthCommand, Key.D3, ImageFitMode.FitWidth),
						FitItem(vm, Text("miFitHeight", "Fit &Height"), m => m.FitHeightCommand, Key.D5, ImageFitMode.FitHeight),
						new NativeMenuItemSeparator(),
						LayoutItem(vm, Text("miSinglePage", "Single Page"), m => m.SingleLayoutCommand, Key.D7, PageLayoutMode.Single),
						LayoutItem(vm, Text("miTwoPages", "Two Pages"), m => m.TwoPageLayoutCommand, Key.D8, PageLayoutMode.Double),
						LayoutItem(vm, Text("miTwoPagesAdaptive", "Two Pages (adaptive)"), m => m.AdaptiveTwoPageLayoutCommand, Key.D9, PageLayoutMode.DoubleAdaptive),
						DirectionItem(vm),
					}),
					Submenu(Text("miZoom", "Zoom"), new NativeMenu
					{
						Item(Text("miZoomIn", "Zoom &In"), vm.ReaderCommand(m => m.ZoomInCommand), Key.OemPlus),
						Item(Text("miZoomOut", "Zoom &Out"), vm.ReaderCommand(m => m.ZoomOutCommand), Key.OemMinus),
						new NativeMenuItemSeparator(),
						Item(Text("miZoom100", "100%"), vm.ReaderCommand(m => m.Zoom100Command)),
						Item(Text("miZoom125", "125%"), vm.ReaderCommand(m => m.Zoom125Command)),
						Item(Text("miZoom150", "150%"), vm.ReaderCommand(m => m.Zoom150Command)),
						Item(Text("miZoom200", "200%"), vm.ReaderCommand(m => m.Zoom200Command)),
						Item(Text("miZoom400", "400%"), vm.ReaderCommand(m => m.Zoom400Command)),
					}),
					new NativeMenuItemSeparator(),
					FullScreenItem(vm),
					UndockItem(vm),
				}),
				Submenu(UiText.Get("MacOnly", "WindowMenu", "Window"), new NativeMenu
				{
					Item(UiText.Get("MacOnly", "Minimize", "Minimize"), vm.MinimizeCommand, Key.M),
					Item(UiText.Get("MacOnly", "Zoom", "Zoom"), vm.ZoomCommand),
					new NativeMenuItemSeparator(),
					PerfItem(perf),
				}),
				Submenu(Text("helpMenu", "&Help"), new NativeMenu
				{
					// MH.02: F1 (alias Cmd+? through Aliases); the documentation is P5.31.
					Item(Text("miWebHelp", "ComicRack Documentation..."), vm.ShowHelpCommand, Key.F1, KeyModifiers.None),
				}),
			};
		}

		// Edit > My Rating (miRating, ME.04): None, 1 to 5 stars, Quick Rating and Review..., Option+Shift+0 to 5 and Q. The picture of five stars the original's
		// menu hosts is a control, which a native menu cannot hold; it is in the context menu of the browser (BC.07). The marks and the enabled state follow the
		// rating target when the menu opens (IEditRating.IsValid, GetRating).
		private static NativeMenuItem RatingItem(MainViewModel vm)
		{
			const KeyModifiers OptionShift = KeyModifiers.Alt | KeyModifiers.Shift;
			string[] names = { "None", "* (1 Star)", "** (2 Stars)", "*** (3 Stars)", "**** (4 Stars)", "***** (5 Stars)" };
			var menu = new NativeMenu();
			var rated = new List<NativeMenuItem>();
			for (int stars = 0; stars <= 5; stars++)
			{
				int value = stars;
				if (stars == 1)
				{
					menu.Items.Add(new NativeMenuItemSeparator());
				}
				NativeMenuItem item = Item(Text("miRate" + stars, names[stars]), new RelayCommand(() => vm.Rate(value), () => vm.CanRate), (Key)((int)Key.D0 + stars), OptionShift);
				item.ToggleType = MenuItemToggleType.Radio;
				rated.Add(item);
				menu.Items.Add(item);
			}
			menu.Items.Add(new NativeMenuItemSeparator());
			NativeMenuItem quick = Item(Text("miQuickRating", "Quick Rating and Review..."), vm.QuickRatingCommand, Key.Q, OptionShift);
			menu.Items.Add(quick);
			menu.Opening += (sender, e) =>
			{
				int current = vm.CurrentRating < 0 ? -1 : (int)Math.Round(vm.CurrentRating);
				for (int i = 0; i < rated.Count; i++)
				{
					rated[i].IsChecked = i == current;
					((RelayCommand)rated[i].Command).NotifyCanExecuteChanged();
				}
			};
			return Submenu(Text("miRating", "My R&ating"), menu);
		}

		// Read > Track current Page (Option+Shift+T, MR.13): a check mark that follows the setting.
		private static NativeMenuItem TrackItem(MainViewModel vm)
		{
			NativeMenuItem item = Item(Text("miTrackCurrentPage", "Track current Page"), vm.ToggleTrackCurrentPageCommand, Key.T, KeyModifiers.Alt | KeyModifiers.Shift);
			item.ToggleType = MenuItemToggleType.CheckBox;
			item.IsChecked = vm.IsTrackingCurrentPage;
			vm.PropertyChanged += (sender, e) =>
			{
				if (e.PropertyName == nameof(MainViewModel.IsTrackingCurrentPage))
				{
					item.IsChecked = vm.IsTrackingCurrentPage;
				}
			};
			return item;
		}

		// Browse > Browser (F3, MB.01): a check mark that follows the browser dock.
		private static NativeMenuItem BrowserItem(MainViewModel vm)
		{
			NativeMenuItem item = Item(Text("miToggleBrowser", "&Browser"), vm.ToggleBrowserCommand, Key.F3, KeyModifiers.None);
			item.ToggleType = MenuItemToggleType.CheckBox;
			item.IsChecked = vm.IsBrowserVisible;
			vm.PropertyChanged += (sender, e) =>
			{
				if (e.PropertyName == nameof(MainViewModel.IsBrowserVisible))
				{
					item.IsChecked = vm.IsBrowserVisible;
				}
			};
			return item;
		}

		// Display > Reader in own Window (F12, MD.26): checked while the reader is in its own window.
		private static NativeMenuItem UndockItem(MainViewModel vm)
		{
			NativeMenuItem item = Item(Text("miReaderUndocked", "Reader in &own Window"), vm.ToggleReaderUndockedCommand, Key.F12, KeyModifiers.None);
			item.ToggleType = MenuItemToggleType.CheckBox;
			item.IsChecked = vm.IsReaderUndocked;
			vm.PropertyChanged += (sender, e) =>
			{
				if (e.PropertyName == nameof(MainViewModel.IsReaderUndocked))
				{
					item.IsChecked = vm.IsReaderUndocked;
				}
			};
			return item;
		}

		// File > Open Books (miOpenNow, MF.15): one item per open book with its tab's caption, the current one marked; Ctrl+Alt+F1 to F12 of the original on the first twelve.
		private static NativeMenuItem OpenBooksItem(MainViewModel vm)
		{
			var item = new NativeMenuItem(Text("miOpenNow", "Open Books")) { Menu = new NativeMenu() };
			void Fill()
			{
				var menu = new NativeMenu();
				int current = vm.Tabs?.CurrentSlot ?? -1;
				foreach ((int index, string caption) in vm.OpenBookSlots)
				{
					int tab = index;
					var entry = new NativeMenuItem(caption)
					{
						ToggleType = MenuItemToggleType.Radio,
						IsChecked = tab == current,
						Command = new RelayCommand(() => vm.ShowTab(tab)),
					};
					if (tab < 12)
					{
						entry.Gesture = new KeyGesture(Key.F1 + tab, KeyModifiers.Meta | KeyModifiers.Control);
					}
					menu.Items.Add(entry);
				}
				item.Menu = menu;
				item.IsEnabled = menu.Items.Count > 0;
			}
			Fill();
			if (vm.Tabs != null)
			{
				vm.Tabs.SlotsChanged += (sender, e) => Fill();
				vm.Tabs.CurrentSlotChanged += (sender, e) => Fill();
				vm.Tabs.CaptionsChanged += (sender, e) => Fill();
			}
			return item;
		}

		// Calls apply now and whenever the book of the current tab changes or one of its properties does (check marks of the Display menu).
		private static void FollowReader(MainViewModel vm, Action<ReaderViewModel> apply)
		{
			apply(vm.Reader);
			vm.ReaderChanged += (sender, e) => apply(vm.Reader);
			vm.ReaderPropertyChanged += (sender, e) => apply(vm.Reader);
		}

		// A radio item whose mark follows the fit mode of the current book (MD.03, MD.04, MD.06).
		private static NativeMenuItem FitItem(MainViewModel vm, string header, Func<ReaderViewModel, ICommand> pick, Key key, ImageFitMode mode)
		{
			NativeMenuItem item = Item(header, vm.ReaderCommand(pick), key);
			item.ToggleType = MenuItemToggleType.Radio;
			FollowReader(vm, reader => item.IsChecked = reader != null && reader.Fit == mode);
			return item;
		}

		// A radio item whose mark follows the page layout of the current book (MD.08 to MD.10).
		private static NativeMenuItem LayoutItem(MainViewModel vm, string header, Func<ReaderViewModel, ICommand> pick, Key key, PageLayoutMode mode)
		{
			NativeMenuItem item = Item(header, vm.ReaderCommand(pick), key);
			item.ToggleType = MenuItemToggleType.Radio;
			FollowReader(vm, reader => item.IsChecked = reader != null && reader.LayoutMode == mode);
			return item;
		}

		// Right to Left (MD.12, Cmd+0): a check mark that follows the reading direction of the current book.
		private static NativeMenuItem DirectionItem(MainViewModel vm)
		{
			NativeMenuItem item = Item(Text("miRightToLeft", "Right to Left"), vm.ReaderCommand(m => m.ToggleRightToLeftCommand), Key.D0);
			item.ToggleType = MenuItemToggleType.CheckBox;
			FollowReader(vm, reader => item.IsChecked = reader != null && reader.RightToLeft);
			return item;
		}

		/// <summary>Window > Show Performance Overlay (Ctrl+Option+Cmd+P), a check mark that follows the overlay. Disabled when there is no overlay.</summary>
		public static NativeMenuItem PerfItem(PerfOverlayController perf)
		{
			string header = UiText.Get("MacOnly", "ShowPerformanceOverlay", "Show Performance Overlay");
			if (perf == null)
			{
				return new NativeMenuItem(header) { IsEnabled = false };
			}
			NativeMenuItem item = Item(header, perf.ToggleCommand, PerfKey, PerfModifiers);
			item.ToggleType = MenuItemToggleType.CheckBox;
			item.IsChecked = perf.IsVisible;
			perf.PropertyChanged += (sender, e) =>
			{
				if (e.PropertyName == nameof(PerfOverlayController.IsVisible))
				{
					item.IsChecked = perf.IsVisible;
				}
			};
			return item;
		}

		private static string Text(string key, string english) => UiText.Get("MainForm", key, english);

		// Browse > Sidebar (Shift+F6, MB.05): a check mark that follows the side panel.
		private static NativeMenuItem SidebarItem(MainViewModel vm)
		{
			NativeMenuItem item = Item(Text("miSidebar", "&Sidebar"), vm.ToggleSidebarCommand, Key.F6, KeyModifiers.Shift);
			item.ToggleType = MenuItemToggleType.CheckBox;
			item.IsChecked = vm.Sidebar.IsVisible;
			vm.Sidebar.PropertyChanged += (sender, e) =>
			{
				if (e.PropertyName == nameof(SidebarViewModel.IsVisible))
				{
					item.IsChecked = vm.Sidebar.IsVisible;
				}
			};
			return item;
		}

		// Display > Full Screen (F11, alias Ctrl+Cmd+F, MD.25): checked while the window is full screen, as the original's item is.
		private static NativeMenuItem FullScreenItem(MainViewModel vm)
		{
			NativeMenuItem item = Item(Text("miFullScreen", "&Full Screen"), vm.ToggleFullScreenCommand, Key.F11, KeyModifiers.None);
			item.ToggleType = MenuItemToggleType.CheckBox;
			item.IsChecked = vm.IsFullScreen;
			vm.PropertyChanged += (sender, e) =>
			{
				if (e.PropertyName == nameof(MainViewModel.IsFullScreen))
				{
					item.IsChecked = vm.IsFullScreen;
				}
			};
			return item;
		}

		private static NativeMenuItem Item(string header, ICommand command, Key key = Key.None, KeyModifiers modifiers = KeyModifiers.Meta)
		{
			var item = new NativeMenuItem(header);
			if (command != null)
			{
				item.Command = command;
			}
			if (key != Key.None)
			{
				item.Gesture = new KeyGesture(key, modifiers);
			}
			return item;
		}

		private static NativeMenuItem Submenu(string header, NativeMenu menu)
		{
			return new NativeMenuItem(header) { Menu = menu };
		}
	}
}
