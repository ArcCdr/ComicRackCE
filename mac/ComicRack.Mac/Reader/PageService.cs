using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ComicRack.Mac.Browser;
using ComicRack.Mac.Thumbnails;

namespace ComicRack.Mac.Reader
{
	/// <summary>Settings of a <see cref="PageService"/>. The defaults are what the app uses; tests replace the executors.</summary>
	public sealed class PageServiceOptions
	{
		/// <summary>
		/// Bytes of decoded pages kept besides the ones in the current window (pages already read, for a quick step back). The pages of
		/// the window are never evicted, so the memory in use can exceed this by the size of the window (at most 4 pages).
		/// </summary>
		public long MaxCacheBytes { get; set; } = 160L * 1024 * 1024;

		/// <summary>
		/// Decodes that run at once for the pages of the window. A page somebody waits for gets one more slot, so it never queues
		/// behind prefetch. The Core reads one archive serially anyway (one lock per provider).
		/// </summary>
		public int MaxConcurrency { get; set; } = 2;

		/// <summary>Where completions go: the UI thread. Default <see cref="UiDispatch.Post(Action, Avalonia.Threading.DispatcherPriority)"/>.</summary>
		public Action<Action> Post { get; set; } = action => UiDispatch.Post(action);

		/// <summary>Where decodes run. Default: the thread pool. A test can hold a job here to observe the state "taken from the queue, not started".</summary>
		public Action<Action> Run { get; set; } = action => Task.Run(action);

		/// <summary>Receives one line per failure (the first few) and per notable event. Default: nothing.</summary>
		public Action<string> Log { get; set; }

		/// <summary>How long <see cref="PageService.Dispose"/> waits for decodes that cannot be interrupted.</summary>
		public TimeSpan DisposeWait { get; set; } = TimeSpan.FromSeconds(3);
	}

	/// <summary>The outcome of <see cref="PageService.RequestAsync"/>. Dispose it, or take <see cref="Image"/> and release that, exactly once.</summary>
	public sealed class PageResult : IDisposable
	{
		private CoverImage image;

		internal PageResult(int page, CoverImage image, Exception error, bool fromCache)
		{
			Page = page;
			this.image = image;
			Error = error;
			FromCache = fromCache;
		}

		public int Page { get; }

		/// <summary>The decoded page, with one reference for the receiver, or null if <see cref="Error"/> is set.</summary>
		public CoverImage Image => image;

		/// <summary>Why the page could not be produced, or null.</summary>
		public Exception Error { get; }

		/// <summary>True if the page was already decoded (no wait).</summary>
		public bool FromCache { get; }

		/// <summary>Hands the image to the caller (who must release it) and forgets it.</summary>
		public CoverImage Take()
		{
			CoverImage taken = image;
			image = null;
			return taken;
		}

		public void Dispose()
		{
			Take()?.Release();
		}
	}

	/// <summary>Counters of a <see cref="PageService"/> (diagnostics, tests, the overlay of M2.4).</summary>
	public sealed class PageServiceStats
	{
		public long Requests { get; set; }

		public long CacheHits { get; set; }

		public long DecodesStarted { get; set; }

		public long DecodesCompleted { get; set; }

		/// <summary>Decodes that were stopped (or whose result was thrown away) because the reader moved on.</summary>
		public long DecodesCancelled { get; set; }

		/// <summary>Jobs that left the queue before they started.</summary>
		public long DroppedFromQueue { get; set; }

		public long Failures { get; set; }

		public int InFlight { get; set; }

		public int PeakInFlight { get; set; }

		public int Queued { get; set; }

		public int CachedCount { get; set; }

		public long CachedBytes { get; set; }

		/// <summary>Time of the last decode that finished (the Core read plus the Skia decode), in milliseconds.</summary>
		public double LastDecodeMilliseconds { get; set; }
	}

	/// <summary>
	/// The reader's page cache and prefetcher. The reader tells it which pages matter (<see cref="SetWindow"/>: the page on screen
	/// first, then the next two and the previous one) and asks for the one it wants to draw (<see cref="TryGetCached"/>,
	/// <see cref="RequestAsync"/>).
	///
	/// <para><b>Stale work is cancelled.</b> A job nobody waits for and that is not in the window any more is dropped from the queue, or,
	/// if it already runs, its token is cancelled (the decoder checks it between the Core read and the Skia decode). A job somebody
	/// waits for keeps running until the last waiter leaves. A page requested again after its job was abandoned gets a new job, so it
	/// never joins a dying one.</para>
	///
	/// <para><b>Ownership.</b> The cache holds one reference of each page's <see cref="CoverImage"/> (which wraps one
	/// <c>SKImage</c> over the decoded <see cref="PixelBlock"/>); every receiver gets its own reference. Evicting a page therefore
	/// never frees pixels that a frame in flight still draws, and a receiver that is done releases its reference at any time. The same
	/// <c>SKImage</c> is handed out each time, so Skia keeps its GPU texture while the page stays cached (a new image per turn would
	/// upload the page again).</para>
	///
	/// <para>Completions are posted to the UI thread; only a cache hit and an already cancelled token complete at once. One lock guards
	/// the state; the decoder, <c>post</c> and every release run outside it.</para>
	/// </summary>
	public sealed class PageService : IDisposable
	{
		private const int LoggedFailureLimit = 10;

