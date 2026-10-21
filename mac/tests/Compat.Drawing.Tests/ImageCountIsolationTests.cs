using System.Drawing;
using Xunit;
using static Compat.Drawing.Tests.TestImages;

namespace Compat.Drawing.Tests
{
	/// <summary>The live image storage count of a test belongs to that test: an image of an earlier test that is disposed or finalized late does not change it.</summary>
	public class ImageCountIsolationTests
	{
		[Fact]
		public void An_image_of_an_earlier_count_that_is_disposed_later_does_not_change_the_current_count()
		{
			StartCounting();
			Bitmap earlier = new Bitmap(4, 4);
			Assert.Equal(1, Live);

			StartCounting(); // the next test starts while the earlier image is still alive
			Assert.Equal(0, Live);
			earlier.Dispose();
			Assert.Equal(0, Live);

			Bitmap mine = new Bitmap(4, 4);
			Assert.Equal(1, Live);
			mine.Dispose();
			Assert.Equal(0, Live);
		}
	}
}
