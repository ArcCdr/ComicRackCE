using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ComicRack.Mac.Host.Scripting
{
	/// <summary>
	/// Where the scripts are: the ones the program ships (<c>Scripts/</c> next to the executable, the payload of P2.6; the original's
	/// <c>SystemPaths.ScriptPath</c>), the user's own (<c>&lt;data folder&gt;/Scripts</c>; the original's <c>ScriptPathSecondary</c>, where installed
	/// plugins go) and any further folders (a command line option, a test). They are read in this order, and when two commands have the same key the first one wins.
	/// </summary>
	public sealed class ScriptFolders
	{
		public const string FolderName = "Scripts";

		/// <summary>The folder of the scripts that come with the program. May not exist (a test program has none).</summary>
		public string Bundled { get; }

		/// <summary>The user's folder (not created by reading it).</summary>
		public string User { get; }

		/// <summary>Further folders, read after the other two.</summary>
		public IReadOnlyList<string> Extra { get; }

		public ScriptFolders(string bundled, string user, IEnumerable<string> extra = null)
		{
			Bundled = string.IsNullOrEmpty(bundled) ? null : Path.GetFullPath(bundled);
			User = string.IsNullOrEmpty(user) ? null : Path.GetFullPath(user);
			Extra = (extra ?? Enumerable.Empty<string>()).Where(p => !string.IsNullOrWhiteSpace(p)).Select(Path.GetFullPath).ToArray();
		}

		/// <summary>The folders of a host: <c>Scripts/</c> next to the executable and <c>&lt;data&gt;/Scripts</c>.</summary>
		public static ScriptFolders For(MacPaths paths, IEnumerable<string> extra = null)
		{
			return new ScriptFolders(Path.Combine(AppContext.BaseDirectory, FolderName), Path.Combine(paths.DataFolder, FolderName), extra);
		}

		/// <summary>No folder at all (scripting is on, nothing is loaded).</summary>
		public static ScriptFolders None => new ScriptFolders(null, null);

		/// <summary>Only the given folders.</summary>
		public static ScriptFolders Only(params string[] folders) => new ScriptFolders(null, null, folders);

		/// <summary>All folders in reading order, the ones that do not exist included.</summary>
		public IEnumerable<string> All
		{
			get
			{
				if (Bundled != null)
				{
					yield return Bundled;
				}
				if (User != null)
				{
					yield return User;
				}
				foreach (string e in Extra)
				{
					yield return e;
				}
			}
		}

		/// <summary>The folders that exist, in reading order, each once.</summary>
		public IEnumerable<string> Existing => All.Where(Directory.Exists).Distinct(StringComparer.Ordinal);
	}
}
