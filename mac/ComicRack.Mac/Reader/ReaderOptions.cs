using System;
using cYo.Projects.ComicRack.Engine.Display;
using ComicRack.Mac.Host;
using ComicRack.Mac.Services;

namespace ComicRack.Mac.Reader
{
	/// <summary>Settings of a <see cref="ComicRack.Mac.ViewModels.ReaderViewModel"/>. The defaults are what the app uses; tests change some.</summary>
	public sealed class ReaderOptions
	{
		/// <summary>
		/// The options a new reader starts with, from the saved preferences (P5.1): the overlays and the cursor as Preferences > Reader > Overlays and Behavior set them,
		/// and the fit, the page layout and the reading direction the reader chose last. A value that is not a name of the enum (a hand edited file) is the default.
		/// </summary>
		public static ReaderOptions From(AppPreferences preferences)
		{
			var options = new ReaderOptions();
			if (preferences == null)
			{
				return options;
			}
			options.ShowCurrentPageOverlay = preferences.ShowCurrentPageOverlay;
			options.CurrentPageShowsName = preferences.CurrentPageShowsName;
			options.showVisiblePagePartOverlay = preferences.ShowVisiblePagePartOverlay;
			options.preferences = preferences;
			options.ShowStatusOverlay = preferences.ShowStatusOverlay;
			options.ShowNavigationOverlay = preferences.ShowNavigationOverlay;
			options.NavigationOverlayOnTop = preferences.NavigationOverlayOnTop;
			options.HideCursorFullScreen = preferences.HideCursorFullScreen;
			options.Fit = ParseFit(preferences.ReaderFit);
			options.Mode = ParseMode(preferences.ReaderLayout);
			options.RightToLeft = preferences.RightToLeftDefault;
			return options;
		}

		/// <summary>The fit mode of a saved name: one of the modes the reader has (Fit All, Fit Width, Fit Height), else Fit All.</summary>
		public static ImageFitMode ParseFit(string name)
		{
			return Enum.TryParse(name, out ImageFitMode fit) && (fit == ImageFitMode.Fit || fit == ImageFitMode.FitWidth || fit == ImageFitMode.FitHeight) ? fit : ImageFitMode.Fit;
		}

		/// <summary>The page layout of a saved name: Single, Double or DoubleAdaptive, else Single.</summary>
		public static PageLayoutMode ParseMode(string name)
		{
			return Enum.TryParse(name, out PageLayoutMode mode) && (mode == PageLayoutMode.Double || mode == PageLayoutMode.DoubleAdaptive) ? mode : PageLayoutMode.Single;
		}

		/// <summary>The page cache and prefetcher.</summary>
		public PageServiceOptions Pages { get; set; } = new PageServiceOptions();

		/// <summary>The page layout a reader starts with: Single, Double (two pages) or DoubleAdaptive. Continuous is not supported and counts as Single.</summary>
		public PageLayoutMode Mode { get; set; } = PageLayoutMode.Single;

		/// <summary>Reading direction to start with when the book does not say (its manga setting is unknown). The book's own setting wins.</summary>
		public bool RightToLeft { get; set; }

		/// <summary>The smallest zoom: the fit itself.</summary>
		public double ZoomMin { get; set; } = 1.0;

		/// <summary>The largest zoom, relative to the fit.</summary>
		public double ZoomMax { get; set; } = 8.0;

		/// <summary>The factor of one Cmd+= or Cmd+- press.</summary>
		public double ZoomStep { get; set; } = 1.25;

		/// <summary>The fit mode a reader starts with.</summary>
		public ImageFitMode Fit { get; set; } = ImageFitMode.Fit;

		/// <summary>How many positions after the current one are decoded in advance.</summary>
		public int Ahead { get; set; } = 2;

		/// <summary>How many positions before the current one are kept decoded.</summary>
		public int Behind { get; set; } = 1;

		/// <summary>A page that takes longer than this to appear shows "Loading page N" (the previous page stays on screen until then).</summary>
		public TimeSpan LoadingDelay { get; set; } = TimeSpan.FromMilliseconds(250);

		/// <summary>The distance of one arrow key press when the page is scrolled, in device independent pixels.</summary>
		public double ArrowScrollStep { get; set; } = 60;

		/// <summary>One notch of a wheel unit, in device independent pixels (Avalonia's own ScrollViewer uses 50).</summary>
		public double WheelScrollStep { get; set; } = 50;

		/// <summary>The fraction of the window that Space scrolls before it turns the page.</summary>
		public double SpaceScrollFraction { get; set; } = 0.9;

		/// <summary>
		/// Draw the outline of each page and the shadow around the pages (<c>DisplayWorkspace.DrawRealisticPages</c>, true by default in the original;
		/// the toggle, Shift+D, comes with P5.23f).
		/// </summary>
		public bool RealisticPages { get; set; } = true;

		// ---- the overlays and the cursor: the defaults of Settings.cs and EngineConfiguration (Preferences > Reader > Overlays is P5.22a; <see cref="From"/> reads them from the saved preferences)

		/// <summary><c>ShowCurrentPageOverlay</c> (true): the page number fades in at the top right when the page changes.</summary>
		public bool ShowCurrentPageOverlay { get; set; } = true;

		/// <summary><c>CurrentPageShowsName</c> (false): the page overlay also shows the names of the page files. Not shown yet (P5.22a).</summary>
		public bool CurrentPageShowsName { get; set; }

		private bool showVisiblePagePartOverlay = true;
		private AppPreferences preferences;

		/// <summary>
		/// <c>ShowVisiblePagePartOverlay</c> (true): the miniature of the page with the visible part, when the page is larger than the window. Closing the
		/// miniature turns it off for good: the value is written back to the preferences (<c>MainForm</c>: <c>VisibleInfoOverlaysChanged</c> sets
		/// <c>Program.Settings.ShowVisiblePagePartOverlay</c>).
		/// </summary>
		public bool ShowVisiblePagePartOverlay
		{
			get => showVisiblePagePartOverlay;
			set
			{
				showVisiblePagePartOverlay = value;
				if (preferences != null)
				{
					preferences.ShowVisiblePagePartOverlay = value;
				}
			}
		}

		/// <summary><c>HideVisiblePartOverlayClose</c> (false): the miniature has a close button.</summary>
		public bool HideVisiblePartOverlayClose { get; set; }

		/// <summary><c>ShowStatusOverlay</c> (true): "Loading Page N..." and the messages in the middle of the page.</summary>
		public bool ShowStatusOverlay { get; set; } = true;

		/// <summary><c>ShowNavigationOverlay</c> (true): the navigation overlay slides in when the pointer comes to the edge.</summary>
		public bool ShowNavigationOverlay { get; set; } = true;

		/// <summary><c>NavigationOverlayOnTop</c> (false): the navigation overlay sits at the top edge.</summary>
		public bool NavigationOverlayOnTop { get; set; }

		/// <summary><c>HideCursorFullScreen</c> (true): in full screen the cursor is hidden after <see cref="AutoHideCursorDelay"/> without movement.</summary>
		public bool HideCursorFullScreen { get; set; } = true;

		/// <summary><c>ExtendedSettings.AutoHideCursorDuration</c> (5000 ms).</summary>
		public int AutoHideCursorDelay { get; set; } = 5000;

		/// <summary>The two-finger swipe detector.</summary>
		public SwipeRecognizer Swipe { get; set; } = new SwipeRecognizer();

		/// <summary>Where the page turn measurements and other reader events are logged.</summary>
		public Action<string> Log { get; set; } = message => AppLog.Info(message);
	}
}
