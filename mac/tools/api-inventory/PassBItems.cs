using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace ApiInventory
{
	/// <summary>The repository files that the pass B build of the three shadow projects compiles (the same query as mac/tools/upstream-scan).</summary>
	internal static class PassBItems
	{
		private static readonly string[] Projects =
		{
			"mac/cYo.Common.Core/cYo.Common.Core.csproj",
			"mac/ComicRack.Engine.Core/ComicRack.Engine.Core.csproj",
			"mac/ComicRack.Plugins.Core/ComicRack.Plugins.Core.csproj",
		};

		/// <summary>Repo-relative paths (forward slashes) of the compile items outside mac/, obj/ and bin/.</summary>
		public static HashSet<string> Query(string repoDir)
		{
			var set = new HashSet<string>(StringComparer.Ordinal);
			string root = Path.GetFullPath(repoDir).TrimEnd('/') + "/";
			foreach (var project in Projects)
			{
				string file = Path.Combine(repoDir, project);
				var psi = new ProcessStartInfo("dotnet")
				{
					WorkingDirectory = Path.GetDirectoryName(file),
					RedirectStandardOutput = true,
					RedirectStandardError = true,
					UseShellExecute = false,
					StandardOutputEncoding = new UTF8Encoding(false),
				};
				foreach (var a in new[] { "msbuild", file, "-p:CensusPass=B", "-getItem:Compile", "-nologo" })
					psi.ArgumentList.Add(a);
				using var p = Process.Start(psi)!;
				var err = p.StandardError.ReadToEndAsync();
				string json = p.StandardOutput.ReadToEnd();
				p.WaitForExit();
				if (p.ExitCode != 0)
					throw new InvalidOperationException("dotnet msbuild " + project + " failed: " + (json + err.Result).Trim());
				using var doc = JsonDocument.Parse(json);
				foreach (var item in doc.RootElement.GetProperty("Items").GetProperty("Compile").EnumerateArray())
				{
					string full = item.GetProperty("FullPath").GetString();
					if (full == null || !full.StartsWith(root, StringComparison.Ordinal))
						continue;
					string rel = full[root.Length..].Replace('\\', '/');
					if (rel.StartsWith("mac/", StringComparison.Ordinal) || rel.Contains("/obj/") || rel.Contains("/bin/"))
						continue;
					set.Add(rel);
				}
			}
			return set;
		}
	}
}
