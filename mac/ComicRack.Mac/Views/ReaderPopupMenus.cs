using System.Collections.Generic;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input;
using ComicRack.Mac.Controls;
using ComicRack.Mac.ViewModels;
using cYo.Projects.ComicRack.Engine.Display;

namespace ComicRack.Mac.Views
{
	/// <summary>
	/// The popup menus of the reader (C-MENU-3: original items, order, shortcuts and 16 unit pictures; the rendering is the popup style of the chrome
	/// layer): the drop-downs of the reader toolbar (<c>mainToolStrip</c>: TB.01 to TB.06, TB.09), and the page menu (<c>pageContextMenu</c>: PC.02, PC.06). Only the items the reader can do are there (C-MENU-4); the others arrive with the tasks of the ledger. The texts are the
	/// original's through TR (section MainForm, key = the item's name), the shortcuts the ledger's.
	/// </summary>
	public static class ReaderPopupMenus
	{
		private static string Text(string key, string english) => UiText.Get("MainForm", key, english);

		private static MenuItem Item(string header, string icon, ICommand command, Key key = Key.None, KeyModifiers modifiers = KeyModifiers.Meta)
		{
			var item = new MenuItem { Header = header, Command = command };
			if (!string.IsNullOrEmpty(icon))
			{
				item.Icon = new CrIcon { IconName = icon };
			}
			if (key != Key.None)
			{
				item.InputGesture = new KeyGesture(key, modifiers);
			}
			return item;
		}

		// A radio item whose mark follows a state of the view model.
		private static MenuItem Choice(ReaderViewModel vm, string header, string icon, ICommand command, Key key, System.Func<bool> isChosen, params string[] watched)
		{
			MenuItem item = Item(header, icon, command, key);
			item.ToggleType = MenuItemToggleType.Radio;
			item.GroupName = "reader-" + header;
			item.IsChecked = isChosen();
			vm.PropertyChanged += (sender, e) =>
			{
				if (System.Array.IndexOf(watched, e.PropertyName) >= 0)
				{
					item.IsChecked = isChosen();
				}
			};
			return item;
		}

		private static MenuItem Check(ReaderViewModel vm, string header, string icon, ICommand command, Key key, System.Func<bool> isOn, params string[] watched)
		{
			MenuItem item = Item(header, icon, command, key);
			item.ToggleType = MenuItemToggleType.CheckBox;
			item.IsChecked = isOn();
			vm.PropertyChanged += (sender, e) =>
			{
				if (System.Array.IndexOf(watched, e.PropertyName) >= 0)
				{
					item.IsChecked = isOn();
				}
			};
			return item;
		}

		private static MenuFlyout Flyout(IEnumerable<Control> items)
		{
			var flyout = new MenuFlyout();
			foreach (Control item in items)
			{
				flyout.Items.Add(item);
			}
			return flyout;
		}

		private static Separator Line() => new Separator();

		// ---- the toolbar drop-downs -----------------------------------------------------------------------------------

		/// <summary>The arrow of the Previous Page button (TB.01): First Page. The bookmark and book entries come with P5.23a.</summary>
		public static MenuFlyout PreviousPage(ReaderViewModel vm)
		{
			return Flyout(new Control[]
			{
				Item(Text("tbFirstPage", "&First Page"), "GoFirst", vm.FirstPageCommand, Key.B),
			});
		}

		/// <summary>The arrow of the Next Page button (TB.02): Last Page.</summary>
		public static MenuFlyout NextPage(ReaderViewModel vm)
		{
			return Flyout(new Control[]
			{
				Item(Text("tbLastPage", "&Last Page"), "GoLast", vm.LastPageCommand, Key.E),
			});
		}

