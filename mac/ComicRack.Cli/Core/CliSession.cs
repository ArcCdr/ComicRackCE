using System;
using System.IO;
using ComicRack.Cli.Fixtures;

namespace ComicRack.Cli.Core
{
	/// <summary>
	/// Where a Core command keeps its data, cache and logs. By default a throw-away folder under the system temp folder,
	/// deleted at the end, so a command never touches ~/Library/Application Support/ComicRack CE (MVP) by accident.
	/// `--data-dir D` (or the environment variable CRCLI_DATA_DIR) keeps everything under D; `--app-folders` uses the real
	/// folders of the MVP app.
	///
	/// SystemPaths also creates a folder of its own for the entry assembly; with a session folder (MacPaths.Under) it is built with
	/// useLocal, so that folder is &lt;executable folder&gt;/Data and the user's Application Support folder stays untouched.
	/// </summary>
	internal sealed class CliSession : IDisposable
	{
		public const string DataDirVariable = "CRCLI_DATA_DIR";

		private readonly string deleteRoot;

		public ComicRack.Mac.Host.MacPaths Paths { get; }

		public string Root { get; }

		public bool UsesAppFolders { get; }

		private CliSession(ComicRack.Mac.Host.MacPaths paths, string root, bool appFolders, string deleteRoot)
		{
			Paths = paths;
			Root = root;
			UsesAppFolders = appFolders;
			this.deleteRoot = deleteRoot;
		}

		public static CliSession Create(Options o)
		{
			if (o.Flag("app-folders"))
			{
				return new CliSession(ComicRack.Mac.Host.MacPaths.ForCurrentUser(), null, appFolders: true, deleteRoot: null);
			}
			string dir = o.Get("data-dir") ?? Environment.GetEnvironmentVariable(DataDirVariable);
			if (!string.IsNullOrWhiteSpace(dir))
			{
				dir = Path.GetFullPath(dir);
				Directory.CreateDirectory(dir);
				return new CliSession(ComicRack.Mac.Host.MacPaths.Under(dir), dir, appFolders: false, deleteRoot: null);
			}
			string temp = Path.Combine(Path.GetTempPath(), "crcli-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(temp);
			return new CliSession(ComicRack.Mac.Host.MacPaths.Under(temp), temp, appFolders: false, deleteRoot: temp);
		}

		public void Dispose()
		{
			if (deleteRoot != null)
			{
				try
				{
					Directory.Delete(deleteRoot, recursive: true);
				}
				catch (Exception)
				{
				}
			}
		}
	}
}
