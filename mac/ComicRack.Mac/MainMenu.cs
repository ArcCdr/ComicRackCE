using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using ComicRack.Mac.Diagnostics;
using ComicRack.Mac.ViewModels;
using CommunityToolkit.Mvvm.Input;

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

		/// <summary>The menu bar: File, Edit, Browse, Read, Display, Window, Help.</summary>
		public static NativeMenu BuildMenuBar(MainViewModel vm, PerfOverlayController perf = null)
		{
			return new NativeMenu
			{
				Submenu(Text("fileMenu", "&File"), new NativeMenu
				{
					// MF.05: the MVP's Open Folder. The original asks for a name or path with a Browse button and scans in the background (P5.10a).
					Item(Text("miAddFolderToLibrary", "&Add Folder to Library..."), vm.OpenFolderCommand, Key.A, KeyModifiers.Meta | KeyModifiers.Shift),
					// Import from a Windows installation has no counterpart in the original: a Mac-only addition (ledger MA.04).
					Item(UiText.Get("MacOnly", "ImportWindowsLibrary", "Import Windows Library…"), vm.ImportWindowsLibraryCommand),
				}),
				Submenu(Text("editMenu", "&Edit"), new NativeMenu()),
				Submenu(Text("browseMenu", "&Browse"), new NativeMenu
				{
					SidebarItem(vm),
				}),
				Submenu(Text("readMenu", "&Read"), new NativeMenu()),
				Submenu(Text("displayMenu", "&Display"), new NativeMenu
				{
					FullScreenItem(vm),
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

		private static NativeMenuItem Item(string header, IRelayCommand command, Key key = Key.None, KeyModifiers modifiers = KeyModifiers.Meta)
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
