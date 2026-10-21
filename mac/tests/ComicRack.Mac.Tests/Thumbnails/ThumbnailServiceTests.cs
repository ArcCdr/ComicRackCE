#pragma warning disable xUnit1031 // The tests pump a fake UI thread first, then read tasks that are already completed (or use a bounded wait).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using ComicRack.Mac.Thumbnails;
using SkiaSharp;
using Xunit;

namespace ComicRack.Mac.Tests.Thumbnails
{
	/// <summary>
	/// The scheduling, cache and delivery rules of <see cref="ThumbnailService"/>, with a decoder the test controls and a UI thread
	/// the test pumps. The real decoder is in <see cref="CoreThumbnailDecoderTests"/>.
	/// </summary>
	public class ThumbnailServiceTests
	{
		private const long Entry = 40_000; // one 100 x 100 BGRA bitmap of the fake decoder

		/// <summary>A service on a fake decoder and a fake UI thread. Disposing it opens the gate, closes the service and frees the images it collected.</summary>
		private sealed class Rig : IDisposable
		{
			private readonly List<SKImage> images = new List<SKImage>();
			public readonly FakeDecoder Decoder = new FakeDecoder();
			public readonly FakeUi Ui = new FakeUi();
			public readonly ManualTime Time = new ManualTime();
			public readonly List<string> Log = new List<string>();
			public readonly ThumbnailService Service;

			public Rig(long maxBytes = 1L << 30, int concurrency = 4, TimeSpan? retry = null, TimeSpan? disposeWait = null, Action<Action> run = null)
			{
				Service = new ThumbnailService(new ThumbnailServiceOptions
				{
					Decoder = Decoder,
					Post = Ui.Post,
					Run = run ?? (work => Task.Run(work)),
					MaxCacheBytes = maxBytes,
					MaxConcurrency = concurrency,
					FailureRetryAfter = retry ?? TimeSpan.FromSeconds(30),
					Time = Time,
					Log = message => { lock (Log) { Log.Add(message); } },
					DisposeWait = disposeWait ?? TimeSpan.FromSeconds(10),
				});
			}

			public Task<ThumbnailResult> Ask(string name, int size = 200, CancellationToken ct = default) => Service.RequestResult(Books.Make(name), size, ct);

			public ThumbnailResult Done(Task<ThumbnailResult> task)
			{
				Ui.PumpUntilCompleted(task);
				ThumbnailResult result = task.GetAwaiter().GetResult();
				images.Add(result.Image);
				return result;
			}

			public ThumbnailResult Own(ThumbnailResult result)
			{
				images.Add(result.Image);
				return result;
			}

			/// <summary>Drops the rig's references to the images it collected (for the test that checks the garbage collector).</summary>
			public void ForgetImages() => images.Clear();

			public void Dispose()
			{
				Decoder.Open();
				Service.Dispose();
				Ui.Pump();
				foreach (SKImage image in images)
				{
					image.Dispose();
				}
			}
		}

		private static string Loc(string name) => "/virtual/" + name + ".cbz";

		private static SKColor PixelOf(SKImage image, int x = 1, int y = 1)
		{
			using (SKPixmap pixmap = image.PeekPixels())
			{
				Assert.NotNull(pixmap);
				return pixmap.GetPixelColor(x, y);
			}
		}

		// ---- Sizes ---------------------------------------------------------------------------------------------------

		[Fact]
		public void Heights_snap_to_the_chain_the_core_uses_for_its_thumbnails()
		{
			Assert.Equal(512, ThumbnailSteps.Snap(512));
			Assert.Equal(512, ThumbnailSteps.Snap(2000));
			Assert.Equal(512, ThumbnailSteps.Snap(385));
			Assert.Equal(384, ThumbnailSteps.Snap(384));
			Assert.Equal(384, ThumbnailSteps.Snap(300));
			Assert.Equal(384, ThumbnailSteps.Snap(289));
			Assert.Equal(288, ThumbnailSteps.Snap(288));
			Assert.Equal(216, ThumbnailSteps.Snap(200));
			Assert.Equal(121, ThumbnailSteps.Snap(100));
			Assert.Equal(38, ThumbnailSteps.Snap(1));
			// The same rule as ThumbnailImage.GetThumbnail(height), written out: smallest step of 512 * 0.75^n that is not below the request.
			for (int h = ThumbnailSteps.MinHeight; h <= ThumbnailSteps.MaxHeight; h++)
			{
				float num = 512f;
				while (num * 0.75f >= h)
				{
					num *= 0.75f;
				}
				Assert.Equal((int)num, ThumbnailSteps.Snap(h));
				Assert.True(ThumbnailSteps.Snap(h) >= h);
			}
		}

