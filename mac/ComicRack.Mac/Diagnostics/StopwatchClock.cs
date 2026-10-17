using System;
using System.Diagnostics;

namespace ComicRack.Mac.Diagnostics
{
	/// <summary>The monotonic clock of the overlay (time since the first use).</summary>
	public static class StopwatchClock
	{
		private static readonly Stopwatch watch = Stopwatch.StartNew();

		public static TimeSpan Now() => watch.Elapsed;
	}
}
