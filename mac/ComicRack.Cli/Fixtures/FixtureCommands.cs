using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using ICSharpCode.SharpZipLib.Zip;

namespace ComicRack.Cli.Fixtures
{
	/// <summary>`crcli fixtures make` and `crcli fixtures from-db` (M1.6).</summary>
	public static class FixtureCommands
	{
		public static int Run(string[] args, TextWriter output, TextWriter error, FixtureEnvironment env)
		{
			if (args.Length == 0)
			{
				throw new UsageException("fixtures: expected `make` or `from-db`.");
			}
			var opts = Options.Parse(args.Skip(1).ToArray());
			switch (args[0])
			{
			case "make":
				return Make(opts, output, env);
			case "from-db":
				return FromDb(opts, output, env);
			default:
				throw new UsageException("fixtures: unknown command " + args[0]);
			}
		}

		private static int Make(Options o, TextWriter output, FixtureEnvironment env)
		{
			string dir = Path.GetFullPath(o.Positional(0, "fixtures make <dir>"));
			int books = o.Int("books", 10);
			int pages = o.Int("pages", 12);
			int seed = o.Int("seed", 1);
			string[] formats = (o.Get("formats") ?? "cbz").ToLowerInvariant().Split(',', StringSplitOptions.RemoveEmptyEntries);
			if (books < 1 || pages < 1)
			{
				throw new UsageException("--books and --pages must be at least 1.");
			}
			var wanted = ResolveFormats(formats, env, output);
			return Generate(BookPlanner.ForMake(dir, books, pages, seed), wanted, env, output, copyDb: null);
		}

		private static int FromDb(Options o, TextWriter output, FixtureEnvironment env)
		{
			string db = Path.GetFullPath(o.Positional(0, "fixtures from-db <ComicDb.xml> --root <dir>"));
			string root = o.Get("root");
			if (root == null)
			{
				throw new UsageException("fixtures from-db: --root <dir> is required.");
			}
			root = Path.GetFullPath(root);
			int pages = o.Int("pages", 1);
			int max = o.Int("max-books", 0);
			string strip = o.Get("strip") ?? @"S:\media\BD\";
			if (!File.Exists(db))
			{
				throw new UsageException("DB not found: " + db);
			}
			var sw = Stopwatch.StartNew();
			List<DbBook> books = BookPlanner.ReadDb(db, max);
			output.WriteLine("Read " + books.Count + " books from " + db + " in " + sw.ElapsedMilliseconds + " ms.");
			List<BookPlan> plans = BookPlanner.ForDb(books, root, strip, pages);
			string dbCopy = Path.Combine(env.FixturesRoot, "ComicDb.xml");
			return Generate(plans, new List<string> { "cbz" }, env, output, copyDb: (db, dbCopy));
		}

		private static List<string> ResolveFormats(string[] formats, FixtureEnvironment env, TextWriter output)
		{
			var result = new List<string>();
			foreach (string f in formats.Distinct())
			{
				switch (f)
				{
				case "cbz":
					result.Add(f);
					break;
				case "cb7":
					if (env.SevenZip == null)
					{
						output.WriteLine("Skipping CB7: `7zz` is not installed (brew install sevenzip).");
					}
					else
					{
						result.Add(f);
					}
					break;
				case "cbr":
					output.WriteLine("Skipping CBR: 7zz cannot create RAR archives (it only extracts them), and there is no rar tool in the toolchain.");
					break;
				default:
					throw new UsageException("Unknown format: " + f + " (cbz, cb7, cbr).");
				}
			}
			if (result.Count == 0)
			{
				result.Add("cbz");
			}
			return result;
		}

