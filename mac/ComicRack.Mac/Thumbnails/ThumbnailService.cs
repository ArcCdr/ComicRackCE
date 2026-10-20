using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Engine.IO;
using SkiaSharp;

namespace ComicRack.Mac.Thumbnails
{
	/// <summary>
	/// Front cover thumbnails for the grid, as <see cref="SKImage"/>s ready to draw.
	///
	/// <list type="bullet">
	/// <item><b>Sizes.</b> A request names a height in pixels; it is served at the next step of <see cref="ThumbnailSteps"/> (512,
	/// 384, 288, ... never above 512), so a slider produces a handful of cache entries per book, not one per pixel.</item>
	/// <item><b>Where pixels come from.</b> The <see cref="IThumbnailDecoder"/> (in the app <see cref="CoreThumbnailDecoder"/>:
	/// the Core's thumbnail caches and, on a miss, the book file).</item>
	/// <item><b>In-memory LRU</b> of decoded images, bounded by pixel bytes (default 256 MB). A cache hit returns an already
	/// completed task.</item>
	/// <item><b>Scheduling.</b> At most <see cref="ThumbnailServiceOptions.MaxConcurrency"/> (4) decodes run at once. Waiting
	/// requests form a stack: the newest request is decoded first, so the cells that are on screen now win over the ones the user
	/// has scrolled past. Asking again for a request that waits moves it to the top.</item>
	/// <item><b>Cancellation.</b> A request whose token is cancelled before its decode starts never decodes: it leaves the queue
	/// (a decode that was handed to a worker but has not begun checks once more). A running decode is cancelled through the
	/// token the decoder receives, but only when nobody waits for it any more.</item>
	/// <item><b>De-duplication.</b> Concurrent requests for the same book and step share one decode. Each caller has its own token:
	/// one caller cancelling only detaches that caller.</item>
	/// <item><b>Failures</b> (missing or damaged file, decoder exception) never fault the task: the result is a placeholder
	/// picture (<see cref="ThumbnailResult.IsPlaceholder"/>, with the exception in <see cref="ThumbnailResult.Error"/>). The
	/// failure is remembered for <see cref="ThumbnailServiceOptions.FailureRetryAfter"/> so a grid of 14,000 missing books does not
	/// retry on every scroll.</item>
	/// <item><b>Threading.</b> Every result is delivered by completing the task on the UI thread, through
	/// <see cref="ThumbnailServiceOptions.Post"/> (<see cref="UiDispatch"/> by default); a cache hit is the one exception,
	/// it completes synchronously in the caller. All methods may be called from any thread.</item>
	/// </list>
	///
	/// <b>Image lifetime (the rule for consumers).</b> Every task gives its caller an <see cref="SKImage"/> of its own. The pixels
	/// behind it are shared and reference counted (<see cref="PixelBlock"/>), so: the caller may <c>Dispose</c> the image when the
	/// cell is recycled or the image replaced, and should; an image that is never disposed is freed by the garbage collector
	/// (and the service tells the collector about memory it still holds after the cache gave it up); and the cache evicting an
	/// entry never invalidates an image that is being drawn, because eviction only drops the cache's own reference. Memory that
	/// consumers hold after eviction is not counted against the cache bound.
	/// </summary>
	public sealed class ThumbnailService : IDisposable
	{
		private const long FailureEntryCost = 512;
		private const int LoggedFailureLimit = 20;

		private readonly object gate = new object();
		private readonly IThumbnailDecoder decoder;
		private readonly Action<Action> post;
		private readonly Action<Action> run;
		private readonly Action<string> log;
		private readonly TimeProvider time;
		private readonly long maxBytes;
		private readonly int maxConcurrency;
		private readonly TimeSpan failureRetry;
		private readonly TimeSpan disposeWait;

		private readonly Dictionary<Key, Job> jobs = new Dictionary<Key, Job>();
		private readonly LinkedList<Job> queue = new LinkedList<Job>(); // Last = the newest request = decoded next
		private readonly HashSet<Job> runningJobs = new HashSet<Job>();
		private readonly Dictionary<Key, LinkedListNode<Entry>> map = new Dictionary<Key, LinkedListNode<Entry>>();
		private readonly LinkedList<Entry> lru = new LinkedList<Entry>(); // First = the most recently used
		private readonly Dictionary<int, PixelBlock> placeholders = new Dictionary<int, PixelBlock>();

