using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace UpstreamScan
{
	/// <summary>
	/// Classifies every file of an upstream commit range (full-port.md section 5.2, step 2).
	/// Usage: dotnet run --project mac/tools/upstream-scan -- --range A..B [--json out.json] [--all] [--repo dir] [--compile-items file]
	/// Exit codes: 0 scan done, 2 bad arguments or the scan could not run.
	/// </summary>
	internal static class Program
	{
		private static int Main(string[] args)
		{
			CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
			var o = new ScanOptions();
			string json = null, itemsFile = null;
			bool all = false;
			try
			{
				for (int i = 0; i < args.Length; i++)
				{
					switch (args[i])
					{
						case "--range": o.Range = Next(args, ref i); break;
						case "--json": json = Next(args, ref i); break;
						case "--repo": o.RepoDir = Next(args, ref i); break;
						case "--compile-items": itemsFile = Next(args, ref i); break;
						case "--all": all = true; break;
						case "-h":
						case "--help":
							Console.WriteLine("usage: upstream-scan --range A..B [--json out.json] [--all] [--repo dir] [--compile-items file]");
							return 0;
						default: throw new ArgumentException("unknown option " + args[i]);
					}
				}
				if (o.Range == null)
					throw new ArgumentException("--range A..B is required");
				o.RepoDir ??= FindRepo();
				if (itemsFile != null)
				{
					CompileItemSource.FromFile(itemsFile, out var compile, out var embedded);
					o.CompileItems = compile;
					o.EmbeddedItems = embedded;
				}
				var result = Scanner.Run(o);
				Console.Write(Report.ToText(result, all));
				if (json != null)
					File.WriteAllText(json, Report.ToJson(result), new UTF8Encoding(false));
				return 0;
			}
			catch (Exception e) when (e is ArgumentException || e is InvalidOperationException || e is IOException)
			{
				Console.Error.WriteLine("upstream-scan: " + e.Message);
				return 2;
			}
		}

		private static string Next(string[] args, ref int i)
		{
			if (i + 1 >= args.Length)
				throw new ArgumentException(args[i] + " needs a value");
			return args[++i];
		}

		private static string FindRepo()
		{
			string dir = Directory.GetCurrentDirectory();
			while (dir != null)
			{
				if (File.Exists(Path.Combine(dir, "mac", "ComicRack.Mac.slnx")))
					return dir;
				dir = Path.GetDirectoryName(dir);
			}
			throw new InvalidOperationException("cannot find mac/ComicRack.Mac.slnx above the current directory; pass --repo");
		}
	}
}
