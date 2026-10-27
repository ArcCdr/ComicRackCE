using System;
using System.IO;
using System.Linq;
using ComicRack.Cli.Fixtures;
using ComicRack.Mac.Host.Scripting.Scan;

namespace ComicRack.Cli.Scripting
{
	/// <summary>
	/// `crcli script-scan` (P4.6a): the script compatibility scanner. It reads scripts, .NET assemblies, HTML files and plugin packages below the given paths without running anything and prints
	/// the ranked table <c>type.member -> #scripts</c> of the WinForms and GDI+ surface they use, the assemblies they reference and what can never run on macOS.
	/// </summary>
	internal static class ScriptScanCommand
	{
		public const string Usage = "script-scan <file-or-folder>... [--json out.json] [--top N] [--reference-pack <dir>] [--exclude-file <file>] [--markdown <out.md>] [--fail-on-error]\n"
			+ "  Scans .py scripts (parsed with the IronPython parser), .dll/.exe assemblies (member references of the metadata), .html files and .crplugin/.zip packages (read in memory).\n"
			+ "  Prints a summary, the first N (default 50) rows of the ranked types, members and referenced assemblies, every finding and one line per unit. With --json the whole report is written to the file.\n"
			+ "  --reference-pack names the folder of System.Windows.Forms.dll (default: COMICRACK_WINFORMS_REFERENCE, the NuGet cache of the Windows Desktop reference pack, else the macOS facade).\n"
			+ "  --exclude-file names a text file of paths to leave out, relative to a scanned folder, one per line (# starts a comment).\n"
			+ "  --markdown writes the usage report (tier counts, plugins, ranked types and members, untyped names, assemblies, findings; mac/scripting/compat-usage.md).\n"
			+ "  Exit 0; with --fail-on-error 1 when any unit has a Tier C finding.";

		public static int Run(string[] args, TextWriter output, TextWriter error)
		{
			var o = Options.Parse(args);
			if (o.PositionalArguments.Count == 0)
			{
				throw new UsageException("Usage: crcli " + Usage);
			}
			foreach (string p in o.PositionalArguments)
			{
				if (!File.Exists(p) && !Directory.Exists(p))
				{
					throw new UsageException("Nothing to scan at " + p);
				}
			}
			ApiCatalog catalog = ReferenceSurface.Load(o.Get("reference-pack"));
			var scanner = new ScriptScanner(catalog);
			string excludeFile = o.Get("exclude-file");
			if (excludeFile != null)
			{
				if (!File.Exists(excludeFile))
				{
					throw new UsageException("No exclude file at " + excludeFile);
				}
				scanner.Exclude = ScriptScanner.ReadExcludeList(excludeFile);
			}
			ScanReport report = scanner.Scan(o.PositionalArguments);
			string markdown = o.Get("markdown");
			if (markdown != null)
			{
				string full = Path.GetFullPath(markdown);
				Directory.CreateDirectory(Path.GetDirectoryName(full));
				using (var writer = new StreamWriter(full, false, new System.Text.UTF8Encoding(false)) { NewLine = "\n" })
				{
					UsageReport.Write(report, writer, new UsageReportOptions
					{
						Commands = new[]
						{
							"mac/scripting/fetch-corpus.sh",
							"crcli script-scan " + string.Join(" ", o.PositionalArguments.Select(a => a.Replace('\\', '/'))) + (excludeFile != null ? " --exclude-file " + excludeFile.Replace('\\', '/') : string.Empty) + " --markdown " + markdown.Replace('\\', '/'),
						},
						Notes = excludeFile == null ? Array.Empty<string>() : new[] { scanner.Exclude.Count + " paths of the checkouts are left out (build tools, tests, program assemblies kept for reference): `" + Path.GetFileName(excludeFile) + "`." },
					});
				}
			}
			string json = o.Get("json");
			if (json != null)
			{
				string full = Path.GetFullPath(json);
				Directory.CreateDirectory(Path.GetDirectoryName(full));
				File.WriteAllText(full, report.ToJson());
			}
			report.WriteText(output, o.Int("top", 50));
			if (json != null)
			{
				output.WriteLine();
				output.WriteLine("json: " + Path.GetFullPath(json));
			}
			return o.Flag("fail-on-error") && report.Units.Any(u => u.Findings.Any(f => f.Severity == "error")) ? 1 : 0;
		}
	}
}