		[Fact]
		public void The_defaults_are_256_MB_and_four_decodes()
		{
			var options = new ThumbnailServiceOptions();
			Assert.Equal(256L * 1024 * 1024, options.MaxCacheBytes);
			Assert.Equal(4, options.MaxConcurrency);
		}

		// ---- Delivery ------------------------------------------------------------------------------------------------

		[Fact]
		public async Task A_result_completes_on_the_ui_thread_and_only_when_it_runs()
		{
			using (var rig = new Rig())
			{
				Task<ThumbnailResult> task = rig.Ask("a");
				rig.Decoder.WaitForFinished(1);
				await Task.Delay(50);
				Assert.False(task.IsCompleted); // decoded, but the delivery waits for the UI thread

				int uiThread = Environment.CurrentManagedThreadId;
				int completedOn = -1;
				Task marker = task.ContinueWith(t => completedOn = Environment.CurrentManagedThreadId, TaskContinuationOptions.ExecuteSynchronously);
				rig.Ui.PumpUntilCompleted(task);
				await marker;
				Assert.Equal(uiThread, completedOn);
				ThumbnailResult result = rig.Own(await task);
				Assert.False(result.IsPlaceholder);
				Assert.Equal(100, result.Image.Width);
				Assert.Equal(FakeDecoder.ColorFor(Loc("a")), PixelOf(result.Image));
			}
		}

		[Fact]
		public async Task Request_gives_the_image_and_a_hit_completes_without_the_ui_thread()
		{
			using (var rig = new Rig())
			{
				Task<SKImage> first = rig.Service.Request(Books.Make("a"), 200, CancellationToken.None);
				rig.Ui.PumpUntilCompleted(first);
				using (SKImage image = await first)
				{
					Assert.Equal(100, image.Width);
				}
				int posts = rig.Ui.PostCount;
				Task<ThumbnailResult> again = rig.Ask("a"); // memory cache hit
				Assert.True(again.IsCompletedSuccessfully);
				Assert.Equal(posts, rig.Ui.PostCount);
				rig.Own(await again).Image.Dispose();
				Assert.Equal(1, rig.Decoder.Started);
				Assert.Equal(1, rig.Service.GetStats().CacheHits);
			}
		}

		[Fact]
		public async Task Every_caller_gets_its_own_image_over_the_same_pixels_with_no_copy()
		{
			using (var rig = new Rig())
			{
				Task<ThumbnailResult> a = rig.Ask("a");
				rig.Ui.PumpUntilCompleted(a);
				ThumbnailResult first = rig.Own(await a);
				ThumbnailResult second = rig.Own(await rig.Ask("a"));
				Assert.NotSame(first.Image, second.Image);
				PixelBlock block = Assert.Single(rig.Decoder.Blocks);
				using (SKPixmap p1 = first.Image.PeekPixels())
				using (SKPixmap p2 = second.Image.PeekPixels())
				{
					Assert.Equal(block.PixelsAddress, p1.GetPixels());
					Assert.Equal(block.PixelsAddress, p2.GetPixels());
				}
				// Disposing one caller's image leaves the other intact.
				first.Image.Dispose();
				Assert.Equal(FakeDecoder.ColorFor(Loc("a")), PixelOf(second.Image));
			}
		}

		// ---- Cancellation --------------------------------------------------------------------------------------------

		[Fact]
		public void A_token_that_is_cancelled_before_the_call_gives_a_cancelled_task_and_no_work()
		{
			using (var rig = new Rig())
			{
				var cts = new CancellationTokenSource();
				cts.Cancel();
				Task<ThumbnailResult> task = rig.Ask("a", ct: cts.Token);
				Assert.True(task.IsCanceled);
				Assert.Equal(0, rig.Ui.PostCount);
				Assert.Equal(0, rig.Service.GetStats().Requests);
				Thread.Sleep(30);
				Assert.Equal(0, rig.Decoder.Started);
			}
		}

		[Fact]
		public void A_request_cancelled_while_it_waits_in_the_queue_never_decodes()
		{
			using (var rig = new Rig(concurrency: 2))
			{
				rig.Decoder.Hold();
				Task<ThumbnailResult> run1 = rig.Ask("run1");
				Task<ThumbnailResult> run2 = rig.Ask("run2");
				rig.Decoder.WaitForStarted(2);
				var cts = new CancellationTokenSource();
				Task<ThumbnailResult> keep = rig.Ask("keep");
				Task<ThumbnailResult> gone = rig.Ask("gone", ct: cts.Token);
				Assert.Equal(2, rig.Service.GetStats().Queued);

				cts.Cancel();
				rig.Ui.PumpUntilCompleted(gone);
				Assert.True(gone.IsCanceled);
				ThumbnailServiceStats mid = rig.Service.GetStats();
				Assert.Equal(1, mid.Queued);
				Assert.Equal(1, mid.DroppedFromQueue);

				rig.Decoder.Open();
				rig.Done(run1);
				rig.Done(run2);
				rig.Done(keep);
				Assert.DoesNotContain(Loc("gone"), rig.Decoder.Order);
				Assert.Equal(3, rig.Decoder.Started);
			}
		}

