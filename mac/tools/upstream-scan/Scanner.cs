using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace UpstreamScan
{
	public static class Scanner
	{
		/// <summary>Folders of the three originals that the shadow projects glob.</summary>
		private static readonly string[] SourceFolders = { "cYo.Common/", "ComicRack.Engine/", "ComicRack.Plugins/" };

		/// <summary>Windows UI folders (section 5.2): not linked, except for single files that Sources.props names.</summary>
		private static readonly string[] UiFolders =
		{
			"ComicRack/", "cYo.Common.Windows/", "cYo.Common.Presentation/", "ComicRack.Engine.Display.Forms/",
			"ComicRack.Engine/Controls/", "ComicRack.Plugins/Controls/",
		};

		public static ScanResult Run(ScanOptions options)
		{
			string repo = options.RepoDir ?? throw new ArgumentException("RepoDir is required");
			string range = options.Range ?? throw new ArgumentException("Range is required");
			int dots = range.IndexOf("..", StringComparison.Ordinal);
			if (dots <= 0 || dots >= range.Length - 2)
				throw new ArgumentException("Range must look like A..B");
			bool threeDots = range.Length > dots + 2 && range[dots + 2] == '.';
			string a = range.Substring(0, dots);
			string b = range.Substring(dots + (threeDots ? 3 : 2));
			string from = Git.Run(repo, false, "rev-parse", "--verify", a + "^{commit}").Trim();
			string to = Git.Run(repo, false, "rev-parse", "--verify", b + "^{commit}").Trim();
			string oldRev = threeDots ? Git.Run(repo, false, "merge-base", from, to).Trim() : from;

			var manifest = Manifest.Load(repo);
			HashSet<string> compile = options.CompileItems, embedded = options.EmbeddedItems ?? new HashSet<string>();
			if (compile == null)
				CompileItemSource.FromMsBuild(repo, out compile, out embedded);
			string sourcesText = ReadMacSources(repo);

			var result = new ScanResult { Range = range, From = from, To = to };
			var ctx = new Context { Repo = repo, Manifest = manifest, Compile = compile, Embedded = embedded, SourcesText = sourcesText, To = to };

			string raw = Git.Run(repo, false, "diff", "--name-status", "-M", "-z", oldRev, to);
			var parts = raw.Split('\0', StringSplitOptions.RemoveEmptyEntries);
			for (int i = 0; i < parts.Length; i++)
			{
				string status = parts[i];
				char kind = status[0];
				if (kind == 'R' || kind == 'C')
				{
					string oldPath = parts[++i], newPath = parts[++i];
					if (kind == 'R')
						result.Files.Add(Classify(ctx, "D", oldPath, null, newPath));
					result.Files.Add(Classify(ctx, "A", newPath, kind == 'R' ? oldPath : null, null));
				}
				else
				{
					string path = parts[++i];
					string st = kind == 'T' ? "M" : kind.ToString();
					result.Files.Add(Classify(ctx, st, path, null, null));
				}
			}

			result.Files = result.Files.OrderBy(f => Array.IndexOf(Categories.All, f.Category)).ThenBy(f => f.Path, StringComparer.Ordinal).ToList();

			// Project files: reference changes against the mac mirrors.
			foreach (var f in result.Files.Where(f => f.Category == Categories.ProjectFiles).ToList())
			{
				Packages.MacMirror.TryGetValue(f.Path, out string macFile);
				string oldPath = f.RenamedFrom ?? f.Path;
				string oldXml = f.Status == "A" && f.RenamedFrom == null ? null : Git.Show(repo, oldRev, oldPath);
				string newXml = f.Status == "D" ? null : Git.Show(repo, to, f.Path);
				string macXml = macFile != null && File.Exists(Path.Combine(repo, macFile)) ? File.ReadAllText(Path.Combine(repo, macFile)) : null;
				var changes = Packages.Compare(f.Path, macFile, oldXml, newXml, macXml);
				result.Packages.AddRange(changes);
				DescribeProject(f, macFile, changes);
			}

			foreach (var c in Categories.All)
				result.Counts[c] = result.Files.Count(f => f.Category == c);
			return result;
		}

		private sealed class Context
		{
			public string Repo, To, SourcesText;
			public Manifest Manifest;
			public HashSet<string> Compile, Embedded;
		}

		private static string ReadMacSources(string repo)
		{
			var sb = new System.Text.StringBuilder();
			string mac = Path.Combine(repo, "mac");
			if (!Directory.Exists(mac))
				return "";
			foreach (var dir in Directory.GetDirectories(mac))
			{
				foreach (var f in Directory.GetFiles(dir, "*.csproj").Concat(Directory.GetFiles(dir, "*.props")))
					sb.AppendLine(File.ReadAllText(f).Replace('\\', '/'));
			}
			return sb.ToString();
		}

		private static bool StartsWithAny(string path, string[] prefixes)
		{
			foreach (var p in prefixes)
				if (path.StartsWith(p, StringComparison.Ordinal))
					return true;
			return false;
		}

		private static FileEntry Classify(Context c, string status, string path, string renamedFrom, string renamedTo)
		{
			var e = new FileEntry { Status = status, Path = path, RenamedFrom = renamedFrom, RenamedTo = renamedTo };
			Fill(c, e);
			if (renamedFrom != null)
				e.Detail = (e.Detail == null ? "" : e.Detail + "; ") + "renamed from " + renamedFrom;
			else if (renamedTo != null)
				e.Detail = (e.Detail == null ? "" : e.Detail + "; ") + "renamed to " + renamedTo;
			return e;
		}

		private static void Fill(Context c, FileEntry e)
		{
			string path = e.Path;
			string ext = Path.GetExtension(path).ToLowerInvariant();

			// 1. Ours.
			if (path.StartsWith("mac/", StringComparison.Ordinal) || path == "MACOS_NATIVE_PATH.md" || path.StartsWith(".github/workflows/mac-", StringComparison.Ordinal))
			{
				e.Category = Categories.Ours;
				return;
			}

			// 2. Project files.
			if (ext == ".csproj" || ext == ".vbproj" || ext == ".fsproj")
			{
				e.Category = Categories.ProjectFiles;
				return;
			}

			// 3. Manifest: excluded and replaced originals.
			var m = c.Manifest.Find(path);
			if (m != null)
			{
				string tag = e.Status == "A" ? "new file" : e.Status == "D" ? "deleted" : "changed";
				if (m.IsExcluded)
				{
					e.Category = Categories.Excluded;
					e.Detail = "excluded by " + m.Name + " (" + tag + ")";
					e.Action = m.Pattern != null
						? "manifest hash of " + m.Pattern + " changes: review, then mac/tools/replacement-sync.sh '" + m.Pattern + "' --accept"
						: (e.Status == "M" ? "recorded hash changes: mac/tools/replacement-sync.sh " + m.Original + " --accept" : "excluded file " + tag + ": update the manifest entry");
				}
				else
				{
					e.Category = Categories.Replaced;
					e.Detail = m.Kind + " -> " + (m.Replacement ?? "(no replacement file)") + " (" + tag + ")";
					e.Action = "mac/tools/replacement-sync.sh " + (m.Original ?? m.Pattern);
				}
				return;
			}

			// 4. Compile items of the three shadow projects (pass B).
			if (c.Compile.Contains(path))
			{
				e.Category = Categories.Linked;
				e.Detail = c.Embedded.Contains(path) ? "embedded resource of a shadow project" : "compiled by a shadow project (pass B)";
				if (e.Status == "A") e.Compiles = true;
				if (e.Status == "D" && c.SourcesText.Contains(path))
				{
					e.Detail = "deleted, but a Sources.props still links it";
					e.Action = "remove the Compile item from the Sources.props (the shadow build breaks) and check what used it";
				}
				return;
			}

			// A deleted file cannot be in the compile items of the current tree: look at the Sources.props that name single files.
			if (e.Status == "D" && StartsWithAny(path, UiFolders) && c.SourcesText.Contains(path))
			{
				e.Category = Categories.Linked;
				e.Detail = "deleted, but a Sources.props still links it";
				e.Action = "remove the Compile item from the Sources.props (the shadow build breaks) and check what used it";
				return;
			}

			// 5. Payload.
			if (path.StartsWith("ComicRack/Output/", StringComparison.Ordinal))
			{
				e.Category = Categories.Payload;
				string rest = path.Substring("ComicRack/Output/".Length);
				e.Detail = rest.Split('/')[0];
				return;
			}

			// 6. Windows UI.
			if (StartsWithAny(path, UiFolders))
			{
				e.Category = Categories.UiNotLinked;
				e.Detail = "Windows UI, not built on the Mac" + (ext == ".cs" ? "" : " (" + (ext.Length == 0 ? "no extension" : ext) + ")");
				return;
			}

			// 7. Files of the three linked projects that no compile item names.
			if (StartsWithAny(path, SourceFolders))
			{
				bool isSource = ext == ".cs" && !path.Contains("/obj/") && !path.Contains("/bin/") && !path.EndsWith("/AssemblyInfo.cs", StringComparison.Ordinal);
				bool inTree = File.Exists(Path.Combine(c.Repo, path));
				if (isSource && e.Status == "D")
				{
					e.Category = Categories.Linked;
					e.Detail = "deleted source of a linked project";
					e.Action = "check that nothing in mac/ or the shadow projects names it";
					return;
				}
				if (isSource && !inTree)
				{
					// The working tree is not at the end of the range yet: the shadow glob decides.
					e.Category = Categories.Linked;
					e.Detail = "new source in a linked project folder; the shadow glob picks it up (working tree is not at the range end)";
					e.Compiles = true;
					return;
				}
				e.Category = Categories.Other;
				e.Detail = isSource ? "source in a linked project folder that no shadow project compiles" : "file of a linked project that the shadow projects do not use";
				if (isSource && e.Status == "A")
					e.Compiles = false;
				return;
			}

			// 8. Build and CI.
			string name = Path.GetFileName(path);
			if (path.StartsWith(".github/", StringComparison.Ordinal) || ext == ".yml" || ext == ".yaml" || ext == ".ps1" || ext == ".bat" || ext == ".iss"
				|| ext == ".sln" || ext == ".slnx" || ext == ".props" || ext == ".targets" || name.Equals("directory.build.props", StringComparison.OrdinalIgnoreCase)
				|| name == ".gitignore" || name == ".gitattributes" || name.Equals("nuget.config", StringComparison.OrdinalIgnoreCase) || name == "global.json")
			{
				e.Category = Categories.BuildCi;
				if (name.Equals("directory.build.props", StringComparison.OrdinalIgnoreCase) || ext == ".props" || ext == ".targets")
				{
					e.Detail = "build settings of the originals";
					e.Action = "check that mac/Directory.Build.props still stops the import and that the shadow projects are not affected";
				}
				return;
			}

			e.Category = Categories.Other;
		}

		private static void DescribeProject(FileEntry f, string macFile, List<PackageChange> changes)
		{
			var needs = changes.Where(p => p.Mac == "absent" && p.Change != "removed" || p.Mac == "different" || p.Mac == "still-referenced"
				|| (p.Mac == "replaced" && p.Note != null && p.Note.Contains("keep them in step"))).ToList();
			if (f.Status == "A")
				f.Detail = "new project file" + (changes.Count > 0 ? ", " + changes.Count + " reference(s)" : "");
			else if (f.Status == "D")
				f.Detail = "project file deleted";
			else
				f.Detail = changes.Count == 0 ? "no PackageReference or Reference change" : changes.Count + " reference change(s)";
			if (macFile == null)
				f.Detail += "; project is not built on the Mac";
			else
				f.Detail += "; mirrored by " + macFile;
			if (needs.Count > 0)
				f.Action = "update " + macFile + ": " + string.Join(", ", needs.Select(p => p.Id));
			else if (macFile != null && (f.Status == "D" || f.Status == "A"))
				f.Action = "a linked project was " + (f.Status == "A" ? "added" : "deleted") + ": review the shadow projects";
		}
	}
}
