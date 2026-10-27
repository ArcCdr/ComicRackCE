using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ComicRack.Cli.Fixtures;
using ComicRack.Mac.Host.Scripting.Scan;

namespace ComicRack.Cli.Scripting
{
	/// <summary>
	/// `crcli plugin-gap` (P4.17): for each plugin package, what it reaches and what the macOS build has. The package is installed through the plugin install path into a throw-away data folder and its
	/// scripts are loaded by the plugin host (the only place its code runs); the scanner reads it without running anything; the facades, the Core and the runtime are indexed from metadata.
	/// </summary>
	internal static class PluginGapCommand
	{
		public const string Usage = "plugin-gap <package.crplugin>... [--rules <file>] [--notes-dir <dir>] [--markdown <out.md>] [--json <out.json>] [--reference-pack <dir>] [--require-tasks]\n"
			+ "  Installs each package into a temporary data folder through the plugin install path, loads its scripts in the plugin host, scans it, and classifies every API it reaches (WinForms and GDI+\n"
			+ "  members, .NET types and members, Core types, Python modules, referenced assemblies, accesses by name) as real, stub, partial, missing, differs, plugin, python or dynamic.\n"
			+ "  --rules is the planning data (owner and note lines, mac/scripting/gap-rules.txt); --notes-dir holds <package file name without extension>.md files that are inserted into the report;\n"
			+ "  --require-tasks exits 1 when a row that needs work names no task. SOURCE.txt next to a package gives URL, pin and licence.";

		public static int Run(string[] args, TextWriter output, TextWriter error)
		{
			var o = Options.Parse(args);
			if (o.PositionalArguments.Count == 0)
			{
				throw new UsageException("Usage: crcli " + Usage);
			}
			foreach (string p in o.PositionalArguments)
			{
				if (!File.Exists(p))
				{
					throw new UsageException("No package at " + p);
				}
			}
			GapRules rules = GapRules.Empty;
			string rulesFile = o.Get("rules");
			if (rulesFile != null)
			{
				if (!File.Exists(rulesFile))
				{
					throw new UsageException("No rules file at " + rulesFile);
				}
				rules = GapRules.Load(rulesFile);
			}
			var notes = new Dictionary<string, string>(StringComparer.Ordinal);
			string notesDir = o.Get("notes-dir");
			if (notesDir != null && Directory.Exists(notesDir))
			{
				foreach (string file in Directory.GetFiles(notesDir, "*.md"))
				{
					notes[Path.GetFileNameWithoutExtension(file)] = File.ReadAllText(file);
				}
			}
			PluginGapReport report = PluginGapRunner.Build(o.PositionalArguments, rules, o.Get("reference-pack"));
			string markdown = o.Get("markdown");
			if (markdown != null)
			{
				string full = Path.GetFullPath(markdown);
				Directory.CreateDirectory(Path.GetDirectoryName(full));
				using (var writer = new StreamWriter(full, false, new System.Text.UTF8Encoding(false)) { NewLine = "\n" })
				{
					GapReportWriter.WriteMarkdown(report, writer, notes, "mac/scripting/plugin-gap.sh");
				}
			}
			string json = o.Get("json");
			if (json != null)
			{
				string full = Path.GetFullPath(json);
				Directory.CreateDirectory(Path.GetDirectoryName(full));
				File.WriteAllText(full, GapReportWriter.ToJson(report));
			}
			int untasked = 0;
			foreach (PluginGap p in report.Plugins)
			{
				output.WriteLine(p.Facts.Id + ": tier " + p.Tier + ", " + p.AllRows.Count() + " rows");
				foreach (string status in GapReportWriter.Statuses)
				{
					output.WriteLine("  " + status + "\t" + p.AllRows.Count(r => r.Status == status));
				}
				foreach (GapRow r in p.AllRows.Where(r => r.NeedsTask && r.Owner == null))
				{
					untasked++;
					output.WriteLine("  NO TASK: " + r.Status + " " + r.Name);
				}
			}
			return o.Flag("require-tasks") && untasked > 0 ? 1 : 0;
		}
	}
}
