using System;
using System.Collections.Generic;

namespace ComicRack.Mac.Diagnostics
{
	/// <summary>
	/// Frames per second over the last second. <see cref="Frame"/> is called once per frame (from the render loop); the clock is
	/// injected so that tests can drive it.
	/// </summary>
	public sealed class FpsCounter
	{
		private readonly Func<TimeSpan> clock;
		private readonly TimeSpan window;
		private readonly Queue<TimeSpan> frames = new Queue<TimeSpan>();

		public FpsCounter(Func<TimeSpan> clock, TimeSpan? window = null)
		{
			this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
			this.window = window ?? TimeSpan.FromSeconds(1);
		}

		public void Frame()
		{
			TimeSpan now = clock();
			frames.Enqueue(now);
			Trim(now);
		}

		public void Reset()
		{
			frames.Clear();
		}

		/// <summary>Frames per second now: 0 if fewer than two frames came in the last second.</summary>
		public double Fps
		{
			get
			{
				Trim(clock());
				if (frames.Count < 2)
				{
					return 0;
				}
				TimeSpan first = frames.Peek();
				TimeSpan last = TimeSpan.Zero;
				foreach (TimeSpan t in frames)
				{
					last = t;
				}
				double seconds = (last - first).TotalSeconds;
				return seconds > 0 ? (frames.Count - 1) / seconds : 0;
			}
		}

		private void Trim(TimeSpan now)
		{
			while (frames.Count > 0 && now - frames.Peek() > window)
			{
				frames.Dequeue();
			}
		}
	}
}