		[Fact]
		public void A_job_handed_to_a_worker_but_not_started_never_decodes_when_its_caller_leaves()
		{
			var handed = new List<Action>();
			using (var rig = new Rig(concurrency: 1, run: work => { lock (handed) { handed.Add(work); } }))
			{
				var cts = new CancellationTokenSource();
				Task<ThumbnailResult> gone = rig.Ask("gone", ct: cts.Token);
				Task<ThumbnailResult> next = rig.Ask("next");
				Assert.Single(handed); // "gone" left the queue and waits for a worker thread; "next" waits for the slot
				Assert.Equal(1, rig.Service.GetStats().InFlight);
				cts.Cancel();
				rig.Ui.PumpUntilCompleted(gone);
				Assert.True(gone.IsCanceled);

				handed[0](); // the worker thread starts now: it must notice and not decode ...
				Assert.Equal(0, rig.Decoder.Started);
				Assert.Equal(1, rig.Service.GetStats().DroppedFromQueue);
				Assert.Equal(2, handed.Count); // ... and the slot goes to the next job
				handed[1]();
				rig.Done(next);
				Assert.Equal(new[] { Loc("next") }, rig.Decoder.Order);
			}
		}

		[Fact]
		public void If_the_work_cannot_be_started_the_callers_get_the_placeholder_and_the_slot_is_free_again()
		{
			bool broken = true;
			using (var rig = new Rig(concurrency: 1, run: work => { if (broken) throw new InvalidOperationException("no threads"); Task.Run(work); }))
			{
				ThumbnailResult result = rig.Done(rig.Ask("a"));
				Assert.True(result.IsPlaceholder);
				Assert.IsType<InvalidOperationException>(result.Error);
				Assert.Equal(0, rig.Service.GetStats().InFlight);
				broken = false;
				rig.Time.Advance(TimeSpan.FromMinutes(1)); // the failure is remembered for 30 s
				Assert.False(rig.Done(rig.Ask("a")).IsPlaceholder);
			}
		}

		[Fact]
		public void Cancelling_in_the_middle_of_the_queue_keeps_the_order_of_the_others()
		{
			using (var rig = new Rig(concurrency: 1))
			{
				rig.Decoder.Hold();
				Task<ThumbnailResult> running = rig.Ask("running");
				rig.Decoder.WaitForStarted(1);
				var cts = new CancellationTokenSource();
				Task<ThumbnailResult> q1 = rig.Ask("q1");
				Task<ThumbnailResult> q2 = rig.Ask("q2", ct: cts.Token);
				Task<ThumbnailResult> q3 = rig.Ask("q3");
				cts.Cancel();
				rig.Ui.PumpUntilCompleted(q2);
				rig.Decoder.Open();
				rig.Done(running);
				rig.Done(q1);
				rig.Done(q3);
				Assert.Equal(new[] { Loc("running"), Loc("q3"), Loc("q1") }, rig.Decoder.Order);
				Assert.True(q2.IsCanceled);
			}
		}

		[Fact]
		public void A_running_decode_is_cancelled_when_its_only_caller_leaves_and_nothing_is_cached()
		{
			using (var rig = new Rig())
			{
				rig.Decoder.Hold();
				var cts = new CancellationTokenSource();
				Task<ThumbnailResult> task = rig.Ask("a", ct: cts.Token);
				rig.Decoder.WaitForStarted(1);
				cts.Cancel();
				rig.Decoder.WaitForFinished(1); // the decoder saw its token cancelled and gave up
				rig.Ui.PumpUntilCompleted(task);
				Assert.True(task.IsCanceled);
				ThumbnailServiceStats stats = rig.Service.GetStats();
				Assert.Equal(1, stats.DecodesCancelled);
				Assert.Equal(0, stats.CachedCount);
				Assert.Equal(0, stats.Failures);
				Assert.Equal(0, stats.InFlight);

				// Asking again starts a fresh decode and succeeds.
				rig.Decoder.Open();
				ThumbnailResult result = rig.Done(rig.Ask("a"));
				Assert.False(result.IsPlaceholder);
				Assert.Equal(2, rig.Decoder.Started);
			}
		}