		private static int Generate(List<BookPlan> plans, List<string> formats, FixtureEnvironment env, TextWriter output, (string Source, string Target)? copyDb)
		{
			int pageTotal = plans.Sum(p => p.Pages.Count);
			output.WriteLine("Plan: " + plans.Count + " books, " + pageTotal + " pages, formats " + string.Join(",", formats) + ".");

			// Estimate: encode a sample of pages per format, then bytes per pixel x all pixels (+ archive overhead).
			var sw = Stopwatch.StartNew();
			double estimate = EstimatePages(plans) * formats.Count + plans.Count * formats.Count * 2048.0;
			long replacing = 0;
			foreach (BookPlan plan in plans)
			{
				foreach (string f in formats)
				{
					string file = ArchivePath(plan.Path, f);
					if (File.Exists(file))
					{
						replacing += new FileInfo(file).Length;
					}
				}
			}
			bool copyDbNeeded = false;
			if (copyDb.HasValue)
			{
				copyDbNeeded = !SameContent(copyDb.Value.Source, copyDb.Value.Target);
				if (copyDbNeeded)
				{
					estimate += new FileInfo(copyDb.Value.Source).Length;
					if (File.Exists(copyDb.Value.Target))
					{
						replacing += new FileInfo(copyDb.Value.Target).Length;
					}
				}
			}
			var budget = new FixtureBudget(env);
			budget.Check((long)estimate, replacing, output);
			output.WriteLine("(estimate took " + sw.ElapsedMilliseconds + " ms)");

			if (copyDbNeeded)
			{
				Directory.CreateDirectory(Path.GetDirectoryName(copyDb.Value.Target));
				File.Copy(copyDb.Value.Source, copyDb.Value.Target, overwrite: true);
				output.WriteLine("Copied the DB to " + copyDb.Value.Target);
			}
			else if (copyDb.HasValue)
			{
				output.WriteLine("DB copy " + copyDb.Value.Target + " is already identical; left alone.");
			}

			sw.Restart();
			int done = 0;
			long bytes = 0;
			int step = Math.Max(1, plans.Count / 10);
			var options = new ParallelOptions { MaxDegreeOfParallelism = Math.Min(Environment.ProcessorCount, 6) };
			try
			{
				Parallel.ForEach(plans, options, plan =>
				{
					long n = WriteBook(plan, formats, env, budget);
					Interlocked.Add(ref bytes, n);
					int d = Interlocked.Increment(ref done);
					if (d % step == 0 || d == plans.Count)
					{
						lock (output)
						{
							output.WriteLine("  " + d + "/" + plans.Count + " books, " + FixtureBudget.Mb(Interlocked.Read(ref bytes)) + " MB, " + sw.Elapsed.TotalSeconds.ToString("0.0") + " s");
						}
					}
				});
			}
			catch (AggregateException ex) when (ex.InnerExceptions.OfType<RefusedException>().Any())
			{
				throw ex.InnerExceptions.OfType<RefusedException>().First();
			}
			output.WriteLine("Wrote " + plans.Count + " books (" + pageTotal + " pages x " + formats.Count + " format(s)): " + FixtureBudget.Mb(bytes) + " MB (" + bytes + " bytes) in " + sw.Elapsed.TotalSeconds.ToString("0.0") + " s; estimate was " + FixtureBudget.Mb((long)estimate) + " MB.");
			long used = FixtureEnvironment.DirectorySize(env.FixturesRoot);
			output.WriteLine("mac/fixtures now holds " + FixtureBudget.Mb(used) + " MB of the " + FixtureBudget.Mb(env.BudgetBytes) + " MB budget.");
			if (env.PrintDiskFree)
			{
				output.WriteLine("Disk after:");
				output.WriteLine(FixtureEnvironment.DiskFreeReport());
			}
			return 0;
		}

		/// <summary>Bytes for all pages once, from a sample encoded per format.</summary>
		private static double EstimatePages(List<BookPlan> plans)
		{
			var all = plans.SelectMany(p => p.Pages).ToList();
			double total = 0;
			foreach (var group in all.GroupBy(p => p.Format))
			{
				var list = group.ToList();
				int sampleCount = Math.Min(list.Count, 4);
				double bytes = 0, pixels = 0;
				for (int i = 0; i < sampleCount; i++)
				{
					PageSpec s = list[(int)((long)i * 7919 % list.Count)];
					bytes += PageRenderer.Render(s).Length;
					pixels += s.Pixels;
				}
				total += list.Sum(p => (double)p.Pixels) * (bytes / pixels) * 1.25; // 25 % margin: the sample is small
			}
			return total;
		}

		private static string ArchivePath(string cbzPath, string format)
		{
			return format == "cbz" ? cbzPath : Path.ChangeExtension(cbzPath, "." + format);
		}

