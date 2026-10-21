using ComicRack.Mac.Thumbnails;
using SkiaSharp;
using Xunit;

namespace ComicRack.Mac.Tests.Thumbnails
{
	/// <summary>
	/// The live pixel block count of a test belongs to that test: a block of an earlier test that is released late (a decode finishing
	/// on the pool, a finalizer) must not change it. Before the count was per test, such a release made the baseline of the next test
	/// stale ("Expected: 13, Actual: 0" in the fast gate).
	/// </summary>
	public class PixelCountIsolationTests
	{
		private static PixelBlock Block() => PixelBlock.FromBitmap(new SKBitmap(4, 4));

		[Fact]
		public void A_block_of_an_earlier_count_that_is_released_later_does_not_change_the_current_count()
		{
			Gc.BeginCount();
			PixelBlock earlier = Block();
			Assert.Equal(1, Gc.Live);

			Gc.BeginCount(); // the next test starts while the earlier one's pool work still holds its block
			Assert.Equal(0, Gc.Live);
			earlier.Release();
			Assert.Equal(0, Gc.Live);

			PixelBlock mine = Block();
			Assert.Equal(1, Gc.Live);
			mine.Release();
			Assert.Equal(0, Gc.Live);
		}

		[Fact]
		public void A_block_is_counted_until_its_last_reference_is_gone()
		{
			Gc.BeginCount();
			PixelBlock block = Block();
			block.Retain();
			block.Release();
			Assert.Equal(1, Gc.Live);
			block.Release();
			Assert.Equal(0, Gc.Live);
		}
	}
}
