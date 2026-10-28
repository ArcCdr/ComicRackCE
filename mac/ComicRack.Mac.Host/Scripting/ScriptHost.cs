using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using cYo.Common.Net.Search;
using cYo.Common.Windows.Forms;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Engine.Database;
using cYo.Projects.ComicRack.Plugins;
using cYo.Projects.ComicRack.Plugins.Theme;

namespace ComicRack.Mac.Host.Scripting
{
	/// <summary>
	/// The script engine of an open <see cref="MacHost"/> (<c>ComicRack/ScriptUtility.cs</c> of the WinForms app): a <see cref="PluginEngine"/> that has read the script
	/// folders, the Python hooks wired into the Core (<c>ParseComicPath</c> into <see cref="ComicBook.ParseFilePath"/>, script lists into
	/// <see cref="ComicBookPluginMatcher.PluginEngine"/>, <c>NetSearch</c> into <see cref="SearchEngines"/>) and calls for the hooks that act on books
	/// (<see cref="GetCommands"/>, <see cref="Invoke"/>, <see cref="RunStartup"/>, <see cref="RunShutdown"/>). It works without a window: whatever a script asks
	/// of the user interface goes to <see cref="Ui"/> (<see cref="HeadlessScriptUi"/> until the app sets its own).
	///
	/// Scripting is switched by the "Scripting" preference (<see cref="MacHostOptions.Scripting"/> overrides it). When it is off, nothing is read, no hook is wired
	/// and script lists stay "Requires scripts". The Core keeps process-wide state for these hooks, which is why only one <see cref="MacHost"/> exists at a time; <see cref="Dispose"/> takes the hooks out again.
	/// </summary>
	public sealed class ScriptHost : IDisposable
	{
		/// <summary>The version a script sees as <c>ComicRack.App.ProductVersion</c>: the one of the Windows program (ComicRack/Properties/AssemblyInfo.cs, AssemblyInformationalVersion), because scripts compare it with the version they need.</summary>
		public const string ProductVersion = "0.9.184";

		private readonly MacHost host;
		private readonly object gate = new object();
		private readonly List<(Command command, EventHandler<ComicBook.ParseFilePathEventArgs> handler)> parseHooks = new List<(Command, EventHandler<ComicBook.ParseFilePathEventArgs>)>();
		private readonly List<ScriptSearch> searchEngines = new List<ScriptSearch>();
		private IScriptUi ui;
		private Func<Type, object> activeServices;
		private readonly IDisposable fileSystemLink;
		private bool started;
		private bool disposed;

		internal ScriptHost(MacHost host, ScriptFolders folders, IScriptUi ui, bool enabled)
		{
			this.host = host;
			// Microsoft.VisualBasic by name is the assembly of this program (FileSystem with UIOption and the Trash, P4.17.4); the Trash and the dialog are the program's.
			if (VisualBasicRuntime.IsProgramAssembly)
			{
				fileSystemLink = VisualBasicRuntime.Connect(() => Ui);
			}
			else
			{
				host.Log?.Error("Microsoft.VisualBasic resolves to the shared framework's file in this process: FileSystem keeps the framework's behaviour (UIOption is refused, no Trash).");
			}
			Folders = folders ?? ScriptFolders.None;
			this.ui = ui ?? new HeadlessScriptUi();
			Enabled = enabled;
			Engine = new PluginEngine();
			MacThemePlugin theme = new MacThemePlugin();
			Theme = theme;
			Application = new MacScriptApplication(host, () => Ui);
			Environment = new MacPluginEnvironment(() => Ui, Application, new MacScriptBrowser(() => Ui), new MacScriptOpenBooks(() => Ui),
				new MacPluginConfig(() => host.Settings.Preferences.ScriptingLibraries), theme);
		}

		/// <summary>True if scripting is on for this host (the preference, or the option of <see cref="MacHostOptions"/>).</summary>
		public bool Enabled { get; }

		/// <summary>The commands of every script that was read; empty until <see cref="Start"/> and when scripting is off.</summary>
		public PluginEngine Engine { get; }

		public ScriptFolders Folders { get; }

		/// <summary>The environment every command is initialised with (each command clones it).</summary>
		public MacPluginEnvironment Environment { get; }

