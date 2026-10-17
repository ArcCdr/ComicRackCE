using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using ComicRack.Cli.Fixtures;
using ComicRack.Mac.Host;
using cYo.Common.Collections;
using cYo.Common.Drawing;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Engine.Database;
using cYo.Projects.ComicRack.Engine.IO;
using cYo.Projects.ComicRack.Engine.IO.Cache;
using cYo.Projects.ComicRack.Engine.IO.Provider;

namespace ComicRack.Cli.Core
{
	/// <summary>`crcli db stats`, `scan`, `page` and `thumb` (M1.5b): the Core commands, on top of ComicRack.Mac.Host.</summary>
	public static class CoreCommands
	{
		public static int Db(string[] args, TextWriter output, TextWriter error)
		{
			if (args.Length == 0 || args[0] != "stats")
			{
				throw new UsageException("db: expected `stats <ComicDb.xml> [--remap \"from=to\"]`.");
			}
			var o = Options.Parse(args.Skip(1).ToArray());
			string db = Path.GetFullPath(o.Positional(0, "db stats <ComicDb.xml> [--remap \"S:\\media\\BD\\=/path/\"]"));
			if (!File.Exists(db))
			{
				throw new UsageException("DB not found: " + db);
			}
			PathRemap remap = PathRemap.Parse(o.Get("remap"));
			using (CliSession session = CliSession.Create(o))
			{
				var sw = Stopwatch.StartNew();
				// Read only: the file is never written, and nothing is saved on close.
				using (MacHost host = MacHost.Open(new MacHostOptions { Paths = session.Paths, DatabaseFile = db, DatabaseReadOnly = true }))
				{
					long loadMs = sw.ElapsedMilliseconds;
					ComicDatabase database = host.DatabaseManager.Database;
					int books = database.Books.Count;
					int lists = database.ComicLists.GetItems<ComicListItem>().Count();
					int topLists = database.ComicLists.Count;
					RemapResult r = null;
					long remapMs = 0;
					if (remap != null)
					{
						sw.Restart();
						r = remap.Apply(database);
						remapMs = sw.ElapsedMilliseconds;
					}
					GC.Collect();
					GC.WaitForPendingFinalizers();
					GC.Collect();
					Process p = Process.GetCurrentProcess();
					p.Refresh();
					output.WriteLine("db stats: " + db);
					output.WriteLine("  file size: " + Mb(new FileInfo(db).Length) + " MB");
					output.WriteLine("  load time: " + loadMs + " ms (MacHost.Open, includes the XML load and the Core services)");
					output.WriteLine("  books: " + books);
					output.WriteLine("  lists: " + lists + " (" + topLists + " at top level)");
					output.WriteLine("  watch folders: " + database.WatchFolders.Count + (database.WatchFolders.Count > 0 ? " (" + string.Join("; ", database.WatchFolders.Select(w => w.Folder)) + ")" : ""));
					if (host.OpenMessage != null)
					{
						output.WriteLine("  open message: " + host.OpenMessage);
					}
					if (r != null)
					{
						output.WriteLine("  remap: " + remap + " in " + remapMs + " ms");
						output.WriteLine("    books remapped: " + r.BooksRemapped + " of " + books + ", of which the file exists: " + r.BooksFound);
						output.WriteLine("    watch folders remapped: " + r.WatchFoldersRemapped + " of " + database.WatchFolders.Count + (database.WatchFolders.Count > 0 ? " (" + string.Join("; ", database.WatchFolders.Select(w => w.Folder)) + ")" : ""));
					}
					output.WriteLine("  memory: working set " + Mb(p.WorkingSet64) + " MB (RSS), peak " + Mb(PeakRss()) + " MB, managed heap " + Mb(GC.GetTotalMemory(false)) + " MB");
					output.WriteLine("  data folder: " + session.Paths.DataFolder + (session.UsesAppFolders ? " (app folders)" : ""));
				}
			}
			return 0;
		}