		[Fact]
		public void A_decode_that_ignores_cancellation_still_fills_the_cache_and_a_new_request_does_not_wait_for_it()
		{
			using (var rig = new Rig())
			{
				rig.Decoder.IgnoreCancellation = true;
				rig.Decoder.Hold();
				var cts = new CancellationTokenSource();
				Task<ThumbnailResult> abandoned = rig.Ask("a", ct: cts.Token);
				rig.Decoder.WaitForStarted(1);
				cts.Cancel();
				rig.Ui.PumpUntilCompleted(abandoned);
				Assert.True(abandoned.IsCanceled);

				// The old decode is still running, but its caller is gone: a new request must not join it (it would be told "cancelled").
				Task<ThumbnailResult> again = rig.Ask("a");
				rig.Decoder.WaitForStarted(2);
				rig.Decoder.Open();
				ThumbnailResult result = rig.Done(again);
				Assert.False(result.IsPlaceholder);
				rig.Decoder.WaitForFinished(2);
				Assert.Equal(2, rig.Decoder.Started);
				Assert.Equal(1, rig.Service.GetStats().CachedCount); // both decodes ended up as one entry
			}
		}

		// ---- Concurrency and order -----------------------------------------------------------------------------------

		[Theory]
		[InlineData(4, 40)]
		[InlineData(2, 12)]
		[InlineData(1, 6)]
		public void Never_more_decodes_in_flight_than_the_limit(int limit, int books)
		{
			using (var rig = new Rig(concurrency: limit))
			{
				rig.Decoder.Delay = TimeSpan.FromMilliseconds(15);
				var tasks = Enumerable.Range(0, books).Select(i => rig.Ask("book" + i)).ToArray();
				rig.Ui.PumpUntilCompleted(tasks);
				foreach (Task<ThumbnailResult> t in tasks)
				{
					rig.Own(t.Result);
				}
				Assert.Equal(books, rig.Decoder.Started);
				Assert.Equal(limit, rig.Decoder.MaxInFlight); // the limit is reached ...
				Assert.Equal(limit, rig.Service.GetStats().PeakInFlight); // ... and is what the service itself counted
				Assert.Equal(0, rig.Service.GetStats().InFlight);
			}
		}

		[Fact]
		public void The_newest_request_is_decoded_first()
		{
			using (var rig = new Rig(concurrency: 1))
			{
				rig.Decoder.Hold();
				Task<ThumbnailResult> first = rig.Ask("first");
				rig.Decoder.WaitForStarted(1);
				var later = Enumerable.Range(1, 6).Select(i => rig.Ask("b" + i)).ToArray();
				rig.Decoder.Open();
				rig.Done(first);
				foreach (Task<ThumbnailResult> t in later)
				{
					rig.Done(t);
				}
				Assert.Equal(new[] { "first", "b6", "b5", "b4", "b3", "b2", "b1" }.Select(Loc), rig.Decoder.Order);
			}
		}

		[Fact]
		public void Asking_again_for_a_waiting_request_makes_it_the_newest_without_a_second_decode()
		{
			using (var rig = new Rig(concurrency: 1))
			{
				rig.Decoder.Hold();
				Task<ThumbnailResult> first = rig.Ask("first");
				rig.Decoder.WaitForStarted(1);
				Task<ThumbnailResult> b1 = rig.Ask("b1");
				Task<ThumbnailResult> b2 = rig.Ask("b2");
				Task<ThumbnailResult> b3 = rig.Ask("b3");
				Task<ThumbnailResult> b1Again = rig.Ask("b1"); // the user scrolled back to it
				rig.Decoder.Open();
				foreach (Task<ThumbnailResult> t in new[] { first, b1, b2, b3, b1Again })
				{
					rig.Done(t);
				}
				Assert.Equal(new[] { "first", "b1", "b3", "b2" }.Select(Loc), rig.Decoder.Order);
			}
		}

		[Fact]
		public void With_four_workers_the_four_newest_requests_start_first()
		{
			using (var rig = new Rig(concurrency: 4))
			{
				rig.Decoder.Hold();
				var running = Enumerable.Range(1, 4).Select(i => rig.Ask("a" + i)).ToArray();
				rig.Decoder.WaitForStarted(4);
				var queued = Enumerable.Range(1, 8).Select(i => rig.Ask("b" + i)).ToArray();
				rig.Decoder.Delay = TimeSpan.FromMilliseconds(150); // keeps the first batch after the gate busy while the next four start
				rig.Decoder.Open();
				rig.Ui.PumpUntilCompleted(running.Concat(queued).ToArray());
				foreach (Task<ThumbnailResult> t in running.Concat(queued))
				{
					rig.Own(t.Result);
				}
				string[] order = rig.Decoder.Order;
				Assert.Equal(12, order.Length);
				Assert.Equal(new[] { "b8", "b7", "b6", "b5" }.Select(Loc).OrderBy(s => s), order.Skip(4).Take(4).OrderBy(s => s));
				Assert.Equal(new[] { "b4", "b3", "b2", "b1" }.Select(Loc).OrderBy(s => s), order.Skip(8).OrderBy(s => s));
			}
		}

