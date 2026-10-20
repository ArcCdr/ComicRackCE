using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using cYo.Common.Threading;
using cYo.Projects.ComicRack.Engine;
using Xunit;

namespace ComicRack.Mac.Host.Tests
{
	/// <summary>
	/// The pass B replacements of the three Thread.Abort sites (P1.1: ThreadUtility.Abort, BackgroundRunner.Dispose,
	/// ComicScanner.Stop). A worker that ignores its stop request is reported and abandoned; nothing throws and the caller goes on.
	/// </summary>
	public class ThreadAbandonTests
	{
		private sealed class Capture : TraceListener
		{
			private readonly object sync = new object();
			private readonly System.Collections.Generic.List<string> warnings = new System.Collections.Generic.List<string>();

			public string[] Warnings
			{
				get
				{
					lock (sync)
					{
						return warnings.ToArray();
					}
				}
			}

			public override void Write(string message)
			{
			}

			public override void WriteLine(string message)
			{
			}

			public override void TraceEvent(TraceEventCache eventCache, string source, TraceEventType eventType, int id, string message)
			{
				if (eventType == TraceEventType.Warning)
				{
					lock (sync)
					{
						warnings.Add(message);
					}
				}
			}
		}

		private static void WaitUntil(Func<bool> condition, int timeoutMs, string what)
		{
			var end = DateTime.UtcNow.AddMilliseconds(timeoutMs);
			while (!condition() && DateTime.UtcNow < end)
			{
				Thread.Sleep(10);
			}
			Assert.True(condition(), what);
		}

		[Fact]
		public void ThreadUtility_Abort_joins_a_thread_that_stops_in_time_and_returns_at_once_for_a_missing_or_finished_one()
		{
			ThreadUtility.Abort(null, 100);
			var done = new Thread(() => { }) { IsBackground = true };
			done.Start();
			done.Join();
			ThreadUtility.Abort(done, 100);

			using (var release = new ManualResetEventSlim())
			{
				var t = new Thread(() => release.Wait()) { IsBackground = true, Name = "abandon-test-stops" };
				t.Start();
				new Thread(() => { Thread.Sleep(100); release.Set(); }) { IsBackground = true }.Start();
				var watch = Stopwatch.StartNew();
				ThreadUtility.Abort(t, 10000);
				Assert.True(watch.ElapsedMilliseconds < 5000);
				Assert.False(t.IsAlive);
			}
		}

		[Fact]
		public void ThreadUtility_Abort_reports_and_abandons_a_thread_that_ignores_the_timeout()
		{
			var capture = new Capture();
			Trace.Listeners.Add(capture);
			using (var release = new ManualResetEventSlim())
			{
				var t = new Thread(() => release.Wait()) { IsBackground = true, Name = "abandon-test-ignores" };
				t.Start();
				try
				{
					var watch = Stopwatch.StartNew();
					ThreadUtility.Abort(t, 200);
					Assert.InRange(watch.ElapsedMilliseconds, 150, 5000);
					Assert.True(t.IsAlive, "the thread is abandoned, not killed");
					Assert.Contains(capture.Warnings, w => w.Contains("abandon-test-ignores") && w.Contains("abandoned"));
				}
				finally
				{
					Trace.Listeners.Remove(capture);
					release.Set();
					t.Join(5000);
				}
			}
		}

		[Fact]
		public void BackgroundRunner_Dispose_abandons_a_tick_that_does_not_return_and_the_thread_ends_quietly_later()
		{
			var capture = new Capture();
			Trace.Listeners.Add(capture);
			using (var release = new ManualResetEventSlim())
			using (var inTick = new ManualResetEventSlim())
			{
				var runner = new BackgroundRunner { Interval = 10 };
				runner.Tick += (s, e) =>
				{
					inTick.Set();
					release.Wait(); // ignores the stop request
				};
				try
				{
					runner.Start();
					Assert.True(inTick.Wait(10000), "the tick started");
					var watch = Stopwatch.StartNew();
					runner.Dispose(); // waits 5 s for the thread, then abandons it
					Assert.InRange(watch.ElapsedMilliseconds, 4500, 20000);
					Assert.Contains(capture.Warnings, w => w.Contains("BackgroundRunner Thread") && w.Contains("abandoned"));
					release.Set();
					WaitUntil(() => !ThreadUtility.ActiveThreads.Any(t => t.Name == "BackgroundRunner Thread"), 10000,
						"the abandoned thread ends when the tick returns (its wait handles were not disposed under it)");
				}
				finally
				{
					Trace.Listeners.Remove(capture);
					release.Set();
				}
			}
		}

		[Fact]
		public void MacHost_Dispose_completes_when_the_scanner_ignores_its_stop_flag_for_15_seconds()
		{
			using (var temp = new TempFolder())
			using (var release = new ManualResetEventSlim())
			using (var inHandler = new ManualResetEventSlim())
			{
				string books = Path.Combine(temp.Path, "books");
				Directory.CreateDirectory(books);
				File.WriteAllBytes(Path.Combine(books, "a.cbz"), new byte[] { 1, 2, 3, 4 });

				MacHost host = MacHost.Open(new MacHostOptions { Paths = temp.Paths });
				try
				{
					bool first = true;
					host.QueueManager.Scanner.ScanNotify += (s, e) =>
					{
						if (first)
						{
							first = false;
							inHandler.Set();
							release.Wait(15000); // a worker that does not look at the stop flag for 15 s
						}
						e.IgnoreFile = true;
					};
					host.DatabaseManager.Database.IsDirty = true;
					host.QueueManager.Scanner.ScanFileOrFolder(books, true, false);
					Assert.True(inHandler.Wait(10000), "the scan reached the handler");

					var watch = Stopwatch.StartNew();
					host.Dispose(); // ComicScanner.Stop: joins 10 s, then used to call Thread.Abort (PlatformNotSupportedException)
					Assert.InRange(watch.ElapsedMilliseconds, 9000, 14500);
					Assert.True(host.IsDisposed);
				}
				finally
				{
					release.Set();
					host.Dispose();
				}

				string log = File.ReadAllText(temp.Paths.LogFile);
				Assert.Contains("did not stop within 10000 ms and is abandoned", log);
				Assert.Contains("Host closed.", log);
				Assert.DoesNotContain("error(s)", log);
				Assert.True(File.Exists(temp.Paths.DatabaseFile), "the later steps ran: the database was saved");
				WaitUntil(() => !ThreadUtility.ActiveThreads.Any(t => t.Name == "Book Scanner"), 10000, "the abandoned scanner thread ends once the handler returns");
			}
		}
	}
}
