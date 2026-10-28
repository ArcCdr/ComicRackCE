using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using ComicRack.Mac.Host.Scripting.Scan;
using Xunit;

namespace ComicRack.Mac.Host.Tests.Acceptance
{
	/// <summary>Skipped when a package of the plugin corpus is not on this machine (mac/fixtures is not in the repository: mac/scripting/fetch-corpus.sh --only NAME).</summary>
	public sealed class PluginFactAttribute : FactAttribute
	{
		public PluginFactAttribute(params string[] needed)
		{
			string[] missing = needed.Where(n => !File.Exists(AcceptancePlugins.Path(n))).ToArray();
			if (missing.Length > 0)
			{
				Skip = "missing from mac/fixtures/plugins: " + string.Join(", ", missing) + " (run mac/scripting/fetch-corpus.sh)";
			}
		}
	}

	/// <summary>The two acceptance plugins of P4.17 and P4.18 (owner, R11). Acceptance tests are the only tests that may name a plugin (PluginNamesRuleTests).</summary>
	internal static class AcceptancePlugins
	{
		public const string Bedetheque = "bedetheque-scraper-2-v6.08/Bedetheque.Scraper.2_v6.08.crplugin";
		public const string Organizer = "library-organizer-2.1.13/Library.Organizer.2.1.13.crplugin";

		public static string Root => Scripting.ScriptTestSupport.RepoRoot();

		public static string Path(string relative) => System.IO.Path.Combine(Root, "mac", "fixtures", "plugins", relative);
	}

	/// <summary>
	/// P4.17: the two acceptance plugins, Bedetheque Scraper 2 v6.08 and Library Organizer 2.1.13, are pinned downloads in their own untrusted folders, install through the plugin install path, register their
	/// commands, and every API they reach is classified in the gap report, with a task for everything that needs work. The plugins run only inside the plugin host (the load probe).
	/// </summary>
	public class PluginGapAcceptanceTests
	{
		private static readonly Lazy<PluginGapReport> Report = new Lazy<PluginGapReport>(() =>
		{
			string root = AcceptancePlugins.Root;
			GapRules rules = GapRules.Load(System.IO.Path.Combine(root, "mac", "scripting", "gap-rules.txt"));
			return PluginGapRunner.Build(new[] { AcceptancePlugins.Path(AcceptancePlugins.Bedetheque), AcceptancePlugins.Path(AcceptancePlugins.Organizer) }, rules);
		});

		private static PluginGap Gap(string idPrefix) => Report.Value.Plugins.Single(p => p.Facts.Id.StartsWith(idPrefix, StringComparison.Ordinal));

