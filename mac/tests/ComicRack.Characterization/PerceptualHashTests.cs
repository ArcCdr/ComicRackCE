using System;
using Xunit;

namespace ComicRack.Characterization
{
	/// <summary>The helpers of <see cref="ArchiveGoldenTests"/> on synthetic pixels (no host, no files).</summary>
	public class PerceptualHashTests
	{
		private static byte[] Picture(int w, int h, Func<int, int, byte> luminance)
		{
			var bgra = new byte[w * h * 4];
			for (int y = 0; y < h; y++)
			{
				for (int x = 0; x < w; x++)
				{
					int o = (y * w + x) * 4;
					bgra[o] = bgra[o + 1] = bgra[o + 2] = luminance(x, y);
					bgra[o + 3] = 255;
				}
			}
			return bgra;
		}

		[Fact]
		public void A_picture_with_a_bright_left_half_sets_the_left_four_bits_of_every_row()
		{
			byte[] bgra = Picture(64, 64, (x, y) => (byte)(x < 32 ? 220 : 30));
			Assert.Equal("f0f0f0f0f0f0f0f0", AverageHash.Of(bgra, 64, 64, 64 * 4));
		}

		[Fact]
		public void A_flat_picture_has_no_bright_cells()
		{
			Assert.Equal("0000000000000000", AverageHash.Of(Picture(40, 30, (x, y) => 128), 40, 30, 40 * 4));
		}

		[Fact]
		public void The_hash_does_not_depend_on_the_scale_of_the_picture()
		{
			byte Lum(int x, int y, int size) => (byte)((x * 8 / size + y * 8 / size) % 2 == 0 ? 240 : 20);
			string small = AverageHash.Of(Picture(64, 64, (x, y) => Lum(x, y, 64)), 64, 64, 64 * 4);
			string large = AverageHash.Of(Picture(256, 256, (x, y) => Lum(x, y, 256)), 256, 256, 256 * 4);
			Assert.Equal(small, large);
			Assert.Equal("aa55aa55aa55aa55", small);
		}

		[Fact]
		public void The_distance_counts_the_differing_bits()
		{
			Assert.Equal(0, AverageHash.Distance("0f0e7e7cfc780000", "0f0e7e7cfc780000"));
			Assert.Equal(1, AverageHash.Distance("0000000000000000", "8000000000000000"));
			Assert.Equal(64, AverageHash.Distance("0000000000000000", "ffffffffffffffff"));
		}

		[Fact]
		public void The_golden_comparison_allows_a_few_hash_bits_but_nothing_else()
		{
			const string line = "\t1\t001.jpg\t83488\tsha:fc74a72267f6f02c\t1510x2431\tah:0f0e7e7cfc780000\n";
			Assert.Null(PerceptualGolden.Compare(line, line, 3));
			// 3 bits: accepted, 4 bits: rejected
			Assert.Null(PerceptualGolden.Compare(line, line.Replace("ah:0f0e7e7cfc780000", "ah:0f0e7e7cfc780007"), 3));
			Assert.NotNull(PerceptualGolden.Compare(line, line.Replace("ah:0f0e7e7cfc780000", "ah:0f0e7e7cfc78000f"), 3));
			// a byte hash, a size or a name must be equal
			Assert.NotNull(PerceptualGolden.Compare(line, line.Replace("sha:fc74a72267f6f02c", "sha:fc74a72267f6f02d"), 3));
			Assert.NotNull(PerceptualGolden.Compare(line, line.Replace("1510x2431", "1510x2432"), 3));
			Assert.NotNull(PerceptualGolden.Compare(line, line.Replace("001.jpg", "002.jpg"), 3));
			// a page more or less
			Assert.NotNull(PerceptualGolden.Compare(line, line + line, 3));
		}
	}
}