		/// <summary>The arrow of the Page Layout button (TB.03).</summary>
		public static MenuFlyout PageLayout(ReaderViewModel vm)
		{
			return Flyout(new Control[]
			{
				Choice(vm, Text("tbSinglePage", "Single Page"), "SinglePage", vm.SingleLayoutCommand, Key.D7, () => vm.LayoutMode == PageLayoutMode.Single, nameof(ReaderViewModel.LayoutMode)),
				Choice(vm, Text("tbTwoPages", "Two Pages"), "TwoPageForced", vm.TwoPageLayoutCommand, Key.D8, () => vm.LayoutMode == PageLayoutMode.Double, nameof(ReaderViewModel.LayoutMode)),
				Choice(vm, Text("tbTwoPagesAdaptive", "Two Pages (adaptive)"), "TwoPage", vm.AdaptiveTwoPageLayoutCommand, Key.D9, () => vm.LayoutMode == PageLayoutMode.DoubleAdaptive, nameof(ReaderViewModel.LayoutMode)),
				Line(),
				Check(vm, Text("tbRightToLeft", "Right to Left"), "RightToLeft", vm.ToggleRightToLeftCommand, Key.D0, () => vm.RightToLeft, nameof(ReaderViewModel.RightToLeft)),
			});
		}

		/// <summary>The arrow of the Fit button (TB.04).</summary>
		public static MenuFlyout Fit(ReaderViewModel vm)
		{
			return Flyout(new Control[]
			{
				Choice(vm, Text("tbFitAll", "Fit All"), "FitAll", vm.FitAllCommand, Key.D2, () => vm.Fit == ImageFitMode.Fit, nameof(ReaderViewModel.Fit)),
				Choice(vm, Text("tbFitWidth", "Fit Width"), "FitWidth", vm.FitWidthCommand, Key.D3, () => vm.Fit == ImageFitMode.FitWidth, nameof(ReaderViewModel.Fit)),
				Choice(vm, Text("tbFitHeight", "Fit Height"), "FitHeight", vm.FitHeightCommand, Key.D5, () => vm.Fit == ImageFitMode.FitHeight, nameof(ReaderViewModel.Fit)),
			});
		}

		/// <summary>The arrow of the Zoom button (TB.05): Zoom In, Zoom Out and the presets. Custom... comes with P5.23e.</summary>
		public static MenuFlyout Zoom(ReaderViewModel vm)
		{
			return Flyout(new Control[]
			{
				Item(Text("tbZoomIn", "Zoom &In"), "ZoomIn", vm.ZoomInCommand, Key.OemPlus),
				Item(Text("tbZoomOut", "Zoom &Out"), "ZoomOut", vm.ZoomOutCommand, Key.OemMinus),
				Line(),
				Item(Text("tbZoom100", "100%"), null, vm.Zoom100Command),
				Item(Text("tbZoom125", "125%"), null, vm.Zoom125Command),
				Item(Text("tbZoom150", "150%"), null, vm.Zoom150Command),
				Item(Text("tbZoom200", "200%"), null, vm.Zoom200Command),
				Item(Text("tbZoom400", "400%"), null, vm.Zoom400Command),
			});
		}

		/// <summary>The Tools button (TB.09): the items of <c>toolsContextMenu</c> that exist: Info, About and Exit.</summary>
		public static MenuFlyout Tools(MainViewModel vm)
		{
			return Flyout(new Control[]
			{
				// tbShowInfo (TB.09): the Info dialog of the book of the tab or the selected books
				Item(Text("tbShowInfo", "Info..."), "GetInfo", vm.ShowInfoCommand, Key.I),
				Item(Text("tbAbout", "&About..."), "About", vm.AboutCommand, Key.F1, KeyModifiers.Alt),
				Line(),
				Item(Text("tbExit", "&Exit"), null, vm.ExitCommand, Key.Q),
			});
		}

		// ---- the page menu --------------------------------------------------------------------------------------------

