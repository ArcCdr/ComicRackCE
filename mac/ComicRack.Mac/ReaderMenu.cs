using Avalonia.Controls;
using Avalonia.Input;
using ComicRack.Mac.Diagnostics;
using ComicRack.Mac.ViewModels;
using CommunityToolkit.Mvvm.Input;
using cYo.Projects.ComicRack.Engine.Display;

namespace ComicRack.Mac
{
	/// <summary>
	/// The native menu bar of a reader window (it replaces the library window's bar while the reader is the front window): File >
	/// Close, View > the fit modes, Go > the page commands. Only the commands that have a Command-key shortcut get one; the arrows,
	/// Space, Home and End are handled by the window itself, because a menu shortcut without a modifier would take them from every
	/// text field too.
	/// </summary>
	public static class ReaderMenu
	{
		public static NativeMenu Build(ReaderViewModel vm, PerfOverlayController perf = null)
		{
			return new NativeMenu
			{
				Submenu("File", new NativeMenu
				{
					Item("Close Reader", vm.CloseCommand, Key.W),
				}),
				Submenu("View", new NativeMenu
				{
					FitItem(vm, "Best Fit", vm.FitBestCommand, Key.D2, ImageFitMode.Fit),
					FitItem(vm, "Fit Width", vm.FitWidthCommand, Key.D3, ImageFitMode.FitWidth),
					FitItem(vm, "Fit Height", vm.FitHeightCommand, Key.D5, ImageFitMode.FitHeight),
					new NativeMenuItemSeparator(),
					Item("Zoom In", vm.ZoomInCommand, Key.OemPlus),
					Item("Zoom Out", vm.ZoomOutCommand, Key.OemMinus),
					Item("Actual Fit", vm.ZoomResetCommand, Key.D0),
					new NativeMenuItemSeparator(),
					LayoutItem(vm, "One Page", vm.SingleLayoutCommand, Key.D1, PageLayoutMode.Single),
					LayoutItem(vm, "Two Pages", vm.TwoPageLayoutCommand, Key.D2, PageLayoutMode.Double),
					LayoutItem(vm, "Two Pages (Adaptive)", vm.AdaptiveTwoPageLayoutCommand, Key.D3, PageLayoutMode.DoubleAdaptive),
					DirectionItem(vm),
					new NativeMenuItemSeparator(),
					FullScreenItem(vm),
					MainMenu.PerfItem(perf),
				}),
				Submenu("Go", new NativeMenu
				{
					Item("Next Page", vm.NextPageCommand),
					Item("Previous Page", vm.PreviousPageCommand),
					Item("First Page", vm.FirstPageCommand),
					Item("Last Page", vm.LastPageCommand),
				}),
			};
		}

		// A radio-style item whose check mark follows the page layout (Option+Cmd+digit).
		private static NativeMenuItem LayoutItem(ReaderViewModel vm, string header, IRelayCommand command, Key key, PageLayoutMode mode)
		{
			NativeMenuItem item = Item(header, command);
			item.Gesture = new KeyGesture(key, KeyModifiers.Meta | KeyModifiers.Alt);
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

		private static NativeMenuItem DirectionItem(ReaderViewModel vm)
		{
			NativeMenuItem item = Item("Read Right to Left", vm.ToggleRightToLeftCommand);
			item.Gesture = new KeyGesture(Key.R, KeyModifiers.Meta | KeyModifiers.Alt);
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

		private static NativeMenuItem FullScreenItem(ReaderViewModel vm)
		{
			NativeMenuItem item = Item(vm.IsFullScreen ? "Exit Full Screen" : "Enter Full Screen", vm.ToggleFullScreenCommand);
			item.Gesture = new KeyGesture(Key.F, KeyModifiers.Meta | KeyModifiers.Control);
			vm.PropertyChanged += (sender, e) =>
			{
				if (e.PropertyName == nameof(ReaderViewModel.IsFullScreen))
				{
					item.Header = vm.IsFullScreen ? "Exit Full Screen" : "Enter Full Screen";
				}
			};
			return item;
		}

		// A radio-style item whose check mark follows the fit mode.
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

		private static NativeMenuItem Item(string header, IRelayCommand command, Key key = Key.None)
		{
			var item = new NativeMenuItem(header) { Command = command };
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
