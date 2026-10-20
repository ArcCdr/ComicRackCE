using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using ComicRack.Cli.Fixtures;
using SkiaSharp;

namespace ComicRack.Cli.Core
{
	/// <summary>
	/// `crcli image probe` and `crcli image stats` (P0.6a): read a PNG (or any Skia-decodable image) and report a pixel colour or
	/// channel statistics. mac/tools/ui-drive uses them to check screenshots (non-blank, expected colour at a point).
	/// `crcli image sheet` (P5.T3, <see cref="ImageSheet"/>) puts screenshots and reference cards into one captioned picture.
	/// </summary>
	public static partial class ImageCommands
	{
		private const string ProbeUsage = "image probe <png> <x> <y> [--box N]";
		private const string StatsUsage = "image stats <png> [--require-nonblank]";
		private const int DistinctCap = 65536;

		/// <summary>An image whose R, G and B channels all have a standard deviation below this (of 255) counts as blank.</summary>
		private const double BlankDeviation = 2.0;

		public static int Run(string[] args, TextWriter output, TextWriter error)
		{
			if (args.Length == 0 || (args[0] != "probe" && args[0] != "stats" && args[0] != "sheet"))
			{
				throw new UsageException("image: expected `probe <png> <x> <y> [--box N]`, `stats <png>` or `sheet --out <png> --cols N <image-or-card.json>...`.");
			}
			var o = Options.Parse(args[1..]);
			if (args[0] == "sheet")
			{
				return ImageSheet.Run(o, output);
			}
			return args[0] == "probe" ? Probe(o, output) : Stats(o, output, error);
		}

		/// <summary>`image probe <png> <x> <y> [--box N]`: the colour at (x, y), top left origin, in pixels. With --box N the mean over the N x N pixels around the point (clipped at the edges).</summary>
		private static int Probe(Options o, TextWriter output)
		{
			string file = o.Positional(0, ProbeUsage);
			int x = ParseCoordinate(o.Positional(1, ProbeUsage), "x");
			int y = ParseCoordinate(o.Positional(2, ProbeUsage), "y");
			int box = o.Int("box", 1);
			if (box < 1 || box > 1000)
			{
				throw new UsageException("--box must be between 1 and 1000.");
			}
			using (SKBitmap bitmap = Load(file))
			{
				if (x < 0 || y < 0 || x >= bitmap.Width || y >= bitmap.Height)
				{
					throw new UsageException("The point " + x + "," + y + " is outside the image (" + bitmap.Width + "x" + bitmap.Height + ").");
				}
				int left0 = x - (box - 1) / 2;
				int top0 = y - (box - 1) / 2;
				int left = Math.Max(0, left0);
				int top = Math.Max(0, top0);
				int right = Math.Min(bitmap.Width, left0 + box);
				int bottom = Math.Min(bitmap.Height, top0 + box);
				double[] sum = new double[4];
				int count = 0;
				for (int py = top; py < bottom; py++)
				{
					for (int px = left; px < right; px++)
					{
						SKColor c = bitmap.GetPixel(px, py);
						sum[0] += c.Red;
						sum[1] += c.Green;
						sum[2] += c.Blue;
						sum[3] += c.Alpha;
						count++;
					}
				}
				int r = (int)Math.Round(sum[0] / count);
				int g = (int)Math.Round(sum[1] / count);
				int b = (int)Math.Round(sum[2] / count);
				int a = (int)Math.Round(sum[3] / count);
				output.WriteLine("probe: " + file);
				output.WriteLine("  point: " + x + "," + y + " box " + box + " (" + (right - left) + "x" + (bottom - top) + " pixels used)");
				output.WriteLine("  rgba: " + r + "," + g + "," + b + "," + a);
				output.WriteLine("  hex: #" + r.ToString("X2") + g.ToString("X2") + b.ToString("X2") + (a == 255 ? "" : a.ToString("X2")));
			}
			return 0;
		}

