using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace ComicRack.Cli.Tests
{
	/// <summary>
	/// R11 (P4.17): plugins run as they are and the layer is generic, so no source under mac/ names a plugin: no code, no name, no special case. The only files that may are the acceptance tests of the two
	/// acceptance plugins (folders named Acceptance below mac/tests), this file (the list of names), the plan, the reports and the corpus data (scan results are data), and the downloaded fixtures.
	/// </summary>
	public class PluginNamesRuleTests
	{
		/// <summary>What identifies a plugin of the corpus or an acceptance plugin; compared without regard to case.</summary>
		private static readonly string[] Names =
		{
			"Bedetheque", "Scraper", "Scrapper", "LibraryOrganizer", "Library Organizer", "Library.Organizer", "library-organizer", "AmazonScrapper", "ComicVineScraper", "comic-vine", "FindImageResolution",
			"CRDataManager", "Data Manager", "FromDucks", "From Ducks", "CVInfoWrite", "GrandComics", "Grand Comics", "MangaUpdate", "WebcomicHelper", "Stonepaw", "pescuma",
		};

		/// <summary>Folders below mac/ that hold no program text: build output, downloads, generated artifacts, the plan and its reports, the corpus data (scan results name plugins as data).</summary>
		private static readonly string[] SkippedTop = { "fixtures", "artifacts", "plans", "reports", "scripting", "upstream-proposals", "specs" };

		private static readonly string[] Extensions = { ".cs", ".axaml", ".csproj", ".props", ".targets", ".slnx", ".py", ".json", ".sh", ".swift", ".yml", ".xml", ".txt", ".resx", ".plist", ".entitlements" };

		private static IEnumerable<string> ProgramFiles(string mac)
		{
			var pending = new Stack<string>();
			pending.Push(mac);
			while (pending.Count > 0)
			{
				string dir = pending.Pop();
				foreach (string sub in Directory.GetDirectories(dir))
				{
					string name = Path.GetFileName(sub);
					string relative = Path.GetRelativePath(mac, sub).Replace('\\', '/');
					if (name == "bin" || name == "obj" || name == ".git" || name == "TestResults" || SkippedTop.Contains(relative))
					{
						continue;
					}
					pending.Push(sub);
				}
				foreach (string file in Directory.GetFiles(dir))
				{
					if (Extensions.Contains(Path.GetExtension(file).ToLowerInvariant()))
					{
						yield return file;
					}
				}
			}
		}

		[Fact]
		public void No_source_under_mac_names_a_plugin_except_the_acceptance_tests_and_this_list()
		{
			string mac = Tools.Path_("mac");
			string self = Path.GetFileName(typeof(PluginNamesRuleTests).Assembly.Location);
			var offenders = new List<string>();
			int scanned = 0;
			foreach (string file in ProgramFiles(mac))
			{
				string relative = Path.GetRelativePath(mac, file).Replace('\\', '/');
				if (relative.StartsWith("tests/", StringComparison.Ordinal) && relative.Split('/').Contains("Acceptance"))
				{
					continue;
				}
				if (relative == "tests/ComicRack.Cli.Tests/PluginNamesRuleTests.cs")
				{
					continue;
				}
				scanned++;
				string text = File.ReadAllText(file);
				string hit = Names.FirstOrDefault(n => text.Contains(n, StringComparison.OrdinalIgnoreCase));
				if (hit != null)
				{
					offenders.Add(relative + " (" + hit + ")");
				}
			}
			Assert.True(scanned > 300, "scanned " + scanned + " files; the walk is broken");
			Assert.True(offenders.Count == 0, "plugin names in program text: " + string.Join(", ", offenders));
		}

		[Fact]
		public void The_rule_sees_a_name_in_a_file_it_scans()
		{
			// the mutation check: a probe file with a plugin name under tools/ would be an offender
			string mac = Tools.Path_("mac");
			string probe = Path.Combine(mac, "tools", "plugin-names-probe-" + Guid.NewGuid().ToString("N") + ".sh");
			try
			{
				File.WriteAllText(probe, "# bedetheque special case\n");
				string text = File.ReadAllText(probe);
				Assert.Contains(probe, ProgramFiles(mac));
				Assert.Contains(Names, n => text.Contains(n, StringComparison.OrdinalIgnoreCase));
			}
			finally
			{
				File.Delete(probe);
			}
		}

		[Fact]
		public void The_acceptance_tests_are_where_the_two_plugins_are_named()
		{
			string dir = Tools.Path_("mac/tests/ComicRack.Mac.Host.Tests/Acceptance");
			Assert.True(Directory.Exists(dir));
			Assert.Contains(Directory.GetFiles(dir, "*.cs"), f => File.ReadAllText(f).Contains("Bedetheque", StringComparison.Ordinal) && File.ReadAllText(f).Contains("Library.Organizer", StringComparison.Ordinal));
		}
	}
}