		private static bool SameContent(string a, string b)
		{
			if (!File.Exists(b) || new FileInfo(a).Length != new FileInfo(b).Length)
			{
				return false;
			}
			using (var sha = SHA256.Create())
			using (var fa = File.OpenRead(a))
			using (var fb = File.OpenRead(b))
			{
				return sha.ComputeHash(fa).AsSpan().SequenceEqual(sha.ComputeHash(fb));
			}
		}

		private static string PageName(PageSpec page, int count)
		{
			return (page.Index + 1).ToString(count > 999 ? "0000" : "000") + page.Extension;
		}

		/// <summary>Renders the pages of one book and writes it in every wanted format. Returns the bytes written.</summary>
		private static long WriteBook(BookPlan plan, List<string> formats, FixtureEnvironment env, FixtureBudget budget)
		{
			Directory.CreateDirectory(Path.GetDirectoryName(plan.Path));
			var pages = new List<(string Name, byte[] Data)>();
			foreach (PageSpec spec in plan.Pages)
			{
				pages.Add((PageName(spec, plan.Pages.Count), PageRenderer.Render(spec)));
			}
			long total = 0;
			foreach (string format in formats)
			{
				string target = ArchivePath(plan.Path, format);
				string temp = target + ".tmp";
				if (format == "cbz")
				{
					WriteZip(temp, pages);
				}
				else
				{
					WriteSevenZip(temp, pages, env.SevenZip);
				}
				long size = new FileInfo(temp).Length;
				budget.Add(size);
				File.Move(temp, target, overwrite: true);
				total += size;
			}
			return total;
		}

		private static void WriteZip(string file, List<(string Name, byte[] Data)> pages)
		{
			using (var fs = File.Create(file))
			using (var zip = new ZipOutputStream(fs))
			{
				zip.SetLevel(1); // images do not compress; level 1 is fast
				foreach (var page in pages)
				{
					zip.PutNextEntry(new ZipEntry(page.Name) { DateTime = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), Size = page.Data.Length });
					zip.Write(page.Data, 0, page.Data.Length);
					zip.CloseEntry();
				}
			}
		}

		private static void WriteSevenZip(string file, List<(string Name, byte[] Data)> pages, string sevenZip)
		{
			string work = file + ".d";
			Directory.CreateDirectory(work);
			try
			{
				foreach (var page in pages)
				{
					File.WriteAllBytes(Path.Combine(work, page.Name), page.Data);
				}
				if (File.Exists(file))
				{
					File.Delete(file);
				}
				var psi = new ProcessStartInfo(sevenZip) { WorkingDirectory = work, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
				foreach (string a in new[] { "a", "-t7z", "-mx=0", "-bd", "-y", Path.GetFullPath(file), "." })
				{
					psi.ArgumentList.Add(a);
				}
				using (var p = Process.Start(psi))
				{
					p.StandardOutput.ReadToEnd();
					string err = p.StandardError.ReadToEnd();
					p.WaitForExit();
					if (p.ExitCode != 0)
					{
						throw new IOException("7zz failed (" + p.ExitCode + "): " + err);
					}
				}
			}
			finally
			{
				Directory.Delete(work, recursive: true);
			}
		}
	}

	/// <summary>`--key value` options and positional arguments.</summary>
	internal sealed class Options
	{
		private readonly List<string> positional = new List<string>();
		private readonly Dictionary<string, string> named = new Dictionary<string, string>();

		public static Options Parse(string[] args)
		{
			var o = new Options();
			for (int i = 0; i < args.Length; i++)
			{
				if (args[i].StartsWith("--"))
				{
					if (i + 1 >= args.Length)
					{
						throw new UsageException("Missing value for " + args[i]);
					}
					o.named[args[i].Substring(2)] = args[++i];
				}
				else
				{
					o.positional.Add(args[i]);
				}
			}
			return o;
		}

		public string Positional(int index, string usage)
		{
			if (index >= positional.Count)
			{
				throw new UsageException("Usage: crcli " + usage);
			}
			return positional[index];
		}

		public string Get(string name) => named.TryGetValue(name, out string v) ? v : null;

		public int Int(string name, int fallback)
		{
			string v = Get(name);
			if (v == null)
			{
				return fallback;
			}
			if (!int.TryParse(v, out int n))
			{
				throw new UsageException("--" + name + " expects a number, got " + v);
			}
			return n;
		}
	}
}
