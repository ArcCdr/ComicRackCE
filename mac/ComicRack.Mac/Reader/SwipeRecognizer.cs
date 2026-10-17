using System;

namespace ComicRack.Mac.Reader
{
	/// <summary>
	/// Turns the stream of horizontal wheel events of a two-finger trackpad swipe into at most one page turn per swipe.
	///
	/// A swipe arrives as many small deltas while the fingers move, followed by a "momentum" tail of decaying deltas for up to a second
	/// after they lift. Turning a page for every event, or every time the sum passes a threshold, would turn several pages per swipe.
	/// The rules:
	/// <list type="bullet">
	/// <item>A gesture is a run of events with no pause longer than <see cref="QuietGap"/> between them.</item>
	/// <item>The horizontal deltas of a gesture add up; when the sum passes <see cref="Threshold"/> the gesture turns one page and is
	/// then "spent": everything after it, including the momentum tail, is ignored.</item>
	/// <item>A spent gesture ends when events stop for <see cref="QuietGap"/> <b>or</b> when a delta is clearly bigger than the smallest
	/// one since the turn (<see cref="ReaccelerationFactor"/>): the momentum tail only gets weaker, so a stronger event means the
	/// user swiped again (also in the other direction) while it was still running.</item>
	/// <item>An event that is mostly vertical (<see cref="HorizontalDominance"/>) is a scroll, not a swipe: it adds nothing and clears
	/// the sum, so a slightly diagonal scroll never turns a page.</item>
	/// </list>
	/// The sign follows <c>PointerWheelEventArgs.Delta.X</c> as <c>ScrollViewer</c> reads it: fingers moving left (the content follows
	/// them to the left) give a negative delta and mean "next page" (+1). The units are Avalonia's wheel units; whether the macOS
	/// backend scales trackpad pixels to lines (50 px per line, as other Avalonia platforms do) or passes pixels was not measurable
	/// without a trackpad, so <see cref="Threshold"/> is a setting.
	/// </summary>
	public sealed class SwipeRecognizer
	{
		private double sum;
		private long lastEvent = long.MinValue;
		private bool spent;
		private double smallestSinceTurn;

		/// <summary>The horizontal distance, in wheel units, that turns a page.</summary>
		public double Threshold { get; set; } = 1.0;

		/// <summary>A pause that ends a gesture.</summary>
		public TimeSpan QuietGap { get; set; } = TimeSpan.FromMilliseconds(180);

		/// <summary>A delta this many times the smallest one since the turn starts a new gesture.</summary>
		public double ReaccelerationFactor { get; set; } = 2.5;

		/// <summary>The horizontal delta must be at least this many times the vertical one.</summary>
		public double HorizontalDominance { get; set; } = 1.5;

		/// <summary>
		/// Feeds one wheel event. <paramref name="timestampMilliseconds"/> is any monotonic clock in milliseconds.
		/// Returns +1 for "next page" (swipe to the left), -1 for "previous page", 0 for nothing.
		/// </summary>
		public int Feed(double deltaX, double deltaY, long timestampMilliseconds)
		{
			if (lastEvent != long.MinValue && timestampMilliseconds - lastEvent > QuietGap.TotalMilliseconds)
			{
				Reset();
			}
			lastEvent = timestampMilliseconds;
			double magnitude = Math.Abs(deltaX);
			if (magnitude < 1e-9 || magnitude < Math.Abs(deltaY) * HorizontalDominance)
			{
				// A scroll (or nothing at all): it is not part of a swipe, and it clears what a swipe had gathered.
				if (!spent)
				{
					sum = 0;
				}
				return 0;
			}
			if (spent)
			{
				if (magnitude >= smallestSinceTurn * ReaccelerationFactor && magnitude >= Threshold / 4)
				{
					Reset(); // the user swiped again while the momentum of the last swipe was still running
				}
				else
				{
					smallestSinceTurn = Math.Min(smallestSinceTurn, magnitude);
					return 0;
				}
			}
			sum += deltaX;
			if (Math.Abs(sum) < Threshold)
			{
				return 0;
			}
			int direction = sum < 0 ? 1 : -1;
			spent = true;
			smallestSinceTurn = magnitude;
			sum = 0;
			return direction;
		}

		/// <summary>Forgets the gesture in progress.</summary>
		public void Reset()
		{
			sum = 0;
			spent = false;
			smallestSinceTurn = 0;
		}
	}
}
