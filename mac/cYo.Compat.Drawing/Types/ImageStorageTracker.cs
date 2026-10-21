using System.Threading;

namespace System.Drawing
{
	/// <summary>
	/// Counts the bitmap storages of the images made while it is <c>Image.StorageTracker</c>, whenever they are released. A test that
	/// asserts on live storages reads its own tracker, so images of other tests (a finalizer running late) never change its count.
	/// </summary>
	internal sealed class ImageStorageTracker
	{
		private int live;

		public int Live => Volatile.Read(ref live);

		public void Add(int delta) => Interlocked.Add(ref live, delta);
	}
}
