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

		/// <summary>The layout: single page in M2.3a.</summary>
		public IPageLayout Layout { get; set; } = SinglePageLayout.Instance;

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
