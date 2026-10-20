using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using ComicRack.Cli.Fixtures;
using ComicRack.Mac.Host;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Engine.Database;
using cYo.Common.Drawing;
using cYo.Projects.ComicRack.Engine.IO;
using cYo.Projects.ComicRack.Engine.IO.Cache;
using cYo.Projects.ComicRack.Engine.IO.Provider;
using Xunit;
using Xunit.Abstractions;

namespace ComicRack.Characterization
{
	/// <summary>
	/// Archives, pages and thumbnails (P0.7b): for a small set of books generated at test time (CBZ with JPEG, PNG and WebP pages, the same pages repacked as CB7
	/// with SharpCompress, a PDF) the page list, the SHA-256 of every page's bytes, the size and a 64-bit average hash of every decoded page and of every
	/// thumbnail. The golden is <c>Golden/Archives.txt</c>. Samples of the owner in <c>mac/fixtures/owner</c> (CBR, real 7-Zip CB7) get a golden of their own
	/// when one exists (<c>Golden/Owner.&lt;file&gt;.txt</c>, written with UPDATE_GOLDENS=1) and a plausibility check otherwise.
	/// </summary>
	public class ArchiveGoldenTests : IClassFixture<ArchiveGoldenTests.GeneratedSet>
	{
		private const string Golden = "Archives.txt";
		private const int Books = 2;
		private const int PagesPerBook = 4;
		private const int PdfPages = 3;

		/// <summary>Perceptual hashes may differ in this many bits (a different resampling of the same picture); byte hashes must be equal.</summary>
		private const int HashTolerance = 3;

		/// <summary>The books, written once into a temp folder with a fixed seed (the generator is deterministic for a SkiaSharp version).</summary>
		public sealed class GeneratedSet : IDisposable
		{
			private readonly TempFolder temp = new TempFolder();

			public string Cbz1 => Path.Combine(temp.Path, "set", "Fixture Series 01 001.cbz");

			public string Cbz2 => Path.Combine(temp.Path, "set", "Fixture Series 01 002.cbz");

			public string Cb7 => Path.Combine(temp.Path, "set", "Fixture Series 01 001.cb7");

			public string Pdf => Path.Combine(temp.Path, "set", "Fixture Series 01 003.pdf");

			public GeneratedSet()
			{
				var env = new FixtureEnvironment { FixturesRoot = temp.Path, PrintDiskFree = false, SevenZip = null };
				string dir = Path.Combine(temp.Path, "set");
				Run(env, "make", dir, "--books", Books.ToString(CultureInfo.InvariantCulture), "--pages", PagesPerBook.ToString(CultureInfo.InvariantCulture), "--seed", "7", "--formats", "cbz");
				Run(env, "cb7", Cbz1, Cb7);
				Run(env, "pdf", Pdf, "--pages", PdfPages.ToString(CultureInfo.InvariantCulture));
			}

			private static void Run(FixtureEnvironment env, params string[] args)
			{
				var output = new StringWriter();
				int code = FixtureCommands.Run(args, output, output, env);
				Assert.True(code == 0, "crcli fixtures " + string.Join(" ", args) + " exited with " + code + "\n" + output);
			}

			public void Dispose() => temp.Dispose();
		}

		private readonly GeneratedSet set;
		private readonly ITestOutputHelper output;

		public ArchiveGoldenTests(GeneratedSet set, ITestOutputHelper output)
		{
			this.set = set;
			this.output = output;
		}

		/// <summary>What the Core makes of one book file.</summary>
		private sealed class BookReport
		{
			public string Name;
			public string Format;
			public int PageCount;
			public int ProviderCount;
			public bool HasByteHashes;
			public List<PageReport> Pages = new List<PageReport>();
		}

		private sealed class PageReport
		{
			public string EntryName;
			public long EntrySize;
			public string Sha;
			public Size Size;
			public string Hash;
			public Size ThumbSize;
			public string ThumbHash;
		}

