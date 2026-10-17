// xUnit1031: these tests run on the thread pool without a synchronization context and block on purpose (a decode is held, a waiter is awaited).
#pragma warning disable xUnit1031
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ComicRack.Mac.Browser;
using ComicRack.Mac.Reader;
using ComicRack.Mac.Tests.Thumbnails;
using ComicRack.Mac.Thumbnails;
using SkiaSharp;
using Xunit;

namespace ComicRack.Mac.Tests.Reader
{
	/// <summary>The page cache and prefetcher with a decoder that blocks, counts and fails on command (no Core, no window).</summary>
	public class PageServiceTests : IDisposable
	{
		private readonly long liveBefore;
		private readonly long bytesBefore;
		private readonly List<PageService> services = new List<PageService>();

		public PageServiceTests()
		{
			Gc.Flush();
			liveBefore = PixelBlock.LiveCount;
			bytesBefore = PixelBlock.LiveBytes;
		}

		public void Dispose()
		{
			foreach (PageService s in services)
			{
				s.Dispose();
			}
			Gc.Flush();
			// Whatever a test did, no page memory may be left behind.
			Assert.Equal(liveBefore, PixelBlock.LiveCount);
			Assert.Equal(bytesBefore, PixelBlock.LiveBytes);
		}

		private PageService Make(FakePageDecoder decoder, int concurrency = 2, long maxBytes = 160L * 1024 * 1024, FakeUi ui = null)
		{
			var options = new PageServiceOptions
			{
				MaxConcurrency = concurrency,
				MaxCacheBytes = maxBytes,
				Post = ui != null ? (Action<Action>)ui.Post : (action => action()),
				DisposeWait = TimeSpan.FromSeconds(5),
			};
			var service = new PageService(decoder, options);
			services.Add(service);
			return service;
		}

		private static void WaitUntil(Func<bool> condition, string what, int timeoutMs = 10000)
		{
			var watch = Stopwatch.StartNew();
			while (!condition())
			{
				if (watch.ElapsedMilliseconds > timeoutMs)
				{
					throw new TimeoutException("Not reached: " + what);
				}
				Thread.Sleep(1);
			}
		}

		[Fact]
		public void The_window_is_decoded_in_order_and_nothing_else()
		{
			var decoder = new FakePageDecoder();
			PageService service = Make(decoder, concurrency: 1);
			service.SetWindow(new[] { 5, 6, 7, 4 });
			WaitUntil(() => service.GetStats().CachedCount == 4, "window decoded");
			Assert.Equal(new[] { 5, 6, 7, 4 }, decoder.Order);
			Assert.Equal(4, decoder.Started);
		}

		[Fact]
		public void A_cached_page_is_returned_at_once_without_decoding_and_is_the_same_picture_each_time()
		{
			var decoder = new FakePageDecoder();
			PageService service = Make(decoder);
			service.SetWindow(new[] { 2 });
			WaitUntil(() => service.GetStats().CachedCount == 1, "page 2 decoded");

			CoverImage a = service.TryGetCached(2);
			CoverImage b = service.TryGetCached(2);
			Assert.NotNull(a);
			Assert.Same(a, b); // one SKImage per cached page: Skia keeps its texture while the page stays cached
			Assert.Equal(100, a.Width);
			Assert.Equal(150, a.Height);
			Assert.Null(service.TryGetCached(3));
			Task<PageResult> task = service.RequestAsync(2, CancellationToken.None);
			Assert.True(task.IsCompletedSuccessfully);
			Assert.True(task.Result.FromCache);
			Assert.Same(a, task.Result.Image);
			task.Result.Dispose();
			a.Release();
			b.Release();
			Assert.Equal(1, decoder.Started);
			PageServiceStats stats = service.GetStats();
			Assert.Equal(1, stats.CacheHits);
		}

		[Fact]
		public async Task A_page_that_is_requested_but_not_cached_is_decoded_and_delivered_with_the_right_pixels()
		{
			var decoder = new FakePageDecoder();
			PageService service = Make(decoder);
			using (PageResult result = await service.RequestAsync(3, CancellationToken.None))
			{
				Assert.Null(result.Error);
				Assert.False(result.FromCache);
				Assert.Equal(3, result.Page);
				Assert.True(PageImages.Near(PageImages.PixelOf(result.Image.Image, 50, 75), FakePageDecoder.ColorFor(3)));
			}
		}