		private long cachedBytes;
		private int running;
		private bool disposed;

		private long requests;
		private long cacheHits;
		private long coalesced;
		private long droppedFromQueue;
		private long decodesStarted;
		private long decodesCompleted;
		private long decodesCancelled;
		private long failures;
		private int peakInFlight;
		private int loggedFailures;

		public ThumbnailService(ThumbnailServiceOptions options)
		{
			if (options == null)
			{
				throw new ArgumentNullException(nameof(options));
			}
			decoder = options.Decoder ?? throw new ArgumentException("A decoder is required.", nameof(options));
			post = options.Post ?? throw new ArgumentException("Post is required.", nameof(options));
			run = options.Run ?? throw new ArgumentException("Run is required.", nameof(options));
			log = options.Log;
			time = options.Time ?? TimeProvider.System;
			if (options.MaxCacheBytes <= 0)
			{
				throw new ArgumentOutOfRangeException(nameof(options), "MaxCacheBytes must be positive.");
			}
			if (options.MaxConcurrency < 1)
			{
				throw new ArgumentOutOfRangeException(nameof(options), "MaxConcurrency must be at least 1.");
			}
			maxBytes = options.MaxCacheBytes;
			maxConcurrency = options.MaxConcurrency;
			failureRetry = options.FailureRetryAfter;
			disposeWait = options.DisposeWait;
		}

		/// <summary>A service on the Core's caches with the defaults (256 MB, 4 decodes, results on the UI thread).</summary>
		public ThumbnailService(CacheManager cache)
			: this(new ThumbnailServiceOptions { Decoder = new CoreThumbnailDecoder(cache) })
		{
		}

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

		// ---- Requests --------------------------------------------------------------------------------------------------

		/// <summary>
		/// The front cover of <paramref name="book"/> at (at least) <paramref name="sizePx"/> pixels high, see
		/// <see cref="ThumbnailSteps"/>. The caller owns the image (see the class remarks). Cancelling the token completes the task
		/// as cancelled and withdraws the request. Never faults because of a file problem: use <see cref="RequestResult"/> to learn
		/// that the picture is a placeholder.
		/// </summary>
		/// <exception cref="ObjectDisposedException">The service is disposed (a request in flight is cancelled instead).</exception>
		public async Task<SKImage> Request(ComicBook book, int sizePx, CancellationToken cancellationToken)
		{
			ThumbnailResult result = await RequestResult(book, sizePx, cancellationToken).ConfigureAwait(false);
			return result.Image;
		}

		/// <summary>Like <see cref="Request"/>, with the information whether the picture is the placeholder and why.</summary>
		public Task<ThumbnailResult> RequestResult(ComicBook book, int sizePx, CancellationToken cancellationToken)
		{
			return RequestResult(book, FrontCover, sizePx, cancellationToken);
		}

		/// <summary>The value of <c>page</c> that means the front cover of the book (the page the book's preferred front cover names).</summary>
		public const int FrontCover = -1;

