using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ComicRack.Mac.Controls;
using ComicRack.Mac.Diagnostics;
using ComicRack.Mac.Reader;
using ComicRack.Mac.Services;
using ComicRack.Mac.ViewModels;
using CommunityToolkit.Mvvm.Input;

namespace ComicRack.Mac.Views
{
	/// <summary>
	/// A reader window (<c>ReaderForm</c> with the tab strip and the toolbar of the main form until P5.32a docks it): the book tab with the reader
	/// toolbar, the page view with its overlays (page number, visible part, loading and message, navigation), the status bar and the native menu of the
	/// reader. The keys go to <see cref="ReaderViewModel.HandleKey"/>; Esc and Cmd+W close the window. Closing disposes the view model, which records the
	/// page in the book and releases the book (<see cref="Services.ReaderManager"/> does that through <see cref="Closed"/>; a window made without the
	/// manager, as the tests do, passes <paramref name="ownsModel"/>).
	/// </summary>
	public partial class ReaderWindow : Window
	{
		private readonly ReaderViewModel model;
		private readonly bool ownsModel;
		private readonly PerfOverlayController perf;
		private readonly INavigationThumbnailSource thumbnails;
		private Point? pointer;

		public ReaderWindow()
		{
			InitializeComponent();
		}

		public ReaderWindow(ReaderViewModel model, bool ownsModel = false, PerfOverlayController perf = null, INavigationThumbnailSource thumbnails = null)
		{
			InitializeComponent();
			this.model = model;
			this.ownsModel = ownsModel;
			this.thumbnails = thumbnails;
			DataContext = model;
			model.CloseRequested += OnCloseRequested;
			model.PropertyChanged += OnModelChanged;
			model.PageInfoChanged += OnPageInfoChanged;
			model.AboutRequested += OnAboutRequested;
			model.ExitRequested += OnExitRequested;
			this.perf = perf;
			if (perf != null)
			{
				this.FindControl<Grid>("ContentArea").Children.Add(new PerfOverlay(perf, StopwatchClock.Now));
			}
			NativeMenu.SetMenu(this, ReaderMenu.Build(model, perf, new RelayCommand(Minimize), new RelayCommand(ZoomWindow)));
			BuildTab();
			BuildToolbar();
			BuildOverlays();
			PageView.ContextFlyout = ReaderPopupMenus.Page(model);
			Opened += (sender, e) => ReadingArea.Focus();
		}

		public ReaderViewModel Model => model;

		public ReaderView ReadingArea => this.FindControl<ReaderView>("PageView");

		/// <summary>The book tab strip with the reader toolbar at its right.</summary>
		public CrTabBar Tabs => this.FindControl<CrTabBar>("TabBar");

		/// <summary>The reader toolbar (<c>mainToolStrip</c>).</summary>
		public CrToolBar ReaderToolbar => this.FindControl<CrToolBar>("ReaderToolBar");

		public CrTextOverlay PageInfo => this.FindControl<CrTextOverlay>("PageInfoOverlay");

		/// <summary>"Loading Page N..." in the middle of the page.</summary>
		public CrTextOverlay Loading => LoadingOverlay;

		/// <summary>The message in the middle of the page: opening the book, why it or a page could not be shown.</summary>
		public CrTextOverlay Message => MessageOverlay;

		/// <summary>The miniature of the page with the visible part, at the bottom right.</summary>
		public CrPartOverlay Part => PartOverlay;

		public CrNavigationOverlay Navigation => this.FindControl<CrNavigationOverlay>("NavigationOverlay");

		private void Minimize() => WindowState = WindowState.Minimized;

		private void ZoomWindow() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

		private void OnCloseRequested(object sender, EventArgs e)
		{
			Close();
		}

		// ---- the tab strip and the toolbar ----------------------------------------------------------------------------

		private CrTabItem tab;

		private void BuildTab()
		{
			tab = new CrTabItem { Text = model.BookSegmentText, CanClose = true };
			Tabs.Tabs.Add(tab);
			Tabs.SelectedIndex = 0;
			Tabs.TabCloseRequested += (sender, e) => model.Close();
		}

		private static void Tip(Control control, string key, string english)
		{
			ToolTip.SetTip(control, UiText.Tooltip("MainForm", key) ?? UiText.Get("MainForm", key, english));
		}

