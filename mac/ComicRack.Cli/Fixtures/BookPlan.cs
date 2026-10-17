using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;

namespace ComicRack.Cli.Fixtures
{
	/// <summary>One CBZ to write: its path (without extension handling, exactly as given) and its pages.</summary>
	internal sealed class BookPlan
	{
		public string Path;
		public List<PageSpec> Pages = new List<PageSpec>();
	}

	/// <summary>A book as the ComicDb.xml describes it (only what the stand-in needs).</summary>
	internal sealed class DbBook
	{
		public string File;
		public string Series = "";
		public string Number = "";
		public string Title = "";
		public int PageCount;
		public List<(int Width, int Height)> PageSizes = new List<(int, int)>();
	}

	internal static class BookPlanner
	{
		/// <summary>Books for `fixtures make`: series of 5, pages of 2 to 6 MP, JPEG/PNG/WebP in turn.</summary>
		public static List<BookPlan> ForMake(string dir, int books, int pages, int seed)
		{
			var plans = new List<BookPlan>();
			for (int b = 0; b < books; b++)
			{
				int series = b / 5 + 1;
				int number = b % 5 + 1;
				string seriesName = "Fixture Series " + series.ToString("00");
				string title = "Stand-in volume " + number;
				var plan = new BookPlan { Path = System.IO.Path.Combine(dir, seriesName + " " + number.ToString("000") + ".cbz") };
				var rnd = new Random(seed * 100003 + b);
				for (int p = 0; p < pages; p++)
				{
					double megapixels = 2.0 + 4.0 * rnd.NextDouble();
					double aspect = 0.62 + 0.10 * rnd.NextDouble();
					int w = (int)Math.Round(Math.Sqrt(megapixels * 1e6 * aspect));
					int h = (int)Math.Round(w / aspect);
					var format = (PageFormat)((b + p) % 3);
					plan.Pages.Add(new PageSpec
					{
						Width = w,
						Height = h,
						Format = format,
						Quality = format == PageFormat.Png ? 100 : 80,
						Seed = seed * 7919 + b * 131 + p,
						Series = seriesName,
						Number = number.ToString(),
						Title = title,
						Index = p,
						Total = pages,
					});
				}
				plans.Add(plan);
			}
			return plans;
		}

		/// <summary>Streams the books of a ComicDb.xml (`Books/Book`), at most <paramref name="max"/> of them (0 = all).</summary>
		public static List<DbBook> ReadDb(string xmlFile, int max)
		{
			var books = new List<DbBook>();
			var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, IgnoreWhitespace = true };
			using (var reader = XmlReader.Create(xmlFile, settings))
			{
				while (reader.Read())
				{
					if (reader.NodeType == XmlNodeType.Element && reader.Name == "Book" && reader.Depth == 2)
					{
						using (var sub = reader.ReadSubtree())
						{
							books.Add(ReadBook(sub, reader.GetAttribute("File")));
						}
						if (max > 0 && books.Count >= max)
						{
							break;
						}
					}
				}
			}
			return books;
		}

		private static DbBook ReadBook(XmlReader r, string file)
		{
			var book = new DbBook { File = file ?? "" };
			r.Read(); // the Book element itself
			r.Read();
			while (!r.EOF)
			{
				if (r.NodeType == XmlNodeType.Element && r.Depth == 1 && r.Name != "Pages")
				{
					string name = r.Name;
					string value = r.ReadElementContentAsString();
					if (name == "Series") book.Series = value;
					else if (name == "Number") book.Number = value;
					else if (name == "Title") book.Title = value;
					else if (name == "PageCount") int.TryParse(value, out book.PageCount);
					continue;
				}
				if (r.NodeType == XmlNodeType.Element && r.Depth == 2 && r.Name == "Page")
				{
					int.TryParse(r.GetAttribute("ImageWidth"), out int w);
					int.TryParse(r.GetAttribute("ImageHeight"), out int h);
					book.PageSizes.Add((w, h));
				}
				r.Read();
			}
			if (book.PageCount == 0)
			{
				book.PageCount = book.PageSizes.Count;
			}
			return book;
		}

		/// <summary>Maps a Windows path from the DB below <paramref name="root"/>: the prefix is removed, anything else lands in `_other`.</summary>
		public static string Remap(string dbPath, string root, string stripPrefix)
		{
			string p = dbPath.Replace('/', '\\');
			string sub;
			if (!string.IsNullOrEmpty(stripPrefix) && p.StartsWith(stripPrefix.Replace('/', '\\'), StringComparison.OrdinalIgnoreCase))
			{
				sub = p.Substring(stripPrefix.Length);
			}
			else
			{
				sub = "_other\\" + (p.Length > 2 && p[1] == ':' ? p.Substring(2) : p);
			}
			string[] parts = sub.Split('\\').Where(s => s.Length > 0 && s != "." && s != "..").ToArray();
			return System.IO.Path.Combine(new[] { root }.Concat(parts).ToArray());
		}

		/// <summary>Books for `fixtures from-db`: pages scaled to fit 800 px, the aspect of the DB size, JPEG quality 70.</summary>
		public static List<BookPlan> ForDb(List<DbBook> books, string root, string stripPrefix, int pages)
		{
			var plans = new List<BookPlan>();
			var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			int ordinal = 0;
			foreach (DbBook book in books)
			{
				ordinal++;
				if (book.File.Length == 0)
				{
					continue;
				}
				string path = Remap(book.File, root, stripPrefix);
				if (!seen.Add(path))
				{
					continue; // the same file listed twice
				}
				var plan = new BookPlan { Path = path };
				int count = Math.Max(1, Math.Min(pages, book.PageCount > 0 ? book.PageCount : 1));
				for (int p = 0; p < count; p++)
				{
					(int w, int h) = p < book.PageSizes.Count ? book.PageSizes[p] : (0, 0);
					if (w <= 0 || h <= 0)
					{
						(w, h) = (1200, 1700);
					}
					double scale = 800.0 / Math.Max(w, h);
					plan.Pages.Add(new PageSpec
					{
						Width = Math.Max(1, (int)Math.Round(w * scale)),
						Height = Math.Max(1, (int)Math.Round(h * scale)),
						Format = PageFormat.Jpeg,
						Quality = 70,
						Seed = StableHash(book.Series + "|" + book.Number) + p,
						Series = book.Series,
						Number = book.Number,
						Title = book.Title,
						Index = p,
						Total = book.PageCount > 0 ? book.PageCount : count,
					});
				}
				plans.Add(plan);
			}
			return plans;
		}

		private static int StableHash(string s)
		{
			unchecked
			{
				int h = 17;
				foreach (char c in s)
				{
					h = h * 31 + c;
				}
				return h & 0x7fffffff;
			}
		}
	}
}
