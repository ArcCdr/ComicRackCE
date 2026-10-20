using System;
using System.Collections.Generic;
using System.Diagnostics;
using ComicRack.Mac.Diagnostics;
using ComicRack.Mac.Tests.Thumbnails;
using Xunit;

namespace ComicRack.Mac.Tests.Diagnostics
{
	/// <summary>A ticker that runs only when the test fires it, and counts how often it was started and stopped.</summary>
	public sealed class FakeTicker : IPerfTicker
	{
		private Action tick;

		public bool IsRunning => tick != null;

		public int Starts { get; private set; }

		public int Stops { get; private set; }

		public TimeSpan Interval { get; private set; }

		public void Start(TimeSpan interval, Action tick)
		{
			Starts++;
			Interval = interval;
			this.tick = tick;
		}

		public void Stop()
		{
			if (tick != null)
			{
				Stops++;
			}
			tick = null;
		}

		/// <summary>One timer tick; nothing happens if the timer is not running.</summary>
		public void Fire() => tick?.Invoke();
	}

	public sealed class FakeClock
	{
		public TimeSpan Now { get; set; }

		public TimeSpan Read() => Now;
	}

	public class PerfOverlayTests
	{
		private sealed class Rig
		{
			public FakeClock Clock = new FakeClock();
			public FakeTicker Ticker = new FakeTicker();
			public List<string> Log = new List<string>();
			public int Reads;
			public PerfSources Sources;
			public PerfOverlayController Controller;

			public Rig()
			{
				Sources = new PerfSources
				{
					DecodeMilliseconds = () => { Reads++; return 23.46; },
					CachedTurnMilliseconds = () => 7.5,
					Thumbnails = () => (12, 2),
					ManagedHeapBytes = () => 123 * 1024 * 1024,
					Memory = () => new ProcessMemoryInfo(456L * 1024 * 1024, 400L * 1024 * 1024, true),
				};
				Controller = new PerfOverlayController(Sources, Ticker, Clock.Read, Log.Add);
			}

			/// <summary>Advances the fake clock by one update interval and fires the timer.</summary>
			public void Step(int count = 1)
			{
				for (int i = 0; i < count; i++)
				{
					Clock.Now += PerfOverlayController.UpdateInterval;
					Ticker.Fire();
				}
			}
		}

		[Fact]
		public void Bytes_are_shown_in_MB_and_in_GB_from_1024_MB()
		{
			Assert.Equal("0.0 MB", PerfFormat.Bytes(0));
			Assert.Equal("1.5 MB", PerfFormat.Bytes(1572864));
			Assert.Equal("1023.9 MB", PerfFormat.Bytes(1073689395));
			Assert.Equal("1.00 GB", PerfFormat.Bytes(1024L * 1024 * 1024));
			Assert.Equal("2.50 GB", PerfFormat.Bytes((long)(2.5 * 1024 * 1024 * 1024)));
		}

		[Fact]
		public void Missing_values_show_a_dash_and_the_text_does_not_depend_on_the_culture()
		{
			var old = System.Globalization.CultureInfo.CurrentCulture;
			System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("fr-FR");
			try
			{
				var s = new PerfSnapshot { Fps = 59.96, ManagedHeapBytes = 1572864 };
				string[] lines = PerfFormat.Lines(s);
				Assert.Equal(7, lines.Length);
				Assert.Equal("FPS         60.0", lines[0]);
				Assert.Equal("Decode      -", lines[1]);
				Assert.Equal("Page turn   -", lines[2]);
				Assert.Equal("Thumbnails  -", lines[3]);
				Assert.Equal("Heap        1.5 MB", lines[4]);
				Assert.Equal("Footprint   0.0 MB", lines[5]);
				Assert.Equal("RSS         0.0 MB", lines[6]);
			}
			finally
			{
				System.Globalization.CultureInfo.CurrentCulture = old;
			}
		}

