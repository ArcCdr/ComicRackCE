using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UpstreamScan
{
	public static class Report
	{
		/// <summary>Categories with more than this many files are summarized unless all is true (our own files are always many).</summary>
		private const int Collapse = 25;

		public static string ToText(ScanResult r, bool all)
		{
			var sb = new StringBuilder();
			sb.AppendLine("Upstream scan " + r.Range + " (" + Short(r.From) + ".." + Short(r.To) + "): " + r.Files.Count + " file(s)");
			foreach (var cat in Categories.All)
			{
				var files = r.Files.Where(f => f.Category == cat).ToList();
				sb.AppendLine();
				sb.AppendLine("== " + cat + ": " + files.Count);
				if (files.Count == 0)
					continue;
				if (!all && files.Count > Collapse && (cat == Categories.Ours || cat == Categories.Payload))
				{
					foreach (var g in files.GroupBy(f => Top(f.Path)).OrderBy(g => g.Key, StringComparer.Ordinal))
						sb.AppendLine("   " + g.Count().ToString().PadLeft(4) + "  " + g.Key);
					sb.AppendLine("   (use --all to list every file)");
					continue;
				}
				foreach (var f in files)
				{
					sb.Append("  " + f.Status + " " + f.Path);
					if (f.Compiles.HasValue)
						sb.Append("  [compiles: " + (f.Compiles.Value ? "yes" : "no") + "]");
					if (f.Detail != null)
						sb.Append("  - " + f.Detail);
					sb.AppendLine();
					if (f.Action != null)
						sb.AppendLine("      ACTION: " + f.Action);
				}
			}

			if (r.Packages.Count > 0)
			{
				sb.AppendLine();
				sb.AppendLine("== reference changes: " + r.Packages.Count);
				foreach (var p in r.Packages)
				{
					string ver = p.Change == "added" ? p.NewVersion : p.Change == "removed" ? p.OldVersion : p.OldVersion + " -> " + p.NewVersion;
					sb.AppendLine("  " + p.Kind + " " + p.Id + " " + p.Change + (ver != null ? " (" + ver + ")" : "") + " in " + p.Project + ": mac " + p.Mac
						+ (p.MacVersion != null ? " " + p.MacVersion : "") + (p.Note != null ? " - " + p.Note : ""));
				}
			}

			sb.AppendLine();
			int actions = r.Files.Count(f => f.Action != null);
			sb.AppendLine("SUMMARY: " + string.Join(", ", Categories.All.Where(c => r.Counts.TryGetValue(c, out int n) && n > 0).Select(c => c + " " + r.Counts[c]))
				+ (r.Files.Count == 0 ? "no changes" : "") + "; " + actions + " file(s) with an action");
			return sb.ToString();
		}

		public static string ToJson(ScanResult r)
		{
			var options = new JsonSerializerOptions
			{
				WriteIndented = true,
				DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
			};
			return JsonSerializer.Serialize(r, options);
		}

		private static string Short(string sha)
		{
			return sha.Length > 7 ? sha.Substring(0, 7) : sha;
		}

		private static string Top(string path)
		{
			var parts = path.Split('/');
			if (parts.Length <= 2) return parts[0] + (parts.Length > 1 ? "/" : "");
			return parts[0] + "/" + parts[1] + "/";
		}
	}
}