		private static BookReport Describe(MacHost host, string file, bool byteHashes)
		{
			ComicBook book = host.DatabaseManager.BookFactory.Create(file, CreateBookOption.DoNotAdd, RefreshInfoOptions.GetPageCount);
			Assert.NotNull(book);
			book.RefreshInfoFromFile(RefreshInfoOptions.GetPageCount);
			var report = new BookReport { Name = Path.GetFileName(file), Format = book.FileFormat.ToString(), PageCount = book.PageCount, HasByteHashes = byteHashes };
			using (IImageProvider provider = book.OpenProvider())
			{
				Assert.NotNull(provider);
				report.ProviderCount = provider.Count;
				for (int i = 0; i < provider.Count; i++)
				{
					var page = new PageReport();
					ProviderImageInfo info = provider.GetImageInfo(i);
					page.EntryName = info?.Name ?? "";
					page.EntrySize = info?.Size ?? 0;
					if (byteHashes)
					{
						page.Sha = Convert.ToHexString(SHA256.HashData(provider.GetByteImage(i))).Substring(0, 16).ToLowerInvariant();
					}
					PageKey pageKey = book.GetPageKey(i, BitmapAdjustment.Empty);
					using (var item = host.ImagePool.GetPage(pageKey, provider, onErrorThrowException: true))
					{
						Bitmap bmp = item.Item.Bitmap;
						page.Size = bmp.Size;
						page.Hash = AverageHash.Of(bmp);
					}
					ThumbnailKey thumbKey = book.GetThumbnailKey(i);
					using (var item = host.ImagePool.GetThumbnail(thumbKey, provider, onErrorThrowException: true))
					{
						Bitmap bmp = item.Item.Bitmap;
						page.ThumbSize = bmp.Size;
						page.ThumbHash = AverageHash.Of(bmp);
					}
					report.Pages.Add(page);
				}
			}
			return report;
		}

		private static string Format(IEnumerable<BookReport> reports)
		{
			var sb = new StringBuilder();
			sb.Append("# book, format, page count; then per page: entry name, entry size, sha:<first 16 hex of SHA-256 of the page bytes> (- for PDF), decoded size, ah:<64-bit average hash>, thumbnail size, ah:<thumbnail hash>\n");
			foreach (BookReport r in reports)
			{
				sb.Append(r.Name).Append('\t').Append(r.Format).Append('\t').Append(r.PageCount).Append(" pages (provider ").Append(r.ProviderCount).Append(")\n");
				for (int i = 0; i < r.Pages.Count; i++)
				{
					PageReport p = r.Pages[i];
					sb.Append('\t').Append(i + 1).Append('\t').Append(p.EntryName).Append('\t').Append(p.EntrySize.ToString(CultureInfo.InvariantCulture))
						.Append('\t').Append(p.Sha == null ? "sha:-" : "sha:" + p.Sha)
						.Append('\t').Append(p.Size.Width).Append('x').Append(p.Size.Height).Append("\tah:").Append(p.Hash)
						.Append('\t').Append(p.ThumbSize.Width).Append('x').Append(p.ThumbSize.Height).Append("\tah:").Append(p.ThumbHash).Append('\n');
				}
			}
			return sb.ToString();
		}

		private static MacHost Open(TempFolder temp)
		{
			return MacHost.Open(new MacHostOptions { Paths = temp.Paths, DatabaseReadOnly = true });
		}

		private List<BookReport> DescribeSet(MacHost host)
		{
			return new List<BookReport>
			{
				Describe(host, set.Cbz1, byteHashes: true),
				Describe(host, set.Cbz2, byteHashes: true),
				Describe(host, set.Cb7, byteHashes: true),
				Describe(host, set.Pdf, byteHashes: false),
			};
		}

		[Fact]
		public void Pages_bytes_and_thumbnails_of_the_generated_books_match_the_golden()
		{
			using (var temp = new TempFolder())
			using (MacHost host = Open(temp))
			{
				List<BookReport> reports = DescribeSet(host);
				PerceptualGolden.Check(Golden, Format(reports), HashTolerance, output.WriteLine);
			}
		}

		[Fact]
		public void The_page_lists_have_the_expected_shape()
		{
			using (var temp = new TempFolder())
			using (MacHost host = Open(temp))
			{
				BookReport cbz = Describe(host, set.Cbz1, byteHashes: true);
				Assert.Equal(PagesPerBook, cbz.PageCount);
				Assert.Equal(PagesPerBook, cbz.ProviderCount);
				Assert.Equal(PagesPerBook, cbz.Pages.Count);
				Assert.Equal(cbz.Pages.Select(p => p.EntryName).OrderBy(n => n, StringComparer.Ordinal), cbz.Pages.Select(p => p.EntryName));
				Assert.All(cbz.Pages, p => Assert.True(p.Size.Width > 1000 && p.Size.Height > 1000, p.EntryName + " " + p.Size));
				Assert.All(cbz.Pages, p => Assert.True(p.ThumbSize.Width > 0 && p.ThumbSize.Height > 0 && p.ThumbSize.Height < p.Size.Height));
				// the generator mixes JPEG, PNG and WebP pages: all three decoded
				Assert.Contains(cbz.Pages, p => p.EntryName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase));
				Assert.Contains(cbz.Pages, p => p.EntryName.EndsWith(".png", StringComparison.OrdinalIgnoreCase));
				Assert.Contains(cbz.Pages, p => p.EntryName.EndsWith(".webp", StringComparison.OrdinalIgnoreCase));

				BookReport pdf = Describe(host, set.Pdf, byteHashes: false);
				Assert.Equal(PdfPages, pdf.PageCount);
				Assert.Equal(PdfPages, pdf.Pages.Count);
				Assert.All(pdf.Pages, p => Assert.True(p.Size.Width > 100 && p.Size.Height > 100));
			}
		}