		private readonly object gate = new object();
		private readonly IPageDecoder decoder;
		private readonly Action<Action> post;
		private readonly Action<Action> run;
		private readonly Action<string> log;
		private readonly long maxBytes;
		private readonly int maxConcurrency;
		private readonly TimeSpan disposeWait;
		private readonly Dictionary<int, Entry> cache = new Dictionary<int, Entry>();
		private readonly Dictionary<int, Job> jobs = new Dictionary<int, Job>();
		private readonly ManualResetEventSlim idle = new ManualResetEventSlim(true);
		private List<int> window = new List<int>();
		private HashSet<int> windowSet = new HashSet<int>();
		private long cachedBytes;
		private long useCounter;
		private int running;
		private bool disposed;

		private long requests;
		private long cacheHits;
		private long decodesStarted;
		private long decodesCompleted;
		private long decodesCancelled;
		private long droppedFromQueue;
		private long failures;
		private int peakInFlight;
		private int loggedFailures;
		private long lastDecodeTicks;

		public PageService(IPageDecoder decoder, PageServiceOptions options = null)
		{
			this.decoder = decoder ?? throw new ArgumentNullException(nameof(decoder));
			options = options ?? new PageServiceOptions();
			post = options.Post ?? (action => UiDispatch.Post(action));
			run = options.Run ?? (action => Task.Run(action));
			log = options.Log;
			maxBytes = Math.Max(0, options.MaxCacheBytes);
			maxConcurrency = Math.Max(1, options.MaxConcurrency);
			disposeWait = options.DisposeWait;
		}

		/// <summary>
		/// Called with the page number and the size of every page that was decoded, from the decoding thread, before the page can be
		/// found in the cache (also for pages that turn out not to be needed). The reader uses it to learn which pages are wide before
		/// it decides how pages pair up. Set it before the first request.
		/// </summary>
		public Action<int, int, int> Decoded { get; set; }

		public bool IsDisposed
		{
			get
			{
				lock (gate)
				{
					return disposed;
				}
			}
		}

		/// <summary>
		/// The decoded page if it is cached: a new reference for the caller, who releases it. Never decodes and never waits. Null if the
		/// page is not decoded (yet).
		/// </summary>
		public CoverImage TryGetCached(int page)
		{
			lock (gate)
			{
				ThrowIfDisposed();
				if (cache.TryGetValue(page, out Entry entry))
				{
					entry.LastUse = ++useCounter;
					return entry.Image.Retain();
				}
				return null;
			}
		}

		/// <summary>
		/// The page, decoded: at once if cached, else when its job finishes (a job is made if none exists, and put in front of the
		/// prefetch). Cancel the token when the page is no longer wanted; the task then ends cancelled after the next dispatcher turn.
		/// A result that arrives after the caller cancelled may still be delivered (the caller must release it); one that does not
		/// get delivered is released here.
		/// </summary>
		/// <exception cref="ObjectDisposedException">The service was disposed.</exception>
		public Task<PageResult> RequestAsync(int page, CancellationToken cancellationToken)
		{
			if (cancellationToken.IsCancellationRequested)
			{
				return Task.FromCanceled<PageResult>(cancellationToken);
			}
			CoverImage hit = null;
			Waiter waiter = null;
			List<Job> start;
			lock (gate)
			{
				ThrowIfDisposed();
				requests++;
				if (cache.TryGetValue(page, out Entry entry))
				{
					entry.LastUse = ++useCounter;
					cacheHits++;
					hit = entry.Image.Retain();
					start = null;
				}
				else
				{
					if (!jobs.TryGetValue(page, out Job job))
					{
						job = new Job(page);
						jobs[page] = job;
					}
					waiter = new Waiter(cancellationToken, job);
					job.Waiters.Add(waiter);
					start = PumpLocked();
				}
			}
			if (hit != null)
			{
				return Task.FromResult(new PageResult(page, hit, null, fromCache: true));
			}
			waiter.Registration = cancellationToken.Register(() => OnWaiterCancelled(waiter));
			Launch(start);
			return waiter.Completion.Task;
		}

