using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace UpstreamScan
{
	/// <summary>The entries of mac/replacements.json (P0.4) that matter for the classification.</summary>
	public sealed class Manifest
	{
		public sealed class Entry
		{
			public string Original;
			public string Pattern;
			public string Replacement;
			public string Kind;
			public Regex PatternRegex;

			public bool Matches(string path)
			{
				if (Original != null)
					return string.Equals(Original, path, StringComparison.Ordinal);
				return PatternRegex != null && PatternRegex.IsMatch(path);
			}

			public bool IsExcluded => Kind == "excluded";
			public string Name => Original ?? Pattern;
		}

		public List<Entry> Entries { get; } = new List<Entry>();

		public static Manifest Load(string repoDir)
		{
			var m = new Manifest();
			string file = System.IO.Path.Combine(repoDir, "mac", "replacements.json");
			if (!File.Exists(file))
				return m;
			using var doc = JsonDocument.Parse(File.ReadAllText(file, Encoding.UTF8));
			foreach (var e in doc.RootElement.GetProperty("entries").EnumerateArray())
			{
				var entry = new Entry
				{
					Original = Str(e, "original"),
					Pattern = Str(e, "pattern"),
					Replacement = Str(e, "replacement"),
					Kind = Str(e, "kind"),
				};
				if (entry.Pattern != null)
					entry.PatternRegex = GlobToRegex(entry.Pattern);
				if (entry.Name != null)
					m.Entries.Add(entry);
			}
			return m;
		}

		public Entry Find(string path)
		{
			foreach (var e in Entries)
				if (e.Matches(path))
					return e;
			return null;
		}

		private static string Str(JsonElement e, string name)
		{
			return e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
		}

		/// <summary>** matches any characters including /, * and ? match within one path segment.</summary>
		public static Regex GlobToRegex(string glob)
		{
			var sb = new StringBuilder("^");
			for (int i = 0; i < glob.Length; i++)
			{
				char c = glob[i];
				if (c == '*' && i + 1 < glob.Length && glob[i + 1] == '*')
				{
					i++;
					if (i + 1 < glob.Length && glob[i + 1] == '/')
					{
						sb.Append("(.*/)?");
						i++;
					}
					else
						sb.Append(".*");
				}
				else if (c == '*')
					sb.Append("[^/]*");
				else if (c == '?')
					sb.Append("[^/]");
				else
					sb.Append(Regex.Escape(c.ToString()));
			}
			sb.Append('$');
			return new Regex(sb.ToString(), RegexOptions.CultureInvariant);
		}
	}
}