		[Fact]
		public void A_page_that_somebody_waits_for_does_not_queue_behind_the_prefetch()
		{
			var decoder = new FakePageDecoder();
			decoder.Hold();
			PageService service = Make(decoder, concurrency: 1);
			service.SetWindow(new[] { 0, 1, 2 });
			decoder.WaitForStarted(1);
			Assert.Equal(new[] { 0 }, decoder.Order); // the limit for prefetch is 1

			Task<PageResult> request = service.RequestAsync(9, CancellationToken.None);
			decoder.WaitForStarted(2); // the page somebody waits for gets one more slot
			Assert.Equal(new[] { 0, 9 }, decoder.Order);
			Assert.Equal(2, decoder.InFlight);

			decoder.Open();
			request.Result.Dispose();
		}

		[Fact]
		public void Moving_the_window_cancels_running_decodes_and_drops_queued_ones_of_the_pages_that_were_left()
		{
			var decoder = new FakePageDecoder();
			decoder.Hold();
			PageService service = Make(decoder, concurrency: 2);
			service.SetWindow(new[] { 0, 1, 2, 3 }); // 0 and 1 run, 2 and 3 wait
			decoder.WaitForStarted(2);

			service.SetWindow(new[] { 10, 11 });
			WaitUntil(() => decoder.Cancelled == 2, "both running decodes cancelled");
			PageServiceStats stats = service.GetStats();
			Assert.Equal(2, stats.DroppedFromQueue); // pages 2 and 3 never started
			Assert.DoesNotContain(2, decoder.Order);
			Assert.DoesNotContain(3, decoder.Order);

			decoder.Open();
			WaitUntil(() => service.GetStats().CachedCount == 2, "the new window decoded");
			Assert.Equal(new[] { 0, 1, 10, 11 }, decoder.Order);
			stats = service.GetStats();
			Assert.Equal(2, stats.DecodesCancelled);
			Assert.Equal(2, stats.DecodesCompleted);
			Assert.Null(service.TryGetCached(0)); // a cancelled decode caches nothing
			CoverImage ten = service.TryGetCached(10);
			Assert.NotNull(ten);
			ten.Release();
		}

		[Fact]
		public void A_page_that_stays_in_the_window_keeps_its_decode_when_the_window_moves()
		{
			var decoder = new FakePageDecoder();
			decoder.Hold();
			PageService service = Make(decoder, concurrency: 1);
			service.SetWindow(new[] { 4, 5, 6 });
			decoder.WaitForStarted(1);
			service.SetWindow(new[] { 5, 4, 6, 3 }); // 4 is still wanted: its decode is not cancelled
			Thread.Sleep(30);
			Assert.Equal(0, decoder.Cancelled);
			decoder.Open();
			WaitUntil(() => service.GetStats().CachedCount == 4, "all four decoded");
			Assert.Equal(0, decoder.Cancelled);
			Assert.Equal(1, decoder.Order.Count(p => p == 4));
		}

		[Fact]
		public async Task A_decode_somebody_waits_for_survives_a_window_change_until_the_last_waiter_leaves()
		{
			var decoder = new FakePageDecoder();
			decoder.Hold();
			PageService service = Make(decoder);
			using (var first = new CancellationTokenSource())
			using (var second = new CancellationTokenSource())
			{
				Task<PageResult> a = service.RequestAsync(7, first.Token);
				Task<PageResult> b = service.RequestAsync(7, second.Token);
				decoder.WaitForStarted(1);
				service.SetWindow(new[] { 0 }); // page 7 is out of the window, but it has waiters
				first.Cancel();
				await Assert.ThrowsAnyAsync<OperationCanceledException>(() => a);
				Thread.Sleep(30);
				Assert.Equal(0, decoder.Cancelled); // the second waiter still wants it
				second.Cancel();
				await Assert.ThrowsAnyAsync<OperationCanceledException>(() => b);
				WaitUntil(() => decoder.Cancelled == 1, "decode cancelled after the last waiter left");
			}
			decoder.Open();
		}

