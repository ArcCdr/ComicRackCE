using System.Threading;

namespace ComicRack.Mac.Thumbnails
{
	/// <summary>
	/// Counts the <see cref="PixelBlock"/>s made while it is the <see cref="PixelBlock.Tracker"/>, whenever they are freed. A test that
	/// asserts on live blocks reads its own tracker, so blocks of other tests (a decode finishing late, a finalizer) never change its count.
	/// </summary>
	public sealed class PixelBlockTracker
	{
		private long live;

		/// <summary>Blocks made while this tracker was current that are not freed yet.</summary>
		public long Live => Interlocked.Read(ref live);

		internal void Add(long delta) => Interlocked.Add(ref live, delta);
	}
}
