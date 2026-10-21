using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ComicRack.Mac.Controls;
using ComicRack.Mac.Reader;
using ComicRack.Mac.ViewModels;

namespace ComicRack.Mac.Views
{
	/// <summary>
	/// The reader area (<c>readerContainer</c> with the <c>ComicDisplayControl</c> of the original): the page view with its overlays (page number, visible part,
	/// loading and message, navigation). It shows the book of its DataContext, a <see cref="ReaderViewModel"/>; the main window sets it to the reader of the current
	/// book tab and sets it again when the tab changes, and the reader window (Reader in own Window) hosts the same control. Everything that is per book
	/// (the overlays' state, the native full screen of the window that hosts the area) follows the model: when the model changes the area lets go of the old
	/// one and sets itself up for the new one. The keys go to <see cref="ReaderViewModel.HandleKey"/>.
	/// </summary>
	public partial class ReaderArea : UserControl
	{
		private ReaderViewModel model;
		private Window hostWindow;
		private INavigationThumbnailSource thumbnails;
		private Point? pointer;

		public ReaderArea()
		{
			InitializeComponent();
			PartOverlay.CloseRequested += (sender, e) =>
			{
				PartOverlay.StopFade();
				if (model != null)
				{
					model.Options.ShowVisiblePagePartOverlay = false;
				}
			};
			Navigation.Browse += OnNavigationBrowse;
			ContentArea.PointerMoved += (sender, e) => OnContentPointerMoved(e);
			ContentArea.PointerExited += (sender, e) =>
			{
				pointer = null;
				UpdateNavigation();
			};
			ContentArea.AddHandler(PointerPressedEvent, OnContentPointerPressed, RoutingStrategies.Tunnel);
		}

		/// <summary>The reader whose book the area shows, or null.</summary>
		public ReaderViewModel Model => model;

		public ReaderView ReadingArea => PageView;

		public CrTextOverlay PageInfo => PageInfoOverlay;

		/// <summary>"Loading Page N..." in the middle of the page.</summary>
		public CrTextOverlay Loading => LoadingOverlay;

		/// <summary>The message in the middle of the page: opening the book, why it or a page could not be shown.</summary>
		public CrTextOverlay Message => MessageOverlay;

		/// <summary>The miniature of the page with the visible part, at the bottom right.</summary>
		public CrPartOverlay Part => PartOverlay;

		public CrNavigationOverlay Navigation => NavigationOverlay;

		/// <summary>The grid that holds the page and the overlays (the performance overlay of an undocked reader window is added there).</summary>
		public Grid Surface => ContentArea;

		/// <summary>The page pictures of the navigation overlay for the book of the model (the host sets it together with the DataContext).</summary>
		public INavigationThumbnailSource Thumbnails
		{
			get => thumbnails;
			set
			{
				thumbnails = value;
				Navigation.Thumbnails = value;
			}
		}

		/// <summary>The keyboard focus goes to the page, so that the reader's keys work.</summary>
		public void FocusReader() => PageView.Focus();

		// ---- the model

		protected override void OnDataContextChanged(EventArgs e)
		{
			base.OnDataContextChanged(e);
			Attach(DataContext as ReaderViewModel);
		}

		private void Attach(ReaderViewModel next)
		{
			if (ReferenceEquals(next, model))
			{
				return;
			}
			if (model != null)
			{
				model.PropertyChanged -= OnModelChanged;
				model.PageInfoChanged -= OnPageInfoChanged;
				model.AboutRequested -= OnAboutRequested;
				model.ExitRequested -= OnExitRequested;
				PartOverlay.Clear();
				PageInfo.Text = string.Empty;
				pointer = null;
			}
			model = next;
			ReadingArea.ContextFlyout = model == null ? null : ReaderPopupMenus.Page(model);
			if (model == null)
			{
				Navigation.HasContent = false;
				UpdateStatusOverlays();
				return;
			}
			model.PropertyChanged += OnModelChanged;
			model.PageInfoChanged += OnPageInfoChanged;
			model.AboutRequested += OnAboutRequested;
			model.ExitRequested += OnExitRequested;
			BuildOverlays();
			SyncFullScreenWithWindow();
		}

		private void BuildOverlays()
		{
			ReaderOptions options = model.Options;
			Navigation.AutoShow = options.ShowNavigationOverlay;
			Navigation.OnTop = options.NavigationOverlayOnTop;
			Navigation.VerticalAlignment = options.NavigationOverlayOnTop ? Avalonia.Layout.VerticalAlignment.Top : Avalonia.Layout.VerticalAlignment.Bottom;
			Navigation.Margin = options.NavigationOverlayOnTop ? new Thickness(0, 3, 0, 0) : new Thickness(0, 0, 0, 3);
			Navigation.Thumbnails = thumbnails;
			PartOverlay.ShowClose = !options.HideVisiblePartOverlayClose;
			ReadingArea.AutoHideCursorDelay = options.AutoHideCursorDelay;
			ReadingArea.AutoHideCursor = model.IsFullScreen && options.HideCursorFullScreen;
			PageInfo.Text = model.PageInfoText;
			SyncNavigation();
			UpdateStatusOverlays();
			UpdatePartOverlay(false);
		}

