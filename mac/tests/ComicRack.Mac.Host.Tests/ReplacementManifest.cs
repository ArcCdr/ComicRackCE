using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ComicRack.Mac.Host.Tests
{
	/// <summary>One entry of mac/replacements.json (mac/plans/full-port.md section 5.1).</summary>
	public sealed class ManifestEntry
	{
		public string Original { get; init; }
		public string Pattern { get; init; }
		public string Replacement { get; init; }
		public string Kind { get; init; }
		public string Pass { get; init; }
		public string Base { get; init; }
		public string Sha256 { get; init; }
		public int MaxChangedLines { get; init; }
		public string[] Companions { get; init; } = Array.Empty<string>();
		public string Reason { get; init; }

		/// <summary>The original file or the glob pattern that this entry tracks; null for a stub of a type that has no original file.</summary>
		public string Key => Original ?? Pattern;

		public override string ToString() => Key ?? Replacement ?? "(entry)";
	}

	/// <summary>Reads the replacement manifest and hashes originals exactly as mac/tools/replacement-sync.sh does.</summary>
	public static class ReplacementManifest
	{
		public const string OverrideVariable = "COMICRACK_REPLACEMENT_MANIFEST";
		public static readonly string[] Kinds = { "excluded", "stub", "patched-copy", "rewrite" };

		public static string RepoRoot()
		{
			string dir = AppContext.BaseDirectory;
			while (dir != null && !File.Exists(Path.Combine(dir, "mac", "replacements.json")))
			{
				dir = Path.GetDirectoryName(dir);
			}
			if (dir == null)
			{
				throw new InvalidOperationException("The repository root (mac/replacements.json) was not found above " + AppContext.BaseDirectory);
			}
			return dir;
		}

		public static string ManifestPath(string root, bool allowOverride)
		{
			string overridden = allowOverride ? Environment.GetEnvironmentVariable(OverrideVariable) : null;
			return string.IsNullOrEmpty(overridden) ? Path.Combine(root, "mac", "replacements.json") : overridden;
		}

		public static List<ManifestEntry> Load(string path)
		{
			using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { AllowTrailingCommas = true });
			var result = new List<ManifestEntry>();
			foreach (JsonElement e in doc.RootElement.GetProperty("entries").EnumerateArray())
			{
				result.Add(new ManifestEntry
				{
					Original = Str(e, "original"),
					Pattern = Str(e, "pattern"),
					Replacement = Str(e, "replacement"),
					Kind = Str(e, "kind"),
					Pass = Str(e, "pass"),
					Base = Str(e, "base"),
					Sha256 = Str(e, "sha256"),
					MaxChangedLines = e.TryGetProperty("maxChangedLines", out JsonElement m) ? m.GetInt32() : 0,
					Companions = e.TryGetProperty("companions", out JsonElement c) ? c.EnumerateArray().Select(x => x.GetString()).ToArray() : Array.Empty<string>(),
					Reason = Str(e, "reason"),
				});
			}
			return result;
		}

		private static string Str(JsonElement e, string name)
		{
			return e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
		}

		/// <summary>The file text without a byte order mark and with LF line endings (the rule of OriginalSourceDriftTests).</summary>
		public static string Normalize(string path)
		{
			return File.ReadAllText(path, Encoding.UTF8).TrimStart('﻿').Replace("\r\n", "\n");
		}

		public static string HashText(string text)
		{
			return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
		}

		public static string ToPath(string root, string repoRelative)
		{
			return Path.IsPathRooted(repoRelative) ? repoRelative : Path.Combine(root, repoRelative.Replace('/', Path.DirectorySeparatorChar));
		}

		public static bool IsGlob(string key) => key.IndexOfAny(new[] { '*', '?' }) >= 0;

		/// <summary>Repo-relative paths matched by a glob (** crosses folders, * and ? stay inside one), sorted ordinally, obj/ and bin/ ignored.</summary>
		public static List<string> Expand(string root, string pattern)
		{
			int wild = pattern.IndexOfAny(new[] { '*', '?' });
			string prefix = pattern.Substring(0, wild);
			prefix = prefix.Contains('/') ? prefix.Substring(0, prefix.LastIndexOf('/')) : "";
			string dir = Path.Combine(root, prefix.Replace('/', Path.DirectorySeparatorChar));
			var rx = new Regex("^" + Regex.Escape(pattern).Replace(@"\*\*", "\u0001").Replace(@"\*", "[^/]*").Replace(@"\?", "[^/]").Replace("\u0001", ".*") + "$");
			var files = new List<string>();
			if (Directory.Exists(dir))
			{
				foreach (string f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
				{
					string rel = Path.GetRelativePath(root, f).Replace(Path.DirectorySeparatorChar, '/');
					if (rx.IsMatch(rel) && !Regex.IsMatch(rel, "(^|/)(obj|bin)/"))
					{
						files.Add(rel);
					}
				}
			}
			files.Sort(StringComparer.Ordinal);
			return files;
		}

		/// <summary>Hash of an original file, or of a glob: the sorted "path sha256" lines joined by LF.</summary>
		public static string CurrentHash(string root, ManifestEntry entry)
		{
			if (entry.Original != null)
			{
				return HashText(Normalize(ToPath(root, entry.Original)));
			}
			return HashText(string.Join("\n", Expand(root, entry.Pattern).Select(p => p + " " + HashText(Normalize(ToPath(root, p))))));
		}

		/// <summary>Entries whose original no longer has the recorded hash, each with the command that re-syncs it.</summary>
		public static List<string> HashFailures(string root, IEnumerable<ManifestEntry> entries)
		{
			var failures = new List<string>();
			foreach (ManifestEntry e in entries.Where(x => x.Key != null))
			{
				string now;
				try
				{
					now = CurrentHash(root, e);
				}
				catch (FileNotFoundException)
				{
					failures.Add(e.Key + ": the original does not exist any more. Run: mac/tools/replacement-sync.sh " + Quote(e.Key));
					continue;
				}
				if (now != e.Sha256)
				{
					failures.Add(e.Key + " (" + e.Kind + (e.Replacement != null ? ", replaced by " + e.Replacement : "") + ") changed since " + (e.Base ?? "?").Substring(0, Math.Min(7, (e.Base ?? "?").Length))
						+ ". Run: mac/tools/replacement-sync.sh " + Quote(e.Key));
				}
			}
			return failures;
		}

		private static string Quote(string key) => IsGlob(key) ? "'" + key + "'" : key;

		/// <summary>
		/// Paths named by Exclude= and Compile Remove= in the shadow projects, as repo-relative paths. Excluded: bin/obj folders and
		/// everything inside mac/ (the Replacements\PassB exclusion).
		/// </summary>
		public static List<string> SourceExclusions(string root)
		{
			var files = Directory.GetFiles(Path.Combine(root, "mac"), "Sources.props", SearchOption.AllDirectories).ToList();
			files.AddRange(Directory.GetFiles(Path.Combine(root, "mac"), "*.csproj", SearchOption.AllDirectories));
			var result = new List<string>();
			foreach (string file in files.Where(f => !Regex.IsMatch(f, "[/\\\\](obj|bin)[/\\\\]")))
			{
				var doc = System.Xml.Linq.XDocument.Load(file);
				string dir = Path.GetDirectoryName(file);
				foreach (System.Xml.Linq.XElement compile in doc.Descendants("Compile"))
				{
					var attributes = new List<string>();
					string exclude = (string)compile.Attribute("Exclude");
					string remove = (string)compile.Attribute("Remove");
					if (exclude != null) attributes.Add(exclude);
					if (remove != null) attributes.Add(remove);
					foreach (string item in attributes.SelectMany(a => a.Split(';', StringSplitOptions.RemoveEmptyEntries)))
					{
						string relative = item.Replace("$(MSBuildThisFileDirectory)", "").Replace('\\', '/');
						string full = Path.GetFullPath(Path.Combine(dir, relative.Replace('/', Path.DirectorySeparatorChar)));
						string repoRelative = Path.GetRelativePath(root, full).Replace(Path.DirectorySeparatorChar, '/');
						if (repoRelative.StartsWith("mac/", StringComparison.Ordinal) || Regex.IsMatch(repoRelative, "(^|/)(obj|bin)/"))
						{
							continue;
						}
						result.Add(repoRelative);
					}
				}
			}
			return result.Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();
		}

		/// <summary>Every .cs file below a mac/*/Replacements folder, repo-relative.</summary>
		public static List<string> ReplacementFiles(string root)
		{
			return Directory.GetFiles(Path.Combine(root, "mac"), "*.cs", SearchOption.AllDirectories)
				.Select(f => Path.GetRelativePath(root, f).Replace(Path.DirectorySeparatorChar, '/'))
				.Where(f => Regex.IsMatch(f, "^mac/[^/]+/Replacements/") && !Regex.IsMatch(f, "(^|/)(obj|bin)/"))
				.OrderBy(x => x, StringComparer.Ordinal).ToList();
		}

		/// <summary>Lines of a source file without its leading comment header (the explanation that a replacement starts with).</summary>
		public static string[] BodyLines(string path)
		{
			string[] lines = Normalize(path).Split('\n');
			int start = 0;
			while (start < lines.Length && (lines[start].StartsWith("//", StringComparison.Ordinal) || lines[start].Trim().Length == 0))
			{
				start++;
			}
			return lines.Skip(start).ToArray();
		}

		/// <summary>Number of lines that differ between two texts: lines only in the first plus lines only in the second (LCS based).</summary>
		public static int ChangedLines(string[] a, string[] b)
		{
			var lcs = new int[a.Length + 1, b.Length + 1];
			for (int i = a.Length - 1; i >= 0; i--)
			{
				for (int j = b.Length - 1; j >= 0; j--)
				{
					lcs[i, j] = a[i] == b[j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
				}
			}
			return a.Length + b.Length - 2 * lcs[0, 0];
		}
	}
}
