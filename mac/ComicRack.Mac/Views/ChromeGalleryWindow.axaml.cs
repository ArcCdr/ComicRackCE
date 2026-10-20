using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ComicRack.Mac.Controls;
using ComicRack.Mac.Platform;
using ComicRack.Mac.Services;
using ComicRack.Mac.Themes;

namespace ComicRack.Mac.Views
{
	/// <summary>
	/// <c>--show ChromeGallery</c>: every custom control of the original's chrome and the restyled standard controls in their states, followed by one
	/// swatch per theme token of the current appearance (P5.T4). The window logs two probe lines (<c>Chrome gallery probe</c>) with the token, its
	/// colour and the middle of its swatch in the client area, so that the e2e case can read those pixels from a screenshot.
	/// </summary>
	public partial class ChromeGalleryWindow : Window
	{
		public ChromeGalleryWindow()
		{
			InitializeComponent();
			BuildToolBar();
			BuildTabBar();
			BuildStatusBar();
			BuildContainers();
			BuildHeaders();
			BuildTrees();
			BuildControls();
			BuildMenuAndScroll();
			BuildFonts();
			BuildSwatches();
			Opened += (sender, e) => Dispatcher.UIThread.Post(LogProbes, DispatcherPriority.Background);
			Part<Button>("AlertButton").Click += (sender, e) => ShowAlert();
			KeyDown += OnGalleryKeyDown;
		}

		/// <summary>Cmd+Shift+A shows the alert too (the e2e case presses it).</summary>
		private void OnGalleryKeyDown(object sender, Avalonia.Input.KeyEventArgs e)
		{
			if (e.Key == Avalonia.Input.Key.A && e.KeyModifiers == (Avalonia.Input.KeyModifiers.Meta | Avalonia.Input.KeyModifiers.Shift))
			{
				e.Handled = true;
				ShowAlert();
			}
		}

		/// <summary>The alert of C-DLG-1: the system alert where it runs, the alert window otherwise; the result goes to the log.</summary>
		private async void ShowAlert()
		{
			var options = new MacAlertOptions
			{
				Message = "Delete the list?",
				Informative = "The books stay in the library. This cannot be undone.",
				Buttons = new[] { "OK", "Cancel" },
				CancelIndex = 1,
				SuppressionText = "Do not ask me again",
				Style = MacAlertStyle.Warning,
			};
			bool native = MacAlert.UseNative && MacAlert.NativeSupported;
			AppLog.Info("Chrome gallery alert: opening (" + (native ? "system alert" : "alert window") + ").");
			MacAlertResult result = await MacAlert.ShowAsync(this, options);
			AppLog.Info("Chrome gallery alert: button " + result.ButtonIndex + ", suppressed " + (result.Suppressed ? "yes" : "no") + ".");
		}

		private T Part<T>(string name) where T : Control => this.FindControl<T>(name);

		private void BuildToolBar()
		{
			Part<CrToolButton>("ToolHot").DisplayState = ToolButtonState.Hot;
			Part<CrToolButton>("ToolPressed").DisplayState = ToolButtonState.Pressed;
			Part<CrToolButton>("ToolChecked").IsChecked = true;
			Part<CrSplitButton>("ToolSplitHot").DisplayState = ToolButtonState.Hot;
			foreach (CrSplitButton split in new[] { Part<CrSplitButton>("ToolSplit"), Part<CrSplitButton>("ToolSplitHot") })
			{
				split.DropDown = new MenuFlyout
				{
					Items =
					{
						new MenuItem { Header = "Fit Width" },
						new MenuItem { Header = "Fit Height" },
						new Separator(),
						new MenuItem { Header = "Original Size" },
					},
				};
			}
		}

		private void BuildTabBar()
		{
			CrTabBar bar = Part<CrTabBar>("TabBarSample");
			bar.Tabs.Add(new CrTabItem { Text = "Comic 1" });
			bar.Tabs.Add(new CrTabItem { Text = "A second book with a longer title" });
			bar.Tabs.Add(new CrTabItem { Text = "Third", CanClose = false });
			bar.SelectedIndex = 1;
			var slot = new CrToolBar { Transparent = true };
			slot.Children.Add(new CrToolButton { IconName = "GoPrevious" });
			slot.Children.Add(new CrToolButton { IconName = "GoNext" });
			slot.Children.Add(new CrToolSeparator());
			slot.Children.Add(new CrToolButton { IconName = "Tools" });
			bar.ToolbarSlot = slot;
		}

