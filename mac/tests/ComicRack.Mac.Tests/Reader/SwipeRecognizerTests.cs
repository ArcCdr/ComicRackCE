using System;
using System.Collections.Generic;
using ComicRack.Mac.Reader;
using Xunit;

namespace ComicRack.Mac.Tests.Reader
{
	/// <summary>
	/// The swipe detector with generated event streams that look like a macOS trackpad: events about every 8 ms, a few dozen while the
	/// fingers move, then an exponentially decaying momentum tail of up to a second.
	/// </summary>
	public class SwipeRecognizerTests
	{
		/// <summary>The events of one swipe: deltas (units per event) of the finger phase, then the momentum tail.</summary>
		private static List<(double dx, double dy, long t)> Swipe(long start, double direction, double fingerDelta = 0.3, int fingerEvents = 20, double tailStart = 0.25, int tailEvents = 90, double decay = 0.94)
		{
			var events = new List<(double, double, long)>();
			long t = start;
			for (int i = 0; i < fingerEvents; i++)
			{
				events.Add((direction * fingerDelta, 0.02 * (i % 3 - 1), t));
				t += 8;
			}
			double d = tailStart;
			for (int i = 0; i < tailEvents; i++)
			{
				events.Add((direction * d, 0, t));
				d *= decay;
				t += 8;
			}
			return events;
		}

		private static int Count(SwipeRecognizer r, IEnumerable<(double dx, double dy, long t)> events, out List<int> results)
		{
			results = new List<int>();
			int turns = 0;
			foreach (var e in events)
			{
				int result = r.Feed(e.dx, e.dy, e.t);
				if (result != 0)
				{
					turns++;
					results.Add(result);
				}
			}
			return turns;
		}

		[Fact]
		public void One_swipe_to_the_left_with_its_momentum_tail_turns_exactly_one_page_forward()
		{
			var r = new SwipeRecognizer();
			int turns = Count(r, Swipe(0, -1), out List<int> results);
			Assert.Equal(1, turns);
			Assert.Equal(new[] { 1 }, results);
		}

		[Fact]
		public void One_swipe_to_the_right_turns_one_page_back()
		{
			var r = new SwipeRecognizer();
			Assert.Equal(1, Count(r, Swipe(0, 1), out List<int> results));
			Assert.Equal(new[] { -1 }, results);
		}

		[Fact]
		public void Two_swipes_with_a_pause_between_them_turn_two_pages()
		{
			var r = new SwipeRecognizer();
			var events = Swipe(0, -1);
			long end = events[events.Count - 1].t;
			events.AddRange(Swipe(end + 600, -1)); // the user lifted off, waited, and swiped again
			Assert.Equal(2, Count(r, events, out _));
		}

		[Fact]
		public void A_second_swipe_started_while_the_momentum_of_the_first_still_runs_counts_as_a_new_swipe()
		{
			var r = new SwipeRecognizer();
			var events = Swipe(0, -1);
			// 400 ms into the tail (the tail is still moving, but weak) the fingers come down again and swipe harder
			long restart = events[20].t + 400;
			events.RemoveAll(e => e.t >= restart);
			events.AddRange(Swipe(restart, -1, fingerDelta: 0.4));
			Assert.Equal(2, Count(r, events, out _));
		}

		[Fact]
		public void A_quick_swipe_back_during_the_momentum_of_the_first_turns_in_the_other_direction()
		{
			var r = new SwipeRecognizer();
			var events = Swipe(0, -1);
			long restart = events[20].t + 300;
			events.RemoveAll(e => e.t >= restart);
			events.AddRange(Swipe(restart, 1, fingerDelta: 0.4));
			Count(r, events, out List<int> results);
			Assert.Equal(new[] { 1, -1 }, results);
		}

		[Fact]
		public void A_vertical_scroll_never_turns_a_page_even_when_it_wobbles_sideways()
		{
			var r = new SwipeRecognizer();
			var events = new List<(double, double, long)>();
			long t = 0;
			for (int i = 0; i < 80; i++)
			{
				events.Add((0.05 * (i % 2 == 0 ? 1 : -1) + 0.02, 0.5, t));
				t += 8;
			}
			Assert.Equal(0, Count(r, events, out _));
		}

		[Fact]
		public void A_diagonal_scroll_is_not_a_swipe_until_it_is_clearly_horizontal()
		{
			var r = new SwipeRecognizer();
			long t = 0;
			for (int i = 0; i < 40; i++)
			{
				Assert.Equal(0, r.Feed(-0.3, 0.3, t)); // 45 degrees: a scroll
				t += 8;
			}
			// the same finger then moves sideways: now it counts
			int turned = 0;
			for (int i = 0; i < 20; i++)
			{
				turned += r.Feed(-0.3, 0.05, t) != 0 ? 1 : 0;
				t += 8;
			}
			Assert.Equal(1, turned);
		}

		[Fact]
		public void A_small_slow_drift_below_the_threshold_turns_nothing()
		{
			var r = new SwipeRecognizer();
			long t = 0;
			for (int i = 0; i < 20; i++)
			{
				Assert.Equal(0, r.Feed(-0.02, 0, t)); // 0.4 units in all: under the threshold of 1
				t += 8;
			}
		}

		[Fact]
		public void A_pause_resets_the_sum_so_that_two_small_nudges_do_not_add_up()
		{
			var r = new SwipeRecognizer();
			Assert.Equal(0, r.Feed(-0.7, 0, 0));
			Assert.Equal(0, r.Feed(-0.7, 0, 500)); // 500 ms later: a new gesture, and 0.7 alone is under the threshold
			Assert.Equal(1, r.Feed(-0.7, 0, 508)); // but together within one gesture they pass it
		}

		[Fact]
		public void A_single_large_event_turns_a_page()
		{
			var r = new SwipeRecognizer();
			Assert.Equal(1, r.Feed(-3, 0, 0));
			Assert.Equal(0, r.Feed(-3, 0, 8)); // the same gesture goes on: nothing more
		}

		[Fact]
		public void Jitter_in_the_momentum_tail_does_not_count_as_a_new_swipe()
		{
			var r = new SwipeRecognizer();
			Assert.Equal(1, r.Feed(-1.2, 0, 0));
			long t = 8;
			// a quantised tail: 0.06, 0.02, 0.06, 0.02, ... never reaches a quarter of the threshold
			for (int i = 0; i < 100; i++)
			{
				Assert.Equal(0, r.Feed(i % 2 == 0 ? -0.06 : -0.02, 0, t));
				t += 8;
			}
		}

		[Fact]
		public void The_threshold_is_a_setting()
		{
			var r = new SwipeRecognizer { Threshold = 4 };
			Assert.Equal(0, r.Feed(-3, 0, 0));
			Assert.Equal(1, r.Feed(-1.5, 0, 8));
		}
	}
}
