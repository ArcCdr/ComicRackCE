using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using cYo.Projects.ComicRack.Engine.Display;
using ComicRack.Mac.Browser;
using ComicRack.Mac.Reader;
using ComicRack.Mac.Services;

namespace ComicRack.Mac.ViewModels
{
	/// <summary>Where the content is scrolled to after a page turn that lands on a page taller than the window.</summary>
	public enum ScrollAnchor
	{
		/// <summary>The top of the page (turning forward).</summary>
		Top,

		/// <summary>The bottom of the page (stepping back with Shift+Space, so the reader continues where the previous page ended).</summary>
		Bottom,
	}

	/// <summary>
	/// State and commands of one reader window (UI thread only). It owns the <see cref="PageService"/> of the book and shows what the
	/// layout says: <see cref="Scene"/> is the decoded pages of the current position, which <c>ReaderView</c> draws.
	///
	/// <para><b>Page turns.</b> Every move (<see cref="NextPage"/>, a key, a swipe) ends in <see cref="GoToAsync"/>: the position is
	/// recorded in the book at once (as the WinForms display does), the page service is told which pages matter now (the one shown,
	/// the next two, the previous one), and the scene is replaced by the new one: immediately when the pages are cached, else when
	/// they have been decoded. The previous scene stays on screen meanwhile, and "Loading page N" appears only if it takes longer than
	/// <see cref="ReaderOptions.LoadingDelay"/>. A move that is overtaken by a newer one is cancelled (its decode is dropped unless
	/// the page is still in the window) and never shows.</para>
	///
	/// <para><b>Ownership.</b> The scene holds one reference of each picture; replacing it releases them. The pictures are shared with
	/// the page cache, so nothing is copied and nothing is freed while a frame still draws it.</para>
	/// </summary>
	public sealed partial class ReaderViewModel : ObservableObject, IDisposable
	{
		private readonly IReadingSession session;
		private readonly ReaderOptions options;
		private IPageLayout layout;
		private IPageLayout sceneLayout = SinglePageLayout.Instance;
		private PageCatalog catalog;
		private SpreadLayout twoPages;
		private SpreadLayout twoPagesAdaptive;
		private PageLayoutMode mode;
		private bool rightToLeft;
		private double zoom = 1.0;
		private double scrollX;
		private readonly PageTurnMeter meter;
		private readonly SwipeRecognizer swipe;
		private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
		private CancellationTokenSource navigation;
		private PageService service;
		private ReaderScene scene;
		private Task lastNavigation = Task.CompletedTask;
		private long currentTurn;
		private double scrollY;
		private Size viewport = new Size(900, 900);
		private bool opened;
		private bool disposed;

		public ReaderViewModel(IReadingSession session, ReaderOptions options = null)
		{
			this.session = session ?? throw new ArgumentNullException(nameof(session));
			this.options = options ?? new ReaderOptions();
			layout = SinglePageLayout.Instance;
			mode = this.options.Mode == PageLayoutMode.Double || this.options.Mode == PageLayoutMode.DoubleAdaptive ? this.options.Mode : PageLayoutMode.Single;
			rightToLeft = this.options.RightToLeft;
			swipe = this.options.Swipe;
			meter = new PageTurnMeter(this.options.Log);
			fit = this.options.Fit;
			Title = session.Title;
			PageText = string.Empty;
			Notice = "Opening...";
		}

		/// <summary>Timing of the page turns of this reader.</summary>
		public PageTurnMeter Meter => meter;

		public IPageLayout Layout => layout;

		/// <summary>The page cache of the open book, or null before the book is open and after the reader is closed.</summary>
		public PageService Pages => service;

		public IReadingSession Session => session;

		/// <summary>The window title.</summary>
		[ObservableProperty]
		public partial string Title { get; set; }

		/// <summary>"12 / 24", or empty before the book is open.</summary>
		[ObservableProperty]
		public partial string PageText { get; set; }

		/// <summary>A line over the page: "Opening...", "Loading page 12...", or why a page or the book could not be shown. Null when there is none.</summary>
		[ObservableProperty]
		[NotifyPropertyChangedFor(nameof(HasNotice))]
		public partial string Notice { get; set; }

		public bool HasNotice => !string.IsNullOrEmpty(Notice);

