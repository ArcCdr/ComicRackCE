using System;
using System.Collections.Generic;
using System.Globalization;
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
		private readonly IPageLayout layout;
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
			layout = this.options.Layout;
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
				double clamped = Arrangement.Scroll;
				if (clamped != scrollY)
				{
					scrollY = clamped;
					OnPropertyChanged(nameof(ScrollY));
				}
				OnPropertyChanged(nameof(ViewportSize));
			}
		}

		/// <summary>Where the pages of the scene are placed right now.</summary>
		public PageArrangement Arrangement => scene == null ? PageArrangement.Empty : layout.Arrange(viewport, scene.Sizes(), fit, scrollY);

		/// <summary>True if the content can be scrolled further down.</summary>
		public bool CanScrollDown => Arrangement.MaxScroll - scrollY > 0.5;

		/// <summary>True if the content can be scrolled further up.</summary>
		public bool CanScrollUp => scrollY > 0.5;

		/// <summary>Raised when the reader wants its window closed (Esc, Cmd+W, the menu).</summary>
		public event EventHandler CloseRequested;

		/// <summary>Raised after the fit mode changed (the app remembers it for the next reader).</summary>
		public event EventHandler FitChanged;

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
			service = new PageService(session.Decoder, options.Pages);
			opened = true;
			Notice = null;
			await GoToAsync(session.ResumePage, ScrollAnchor.Top);
		}

		// ---- Moving

		/// <summary>
		/// Moves to a position (see <see cref="IPageLayout"/>). Completes when the pages are on screen, or when a newer move replaced
		/// this one. Does not throw for a page that cannot be decoded: the notice says so.
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
			PageText = (position + 1).ToString(CultureInfo.CurrentCulture) + " / " + PageCount.ToString(CultureInfo.CurrentCulture);
			OnPropertyChanged(nameof(CurrentPage));
			session.ReportPage(position);

			int[] pages = layout.PagesAt(position, PageCount);
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

		private async Task LoadAsync(long turn, int position, int[] pages, CoverImage[] have, ScrollAnchor anchor, CancellationToken token)
		{
			_ = ShowLoadingLaterAsync(turn, position, token);
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
			}
			Publish(turn, position, pages, images, anchor, cached: false);
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
			double target = 0;
			if (anchor == ScrollAnchor.Bottom)
			{
				target = layout.Arrange(viewport, next.Sizes(), fit, double.MaxValue).Scroll;
			}
			scrollY = target;
			Notice = null;
			OnPropertyChanged(nameof(Scene));
			OnPropertyChanged(nameof(ScrollY));
			old?.Release();
			meter.Published(turn, cached, cached ? 0 : service?.GetStats().LastDecodeMilliseconds ?? 0);
		}

		private void SetScene(ReaderScene next)
		{
			ReaderScene old = scene;
			scene = next;
			scrollY = 0;
			OnPropertyChanged(nameof(Scene));
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
			if (scene == null)
			{
				return;
			}
			double target = layout.Arrange(viewport, scene.Sizes(), fit, scrollY + delta).Scroll;
			if (target != scrollY)
			{
				scrollY = target;
				OnPropertyChanged(nameof(ScrollY));
			}
		}

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
			scrollY = 0;
			OnPropertyChanged(nameof(Fit));
			OnPropertyChanged(nameof(FitText));
			OnPropertyChanged(nameof(ScrollY));
			FitChanged?.Invoke(this, EventArgs.Empty);
		}

		[RelayCommand]
		public void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);

		// ---- Input

		/// <summary>
		/// The reader's keys: Right, Left, Space, Shift+Space, Home, End, Page Down, Page Up, Up, Down, Esc; and Cmd+2, Cmd+3, Cmd+5 for
		/// the fit modes (the WinForms app uses Ctrl+2/3/5 for Fit All, Fit Width and Fit Height) and Cmd+W. Returns true if the key was used.
		/// </summary>
		public bool HandleKey(Key key, KeyModifiers modifiers)
		{
			if ((modifiers & (KeyModifiers.Control | KeyModifiers.Alt)) != 0)
			{
				return false;
			}
			if ((modifiers & KeyModifiers.Meta) != 0)
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
				case Key.PageDown:
					NextPage();
					return true;
				case Key.Left:
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
					Close();
					return true;
				default:
					return false;
			}
		}

		/// <summary>
		/// A wheel or trackpad event. A two-finger horizontal swipe turns one page (see <see cref="SwipeRecognizer"/>); vertical movement
		/// scrolls a page that is taller than the window. Returns true if the event was used.
		/// </summary>
		public bool HandleWheel(Vector delta, long timestampMilliseconds)
		{
			int direction = swipe.Feed(delta.X, delta.Y, timestampMilliseconds);
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
