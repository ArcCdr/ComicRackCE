using System;
using Avalonia;
using Avalonia.Controls;
using ComicRack.Mac.Themes;

namespace ComicRack.Mac.Controls
{
	/// <summary>
	/// What the overlay panels of the reader (<c>OverlayPanel</c> of <c>cYo.Common.Presentation</c>) have in common: they float over the page on
	/// glass pictures, can fade in, stay and fade out (<c>FadeAnimator</c>: 100, 2000, 100 ms for the page info and the visible part), and are
	/// driven by <see cref="ReaderOverlayClock"/>. The overlays do not take the keyboard. A fade starts transparent and is moved by the clock;
	/// with the clock off (a headless run) it stays at the start until a test advances it.
	/// </summary>
	public abstract class CrReaderOverlay : Control, IChromeElement, IOverlayAnimation
	{
		private double fadeIn, fadeHold, fadeOut, fadeElapsed;
		private bool fading;

		protected CrReaderOverlay()
		{
			Focusable = false;
			ActualThemeVariantChanged += (sender, e) => InvalidateVisual();
			ChromeVariants.Bind(this);
		}

		public ChromeElement Element => ChromeElement.ReaderOverlay;

		/// <summary>True while a fade is running.</summary>
		public bool IsFading => fading;

		/// <summary>
		/// True for an overlay that shows only while a fade runs (the page info, the visible part: <c>Opacity = 0f</c> in the original). It is
		/// transparent as long as no fade has started.
		/// </summary>
		public bool FadeOnly
		{
			get => fadeOnly;
			set
			{
				fadeOnly = value;
				if (value && !fading)
				{
					Opacity = 0;
				}
				else if (!value && !fading)
				{
					Opacity = 1;
				}
			}
		}

		private bool fadeOnly;

		/// <summary>
		/// Starts <c>FadeAnimator(fadeIn, hold, fadeOut)</c>: the opacity rises from 0 to 1 in <paramref name="fadeInMilliseconds"/>, stays for
		/// <paramref name="holdMilliseconds"/> and falls to 0 in <paramref name="fadeOutMilliseconds"/>. A new start restarts it.
		/// </summary>
		public void StartFade(double fadeInMilliseconds = 100, double holdMilliseconds = 2000, double fadeOutMilliseconds = 100)
		{
			fadeIn = fadeInMilliseconds;
			fadeHold = holdMilliseconds;
			fadeOut = fadeOutMilliseconds;
			fadeElapsed = 0;
			fading = true;
			Opacity = FadeValue();
			ReaderOverlayClock.Start(this);
		}

		/// <summary>Ends a running fade at once and hides the overlay.</summary>
		public void StopFade()
		{
			if (fading)
			{
				fading = false;
				ReaderOverlayClock.Stop(this);
			}
			Opacity = 0;
		}

		/// <summary>The opacity the fade has at the current time (<c>Animator.CreateLinearBouncer</c>).</summary>
		private double FadeValue()
		{
			double t = fadeElapsed;
			if (t < fadeIn)
			{
				return fadeIn <= 0 ? 1 : t / fadeIn;
			}
			t -= fadeIn;
			if (t < fadeHold)
			{
				return 1;
			}
			t -= fadeHold;
			return t < fadeOut ? 1 - t / fadeOut : 0;
		}

		bool IOverlayAnimation.Advance(double milliseconds)
		{
			if (!fading)
			{
				return false;
			}
			fadeElapsed += milliseconds;
			Opacity = FadeValue();
			if (fadeElapsed >= fadeIn + fadeHold + fadeOut)
			{
				fading = false;
				Opacity = 0;
				return false;
			}
			return true;
		}

		protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
		{
			base.OnDetachedFromVisualTree(e);
			if (fading)
			{
				fading = false;
				ReaderOverlayClock.Stop(this);
			}
		}

		protected override Size MeasureOverride(Size availableSize)
		{
			return ContentSize();
		}

		/// <summary>The size the overlay wants.</summary>
		protected abstract Size ContentSize();
	}
}