		public MacScriptApplication Application { get; }

		public MacThemePlugin Theme { get; }

		/// <summary>The user interface that scripts talk to. Set by the app once its windows exist; a headless one before.</summary>
		public IScriptUi Ui
		{
			get { lock (gate) { return ui; } }
			set { lock (gate) { ui = value ?? new HeadlessScriptUi(); } }
		}

		/// <summary>The search providers that <c>NetSearch</c> scripts add (in the WinForms app they join <c>SearchEngines.Engines</c>).</summary>
		public IReadOnlyList<INetSearch> SearchEngines
		{
			get { lock (gate) { return searchEngines.ToArray(); } }
		}

		/// <summary>True after <see cref="Start"/> read the folders.</summary>
		public bool IsStarted => started;

		/// <summary>
		/// <c>ScriptUtility.Initialize</c>: reads the script folders (bundled, user, extra), applies the saved switches of the commands, and wires the hooks into the Core.
		/// Returns false when scripting is off. Calling it again does nothing.
		/// </summary>
		public bool Start()
		{
			lock (gate)
			{
				if (disposed)
				{
					throw new ObjectDisposedException(nameof(ScriptHost));
				}
				if (started)
				{
					return Enabled;
				}
				started = true;
			}
			if (!Enabled)
			{
				return false;
			}
			// FormUtility.FindActiveService<T>() of the assembly cYo.Common.Windows (RefreshView.py and others): the window the script sees is the program's.
			activeServices = type => Ui.FindActiveService(type);
			ActiveServices.Resolver = activeServices;
			foreach (string folder in Folders.Existing)
			{
				try
				{
					Engine.Initialize(Environment, folder);
				}
				catch (Exception ex)
				{
					host.Log?.Error("The scripts in " + folder + " could not be read.", ex);
				}
			}
			Engine.CommandStates = host.Settings.Preferences.PluginsStates;
			ComicBookPluginMatcher.PluginEngine = Engine;
			foreach (Command command in Engine.GetCommands(PluginEngine.ScriptTypeParseComicPath).ToArray())
			{
				Command c = command;
				EventHandler<ComicBook.ParseFilePathEventArgs> handler = (sender, e) => c.Invoke(new object[] { e.Path, e.NameInfo }, catchErrors: true);
				ComicBook.ParseFilePath += handler;
				parseHooks.Add((c, handler));
			}
			foreach (Command command in Engine.GetCommands(PluginEngine.ScriptTypeSearch).ToArray())
			{
				searchEngines.Add(new ScriptSearch(command));
			}
			host.Log?.Info("Scripting: " + Engine.GetAllCommands().Count() + " commands from " + string.Join(", ", Folders.Existing));
			return true;
		}

		/// <summary>The enabled commands of a hook (<see cref="PluginEngine.ScriptTypeBooks"/> and the others), in menu order. Empty when scripting is off.</summary>
		public IEnumerable<Command> GetCommands(string hook)
		{
			try
			{
				return Enabled ? Engine.GetCommands(hook).ToArray() : Enumerable.Empty<Command>();
			}
			catch (Exception)
			{
				return Enumerable.Empty<Command>();
			}
		}

		/// <summary>
		/// <c>ScriptUtility.Invoke</c>: calls every enabled command of the hook with the arguments. An exception ends the call (the commands after the failing one do not
		/// run, as in the original) and is logged; false is returned then.
		/// </summary>
		public bool Invoke(string hook, params object[] args)
		{
			if (!Enabled)
			{
				return false;
			}
			try
			{
				Engine.Invoke(hook, args ?? new object[0]);
				return true;
			}
			catch (Exception ex)
			{
				host.Log?.Error("A script of the hook " + hook + " failed.", ex);
				return false;
			}
		}

		/// <summary><c>MainForm.cs:1077</c>: the <c>Startup</c> hook, once the main window is up.</summary>
		public void RunStartup() => Invoke(PluginEngine.ScriptTypeStartup);

