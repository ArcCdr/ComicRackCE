using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ComicRack.Mac.Browser;
using ComicRack.Mac.Themes;

namespace ComicRack.Mac.Controls
{
	/// <summary>
	/// The picture area of a cell. <see cref="Render"/> runs only when the picture or the size changes (Avalonia keeps the
	/// recorded frame otherwise), so scrolling does not redraw covers on the UI thread; the pixels are drawn by the render thread
	/// through <see cref="CoverDrawOperation"/>. The picture is the cover as large as fits the box with its proportions kept, or, for a browser
	/// whose covers all have the same size (<see cref="FillBox"/>, <c>CoverThumbnailsSameSize</c>), the whole box (a landscape picture shows its
	/// right half, as <c>ThumbRenderer.DrawThumbnail</c> crops it). Everything around the picture (shadow, frame, badges) is drawn by
	/// <see cref="CrThumbnailItem"/>.
	///
	/// <b>Fade-in</b> (<c>FadeInThumbnails</c>, <c>ThumbnailViewItem.Animate</c>): <see cref="StartFadeIn"/> starts the picture transparent and
	/// raises it to opaque in <see cref="ThumbnailMetrics.FadeMilliseconds"/>. One timer moves all fading covers (<see cref="AdvanceFades"/> is
	/// what the timer calls; a test calls it with the time it wants, with <see cref="FadeEnabled"/> off so that no timer runs).
	/// </summary>
	public sealed class CoverView : Control, IChromeElement
	{
		private static readonly List<CoverView> fading = new List<CoverView>();
		private static DispatcherTimer timer;
		private static long timerTicks;

		private CoverImage image;
		private bool fillBox;
		private double pictureOpacity = 1;

		public CoverView()
		{
			ChromeVariants.Bind(this);
		}

		public ChromeElement Element => ChromeElement.ItemView;

		/// <summary>Whether <see cref="StartFadeIn"/> runs a timer. The app turns it off for a headless run, a test turns it on to test the fade.</summary>
		public static bool FadeEnabled { get; set; } = true;

		/// <summary>Raised when the picture was replaced or removed.</summary>
		public event EventHandler ImageChanged;

		/// <summary>True: the picture's edges are rounded to whole units (the original draws on whole pixels; a thumbnail item sets it).</summary>
		public bool SnapToUnits { get; set; }

		/// <summary>True when a picture is set.</summary>
		public bool HasImage => image != null;

		/// <summary>The current picture (for tests); do not keep it without <see cref="CoverImage.Retain"/>.</summary>
		public CoverImage Image => image;

		/// <summary>True: the picture fills the box (all covers have the same size). False: it keeps its proportions inside the box.</summary>
		public bool FillBox
		{
			get => fillBox;
			set
			{
				if (fillBox != value)
				{
					fillBox = value;
					InvalidateVisual();
				}
			}
		}

		/// <summary>0 to 1; below 1 while a fresh cover fades in.</summary>
		public double PictureOpacity
		{
			get => pictureOpacity;
			set
			{
				value = Math.Clamp(value, 0, 1);
				if (pictureOpacity != value)
				{
					pictureOpacity = value;
					InvalidateVisual();
				}
			}
		}

		/// <summary>True while the picture is fading in.</summary>
		public bool IsFading => fading.Contains(this);

		/// <summary>Replaces the picture; takes its own reference. Null shows nothing (the item draws the empty tile).</summary>
		public void SetImage(CoverImage value)
		{
			if (ReferenceEquals(value, image))
			{
				return;
			}
			CoverImage old = image;
			image = value?.Retain();
			old?.Release();
			if (image == null)
			{
				StopFade();
			}
			InvalidateVisual();
			ImageChanged?.Invoke(this, EventArgs.Empty);
		}