		// ---- De-duplication ------------------------------------------------------------------------------------------

		[Fact]
		public void Concurrent_requests_for_the_same_cover_share_one_decode()
		{
			using (var rig = new Rig())
			{
				rig.Decoder.Hold();
				var tasks = Enumerable.Range(0, 5).Select(_ => rig.Ask("same")).ToArray(); // five different ComicBook objects, one file
				rig.Decoder.WaitForStarted(1);
				Thread.Sleep(50);
				Assert.Equal(1, rig.Decoder.Started);
				rig.Decoder.Open();
				rig.Ui.PumpUntilCompleted(tasks);
				var results = tasks.Select(t => rig.Own(t.Result)).ToArray();
				Assert.Equal(1, rig.Decoder.Started);
				Assert.Equal(5, results.Select(r => r.Image).Distinct().Count());
				Assert.All(results, r => Assert.False(r.IsPlaceholder));
				Assert.Equal(4, rig.Service.GetStats().Coalesced);
			}
		}

		[Fact]
		public void One_caller_cancelling_does_not_cancel_the_decode_the_others_wait_for()
		{
			using (var rig = new Rig())
			{
				rig.Decoder.Hold();
				var ctsA = new CancellationTokenSource();
				var ctsB = new CancellationTokenSource();
				Task<ThumbnailResult> a = rig.Ask("x", ct: ctsA.Token);
				Task<ThumbnailResult> b = rig.Ask("x", ct: ctsB.Token);
				Task<ThumbnailResult> c = rig.Ask("x");
				rig.Decoder.WaitForStarted(1);

				ctsA.Cancel();
				rig.Ui.PumpUntilCompleted(a);
				Assert.True(a.IsCanceled);
				Thread.Sleep(50);
				Assert.Equal(0, rig.Decoder.Finished); // the decode goes on
				ctsB.Cancel();
				rig.Ui.PumpUntilCompleted(b);
				Assert.True(b.IsCanceled);
				Thread.Sleep(50);
				Assert.Equal(0, rig.Decoder.Finished); // still one caller left

				rig.Decoder.Open();
				ThumbnailResult result = rig.Done(c);
				Assert.False(result.IsPlaceholder);
				Assert.Equal(1, rig.Decoder.Started);
				Assert.Equal(0, rig.Service.GetStats().DecodesCancelled);
			}
		}

		[Fact]
		public void The_decode_is_cancelled_when_every_caller_has_left()
		{
			using (var rig = new Rig())
			{
				rig.Decoder.Hold();
				var ctsA = new CancellationTokenSource();
				var ctsB = new CancellationTokenSource();
				Task<ThumbnailResult> a = rig.Ask("y", ct: ctsA.Token);
				Task<ThumbnailResult> b = rig.Ask("y", ct: ctsB.Token);
				rig.Decoder.WaitForStarted(1);
				ctsA.Cancel();
				ctsB.Cancel();
				rig.Decoder.WaitForFinished(1);
				rig.Ui.PumpUntilCompleted(a, b);
				Assert.True(a.IsCanceled && b.IsCanceled);
				Assert.Equal(1, rig.Service.GetStats().DecodesCancelled);
			}
		}

		[Fact]
		public void A_result_that_arrives_after_the_caller_cancelled_is_not_delivered_and_is_not_leaked()
		{
			Gc.Flush();
			long live = Gc.Live;
			using (var rig = new Rig(maxBytes: Entry))
			{
				var cts = new CancellationTokenSource();
				Task<ThumbnailResult> task = rig.Ask("a", ct: cts.Token);
				rig.Decoder.WaitForFinished(1);
				Thread.Sleep(50); // decoded; the delivery waits in the UI queue
				cts.Cancel(); // the cell was recycled before the UI thread got to it
				rig.Ui.PumpUntilCompleted(task);
				Assert.True(task.IsCanceled);
				rig.Service.Clear();
				Assert.Equal(live, Gc.Live); // no image stayed behind holding the pixels
			}
		}

		[Fact]
		public void Different_steps_are_different_images_and_sizes_in_the_same_step_share_one()
		{
			using (var rig = new Rig())
			{
				var book = Books.Make("steps");
				Task<ThumbnailResult> small = rig.Service.RequestResult(book, 100, CancellationToken.None);
				Task<ThumbnailResult> big = rig.Service.RequestResult(book, 300, CancellationToken.None);
				Task<ThumbnailResult> big2 = rig.Service.RequestResult(book, 380, CancellationToken.None); // 289..384 are all the 384 step
				rig.Ui.PumpUntilCompleted(small, big, big2);
				rig.Own(small.Result);
				rig.Own(big.Result);
				rig.Own(big2.Result);
				Assert.Equal(2, rig.Decoder.Started);
				Assert.Equal(2, rig.Service.GetStats().CachedCount);
			}
		}