		/// <summary>The current position (0-based): the number of the first page on screen.</summary>
		public int CurrentPage { get; private set; }

		public int PageCount { get; private set; }

		/// <summary>True once the book is open and has pages.</summary>
		public bool IsOpen => opened;

		/// <summary>True after <see cref="Dispose"/>.</summary>
		public bool IsDisposed => disposed;

		private ImageFitMode fit;

		/// <summary>How the page is fitted to the window; set it with <see cref="SetFit"/>.</summary>
		public ImageFitMode Fit => fit;

		/// <summary>The menu text of the current fit mode.</summary>
		public string FitText => FitName(fit);

		/// <summary>The decoded pages of the current position, or null (nothing yet, or the page could not be loaded). Changes raise <c>PropertyChanged</c>.</summary>
		public ReaderScene Scene => scene;

		/// <summary>How far the content is scrolled down, in device independent pixels (0 unless the page is taller than the window).</summary>
		public double ScrollY => scrollY;

		/// <summary>The size of the reading area, set by the view; the layout and the scrolling work with it.</summary>
		public Size ViewportSize
		{
			get => viewport;
			set
			{
				if (viewport == value || value.Width <= 0 || value.Height <= 0)
				{
					return;
				}
				viewport = value;
				ClampScroll();
				OnPropertyChanged(nameof(ViewportSize));
			}
		}

		/// <summary>Where the pages of the scene are placed right now.</summary>
		public PageArrangement Arrangement => ArrangeFor(viewport);

		/// <summary>Where the pages of the scene are placed in a reading area of the given size (the layout that made the scene decides).</summary>
		public PageArrangement ArrangeFor(Size area)
		{
			ReaderScene current = scene;
			return current == null ? PageArrangement.Empty : sceneLayout.Arrange(area, current.Position, current.Sizes(), fit, zoom, new Vector(scrollX, scrollY));
		}

		// Brings the scroll back inside the content after the viewport, the zoom or the fit changed.
		private void ClampScroll()
		{
			PageArrangement a = Arrangement;
			if (a.Scroll != scrollY)
			{
				scrollY = a.Scroll;
				OnPropertyChanged(nameof(ScrollY));
			}
			if (a.ScrollX != scrollX)
			{
				scrollX = a.ScrollX;
				OnPropertyChanged(nameof(ScrollX));
			}
		}

		// The scroll a page starts at: the top, or the bottom after stepping back; the left, or the right when reading right to left.
		private void AnchorScroll(ScrollAnchor anchor)
		{
			scrollX = rightToLeft ? double.MaxValue : 0;
			scrollY = anchor == ScrollAnchor.Bottom ? double.MaxValue : 0;
			if (scene != null)
			{
				PageArrangement a = Arrangement;
				scrollX = a.ScrollX;
				scrollY = a.Scroll;
			}
			else
			{
				scrollX = 0;
				scrollY = 0;
			}
			OnPropertyChanged(nameof(ScrollX));
			OnPropertyChanged(nameof(ScrollY));
		}

		/// <summary>True if the content can be scrolled further down.</summary>
		public bool CanScrollDown => Arrangement.MaxScroll - scrollY > 0.5;

		/// <summary>True if the content can be scrolled further up.</summary>
		public bool CanScrollUp => scrollY > 0.5;

		/// <summary>The layout in use: <see cref="SinglePageLayout"/> or a <see cref="SpreadLayout"/>.</summary>
		public PageLayoutMode LayoutMode => mode;

		/// <summary>True if the book is read from right to left: the arrow keys and the swipe turn the other way and a spread shows its first page on the right.</summary>
		public bool RightToLeft => rightToLeft;

		/// <summary>How much the page is enlarged beyond the fit mode: 1 is the fit, at most <see cref="ReaderOptions.ZoomMax"/>.</summary>
		public double Zoom => zoom;

		/// <summary>How far the content is scrolled to the left, in device independent pixels (0 unless it is wider than the window).</summary>
		public double ScrollX => scrollX;

		/// <summary>True if the content is larger than the reading area in either direction, so dragging pans it.</summary>
		public bool CanPan
		{
			get
			{
				PageArrangement a = Arrangement;
				return a.MaxScroll > 0.5 || a.MaxScrollX > 0.5;
			}
		}

