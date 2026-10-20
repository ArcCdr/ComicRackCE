using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace ComicRack.Mac.Host.Tests
{
	/// <summary>MacSettings copies defaults from ComicRack/Config/Settings.cs and cites the lines; these tests keep both honest.</summary>
	public class MacSettingsTests
	{
		private static string RepoRoot()
		{
			string dir = AppContext.BaseDirectory;
			while (dir != null && !File.Exists(Path.Combine(dir, "ComicRack", "Config", "Settings.cs")))
			{
				dir = Path.GetDirectoryName(dir);
			}
			Assert.True(dir != null, "The repository root was not found above " + AppContext.BaseDirectory);
			return dir;
		}

		private static string[] SettingsLines() => File.ReadAllLines(Path.Combine(RepoRoot(), "ComicRack", "Config", "Settings.cs"));

		// the citations moved with the values: AppPreferences holds them, MacSettings keeps the two it still owns
		private static string[] MacSettingsLines() => File.ReadAllLines(Path.Combine(RepoRoot(), "mac", "ComicRack.Mac.Host", "MacSettings.cs"))
			.Concat(File.ReadAllLines(Path.Combine(RepoRoot(), "mac", "ComicRack.Mac.Host", "Settings", "AppPreferences.cs"))).ToArray();

		[Fact]
		public void Every_cited_line_of_Settings_cs_holds_the_field_or_constant_it_names()
		{
			string[] settings = SettingsLines();
			var cite = new Regex(@"(?<name>\w+)(\s*=[^;]*)?;\s*//\s*Settings\.cs:(?<line>\d+)(-(?<end>\d+))?");
			int checkedCount = 0;
			foreach (string line in MacSettingsLines())
			{
				Match m = cite.Match(line);
				if (!m.Success || m.Groups["end"].Success || !line.TrimStart().StartsWith("private "))
				{
					continue;
				}
				int n = int.Parse(m.Groups["line"].Value);
				// a field of an automatic property is cited at the property (same name, capital first letter)
				Assert.Matches("(?i)\\b" + Regex.Escape(m.Groups["name"].Value) + "\\b", settings[n - 1]);
				checkedCount++;
			}
			Assert.True(checkedCount >= 60, "only " + checkedCount + " cited fields were found");
		}

		[Fact]
		public void Defaults_equal_the_initializers_of_the_WinForms_settings()
		{
			string[] settings = SettingsLines();
			var consts = new Dictionary<string, string>();
			var fields = new Dictionary<string, string>();
			foreach (string line in settings)
			{
				Match c = Regex.Match(line, @"public const int (\w+) = (\d+);");
				if (c.Success)
				{
					consts[c.Groups[1].Value] = c.Groups[2].Value;
				}
				Match f = Regex.Match(line, @"^\s*private (?:volatile )?(bool|int) (\w+)(?: = ([^;]+))?;");
				if (f.Success && !fields.ContainsKey(f.Groups[2].Value))
				{
					string value = f.Groups[3].Success ? f.Groups[3].Value : (f.Groups[1].Value == "bool" ? "false" : "0");
					fields[f.Groups[2].Value] = consts.TryGetValue(value, out string resolved) ? resolved : value;
				}
			}

			var mac = new MacSettings();
			var expected = new Dictionary<string, object>
			{
				["thumbCacheEnabled"] = mac.ThumbCacheEnabled,
				["thumbCacheSizeMB"] = mac.ThumbCacheSizeMB,
				["pageCacheEnabled"] = mac.PageCacheEnabled,
				["pageCacheSizeMB"] = mac.PageCacheSizeMB,
				["internetCacheEnabled"] = mac.InternetCacheEnabled,
				["internetCacheSizeMB"] = mac.InternetCacheSizeMB,
				["memoryThumbCacheSizeMB"] = mac.MemoryThumbCacheSizeMB,
				["memoryPageCacheCount"] = mac.MemoryPageCacheCount,
				["memoryThumbCacheOptimized"] = mac.MemoryThumbCacheOptimized,
				["memoryPageCacheOptimized"] = mac.MemoryPageCacheOptimized,
				["updateComicFiles"] = mac.UpdateComicFiles,
				["updateComicBookFiles"] = mac.UpdateComicBookFiles,
				["autoUpdateComicsFiles"] = mac.AutoUpdateComicsFiles,
				["lookForShared"] = mac.LookForShared,
			};
			foreach (var pair in expected)
			{
				Assert.True(fields.ContainsKey(pair.Key), "Settings.cs has no field " + pair.Key);
				Assert.Equal(fields[pair.Key], pair.Value is bool b ? (b ? "true" : "false") : pair.Value.ToString());
			}
			Assert.Empty(mac.Shares);
			Assert.Empty(mac.VirtualTags);
			Assert.Equal(string.Empty, mac.PrivateListingPassword);
			Assert.Equal(string.Empty, mac.ExternalServerAddress);
		}

		[Fact]
		public void Memory_cache_settings_are_clamped_and_raise_the_change_event()
		{
			var mac = new MacSettings();
			int raised = 0;
			mac.CacheSettingsChanged += (s, e) => raised++;
			mac.MemoryPageCacheCount = 5;
			Assert.Equal(20, mac.MemoryPageCacheCount);
			mac.MemoryPageCacheCount = 5000;
			Assert.Equal(100, mac.MemoryPageCacheCount);
			mac.MemoryThumbCacheSizeMB = 1;
			Assert.Equal(5, mac.MemoryThumbCacheSizeMB);
			mac.MemoryThumbCacheSizeMB = 9999;
			Assert.Equal(500, mac.MemoryThumbCacheSizeMB);
			Assert.Equal(4, raised);
			mac.MemoryThumbCacheSizeMB = 500; // unchanged: no event
			Assert.Equal(4, raised);
		}
	}
}
