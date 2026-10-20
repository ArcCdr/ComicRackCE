using System;
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
	/// The native menu bar of a reader window (it replaces the library window's bar while the reader is the front window): the original's menus
	/// (<c>MainForm.Designer.cs</c>, C-MENU-1, C-MENU-4) with the items the reader can do. File has Close; Read has the page commands (MR.01 to MR.04,
	/// Cmd+B, P, N, E); Display has Page Layout (Fit All, Fit Width, Fit Height, Single Page, Two Pages, Two Pages (adaptive), Right to Left: MD.03,
	/// MD.04, MD.06, MD.08 to MD.10, MD.12 with Cmd+2, 3, 5, 7, 8, 9, 0), Zoom (Zoom In, Zoom Out, 100% to 400%) and Full Screen (F11, alias
	/// Ctrl+Cmd+F, a window key binding of <see cref="ReaderViewModel.HandleKey"/>); Window is the macOS window menu (C-MENU-2) with the performance
	/// overlay. Texts are the original's through TR (<see cref="UiText"/>), shortcuts the ledger's. Edit, Browse and Help stay empty in the reader.
	/// </summary>
	public static class ReaderMenu
	{
		public static NativeMenu Build(ReaderViewModel vm, PerfOverlayController perf = null, ICommand minimize = null, ICommand zoom = null)
		{
			return new NativeMenu
			{
				Submenu(Text("fileMenu", "&File"), new NativeMenu
				{
					Item(Text("miCloseComic", "&Close"), vm.CloseCommand, Key.W),
				}),
				Submenu(Text("editMenu", "&Edit"), new NativeMenu()),
				Submenu(Text("browseMenu", "&Browse"), new NativeMenu()),
				Submenu(Text("readMenu", "&Read"), new NativeMenu
				{
					Item(Text("miFirstPage", "&First Page"), vm.FirstPageCommand, Key.B),
					Item(Text("miPrevPage", "&Previous Page"), vm.PreviousPageCommand, Key.P),
					Item(Text("miNextPage", "&Next Page"), vm.NextPageCommand, Key.N),
					Item(Text("miLastPage", "&Last Page"), vm.LastPageCommand, Key.E),
				}),
				Submenu(Text("displayMenu", "&Display"), new NativeMenu
				{
					Submenu(Text("miPageLayout", "&Page Layout"), new NativeMenu
					{
						FitItem(vm, Text("miFitAll", "Fit &All"), vm.FitAllCommand, Key.D2, ImageFitMode.Fit),
						FitItem(vm, Text("miFitWidth", "Fit &Width"), vm.FitWidthCommand, Key.D3, ImageFitMode.FitWidth),
						FitItem(vm, Text("miFitHeight", "Fit &Height"), vm.FitHeightCommand, Key.D5, ImageFitMode.FitHeight),
						new NativeMenuItemSeparator(),
						LayoutItem(vm, Text("miSinglePage", "Single Page"), vm.SingleLayoutCommand, Key.D7, PageLayoutMode.Single),
						LayoutItem(vm, Text("miTwoPages", "Two Pages"), vm.TwoPageLayoutCommand, Key.D8, PageLayoutMode.Double),
						LayoutItem(vm, Text("miTwoPagesAdaptive", "Two Pages (adaptive)"), vm.AdaptiveTwoPageLayoutCommand, Key.D9, PageLayoutMode.DoubleAdaptive),
						DirectionItem(vm),
					}),
					Submenu(Text("miZoom", "Zoom"), new NativeMenu
					{
						Item(Text("miZoomIn", "Zoom &In"), vm.ZoomInCommand, Key.OemPlus),
						Item(Text("miZoomOut", "Zoom &Out"), vm.ZoomOutCommand, Key.OemMinus),
						new NativeMenuItemSeparator(),
						Item(Text("miZoom100", "100%"), vm.Zoom100Command),
						Item(Text("miZoom125", "125%"), vm.Zoom125Command),
						Item(Text("miZoom150", "150%"), vm.Zoom150Command),
						Item(Text("miZoom200", "200%"), vm.Zoom200Command),
						Item(Text("miZoom400", "400%"), vm.Zoom400Command),
					}),
					new NativeMenuItemSeparator(),
					FullScreenItem(vm),
				}),
				Submenu(UiText.Get("MacOnly", "WindowMenu", "Window"), new NativeMenu
				{
					Item(UiText.Get("MacOnly", "Minimize", "Minimize"), minimize, Key.M),
					Item(UiText.Get("MacOnly", "Zoom", "Zoom"), zoom),
					new NativeMenuItemSeparator(),
					MainMenu.PerfItem(perf),
				}),
				Submenu(Text("helpMenu", "&Help"), new NativeMenu()),
			};
		}

		private static string Text(string key, string english) => UiText.Get("MainForm", key, english);

		// A radio item whose mark follows the page layout (MD.08 to MD.10).
		private static NativeMenuItem LayoutItem(ReaderViewModel vm, string header, IRelayCommand command, Key key, PageLayoutMode mode)
		{
			NativeMenuItem item = Item(header, command, key);
			item.ToggleType = MenuItemToggleType.Radio;
			item.IsChecked = vm.LayoutMode == mode;
			vm.PropertyChanged += (sender, e) =>
			{
				if (e.PropertyName == nameof(ReaderViewModel.LayoutMode))
				{
					item.IsChecked = vm.LayoutMode == mode;
				}
			};
			return item;
		}

		// Right to Left (MD.12, Cmd+0): a check mark that follows the reading direction.
		private static NativeMenuItem DirectionItem(ReaderViewModel vm)
		{
			NativeMenuItem item = Item(Text("miRightToLeft", "Right to Left"), vm.ToggleRightToLeftCommand, Key.D0);
			item.ToggleType = MenuItemToggleType.CheckBox;
			item.IsChecked = vm.RightToLeft;
			vm.PropertyChanged += (sender, e) =>
			{
				if (e.PropertyName == nameof(ReaderViewModel.RightToLeft))
				{
					item.IsChecked = vm.RightToLeft;
				}
			};
			return item;
		}

		// Full Screen (MD.25): F11, alias Ctrl+Cmd+F, checked while the window is full screen as the original's item is.
		private static NativeMenuItem FullScreenItem(ReaderViewModel vm)
		{
			NativeMenuItem item = Item(Text("miFullScreen", "&Full Screen"), vm.ToggleFullScreenCommand);
			item.Gesture = new KeyGesture(Key.F11, KeyModifiers.None);
			item.ToggleType = MenuItemToggleType.CheckBox;
			item.IsChecked = vm.IsFullScreen;
			vm.PropertyChanged += (sender, e) =>
			{
				if (e.PropertyName == nameof(ReaderViewModel.IsFullScreen))
				{
					item.IsChecked = vm.IsFullScreen;
				}
			};
			return item;
		}

		// A radio item whose mark follows the fit mode (MD.03, MD.04, MD.06).
		private static NativeMenuItem FitItem(ReaderViewModel vm, string header, IRelayCommand command, Key key, ImageFitMode mode)
		{
			NativeMenuItem item = Item(header, command, key);
			item.ToggleType = MenuItemToggleType.Radio;
			item.IsChecked = vm.Fit == mode;
			vm.PropertyChanged += (sender, e) =>
			{
				if (e.PropertyName == nameof(ReaderViewModel.Fit))
				{
					item.IsChecked = vm.Fit == mode;
				}
			};
			return item;
		}

		private static NativeMenuItem Item(string header, ICommand command, Key key = Key.None)
		{
			var item = new NativeMenuItem(header);
			if (command != null)
			{
				item.Command = command;
			}
			if (key != Key.None)
			{
				item.Gesture = new KeyGesture(key, KeyModifiers.Meta);
			}
			return item;
		}

		private static NativeMenuItem Submenu(string header, NativeMenu menu)
		{
			return new NativeMenuItem(header) { Menu = menu };
		}
	}
}