		/// <summary>True while the window is in native full screen (the window follows this value and reports the user's own changes back).</summary>
		[ObservableProperty]
		[NotifyPropertyChangedFor(nameof(ShowStatus))]
		public partial bool IsFullScreen { get; set; }

		/// <summary>The status strip is hidden in full screen.</summary>
		public bool ShowStatus => !IsFullScreen;

		/// <summary>"Two pages · Right to left · 150 %" for the status strip.</summary>
		public string ModeText
		{
			get
			{
				var parts = new List<string> { ModeName(mode) };
				if (rightToLeft)
				{
					parts.Add("Right to left");
				}
				if (zoom > 1.0005)
				{
					parts.Add(Math.Round(zoom * 100).ToString(CultureInfo.CurrentCulture) + " %");
				}
				return string.Join(" \u00B7 ", parts);
			}
		}

		public static string ModeName(PageLayoutMode mode)
		{
			switch (mode)
			{
				case PageLayoutMode.Double:
					return "Two pages";
				case PageLayoutMode.DoubleAdaptive:
					return "Two pages (adaptive)";
				default:
					return "One page";
			}
		}

		/// <summary>Raised when the reader wants its window closed (Esc, Cmd+W, the menu).</summary>
		public event EventHandler CloseRequested;

		/// <summary>Raised after the fit mode changed (the app remembers it for the next reader).</summary>
		public event EventHandler FitChanged;

		/// <summary>Raised after the page layout (one page, two pages) changed (the app remembers it for the next reader).</summary>
		public event EventHandler LayoutChanged;

		/// <summary>The task of the newest page turn: completes when its pages are on screen, or when a newer turn overtook it.</summary>
		public Task WhenSettled => lastNavigation;

		public static string FitName(ImageFitMode mode)
		{
			switch (mode)
			{
				case ImageFitMode.FitWidth:
					return "Fit Width";
				case ImageFitMode.FitHeight:
					return "Fit Height";
				default:
					return "Best Fit";
			}
		}

		// ---- Opening

		/// <summary>Opens the book, then shows the page it was left on. A failure is shown as the notice; this does not throw.</summary>
		public async Task OpenAsync()
		{
			if (opened || disposed)
			{
				return;
			}
			try
			{
				await session.OpenAsync(lifetime.Token);
			}
			catch (OperationCanceledException)
			{
				return;
			}
			catch (Exception ex)
			{
				if (!disposed)
				{
					Notice = ex.Message;
					Log("Opening the book failed: " + ex.Message);
				}
				return;
			}
			if (disposed)
			{
				return;
			}
			PageCount = session.PageCount;
			if (PageCount <= 0)
			{
				Notice = "The book has no pages.";
				return;
			}
			catalog = new PageCatalog(PageCount, session.GetPageTraits);
			rightToLeft = session.RightToLeft ?? options.RightToLeft;
			twoPages = new SpreadLayout(catalog, adaptive: false, rightToLeft);
			twoPagesAdaptive = new SpreadLayout(catalog, adaptive: true, rightToLeft);
			layout = LayoutFor(mode);
			OnPropertyChanged(nameof(RightToLeft));
			OnPropertyChanged(nameof(ModeText));
			service = new PageService(session.Decoder, options.Pages) { Decoded = (page, width, height) => catalog.Learn(page, width, height) };
			opened = true;
			Notice = null;
			await GoToAsync(session.ResumePage, ScrollAnchor.Top);
		}

		// ---- Moving

