using System;
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using ComicRack.Mac.Controls;
using ComicRack.Mac.Diagnostics;
using ComicRack.Mac.ViewModels;

namespace ComicRack.Mac.Views
{
	public partial class MainWindow : Window
	{
		private PerfOverlayController perf;
		private MainViewModel model;
		private readonly List<MenuItem> sortItems = new List<MenuItem>();

		public MainWindow()
		{
			InitializeComponent();
			ApplyTexts();
			Arrange.Click += (sender, e) => Arrange.ShowDropDown();
			Arrange.DropDown = BuildSortMenu();
			Arrange.DropDownOpening += (sender, e) => CheckSortItem();
			PropertyChanged += OnWindowPropertyChanged;
			DataContextChanged += (sender, e) => Attach(DataContext as MainViewModel);
		}

		private CrSplitButton Arrange => this.FindControl<CrSplitButton>("ArrangeButton");

		/// <summary>The labels and tool tips of the toolbar, as the original names them (ComicBrowserControl.xml, MainForm.xml, PreferencesDialog.xml).</summary>
		private void ApplyTexts()
		{
			ToolTip.SetTip(this.FindControl<CrToolButton>("SidebarButton"), UiText.Get("ComicBrowserControl", "tbSidebar", "Sidebar"));
			ToolTip.SetTip(Arrange, UiText.Get("ComicBrowserControl", "tbbSort", "Arrange"));
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

		// The sort menu of the Arrange button: the choices of the MVP sort, one of them checked.
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
					if (model != null)
					{
						model.Browser.SortIndex = index;
					}
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
			model = next;
			if (model != null)
			{
				model.IsFullScreen = WindowState == WindowState.FullScreen;
				KeyBindings.Clear();
				KeyBindings.AddRange(MainMenu.Aliases(model));
				KeyBindings.Add(new KeyBinding { Gesture = MainMenu.FocusQuickSearchGesture, Command = new CommunityToolkit.Mvvm.Input.RelayCommand(FocusQuickSearch) });
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

		/// <summary>Adds the performance overlay (Ctrl+Option+Cmd+P) over the content. Call once.</summary>
		public PerfOverlay AttachPerfOverlay(PerfOverlayController controller)
		{
			perf = controller;
			var overlay = new PerfOverlay(controller, StopwatchClock.Now);
			this.FindControl<Grid>("ContentArea").Children.Add(overlay);
			return overlay;
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