		// ---- Memory cache --------------------------------------------------------------------------------------------

		[Fact]
		public void The_cache_never_holds_more_bytes_than_its_bound_and_evicts_the_least_recently_used()
		{
			Gc.Flush();
			long live = Gc.Live;
			using (var rig = new Rig(maxBytes: 10 * Entry))
			{
				for (int i = 0; i < 25; i++)
				{
					rig.Done(rig.Ask("b" + i)).Image.Dispose();
					ThumbnailServiceStats s = rig.Service.GetStats();
					Assert.True(s.CachedBytes <= 10 * Entry, "bound exceeded after " + i);
				}
				ThumbnailServiceStats stats = rig.Service.GetStats();
				Assert.Equal(10, stats.CachedCount);
				Assert.Equal(10 * Entry, stats.CachedBytes);
				Assert.Equal(10, Gc.Live - live); // the evicted pixels are really freed
				for (int i = 0; i < 25; i++)
				{
					bool cached = rig.Service.TryGetCached(Books.Make("b" + i), 200, out ThumbnailResult hit);
					if (cached)
					{
						hit.Image.Dispose();
					}
					Assert.Equal(i >= 15, cached); // exactly the ten newest
				}
			}
		}

		[Fact]
		public void A_hit_protects_an_entry_from_eviction()
		{
			using (var rig = new Rig(maxBytes: 3 * Entry))
			{
				foreach (string name in new[] { "a", "b", "c" })
				{
					rig.Done(rig.Ask(name)).Image.Dispose();
				}
				rig.Done(rig.Ask("a")).Image.Dispose(); // a is the most recently used now: order b, c, a
				rig.Done(rig.Ask("d")).Image.Dispose(); // evicts b
				Assert.False(rig.Service.TryGetCached(Books.Make("b"), 200, out _));
				foreach (string name in new[] { "a", "c", "d" })
				{
					Assert.True(rig.Service.TryGetCached(Books.Make(name), 200, out ThumbnailResult hit), name);
					hit.Image.Dispose();
				}
				Assert.Equal(4, rig.Decoder.Started);
			}
		}

		[Fact]
		public void An_image_bigger_than_the_whole_cache_is_delivered_but_not_kept()
		{
			Gc.Flush();
			long live = Gc.Live;
			using (var rig = new Rig(maxBytes: Entry))
			{
				rig.Decoder.Size = r => (1000, 1000);
				ThumbnailResult result = rig.Done(rig.Ask("huge"));
				Assert.Equal(1000, result.Image.Width);
				Assert.Equal(0, rig.Service.GetStats().CachedCount);
				Assert.Equal(live + 1, Gc.Live); // only the caller's image holds it
				result.Image.Dispose();
				Assert.Equal(live, Gc.Live);
			}
		}

		[Fact]
		public void Evicting_an_image_that_is_still_drawn_does_not_free_its_pixels()
		{
			Gc.Flush();
			long live = Gc.Live;
			using (var rig = new Rig(maxBytes: 2 * Entry))
			{
				SKImage held = rig.Done(rig.Ask("held")).Image;
				for (int i = 1; i <= 3; i++)
				{
					rig.Done(rig.Ask("other" + i)).Image.Dispose();
				}
				Assert.False(rig.Service.TryGetCached(Books.Make("held"), 200, out _)); // evicted ...
				Assert.Equal(live + 3, Gc.Live); // ... but its pixels live on, next to the two cached ones

				using (var target = new SKBitmap(100, 100))
				using (var canvas = new SKCanvas(target))
				{
					canvas.DrawImage(held, 0, 0); // still drawable
					Assert.Equal(FakeDecoder.ColorFor(Loc("held")), target.GetPixel(50, 50));
				}

				rig.Service.Clear();
				Assert.Equal(live + 1, Gc.Live);
				held.Dispose(); // the last holder lets go
				Assert.Equal(live, Gc.Live);
			}
		}

