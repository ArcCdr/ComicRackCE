using Avalonia.Controls;
using Avalonia.Input;
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
		public static NativeMenu Build(ReaderViewModel vm)
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
