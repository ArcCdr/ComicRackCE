using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using ComicRack.Mac.Thumbnails;
using cYo.Projects.ComicRack.Engine;
using SkiaSharp;

namespace ComicRack.Mac.Tests.Thumbnails
{
	/// <summary>A stand-in for the UI thread: posted actions wait in a queue until the test calls <see cref="Pump"/> on its own thread.</summary>
	public sealed class FakeUi
	{
		private readonly ConcurrentQueue<Action> queue = new ConcurrentQueue<Action>();

		public int Pending => queue.Count;

		public int PostCount { get; private set; }

		public void Post(Action action)
		{
			PostCount++;
			queue.Enqueue(action);
		}

		/// <summary>Runs what was posted so far (and what that posts) on the calling thread; returns how many actions ran.</summary>
		public int Pump()
		{
			int n = 0;
			while (queue.TryDequeue(out Action action))
			{
				action();
				n++;
			}
			return n;
		}

		/// <summary>Pumps until <paramref name="done"/> is true; fails after <paramref name="timeoutMs"/>.</summary>
		public void PumpUntil(Func<bool> done, int timeoutMs = 10000)
		{
			var watch = Stopwatch.StartNew();
			while (!done())
			{
				Pump();
				if (done())
				{
					return;
				}
				if (watch.ElapsedMilliseconds > timeoutMs)
				{
					throw new TimeoutException("Condition not reached within " + timeoutMs + " ms.");
				}
				Thread.Sleep(1);
			}
		}

		public void PumpUntilCompleted(params Task[] tasks)
		{
			PumpUntil(() => Array.TrueForAll(tasks, t => t.IsCompleted));
		}
	}

	/// <summary>
	/// A decoder that can be told to hold every decode until the test opens a gate, to take its time, to fail, and to ignore
	/// cancellation; it counts what it was asked and how many decodes overlapped.
	/// </summary>
	public sealed class FakeDecoder : IThumbnailDecoder
	{
		private readonly object sync = new object();
		private readonly ManualResetEventSlim open = new ManualResetEventSlim(true);
		private readonly SemaphoreSlim startedSignal = new SemaphoreSlim(0);
		private int started;
		private int inFlight;
		private int maxInFlight;
		private int finished;
		private readonly List<string> order = new List<string>();
		private readonly List<PixelBlock> blocks = new List<PixelBlock>();

		/// <summary>Pixel size of the produced bitmap (default 100 x 100 = 40,000 bytes, whatever the step).</summary>
		public Func<ThumbnailRequest, (int Width, int Height)> Size { get; set; } = r => (100, 100);

		public TimeSpan Delay { get; set; }

		public Func<ThumbnailRequest, Exception> FailWith { get; set; }

		/// <summary>If true the decode finishes even when cancelled (it only waits for the gate).</summary>
		public bool IgnoreCancellation { get; set; }

		public int Started => Volatile.Read(ref started);

		public int Finished => Volatile.Read(ref finished);

		public int InFlight => Volatile.Read(ref inFlight);

		public int MaxInFlight
		{
			get
			{
				lock (sync)
				{
					return maxInFlight;
				}
			}
		}

		/// <summary>Locations in the order the decodes began.</summary>
		public string[] Order
		{
			get
			{
				lock (sync)
				{
					return order.ToArray();
				}
			}
		}

		/// <summary>The blocks handed to the service, in the order they were made.</summary>
		public PixelBlock[] Blocks
		{
			get
			{
				lock (sync)
				{
					return blocks.ToArray();
				}
			}
		}

		/// <summary>Decodes block until <see cref="Open"/>.</summary>
		public void Hold() => open.Reset();

		public void Open() => open.Set();

		/// <summary>Waits until <paramref name="count"/> decodes have begun in total.</summary>
		public void WaitForStarted(int count, int timeoutMs = 10000)
		{
			var watch = Stopwatch.StartNew();
			while (Started < count)
			{
				if (watch.ElapsedMilliseconds > timeoutMs)
				{
					throw new TimeoutException("Only " + Started + " of " + count + " decodes started.");
				}
				Thread.Sleep(1);
			}
		}

		public void WaitForFinished(int count, int timeoutMs = 10000)
		{
			var watch = Stopwatch.StartNew();
			while (Finished < count)
			{
				if (watch.ElapsedMilliseconds > timeoutMs)
				{
					throw new TimeoutException("Only " + Finished + " of " + count + " decodes finished.");
				}
				Thread.Sleep(1);
			}
		}

		public static SKColor ColorFor(string location)
		{
			int h = location.GetHashCode();
			return new SKColor((byte)(h & 0xFF), (byte)((h >> 8) & 0xFF), (byte)((h >> 16) & 0xFF), 255);
		}

		public PixelBlock Decode(ThumbnailRequest request, CancellationToken cancellationToken)
		{
			int now = Interlocked.Increment(ref inFlight);
			lock (sync)
			{
				if (now > maxInFlight)
				{
					maxInFlight = now;
				}
				order.Add(request.Key.Location);
			}
			Interlocked.Increment(ref started);
			startedSignal.Release();
			try
			{
				if (IgnoreCancellation)
				{
					open.Wait();
				}
				else if (WaitHandle.WaitAny(new[] { open.WaitHandle, cancellationToken.WaitHandle }) == 1)
				{
					throw new OperationCanceledException(cancellationToken);
				}
				if (Delay > TimeSpan.Zero)
				{
					if (!IgnoreCancellation && cancellationToken.WaitHandle.WaitOne(Delay))
					{
						throw new OperationCanceledException(cancellationToken);
					}
					if (IgnoreCancellation)
					{
						Thread.Sleep(Delay);
					}
				}
				Exception failure = FailWith?.Invoke(request);
				if (failure != null)
				{
					throw failure;
				}
				(int w, int h) = Size(request);
				var bitmap = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque));
				bitmap.Erase(ColorFor(request.Key.Location));
				PixelBlock block = PixelBlock.FromBitmap(bitmap);
				lock (sync)
				{
					blocks.Add(block);
				}
				return block;
			}
			finally
			{
				Interlocked.Decrement(ref inFlight);
				Interlocked.Increment(ref finished);
			}
		}
	}

	public static class Books
	{
		/// <summary>A book that is not in any database and whose file does not exist: enough for the Core to make its thumbnail key.</summary>
		public static ComicBook Make(string name)
		{
			return new ComicBook { FilePath = "/virtual/" + name + ".cbz" };
		}

		public static string Location(ComicBook book) => book.GetFrontCoverThumbnailKey().Location;
	}

	public static class Gc
	{
		/// <summary>Runs the finalizers of everything unreachable, so images that earlier tests forgot to dispose do not change counters later.</summary>
		public static void Flush()
		{
			for (int i = 0; i < 3; i++)
			{
				GC.Collect();
				GC.WaitForPendingFinalizers();
			}
		}
	}

	/// <summary>A clock the test advances.</summary>
	public sealed class ManualTime : TimeProvider
	{
		private DateTimeOffset now = new DateTimeOffset(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);

		public override DateTimeOffset GetUtcNow() => now;

		public void Advance(TimeSpan by) => now += by;
	}
}
