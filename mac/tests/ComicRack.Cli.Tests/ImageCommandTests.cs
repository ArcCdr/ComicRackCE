using System;
using System.IO;
using SkiaSharp;
using Xunit;

namespace ComicRack.Cli.Tests
{
	/// <summary>`crcli image probe` and `image stats` (P0.6a), run in process on small PNGs written by the test.</summary>
	public class ImageCommandTests : IDisposable
	{
		private readonly string temp = Path.Combine(Path.GetTempPath(), "crcli-image-" + Guid.NewGuid().ToString("N"));

		public ImageCommandTests()
		{
			Directory.CreateDirectory(temp);
		}

		public void Dispose()
		{
			try
			{
				Directory.Delete(temp, recursive: true);
			}
			catch (IOException)
			{
			}
		}

		private string Png(string name, int width, int height, Func<int, int, SKColor> pixel)
		{
			string path = Path.Combine(temp, name);
			using (var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul)))
			{
				for (int y = 0; y < height; y++)
				{
					for (int x = 0; x < width; x++)
					{
						bitmap.SetPixel(x, y, pixel(x, y));
					}
				}
				using (var image = SKImage.FromBitmap(bitmap))
				using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
				using (var stream = File.Create(path))
				{
					data.SaveTo(stream);
				}
			}
			return path;
		}

		private static (int code, string output, string error) Crcli(params string[] args)
		{
			var o = new StringWriter();
			var e = new StringWriter();
			int code = Program.Run(args, o, e);
			return (code, o.ToString(), e.ToString());
		}

		/// <summary>Left half red, right half blue, 10 x 4.</summary>
		private string TwoColours() => Png("two.png", 10, 4, (x, y) => x < 5 ? new SKColor(255, 0, 0) : new SKColor(0, 0, 255));

		[Fact]
		public void Probe_reports_the_colour_at_a_point()
		{
			string file = TwoColours();
			var left = Crcli("image", "probe", file, "1", "2");
			Assert.True(left.code == 0, left.output + left.error);
			Assert.Contains("rgba: 255,0,0,255", left.output);
			Assert.Contains("hex: #FF0000", left.output);
			var right = Crcli("image", "probe", file, "9", "0");
			Assert.Contains("rgba: 0,0,255,255", right.output);
		}

		[Fact]
		public void Probe_box_averages_the_pixels_around_the_point()
		{
			string file = TwoColours();
			// A 2 x 2 box at x = 5 covers x 5..6 (it starts (box - 1) / 2 = 0 pixels left of the point): all blue.
			Assert.Contains("rgba: 0,0,255,255", Crcli("image", "probe", file, "5", "1", "--box", "2").output);
			// A 3 x 3 box at x = 5 covers x 4..6: one red column and two blue ones.
			var mixed = Crcli("image", "probe", file, "5", "1", "--box", "3");
			Assert.Equal(0, mixed.code);
			Assert.Contains("rgba: 85,0,170,255", mixed.output);
			// A box that reaches over the edge is clipped and says so: 3 x 3 around (0, 0) leaves 2 x 2 pixels.
			Assert.Contains("(2x2 pixels used)", Crcli("image", "probe", file, "0", "0", "--box", "3").output);
		}

		[Fact]
		public void Probe_keeps_the_alpha_of_the_file()
		{
			string file = Png("alpha.png", 2, 2, (x, y) => new SKColor(10, 20, 30, 128));
			var r = Crcli("image", "probe", file, "0", "0");
			Assert.Contains("rgba: 10,20,30,128", r.output);
			Assert.Contains("hex: #0A141E80", r.output);
		}

		[Fact]
		public void Probe_rejects_a_point_outside_the_image_and_bad_numbers()
		{
			string file = TwoColours();
			Assert.Equal(2, Crcli("image", "probe", file, "10", "0").code);
			Assert.Equal(2, Crcli("image", "probe", file, "0", "4").code);
			Assert.Equal(2, Crcli("image", "probe", file, "-1", "0").code);
			Assert.Equal(2, Crcli("image", "probe", file, "x", "0").code);
			Assert.Equal(2, Crcli("image", "probe", file, "0", "0", "--box", "0").code);
			Assert.Equal(2, Crcli("image", "probe", file, "0").code);
			Assert.Equal(2, Crcli("image", "probe", Path.Combine(temp, "missing.png"), "0", "0").code);
		}

		[Fact]
		public void Stats_reports_mean_min_max_and_distinct_colours()
		{
			var r = Crcli("image", "stats", TwoColours());
			Assert.True(r.code == 0, r.output + r.error);
			Assert.Contains("size: 10x4", r.output);
			Assert.Contains("R: mean 127.50 min 0 max 255", r.output);
			Assert.Contains("G: mean 0.00 min 0 max 0", r.output);
			Assert.Contains("B: mean 127.50 min 0 max 255", r.output);
			Assert.Contains("A: mean 255.00 min 255 max 255", r.output);
			Assert.Contains("distinct colours: 2", r.output);
			Assert.Contains("blank: no", r.output);
		}

		[Fact]
		public void Stats_detects_a_blank_image_and_require_nonblank_fails_on_it()
		{
			string blank = Png("blank.png", 8, 8, (x, y) => new SKColor(200, 200, 200));
			var plain = Crcli("image", "stats", blank);
			Assert.Equal(0, plain.code);
			Assert.Contains("blank: yes", plain.output);
			Assert.Contains("distinct colours: 1", plain.output);
			var strict = Crcli("image", "stats", blank, "--require-nonblank");
			Assert.Equal(1, strict.code);
			Assert.Contains("blank", strict.error);
			Assert.Equal(0, Crcli("image", "stats", TwoColours(), "--require-nonblank").code);
		}

		[Fact]
		public void Blank_means_low_variance_not_identical_pixels()
		{
			// A flat colour with noise of one level (what a compressed or dithered empty window looks like) is blank.
			string noisy = Png("noisy.png", 40, 40, (x, y) => new SKColor((byte)(200 + ((x + y) % 2)), 200, (byte)(200 - (x % 2))));
			var noisyStats = Crcli("image", "stats", noisy);
			Assert.Contains("blank: yes", noisyStats.output);
			Assert.True(noisyStats.output.Contains("distinct colours: 4") || noisyStats.output.Contains("distinct colours: 3"), noisyStats.output);
			Assert.Equal(1, Crcli("image", "stats", noisy, "--require-nonblank").code);

			// A flat window with a small high-contrast element (about 1 percent of the pixels) is not blank.
			string small = Png("small.png", 100, 100, (x, y) => x < 10 && y < 10 ? new SKColor(0, 0, 0) : new SKColor(240, 240, 240));
			var smallStats = Crcli("image", "stats", small);
			Assert.Contains("blank: no", smallStats.output);
			Assert.Equal(0, Crcli("image", "stats", small, "--require-nonblank").code);
		}

		[Fact]
		public void Image_without_a_valid_subcommand_is_a_usage_error()
		{
			Assert.Equal(2, Crcli("image").code);
			Assert.Equal(2, Crcli("image", "bogus").code);
			Assert.Equal(2, Crcli("image", "stats").code);
		}
	}
}