		private void BuildStatusBar()
		{
			CrStatusBar bar = Part<CrStatusBar>("StatusBarSample");
			var slider = new Slider { Width = 90, Minimum = 0, Maximum = 100, Value = 40, VerticalAlignment = VerticalAlignment.Center };
			var sliderSlot = new CrStatusSlot { Width = 100, Height = 22, Margin = new Thickness(0, 1, 0, 1), Sunken = false, Child = slider };
			bar.AddRight(sliderSlot);
			bar.AddRight(new CrStatusSlot { IconName = "Refresh" });
			bar.AddRight(new CrStatusSlot { IconName = "Search" });
			bar.AddRight(new CrStatusSlot { IconName = "UpdatePages" });
			bar.SetSpring(new TextBlock { Text = "14.706 books" });
		}

		private void BuildContainers()
		{
			StackPanel host = Part<StackPanel>("Containers");
			host.Children.Add(Container(GripPosition.Top, false, c => { }));
			host.Children.Add(Container(GripPosition.Left, false, c => c.PreviewGrip(true, false)));
			host.Children.Add(Container(GripPosition.Right, false, c => c.PreviewGrip(false, true)));
			host.Children.Add(Container(GripPosition.Bottom, true, c => { }));
		}

		private static CrSizableContainer Container(GripPosition grip, bool collapsed, Action<CrSizableContainer> adjust)
		{
			var container = new CrSizableContainer
			{
				Grip = grip,
				ExpandedExtent = 120,
				AnimateExpand = false,
				KeepGripVisible = true,
				VerticalAlignment = VerticalAlignment.Top,
				HorizontalAlignment = HorizontalAlignment.Left,
				Child = new Border { BorderThickness = new Thickness(1), Child = new TextBlock { Text = grip + " grip", Margin = new Thickness(6), VerticalAlignment = VerticalAlignment.Top } },
			};
			((Border)container.Child).Bind(Border.BackgroundProperty, container.GetResourceObservable(ThemeTokens.BrushKey(ThemeTokens.Names.Window)));
			((Border)container.Child).Bind(Border.BorderBrushProperty, container.GetResourceObservable(ThemeTokens.BrushKey(ThemeTokens.Names.ControlDark)));
			if (grip == GripPosition.Left || grip == GripPosition.Right)
			{
				container.Height = 70;
			}
			else
			{
				container.Width = 150;
			}
			container.IsExpanded = !collapsed;
			adjust(container);
			return container;
		}

		private void BuildHeaders()
		{
			StackPanel host = Part<StackPanel>("Headers");
			host.Children.Add(new CrHeader { Text = "Normal", Width = 110, DisplayState = HeaderState.Normal });
			host.Children.Add(new CrHeader { Text = "Hot", Width = 110, DisplayState = HeaderState.Hot });
			host.Children.Add(new CrHeader { Text = "Pressed", Width = 110, DisplayState = HeaderState.Pressed });
			host.Children.Add(new CrHeader { Text = "Active", Width = 110, DisplayState = HeaderState.Active, Adornments = HeaderAdornments.SortDown, Active = true });
			host.Children.Add(new CrHeader { Text = "Sort up", Width = 110, DisplayState = HeaderState.Normal, Adornments = HeaderAdornments.SortUp });
			host.Children.Add(new CrHeader { Text = "Drop-down", Width = 130, DisplayState = HeaderState.Normal, Adornments = HeaderAdornments.DropDown });
		}

		private void BuildTrees()
		{
			foreach (TreeView tree in new[] { Part<TreeView>("TreeFocused"), Part<TreeView>("TreeUnfocused") })
			{
				var library = new TreeViewItem { Header = "Library", IsExpanded = true };
				library.Items.Add(new TreeViewItem { Header = "All Comics" });
				var smart = new TreeViewItem { Header = "Smart Lists", IsExpanded = true };
				smart.Items.Add(new TreeViewItem { Header = "Recently Added" });
				smart.Items.Add(new TreeViewItem { Header = "Unread" });
				library.Items.Add(smart);
				library.Items.Add(new TreeViewItem { Header = "Folders" });
				tree.Items.Add(library);
				tree.Items.Add(new TreeViewItem { Header = "Favorites" });
				tree.SelectedItem = smart.Items[0];
			}
			Opened += (sender, e) => Part<TreeView>("TreeFocused").Focus();
		}

		private void BuildControls()
		{
			Part<ComboBox>("ComboSample").ItemsSource = new[] { "Fit Width", "Fit Height", "Fit Best" };
			var tabs = Part<TabControl>("TabControlSample");
			tabs.Items.Add(new TabItem { Header = "Reader", Content = new TextBlock { Text = "Tab page", Margin = new Thickness(8) } });
			tabs.Items.Add(new TabItem { Header = "Libraries", Content = new TextBlock { Text = "Another page", Margin = new Thickness(8) } });
			tabs.Items.Add(new TabItem { Header = "Behavior", Content = new TextBlock { Text = "Third page", Margin = new Thickness(8) } });
			var list = Part<ListBox>("ListSample");
			list.ItemsSource = new[] { "First item", "Second item", "Third item" };
			list.SelectedIndex = 1;
		}