		/// <summary>
		/// The <c>Shutdown</c> hook as <c>Program.cs</c> runs it when the main window closes: each command gets "the user closed the window" and answers whether the program may
		/// close. Returns false when a script said no and the close was started by the user (<paramref name="userClosing"/>); the commands after it do not run.
		/// An exception of a script is ignored.
		/// </summary>
		public bool RunShutdown(bool userClosing)
		{
			foreach (Command command in GetCommands(PluginEngine.ScriptTypeShutdown))
			{
				try
				{
					if (!(bool)command.Invoke(new object[] { userClosing }) && userClosing)
					{
						return false;
					}
				}
				catch (Exception ex)
				{
					host.Log?.Error("The shutdown script " + command.Key + " failed.", ex);
				}
			}
			return true;
		}

		/// <summary>True if a <c>CreateBookList</c> command with this key is on (the keys of <c>ComicBookPluginMatcher.PluginKey</c>).</summary>
		public bool HasListCommand(string key)
		{
			return Enabled && !string.IsNullOrEmpty(key) && GetCommands(PluginEngine.ScriptTypeCreateBookList).Any(c => c.Key == key);
		}

		/// <summary>Where script output (<c>print</c>, errors) goes from now on; null for nowhere. Set it before the first script runs: an engine keeps the output it was made with.</summary>
		public void AttachConsole(TextWriter writer, bool log = false)
		{
			PythonCommand.Output = writer == null ? null : new TextWriterStream(writer);
			PythonCommand.EnableLog = writer != null && log;
		}

		/// <summary>
		/// Reads the commands of one script file (a .py with hook comments, or a .xml command list) without adding them to <see cref="Engine"/>: they are initialised for
		/// <see cref="Environment"/> and returned whether they are enabled or not. This is what <c>crcli script run</c> uses.
		/// </summary>
		public IReadOnlyList<Command> LoadCommands(string file)
		{
			string full = Path.GetFullPath(file);
			PluginInitializer[] initializers = { new XmlPluginInitializer(), new PythonPluginInitializer() };
			var commands = new List<Command>();
			foreach (Command command in initializers.SelectMany(i => i.GetCommands(full)).ToArray())
			{
				if (command.Initialize(Environment, Path.GetDirectoryName(full)))
				{
					commands.Add(command);
				}
			}
			return commands;
		}

		/// <summary>Writes the on/off switches of the commands into the preferences (the original does it when the main window closes).</summary>
		public void StoreCommandStates()
		{
			if (Enabled && started)
			{
				host.Settings.Preferences.PluginsStates = Engine.CommandStates;
			}
		}

		public void Dispose()
		{
			lock (gate)
			{
				if (disposed)
				{
					return;
				}
				disposed = true;
			}
			foreach ((Command _, EventHandler<ComicBook.ParseFilePathEventArgs> handler) in parseHooks)
			{
				ComicBook.ParseFilePath -= handler;
			}
			parseHooks.Clear();
			if (ReferenceEquals(ComicBookPluginMatcher.PluginEngine, Engine))
			{
				ComicBookPluginMatcher.PluginEngine = null;
			}
			if (activeServices != null && ReferenceEquals(ActiveServices.Resolver, activeServices))
			{
				ActiveServices.Resolver = null;
			}
			PythonCommand.Output = null;
			PythonCommand.EnableLog = false;
			fileSystemLink?.Dispose();
		}
	}

	/// <summary>One <c>NetSearch</c> script as a search provider (<c>ScriptUtility.ScriptSearch</c>): the script returns a dictionary of names and links.</summary>
	public sealed class ScriptSearch : CachedSearch
	{
		private readonly Command command;

		public ScriptSearch(Command command)
		{
			this.command = command ?? throw new ArgumentNullException(nameof(command));
		}

		public Command Command => command;

		public override string Name => command.GetLocalizedName();

		public override Image Image => command.CommandImage;

		protected override IEnumerable<SearchResult> OnSearch(string hint, string text, int limit)
		{
			Dictionary<string, string> source = command.Invoke(new object[] { hint, text, limit }) as Dictionary<string, string>;
			return source.Select(kvp => new SearchResult { Name = kvp.Key, Result = kvp.Value });
		}

		protected override string OnGenericSearchLink(string hint, string text)
		{
			Dictionary<string, string> source = command.Invoke(new object[] { hint, text, -1 }) as Dictionary<string, string>;
			return source.Select(kvp => kvp.Value).FirstOrDefault();
		}
	}
}
