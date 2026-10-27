using ComicRack.Mac.Host.Scripting;

namespace ComicRack.Mac.Host
{
	public sealed class MacHostOptions
	{
		/// <summary>Folders of the host. Default: <see cref="MacPaths.ForCurrentUser"/>.</summary>
		public MacPaths Paths { get; set; }

		/// <summary>Defaults of the WinForms settings. Default: a new <see cref="MacSettings"/>.</summary>
		public MacSettings Settings { get; set; }

		/// <summary>
		/// The ComicDb.xml to open. Default: <see cref="MacPaths.DatabaseFile"/>. A file that does not exist gives an empty
		/// database that is written to this path when the host closes.
		/// </summary>
		public string DatabaseFile { get; set; }

		/// <summary>
		/// Never write the database: neither on close nor in the background. For tools that open a database they do not own.
		/// The Core has no read-only mode; this sets DatabaseManager.InitialConnectionError, which DatabaseManager.Save and
		/// BackupTo check (so the save on close is skipped), and starts no background save timer. DatabaseManager.SaveInBackground()
		/// does not check it: only that timer and the WinForms MainForm call it, so callers must not.
		/// </summary>
		public bool DatabaseReadOnly { get; set; }

		/// <summary>Scripting on or off for this host. Default (null): the "Scripting" preference, which is on unless the user switched it off.</summary>
		public bool? Scripting { get; set; }

		/// <summary>Where the scripts are. Default: <c>Scripts/</c> next to the executable and <c>&lt;data&gt;/Scripts</c> (<see cref="ScriptFolders.For"/>).</summary>
		public ScriptFolders ScriptFolders { get; set; }

		/// <summary>The user interface of the scripts. Default: a <see cref="HeadlessScriptUi"/>; the app sets <see cref="ScriptHost.Ui"/> when its windows exist.</summary>
		public IScriptUi ScriptUi { get; set; }

		/// <summary>Seconds between background saves, 0 for none. Default 600 (ExtendedSettings.DatabaseBackgroundSaving).</summary>
		public int DatabaseBackgroundSaveSeconds { get; set; } = 600;
	}
}
