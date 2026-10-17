using Avalonia.Controls;
using Avalonia.Input;
using ComicRack.Mac.ViewModels;
using CommunityToolkit.Mvvm.Input;

namespace ComicRack.Mac
{
	/// <summary>
	/// The native macOS menus. Built in code so that every item visibly points at a view model command. Avalonia puts the
	/// items of the application's menu into the app menu (the one named after the app) and those of the window's menu into the
	/// menu bar.
	/// </summary>
	public static class MainMenu
	{
		/// <summary>
		/// The items of the app menu: About. Avalonia.Native appends its standard items (Services, Hide, Hide Others, Show All
		/// and Quit with Cmd+Q, which calls <c>TryShutdown(0)</c>) to the application's menu, so Quit is not added here (it would
		/// appear twice); the app logs it through <c>ShutdownRequested</c>.
		/// </summary>
		public static NativeMenu BuildAppMenu(MainViewModel vm)
		{
			return new NativeMenu
			{
				Item("About " + vm.AppName, vm.AboutCommand),
			};
		}

		/// <summary>The menu bar: File, View, Window, Help.</summary>
		public static NativeMenu BuildMenuBar(MainViewModel vm)
		{
			return new NativeMenu
			{
				Submenu("File", new NativeMenu
				{
					Item("Open Folder…", vm.OpenFolderCommand, Key.O),
					Item("Import Windows Library…", vm.ImportWindowsLibraryCommand),
				}),
				Submenu("View", new NativeMenu
				{
					SidebarItem(vm),
					Item("Enter Full Screen", vm.ToggleFullScreenCommand, Key.F, KeyModifiers.Meta | KeyModifiers.Control),
				}),
				Submenu("Window", new NativeMenu
				{
					Item("Minimize", vm.MinimizeCommand, Key.M),
					Item("Zoom", vm.ZoomCommand),
				}),
				Submenu("Help", new NativeMenu
				{
					Item("ComicRack CE Help", vm.ShowHelpCommand, Key.OemQuestion, KeyModifiers.Meta | KeyModifiers.Shift),
				}),
			};
		}

		// View > Show Sidebar: a check mark that follows the sidebar's visibility.
		private static NativeMenuItem SidebarItem(MainViewModel vm)
		{
			NativeMenuItem item = Item("Show Sidebar", vm.ToggleSidebarCommand, Key.S, KeyModifiers.Meta | KeyModifiers.Control);
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

		private static NativeMenuItem Item(string header, IRelayCommand command, Key key = Key.None, KeyModifiers modifiers = KeyModifiers.Meta)
		{
			var item = new NativeMenuItem(header) { Command = command };
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