		[Fact]
		public async Task An_image_that_is_never_disposed_is_freed_by_the_garbage_collector()
		{
			Gc.Flush();
			long live = Gc.Live;
			using (var rig = new Rig(maxBytes: 1 * Entry))
			{
				for (int i = 0; i < 6; i++)
				{
					RequestAndForget(rig, "leak" + i);
				}
				rig.Service.Clear(); // the cache gives its references back: only the forgotten images hold pixels now
				bool freed = false;
				for (int attempt = 0; attempt < 50 && !freed; attempt++)
				{
					Gc.Flush();
					freed = Gc.Live == live;
					if (!freed)
					{
						await Task.Delay(50);
					}
				}
				Assert.True(freed, "blocks still alive: " + (Gc.Live - live));
			}
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		private static void RequestAndForget(Rig rig, string name)
		{
			rig.Done(rig.Ask(name)); // keeps no reference to the image; Rig.Done remembers it, so drop that one too
			rig.ForgetImages();
		}

		// ---- Failures ------------------------------------------------------------------------------------------------

		[Fact]
		public async Task A_failing_decode_gives_a_placeholder_not_an_exception()
		{
			using (var rig = new Rig())
			{
				rig.Decoder.FailWith = r => new FileNotFoundException("gone", "/virtual/missing.cbz");
				ThumbnailResult result = rig.Done(rig.Ask("missing"));
				Assert.True(result.IsPlaceholder);
				Assert.IsType<FileNotFoundException>(result.Error);
				Assert.Equal(216, result.Image.Height); // the step of a 200 px request, like a real thumbnail
				Assert.Equal(144, result.Image.Width);
				Assert.Equal(SKAlphaType.Opaque, result.Image.AlphaType);
				using (var bitmap = new SKBitmap(result.Image.Width, result.Image.Height))
				using (var canvas = new SKCanvas(bitmap))
				{
					canvas.DrawImage(result.Image, 0, 0);
					Assert.NotEqual(bitmap.GetPixel(2, 2), bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2)); // not a flat colour: the cross is there
				}
				Assert.Equal(1, rig.Service.GetStats().Failures);

				// The plain Request returns the placeholder as well.
				Task<SKImage> plain = rig.Service.Request(Books.Make("missing2"), 200, CancellationToken.None);
				rig.Ui.PumpUntilCompleted(plain);
				(await plain).Dispose();
			}
		}

		[Fact]
		public void A_decoder_that_returns_nothing_or_cancels_by_itself_is_a_failure_too()
		{
			using (var rig = new Rig())
			{
				rig.Decoder.FailWith = r => r.Key.Location == Loc("odd") ? new OperationCanceledException("its own timeout") : new InvalidDataException("damaged");
				ThumbnailResult odd = rig.Done(rig.Ask("odd"));
				Assert.True(odd.IsPlaceholder);
				Assert.IsType<OperationCanceledException>(odd.Error);
				ThumbnailResult damaged = rig.Done(rig.Ask("damaged"));
				Assert.True(damaged.IsPlaceholder);
				Assert.IsType<InvalidDataException>(damaged.Error);
			}
		}

		[Fact]
		public void A_failure_is_remembered_for_a_while_and_then_tried_again()
		{
			using (var rig = new Rig(retry: TimeSpan.FromSeconds(30)))
			{
				bool broken = true;
				rig.Decoder.FailWith = r => broken ? new IOException("volume not mounted") : null;
				Assert.True(rig.Done(rig.Ask("book")).IsPlaceholder);
				Assert.Equal(1, rig.Decoder.Started);

				broken = false;
				Task<ThumbnailResult> again = rig.Ask("book"); // within the 30 seconds: no new attempt
				Assert.True(again.IsCompletedSuccessfully);
				Assert.True(rig.Own(again.Result).IsPlaceholder);
				Assert.Equal(1, rig.Decoder.Started);

				rig.Time.Advance(TimeSpan.FromSeconds(31));
				ThumbnailResult retried = rig.Done(rig.Ask("book"));
				Assert.False(retried.IsPlaceholder);
				Assert.Equal(2, rig.Decoder.Started);
				Assert.Equal(FakeDecoder.ColorFor(Loc("book")), PixelOf(retried.Image));
			}
		}

		[Fact]
		public void Failures_are_logged_a_few_times_and_then_only_counted()
		{
			using (var rig = new Rig())
			{
				rig.Decoder.FailWith = r => new FileNotFoundException("gone");
				var tasks = Enumerable.Range(0, 30).Select(i => rig.Ask("missing" + i)).ToArray();
				rig.Ui.PumpUntilCompleted(tasks);
				foreach (Task<ThumbnailResult> t in tasks)
				{
					Assert.True(rig.Own(t.Result).IsPlaceholder);
				}
				Assert.Equal(30, rig.Service.GetStats().Failures);
				lock (rig.Log)
				{
					Assert.Equal(20, rig.Log.Count);
					Assert.Contains("/virtual/missing", rig.Log[0]);
					Assert.Contains("FileNotFoundException", rig.Log[0]);
				}
			}
		}

		// ---- Shutdown ------------------------------------------------------------------------------------------------