		[Fact]
		public async Task A_new_request_after_a_job_was_abandoned_starts_a_new_one_even_if_the_old_decode_ignores_cancellation()
		{
			var decoder = new FakePageDecoder { IgnoreCancellation = true };
			decoder.Hold();
			PageService service = Make(decoder, concurrency: 2);
			using (var cts = new CancellationTokenSource())
			{
				Task<PageResult> old = service.RequestAsync(4, cts.Token);
				decoder.WaitForStarted(1);
				cts.Cancel();
				await Assert.ThrowsAnyAsync<OperationCanceledException>(() => old);

				Task<PageResult> fresh = service.RequestAsync(4, CancellationToken.None);
				decoder.WaitForStarted(2); // a second decode of page 4, not a join with the dying one
				decoder.Open();
				using (PageResult r = await fresh)
				{
					Assert.Null(r.Error);
					Assert.NotNull(r.Image);
				}
			}
			WaitUntil(() => decoder.Finished == 2 && service.GetStats().InFlight == 0, "both decodes ended");
			Assert.Equal(1, service.GetStats().CachedCount); // the abandoned decode's picture was thrown away
		}

		[Fact]
		public async Task A_token_that_is_cancelled_before_the_request_gives_a_cancelled_task_and_starts_nothing()
		{
			var decoder = new FakePageDecoder();
			PageService service = Make(decoder);
			using (var cts = new CancellationTokenSource())
			{
				cts.Cancel();
				await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.RequestAsync(1, cts.Token));
			}
			Thread.Sleep(20);
			Assert.Equal(0, decoder.Started);
		}

		[Fact]
		public void Pages_of_the_window_are_never_evicted_the_others_go_least_recently_used_first_over_the_bound()
		{
			var decoder = new FakePageDecoder { Size = page => (100, 100) }; // 40,000 bytes a page
			PageService service = Make(decoder, maxBytes: 140_000); // room for three pages; the window itself is exempt
			service.SetWindow(new[] { 0, 1, 2, 3 });
			WaitUntil(() => service.GetStats().CachedCount == 4, "first window");
			CoverImage touch = service.TryGetCached(0); // page 0 is the most recently used of the four
			touch.Release();

			service.SetWindow(new[] { 8, 9 }); // 0..3 leave the window; 8 and 9 arrive
			WaitUntil(() => service.TryGetCachedOrNull(8) && service.TryGetCachedOrNull(9), "second window decoded");
			PageServiceStats stats = service.GetStats();
			Assert.True(stats.CachedBytes <= 140_000, "cache is within its bound: " + stats.CachedBytes);
			Assert.True(service.TryGetCachedOrNull(0), "the most recently used page outside the window stays");
			Assert.False(service.TryGetCachedOrNull(1));
			Assert.False(service.TryGetCachedOrNull(2));
			Assert.False(service.TryGetCachedOrNull(3));
			Assert.Equal(3, stats.CachedCount);
		}

		[Fact]
		public void The_number_of_decodes_at_once_never_exceeds_the_limit_plus_the_one_slot_for_a_waited_for_page()
		{
			var decoder = new FakePageDecoder { Delay = TimeSpan.FromMilliseconds(15) };
			PageService service = Make(decoder, concurrency: 2);
			service.SetWindow(Enumerable.Range(0, 8).ToArray());
			Task<PageResult> urgent = service.RequestAsync(9, CancellationToken.None);
			WaitUntil(() => service.GetStats().CachedCount == 9, "all decoded");
			urgent.Result.Dispose();
			Assert.True(decoder.MaxInFlight <= 3, "max in flight " + decoder.MaxInFlight);
			Assert.True(decoder.MaxInFlight >= 2);
		}