		/// <summary>
		/// Says which pages matter, most important first (the page on screen, then the next two, then the previous one): missing pages
		/// get a job in that order, jobs for other pages that nobody waits for are cancelled, and decoded pages outside the window
		/// are evicted, least recently used first, once the cache exceeds its bound. Cheap to call at every page turn.
		/// </summary>
		public void SetWindow(IReadOnlyList<int> pages)
		{
			if (pages == null)
			{
				throw new ArgumentNullException(nameof(pages));
			}
			var removed = new List<Entry>();
			var toCancel = new List<Job>();
			List<Job> start;
			lock (gate)
			{
				ThrowIfDisposed();
				window = pages.Where(p => p >= 0).Distinct().ToList();
				windowSet = new HashSet<int>(window);
				foreach (int page in window)
				{
					if (!cache.ContainsKey(page) && !jobs.ContainsKey(page))
					{
						jobs[page] = new Job(page);
					}
				}
				foreach (Job job in jobs.Values.ToList())
				{
					if (job.Waiters.Count == 0 && !windowSet.Contains(job.Page))
					{
						AbandonLocked(job, toCancel);
					}
				}
				TrimLocked(removed);
				start = PumpLocked();
			}
			CancelAll(toCancel);
			Free(removed);
			Launch(start);
		}

		public PageServiceStats GetStats()
		{
			lock (gate)
			{
				return new PageServiceStats
				{
					Requests = requests,
					CacheHits = cacheHits,
					DecodesStarted = Interlocked.Read(ref decodesStarted),
					DecodesCompleted = decodesCompleted,
					DecodesCancelled = decodesCancelled,
					DroppedFromQueue = droppedFromQueue,
					Failures = failures,
					InFlight = running,
					PeakInFlight = peakInFlight,
					Queued = jobs.Values.Count(j => !j.Running),
					CachedCount = cache.Count,
					CachedBytes = cachedBytes,
					LastDecodeMilliseconds = Interlocked.Read(ref lastDecodeTicks) * 1000.0 / Stopwatch.Frequency,
				};
			}
		}

		// Starts as many queued jobs as the limits allow, most important first. Returns the jobs to launch (outside the lock).
		private List<Job> PumpLocked()
		{
			List<Job> start = null;
			while (!disposed)
			{
				Job best = null;
				int bestPriority = int.MaxValue;
				foreach (Job job in jobs.Values)
				{
					if (job.Running || job.Abandoned)
					{
						continue;
					}
					int priority = PriorityLocked(job);
					if (best == null || priority < bestPriority)
					{
						best = job;
						bestPriority = priority;
					}
				}
				if (best == null)
				{
					break;
				}
				// A page that somebody waits for may use one slot more than the prefetch.
				int limit = best.Waiters.Count > 0 ? maxConcurrency + 1 : maxConcurrency;
				if (running >= limit)
				{
					break;
				}
				best.Running = true;
				running++;
				idle.Reset();
				peakInFlight = Math.Max(peakInFlight, running);
				(start ?? (start = new List<Job>())).Add(best);
			}
			return start;
		}

		// A page that somebody waits for goes first (-1); the rest in window order.
		private int PriorityLocked(Job job)
		{
			if (job.Waiters.Count > 0)
			{
				return -1;
			}
			int index = window.IndexOf(job.Page);
			return index < 0 ? int.MaxValue - 1 : index;
		}

		private void Launch(List<Job> start)
		{
			if (start == null)
			{
				return;
			}
			foreach (Job job in start)
			{
				try
				{
					run(() => RunJob(job));
				}
				catch (Exception ex)
				{
					Finish(job, null, ex, cancelled: false, TimeSpan.Zero);
				}
			}
		}

		private void RunJob(Job job)
		{
			PixelBlock block = null;
			Exception error = null;
			bool cancelled = false;
			var watch = Stopwatch.StartNew();
			if (job.Cts.IsCancellationRequested)
			{
				cancelled = true;
			}
			else
			{
				try
				{
					Interlocked.Increment(ref decodesStarted);
					block = decoder.Decode(job.Page, job.Cts.Token);
					if (block == null)
					{
						error = new InvalidOperationException("The decoder returned no pixels for page " + (job.Page + 1) + ".");
					}
				}
				catch (OperationCanceledException)
				{
					cancelled = true;
				}
				catch (Exception ex)
				{
					if (job.Cts.IsCancellationRequested)
					{
						cancelled = true;
					}
					else
					{
						error = ex;
					}
				}
			}
			watch.Stop();
			Finish(job, block, error, cancelled, watch.Elapsed);
		}