		/// <summary>The menu of a right click on the page (<c>pageContextMenu</c>): Info..., My Rating, Page Layout (PC.01).</summary>
		public static MenuFlyout Page(ReaderViewModel vm)
		{
			var rating = new MenuItem { Header = Text("cmRating", "My R&ating") };
			string[] names = { "None", "* (1 Star)", "** (2 Stars)", "*** (3 Stars)", "**** (4 Stars)", "***** (5 Stars)" };
			for (int stars = 0; stars <= 5; stars++)
			{
				int value = stars;
				if (stars == 1)
				{
					rating.Items.Add(Line());
				}
				var item = new MenuItem { Header = Text("cmRate" + stars, names[stars]), ToggleType = MenuItemToggleType.Radio, GroupName = "reader-rating", Command = new RelayAction(() => vm.SetRating(value)) };
				item.InputGesture = new KeyGesture((Key)((int)Key.D0 + stars), KeyModifiers.Alt | KeyModifiers.Shift);
				item.IsChecked = (int)System.Math.Round(vm.Rating) == stars;
				vm.PropertyChanged += (sender, e) =>
				{
					if (e.PropertyName == nameof(ReaderViewModel.Rating))
					{
						item.IsChecked = (int)System.Math.Round(vm.Rating) == value;
					}
				};
				rating.Items.Add(item);
			}
			var layout = new MenuItem { Header = Text("cmPageLayout", "Page Layout") };
			layout.Items.Add(Choice(vm, Text("cmFitAll", "Fit All"), "FitAll", vm.FitAllCommand, Key.D2, () => vm.Fit == ImageFitMode.Fit, nameof(ReaderViewModel.Fit)));
			layout.Items.Add(Choice(vm, Text("cmFitWidth", "Fit Width"), "FitWidth", vm.FitWidthCommand, Key.D3, () => vm.Fit == ImageFitMode.FitWidth, nameof(ReaderViewModel.Fit)));
			layout.Items.Add(Choice(vm, Text("cmFitHeight", "Fit Height"), "FitHeight", vm.FitHeightCommand, Key.D5, () => vm.Fit == ImageFitMode.FitHeight, nameof(ReaderViewModel.Fit)));
			layout.Items.Add(Line());
			layout.Items.Add(Choice(vm, Text("cmSinglePage", "Single Page"), "SinglePage", vm.SingleLayoutCommand, Key.D7, () => vm.LayoutMode == PageLayoutMode.Single, nameof(ReaderViewModel.LayoutMode)));
			layout.Items.Add(Choice(vm, Text("cmTwoPages", "Two Pages"), "TwoPageForced", vm.TwoPageLayoutCommand, Key.D8, () => vm.LayoutMode == PageLayoutMode.Double, nameof(ReaderViewModel.LayoutMode)));
			layout.Items.Add(Choice(vm, Text("cmTwoPagesAdaptive", "Two Pages (adaptive)"), "TwoPage", vm.AdaptiveTwoPageLayoutCommand, Key.D9, () => vm.LayoutMode == PageLayoutMode.DoubleAdaptive, nameof(ReaderViewModel.LayoutMode)));
			layout.Items.Add(Check(vm, Text("cmRightToLeft", "Right to Left"), "RightToLeft", vm.ToggleRightToLeftCommand, Key.D0, () => vm.RightToLeft, nameof(ReaderViewModel.RightToLeft)));
			return Flyout(new Control[]
			{
				// cmShowInfo, Cmd+I: the Info dialog of the book of the tab
				Item(Text("cmShowInfo", "Info..."), "GetInfo", vm.ShowInfoCommand, Key.I),
				rating,
				layout,
			});
		}

		/// <summary>A command that runs an action (the rating items carry their value in the closure).</summary>
		private sealed class RelayAction : ICommand
		{
			private readonly System.Action action;

			public RelayAction(System.Action action) => this.action = action;

			public event System.EventHandler CanExecuteChanged
			{
				add { }
				remove { }
			}

			public bool CanExecute(object parameter) => true;

			public void Execute(object parameter) => action();
		}
	}
}