		[Fact]
		public void The_lines_and_the_log_line_carry_every_value()
		{
			var r = new Rig();
			PerfSnapshot s = r.Controller.Sample();
			Assert.Equal(new[]
			{
				"FPS         0.0",
				"Decode      23.5 ms",
				"Page turn   7.5 ms",
				"Thumbnails  12 queued, 2 running",
				"Heap        123.0 MB",
				"Footprint   456.0 MB",
				"RSS         400.0 MB",
			}, PerfFormat.Lines(s));
			Assert.Equal("Perf: fps 0.0, decode 23.5 ms, cached page turn 7.5 ms, thumbnails 12 queued, 2 running, heap 123.0 MB, footprint 456.0 MB, rss 400.0 MB.", PerfFormat.LogLine(s));
		}

		[Fact]
		public void The_frame_counter_measures_the_last_second_and_goes_to_zero_when_frames_stop()
		{
			var clock = new FakeClock();
			var counter = new FpsCounter(clock.Read);
			Assert.Equal(0, counter.Fps);
			for (int i = 0; i < 120; i++)
			{
				counter.Frame();
				clock.Now += TimeSpan.FromSeconds(1.0 / 60);
			}
			Assert.InRange(counter.Fps, 59.5, 60.5);

			// 30 fps afterwards: the old 60 fps frames leave the window.
			for (int i = 0; i < 90; i++)
			{
				counter.Frame();
				clock.Now += TimeSpan.FromSeconds(1.0 / 30);
			}
			Assert.InRange(counter.Fps, 29.5, 30.5);

			clock.Now += TimeSpan.FromSeconds(2);
			Assert.Equal(0, counter.Fps);
		}

		[Fact]
		public void Hidden_means_no_timer_and_no_sampling()
		{
			var r = new Rig();
			Assert.False(r.Controller.IsVisible);
			Assert.False(r.Ticker.IsRunning);
			Assert.Equal(0, r.Ticker.Starts);
			r.Step(5); // a tick that cannot happen: the timer does not run
			Assert.Equal(0, r.Reads);
			Assert.Empty(r.Log);
			Assert.Null(r.Controller.Last);
		}

		[Fact]
		public void Showing_starts_one_timer_at_500_ms_and_hiding_stops_it()
		{
			var r = new Rig();
			int updates = 0;
			r.Controller.Updated += s => updates++;
			r.Controller.Toggle();
			Assert.True(r.Controller.IsVisible);
			Assert.True(r.Ticker.IsRunning);
			Assert.Equal(TimeSpan.FromMilliseconds(500), r.Ticker.Interval);
			Assert.Equal(1, updates); // sampled at once, not after the first interval
			Assert.NotNull(r.Controller.Last);

			r.Step(4);
			Assert.Equal(5, updates);

			r.Controller.Toggle();
			Assert.False(r.Controller.IsVisible);
			Assert.False(r.Ticker.IsRunning);
			int reads = r.Reads;
			r.Step(10);
			Assert.Equal(5, updates);
			Assert.Equal(reads, r.Reads);

			// Showing twice does not leave two timers; the toggle is repeatable.
			r.Controller.IsVisible = true;
			r.Controller.IsVisible = true;
			Assert.Equal(2, r.Ticker.Starts);
			Assert.Equal(1, r.Ticker.Stops);
			r.Controller.Hide();
			Assert.False(r.Ticker.IsRunning);
		}

		[Fact]
		public void The_toggle_command_toggles()
		{
			var r = new Rig();
			r.Controller.ToggleCommand.Execute(null);
			Assert.True(r.Controller.IsVisible);
			r.Controller.ToggleCommand.Execute(null);
			Assert.False(r.Controller.IsVisible);
		}

		[Fact]
		public void The_log_gets_one_line_every_10_seconds_while_visible_and_none_when_hidden()
		{
			var r = new Rig();
			r.Clock.Now = TimeSpan.FromSeconds(1000);
			r.Controller.Toggle();
			Assert.Empty(r.Log);

			r.Step(19); // 9.5 s
			Assert.Empty(r.Log);
			r.Step(1); // 10.0 s
			Assert.Single(r.Log);
			r.Step(19);
			Assert.Single(r.Log);
			r.Step(1); // 20.0 s
			Assert.Equal(2, r.Log.Count);
			Assert.StartsWith("Perf: fps ", r.Log[1]);

			// 10 more seconds hidden: nothing; shown again: the 10 s count starts again.
			r.Controller.Toggle();
			r.Step(30);
			Assert.Equal(2, r.Log.Count);
			r.Controller.Toggle();
			r.Step(19);
			Assert.Equal(2, r.Log.Count);
			r.Step(1);
			Assert.Equal(3, r.Log.Count);
		}