		private void Finish(Job job, PixelBlock block, Exception error, bool cancelled, TimeSpan elapsed)
		{
			CoverImage image = null;
			long bytes = 0;
			if (block != null)
			{
				try
				{
					bytes = block.ByteCount;
					image = new CoverImage(block.CreateImage());
					NotifyDecoded(job.Page, image);
				}
				catch (Exception ex)
				{
					error = ex;
				}
				finally
				{
					block.Release(); // the image holds its own reference now
				}
			}

			var deliveries = new List<(Waiter waiter, PageResult result)>();
			var cancelledWaiters = new List<Waiter>();
			var removed = new List<Entry>();
			List<Job> start;
			CoverImage discard = null;
			string failureLine = null;
			lock (gate)
			{
				running--;
				if (running == 0)
				{
					idle.Set();
				}
				if (jobs.TryGetValue(job.Page, out Job current) && ReferenceEquals(current, job))
				{
					jobs.Remove(job.Page);
				}
				Interlocked.Exchange(ref lastDecodeTicks, elapsed.Ticks * Stopwatch.Frequency / TimeSpan.TicksPerSecond);
				bool wanted = !disposed && !job.Abandoned && (job.Waiters.Count > 0 || windowSet.Contains(job.Page));
				if (image != null && wanted)
				{
					decodesCompleted++;
					if (cache.TryGetValue(job.Page, out Entry old))
					{
						cache.Remove(job.Page);
						cachedBytes -= old.Bytes;
						removed.Add(old);
					}
					cache[job.Page] = new Entry(job.Page, image, bytes) { LastUse = ++useCounter };
					cachedBytes += bytes;
					foreach (Waiter w in job.Waiters)
					{
						if (w.TryClaim())
						{
							deliveries.Add((w, new PageResult(job.Page, image.Retain(), null, fromCache: false)));
						}
					}
					TrimLocked(removed);
				}
				else if (image != null)
				{
					decodesCancelled++; // decoded for nothing: the reader moved on while it ran
					discard = image;
				}
				else if (error != null && !job.Abandoned && !disposed)
				{
					failures++;
					foreach (Waiter w in job.Waiters)
					{
						if (w.TryClaim())
						{
							deliveries.Add((w, new PageResult(job.Page, null, error, fromCache: false)));
						}
					}
					if (loggedFailures < LoggedFailureLimit)
					{
						loggedFailures++;
						failureLine = "Page " + (job.Page + 1) + " failed: " + error.Message;
					}
				}
				else
				{
					decodesCancelled++;
					foreach (Waiter w in job.Waiters)
					{
						if (w.TryClaim())
						{
							cancelledWaiters.Add(w);
						}
					}
				}
				start = PumpLocked();
			}

			discard?.Release();
			Free(removed);
			if (failureLine != null)
			{
				SafeLog(failureLine);
			}
			foreach ((Waiter waiter, PageResult result) in deliveries)
			{
				PostDelivery(waiter, result);
			}
			foreach (Waiter waiter in cancelledWaiters)
			{
				PostCancellation(waiter);
			}
			Launch(start);
		}

		private void OnWaiterCancelled(Waiter waiter)
		{
			if (!waiter.TryCancel())
			{
				return; // delivered (or cancelled) already
			}
			var toCancel = new List<Job>();
			lock (gate)
			{
				Job job = waiter.Job;
				job.Waiters.Remove(waiter);
				if (job.Waiters.Count == 0 && !windowSet.Contains(job.Page) && !job.Abandoned)
				{
					AbandonLocked(job, toCancel);
				}
			}
			CancelAll(toCancel);
			PostCancellation(waiter);
		}

		// The job leaves the table (a new request makes a new job). A queued job simply never runs; a running one is told to stop.
		private void AbandonLocked(Job job, List<Job> toCancel)
		{
			job.Abandoned = true;
			if (jobs.TryGetValue(job.Page, out Job current) && ReferenceEquals(current, job))
			{
				jobs.Remove(job.Page);
			}
			if (job.Running)
			{
				toCancel.Add(job);
			}
			else
			{
				droppedFromQueue++;
			}
		}

		private static void CancelAll(List<Job> jobsToCancel)
		{
			foreach (Job job in jobsToCancel)
			{
				try
				{
					job.Cts.Cancel();
				}
				catch (ObjectDisposedException)
				{
				}
			}
		}

