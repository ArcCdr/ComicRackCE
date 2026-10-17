using System;
using cYo.Projects.ComicRack.Engine.Display;
using ComicRack.Mac.Services;

namespace ComicRack.Mac.Reader
{
	/// <summary>Settings of a <see cref="ComicRack.Mac.ViewModels.ReaderViewModel"/>. The defaults are what the app uses; tests change some.</summary>
	public sealed class ReaderOptions
	{
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

		/// <summary>The two-finger swipe detector.</summary>
		public SwipeRecognizer Swipe { get; set; } = new SwipeRecognizer();

		/// <summary>Where the page turn measurements and other reader events are logged.</summary>
		public Action<string> Log { get; set; } = message => AppLog.Info(message);
	}
}