		/// <summary>
		/// A page of the book instead of its front cover: the page behind the dog-ear of a selected cell (<c>CoverViewItem.GetBackThumbnail</c>).
		/// Same sizes, cache and rules as <see cref="RequestResult(ComicBook, int, CancellationToken)"/>.
		/// </summary>
		public Task<ThumbnailResult> RequestResult(ComicBook book, int page, int sizePx, CancellationToken cancellationToken)
		{
			if (book == null)
			{
				throw new ArgumentNullException(nameof(book));
			}
			if (sizePx <= 0)
			{
				throw new ArgumentOutOfRangeException(nameof(sizePx), "The size must be positive.");
			}
			ThrowIfDisposed();
			if (cancellationToken.IsCancellationRequested)
			{
				return Task.FromCanceled<ThumbnailResult>(cancellationToken);
			}
			int height = ThumbnailSteps.Snap(sizePx);
			var key = new Key(page == FrontCover ? book.GetFrontCoverThumbnailKey() : book.GetThumbnailKey(page), height);
			Subscriber subscriber = null;
			ThumbnailResult hit;
			List<Entry> removed = null;
			List<Job> start = null;
			lock (gate)
			{
				ThrowIfDisposed();
				requests++;
				if (TryHitLocked(key, out hit, ref removed))
				{
					cacheHits++;
				}
				else
				{
					if (!jobs.TryGetValue(key, out Job job) || job.Abandoned)
					{
						job = new Job(key, new ThumbnailRequest(book, key.Image, height));
						jobs[key] = job;
						job.QueueNode = queue.AddLast(job);
					}
					else
					{
						coalesced++;
						if (job.QueueNode != null)
						{
							// Asked for again: it is the newest request now.
							queue.Remove(job.QueueNode);
							queue.AddLast(job.QueueNode);
						}
					}
					subscriber = new Subscriber(cancellationToken) { Job = job };
					job.Subscribers.Add(subscriber);
					start = PumpLocked();
				}
			}
			Free(removed);
			if (hit != null)
			{
				return Task.FromResult(hit);
			}
			Launch(start);
			// Outside the lock: an already cancelled token runs the callback right here, and the callback takes the lock.
			subscriber.Attach(cancellationToken.Register(() => OnCancelled(subscriber)));
			return subscriber.Completion.Task;
		}

		/// <summary>
		/// The cached thumbnail, if the memory cache has it (a placeholder counts, while its failure is remembered). Never starts a
		/// decode; a hit counts as a use for the LRU order. The caller owns the image.
		/// </summary>
		public bool TryGetCached(ComicBook book, int sizePx, out ThumbnailResult result)
		{
			return TryGetCached(book, FrontCover, sizePx, out result);
		}

		/// <summary>Like <see cref="TryGetCached(ComicBook, int, out ThumbnailResult)"/> for a page of the book (see <see cref="RequestResult(ComicBook, int, int, CancellationToken)"/>).</summary>
		public bool TryGetCached(ComicBook book, int page, int sizePx, out ThumbnailResult result)
		{
			if (book == null)
			{
				throw new ArgumentNullException(nameof(book));
			}
			if (sizePx <= 0)
			{
				throw new ArgumentOutOfRangeException(nameof(sizePx), "The size must be positive.");
			}
			var key = new Key(page == FrontCover ? book.GetFrontCoverThumbnailKey() : book.GetThumbnailKey(page), ThumbnailSteps.Snap(sizePx));
			List<Entry> removed = null;
			bool hit;
			lock (gate)
			{
				ThrowIfDisposed();
				hit = TryHitLocked(key, out result, ref removed);
			}
			Free(removed);
			return hit;
		}

		// ---- Scheduling ------------------------------------------------------------------------------------------------

		/// <summary>Takes the newest queued jobs while a slot is free. Call with the lock held; start the result outside it.</summary>
		private List<Job> PumpLocked()
		{
			List<Job> start = null;
			while (!disposed && running < maxConcurrency && queue.Last != null)
			{
				Job job = queue.Last.Value;
				queue.RemoveLast();
				job.QueueNode = null;
				running++;
				runningJobs.Add(job);
				if (running > peakInFlight)
				{
					peakInFlight = running;
				}
				(start ?? (start = new List<Job>())).Add(job);
			}
			return start;
		}

		private void Launch(List<Job> start)
		{
			if (start == null)
			{
				return;
			}
			foreach (Job job in start)
			{
				Job j = job;
				try
				{
					run(() => RunJob(j));
				}
				catch (Exception ex)
				{
					// The work could not be queued: finish the job as a failure, or its callers would wait for ever.
					RunJobFailed(j, ex);
				}
			}
		}

		private void RunJobFailed(Job job, Exception error)
		{
			try
			{
				Finish(job, null, error, cancelled: false);
			}
			catch (Exception ex)
			{
				SafeLog("Thumbnail service: could not start a decode: " + ex);
			}
		}