		private void PostDelivery(Waiter waiter, PageResult result)
		{
			try
			{
				post(() =>
				{
					waiter.Registration.Dispose();
					if (!waiter.Completion.TrySetResult(result))
					{
						result.Dispose();
					}
				});
			}
			catch (Exception)
			{
				result.Dispose();
				waiter.Completion.TrySetCanceled();
			}
		}

		private void PostCancellation(Waiter waiter)
		{
			try
			{
				post(() =>
				{
					waiter.Registration.Dispose();
					waiter.Completion.TrySetCanceled(waiter.Token);
				});
			}
			catch (Exception)
			{
				waiter.Completion.TrySetCanceled();
			}
		}

		// Evicts the least recently used pages outside the window while the cache is over its bound.
		private void TrimLocked(List<Entry> removed)
		{
			if (cachedBytes <= maxBytes)
			{
				return;
			}
			foreach (Entry entry in cache.Values.Where(e => !windowSet.Contains(e.Page)).OrderBy(e => e.LastUse).ToList())
			{
				if (cachedBytes <= maxBytes)
				{
					break;
				}
				cache.Remove(entry.Page);
				cachedBytes -= entry.Bytes;
				removed.Add(entry);
			}
		}

		private static void Free(List<Entry> entries)
		{
			foreach (Entry entry in entries)
			{
				entry.Image.Release();
			}
		}

		private void NotifyDecoded(int page, CoverImage image)
		{
			Action<int, int, int> callback = Decoded;
			if (callback == null)
			{
				return;
			}
			try
			{
				callback(page, image.Width, image.Height);
			}
			catch (Exception)
			{
			}
		}

		private void SafeLog(string message)
		{
			try
			{
				log?.Invoke(message);
			}
			catch (Exception)
			{
			}
		}

		private void ThrowIfDisposed()
		{
			if (disposed)
			{
				throw new ObjectDisposedException(nameof(PageService));
			}
		}

		/// <summary>
		/// Cancels every job and waiter, empties the cache and waits (up to <see cref="PageServiceOptions.DisposeWait"/>) for decodes
		/// that are still running, so the Core can be torn down afterwards. Pages that frames in flight still draw stay valid until
		/// those release them.
		/// </summary>
		public void Dispose()
		{
			var toCancel = new List<Job>();
			var waiters = new List<Waiter>();
			var removed = new List<Entry>();
			lock (gate)
			{
				if (disposed)
				{
					return;
				}
				disposed = true;
				foreach (Job job in jobs.Values.ToList())
				{
					foreach (Waiter w in job.Waiters)
					{
						if (w.TryClaim())
						{
							waiters.Add(w);
						}
					}
					AbandonLocked(job, toCancel);
				}
				removed.AddRange(cache.Values);
				cache.Clear();
				cachedBytes = 0;
				window = new List<int>();
				windowSet = new HashSet<int>();
			}
			CancelAll(toCancel);
			foreach (Waiter waiter in waiters)
			{
				PostCancellation(waiter);
			}
			Free(removed);
			if (!idle.Wait(disposeWait))
			{
				SafeLog("The page service was disposed while a decode was still running (waited " + disposeWait.TotalSeconds + " s).");
			}
		}

		private sealed class Entry
		{
			public Entry(int page, CoverImage image, long bytes)
			{
				Page = page;
				Image = image;
				Bytes = bytes;
			}

			public int Page { get; }

			/// <summary>The cache's own reference.</summary>
			public CoverImage Image { get; }

			public long Bytes { get; }

			public long LastUse { get; set; }
		}

		private sealed class Job
		{
			public Job(int page)
			{
				Page = page;
			}

			public int Page { get; }

			public CancellationTokenSource Cts { get; } = new CancellationTokenSource();

			public List<Waiter> Waiters { get; } = new List<Waiter>();

			public bool Running { get; set; }

			public bool Abandoned { get; set; }
		}

		private sealed class Waiter
		{
			private int state; // 0 waiting, 1 delivered or being delivered, 2 cancelled

			public Waiter(CancellationToken token, Job job)
			{
				Token = token;
				Job = job;
			}

			public CancellationToken Token { get; }

			public Job Job { get; }

			public CancellationTokenRegistration Registration { get; set; }

			public TaskCompletionSource<PageResult> Completion { get; } = new TaskCompletionSource<PageResult>(TaskCreationOptions.RunContinuationsAsynchronously);

			public bool TryClaim() => Interlocked.CompareExchange(ref state, 1, 0) == 0;

			public bool TryCancel() => Interlocked.CompareExchange(ref state, 2, 0) == 0;
		}
	}
}