		/// <summary>`image stats <png>`: mean, min and max of R, G, B, A; the number of distinct colours (counted up to 65536); whether the image is blank: the largest standard deviation of the R, G and B channels is below <see cref="BlankDeviation"/> (a flat colour, or a flat colour with compression or dithering noise). --require-nonblank turns a blank image into exit code 1.</summary>
		private static int Stats(Options o, TextWriter output, TextWriter error)
		{
			string file = o.Positional(0, StatsUsage);
			using (SKBitmap bitmap = Load(file))
			{
				string[] names = { "R", "G", "B", "A" };
				double[] sum = new double[4];
				double[] sumSquares = new double[4];
				int[] min = { 255, 255, 255, 255 };
				int[] max = { 0, 0, 0, 0 };
				var distinct = new HashSet<uint>();
				uint first = 0;
				for (int y = 0; y < bitmap.Height; y++)
				{
					for (int x = 0; x < bitmap.Width; x++)
					{
						SKColor c = bitmap.GetPixel(x, y);
						int[] v = { c.Red, c.Green, c.Blue, c.Alpha };
						for (int i = 0; i < 4; i++)
						{
							sum[i] += v[i];
							sumSquares[i] += (double)v[i] * v[i];
							if (v[i] < min[i])
							{
								min[i] = v[i];
							}
							if (v[i] > max[i])
							{
								max[i] = v[i];
							}
						}
						uint packed = (uint)c;
						if (x == 0 && y == 0)
						{
							first = packed;
						}
						if (distinct.Count < DistinctCap)
						{
							distinct.Add(packed);
						}
					}
				}
				double pixels = (double)bitmap.Width * bitmap.Height;
				double deviation = 0;
				for (int i = 0; i < 3; i++)
				{
					double mean = sum[i] / pixels;
					deviation = Math.Max(deviation, Math.Sqrt(Math.Max(0, sumSquares[i] / pixels - mean * mean)));
				}
				bool blank = deviation < BlankDeviation;
				output.WriteLine("stats: " + file);
				output.WriteLine("  size: " + bitmap.Width + "x" + bitmap.Height);
				for (int i = 0; i < 4; i++)
				{
					output.WriteLine("  " + names[i] + ": mean " + (sum[i] / pixels).ToString("F2", CultureInfo.InvariantCulture) + " min " + min[i] + " max " + max[i]);
				}
				output.WriteLine("  distinct colours: " + distinct.Count + (distinct.Count >= DistinctCap ? "+" : ""));
				output.WriteLine("  deviation: " + deviation.ToString("F2", CultureInfo.InvariantCulture) + " (largest standard deviation of R, G, B)");
				output.WriteLine("  blank: " + (blank ? "yes" : "no"));
				if (blank && o.Flag("require-nonblank"))
				{
					error.WriteLine("The image is blank (largest channel deviation " + deviation.ToString("F2", CultureInfo.InvariantCulture) + ", first pixel #" + (first & 0xFFFFFF).ToString("X6") + ").");
					return 1;
				}
			}
			return 0;
		}

		private static int ParseCoordinate(string text, string name)
		{
			if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int value))
			{
				throw new UsageException(name + " expects a non-negative whole number of pixels, got " + text);
			}
			return value;
		}

		/// <summary>Decodes to non-premultiplied RGBA so GetPixel returns the stored colour.</summary>
		private static SKBitmap Load(string file)
		{
			if (!File.Exists(file))
			{
				throw new UsageException("Image not found: " + file);
			}
			SKBitmap bitmap = null;
			using (SKCodec codec = SKCodec.Create(file))
			{
				if (codec != null)
				{
					bitmap = SKBitmap.Decode(codec, codec.Info.WithColorType(SKColorType.Rgba8888).WithAlphaType(SKAlphaType.Unpremul));
				}
			}
			if (bitmap == null)
			{
				throw new UsageException("Cannot decode the image: " + file);
			}
			return bitmap;
		}
	}
}