		private void BuildToolbar()
		{
			var previous = this.FindControl<CrSplitButton>("PreviousPageButton");
			var next = this.FindControl<CrSplitButton>("NextPageButton");
			var layout = this.FindControl<CrSplitButton>("PageLayoutButton");
			var fit = this.FindControl<CrSplitButton>("FitButton");
			var zoom = this.FindControl<CrSplitButton>("ZoomButton");
			var fullScreen = this.FindControl<CrToolButton>("FullScreenButton");
			var tools = this.FindControl<CrToolButton>("ToolsButton");
			previous.DropDown = ReaderPopupMenus.PreviousPage(model);
			next.DropDown = ReaderPopupMenus.NextPage(model);
			layout.DropDown = ReaderPopupMenus.PageLayout(model);
			fit.DropDown = ReaderPopupMenus.Fit(model);
			zoom.DropDown = ReaderPopupMenus.Zoom(model);
			tools.Flyout = ReaderPopupMenus.Tools(model);
			Tip(previous, "tbPrevPage", "Previous Page");
			Tip(next, "tbNextPage", "Next Page");
			Tip(layout, "tbPageLayout", "Page Layout");
			Tip(fit, "tbFit", "Toggle Fit Mode");
			Tip(zoom, "tbZoom", "Change the page zoom");
			Tip(fullScreen, "tbFullScreen", "Full Screen");
			Tip(tools, "tbTools", "Tools");
		}

		// ---- the overlays ---------------------------------------------------------------------------------------------

		private void BuildOverlays()
		{
			ReaderOptions options = model.Options;
			Navigation.AutoShow = options.ShowNavigationOverlay;
			Navigation.OnTop = options.NavigationOverlayOnTop;
			Navigation.VerticalAlignment = options.NavigationOverlayOnTop ? Avalonia.Layout.VerticalAlignment.Top : Avalonia.Layout.VerticalAlignment.Bottom;
			Navigation.Margin = options.NavigationOverlayOnTop ? new Thickness(0, 3, 0, 0) : new Thickness(0, 0, 0, 3);
			Navigation.Thumbnails = thumbnails;
			Navigation.Browse += OnNavigationBrowse;
			PartOverlay.ShowClose = !options.HideVisiblePartOverlayClose;
			PartOverlay.CloseRequested += (sender, e) =>
			{
				PartOverlay.StopFade();
				model.Options.ShowVisiblePagePartOverlay = false;
			};
			ReadingArea.AutoHideCursorDelay = options.AutoHideCursorDelay;
			ContentArea.PointerMoved += (sender, e) => OnContentPointerMoved(e);
			ContentArea.PointerExited += (sender, e) =>
			{
				pointer = null;
				UpdateNavigation();
			};
			ContentArea.AddHandler(PointerPressedEvent, OnContentPointerPressed, RoutingStrategies.Tunnel);
			SyncNavigation();
			UpdateStatusOverlays();
		}

		private void OnContentPointerMoved(PointerEventArgs e)
		{
			pointer = e.GetPosition(ContentArea);
			UpdateNavigation(e.GetCurrentPoint(ContentArea).Properties.IsLeftButtonPressed);
		}

		// OnMouseDown of the display control: a click on the page puts a navigation overlay that was toggled on away again.
		private void OnContentPointerPressed(object sender, PointerPressedEventArgs e)
		{
			if (model.NavigationOverlayVisible && e.GetCurrentPoint(ContentArea).Properties.IsLeftButtonPressed && !IsInside(e.Source as Visual, Navigation))
			{
				model.NavigationOverlayVisible = false;
			}
		}

		private static bool IsInside(Visual source, Visual ancestor)
		{
			for (Visual v = source; v != null; v = v.GetVisualParent())
			{
				if (ReferenceEquals(v, ancestor))
				{
					return true;
				}
			}
			return false;
		}

		private void UpdateNavigation(bool buttonDown = false)
		{
			Navigation.ForcedVisible = model.NavigationOverlayVisible;
			Navigation.HasContent = model.IsOpen && model.Scene != null;
			Navigation.UpdatePointer(pointer, ContentArea.Bounds.Size, buttonDown);
		}

		// The page count, the displayed page, the reading direction and the name of the book for the navigation overlay.
		private void SyncNavigation()
		{
			Navigation.PageCount = model.PageCount;
			Navigation.DisplayedPage = model.CurrentPage;
			Navigation.IsDoublePage = model.Scene != null && model.Scene.Pages.Count > 1;
			Navigation.Mirror = model.RightToLeft;
			Navigation.Caption = model.Title;
			UpdateNavigation();
		}

		private void OnNavigationBrowse(object sender, NavigationBrowseEventArgs e)
		{
			switch (e.Origin)
			{
				case NavigationOrigin.Beginning:
					model.FirstPage();
					break;
				case NavigationOrigin.End:
					model.LastPage();
					break;
				case NavigationOrigin.Current:
					if (e.Offset < 0)
					{
						model.PreviousPage();
					}
					else
					{
						model.NextPage();
					}
					break;
				default:
					model.BrowseTo(e.Offset);
					break;
			}
		}

		// The page number at the top right fades in when the page changes (UpdateCurrentPageOverlay).
		private void OnPageInfoChanged(object sender, EventArgs e)
		{
			PageInfo.Text = model.PageInfoText;
			if (model.Options.ShowCurrentPageOverlay)
			{
				PageInfo.StartFade();
			}
		}