		/// <summary>
		/// Moves to a position (see <see cref="IPageLayout"/>; for a spread the position is moved to the first page of the spread that
		/// holds it). Completes when the pages are on screen, or when a newer move replaced this one. Does not throw for a page that
		/// cannot be decoded: the notice says so.
		/// </summary>
		public Task GoToAsync(int position, ScrollAnchor anchor = ScrollAnchor.Top)
		{
			if (!opened || disposed)
			{
				return Task.CompletedTask;
			}
			position = layout.Normalize(position, PageCount);
			navigation?.Cancel();
			navigation = new CancellationTokenSource();
			CancellationToken token = navigation.Token;
			long turn = meter.Begin(position);
			currentTurn = turn;

			CurrentPage = position;
			int[] pages = layout.PagesAt(position, PageCount);
			PageText = PageNumbers(pages);
			OnPropertyChanged(nameof(CurrentPage));
			session.ReportPage(position);

			service.SetWindow(layout.Window(position, PageCount, options.Ahead, options.Behind));

			var have = new CoverImage[pages.Length];
			bool all = true;
			for (int i = 0; i < pages.Length; i++)
			{
				have[i] = service.TryGetCached(pages[i]);
				all &= have[i] != null;
			}
			if (all)
			{
				Publish(turn, position, pages, have, anchor, cached: true);
				lastNavigation = Task.CompletedTask;
			}
			else
			{
				lastNavigation = LoadAsync(turn, position, pages, have, anchor, token);
			}
			return lastNavigation;
		}

		// "12 / 24", or "12-13 / 24" for a spread.
		private string PageNumbers(int[] pages)
		{
			if (pages.Length == 0)
			{
				return string.Empty;
			}
			string first = (pages[0] + 1).ToString(CultureInfo.CurrentCulture);
			string shown = pages.Length > 1 ? first + "-" + (pages[pages.Length - 1] + 1).ToString(CultureInfo.CurrentCulture) : first;
			return shown + " / " + PageCount.ToString(CultureInfo.CurrentCulture);
		}

		private async Task LoadAsync(long turn, int position, int[] pages, CoverImage[] have, ScrollAnchor anchor, CancellationToken token)
		{
			_ = ShowLoadingLaterAsync(turn, position, token);
			// Decoding reveals the size of a page, and a wide page does not share a spread: when the pages that were asked for are not
			// what the layout shows now, the missing ones are fetched and the loop ends once the set is stable (a few rounds at most).
			for (int round = 0; round < 4; round++)
			{
				var tasks = new Task<PageResult>[pages.Length];
				var results = new PageResult[pages.Length];
				Exception failure = null;
				try
				{
					for (int i = 0; i < pages.Length; i++)
					{
						if (have[i] == null)
						{
							tasks[i] = service.RequestAsync(pages[i], token);
						}
					}
					for (int i = 0; i < pages.Length; i++)
					{
						if (tasks[i] != null)
						{
							results[i] = await tasks[i];
						}
					}
				}
				catch (OperationCanceledException)
				{
					failure = new OperationCanceledException();
				}
				catch (ObjectDisposedException ex)
				{
					failure = ex; // the reader is closing
				}
				catch (Exception ex)
				{
					failure = ex;
				}

				bool stale = failure is OperationCanceledException || failure is ObjectDisposedException || token.IsCancellationRequested || disposed || turn != currentTurn;
				PageResult broken = null;
				if (failure == null && !stale)
				{
					foreach (PageResult r in results)
					{
						if (r != null && r.Error != null)
						{
							broken = r;
							break;
						}
					}
				}
				if (failure != null || stale || broken != null)
				{
					ReleaseAll(have, tasks, results);
					if (broken != null || (failure != null && !stale))
					{
						string reason = broken?.Error.Message ?? failure.Message;
						string text = "Page " + (position + 1) + " could not be loaded: " + reason;
						Log(text);
						SetScene(null);
						Notice = text;
					}
					return;
				}

				var images = new CoverImage[pages.Length];
				for (int i = 0; i < pages.Length; i++)
				{
					images[i] = have[i] ?? results[i].Take();
					catalog.Learn(pages[i], images[i].Width, images[i].Height);
				}
				int[] now = layout.PagesAt(position, PageCount);
				if (now.SequenceEqual(pages) || round == 3) // a pairing that does not settle shows what was fetched
				{
					Publish(turn, position, pages, images, anchor, cached: false);
					return;
				}
				// The pairing changed: keep the pictures that are still wanted, ask for the others.
				PageText = PageNumbers(now);
				service.SetWindow(layout.Window(position, PageCount, options.Ahead, options.Behind));
				var next = new CoverImage[now.Length];
				for (int i = 0; i < now.Length; i++)
				{
					int old = Array.IndexOf(pages, now[i]);
					if (old >= 0)
					{
						next[i] = images[old];
						images[old] = null;
					}
					else
					{
						next[i] = service.TryGetCached(now[i]);
					}
				}
				foreach (CoverImage unused in images)
				{
					unused?.Release();
				}
				pages = now;
				have = next;
			}
		}