		private void OnContentPointerMoved(PointerEventArgs e)
		{
			pointer = e.GetPosition(ContentArea);
			UpdateNavigation(e.GetCurrentPoint(ContentArea).Properties.IsLeftButtonPressed);
		}

		// OnMouseDown of the display control: a click on the page puts a navigation overlay that was toggled on away again.
		private void OnContentPointerPressed(object sender, PointerPressedEventArgs e)
		{
			if (model != null && model.NavigationOverlayVisible && e.GetCurrentPoint(ContentArea).Properties.IsLeftButtonPressed && !IsInside(e.Source as Visual, Navigation))
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
			if (model == null)
			{
				return;
			}
			Navigation.ForcedVisible = model.NavigationOverlayVisible;
			Navigation.HasContent = model.IsOpen && model.Scene != null;
			Navigation.UpdatePointer(pointer, ContentArea.Bounds.Size, buttonDown);
		}

		// The page count, the displayed page, the reading direction and the name of the book for the navigation overlay.
		private void SyncNavigation()
		{
			if (model == null)
			{
				return;
			}
			Navigation.PageCount = model.PageCount;
			Navigation.DisplayedPage = model.CurrentPage;
			Navigation.IsDoublePage = model.Scene != null && model.Scene.Pages.Count > 1;
			Navigation.Mirror = model.RightToLeft;
			Navigation.Caption = model.Title;
			UpdateNavigation();
		}

		private void OnNavigationBrowse(object sender, NavigationBrowseEventArgs e)
		{
			if (model == null)
			{
				return;
			}
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
			if (model == null)
			{
				MessageOverlay.IsVisible = false;
				LoadingOverlay.IsVisible = false;
				return;
			}
			MessageOverlay.Text = model.Notice;
			MessageOverlay.IsVisible = model.HasNotice;
			LoadingOverlay.Text = model.LoadingText;
			LoadingOverlay.IsVisible = model.Options.ShowStatusOverlay && model.HasLoading && !model.HasNotice;
		}

		// The miniature of the page with the visible part (UpdatePartOverlay): it follows the scene and the scroll, and fades in whenever the part changes
		// while the page is larger than the window.
		private void UpdatePartOverlay(bool partChanged)
		{
			ReaderPartInfo info = model?.PartInfo;
			ReaderScene scene = model?.Scene;
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

		private void OnModelChanged(object sender, PropertyChangedEventArgs e)
		{
			switch (e.PropertyName)
			{
				case nameof(ReaderViewModel.IsFullScreen):
					ApplyFullScreenToWindow();
					ReadingArea.AutoHideCursor = model.IsFullScreen && model.Options.HideCursorFullScreen;
					break;
				case nameof(ReaderViewModel.Title):
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

		// ---- native full screen of the window that hosts the area

		protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
		{
			base.OnAttachedToVisualTree(e);
			hostWindow = TopLevel.GetTopLevel(this) as Window;
			if (hostWindow != null)
			{
				hostWindow.PropertyChanged += OnHostWindowChanged;
				SyncFullScreenWithWindow();
			}
		}

		protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
		{
			base.OnDetachedFromVisualTree(e);
			if (hostWindow != null)
			{
				hostWindow.PropertyChanged -= OnHostWindowChanged;
				hostWindow = null;
			}
		}

		// The model follows the window (the green button, or macOS itself)...
		private void OnHostWindowChanged(object sender, AvaloniaPropertyChangedEventArgs change)
		{
			if (change.Property == Window.WindowStateProperty)
			{
				SyncFullScreenWithWindow();
			}
		}

		private void SyncFullScreenWithWindow()
		{
			if (model != null && hostWindow != null)
			{
				bool full = hostWindow.WindowState == WindowState.FullScreen;
				if (model.IsFullScreen != full)
				{
					model.IsFullScreen = full;
				}
			}
		}

		// ...and the window follows the model (F, F11, the full screen button).
		private void ApplyFullScreenToWindow()
		{
			if (model == null || hostWindow == null)
			{
				return;
			}
			WindowState wanted = model.IsFullScreen ? WindowState.FullScreen : WindowState.Normal;
			if (hostWindow.WindowState != wanted && (model.IsFullScreen || hostWindow.WindowState == WindowState.FullScreen))
			{
				hostWindow.WindowState = wanted;
			}
		}

		// ---- Tools menu and keys

		// Tools > About... (TB.09): the About window over the window that hosts the area.
		private void OnAboutRequested(object sender, EventArgs e)
		{
			if (hostWindow != null)
			{
				new AboutWindow().ShowDialog(hostWindow);
			}
			else
			{
				new AboutWindow().Show();
			}
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
				hostWindow?.Close();
			}
		}

		protected override void OnKeyDown(KeyEventArgs e)
		{
			base.OnKeyDown(e);
			if (!e.Handled && model != null && model.HandleKey(e.Key, e.KeyModifiers))
			{
				e.Handled = true;
			}
		}
	}
}
