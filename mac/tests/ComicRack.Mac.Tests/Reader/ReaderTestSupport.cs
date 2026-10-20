using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ComicRack.Mac.Reader;
using ComicRack.Mac.Thumbnails;
using SkiaSharp;

namespace ComicRack.Mac.Tests.Reader
{
	/// <summary>
	/// A page decoder that can be told to hold every decode (or the decodes of some pages) until the test opens a gate, to take its
	/// time, to fail, and to ignore cancellation; it records what it was asked, in which order, how many decodes overlapped and which
	/// ones were cancelled. Pages are solid colours that depend on the page number, so a test can tell which page it is looking at.
	/// </summary>
	public sealed class FakePageDecoder : IPageDecoder
	{
		private readonly object sync = new object();
		private readonly ManualResetEventSlim open = new ManualResetEventSlim(true);
		private readonly HashSet<int> heldPages = new HashSet<int>();
		private readonly List<int> order = new List<int>();
		private readonly List<PixelBlock> blocks = new List<PixelBlock>();
		private int started;
		private int finished;
		private int cancelled;
		private int inFlight;
		private int maxInFlight;

		public int PageCount { get; set; } = 10;

		public Func<int, (int Width, int Height)> Size { get; set; } = page => (100, 150);

		/// <summary>If true each page shows its number (and its size) in large letters on its colour, for frames a person looks at.</summary>
		public bool DrawNumbers { get; set; }

		public TimeSpan Delay { get; set; }

		public Func<int, Exception> FailWith { get; set; }

		/// <summary>If true the decode finishes even when cancelled (it only waits for the gate).</summary>
		public bool IgnoreCancellation { get; set; }

		/// <summary>If true the decoder returns null (a broken decoder).</summary>
		public bool ReturnNull { get; set; }

		public int Started => Volatile.Read(ref started);

		public int Finished => Volatile.Read(ref finished);

		public int Cancelled => Volatile.Read(ref cancelled);

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

		/// <summary>The pages in the order their decodes began.</summary>
		public int[] Order
		{
			get
			{
				lock (sync)
				{
					return order.ToArray();
				}
			}
		}

		/// <summary>The blocks handed out, in the order they were made.</summary>
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

		/// <summary>Every decode blocks until <see cref="Open"/>.</summary>
		public void Hold() => open.Reset();

		public void Open() => open.Set();

		/// <summary>The decodes of these pages block until <see cref="Release"/> (the others run).</summary>
		public void HoldPage(int page)
		{
			lock (sync)
			{
				heldPages.Add(page);
			}
		}

		public void Release(int page)
		{
			lock (sync)
			{
				heldPages.Remove(page);
			}
		}

		private static void DrawLabel(SKBitmap bitmap, int page)
		{
			using (var canvas = new SKCanvas(bitmap))
			using (var paint = new SKPaint { IsAntialias = true, Color = SKColors.White })
			using (var font = new SKFont(SKTypeface.FromFamilyName("Helvetica", SKFontStyle.Bold), Math.Min(bitmap.Width, bitmap.Height) * 0.45f))
			using (var small = new SKFont(SKTypeface.FromFamilyName("Helvetica"), Math.Min(bitmap.Width, bitmap.Height) * 0.06f))
			using (var edge = new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Stroke, StrokeWidth = Math.Max(2, bitmap.Width / 150f) })
			{
				canvas.DrawText((page + 1).ToString(), bitmap.Width / 2f, bitmap.Height / 2f + font.Size * 0.35f, SKTextAlign.Center, font, paint);
				canvas.DrawText(bitmap.Width + " x " + bitmap.Height, bitmap.Width / 2f, bitmap.Height * 0.85f, SKTextAlign.Center, small, paint);
				float inset = edge.StrokeWidth;
				canvas.DrawRect(inset / 2, inset / 2, bitmap.Width - inset, bitmap.Height - inset, edge);
			}
		}

		public static SKColor ColorFor(int page)
		{
			return new SKColor((byte)((page * 37 + 40) & 0xFF), (byte)((page * 91 + 20) & 0xFF), (byte)((page * 53 + 90) & 0xFF), 255);
		}

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

		public PixelBlock Decode(int page, CancellationToken cancellationToken)
		{
			int now = Interlocked.Increment(ref inFlight);
			lock (sync)
			{
				if (now > maxInFlight)
				{
					maxInFlight = now;
				}
				order.Add(page);
			}
			Interlocked.Increment(ref started);
			try
			{
				while (true)
				{
					bool held;
					lock (sync)
					{
						held = heldPages.Contains(page);
					}
					if (open.IsSet && !held)
					{
						break;
					}
					if (!IgnoreCancellation && cancellationToken.IsCancellationRequested)
					{
						Interlocked.Increment(ref cancelled);
						throw new OperationCanceledException(cancellationToken);
					}
					Thread.Sleep(1);
				}
				if (Delay > TimeSpan.Zero)
				{
					if (IgnoreCancellation)
					{
						Thread.Sleep(Delay);
					}
					else if (cancellationToken.WaitHandle.WaitOne(Delay))
					{
						Interlocked.Increment(ref cancelled);
						throw new OperationCanceledException(cancellationToken);
					}
				}
				Exception failure = FailWith?.Invoke(page);
				if (failure != null)
				{
					throw failure;
				}
				if (ReturnNull)
				{
					return null;
				}
				(int w, int h) = Size(page);
				var bitmap = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque));
				bitmap.Erase(ColorFor(page));
				if (DrawNumbers)
				{
					DrawLabel(bitmap, page);
				}
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

