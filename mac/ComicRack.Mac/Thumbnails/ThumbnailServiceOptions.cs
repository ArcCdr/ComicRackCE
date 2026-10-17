using System;
using System.Threading.Tasks;
using ComicRack.Mac.Services;

namespace ComicRack.Mac.Thumbnails
{
	/// <summary>Settings of a <see cref="ThumbnailService"/>. Everything but <see cref="Decoder"/> has a default for the app; tests replace the seams.</summary>
	public sealed class ThumbnailServiceOptions
	{
		/// <summary>Produces the pixels (required). The app uses <see cref="CoreThumbnailDecoder"/>.</summary>
		public IThumbnailDecoder Decoder { get; set; }

		/// <summary>Bound of the in-memory cache, in pixel bytes (default 256 MB). An image larger than this is delivered but not cached.</summary>
		public long MaxCacheBytes { get; set; } = 256L * 1024 * 1024;

		/// <summary>Decodes that may run at the same time (default 4).</summary>
		public int MaxConcurrency { get; set; } = 4;

		/// <summary>
		/// Runs an action on the UI thread, later (never inline): every result is delivered through it. Default
		/// <see cref="UiDispatch.Post"/>; tests pass a queue they pump themselves.
		/// </summary>
		public Action<Action> Post { get; set; } = action => UiDispatch.Post(action);

		/// <summary>
		/// Starts the work of one decode on a worker thread (default <c>Task.Run</c>). Tests replace it to hold a job in the state
		/// "taken from the queue, not started yet".
		/// </summary>
		public Action<Action> Run { get; set; } = work => Task.Run(work);

		/// <summary>How long a failed thumbnail stays a placeholder before the next request tries again (default 30 s).</summary>
		public TimeSpan FailureRetryAfter { get; set; } = TimeSpan.FromSeconds(30);

		/// <summary>The clock for <see cref="FailureRetryAfter"/>.</summary>
		public TimeProvider Time { get; set; } = TimeProvider.System;

		/// <summary>Receives the first few failure messages (default <see cref="AppLog.Info"/>; a missing file is not an error of the app).</summary>
		public Action<string> Log { get; set; } = message => AppLog.Info(message);

		/// <summary>How long <see cref="ThumbnailService.Dispose"/> waits for decodes that are running (default 3 s).</summary>
		public TimeSpan DisposeWait { get; set; } = TimeSpan.FromSeconds(3);
	}
}
