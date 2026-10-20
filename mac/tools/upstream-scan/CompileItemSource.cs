using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace UpstreamScan
{
	public static class CompileItemSource
	{
		private static readonly string[] Projects =
		{
			"mac/cYo.Common.Core/cYo.Common.Core.csproj",
			"mac/ComicRack.Engine.Core/ComicRack.Engine.Core.csproj",
			"mac/ComicRack.Plugins.Core/ComicRack.Plugins.Core.csproj",
		};

		/// <summary>Pass B compile items and embedded resources of the three shadow projects, as repo-relative paths with / separators.
		/// Only files of the original folders are returned (files under mac/ and generated files under obj/ are ours or not sources).</summary>
		public static void FromMsBuild(string repoDir, out HashSet<string> compile, out HashSet<string> embedded)
		{
			compile = new HashSet<string>(StringComparer.Ordinal);
			embedded = new HashSet<string>(StringComparer.Ordinal);
			string root = Path.GetFullPath(repoDir).TrimEnd('/') + "/";
			foreach (var project in Projects)
			{
				string file = Path.Combine(repoDir, project);
				if (!File.Exists(file))
					throw new FileNotFoundException("Shadow project not found: " + project);
				var psi = new ProcessStartInfo("dotnet")
				{
					WorkingDirectory = Path.GetDirectoryName(file),
					RedirectStandardOutput = true,
					RedirectStandardError = true,
					UseShellExecute = false,
					StandardOutputEncoding = new UTF8Encoding(false),
				};
				foreach (var a in new[] { "msbuild", file, "-p:CensusPass=B", "-getItem:Compile", "-getItem:EmbeddedResource", "-nologo" })
					psi.ArgumentList.Add(a);
				using var p = Process.Start(psi);
				var err = p.StandardError.ReadToEndAsync();
				string json = p.StandardOutput.ReadToEnd();
				p.WaitForExit();
				if (p.ExitCode != 0)
					throw new InvalidOperationException("dotnet msbuild " + project + " failed: " + (json + err.Result).Trim());
				using var doc = JsonDocument.Parse(json);
				var items = doc.RootElement.GetProperty("Items");
				Collect(items, "Compile", root, compile);
				Collect(items, "EmbeddedResource", root, embedded);
			}
			compile.UnionWith(embedded);
		}

		private static void Collect(JsonElement items, string name, string root, HashSet<string> into)
		{
			if (!items.TryGetProperty(name, out var arr))
				return;
			foreach (var item in arr.EnumerateArray())
			{
				string full = item.GetProperty("FullPath").GetString();
				if (full == null || !full.StartsWith(root, StringComparison.Ordinal))
					continue;
				string rel = full.Substring(root.Length).Replace('\\', '/');
				if (rel.StartsWith("mac/", StringComparison.Ordinal) || rel.Contains("/obj/") || rel.Contains("/bin/"))
					continue;
				into.Add(rel);
			}
		}

		/// <summary>One repo-relative path per line; lines that are empty or start with # are ignored.</summary>
		public static void FromFile(string file, out HashSet<string> compile, out HashSet<string> embedded)
		{
			compile = new HashSet<string>(StringComparer.Ordinal);
			embedded = new HashSet<string>(StringComparer.Ordinal);
			foreach (var raw in File.ReadAllLines(file))
			{
				string line = raw.Trim();
				if (line.Length == 0 || line[0] == '#')
					continue;
				compile.Add(line.Replace('\\', '/'));
			}
		}
	}
}