		[Fact]
		public void A_late_tick_logs_once_not_once_per_missed_interval()
		{
			var r = new Rig();
			r.Controller.Toggle();
			r.Clock.Now += TimeSpan.FromSeconds(35);
			r.Ticker.Fire();
			Assert.Single(r.Log);
			r.Clock.Now += TimeSpan.FromSeconds(1);
			r.Ticker.Fire();
			Assert.Single(r.Log);
		}

		[Fact]
		public void The_reported_frame_rate_is_the_highest_of_the_registered_windows()
		{
			var r = new Rig();
			var a = new FpsCounter(r.Clock.Read);
			var b = new FpsCounter(r.Clock.Read);
			r.Controller.Register(a);
			r.Controller.Register(b);
			r.Controller.Register(a); // twice is once
			r.Controller.Toggle();
			for (int i = 0; i < 60; i++)
			{
				a.Frame();
				if (i % 2 == 0)
				{
					b.Frame();
				}
				r.Clock.Now += TimeSpan.FromSeconds(1.0 / 120);
			}
			PerfSnapshot s = r.Controller.Sample();
			Assert.InRange(s.Fps, 118, 122);
			r.Controller.Unregister(a);
			Assert.InRange(r.Controller.Sample().Fps, 58, 62);
		}

		[Fact]
		public void A_source_that_throws_gives_an_empty_value_and_the_timer_goes_on()
		{
			var r = new Rig();
			r.Sources.DecodeMilliseconds = () => throw new InvalidOperationException("gone");
			r.Sources.Memory = () => throw new InvalidOperationException("gone");
			r.Controller.Toggle();
			r.Step(2);
			Assert.Null(r.Controller.Last.DecodeMilliseconds);
			Assert.Equal(0, r.Controller.Last.Memory.FootprintBytes);
			Assert.True(r.Ticker.IsRunning);
		}

		[Fact]
		public void Process_memory_is_read_from_the_system()
		{
			ProcessMemoryInfo info = ProcessMemory.Read();
			Assert.True(info.FootprintBytes > 10L * 1024 * 1024, "footprint " + info.FootprintBytes);
			Assert.True(info.ResidentBytes > 10L * 1024 * 1024, "resident " + info.ResidentBytes);
			if (OperatingSystem.IsMacOS())
			{
				Assert.True(info.FootprintIsPhysical);
			}
			// Same order of magnitude as the portable value.
			long workingSet = ProcessMemory.ReadWorkingSet().ResidentBytes;
			Assert.InRange(info.ResidentBytes, workingSet / 3, workingSet * 3);
		}

		[Fact]
		public void The_footprint_follows_a_big_allocation()
		{
			// The footprint is a process-wide counter: memory that earlier tests or the GC give back while this one measures lowers it
			// (one run saw 2,288 MB before and 1,945 MB after a 200 MB allocation). The counter is right if it can see the allocation in at
			// least one of a few attempts; it cannot invent 150 MB, so a counter that does not follow allocations fails all of them.
			var attempts = new List<string>();
			for (int attempt = 0; attempt < 5; attempt++)
			{
				Gc.Flush();
				long before = ProcessMemory.Read().FootprintBytes;
				byte[] block = new byte[200 * 1024 * 1024];
				for (int i = 0; i < block.Length; i += 4096)
				{
					block[i] = 1;
				}
				long after = ProcessMemory.Read().FootprintBytes;
				GC.KeepAlive(block);
				if (after - before > 150L * 1024 * 1024)
				{
					return;
				}
				attempts.Add($"before {before}, after {after}");
			}
			throw new Xunit.Sdk.XunitException("the footprint did not follow a 200 MB allocation in any attempt: " + string.Join("; ", attempts));
		}
	}
}