		private void BuildMenuAndScroll()
		{
			var presenter = new MenuFlyoutPresenter();
			presenter.Items.Add(new MenuItem { Header = "Open", InputGesture = new Avalonia.Input.KeyGesture(Avalonia.Input.Key.O, Avalonia.Input.KeyModifiers.Meta) });
			presenter.Items.Add(new MenuItem { Header = "Fit Width", Icon = new CrIcon { IconName = "FitWidth" } });
			presenter.Items.Add(new Separator());
			presenter.Items.Add(new MenuItem { Header = "Disabled", IsEnabled = false });
			presenter.Items.Add(new MenuItem { Header = "Settings" });
			Part<Border>("MenuSample").Child = presenter;
			Part<ScrollViewer>("ScrollSample").Content = new Border { Width = 420, Height = 200, Child = new TextBlock { Text = "Scrollable content: overlay scroll bars appear on scrolling.", Margin = new Thickness(6) } };
		}

		private void BuildFonts()
		{
			StackPanel host = Part<StackPanel>("FontSamples");
			host.Children.Add(FontLine("Control 11: The quick brown fox jumps over the lazy dog", ChromeFonts.ControlSize, FontWeight.Normal));
			host.Children.Add(FontLine("Caption 9 bold: THE QUICK BROWN FOX", ChromeFonts.CaptionSize, FontWeight.Bold));
			host.Children.Add(FontLine("Item 12: The quick brown fox jumps over the lazy dog", ChromeFonts.ItemSize, FontWeight.Normal));
			host.Children.Add(FontLine("Group header 13.8: The quick brown fox", ChromeFonts.GroupHeaderSize, FontWeight.Normal));
			host.Children.Add(FontLine("Overlay 16: The quick brown fox", ChromeFonts.OverlaySize, FontWeight.Normal));
		}

		private static TextBlock FontLine(string text, double size, FontWeight weight)
		{
			return new TextBlock { Text = text, FontSize = size, FontWeight = weight };
		}

		private void BuildSwatches()
		{
			WrapPanel swatches = Part<WrapPanel>("Swatches");
			foreach (string name in TokenNames())
			{
				var swatch = new Border
				{
					Width = 140,
					Height = 40,
					BorderThickness = new Thickness(1),
				};
				swatch.Bind(Border.BackgroundProperty, swatch.GetResourceObservable(ThemeTokens.BrushKey(name)));
				swatch.Bind(Border.BorderBrushProperty, swatch.GetResourceObservable(ThemeTokens.BrushKey(ThemeTokens.Names.ControlDark)));
				var caption = new TextBlock { Text = name, FontSize = 10, TextTrimming = TextTrimming.CharacterEllipsis, Width = 140 };
				caption.Bind(TextBlock.ForegroundProperty, caption.GetResourceObservable(ThemeTokens.BrushKey(ThemeTokens.Names.WindowText)));
				var cell = new StackPanel { Margin = new Thickness(4), Spacing = 2 };
				cell.Children.Add(swatch);
				cell.Children.Add(caption);
				swatches.Children.Add(cell);
			}
		}

		/// <summary>The token names that exist in the dictionary of the current appearance, in the order of <see cref="ThemeTokens.Names"/>.</summary>
		private static string[] TokenNames()
		{
			return typeof(ThemeTokens.Names)
				.GetFields(BindingFlags.Public | BindingFlags.Static)
				.Where(f => f.IsLiteral && f.FieldType == typeof(string))
				.Select(f => (string)f.GetRawConstantValue())
				.Where(n => ThemeTokens.TryGetColor(n, out _))
				.ToArray();
		}

		/// <summary>One log line per probe swatch: token, colour and the centre of the swatch in client points; the e2e case reads the pixel there.</summary>
		private void LogProbes()
		{
			foreach ((string token, string name) in new[] { (ThemeTokens.Names.Highlight, "ProbeHighlight"), (ThemeTokens.Names.Window, "ProbeWindow") })
			{
				Control probe = Part<Control>(name);
				Point? centre = probe.TranslatePoint(new Point(probe.Bounds.Width / 2, probe.Bounds.Height / 2), this);
				if (centre == null)
				{
					continue;
				}
				Color color = ThemeTokens.Get(token);
				AppLog.Info(string.Format(System.Globalization.CultureInfo.InvariantCulture,
					"Chrome gallery probe: {0} #{1:X2}{2:X2}{3:X2} at {4:0},{5:0} in a client area of {6:0}x{7:0}",
					token, color.R, color.G, color.B, centre.Value.X, centre.Value.Y, Bounds.Width, Bounds.Height));
			}
		}
	}
}