		private void RunJob(Job job)
		{
			PixelBlock block = null;
			Exception error = null;
			bool cancelled = false;
			try
			{
				if (job.Cts.IsCancellationRequested)
				{
					// Handed to a worker, but everyone left before it began: it never decodes.
					cancelled = true;
					Interlocked.Increment(ref droppedFromQueue);
				}
				else
				{
					Interlocked.Increment(ref decodesStarted);
					block = decoder.Decode(job.Request, job.Cts.Token);
					if (block == null)
					{
						throw new InvalidOperationException("The decoder returned no pixels.");
					}
				}
			}
			catch (OperationCanceledException) when (job.Cts.IsCancellationRequested)
			{
				cancelled = true;
				Interlocked.Increment(ref decodesCancelled);
			}
			catch (Exception ex)
			{
				error = ex;
			}
			try
			{
				Finish(job, block, error, cancelled);
			}
			catch (Exception ex)
			{
				// Finish does bookkeeping only; if it ever throws, do not leave the waiting requests hanging.
				SafeLog("Thumbnail service: finishing a request failed: " + ex);
			}
		}

		private void Finish(Job job, PixelBlock block, Exception error, bool cancelled)
		{
			if (Interlocked.Exchange(ref job.Ended, 1) != 0)
			{
				// Only if an executor both queued the work and threw: the job must not be accounted twice.
				block?.Release();
				return;
			}
			var removed = new List<Entry>();
			var deliveries = new List<Delivery>();
			PixelBlock uncached = null;
			List<Job> start;
			lock (gate)
			{
				running--;
				runningJobs.Remove(job);
				job.Finished = true;
				if (jobs.TryGetValue(job.Key, out Job current) && ReferenceEquals(current, job))
				{
					jobs.Remove(job.Key);
				}
				List<Subscriber> waiting = job.Subscribers.FindAll(s => s.State == SubscriberState.Pending);
				job.Subscribers.Clear();
				foreach (Subscriber s in waiting)
				{
					s.State = SubscriberState.Delivered;
				}

				PixelBlock show = null;
				bool placeholder = false;
				if (disposed)
				{
					uncached = block;
				}
				else if (block != null)
				{
					decodesCompleted++;
					show = block;
					if (!InsertLocked(job.Key, block, isFailure: false, null, removed))
					{
						uncached = block; // bigger than the whole cache: delivered, not kept
					}
				}
				else if (!cancelled)
				{
					failures++;
					placeholder = true;
					show = PlaceholderLocked(job.Key.Height);
					InsertLocked(job.Key, show, isFailure: true, error, removed);
				}
				foreach (Subscriber s in waiting)
				{
					deliveries.Add(new Delivery(s, show?.CreateImage(), placeholder, placeholder ? error : null));
				}
				start = PumpLocked();
				Monitor.PulseAll(gate);
			}
			Free(removed);
			uncached?.ReleaseOwner();
			job.Cts.Dispose(); // nobody cancels a finished job any more (SafeCancel tolerates the race)
			Launch(start);
			if (error != null && !cancelled)
			{
				LogFailure(job, error);
			}
			foreach (Delivery d in deliveries)
			{
				PostDelivery(d);
			}
		}

		private void OnCancelled(Subscriber subscriber)
		{
			CancellationTokenSource stop = null;
			lock (gate)
			{
				if (subscriber.State != SubscriberState.Pending)
				{
					return; // already being delivered (the delivery sees the cancelled token) or already handled
				}
				subscriber.State = SubscriberState.Cancelled;
				Job job = subscriber.Job;
				job.Subscribers.Remove(subscriber);
				if (job.Subscribers.Count == 0 && !job.Finished && !job.Abandoned)
				{
					job.Abandoned = true;
					if (job.QueueNode != null)
					{
						// Still waiting for its turn: gone, and it will never decode.
						queue.Remove(job.QueueNode);
						job.QueueNode = null;
						job.Finished = true;
						if (jobs.TryGetValue(job.Key, out Job current) && ReferenceEquals(current, job))
						{
							jobs.Remove(job.Key);
						}
						Interlocked.Increment(ref droppedFromQueue);
					}
					else
					{
						stop = job.Cts; // running, or about to run: stop it
					}
				}
			}
			SafeCancel(stop);
			PostCancellation(subscriber);
		}

		// ---- Delivery on the UI thread ---------------------------------------------------------------------------------