	/// <summary>A book that does not touch the Core: a fake decoder and a record of what the reader reported.</summary>
	public sealed class FakeSession : IReadingSession
	{
		private readonly TaskCompletionSource<bool> gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		public FakeSession(FakePageDecoder decoder = null, bool openImmediately = true)
		{
			FakeDecoder = decoder ?? new FakePageDecoder();
			if (openImmediately)
			{
				gate.SetResult(true);
			}
		}

		public FakePageDecoder FakeDecoder { get; }

		public string Title { get; set; } = "Fake Book";

		public IPageDecoder Decoder => FakeDecoder;

		public int PageCount => FakeDecoder.PageCount;

		public int ResumePage { get; set; }

		public Exception OpenFailure { get; set; }

		public bool IsOpened { get; private set; }

		public bool IsDisposed { get; private set; }

		public List<int> Reported { get; } = new List<int>();

		public int EndReached { get; private set; }

		/// <summary>What the layout is told about the pages (default: the first page is the front cover).</summary>
		public Func<int, PageTraits> Traits { get; set; }

		/// <summary>What the book says about its reading direction (null: nothing).</summary>
		public bool? RightToLeft { get; set; }

		/// <summary>The furthest pages reported for spreads.</summary>
		public List<int> Furthest { get; } = new List<int>();

		public PageTraits GetPageTraits(int page) => Traits != null ? Traits(page) : PageTraits.Default(page);

		public void ReportFurthest(int page)
		{
			lock (Furthest)
			{
				Furthest.Add(page);
			}
		}

		/// <summary>The rating of the book (the page menu's My Rating).</summary>
		public float RatingValue { get; set; }

		public float Rating => RatingValue;

		public void SetRating(float rating) => RatingValue = rating;

		public int LastReported => Reported.Count == 0 ? -1 : Reported[Reported.Count - 1];

		/// <summary>Lets <see cref="OpenAsync"/> complete (for a session made with openImmediately: false).</summary>
		public void CompleteOpen() => gate.TrySetResult(true);

		public async Task OpenAsync(CancellationToken cancellationToken)
		{
			using (cancellationToken.Register(() => gate.TrySetCanceled(cancellationToken)))
			{
				await gate.Task;
			}
			if (OpenFailure != null)
			{
				throw OpenFailure;
			}
			IsOpened = true;
		}

		public void ReportPage(int page)
		{
			lock (Reported)
			{
				Reported.Add(page);
			}
		}

		public void ReportEndReached()
		{
			EndReached++;
		}

		public void Dispose()
		{
			IsDisposed = true;
		}
	}

	public static class PageImages
	{
		/// <summary>An encoded JPEG of a smooth gradient with a bar, <paramref name="width"/> x <paramref name="height"/>.</summary>
		public static byte[] Jpeg(int width, int height, int quality = 85)
		{
			using (var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque)))
			{
				using (var canvas = new SKCanvas(bitmap))
				using (var paint = new SKPaint())
				{
					paint.Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(width, height), new[] { new SKColor(250, 220, 90), new SKColor(40, 90, 200) }, SKShaderTileMode.Clamp);
					canvas.DrawRect(0, 0, width, height, paint);
				}
				using (SKImage image = SKImage.FromBitmap(bitmap))
				using (SKData data = image.Encode(SKEncodedImageFormat.Jpeg, quality))
				{
					return data.ToArray();
				}
			}
		}

		public static byte[] Png(int width, int height, SKColor color, bool withAlpha)
		{
			using (var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, withAlpha ? SKAlphaType.Premul : SKAlphaType.Opaque)))
			{
				bitmap.Erase(color);
				using (SKImage image = SKImage.FromBitmap(bitmap))
				using (SKData data = image.Encode(SKEncodedImageFormat.Png, 100))
				{
					return data.ToArray();
				}
			}
		}

		public static byte[] Webp(int width, int height, SKColor color)
		{
			using (var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque)))
			{
				bitmap.Erase(color);
				using (SKImage image = SKImage.FromBitmap(bitmap))
				using (SKData data = image.Encode(SKEncodedImageFormat.Webp, 90))
				{
					return data.ToArray();
				}
			}
		}

		/// <summary>The colour of the pixel at (x, y) of an image, read through a copy.</summary>
		public static SKColor PixelOf(SKImage image, int x, int y)
		{
			using (var bitmap = new SKBitmap(new SKImageInfo(image.Width, image.Height, SKColorType.Bgra8888, SKAlphaType.Premul)))
			{
				if (!image.ReadPixels(bitmap.Info, bitmap.GetPixels(), bitmap.RowBytes, 0, 0))
				{
					throw new InvalidOperationException("The image could not be read.");
				}
				return bitmap.GetPixel(x, y);
			}
		}

		public static bool Near(SKColor a, SKColor b, int tolerance = 2)
		{
			return Math.Abs(a.Red - b.Red) <= tolerance && Math.Abs(a.Green - b.Green) <= tolerance && Math.Abs(a.Blue - b.Blue) <= tolerance;
		}
	}
}