		[Fact]
		public async Task A_failure_goes_to_the_waiter_is_not_cached_and_the_next_request_tries_again()
		{
			bool fail = true;
			var decoder = new FakePageDecoder { FailWith = page => fail ? new InvalidOperationException("broken page " + page) : null };
			var logged = new List<string>();
			var options = new PageServiceOptions { Post = a => a(), Log = logged.Add };
			var service = new PageService(decoder, options);
			services.Add(service);

			using (PageResult first = await service.RequestAsync(2, CancellationToken.None))
			{
				Assert.Null(first.Image);
				Assert.IsType<InvalidOperationException>(first.Error);
				Assert.Equal("broken page 2", first.Error.Message);
			}
			Assert.Equal(1, service.GetStats().Failures);
			Assert.Equal(0, service.GetStats().CachedCount);
			Assert.Single(logged);

			fail = false;
			using (PageResult second = await service.RequestAsync(2, CancellationToken.None))
			{
				Assert.Null(second.Error);
				Assert.NotNull(second.Image);
			}
			Assert.Equal(2, decoder.Started);
		}

		[Fact]
		public async Task A_decoder_that_returns_nothing_is_a_failure()
		{
			var decoder = new FakePageDecoder { ReturnNull = true };
			PageService service = Make(decoder);
			using (PageResult r = await service.RequestAsync(0, CancellationToken.None))
			{
				Assert.NotNull(r.Error);
				Assert.Null(r.Image);
			}
		}

		[Fact]
		public async Task A_failed_prefetch_is_counted_and_does_not_fault_anything()
		{
			var decoder = new FakePageDecoder { FailWith = page => page == 1 ? new InvalidOperationException("no") : null };
			PageService service = Make(decoder);
			service.SetWindow(new[] { 0, 1, 2 });
			WaitUntil(() => decoder.Finished == 3, "three decodes ended");
			WaitUntil(() => service.GetStats().CachedCount == 2, "two pages cached");
			Assert.Equal(1, service.GetStats().Failures);
			using (PageResult r = await service.RequestAsync(1, CancellationToken.None))
			{
				Assert.NotNull(r.Error); // asking for it again retries, and fails again
			}
		}

		[Fact]
		public void Results_reach_the_waiter_only_through_post()
		{
			var decoder = new FakePageDecoder();
			var ui = new FakeUi();
			PageService service = Make(decoder, ui: ui);
			Task<PageResult> task = service.RequestAsync(1, CancellationToken.None);
			WaitUntil(() => service.GetStats().CachedCount == 1, "decoded");
			Thread.Sleep(20);
			Assert.False(task.IsCompleted); // decoded, but the completion waits in the UI thread's queue
			ui.PumpUntilCompleted(task);
			Assert.True(task.IsCompletedSuccessfully);
			task.Result.Dispose();
		}

		[Fact]
		public void A_caller_that_cancels_gets_a_cancelled_task_while_the_decode_of_a_wanted_page_goes_on()
		{
			var decoder = new FakePageDecoder();
			decoder.Hold();
			var ui = new FakeUi();
			PageService service = Make(decoder, ui: ui);
			using (var cts = new CancellationTokenSource())
			{
				Task<PageResult> task = service.RequestAsync(1, cts.Token);
				decoder.WaitForStarted(1);
				service.SetWindow(new[] { 1 }); // page 1 stays wanted, so its decode finishes and is cached
				cts.Cancel();
				decoder.Open();
				WaitUntil(() => service.GetStats().CachedCount == 1, "decoded");
				ui.PumpUntil(() => task.IsCompleted);
				Assert.True(task.IsCanceled);
				CoverImage cached = service.TryGetCached(1);
				Assert.NotNull(cached); // the page was in the window, so its decode was kept
				cached.Release();
			}
			// The Dispose of this test class checks that no memory is left once the service is gone.
		}

