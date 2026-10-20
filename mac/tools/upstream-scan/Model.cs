using System.Collections.Generic;

namespace UpstreamScan
{
	/// <summary>The categories of section 5.2, step 2, in report order.</summary>
	public static class Categories
	{
		public const string Ours = "macOS (ours)";
		public const string Linked = "linked";
		public const string Replaced = "replaced";
		public const string Excluded = "excluded";
		public const string UiNotLinked = "UI not linked";
		public const string ProjectFiles = "project files";
		public const string Payload = "payload";
		public const string BuildCi = "build/CI";
		public const string Other = "other";

		public static readonly string[] All = { Ours, Linked, Replaced, Excluded, UiNotLinked, ProjectFiles, Payload, BuildCi, Other };
	}

	public sealed class ScanOptions
	{
		/// <summary>Repository root (the folder that contains mac/). Default: the git top level of the current directory.</summary>
		public string RepoDir { get; set; }

		/// <summary>"A..B" (or "A...B"): the upstream range to classify.</summary>
		public string Range { get; set; }

		/// <summary>Repo-relative paths of the pass B compile items (and embedded resources) of the three shadow projects.
		/// null: ask MSBuild (<see cref="CompileItemSource.FromMsBuild"/>).</summary>
		public HashSet<string> CompileItems { get; set; }

		/// <summary>Repo-relative paths of the embedded resources among <see cref="CompileItems"/> (only used for the detail text).</summary>
		public HashSet<string> EmbeddedItems { get; set; }
	}

	public sealed class FileEntry
	{
		/// <summary>A (added), M (modified), D (deleted), T (type change). A rename is reported as D of the old path plus A of the new one.</summary>
		public string Status { get; set; }
		public string Path { get; set; }
		/// <summary>Set on both halves of a rename: the other path.</summary>
		public string RenamedFrom { get; set; }
		public string RenamedTo { get; set; }
		public string Category { get; set; }
		public string Detail { get; set; }
		/// <summary>For an added file of a linked project: would a shadow project compile it (yes) or not (no). null for other files.</summary>
		public bool? Compiles { get; set; }
		/// <summary>The follow-up a session or the owner has to do for this file; null when nothing is needed.</summary>
		public string Action { get; set; }
	}

	public sealed class PackageChange
	{
		/// <summary>Repo-relative path of the original project file.</summary>
		public string Project { get; set; }
		/// <summary>The mac file that mirrors it (Sources.props or the Plugins.Core csproj); null for a project that is not built on the Mac.</summary>
		public string MacFile { get; set; }
		/// <summary>PackageReference or Reference.</summary>
		public string Kind { get; set; }
		public string Id { get; set; }
		/// <summary>added, removed or changed.</summary>
		public string Change { get; set; }
		public string OldVersion { get; set; }
		public string NewVersion { get; set; }
		/// <summary>What the mac side has: same, different, absent, replaced, dropped or not-applicable.</summary>
		public string Mac { get; set; }
		public string MacVersion { get; set; }
		public string Note { get; set; }
	}

	public sealed class ScanResult
	{
		public string Range { get; set; }
		public string From { get; set; }
		public string To { get; set; }
		public List<FileEntry> Files { get; set; } = new List<FileEntry>();
		public List<PackageChange> Packages { get; set; } = new List<PackageChange>();
		public Dictionary<string, int> Counts { get; set; } = new Dictionary<string, int>();
	}
}