		/// <summary>The rectangle a picture of the given size takes inside a box: as large as fits, aspect kept, centred.</summary>
		public static Rect Fit(double boxWidth, double boxHeight, double imageWidth, double imageHeight)
		{
			if (imageWidth <= 0 || imageHeight <= 0 || boxWidth <= 0 || boxHeight <= 0)
			{
				return default;
			}
			double scale = Math.Min(boxWidth / imageWidth, boxHeight / imageHeight);
			double w = imageWidth * scale, h = imageHeight * scale;
			return new Rect((boxWidth - w) / 2, (boxHeight - h) / 2, w, h);
		}

		/// <summary>Where the picture is drawn, in the coordinates of this control (empty without a picture).</summary>
		public Rect PictureRect
		{
			get
			{
				CoverImage current = image;
				if (current == null)
				{
					return default;
				}
				if (fillBox)
				{
					return new Rect(0, 0, Bounds.Width, Bounds.Height);
				}
				Rect fit = Fit(Bounds.Width, Bounds.Height, current.Width, current.Height);
				if (!SnapToUnits)
				{
					return fit;
				}
				double left = Math.Round(fit.Left), top = Math.Round(fit.Top);
				return new Rect(left, top, Math.Round(fit.Right) - left, Math.Round(fit.Bottom) - top);
			}
		}

		/// <summary>The part of the picture that is drawn: all of it, or for a same size browser the right part of a landscape picture in a portrait box.</summary>
		public static Rect SourceRect(double boxWidth, double boxHeight, int imageWidth, int imageHeight, bool fill)
		{
			var all = new Rect(0, 0, imageWidth, imageHeight);
			if (fill && imageWidth > imageHeight && boxHeight > boxWidth)
			{
				double width = Math.Min(imageWidth, imageHeight * boxWidth / boxHeight);
				return new Rect(imageWidth - width, 0, width, imageHeight);
			}
			return all;
		}

		public override void Render(DrawingContext context)
		{
			CoverImage current = image;
			if (current == null)
			{
				return;
			}
			Rect target = PictureRect;
			if (target.Width > 0)
			{
				Rect? source = fillBox ? SourceRect(Bounds.Width, Bounds.Height, current.Width, current.Height, true) : (Rect?)null;
				if (source == new Rect(0, 0, current.Width, current.Height))
				{
					source = null;
				}
				context.Custom(new CoverDrawOperation(current, target, source, pictureOpacity));
			}
		}

		protected override Size MeasureOverride(Size availableSize)
		{
			return new Size(double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width, double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height);
		}

		// ---- fade-in ---------------------------------------------------------------------------------------------------

		/// <summary>The picture that was just set starts transparent and rises to opaque (does nothing when <see cref="FadeEnabled"/> is off).</summary>
		public void StartFadeIn()
		{
			if (!FadeEnabled || image == null)
			{
				PictureOpacity = 1;
				return;
			}
			PictureOpacity = 0;
			if (!fading.Contains(this))
			{
				fading.Add(this);
			}
			if (timer == null)
			{
				timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
				timer.Tick += (sender, e) =>
				{
					long now = Stopwatch.GetTimestamp();
					double elapsed = (now - timerTicks) * 1000.0 / Stopwatch.Frequency;
					timerTicks = now;
					AdvanceFades(elapsed);
				};
			}
			if (!timer.IsEnabled)
			{
				timerTicks = Stopwatch.GetTimestamp();
				timer.Start();
			}
		}

		private void StopFade()
		{
			fading.Remove(this);
			pictureOpacity = 1;
		}

		/// <summary>Moves every fading picture on by <paramref name="milliseconds"/> (<c>animationTimer_Elapsed</c>); returns how many are still fading.</summary>
		public static int AdvanceFades(double milliseconds)
		{
			for (int i = fading.Count - 1; i >= 0; i--)
			{
				CoverView view = fading[i];
				view.PictureOpacity += milliseconds / ThumbnailMetrics.FadeMilliseconds;
				if (view.PictureOpacity >= 1)
				{
					fading.RemoveAt(i);
				}
			}
			if (fading.Count == 0)
			{
				timer?.Stop();
			}
			return fading.Count;
		}

		protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
		{
			base.OnDetachedFromVisualTree(e);
			if (fading.Remove(this))
			{
				pictureOpacity = 1;
			}
		}
	}
}
