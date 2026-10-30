using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ComicRack.Mac.Host.Scripting
{
	/// <summary>What one <see cref="PathAliases.Sync"/> did.</summary>
	public sealed class PathAliasReport
	{
		public int Created { get; internal set; }

		public int Removed { get; internal set; }

		public int Adopted { get; internal set; }

		public List<string> Errors { get; } = new List<string>();

		public bool Changed => Created + Removed + Adopted > 0;

		public override string ToString() => Created + " created, " + Removed + " removed, " + Adopted + " moved to their real name" + (Errors.Count > 0 ? ", " + Errors.Count + " failed" : "");
	}

	/// <summary>
	/// Windows path separators in file names (P4.17.5). Plugins join a folder and a file name with a backslash (<c>path + "\App.Config"</c>,
	/// <c>SCRIPTDIRECTORY + "\\home_32.png"</c>). On macOS the backslash is part of the file name, so every path API looks for a file with that odd name and
	/// .NET static members cannot be intercepted. The way out is to make the odd name exist:
	/// <list type="bullet">
	/// <item>For every string constant <c>\F</c> (a backslash and a file name, optionally with sub folders) in a script <c>D/x.py</c>, where <c>D/F</c> exists, <c>D/\F</c> is a
	/// symbolic link to <c>F</c> (the plugin joined the folder with a trailing separator, as <c>__file__[:-len('x.py')] + "\F"</c> does).</item>
	/// <item>When D is a folder inside a script folder, <c>parent/D\F</c> is a link to <c>D/F</c> too (the plugin joined the folder without a trailing separator, as
	/// <c>FileInfo(__file__).DirectoryName + "\F"</c> does).</item>
	/// <item>A file that the plugin creates under such a name at run time (a regular file <c>D/\F</c> or <c>parent/D\F</c>) is moved to its real name <c>D/F</c> and the odd name
	/// becomes a link to it, so that <c>\App.Config</c> and <c>App.Config</c> (which a plugin may write by the other spelling) are one file, as they are on Windows, and a file
	/// that <c>KeepFiles</c> of a package names survives an update.</item>
	/// </list>
	/// Links are made only for names that scripts use and only for files that exist (a link to a missing file would make <c>File.Exists</c> true, and the plugin would not create it).
	/// Links to <c>.py</c> and <c>.xml</c> files are never made (the script engine would load them twice). Only links with a relative target and a backslash in their name are removed
	/// again: when their file is gone, or when scripts name them with another target. A link that no script names (the plugin builds the name at run time) stays as long as its file
	/// exists, through restarts and package updates. Nothing here changes a script.
	/// </summary>
	public static class PathAliases
	{
		private static readonly Regex Candidate = new Regex(@"(?<prefix>(?<!\w)[rRuUbB]{0,2})(?<quote>[""'])(?<body>(?:\\{1,2}[A-Za-z0-9_][\w .\-]*)+)\k<quote>", RegexOptions.CultureInvariant);

		private static readonly Regex Name = new Regex(@"^\\[A-Za-z0-9_][\w .\-]*(\\[A-Za-z0-9_][\w .\-]*)*\.[A-Za-z0-9]{1,6}$", RegexOptions.CultureInvariant);

		/// <summary>True for a decoded string constant that is a backslash followed by a file name (and sub folders): <c>\App.Config</c>, <c>\Images\x.png</c>.</summary>
		public static bool IsBackslashFileName(string text) => text != null && text.Length <= 260 && Name.IsMatch(text);

		/// <summary>True when <see cref="Sync"/> makes a link for this string constant (when the file exists): not for the extensions the script engine loads by itself.</summary>
		public static bool HasAlias(string text)
		{
			if (!IsBackslashFileName(text))
			{
				return false;
			}
			string extension = Path.GetExtension(text);
			return !extension.Equals(".py", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".xml", StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>The string constants of a Python 2 script that are a backslash and a file name, as the program sees them (escape sequences resolved), each once, in order of appearance.</summary>
		public static IReadOnlyList<string> Literals(string script)
		{
			var found = new List<string>();
			if (string.IsNullOrEmpty(script) || script.IndexOf('\\') < 0)
			{
				return found;
			}
			foreach (string line in script.Split('\n'))
			{
				if (line.IndexOf('\\') < 0 || line.TrimStart().StartsWith("#", StringComparison.Ordinal))
				{
					continue;
				}
				foreach (Match m in Candidate.Matches(line))
				{
					string decoded = Decode(m.Groups["body"].Value, m.Groups["prefix"].Value);
					if (decoded != null && IsBackslashFileName(decoded) && !found.Contains(decoded))
					{
						found.Add(decoded);
					}
				}
			}
			return found;
		}

		private static string Decode(string body, string prefix)
		{
			bool raw = prefix.IndexOfAny(new[] { 'r', 'R' }) >= 0;
			bool unicode = prefix.IndexOfAny(new[] { 'u', 'U' }) >= 0;
			if (raw)
			{
				return body.Contains("\\\\") ? null : body;
			}
			var sb = new StringBuilder();
			for (int i = 0; i < body.Length; i++)
			{
				char c = body[i];
				if (c != '\\')
				{
					sb.Append(c);
					continue;
				}
				char next = body[i + 1];
				if (next == '\\')
				{
					sb.Append('\\');
					i++;
				}
				else if ("abfnrtvx01234567".IndexOf(next) >= 0 || (unicode && "NuU".IndexOf(next) >= 0))
				{
					return null;
				}
				else
				{
					sb.Append('\\');
				}
			}
			return sb.ToString();
		}

		/// <summary>Brings the aliases of a script folder (the user's, an extra one; not the folder of the program's own scripts) in line with its scripts. Never throws.</summary>
		public static PathAliasReport Sync(string root)
		{
			var report = new PathAliasReport();
			if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
			{
				return report;
			}
			try
			{
				root = Path.GetFullPath(root);
				List<string> dirs = Folders(root);
				foreach (string dir in dirs)
				{
					Run(report, () => Adopt(dir, report, withSiblings: dir != root));
				}
				var wanted = new Dictionary<string, string>(StringComparer.Ordinal);
				foreach (string dir in dirs)
				{
					Run(report, () => Wanted(root, dir, wanted));
				}
				foreach (string dir in dirs)
				{
					foreach (string entry in Entries(dir).Where(e => Path.GetFileName(e).Contains('\\')))
					{
						string target = LinkTarget(entry);
						if (target != null && IsOurTarget(target) && !Keeps(entry, target, wanted))
						{
							Run(report, () =>
							{
								File.Delete(entry);
								report.Removed++;
							});
						}
					}
				}
				foreach (KeyValuePair<string, string> alias in wanted.OrderBy(a => a.Key, StringComparer.Ordinal))
				{
					if (new FileInfo(alias.Key).Exists || Directory.Exists(alias.Key) || LinkTarget(alias.Key) != null)
					{
						continue;
					}
					Run(report, () =>
					{
						File.CreateSymbolicLink(alias.Key, alias.Value);
						report.Created++;
					});
				}
			}
			catch (Exception ex)
			{
				report.Errors.Add(ex.Message);
			}
			return report;
		}

		/// <summary>
		/// Moves the files that plugins created under a Windows spelling inside <paramref name="folder"/> to their real names: <c>folder/\F</c> to <c>folder/F</c>, and
		/// <c>folder/Sub\F</c> (a file named like a folder inside it) to <c>folder/Sub/F</c>. When the real file exists too, the newer of the two stays. Called by
		/// <see cref="Sync"/>, and by the package manager before it replaces or removes a package folder (data a plugin kept must not be lost or left behind).
		/// </summary>
		public static void Adopt(string folder, PathAliasReport report = null, bool withSiblings = true)
		{
			report = report ?? new PathAliasReport();
			if (!Directory.Exists(folder))
			{
				return;
			}
			folder = Path.GetFullPath(folder);
			foreach (string file in Directory.GetFiles(folder).ToArray())
			{
				string name = Path.GetFileName(file);
				if (name.IndexOf('\\') < 0 || LinkTarget(file) != null)
				{
					continue;
				}
				string real = RealPath(folder, name);
				if (real != null)
				{
					Run(report, () =>
					{
						Merge(file, real);
						report.Adopted++;
						bool sub = name[0] != '\\';
						LinkAll(sub ? Path.Combine(folder, name.Substring(0, name.IndexOf('\\'))) : folder, real, withSiblings || sub, file);
					});
				}
			}
			// the same names in the folder next to it: parent/Name\F belongs to parent/Name/F
			string parent = Path.GetDirectoryName(folder);
			string self = Path.GetFileName(folder);
			if (!withSiblings || parent == null || self.Length == 0)
			{
				return;
			}
			foreach (string file in Directory.GetFiles(parent).ToArray())
			{
				string name = Path.GetFileName(file);
				if (!name.StartsWith(self + "\\", StringComparison.Ordinal) || LinkTarget(file) != null)
				{
					continue;
				}
				string real = RealPath(folder, name.Substring(self.Length));
				if (real != null)
				{
					Run(report, () =>
					{
						Merge(file, real);
						report.Adopted++;
						LinkAll(folder, real, withSiblings, file);
					});
				}
			}
		}

		/// <summary>Removes the links that point into <paramref name="folder"/> from the folder next to it (called before the folder of a package is removed, after <see cref="Adopt"/>).</summary>
		public static void RemoveSiblingLinks(string folder)
		{
			string parent = Path.GetDirectoryName(Path.GetFullPath(folder));
			string self = Path.GetFileName(Path.GetFullPath(folder));
			if (parent == null || self.Length == 0 || !Directory.Exists(parent))
			{
				return;
			}
			foreach (string entry in Directory.GetFileSystemEntries(parent).Where(e => Path.GetFileName(e).StartsWith(self + "\\", StringComparison.Ordinal)))
			{
				string target = LinkTarget(entry);
				if (target != null && IsOurTarget(target) && target.StartsWith(self + "/", StringComparison.Ordinal))
				{
					try
					{
						File.Delete(entry);
					}
					catch (Exception)
					{
					}
				}
			}
		}

		private static void Wanted(string root, string dir, Dictionary<string, string> wanted)
		{
			var literals = new List<string>();
			foreach (string script in Directory.GetFiles(dir, "*.py").Where(f => LinkTarget(f) == null).OrderBy(f => f, StringComparer.Ordinal))
			{
				foreach (string literal in Literals(File.ReadAllText(script)))
				{
					if (!literals.Contains(literal))
					{
						literals.Add(literal);
					}
				}
			}
			string self = Path.GetFileName(dir);
			foreach (string literal in literals)
			{
				string relative = literal.Substring(1).Replace('\\', '/');
				if (!HasAlias(literal))
				{
					continue;
				}
				string real = Path.Combine(dir, relative.Replace('/', Path.DirectorySeparatorChar));
				FileInfo info = new FileInfo(real);
				if (!info.Exists || info.LinkTarget != null)
				{
					continue;
				}
				wanted[dir + "/" + literal] = relative;
				if (!string.Equals(dir, root, StringComparison.Ordinal))
				{
					wanted[Path.GetDirectoryName(dir) + "/" + self + literal] = self + "/" + relative;
				}
			}
		}

		/// <summary>
		/// Whether the removal pass keeps a link of ours. A link that scripts name (a string constant) stays when its target is the one they ask for. A link that no
		/// script names is the odd name of a file that a plugin created at run time under a name it builds itself (<c>folder + "\\" + name</c>): it stays as long as the file
		/// exists, otherwise the plugin would start from defaults at every start. It goes with its file (a link to a missing file would make <c>File.Exists</c> true).
		/// </summary>
		private static bool Keeps(string entry, string target, Dictionary<string, string> wanted)
		{
			if (wanted.TryGetValue(entry, out string want))
			{
				return want == target;
			}
			string extension = Path.GetExtension(target);
			if (extension.Equals(".py", StringComparison.OrdinalIgnoreCase) || extension.Equals(".xml", StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}
			FileInfo file = new FileInfo(Path.Combine(Path.GetDirectoryName(entry), target.Replace('/', Path.DirectorySeparatorChar)));
			return file.Exists && file.LinkTarget == null;
		}

		/// <summary>
		/// After a file was adopted: every spelling a plugin may use for it links to the real file: the name it was found under, <c>owner/\F</c> and, beside the owner folder,
		/// <c>parent/Name\F</c>. The owner is the folder whose name the plugin joined with a backslash.
		/// </summary>
		private static void LinkAll(string owner, string real, bool withSiblings, string found)
		{
			Link(found, real);
			string relative = Path.GetRelativePath(owner, real).Replace(Path.DirectorySeparatorChar, '\\');
			Link(Path.Combine(owner, "\\" + relative), real);
			string parent = Path.GetDirectoryName(owner);
			if (withSiblings && parent != null && Path.GetFileName(owner).Length > 0)
			{
				Link(Path.Combine(parent, Path.GetFileName(owner) + "\\" + relative), real);
			}
		}

		/// <summary>Makes <paramref name="odd"/> a relative link to the file <paramref name="real"/> that took its content (not for names the script engine loads by itself).</summary>
		private static void Link(string odd, string real)
		{
			string extension = Path.GetExtension(real);
			if (extension.Equals(".py", StringComparison.OrdinalIgnoreCase) || extension.Equals(".xml", StringComparison.OrdinalIgnoreCase))
			{
				return;
			}
			if (!File.Exists(real) || new FileInfo(real).LinkTarget != null || new FileInfo(odd).Exists || Directory.Exists(odd) || LinkTarget(odd) != null)
			{
				return;
			}
			File.CreateSymbolicLink(odd, Path.GetRelativePath(Path.GetDirectoryName(odd), real).Replace(Path.DirectorySeparatorChar, '/'));
		}

		/// <summary>The odd names that link to files of <paramref name="folder"/> (inside it and beside it). Take it before an update replaces the folder's files and give it to <see cref="Restore"/> afterwards.</summary>
		public static IReadOnlyList<string> Remember(string folder)
		{
			var names = new List<string>();
			folder = Path.GetFullPath(folder);
			string parent = Path.GetDirectoryName(folder);
			string self = Path.GetFileName(folder);
			if (Directory.Exists(folder))
			{
				foreach (string entry in Entries(folder).Where(e => Path.GetFileName(e).Contains('\\') && IsOurLink(e)))
				{
					names.Add("in:" + Path.GetFileName(entry));
				}
			}
			if (parent != null && self.Length > 0 && Directory.Exists(parent))
			{
				foreach (string entry in Entries(parent).Where(e => Path.GetFileName(e).StartsWith(self + "\\", StringComparison.Ordinal) && IsOurLink(e)))
				{
					names.Add("beside:" + Path.GetFileName(entry).Substring(self.Length));
				}
			}
			return names;
		}

		/// <summary>Makes the odd names that <see cref="Remember"/> returned again, for the files that exist (the new version of a package keeps the files that its <c>KeepFiles</c> names). Never throws.</summary>
		public static void Restore(string folder, IReadOnlyList<string> names)
		{
			folder = Path.GetFullPath(folder);
			string parent = Path.GetDirectoryName(folder);
			string self = Path.GetFileName(folder);
			foreach (string entry in names)
			{
				try
				{
					bool inside = entry.StartsWith("in:", StringComparison.Ordinal);
					string name = entry.Substring(inside ? 3 : 7);
					string real = RealPath(folder, name);
					if (real != null)
					{
						Link(inside ? Path.Combine(folder, name) : Path.Combine(parent, self + name), real);
					}
				}
				catch (Exception)
				{
				}
			}
		}

		private static bool IsOurLink(string path)
		{
			string target = LinkTarget(path);
			return target != null && IsOurTarget(target);
		}

		private static string RealPath(string folder, string name)
		{
			string[] parts = name.TrimStart('\\').Split('\\');
			if (parts.Any(p => p.Length == 0 || p == "." || p == ".."))
			{
				return null;
			}
			if (name[0] != '\\')
			{
				// "Sub\F": only for a folder that exists
				if (!Directory.Exists(Path.Combine(folder, parts[0])) || LinkTarget(Path.Combine(folder, parts[0])) != null)
				{
					return null;
				}
			}
			return Path.Combine(folder, string.Join("/", parts));
		}

		private static void Merge(string source, string real)
		{
			if (Directory.Exists(real))
			{
				return;
			}
			string dir = Path.GetDirectoryName(real);
			Directory.CreateDirectory(dir);
			if (File.Exists(real))
			{
				if (File.GetLastWriteTimeUtc(source) > File.GetLastWriteTimeUtc(real))
				{
					File.Move(source, real, overwrite: true);
				}
				else
				{
					File.Delete(source);
				}
			}
			else
			{
				File.Move(source, real);
			}
		}

		private static List<string> Folders(string root)
		{
			var result = new List<string> { root };
			for (int i = 0; i < result.Count; i++)
			{
				foreach (string sub in Directory.GetDirectories(result[i]).OrderBy(d => d, StringComparer.Ordinal))
				{
					if (!Path.GetFileName(sub).StartsWith('.') && new DirectoryInfo(sub).LinkTarget == null)
					{
						result.Add(sub);
					}
				}
			}
			return result;
		}

		private static IEnumerable<string> Entries(string dir)
		{
			try
			{
				return Directory.GetFileSystemEntries(dir).ToArray();
			}
			catch (Exception)
			{
				return Array.Empty<string>();
			}
		}

		private static string LinkTarget(string path)
		{
			try
			{
				return new FileInfo(path).LinkTarget;
			}
			catch (Exception)
			{
				return null;
			}
		}

		/// <summary>A link made here: a relative target with forward slashes and no way out of the folder.</summary>
		private static bool IsOurTarget(string target)
		{
			return target.Length > 0 && !Path.IsPathRooted(target) && !target.Contains('\\') && !target.Split('/').Any(p => p.Length == 0 || p == "." || p == "..");
		}

		private static void Run(PathAliasReport report, Action action)
		{
			try
			{
				action();
			}
			catch (Exception ex)
			{
				report.Errors.Add(ex.GetType().Name + ": " + ex.Message);
			}
		}
	}
}