		// "Loading Page N..." is shown while the message is not (UpdateLoadPageOverlay is skipped while messageOverlay.Visible); ShowStatusOverlay allows it.
		private void UpdateStatusOverlays()
		{
			MessageOverlay.Text = model.Notice;
			MessageOverlay.IsVisible = model.HasNotice;
			LoadingOverlay.Text = model.LoadingText;
			LoadingOverlay.IsVisible = model.Options.ShowStatusOverlay && model.HasLoading && !model.HasNotice;
		}

		// The miniature of the page with the visible part (UpdatePartOverlay): it follows the scene and the scroll, and fades in whenever the part changes
		// while the page is larger than the window.
		private void UpdatePartOverlay(bool partChanged)
		{
			ReaderPartInfo info = model.PartInfo;
			ReaderScene scene = model.Scene;
			if (info == null || scene == null || scene.Pages.Count == 0)
			{
				PartOverlay.Clear();
				return;
			}
			var pictures = new Browser.CoverImage[scene.Pages.Count];
			for (int i = 0; i < pictures.Length; i++)
			{
				pictures[i] = scene.Pages[i].Image;
			}
			PartOverlay.SetContent(pictures, info.Pages, info.Content, info.Visible);
			if (partChanged && info.CanPan && model.Options.ShowVisiblePagePartOverlay)
			{
				PartOverlay.StartFade();
			}
		}

		// ---- native full screen and the model -------------------------------------------------------------------------

		// Native full screen: the window follows the view model, and the view model follows the window (the green button, or macOS itself).
		private void OnModelChanged(object sender, PropertyChangedEventArgs e)
		{
			switch (e.PropertyName)
			{
				case nameof(ReaderViewModel.IsFullScreen):
					WindowState wanted = model.IsFullScreen ? WindowState.FullScreen : WindowState.Normal;
					if (WindowState != wanted && (model.IsFullScreen || WindowState == WindowState.FullScreen))
					{
						WindowState = wanted;
					}
					ReadingArea.AutoHideCursor = model.IsFullScreen && model.Options.HideCursorFullScreen;
					break;
				case nameof(ReaderViewModel.Title):
					if (tab != null)
					{
						tab.Text = model.BookSegmentText;
					}
					SyncNavigation();
					break;
				case nameof(ReaderViewModel.Notice):
				case nameof(ReaderViewModel.LoadingText):
					UpdateStatusOverlays();
					break;
				case nameof(ReaderViewModel.Scene):
					SyncNavigation();
					UpdatePartOverlay(true);
					break;
				case nameof(ReaderViewModel.CurrentPage):
				case nameof(ReaderViewModel.RightToLeft):
					SyncNavigation();
					break;
				case nameof(ReaderViewModel.NavigationOverlayVisible):
					UpdateNavigation();
					break;
				case nameof(ReaderViewModel.ScrollX):
				case nameof(ReaderViewModel.ScrollY):
				case nameof(ReaderViewModel.Zoom):
				case nameof(ReaderViewModel.Fit):
				case nameof(ReaderViewModel.ViewportSize):
					UpdatePartOverlay(true);
					break;
			}
		}

		protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
		{
			base.OnPropertyChanged(change);
			if (change.Property == WindowStateProperty && model != null)
			{
				bool full = WindowState == WindowState.FullScreen;
				if (model.IsFullScreen != full)
				{
					model.IsFullScreen = full;
				}
			}
		}

		// Tools > About... (TB.09): the About window over the reader.
		private void OnAboutRequested(object sender, EventArgs e)
		{
			new AboutWindow().ShowDialog(this);
		}

		// Q and Tools > Exit (ControlExit): the application quits like Cmd+Q.
		private void OnExitRequested(object sender, EventArgs e)
		{
			if (Avalonia.Application.Current?.ApplicationLifetime is IControlledApplicationLifetime lifetime)
			{
				lifetime.Shutdown();
			}
			else
			{
				Close();
			}
		}

		protected override void OnKeyDown(KeyEventArgs e)
		{
			base.OnKeyDown(e);
			if (!e.Handled && perf != null && e.Key == MainMenu.PerfKey && e.KeyModifiers == MainMenu.PerfModifiers)
			{
				perf.Toggle();
				e.Handled = true;
				return;
			}
			if (!e.Handled && model != null && model.HandleKey(e.Key, e.KeyModifiers))
			{
				e.Handled = true;
			}
		}

		protected override void OnClosed(EventArgs e)
		{
			PartOverlay.Clear();
			Navigation.Thumbnails = null;
			if (model != null)
			{
				model.CloseRequested -= OnCloseRequested;
				model.PropertyChanged -= OnModelChanged;
				model.PageInfoChanged -= OnPageInfoChanged;
				model.AboutRequested -= OnAboutRequested;
				model.ExitRequested -= OnExitRequested;
				if (ownsModel)
				{
					model.Dispose();
				}
			}
			base.OnClosed(e);
		}
	}
}