		[Fact]
		public async Task A_picture_that_was_handed_out_survives_eviction_and_disposal_of_the_service_and_frees_with_its_last_reference()
		{
			var decoder = new FakePageDecoder();
			PageService service = Make(decoder, maxBytes: 0);
			CoverImage held;
			using (PageResult r = await service.RequestAsync(5, CancellationToken.None))
			{
				held = r.Image.Retain(); // what a frame in flight does
			}
			service.SetWindow(new[] { 0 }); // page 5 leaves the window: evicted at once (the bound is 0)
			WaitUntil(() => service.TryGetCachedOrNull(5) == false, "page 5 evicted");
			Assert.True(PixelBlock.LiveCount > liveBefore, "the held picture keeps its pixels");
			service.Dispose();
			Assert.True(PixelBlock.LiveCount > liveBefore);

			// Still drawable: draw it into a bitmap and look at a pixel.
			using (var target = new SKBitmap(new SKImageInfo(10, 10, SKColorType.Bgra8888, SKAlphaType.Opaque)))
			{
				using (var canvas = new SKCanvas(target))
				{
					canvas.DrawImage(held.Image, new SKRect(0, 0, 10, 10), new SKSamplingOptions(SKFilterMode.Nearest));
				}
				Assert.True(PageImages.Near(target.GetPixel(5, 5), FakePageDecoder.ColorFor(5)));
			}
			held.Release();
			Gc.Flush();
			WaitUntil(() => PixelBlock.LiveCount == liveBefore, "pixels freed with the last reference");
		}

		[Fact]
		public async Task Dispose_cancels_waiters_and_decodes_waits_for_them_frees_everything_and_refuses_new_work()
		{
			var decoder = new FakePageDecoder();
			decoder.Hold();
			PageService service = Make(decoder);
			Task<PageResult> waiting = service.RequestAsync(1, CancellationToken.None);
			service.SetWindow(new[] { 1, 2, 3 });
			decoder.WaitForStarted(2);

			service.Dispose(); // cancels the decodes (they honour it) and returns when they have ended
			Assert.Equal(0, service.GetStats().InFlight);
			Assert.Equal(0, decoder.InFlight);
			await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
			Assert.Throws<ObjectDisposedException>(() => service.TryGetCached(1));
			Assert.Throws<ObjectDisposedException>(() => service.SetWindow(new[] { 1 }));
			Assert.IsType<ObjectDisposedException>(Record.Exception(() => { Task<PageResult> unused = service.RequestAsync(1, CancellationToken.None); }));
			service.Dispose(); // twice is fine
		}

		[Fact]
		public async Task The_decode_time_is_reported()
		{
			var decoder = new FakePageDecoder { Delay = TimeSpan.FromMilliseconds(40) };
			PageService service = Make(decoder);
			using (await service.RequestAsync(0, CancellationToken.None))
			{
			}
			double ms = service.GetStats().LastDecodeMilliseconds;
			Assert.InRange(ms, 30, 2000);
		}

		[Fact]
		public async Task Many_cancelled_requests_leave_no_work_and_no_memory_behind()
		{
			var decoder = new FakePageDecoder { Delay = TimeSpan.FromMilliseconds(5) };
			PageService service = Make(decoder, concurrency: 2);
			// A user who holds the arrow key: every step asks for a page and moves the window, the previous request is cancelled.
			CancellationTokenSource previous = null;
			var tasks = new List<Task<PageResult>>();
			for (int page = 0; page < 60; page++)
			{
				previous?.Cancel();
				previous = new CancellationTokenSource();
				service.SetWindow(new[] { page, page + 1, page + 2, page - 1 });
				tasks.Add(service.RequestAsync(page, previous.Token));
				await Task.Delay(1);
			}
			PageResult last = await tasks[tasks.Count - 1];
			last.Dispose();
			foreach (Task<PageResult> t in tasks.Take(tasks.Count - 1))
			{
				try
				{
					(await t).Dispose();
				}
				catch (OperationCanceledException)
				{
				}
			}
			PageServiceStats stats = service.GetStats();
			// Far fewer pages were decoded than were passed over.
			Assert.True(decoder.Started < 60 * 2, "decodes started: " + decoder.Started);
			Assert.True(stats.DecodesCancelled + stats.DroppedFromQueue > 0);
			Assert.True(stats.CachedCount <= 5);
		}
	}

	internal static class PageServiceTestExtensions
	{
		/// <summary>True if the page is cached (takes and gives back a reference).</summary>
		public static bool TryGetCachedOrNull(this PageService service, int page)
		{
			CoverImage image = service.TryGetCached(page);
			if (image == null)
			{
				return false;
			}
			image.Release();
			return true;
		}
	}
}