		// Gives back every reference a failed or stale move holds: the cached pictures, the delivered results, and results still to come.
		private static void ReleaseAll(CoverImage[] have, Task<PageResult>[] tasks, PageResult[] results)
		{
			foreach (CoverImage image in have)
			{
				image?.Release();
			}
			for (int i = 0; i < tasks.Length; i++)
			{
				if (results[i] != null)
				{
					results[i].Dispose();
				}
				else if (tasks[i] != null)
				{
					tasks[i].ContinueWith(t =>
					{
						if (t.Status == TaskStatus.RanToCompletion)
						{
							t.Result.Dispose();
						}
					}, TaskScheduler.Default);
				}
			}
		}

		private async Task ShowLoadingLaterAsync(long turn, int position, CancellationToken token)
		{
			try
			{
				await Task.Delay(options.LoadingDelay, token);
			}
			catch (OperationCanceledException)
			{
				return;
			}
			if (!disposed && turn == currentTurn && (scene == null || scene.Turn != turn))
			{
				Notice = "Loading page " + (position + 1) + "...";
			}
		}

		private void Publish(long turn, int position, int[] pages, CoverImage[] images, ScrollAnchor anchor, bool cached)
		{
			var scenePages = new ScenePage[pages.Length];
			for (int i = 0; i < pages.Length; i++)
			{
				scenePages[i] = new ScenePage(pages[i], images[i]);
			}
			var next = new ReaderScene(scenePages, turn, position);
			ReaderScene old = scene;
			scene = next;
			sceneLayout = layout;
			AnchorScroll(anchor);
			Notice = null;
			PageText = PageNumbers(pages);
			OnPropertyChanged(nameof(Scene));
			old?.Release();
			if (pages.Length > 1)
			{
				session.ReportFurthest(pages[pages.Length - 1]);
			}
			meter.Published(turn, cached, cached ? 0 : service?.GetStats().LastDecodeMilliseconds ?? 0);
		}

		private void SetScene(ReaderScene next)
		{
			ReaderScene old = scene;
			scene = next;
			scrollX = 0;
			scrollY = 0;
			OnPropertyChanged(nameof(Scene));
			OnPropertyChanged(nameof(ScrollX));
			OnPropertyChanged(nameof(ScrollY));
			old?.Release();
		}

		// ---- Commands

		/// <summary>The next position; at the end it does nothing (a one-page book is marked as read, as in the Core).</summary>
		[RelayCommand]
		public void NextPage()
		{
			if (!opened || disposed)
			{
				return;
			}
			int next = layout.Next(CurrentPage, PageCount);
			if (next < 0)
			{
				session.ReportEndReached();
				return;
			}
			_ = GoToAsync(next);
		}

		[RelayCommand]
		public void PreviousPage()
		{
			StepBack(ScrollAnchor.Top);
		}

		private void StepBack(ScrollAnchor anchor)
		{
			if (!opened || disposed)
			{
				return;
			}
			int previous = layout.Previous(CurrentPage, PageCount);
			if (previous >= 0)
			{
				_ = GoToAsync(previous, anchor);
			}
		}

		[RelayCommand]
		public void FirstPage()
		{
			if (opened && !disposed)
			{
				_ = GoToAsync(layout.First(PageCount));
			}
		}

		[RelayCommand]
		public void LastPage()
		{
			if (opened && !disposed)
			{
				_ = GoToAsync(layout.Last(PageCount));
			}
		}

		/// <summary>Space: scroll down by most of a window if the page continues below it, else turn to the next page.</summary>
		[RelayCommand]
		public void SpaceForward()
		{
			if (CanScrollDown)
			{
				ScrollBy(viewport.Height * options.SpaceScrollFraction);
			}
			else
			{
				NextPage();
			}
		}

		/// <summary>Shift+Space: scroll up if the page is scrolled, else turn back and land on the bottom of the previous page.</summary>
		[RelayCommand]
		public void SpaceBack()
		{
			if (CanScrollUp)
			{
				ScrollBy(-viewport.Height * options.SpaceScrollFraction);
			}
			else
			{
				StepBack(ScrollAnchor.Bottom);
			}
		}