		private void PostDelivery(Delivery d)
		{
			Subscriber s = d.Subscriber;
			Action deliver = () =>
			{
				s.Detach();
				CancellationToken token = s.Token;
				if (d.Image == null || token.IsCancellationRequested)
				{
					// The caller left (a recycled cell) or the service closed: the picture is not wanted.
					d.Image?.Dispose();
					s.Completion.TrySetCanceled(token.IsCancellationRequested ? token : default);
				}
				else if (!s.Completion.TrySetResult(new ThumbnailResult(d.Image, d.IsPlaceholder, d.Error)))
				{
					d.Image.Dispose();
				}
			};
			try
			{
				post(deliver);
			}
			catch (Exception)
			{
				// The UI thread is gone (shutdown): nobody can use the picture.
				d.Image?.Dispose();
				s.Completion.TrySetCanceled();
			}
		}

		private void PostCancellation(Subscriber s)
		{
			try
			{
				post(() =>
				{
					s.Detach();
					s.Completion.TrySetCanceled(s.Token);
				});
			}
			catch (Exception)
			{
				s.Completion.TrySetCanceled(s.Token);
			}
		}

		// ---- Cache -----------------------------------------------------------------------------------------------------

		private bool TryHitLocked(Key key, out ThumbnailResult result, ref List<Entry> removed)
		{
			result = null;
			if (!map.TryGetValue(key, out LinkedListNode<Entry> node))
			{
				return false;
			}
			Entry entry = node.Value;
			if (entry.IsFailure && time.GetUtcNow() >= entry.RetryAt)
			{
				// The failure has been remembered long enough: try again.
				RemoveLocked(node, removed ?? (removed = new List<Entry>()));
				return false;
			}
			lru.Remove(node);
			lru.AddFirst(node);
			result = new ThumbnailResult(entry.Block.CreateImage(), entry.IsFailure, entry.IsFailure ? entry.Error : null);
			return true;
		}

		/// <summary>
		/// Adds an entry as the most recently used and evicts from the other end until the bound holds. For a normal entry the cache
		/// takes over the caller's reference if it returns true. A failure entry takes a reference of its own. Evicted entries go to
		/// <paramref name="removed"/>, to be given back outside the lock.
		/// </summary>
		private bool InsertLocked(Key key, PixelBlock block, bool isFailure, Exception error, List<Entry> removed)
		{
			long cost = isFailure ? FailureEntryCost : block.ByteCount;
			if (cost > maxBytes)
			{
				return false;
			}
			if (map.TryGetValue(key, out LinkedListNode<Entry> old))
			{
				RemoveLocked(old, removed);
			}
			if (isFailure)
			{
				block.Retain();
			}
			var entry = new Entry(key, block, cost, isFailure, error, isFailure ? time.GetUtcNow() + failureRetry : default);
			LinkedListNode<Entry> node = lru.AddFirst(entry);
			map[key] = node;
			cachedBytes += cost;
			while (cachedBytes > maxBytes && lru.Last != node)
			{
				RemoveLocked(lru.Last, removed);
			}
			return true;
		}

		private void RemoveLocked(LinkedListNode<Entry> node, List<Entry> removed)
		{
			Entry entry = node.Value;
			map.Remove(entry.Key);
			lru.Remove(node);
			cachedBytes -= entry.Cost;
			removed.Add(entry);
		}

		/// <summary>Gives the cache's references back. Outside the lock: freeing pixels is not free.</summary>
		private static void Free(List<Entry> entries)
		{
			if (entries == null)
			{
				return;
			}
			foreach (Entry e in entries)
			{
				if (e.IsFailure)
				{
					e.Block.Release();
				}
				else
				{
					e.Block.ReleaseOwner();
				}
			}
		}

		private PixelBlock PlaceholderLocked(int height)
		{
			if (!placeholders.TryGetValue(height, out PixelBlock block))
			{
				block = ThumbnailPixels.CreatePlaceholder(height);
				placeholders[height] = block;
			}
			return block;
		}

		/// <summary>Empties the memory cache. Images already handed out stay valid.</summary>
		public void Clear()
		{
			var removed = new List<Entry>();
			lock (gate)
			{
				while (lru.Last != null)
				{
					RemoveLocked(lru.Last, removed);
				}
			}
			Free(removed);
		}