		[PluginFact(AcceptancePlugins.Bedetheque, AcceptancePlugins.Organizer)]
		public void The_packages_are_the_pinned_downloads_each_in_its_own_untrusted_folder()
		{
			(string file, string sha, long size, string licence)[] expected =
			{
				(AcceptancePlugins.Bedetheque, "666016de2439ba179c27fb267b64d9896faa3bae71200f9b372969991954e5c5", 13282162, "none stated"),
				(AcceptancePlugins.Organizer, "9ef9617d349fc212396409d84a3c3b2c927105257bb18f5fc63cabd284312320", 126112, "Apache-2.0"),
			};
			foreach ((string file, string sha, long size, string licence) in expected)
			{
				string path = AcceptancePlugins.Path(file);
				using (FileStream stream = File.OpenRead(path))
				{
					Assert.Equal(sha, Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant());
				}
				Assert.Equal(size, new FileInfo(path).Length);
				string[] source = File.ReadAllLines(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path), "SOURCE.txt"));
				Assert.Contains(source, l => l.StartsWith("url: https://github.com/", StringComparison.Ordinal) && l.EndsWith(System.IO.Path.GetFileName(path), StringComparison.Ordinal));
				Assert.Contains(source, l => l == "pin: " + sha);
				Assert.Contains(source, l => l.StartsWith("licence: " + licence, StringComparison.Ordinal));
				Assert.Contains(source, l => l.StartsWith("note: untrusted download", StringComparison.Ordinal));
			}
			// no two plugins share a folder, and the folders are not in the repository
			Assert.NotEqual(System.IO.Path.GetDirectoryName(AcceptancePlugins.Path(AcceptancePlugins.Bedetheque)), System.IO.Path.GetDirectoryName(AcceptancePlugins.Path(AcceptancePlugins.Organizer)));
			Assert.Contains("/fixtures/", File.ReadAllLines(System.IO.Path.Combine(AcceptancePlugins.Root, "mac", ".gitignore")));
		}

		[PluginFact(AcceptancePlugins.Bedetheque, AcceptancePlugins.Organizer)]
		public void Installing_through_the_plugin_install_path_registers_the_commands_of_each_plugin()
		{
			PluginGap bd = Gap("Bedetheque");
			Assert.Equal("Bedetheque Scraper 2", bd.Facts.Ini["Name"]);
			Assert.Equal("6.08", bd.Facts.Ini["Version"]);
			Assert.Contains(bd.Facts.Commands, c => c.Key == "Bedetheque2" && c.Hook.Contains("Books") && c.Enabled);
			Assert.Contains(bd.Facts.Commands, c => c.Key == "QuickScrapeBD2" && c.Hook == "Books");
			Assert.Contains(bd.Facts.Commands, c => c.Key == "ConfigureBD2" && c.Hook == "Library");
			Assert.Contains(bd.Hooks, h => h == "ConfigScript");

			PluginGap lo = Gap("Library.Organizer");
			Assert.Equal("Library Organizer", lo.Facts.Ini["Name"]);
			Assert.Equal("2.1.13", lo.Facts.Ini["Version"]);
			Assert.Contains(lo.Facts.Commands, c => c.Key == "library-organizer-main" && c.Hook == "Books");
			Assert.Contains(lo.Facts.Commands, c => c.Key == "library-organizer-quick" && c.Hook == "Books");
			Assert.Contains(lo.Facts.Commands, c => c.Key == "ConfigureLibraryOrganizer" && c.Hook == "Library");
			Assert.Contains(lo.Facts.Commands, c => c.Key == "LibraryOrganizerUndo" && c.Hook == "Library");
			Assert.Contains(lo.Facts.Commands, c => c.Key == "LibraryOrganizerStartup" && c.Hook == "Startup" && !c.Enabled);
		}

		[PluginFact(AcceptancePlugins.Bedetheque, AcceptancePlugins.Organizer)]
		public void The_scripts_of_both_plugins_load_in_the_plugin_host()
		{
			GapLoadCheck bd = Assert.Single(Gap("Bedetheque").Facts.LoadChecks);
			Assert.True(bd.Ok, bd.Message);
			GapLoadCheck lo = Assert.Single(Gap("Library.Organizer").Facts.LoadChecks);
			// P4.17.3: the WPF assemblies are there by name (throwing facades) and System.dll is loaded as on the Framework, so the top level of every script runs
			Assert.True(lo.Ok, lo.Message);
		}

		[PluginFact(AcceptancePlugins.Bedetheque, AcceptancePlugins.Organizer)]
		public void Every_row_has_a_status_and_everything_that_needs_work_has_a_task_that_exists_in_the_plan()
		{
			string plan = File.ReadAllText(System.IO.Path.Combine(AcceptancePlugins.Root, "mac", "plans", "full-port.md"));
			foreach (PluginGap gap in Report.Value.Plugins)
			{
				Assert.True(gap.AllRows.Count() > 400, gap.Facts.Id);
				Assert.All(gap.AllRows, r => Assert.Contains(r.Status, GapReportWriter.Statuses));
				GapRow[] untasked = gap.AllRows.Where(r => r.NeedsTask && string.IsNullOrEmpty(r.Owner)).ToArray();
				Assert.True(untasked.Length == 0, gap.Facts.Id + " has rows without a task: " + string.Join(", ", untasked.Select(r => r.Status + " " + r.Name)));
				foreach (string task in gap.AllRows.Select(r => r.Owner).Concat(gap.Findings.Select(f => f.Task)).Where(t => t != null).Distinct())
				{
					Assert.True(plan.Contains("\n#### " + task + " "), task + " has no task block");
				}
				Assert.All(gap.Findings.Where(f => f.Severity == "error"), f => Assert.False(string.IsNullOrEmpty(f.Task), "error finding without a task: " + f.Kind + " " + f.Symbol));
			}
		}

		[PluginFact(AcceptancePlugins.Bedetheque, AcceptancePlugins.Organizer)]
		public void The_report_names_what_each_plugin_reaches_beyond_the_forms_and_drawing_surface()
		{
			PluginGap bd = Gap("Bedetheque");
			Assert.Equal("A", bd.Tier);
			Assert.Equal(1, bd.NativeFiles);
			Assert.Contains(bd.Findings, f => f.Kind == "native-binary" && f.Symbol == "BedethequeFetcher.exe");
			Assert.Contains(bd.Findings, f => f.Kind == "windows-path" && f.Symbol.Contains("\\App.Config"));
			Assert.Equal("real", Assert.Single(bd.Framework, r => r.Name == "System.Web.Script.Serialization.JavaScriptSerializer").Status);
			Assert.Contains(bd.Framework, r => r.Name == "System.Net.Sockets.TcpClient" && r.Status == "real");
			Assert.Contains(bd.Framework, r => r.Name == "System.Diagnostics.Process.Start" && r.Status == "differs");
			Assert.Contains(bd.Core, r => r.Name == "cYo.Projects.ComicRack.Engine.YesNo.Yes");
			Assert.Contains(bd.ByName, r => r.Name == "GetComicThumbnail" && r.Status == "real" && r.Source == "program");

			PluginGap lo = Gap("Library.Organizer");
			Assert.Equal("C", lo.Tier);
			Assert.Contains(lo.References, r => r.Name == "PresentationFramework" && r.Status == "real");
			// P4.17.4: the Recycle Bin overloads work, the row is real
			Assert.Contains(lo.Framework, r => r.Name == "Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile" && r.Status == "real");
			Assert.Contains(lo.Framework, r => r.Name == "System.ComponentModel.BackgroundWorker");
			Assert.Contains(lo.Findings, f => f.Kind == "windows-path" && f.Symbol.Contains("\\home_32.png"));
			Assert.Contains(lo.Findings, f => f.Kind == "windows-member" && f.Symbol == "Control.Handle");
		}

		[Fact]
		public void The_committed_report_exists_has_both_checksums_and_a_task_in_every_row_that_needs_one()
		{
			string path = System.IO.Path.Combine(AcceptancePlugins.Root, "mac", "reports", "plugin-gap-bedetheque.md");
			Assert.True(File.Exists(path), "run mac/scripting/plugin-gap.sh");
			string text = File.ReadAllText(path);
			Assert.Contains("666016de2439ba179c27fb267b64d9896faa3bae71200f9b372969991954e5c5", text);
			Assert.Contains("9ef9617d349fc212396409d84a3c3b2c927105257bb18f5fc63cabd284312320", text);
			Assert.DoesNotContain("/Users/", text);
			Assert.DoesNotContain("/private/", text);
			Assert.Contains("How the plugin starts the helper and talks to it", text);
			int rows = 0;
			var statuses = new HashSet<string>(GapReportWriter.Statuses);
			foreach (string line in text.Split('\n').Where(l => l.StartsWith("| `", StringComparison.Ordinal)))
			{
				string[] cells = Regex.Split(line.Trim().Trim('|'), @"(?<!\\)\|").Select(c => c.Trim()).ToArray();
				if (cells.Length < 7 || !statuses.Contains(cells[3]))
				{
					continue;
				}
				rows++;
				if (cells[3] == "stub" || cells[3] == "partial" || cells[3] == "missing" || cells[3] == "differs")
				{
					Assert.True(Regex.IsMatch(cells[5], @"^P\d+\.\d+(\.\d+)?[a-z]?$"), "no task in: " + line);
				}
			}
			// The report lists every row that needs work and only the first rows of each table that do not: it shrinks as the facades are implemented (631 rows before P4.9a, 405 after).
			Assert.True(rows > 150, "rows: " + rows);
		}
	}
}