		/// <summary>Scrolls the content by <paramref name="delta"/> device independent pixels (positive = down), within the page.</summary>
		public void ScrollBy(double delta)
		{
			PanBy(new Vector(0, delta));
		}

		/// <summary>
		/// Moves the content by <paramref name="delta"/> (positive = further right and down in the content, i.e. the content moves left
		/// and up), within its edges. Returns true if anything moved.
		/// </summary>
		public bool PanBy(Vector delta)
		{
			if (scene == null)
			{
				return false;
			}
			PageArrangement a = sceneLayout.Arrange(viewport, scene.Position, scene.Sizes(), fit, zoom, new Vector(scrollX + delta.X, scrollY + delta.Y));
			bool moved = false;
			if (a.ScrollX != scrollX)
			{
				scrollX = a.ScrollX;
				OnPropertyChanged(nameof(ScrollX));
				moved = true;
			}
			if (a.Scroll != scrollY)
			{
				scrollY = a.Scroll;
				OnPropertyChanged(nameof(ScrollY));
				moved = true;
			}
			return moved;
		}

		// ---- Zoom

		/// <summary>Enlarges the page by one step (Cmd+=), about the centre of the reading area.</summary>
		[RelayCommand]
		public void ZoomIn() => ZoomBy(options.ZoomStep);

		/// <summary>Shrinks the page by one step (Cmd+-), never below the fit.</summary>
		[RelayCommand]
		public void ZoomOut() => ZoomBy(1.0 / options.ZoomStep);

		/// <summary>Back to the fit and to the start of the page (Cmd+0).</summary>
		[RelayCommand]
		public void ZoomReset()
		{
			bool changed = zoom != 1.0;
			zoom = 1.0;
			AnchorScroll(ScrollAnchor.Top);
			if (changed)
			{
				OnZoomChanged();
			}
		}

		/// <summary>Multiplies the zoom by <paramref name="factor"/> (a pinch passes 1 + its magnification), keeping the content under <paramref name="focal"/> (a point of the reading area; default its centre) where it is.</summary>
		public void ZoomBy(double factor, Point? focal = null)
		{
			if (!(factor > 0) || double.IsInfinity(factor))
			{
				return;
			}
			SetZoom(zoom * factor, focal);
		}

		/// <summary>Sets the zoom (limited to <see cref="ReaderOptions.ZoomMin"/>..<see cref="ReaderOptions.ZoomMax"/>) about <paramref name="focal"/>.</summary>
		public void SetZoom(double value, Point? focal = null)
		{
			value = Math.Max(options.ZoomMin, Math.Min(options.ZoomMax, value));
			if (Math.Abs(value - zoom) < 1e-9)
			{
				return;
			}
			if (scene == null)
			{
				zoom = value;
				OnZoomChanged();
				return;
			}
			Point at = focal ?? new Point(viewport.Width / 2, viewport.Height / 2);
			PageArrangement before = Arrangement;
			Rect box = before.Content;
			double ux = box.Width > 0 ? Math.Max(0, Math.Min(1, (at.X - box.X) / box.Width)) : 0.5;
			double uy = box.Height > 0 ? Math.Max(0, Math.Min(1, (at.Y - box.Y) / box.Height)) : 0.5;
			zoom = value;
			Rect size = sceneLayout.Arrange(viewport, scene.Position, scene.Sizes(), fit, zoom, default(Vector)).Content;
			scrollX = ux * size.Width - at.X;
			scrollY = uy * size.Height - at.Y;
			ClampScroll();
			OnZoomChanged();
		}

		private void OnZoomChanged()
		{
			OnPropertyChanged(nameof(Zoom));
			OnPropertyChanged(nameof(ModeText));
		}

		// ---- Layout and reading direction

		private IPageLayout LayoutFor(PageLayoutMode wanted)
		{
			switch (wanted)
			{
				case PageLayoutMode.Double:
					return twoPages ?? (IPageLayout)SinglePageLayout.Instance;
				case PageLayoutMode.DoubleAdaptive:
					return twoPagesAdaptive ?? (IPageLayout)SinglePageLayout.Instance;
				default:
					return SinglePageLayout.Instance;
			}
		}