		// ---- Statistics, logging, shutdown -----------------------------------------------------------------------------

		public ThumbnailServiceStats GetStats()
		{
			lock (gate)
			{
				return new ThumbnailServiceStats
				{
					Requests = requests,
					CacheHits = cacheHits,
					Coalesced = coalesced,
					DroppedFromQueue = Interlocked.Read(ref droppedFromQueue),
					DecodesStarted = Interlocked.Read(ref decodesStarted),
					DecodesCompleted = decodesCompleted,
					DecodesCancelled = Interlocked.Read(ref decodesCancelled),
					Failures = failures,
					InFlight = running,
					PeakInFlight = peakInFlight,
					Queued = queue.Count,
					CachedCount = map.Count,
					CachedBytes = cachedBytes,
					MaxCacheBytes = maxBytes,
				};
			}
		}

		private void LogFailure(Job job, Exception error)
		{
			if (log == null)
			{
				return;
			}
			int n = Interlocked.Increment(ref loggedFailures);
			if (n <= LoggedFailureLimit)
			{
				SafeLog("Thumbnail not available for " + job.Request.Key.Location + ": " + error.GetType().Name + ": " + error.Message
					+ (n == LoggedFailureLimit ? " (further thumbnail failures are only counted)" : string.Empty));
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
				throw new ObjectDisposedException(nameof(ThumbnailService));
			}
		}

		/// <summary>
		/// Cancels everything that waits or runs (their tasks complete as cancelled), empties the cache and waits a few seconds for
		/// decodes that are running, so that the host the decoder reads from can be closed afterwards. Images handed out stay valid.
		/// </summary>
		public void Dispose()
		{
			var removed = new List<Entry>();
			var cancelled = new List<Subscriber>();
			var stop = new List<CancellationTokenSource>();
			var blocks = new List<PixelBlock>();
			lock (gate)
			{
				if (disposed)
				{
					return;
				}
				disposed = true;
				foreach (Job job in jobs.Values)
				{
					Collect(job, cancelled);
				}
				foreach (Job job in runningJobs)
				{
					Collect(job, cancelled);
					stop.Add(job.Cts);
				}
				foreach (Job job in queue)
				{
					job.QueueNode = null;
					job.Finished = true;
				}
				queue.Clear();
				jobs.Clear();
				while (lru.Last != null)
				{
					RemoveLocked(lru.Last, removed);
				}
				blocks.AddRange(placeholders.Values);
				placeholders.Clear();
			}
			foreach (CancellationTokenSource cts in stop)
			{
				SafeCancel(cts);
			}
			Free(removed);
			foreach (PixelBlock b in blocks)
			{
				b.Release();
			}
			foreach (Subscriber s in cancelled)
			{
				PostCancellation(s);
			}
			// Wait (bounded) for the decodes that are running, so the caller may close what they read from.
			var watch = Stopwatch.StartNew();
			lock (gate)
			{
				while (running > 0)
				{
					TimeSpan left = disposeWait - watch.Elapsed;
					if (left <= TimeSpan.Zero)
					{
						break;
					}
					Monitor.Wait(gate, left);
				}
			}
		}

		/// <summary>Cancels a job's token; the job may have finished (and its source been disposed) in the meantime.</summary>
		private static void SafeCancel(CancellationTokenSource cts)
		{
			try
			{
				cts?.Cancel();
			}
			catch (ObjectDisposedException)
			{
			}
		}

		private static void Collect(Job job, List<Subscriber> into)
		{
			foreach (Subscriber s in job.Subscribers)
			{
				if (s.State == SubscriberState.Pending)
				{
					s.State = SubscriberState.Cancelled;
					into.Add(s);
				}
			}
			job.Subscribers.Clear();
			job.Abandoned = true;
		}

		// ---- Types -----------------------------------------------------------------------------------------------------

		/// <summary>Book image identity (the Core's key: location, file size and time, page index, rotation) plus the step.</summary>
		private readonly struct Key : IEquatable<Key>
		{
			public Key(ThumbnailKey image, int height)
			{
				Image = image;
				Height = height;
			}

			public ThumbnailKey Image { get; }

			public int Height { get; }

			public bool Equals(Key other) => Height == other.Height && Image.Equals(other.Image);

			public override bool Equals(object obj) => obj is Key other && Equals(other);

			public override int GetHashCode() => HashCode.Combine(Image.GetHashCode(), Height);
		}