		[Fact]
		public void The_same_pages_in_a_CB7_have_the_same_bytes_and_pictures_as_in_the_CBZ()
		{
			using (var temp = new TempFolder())
			using (MacHost host = Open(temp))
			{
				BookReport cbz = Describe(host, set.Cbz1, byteHashes: true);
				BookReport cb7 = Describe(host, set.Cb7, byteHashes: true);
				Assert.NotEqual(cbz.Format, cb7.Format);
				Assert.Equal(cbz.PageCount, cb7.PageCount);
				for (int i = 0; i < cbz.Pages.Count; i++)
				{
					Assert.Equal(cbz.Pages[i].EntryName, cb7.Pages[i].EntryName);
					Assert.Equal(cbz.Pages[i].Sha, cb7.Pages[i].Sha);
					Assert.Equal(cbz.Pages[i].Size, cb7.Pages[i].Size);
					Assert.Equal(cbz.Pages[i].Hash, cb7.Pages[i].Hash);
					Assert.Equal(cbz.Pages[i].ThumbHash, cb7.Pages[i].ThumbHash);
				}
			}
		}

		[Fact]
		public void Pages_of_different_books_and_different_pages_do_not_share_hashes()
		{
			using (var temp = new TempFolder())
			using (MacHost host = Open(temp))
			{
				BookReport a = Describe(host, set.Cbz1, byteHashes: true);
				BookReport b = Describe(host, set.Cbz2, byteHashes: true);
				Assert.Equal(PagesPerBook * 2, a.Pages.Concat(b.Pages).Select(p => p.Sha).Distinct().Count());
				// the picture hashes tell the pages apart too (not all of them: two stand-in pages may look alike at 8 x 8)
				Assert.True(a.Pages.Concat(b.Pages).Select(p => p.Hash).Distinct().Count() >= PagesPerBook, "the average hashes are all alike");
			}
		}

		[Fact]
		public void Describing_a_book_twice_in_two_hosts_gives_the_same_report()
		{
			string first;
			using (var temp = new TempFolder())
			using (MacHost host = Open(temp))
			{
				first = Format(new[] { Describe(host, set.Cbz2, byteHashes: true) });
			}
			using (var temp = new TempFolder())
			using (MacHost host = Open(temp))
			{
				Assert.Equal(first, Format(new[] { Describe(host, set.Cbz2, byteHashes: true) }));
			}
		}

		// ---- the owner's samples (C1): real CBR and real 7-Zip CB7 ----

		private static string OwnerFolder => Path.Combine(Goldens.RepoRoot, "mac", "fixtures", "owner");

		private static string[] OwnerSamples()
		{
			if (!Directory.Exists(OwnerFolder))
			{
				return new string[0];
			}
			return Directory.EnumerateFiles(OwnerFolder)
				.Where(f => f.EndsWith(".cbr", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".cb7", StringComparison.OrdinalIgnoreCase))
				.OrderBy(f => f, StringComparer.Ordinal)
				.ToArray();
		}

		private sealed class OwnerSamplesFactAttribute : FactAttribute
		{
			public OwnerSamplesFactAttribute()
			{
				if (OwnerSamples().Length == 0)
				{
					Skip = "no mac/fixtures/owner/*.cbr or *.cb7 (samples of the owner, checkpoint C1)";
				}
			}
		}