		public static int Scan(string[] args, TextWriter output, TextWriter error)
		{
			var o = Options.Parse(args);
			string folder = Path.GetFullPath(o.Positional(0, "scan <folder> --db <out.xml>"));
			string outDb = o.Get("db");
			if (outDb == null)
			{
				throw new UsageException("scan: --db <out.xml> is required.");
			}
			outDb = Path.GetFullPath(outDb);
			if (!Directory.Exists(folder))
			{
				throw new UsageException("Folder not found: " + folder);
			}
			if (!outDb.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
			{
				throw new UsageException("scan: --db must end with .xml");
			}
			Directory.CreateDirectory(Path.GetDirectoryName(outDb));
			using (CliSession session = CliSession.Create(o))
			{
				var stamps = new List<(string file, long ticks)>();
				var total = Stopwatch.StartNew();
				using (MacHost host = MacHost.Open(new MacHostOptions { Paths = session.Paths, DatabaseFile = outDb, DatabaseBackgroundSaveSeconds = 0 }))
				{
					long openMs = total.ElapsedMilliseconds;
					ComicScanner scanner = host.QueueManager.Scanner;
					scanner.ScanNotify += (s, e) =>
					{
						lock (stamps)
						{
							stamps.Add((e.File, Stopwatch.GetTimestamp()));
						}
					};
					long start = Stopwatch.GetTimestamp();
					scanner.ScanFileOrFolder(folder, all: true, removeMissing: false);
					while (scanner.IsScanning)
					{
						Thread.Sleep(20);
					}
					long end = Stopwatch.GetTimestamp();
					int books = host.DatabaseManager.Database.Books.Count;

					// A notification is raised before each file is processed, so file i takes (stamp i+1 - stamp i); the last one ends at the completion
					// notification (empty file name), which is the final stamp.
					var perBook = new List<double>();
					lock (stamps)
					{
						for (int i = 0; i + 1 < stamps.Count; i++)
						{
							if (stamps[i].file.Length > 0)
							{
								perBook.Add((stamps[i + 1].ticks - stamps[i].ticks) * 1000.0 / Stopwatch.Frequency);
							}
						}
					}
					double totalMs = (end - start) * 1000.0 / Stopwatch.Frequency;
					output.WriteLine("scan: " + folder);
					output.WriteLine("  host open: " + openMs + " ms");
					output.WriteLine("  files seen: " + perBook.Count + ", books added: " + books);
					output.WriteLine("  scan time: " + totalMs.ToString("F0", CultureInfo.InvariantCulture) + " ms" + (books > 0 ? ", " + (totalMs / books).ToString("F1", CultureInfo.InvariantCulture) + " ms per book" : ""));
					if (perBook.Count > 0)
					{
						perBook.Sort();
						output.WriteLine("  per book: p50 " + Pct(perBook, 0.5) + " ms, p95 " + Pct(perBook, 0.95) + " ms, max " + perBook[perBook.Count - 1].ToString("F1", CultureInfo.InvariantCulture) + " ms");
					}
					int pages = host.DatabaseManager.Database.Books.Sum(b => b.PageCount);
					output.WriteLine("  pages counted: " + pages);
				}
				output.WriteLine("  database written: " + outDb + " (" + (File.Exists(outDb) ? new FileInfo(outDb).Length + " bytes" : "MISSING") + ")");
				if (!File.Exists(outDb))
				{
					error.WriteLine("The database was not written.");
					return 1;
				}
			}
			return 0;
		}

		public static int Page(string[] args, TextWriter output, TextWriter error)
		{
			var o = Options.Parse(args);
			string book = Path.GetFullPath(o.Positional(0, "page <book> --page N --out p.png"));
			int page = o.Int("page", 1);
			string outFile = o.Get("out");
			if (outFile == null || page < 1)
			{
				throw new UsageException("page: --out <file.png> is required and --page is 1-based (default 1).");
			}
			outFile = Path.GetFullPath(outFile);
			if (!File.Exists(book))
			{
				throw new UsageException("Book not found: " + book);
			}
			using (CliSession session = CliSession.Create(o))
			using (MacHost host = MacHost.Open(new MacHostOptions { Paths = session.Paths, DatabaseReadOnly = true }))
			{
				var sw = Stopwatch.StartNew();
				ComicBook cb = OpenBook(host, book);
				int count = cb.PageCount;
				long infoMs = sw.ElapsedMilliseconds;
				if (page > count)
				{
					error.WriteLine("The book has " + count + " pages, there is no page " + page + ".");
					return 1;
				}
				sw.Restart();
				PageKey key = cb.GetPageKey(page - 1, BitmapAdjustment.Empty);
				Size size;
				using (IImageProvider provider = cb.OpenProvider(key.Index))
				using (var item = host.ImagePool.GetPage(key, provider, onErrorThrowException: true))
				{
					long decodeMs = sw.ElapsedMilliseconds;
					Bitmap bmp = item.Item.Bitmap;
					size = bmp.Size;
					Directory.CreateDirectory(Path.GetDirectoryName(outFile));
					bmp.Save(outFile, ImageFormat.Png);
					output.WriteLine("page: " + book);
					output.WriteLine("  pages in book: " + count + ", format: " + cb.FileFormat);
					output.WriteLine("  page " + page + ": " + size.Width + " x " + size.Height + " (" + ((long)size.Width * size.Height / 1e6).ToString("F1", CultureInfo.InvariantCulture) + " MP)");
					output.WriteLine("  book info " + infoMs + " ms, decode through ImagePool " + decodeMs + " ms");
					output.WriteLine("  written: " + outFile + " (" + new FileInfo(outFile).Length + " bytes)");
				}
			}
			return 0;
		}

		public static int Thumb(string[] args, TextWriter output, TextWriter error)
		{
			var o = Options.Parse(args);
			string book = Path.GetFullPath(o.Positional(0, "thumb <book> --out t.jpg"));
			string outFile = o.Get("out");
			if (outFile == null)
			{
				throw new UsageException("thumb: --out <file.jpg> is required.");
			}
			outFile = Path.GetFullPath(outFile);
			if (!File.Exists(book))
			{
				throw new UsageException("Book not found: " + book);
			}
			using (CliSession session = CliSession.Create(o))
			{
				int page = o.Int("page", 0); // 0 = the front cover
				for (int run = 1; run <= 2; run++)
				{
					// Two sessions on the same cache folder: the second finds the thumbnail on disk (the first closes the pool, which saves the index).
					using (MacHost host = MacHost.Open(new MacHostOptions { Paths = session.Paths, DatabaseReadOnly = true }))
					{
						ComicBook cb = OpenBook(host, book);
						ThumbnailKey key = page > 0 ? cb.GetThumbnailKey(page - 1) : cb.GetFrontCoverThumbnailKey();
						var sw = Stopwatch.StartNew();
						bool cachedBefore = host.ImagePool.Thumbs.DiskCache.IsAvailable(key);
						using (IImageProvider provider = cb.OpenProvider(key.Index))
						using (var item = host.ImagePool.GetThumbnail(key, provider, onErrorThrowException: true))
						{
							long ms = sw.ElapsedMilliseconds;
							if (run == 1)
							{
								Directory.CreateDirectory(Path.GetDirectoryName(outFile));
								item.Item.Bitmap.Save(outFile, ImageFormat.Jpeg);
								output.WriteLine("thumb: " + book);
								output.WriteLine("  thumbnail " + item.Item.Width + " x " + item.Item.Height + " (original page " + item.Item.OriginalSize.Width + " x " + item.Item.OriginalSize.Height + ")");
								output.WriteLine("  written: " + outFile + " (" + new FileInfo(outFile).Length + " bytes)");
							}
							output.WriteLine("  run " + run + ": " + ms + " ms, in disk cache before: " + cachedBefore);
						}
					}
				}
			}
			return 0;
		}

		private static ComicBook OpenBook(MacHost host, string file)
		{
			ComicBook cb = host.DatabaseManager.BookFactory.Create(file, CreateBookOption.DoNotAdd, RefreshInfoOptions.GetPageCount);
			if (cb == null)
			{
				throw new UsageException("Not a book the Core can read: " + file);
			}
			cb.RefreshInfoFromFile(RefreshInfoOptions.GetPageCount);
			return cb;
		}

		/// <summary>Peak resident set size of the process (Process.PeakWorkingSet64 reports 0 on macOS).</summary>
		private static long PeakRss()
		{
			try
			{
				var buffer = new byte[256]; // struct rusage: two timevals (32 bytes), then ru_maxrss, in bytes on macOS
				return getrusage(0, buffer) == 0 ? BitConverter.ToInt64(buffer, 32) : 0;
			}
			catch (Exception)
			{
				return 0;
			}
		}

		[System.Runtime.InteropServices.DllImport("libSystem", EntryPoint = "getrusage")]
		private static extern int getrusage(int who, byte[] usage);

		private static string Mb(long bytes) => (bytes / 1048576.0).ToString("F1", CultureInfo.InvariantCulture);

		private static string Pct(List<double> sorted, double p) => sorted[Math.Min(sorted.Count - 1, (int)(p * sorted.Count))].ToString("F1", CultureInfo.InvariantCulture);
	}