		private sealed class Entry
		{
			public Entry(Key key, PixelBlock block, long cost, bool isFailure, Exception error, DateTimeOffset retryAt)
			{
				Key = key;
				Block = block;
				Cost = cost;
				IsFailure = isFailure;
				Error = error;
				RetryAt = retryAt;
			}

			public Key Key { get; }

			public PixelBlock Block { get; }

			public long Cost { get; }

			public bool IsFailure { get; }

			public Exception Error { get; }

			public DateTimeOffset RetryAt { get; }
		}

		private sealed class Job
		{
			public Job(Key key, ThumbnailRequest request)
			{
				Key = key;
				Request = request;
			}

			public Key Key { get; }

			public ThumbnailRequest Request { get; }

			/// <summary>Cancelled when the last caller leaves; disposed when the job ends.</summary>
			public CancellationTokenSource Cts { get; } = new CancellationTokenSource();

			public List<Subscriber> Subscribers { get; } = new List<Subscriber>();

			/// <summary>Set while the job waits in the queue.</summary>
			public LinkedListNode<Job> QueueNode { get; set; }

			/// <summary>The last caller left: a new request for the same key starts a job of its own.</summary>
			public bool Abandoned { get; set; }

			public bool Finished { get; set; }

			/// <summary>0 until <c>Finish</c> ran for this job (a field, for <c>Interlocked</c>).</summary>
			public int Ended;
		}

		private enum SubscriberState
		{
			Pending,
			Delivered,
			Cancelled,
		}

		/// <summary>One caller of <see cref="RequestResult"/>: its token, its task, and its registration on the token.</summary>
		private sealed class Subscriber
		{
			private CancellationTokenRegistration registration;
			private bool detached;

			public Subscriber(CancellationToken token)
			{
				Token = token;
			}

			public CancellationToken Token { get; }

			public TaskCompletionSource<ThumbnailResult> Completion { get; } = new TaskCompletionSource<ThumbnailResult>();

			public Job Job { get; set; }

			/// <summary>Guarded by the service lock.</summary>
			public SubscriberState State { get; set; }

			public void Attach(CancellationTokenRegistration value)
			{
				bool dispose;
				lock (this)
				{
					registration = value;
					dispose = detached;
				}
				if (dispose)
				{
					value.Dispose();
				}
			}

			public void Detach()
			{
				CancellationTokenRegistration value;
				lock (this)
				{
					detached = true;
					value = registration;
					registration = default;
				}
				value.Dispose();
			}
		}

		private readonly struct Delivery
		{
			public Delivery(Subscriber subscriber, SKImage image, bool isPlaceholder, Exception error)
			{
				Subscriber = subscriber;
				Image = image;
				IsPlaceholder = isPlaceholder;
				Error = error;
			}

			public Subscriber Subscriber { get; }

			public SKImage Image { get; }

			public bool IsPlaceholder { get; }

			public Exception Error { get; }
		}
	}

	/// <summary>A snapshot of the counters of a <see cref="ThumbnailService"/>.</summary>
	public sealed class ThumbnailServiceStats
	{
		public long Requests { get; set; }

		/// <summary>Requests answered from the memory cache.</summary>
		public long CacheHits { get; set; }

		/// <summary>Requests that joined a decode that was already queued or running.</summary>
		public long Coalesced { get; set; }

		/// <summary>Jobs that were cancelled before their decode began (they never decoded).</summary>
		public long DroppedFromQueue { get; set; }

		public long DecodesStarted { get; set; }

		public long DecodesCompleted { get; set; }

		/// <summary>Running decodes that were cancelled because nobody waited for them any more.</summary>
		public long DecodesCancelled { get; set; }

		/// <summary>Decodes that failed (the callers got the placeholder).</summary>
		public long Failures { get; set; }

		public int InFlight { get; set; }

		public int PeakInFlight { get; set; }

		public int Queued { get; set; }

		public int CachedCount { get; set; }

		public long CachedBytes { get; set; }

		public long MaxCacheBytes { get; set; }
	}
}