		[RelayCommand]
		public void SingleLayout() => SetLayoutMode(PageLayoutMode.Single);

		[RelayCommand]
		public void TwoPageLayout() => SetLayoutMode(PageLayoutMode.Double);

		[RelayCommand]
		public void AdaptiveTwoPageLayout() => SetLayoutMode(PageLayoutMode.DoubleAdaptive);

		/// <summary>Switches between one page and two pages. The reader stays on the spread that holds the page it was on.</summary>
		public void SetLayoutMode(PageLayoutMode wanted)
		{
			if (wanted != PageLayoutMode.Double && wanted != PageLayoutMode.DoubleAdaptive)
			{
				wanted = PageLayoutMode.Single; // Continuous is not supported
			}
			if (wanted == mode)
			{
				return;
			}
			mode = wanted;
			layout = LayoutFor(mode);
			OnPropertyChanged(nameof(LayoutMode));
			OnPropertyChanged(nameof(ModeText));
			LayoutChanged?.Invoke(this, EventArgs.Empty);
			if (opened && !disposed)
			{
				_ = GoToAsync(CurrentPage);
			}
		}

		[RelayCommand]
		public void ToggleRightToLeft() => SetRightToLeft(!rightToLeft);

		/// <summary>Sets the reading direction (the book's own setting is only where it starts).</summary>
		public void SetRightToLeft(bool value)
		{
			if (value == rightToLeft)
			{
				return;
			}
			rightToLeft = value;
			if (twoPages != null)
			{
				twoPages.RightToLeft = value;
				twoPagesAdaptive.RightToLeft = value;
			}
			swipe.Reset();
			if (scene != null)
			{
				AnchorScroll(ScrollAnchor.Top);
			}
			OnPropertyChanged(nameof(RightToLeft));
			OnPropertyChanged(nameof(ModeText));
		}

		[RelayCommand]
		public void ToggleFullScreen() => IsFullScreen = !IsFullScreen;

		[RelayCommand]
		public void FitBest() => SetFit(ImageFitMode.Fit);

		[RelayCommand]
		public void FitWidth() => SetFit(ImageFitMode.FitWidth);

		[RelayCommand]
		public void FitHeight() => SetFit(ImageFitMode.FitHeight);

		public void SetFit(ImageFitMode mode)
		{
			if (mode != ImageFitMode.FitWidth && mode != ImageFitMode.FitHeight)
			{
				mode = ImageFitMode.Fit; // the only modes of M2.3a
			}
			if (mode == fit)
			{
				return;
			}
			fit = mode;
			AnchorScroll(ScrollAnchor.Top);
			OnPropertyChanged(nameof(Fit));
			OnPropertyChanged(nameof(FitText));
			FitChanged?.Invoke(this, EventArgs.Empty);
		}

		[RelayCommand]
		public void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);

		// ---- Input

