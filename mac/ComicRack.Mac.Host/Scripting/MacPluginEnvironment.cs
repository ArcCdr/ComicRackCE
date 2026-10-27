using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using cYo.Common.IO;
using cYo.Common.Localize;
using cYo.Common.Windows.Forms.Theme;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Engine.Database;
using cYo.Projects.ComicRack.Engine.Display;
using cYo.Projects.ComicRack.Plugins;
using cYo.Projects.ComicRack.Plugins.Automation;
using cYo.Projects.ComicRack.Plugins.Theme;

namespace ComicRack.Mac.Host.Scripting
{
	/// <summary>
	/// The object a script sees as <c>ComicRack</c> (<c>PluginEnvironment</c> of the original): <c>App</c>, <c>Browser</c>, <c>OpenBooks</c>, <c>ComicDisplay</c>,
	/// <c>MainWindow</c>, <c>Theme</c>, <c>LibraryPaths</c>, <c>CommandPath</c>, <c>Localize</c> and <c>ReadDatabaseBooks</c> keep their names and meaning. The
	/// window parts come from the current <see cref="IScriptUi"/>, so a window that appears after the scripts were loaded is seen by the next call.
	/// Every command gets its own clone (<c>Command.OnInitialize</c>), whose <see cref="CommandPath"/> is the folder of its script.
	/// </summary>
	public sealed class MacPluginEnvironment : IPluginEnvironment, IPluginConfig, ICloneable
	{
		private readonly Func<IScriptUi> ui;
		private readonly IPluginConfig config;
		private TRDictionary localization;

		public MacPluginEnvironment(Func<IScriptUi> ui, IApplication app, IBrowser browser, IOpenBooksManager openBooks, IPluginConfig config, IThemePlugin theme)
		{
			this.ui = ui ?? throw new ArgumentNullException(nameof(ui));
			App = app;
			Browser = browser;
			OpenBooks = openBooks;
			this.config = config ?? throw new ArgumentNullException(nameof(config));
			Theme = theme;
		}

		public IWin32Window MainWindow => ui().MainWindow;

		public IApplication App { get; }

		public IBrowser Browser { get; }

		public IOpenBooksManager OpenBooks { get; }

		public IComicDisplay ComicDisplay => ui().ComicDisplay;

		public string CommandPath { get; set; }

		public IThemePlugin Theme { get; set; }

		public IEnumerable<string> LibraryPaths => config.LibraryPaths;

		/// <summary><c>PluginEnvironment.ReadDatabaseBooks</c>: the books of another ComicRack database file.</summary>
		public IEnumerable<ComicBook> ReadDatabaseBooks(string file)
		{
			return ComicDatabase.LoadXml(file).Books;
		}

		/// <summary><c>PluginEnvironment.Localize</c>: the texts of a script come from its own language files, then <c>Script.&lt;key&gt;</c> of the program, then the key.</summary>
		public string Localize(string resourceKey, string nameKey, string text)
		{
			if (string.IsNullOrEmpty(resourceKey))
			{
				return text;
			}
			if (localization == null)
			{
				localization = new TRDictionary
				{
					ResourceFolder = new PackedLocalize(new VirtualFileFolder(CommandPath))
				};
			}
			TR tr = localization.Load(resourceKey);
			if (tr.IsEmpty)
			{
				tr = TR.Load("Script." + resourceKey);
				if (tr.IsEmpty)
				{
					tr = TR.Load(resourceKey);
				}
			}
			return tr[nameKey, text];
		}

		public object Clone()
		{
			return new MacPluginEnvironment(ui, App, Browser, OpenBooks, config, Theme);
		}
	}

	/// <summary>The folders scripts may import Python modules from: the "Scripting libraries" preference (<c>MainForm.LibraryPaths</c>).</summary>
	public sealed class MacPluginConfig : IPluginConfig
	{
		private readonly Func<string> libraries;

		public MacPluginConfig(Func<string> libraries)
		{
			this.libraries = libraries ?? throw new ArgumentNullException(nameof(libraries));
		}

		public IEnumerable<string> LibraryPaths
		{
			get
			{
				string text = libraries() ?? string.Empty;
				return text.Replace("\n", string.Empty).Replace("\r", string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries);
			}
		}
	}

	/// <summary>The theme a script sees: the default (light) theme, or the dark one (scripts then look for images named "Dark" + file name). The app switches it with its appearance.</summary>
	public sealed class MacThemePlugin : IThemePlugin
	{
		/// <summary>True while the app shows its dark appearance.</summary>
		public bool Dark { get; set; }

		public Themes CurrentTheme => Dark ? Themes.Dark : Themes.Default;

		public bool IsDarkModeEnabled => Dark;

		public ToolStripRenderer ToolStripRenderer => null;

		public UIComponent UIComponent => UIComponent.None;

		public void ApplyTheme(Control control)
		{
		}
	}
}
