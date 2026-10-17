using System;
using System.IO;
using cYo.Projects.ComicRack.Engine;

namespace ComicRack.Mac.Host
{
	/// <summary>
	/// The three folders of the macOS MVP (data, cache, logs). The MVP has its own folders and never touches those of a
	/// Windows or other ComicRack installation.
	/// </summary>
	public sealed class MacPaths
	{
		public const string AppFolderName = "ComicRack CE (MVP)";

		/// <summary>Database (ComicDb.xml) and every other piece of user data.</summary>
		public string DataFolder { get; }

		/// <summary>Thumbnail, page and file caches. Can be deleted without losing data.</summary>
		public string CacheFolder { get; }

		public string LogFolder { get; }

		/// <summary>The database file the host opens by default.</summary>
		public string DatabaseFile => Path.Combine(DataFolder, "ComicDb.xml");

		public string LogFile => Path.Combine(LogFolder, "ComicRack.log");

		/// <summary>
		/// False (the app): SystemPaths creates its own empty &lt;ApplicationData&gt;/&lt;company&gt;/&lt;product&gt; folder from the entry assembly.
		/// True (<see cref="Under"/>: tests, tools, portable use): SystemPaths is built with useLocal, so its own folders are
		/// &lt;folder of the executable&gt;/Data and nothing is created in the user's Application Support folder. Setting HOME does not
		/// work for this (.NET reads the home folder once).
		/// </summary>
		public bool SystemPathsUseLocal { get; }

		public MacPaths(string dataFolder, string cacheFolder, string logFolder, bool systemPathsUseLocal = false)
		{
			SystemPathsUseLocal = systemPathsUseLocal;
			DataFolder = Full(dataFolder, nameof(dataFolder));
			CacheFolder = Full(cacheFolder, nameof(cacheFolder));
			LogFolder = Full(logFolder, nameof(logFolder));
		}

		/// <summary>~/Library/Application Support, ~/Library/Caches and ~/Library/Logs, each with the MVP folder name.</summary>
		public static MacPaths ForCurrentUser()
		{
			string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
			if (string.IsNullOrEmpty(home))
			{
				home = Environment.GetEnvironmentVariable("HOME");
			}
			if (string.IsNullOrEmpty(home))
			{
				throw new InvalidOperationException("The home folder of the current user is unknown.");
			}
			string library = Path.Combine(home, "Library");
			return new MacPaths(Path.Combine(library, "Application Support", AppFolderName), Path.Combine(library, "Caches", AppFolderName), Path.Combine(library, "Logs", AppFolderName));
		}

		/// <summary>Data, cache and logs as sub folders of <paramref name="root"/> (tests, portable use).</summary>
		public static MacPaths Under(string root)
		{
			return new MacPaths(Path.Combine(root, "Data"), Path.Combine(root, "Cache"), Path.Combine(root, "Logs"), systemPathsUseLocal: true);
		}

		public void CreateFolders()
		{
			Directory.CreateDirectory(DataFolder);
			Directory.CreateDirectory(CacheFolder);
			Directory.CreateDirectory(LogFolder);
		}

		/// <summary>
		/// The Core's own path object. The database and cache folders are passed explicitly, as ComicRack/Program.cs does with
		/// the command line switches /db and /cp. The other folders of <see cref="SystemPaths"/> (ApplicationDataPath, scripts)
		/// still come from Environment.SpecialFolder (or the executable's Data folder, see <see cref="SystemPathsUseLocal"/>); the Core creates them when it builds the object.
		/// </summary>
		public SystemPaths CreateSystemPaths()
		{
			return new SystemPaths(useLocal: SystemPathsUseLocal, alternateConfig: null, databasePath: DataFolder, cachePath: CacheFolder);
		}

		private static string Full(string path, string name)
		{
			if (string.IsNullOrWhiteSpace(path))
			{
				throw new ArgumentException("A folder is required.", name);
			}
			return Path.GetFullPath(path);
		}
	}
}
