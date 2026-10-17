using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;

namespace ComicRack.Mac.Reader
{
	/// <summary>One finished page turn.</summary>
	public sealed class PageTurnRecord
	{
		public long Turn { get; set; }

		/// <summary>The position that was shown (0-based).</summary>
		public int Position { get; set; }

		/// <summary>True if the page was decoded before the turn (a prefetch hit): the turn then did no decoding at all.</summary>
		public bool Cached { get; set; }

		/// <summary>From the command (key, swipe) to the moment the new picture was handed to the view, on the UI thread. Includes the wait for the decode when the page was not cached.</summary>
		public double UiMilliseconds { get; set; }

		/// <summary>From that moment to the end of the draw call that drew the new picture, on the render thread.</summary>
		public double RenderMilliseconds { get; set; }

		/// <summary>The whole turn: command to the end of the draw call.</summary>
		public double TotalMilliseconds => UiMilliseconds + RenderMilliseconds;

		/// <summary>The decode time of the page (the Core read plus the Skia decode) when it was not cached, else 0.</summary>
		public double DecodeMilliseconds { get; set; }

		public override string ToString()
		{
			return string.Format(CultureInfo.InvariantCulture, "page {0} {1}: total {2:0.0} ms (ui {3:0.0} + draw {4:0.0}{5})",
				Position + 1, Cached ? "cached" : "decoded", TotalMilliseconds, UiMilliseconds, RenderMilliseconds,
				Cached ? string.Empty : string.Format(CultureInfo.InvariantCulture, ", decode {0:0.0}", DecodeMilliseconds));
		}
	}

	/// <summary>
	/// Measures page turns from the command to the draw. The view model calls <see cref="Begin"/> when a command moves the reader,
	/// and <see cref="Published"/> when it hands the new picture to the view; the draw operation calls <see cref="Presented"/> from the
	/// render thread when it has drawn that picture. A turn that is replaced before it is drawn (the user paged on) is dropped.
	///
	/// "Presented" is the end of the Skia draw call. On a Metal-backed canvas the GPU finishes and the screen flips a little later
	/// (up to one frame, 8 ms at 120 Hz); that part is not measured here. Under the headless platform the draw happens when a frame is
	/// captured or the render timer is ticked.
	/// </summary>
	public sealed class PageTurnMeter
	{
		private const int KeepRecords = 500;

		private readonly object gate = new object();
		private readonly List<PageTurnRecord> records = new List<PageTurnRecord>();
		private readonly Dictionary<long, Pending> pending = new Dictionary<long, Pending>();
		private readonly Action<string> log;
		private long nextTurn;

		public PageTurnMeter(Action<string> log = null)
		{
			this.log = log;
		}

		/// <summary>Starts timing a turn to <paramref name="position"/>; returns its id.</summary>
		public long Begin(int position)
		{
			long id = Interlocked.Increment(ref nextTurn);
			lock (gate)
			{
				// Turns that were never drawn belong to pages the user passed over.
				foreach (long old in pending.Keys.Where(k => k < id - 8).ToList())
				{
					pending.Remove(old);
				}
				pending[id] = new Pending { Position = position, Start = Stopwatch.GetTimestamp() };
			}
			return id;
		}

		/// <summary>The picture of the turn is with the view (UI thread).</summary>
		public void Published(long turn, bool cached, double decodeMilliseconds)
		{
			lock (gate)
			{
				if (pending.TryGetValue(turn, out Pending p))
				{
					p.Published = Stopwatch.GetTimestamp();
					p.Cached = cached;
					p.DecodeMilliseconds = decodeMilliseconds;
				}
			}
		}

		/// <summary>The draw call of the turn ended (render thread). Only the first call per turn counts.</summary>
		public void Presented(long turn)
		{
			long now = Stopwatch.GetTimestamp();
			PageTurnRecord record = null;
			lock (gate)
			{
				if (!pending.TryGetValue(turn, out Pending p) || p.Published == 0)
				{
					return;
				}
				pending.Remove(turn);
				record = new PageTurnRecord
				{
					Turn = turn,
					Position = p.Position,
					Cached = p.Cached,
					UiMilliseconds = Ms(p.Published - p.Start),
					RenderMilliseconds = Ms(now - p.Published),
					DecodeMilliseconds = p.DecodeMilliseconds,
				};
				records.Add(record);
				if (records.Count > KeepRecords)
				{
					records.RemoveRange(0, records.Count - KeepRecords);
				}
			}
			try
			{
				log?.Invoke("Page turn " + record);
			}
			catch (Exception)
			{
			}
		}

		/// <summary>The finished turns, oldest first.</summary>
		public PageTurnRecord[] Records
		{
			get
			{
				lock (gate)
				{
					return records.ToArray();
				}
			}
		}

		/// <summary>The median and the 95th percentile of the total time of the turns that match <paramref name="cached"/>, or false if there are none.</summary>
		public bool TryGetStatistics(bool cached, out double median, out double p95, out int count)
		{
			double[] totals = Records.Where(r => r.Cached == cached).Select(r => r.TotalMilliseconds).OrderBy(t => t).ToArray();
			count = totals.Length;
			if (count == 0)
			{
				median = p95 = 0;
				return false;
			}
			median = totals[count / 2];
			p95 = totals[Math.Min(count - 1, (int)Math.Ceiling(count * 0.95) - 1)];
			return true;
		}

		private static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

		private sealed class Pending
		{
			public int Position;
			public long Start;
			public long Published;
			public bool Cached;
			public double DecodeMilliseconds;
		}
	}
}