		[OwnerSamplesFact]
		public void Owner_samples_open_and_match_their_golden_when_there_is_one()
		{
			using (var temp = new TempFolder())
			using (MacHost host = Open(temp))
			{
				foreach (string file in OwnerSamples())
				{
					BookReport r = Describe(host, file, byteHashes: true);
					Assert.True(r.PageCount > 0, file + " has no pages");
					Assert.Equal(r.PageCount, r.ProviderCount);
					Assert.All(r.Pages, p => Assert.True(p.Size.Width > 0 && p.Size.Height > 0 && p.ThumbSize.Width > 0, file + " " + p.EntryName));
					string golden = "Owner." + Path.GetFileName(file) + ".txt";
					if (File.Exists(Goldens.PathOf(golden)) || Goldens.Update)
					{
						PerceptualGolden.Check(golden, Format(new[] { r }), HashTolerance, output.WriteLine);
					}
					else
					{
						output.WriteLine($"{r.Name}: {r.Format}, {r.PageCount} pages; no golden yet (UPDATE_GOLDENS=1 writes Golden/{golden})");
					}
				}
			}
		}
	}

	/// <summary>
	/// A 64-bit average hash: the picture is reduced to 8 x 8 cells (mean luminance of the pixels of each cell), a bit is set for every cell brighter than
	/// the mean of all cells. Pure integer arithmetic on the pixels, so it is the same on every machine for the same pixels.
	/// </summary>
	internal static class AverageHash
	{
		public static string Of(Bitmap bitmap)
		{
			int w = bitmap.Width, h = bitmap.Height;
			BitmapData data = bitmap.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
			byte[] pixels = new byte[Math.Abs(data.Stride) * h];
			try
			{
				Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
			}
			finally
			{
				bitmap.UnlockBits(data);
			}
			return Of(pixels, w, h, Math.Abs(data.Stride));
		}

		/// <summary>B, G, R, A bytes per pixel.</summary>
		public static string Of(byte[] bgra, int width, int height, int stride)
		{
			var cells = new long[64];
			for (int cy = 0; cy < 8; cy++)
			{
				int y0 = cy * height / 8, y1 = (cy + 1) * height / 8;
				for (int cx = 0; cx < 8; cx++)
				{
					int x0 = cx * width / 8, x1 = (cx + 1) * width / 8;
					long sum = 0;
					for (int y = y0; y < y1; y++)
					{
						int row = y * stride;
						for (int x = x0; x < x1; x++)
						{
							int o = row + x * 4;
							sum += (bgra[o + 2] * 299 + bgra[o + 1] * 587 + bgra[o] * 114) / 1000;
						}
					}
					long count = Math.Max(1L, (long)(y1 - y0) * (x1 - x0));
					cells[cy * 8 + cx] = sum * 256 / count; // 8 fractional bits
				}
			}
			long mean = cells.Sum() / 64;
			ulong hash = 0;
			for (int i = 0; i < 64; i++)
			{
				if (cells[i] > mean)
				{
					hash |= 1UL << (63 - i);
				}
			}
			return hash.ToString("x16", CultureInfo.InvariantCulture);
		}

		public static int Distance(string a, string b)
		{
			return System.Numerics.BitOperations.PopCount(Convert.ToUInt64(a, 16) ^ Convert.ToUInt64(b, 16));
		}
	}

	/// <summary>
	/// A golden whose <c>ah:&lt;16 hex&gt;</c> words are average hashes: they may differ from the golden in a few bits (another resampling of the same
	/// picture); everything else must be equal. Updating works as for <see cref="Goldens"/>.
	/// </summary>
	internal static class PerceptualGolden
	{
		public static void Check(string name, string actual, int tolerance, Action<string> log = null)
		{
			actual = Goldens.Normalize(actual);
			string file = Goldens.PathOf(name);
			if (!Goldens.Update && File.Exists(file))
			{
				string expected = Goldens.Read(name);
				string problem = Compare(expected, actual, tolerance);
				if (problem == null)
				{
					return;
				}
				Assert.Fail($"Golden/{name} differs from the actual result (full actual text: {Goldens.SaveActual(name, actual)}); average hashes may differ in up to {tolerance} bits.\n{problem}\n{Goldens.Diff(expected, actual)}");
			}
			Goldens.Check(name, actual, log);
		}

		/// <summary>Null when the texts agree line by line and word by word, hashes within the tolerance.</summary>
		public static string Compare(string expected, string actual, int tolerance)
		{
			string[] a = expected.Split('\n');
			string[] b = actual.Split('\n');
			if (a.Length != b.Length)
			{
				return $"{a.Length} lines expected, {b.Length} found";
			}
			for (int i = 0; i < a.Length; i++)
			{
				if (a[i] == b[i])
				{
					continue;
				}
				string[] wa = a[i].Split('\t');
				string[] wb = b[i].Split('\t');
				if (wa.Length != wb.Length)
				{
					return $"line {i + 1}: {wa.Length} fields expected, {wb.Length} found";
				}
				for (int k = 0; k < wa.Length; k++)
				{
					if (wa[k] == wb[k])
					{
						continue;
					}
					bool hashes = wa[k].StartsWith("ah:", StringComparison.Ordinal) && wb[k].StartsWith("ah:", StringComparison.Ordinal);
					if (!hashes || AverageHash.Distance(wa[k].Substring(3), wb[k].Substring(3)) > tolerance)
					{
						return $"line {i + 1}, field {k + 1}: expected '{wa[k]}', found '{wb[k]}'";
					}
				}
			}
			return null;
		}
	}
}