		/// <summary>
		/// The reader's keys: Right, Left, Space, Shift+Space, Home, End, Page Down, Page Up, Up, Down, Esc (leaves full screen first);
		/// Cmd+2, Cmd+3, Cmd+5 for the fit modes (the WinForms app uses Ctrl+2/3/5 for Fit All, Fit Width and Fit Height); Cmd+= and
		/// Cmd+- zoom, Cmd+0 resets the zoom; Ctrl+Cmd+F full screen; Option+Cmd+1, 2, 3 one page, two pages, two pages (adaptive) and
		/// Option+Cmd+R the reading direction; Cmd+W closes. Reading right to left, Left turns forward and Right back. Returns true if
		/// the key was used.
		/// </summary>
		public bool HandleKey(Key key, KeyModifiers modifiers)
		{
			bool meta = (modifiers & KeyModifiers.Meta) != 0;
			bool control = (modifiers & KeyModifiers.Control) != 0;
			bool alt = (modifiers & KeyModifiers.Alt) != 0;
			if (meta && control && !alt && key == Key.F)
			{
				ToggleFullScreen();
				return true;
			}
			if (meta && alt && !control)
			{
				switch (key)
				{
					case Key.D1:
						SingleLayout();
						return true;
					case Key.D2:
						TwoPageLayout();
						return true;
					case Key.D3:
						AdaptiveTwoPageLayout();
						return true;
					case Key.R:
						ToggleRightToLeft();
						return true;
					default:
						return false;
				}
			}
			if (control || alt)
			{
				return false;
			}
			if (meta)
			{
				switch (key)
				{
					case Key.D2:
						FitBest();
						return true;
					case Key.D3:
						FitWidth();
						return true;
					case Key.D5:
						FitHeight();
						return true;
					case Key.OemPlus:
					case Key.Add:
						ZoomIn();
						return true;
					case Key.OemMinus:
					case Key.Subtract:
						ZoomOut();
						return true;
					case Key.D0:
					case Key.NumPad0:
						ZoomReset();
						return true;
					case Key.W:
						Close();
						return true;
					default:
						return false;
				}
			}
			bool shift = (modifiers & KeyModifiers.Shift) != 0;
			switch (key)
			{
				case Key.Right:
					if (rightToLeft)
					{
						PreviousPage();
					}
					else
					{
						NextPage();
					}
					return true;
				case Key.Left:
					if (rightToLeft)
					{
						NextPage();
					}
					else
					{
						PreviousPage();
					}
					return true;
				case Key.PageDown:
					NextPage();
					return true;
				case Key.PageUp:
					PreviousPage();
					return true;
				case Key.Space:
					if (shift)
					{
						SpaceBack();
					}
					else
					{
						SpaceForward();
					}
					return true;
				case Key.Home:
					FirstPage();
					return true;
				case Key.End:
					LastPage();
					return true;
				case Key.Down:
					ScrollBy(options.ArrowScrollStep);
					return true;
				case Key.Up:
					ScrollBy(-options.ArrowScrollStep);
					return true;
				case Key.Escape:
					if (IsFullScreen)
					{
						IsFullScreen = false;
					}
					else
					{
						Close();
					}
					return true;
				default:
					return false;
			}
		}

		/// <summary>
		/// A wheel or trackpad event. While the content is larger than the window (zoomed, or a fit mode that overflows) the event pans
		/// it in both directions. Otherwise a two-finger horizontal swipe turns one page (see <see cref="SwipeRecognizer"/>; reading
		/// right to left the swipe is the other way round) and vertical movement scrolls a page that is taller than the window.
		/// Returns true if the event was used.
		/// </summary>
		public bool HandleWheel(Vector delta, long timestampMilliseconds)
		{
			PageArrangement a = Arrangement;
			if (a.MaxScrollX > 0.5)
			{
				swipe.Reset();
				return PanBy(new Vector(-delta.X * options.WheelScrollStep, -delta.Y * options.WheelScrollStep));
			}
			int direction = swipe.Feed(delta.X, delta.Y, timestampMilliseconds);
			if (rightToLeft)
			{
				direction = -direction;
			}
			if (direction > 0)
			{
				NextPage();
				return true;
			}
			if (direction < 0)
			{
				PreviousPage();
				return true;
			}
			if (Math.Abs(delta.Y) > Math.Abs(delta.X) && Math.Abs(delta.Y) > 0)
			{
				double before = scrollY;
				ScrollBy(-delta.Y * options.WheelScrollStep);
				return scrollY != before;
			}
			return false;
		}

		private void Log(string message)
		{
			try
			{
				options.Log?.Invoke(message);
			}
			catch (Exception)
			{
			}
		}

		// ---- Closing

		/// <summary>
		/// Closes the reader: records the page in the book, stops the page service (cancels what is queued and waits briefly for decodes
		/// that cannot be interrupted, so the Core can be torn down afterwards) and releases the pictures and the book. Idempotent.
		/// </summary>
		public void Dispose()
		{
			if (disposed)
			{
				return;
			}
			disposed = true;
			lifetime.Cancel();
			navigation?.Cancel();
			if (opened)
			{
				session.ReportPage(CurrentPage);
			}
			try
			{
				service?.Dispose();
			}
			catch (Exception ex)
			{
				Log("Stopping the page service failed: " + ex.Message);
			}
			ReaderScene old = scene;
			scene = null;
			old?.Release();
			try
			{
				session.Dispose();
			}
			catch (Exception ex)
			{
				Log("Closing the book failed: " + ex.Message);
			}
			service = null;
		}
	}
}