	/// <summary>`--remap "from=to"`: replaces a path prefix in the loaded database, for books and watch folders. Backslashes after the prefix become `/`.</summary>
	internal sealed class PathRemap
	{
		public string From { get; }

		public string To { get; }

		private PathRemap(string from, string to)
		{
			From = from;
			To = to;
		}

		public override string ToString() => From + " => " + To;

		public static PathRemap Parse(string spec)
		{
			if (spec == null)
			{
				return null;
			}
			// The split is on the last '=' that follows a path separator, so '=' inside the target is not special unless it is ambiguous; the
			// first '=' is used, which is enough for the paths of this project.
			int i = spec.IndexOf('=');
			if (i <= 0 || i == spec.Length - 1)
			{
				throw new UsageException("--remap expects \"<from prefix>=<to prefix>\", got: " + spec);
			}
			return new PathRemap(spec.Substring(0, i), spec.Substring(i + 1));
		}

		/// <summary>Null if <paramref name="path"/> is outside the prefix. The prefix matches with or without a trailing separator.</summary>
		public string Map(string path)
		{
			if (string.IsNullOrEmpty(path))
			{
				return null;
			}
			string from = From;
			string fromBare = from.TrimEnd('\\', '/');
			string toBare = To.TrimEnd('\\', '/');
			if (path.Equals(fromBare, StringComparison.OrdinalIgnoreCase))
			{
				return toBare;
			}
			if (path.Length > fromBare.Length && path.StartsWith(fromBare, StringComparison.OrdinalIgnoreCase) && (path[fromBare.Length] == '\\' || path[fromBare.Length] == '/'))
			{
				return toBare + "/" + path.Substring(fromBare.Length + 1).Replace('\\', '/');
			}
			return null;
		}

		public RemapResult Apply(ComicDatabase database)
		{
			var r = new RemapResult();
			foreach (ComicBook book in database.Books.ToArray())
			{
				string mapped = Map(book.FilePath);
				if (mapped != null)
				{
					book.FilePath = mapped;
					r.BooksRemapped++;
					if (File.Exists(mapped))
					{
						r.BooksFound++;
					}
				}
			}
			foreach (WatchFolder wf in database.WatchFolders.ToArray())
			{
				string mapped = Map(wf.Folder);
				if (mapped != null)
				{
					wf.Folder = mapped;
					r.WatchFoldersRemapped++;
				}
			}
			return r;
		}
	}

	internal sealed class RemapResult
	{
		public int BooksRemapped;
		public int BooksFound;
		public int WatchFoldersRemapped;
	}
}
