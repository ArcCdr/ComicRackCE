using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia.Threading;

namespace ComicRack.Mac.Controls
{
	/// <summary>One animation of a reader overlay that <see cref="ReaderOverlayClock"/> moves on (a fade, the slide of the navigation overlay).</summary>
	public interface IOverlayAnimation
	{
		/// <summary>Moves the animation on by <paramref name="milliseconds"/>; returns true while it has more to do.</summary>
		bool Advance(double milliseconds);
	}

	/// <summary>
	/// The animation clock of the reader overlays (<c>OverlayManager</c>'s animation timer): one 16 ms timer moves every running animation and
	/// stops when none is left. <see cref="Enabled"/> is off in a headless run (the app sets it from <c>ApplicationLifetime</c>); a test then
	/// moves the animations itself with <see cref="Advance"/>, so no test depends on how fast the machine is.
	/// </summary>
	public static class ReaderOverlayClock
	{
		private static readonly List<IOverlayAnimation> running = new List<IOverlayAnimation>();
		private static DispatcherTimer timer;
		private static long timerTicks;

		/// <summary>Whether <see cref="Start"/> runs a timer. Off, nothing moves until <see cref="Advance"/> is called.</summary>
		public static bool Enabled { get; set; } = true;

		/// <summary>The animations that are running.</summary>
		public static int Count => running.Count;

		/// <summary>Starts moving <paramref name="animation"/> (once; a second call keeps it running).</summary>
		public static void Start(IOverlayAnimation animation)
		{
			if (animation == null)
			{
				throw new ArgumentNullException(nameof(animation));
			}
			if (!running.Contains(animation))
			{
				running.Add(animation);
			}
			if (!Enabled)
			{
				return;
			}
			if (timer == null)
			{
				timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
				timer.Tick += (sender, e) =>
				{
					long now = Stopwatch.GetTimestamp();
					double elapsed = (now - timerTicks) * 1000.0 / Stopwatch.Frequency;
					timerTicks = now;
					Advance(elapsed);
				};
			}
			if (!timer.IsEnabled)
			{
				timerTicks = Stopwatch.GetTimestamp();
				timer.Start();
			}
		}

		/// <summary>Stops moving <paramref name="animation"/>.</summary>
		public static void Stop(IOverlayAnimation animation)
		{
			running.Remove(animation);
			if (running.Count == 0)
			{
				timer?.Stop();
			}
		}

		/// <summary>Moves every running animation on by <paramref name="milliseconds"/>; returns how many are still running.</summary>
		public static int Advance(double milliseconds)
		{
			foreach (IOverlayAnimation animation in running.ToArray())
			{
				if (!animation.Advance(milliseconds))
				{
					running.Remove(animation);
				}
			}
			if (running.Count == 0)
			{
				timer?.Stop();
			}
			return running.Count;
		}
	}
}
