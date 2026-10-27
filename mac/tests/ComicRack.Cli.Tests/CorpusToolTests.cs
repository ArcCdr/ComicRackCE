using System;
using System.IO;
using System.Linq;
using Xunit;

namespace ComicRack.Cli.Tests
{
	/// <summary>The corpus tooling of P4.6b (mac/scripting) and the R11 rule that the scripting layer knows no single plugin.</summary>
	public class CorpusToolTests : IDisposable
	{
		private readonly string temp = Path.Combine(Path.GetTempPath(), "crcorpus-" + Guid.NewGuid().ToString("N"));

		public CorpusToolTests()
		{
			Directory.CreateDirectory(temp);
		}

		public void Dispose()
		{
			try
			{
				Directory.Delete(temp, recursive: true);
			}
			catch (IOException)
			{
			}
		}

		[Fact]
		public void The_fetch_script_lists_pinned_sources_and_every_entry_has_a_pin_and_a_licence()
		{
			var r = Tools.Run("/bin/bash", new[] { Tools.Path_("mac/scripting/fetch-corpus.sh"), "--list" });
			Assert.Equal(0, r.code);
			string[] lines = r.output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
			Assert.True(lines.Length >= 20, r.output);
			Assert.All(lines, l => Assert.Contains("https://", l));
			string script = File.ReadAllText(Tools.Path_("mac/scripting/fetch-corpus.sh"));
			var entries = script.Split('\n').Where(l => l.Contains("|git|") || l.Contains("|release|")).Select(l => l.Split('|')).ToList();
			Assert.Equal(lines.Length, entries.Count);
			Assert.All(entries, e =>
			{
				Assert.Equal(5, e.Length);
				Assert.StartsWith("https://", e[2]);
				Assert.Matches(e[1] == "git" ? "^[0-9a-f]{40}$" : "^[0-9a-f]{64}$", e[3]);
				Assert.False(string.IsNullOrWhiteSpace(e[4]));
			});
			Assert.Equal(entries.Count, entries.Select(e => e[0]).Distinct().Count());
		}

		[Fact]
		public void The_fetch_script_refuses_a_folder_outside_the_fixtures_and_artifacts_without_creating_it()
		{
			const string outside = "/nonexistent-corpus-root/plugins";
			var r = Tools.Run("/bin/bash", new[] { Tools.Path_("mac/scripting/fetch-corpus.sh"), "--root", outside });
			Assert.Equal(2, r.code);
			Assert.Contains("refusing", r.error);
			Assert.False(Directory.Exists(outside));
			var up = Tools.Run("/bin/bash", new[] { Tools.Path_("mac/scripting/fetch-corpus.sh"), "--root", Path.Combine(temp, "..", "x") });
			Assert.Equal(2, up.code);
			var bad = Tools.Run("/bin/bash", new[] { Tools.Path_("mac/scripting/fetch-corpus.sh"), "--nope" });
			Assert.Equal(2, bad.code);
		}

		[Fact]
		public void A_folder_that_exists_without_a_source_record_is_not_touched()
		{
			string root = Path.Combine(temp, "plugins");
			string dir = Path.Combine(root, "mifs-v1.0.0");
			Directory.CreateDirectory(dir);
			File.WriteAllText(Path.Combine(dir, "keep.txt"), "x");
			var r = Tools.Run("/bin/bash", new[] { Tools.Path_("mac/scripting/fetch-corpus.sh"), "--root", root, "--only", "mifs-v1.0.0" });
			Assert.Equal(1, r.code);
			Assert.Contains("without SOURCE.txt", r.error);
			Assert.Equal(new[] { "keep.txt" }, Directory.GetFiles(dir).Select(Path.GetFileName).ToArray());
		}

		/// <summary>R11: no plugin-specific code, names or special cases in mac/. Tests, tools, spikes and fixtures are not the layer; the plan files and scan results name plugins as data.</summary>
		[Fact]
		public void The_program_code_names_no_single_plugin()
		{
			string[] names = { "Bedetheque", "AmazonScrapper", "ComicVineScraper", "FindImageResolution", "CRDataManager", "LibraryOrganizer", "FromDucks", "CVInfoWrite", "GrandComics" };
			string mac = Tools.Path_("mac");
			var offenders = Directory.EnumerateFiles(mac, "*.*", SearchOption.AllDirectories)
				.Where(f => f.EndsWith(".cs") || f.EndsWith(".axaml") || f.EndsWith(".csproj") || f.EndsWith(".py") || f.EndsWith(".json"))
				.Select(f => Path.GetRelativePath(mac, f).Replace('\\', '/'))
				.Where(f => !f.Contains("/bin/") && !f.Contains("/obj/") && !f.StartsWith("fixtures/") && !f.StartsWith("artifacts/") && !f.StartsWith("tests/") && !f.StartsWith("spikes/")
					&& !f.StartsWith("tools/") && !f.StartsWith("plans/") && !f.StartsWith("scripting/") && !f.StartsWith("reports/") && !f.StartsWith("upstream-proposals/"))
				.Where(f => names.Any(n => File.ReadAllText(Path.Combine(mac, f)).Contains(n, StringComparison.OrdinalIgnoreCase)))
				.ToList();
			Assert.True(offenders.Count == 0, "plugin names in program code: " + string.Join(", ", offenders));
		}
	}
}