		[Fact]
		public void Dispose_cancels_what_waits_and_what_runs_and_frees_the_pixels()
		{
			Gc.Flush();
			long live = Gc.Live;
			var rig = new Rig(concurrency: 1);
			try
			{
				rig.Done(rig.Ask("cached")).Image.Dispose();
				rig.Decoder.Hold();
				Task<ThumbnailResult> running = rig.Ask("running");
				rig.Decoder.WaitForStarted(2);
				Task<ThumbnailResult> queued = rig.Ask("queued");

				rig.Service.Dispose(); // the running decode sees its token cancelled, so this returns quickly
				rig.Ui.PumpUntilCompleted(running, queued);
				Assert.True(running.IsCanceled);
				Assert.True(queued.IsCanceled);
				Assert.Equal(2, rig.Decoder.Started); // "queued" never decoded
				Assert.True(rig.Service.IsDisposed);
				Assert.IsType<ObjectDisposedException>(Record.Exception(() => { rig.Ask("late"); }));
				Assert.Equal(live, Gc.Live);
			}
			finally
			{
				rig.Dispose();
			}
		}

		[Fact]
		public void Dispose_does_not_wait_longer_than_asked_for_a_decode_that_cannot_be_interrupted()
		{
			Gc.Flush();
			long live = Gc.Live;
			var rig = new Rig(disposeWait: TimeSpan.FromMilliseconds(200));
			try
			{
				rig.Decoder.IgnoreCancellation = true;
				rig.Decoder.Hold();
				Task<ThumbnailResult> task = rig.Ask("stuck");
				rig.Decoder.WaitForStarted(1);
				var watch = System.Diagnostics.Stopwatch.StartNew();
				rig.Service.Dispose();
				Assert.True(watch.ElapsedMilliseconds < 3000, "Dispose waited " + watch.ElapsedMilliseconds + " ms");
				rig.Ui.PumpUntilCompleted(task);
				Assert.True(task.IsCanceled);

				rig.Decoder.Open(); // the decode finally ends; the service is closed, so the pixels go straight back
				rig.Decoder.WaitForFinished(1);
				for (int i = 0; i < 100 && Gc.Live != live; i++)
				{
					Thread.Sleep(10);
				}
				Assert.Equal(live, Gc.Live);
			}
			finally
			{
				rig.Dispose();
			}
		}

		[Fact]
		public void Arguments_are_checked()
		{
			using (var rig = new Rig())
			{
				Assert.IsType<ArgumentNullException>(Record.Exception(() => { rig.Service.RequestResult(null, 100, CancellationToken.None); }));
				Assert.IsType<ArgumentOutOfRangeException>(Record.Exception(() => { rig.Service.RequestResult(Books.Make("a"), 0, CancellationToken.None); }));
				Assert.Throws<ArgumentException>(() => new ThumbnailService(new ThumbnailServiceOptions()));
				Assert.Throws<ArgumentOutOfRangeException>(() => new ThumbnailService(new ThumbnailServiceOptions { Decoder = rig.Decoder, MaxConcurrency = 0 }));
				Assert.Throws<ArgumentOutOfRangeException>(() => new ThumbnailService(new ThumbnailServiceOptions { Decoder = rig.Decoder, MaxCacheBytes = 0 }));
			}
		}

		[Fact]
		public void Many_threads_asking_for_overlapping_covers_keep_every_rule()
		{
			Gc.Flush();
			long live = Gc.Live;
			using (var rig = new Rig(maxBytes: 20 * Entry, concurrency: 4))
			{
				rig.Decoder.Delay = TimeSpan.FromMilliseconds(2);
				var all = new List<Task<ThumbnailResult>>();
				object sync = new object();
				var workers = Enumerable.Range(0, 6).Select(w => Task.Run(() =>
				{
					var local = new Random(w);
					for (int i = 0; i < 200; i++)
					{
						var cts = new CancellationTokenSource();
						Task<ThumbnailResult> t = rig.Ask("b" + local.Next(0, 60), 100 + 100 * local.Next(0, 3), cts.Token);
						lock (sync)
						{
							all.Add(t);
						}
						if (local.Next(0, 3) == 0)
						{
							cts.Cancel();
						}
					}
				})).ToArray();
				Task.WaitAll(workers);
				rig.Ui.PumpUntil(() => all.TrueForAll(t => t.IsCompleted), 30000);
				foreach (Task<ThumbnailResult> t in all)
				{
					Assert.False(t.IsFaulted);
					if (t.IsCompletedSuccessfully)
					{
						Assert.False(t.Result.IsPlaceholder);
						rig.Own(t.Result);
					}
				}
				ThumbnailServiceStats stats = rig.Service.GetStats();
				Assert.True(rig.Decoder.MaxInFlight <= 4);
				Assert.True(stats.CachedBytes <= 20 * Entry);
				Assert.Equal(0, stats.InFlight);
				Assert.Equal(0, stats.Queued);
			}
			Gc.Flush();
			Assert.Equal(live, Gc.Live);
		}
	}
}
