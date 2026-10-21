using System;
using System.Globalization;
using System.IO;

namespace ComicRack.Mac
{
	/// <summary>
	/// Command line of the app. <c>--smoke-test[=SECONDS]</c> opens the window, waits for the host to open, stays up for SECONDS
	/// (default 3) and exits with 0 (1 if the host did not open or an exception reached a handler). <c>--data-dir DIR</c> (or the
	/// environment variable COMICRACK_MAC_DATA_DIR) puts data, cache and logs below DIR instead of the real MVP folders; a smoke
	/// test without one uses a new temporary folder that is deleted at the end.
	/// </summary>
	public sealed class AppOptions
	{
		public const string DataDirVariable = "COMICRACK_MAC_DATA_DIR";
		public const string LanguageVariable = "COMICRACK_MAC_LANGUAGE";
		public const string ThemeVariable = "COMICRACK_MAC_THEME";

		public bool SmokeTest { get; private set; }

		/// <summary>
		/// <c>--headless</c>: Avalonia's headless platform (no screen, no native menu, no drawing). The window, the view models and
		/// the host still run. For smoke runs on a machine without a usable display (locked screen, CI).
		/// </summary>
		public bool Headless { get; private set; }

		public TimeSpan SmokeDuration { get; private set; } = TimeSpan.FromSeconds(3);

		/// <summary>
		/// <c>--read-book FILE</c>: opens FILE in a reader window as soon as the library engine is up (the book is not added to the
		/// library, so nothing is recorded). With <c>--smoke-test</c> the run exits with 1 if the first page could not be shown, so it
		/// is also a check of the page path (archive provider, pdfium, Skia decode) for a bundle on a machine without a display.
		/// </summary>
		public string ReadBook { get; private set; }

		/// <summary>
		/// Hidden <c>--script-selftest FILE</c> (spike S4): run the scripting checks of <see cref="ScriptSelfTest"/> with the Python
		/// probe script FILE in this process, print the result lines to the console and exit (0 when nothing failed). No window,
		/// no host, no data folder.
		/// </summary>
		public string ScriptSelfTest { get; private set; }

		/// <summary>
		/// <c>--language CULTURE</c> (or the environment variable COMICRACK_MAC_LANGUAGE): the language setting of <see cref="Localization"/>
		/// (a folder name of <c>Languages/</c>, <c>en</c> for English). Until the settings of P5.1 exist this is the only setting; without it the
		/// macOS preferred languages decide.
		/// </summary>
		public string Language { get; private set; }

		/// <summary>
		/// <c>--theme light|dark</c> (or the environment variable COMICRACK_MAC_THEME): shows the original's Default (light) or Dark theme
		/// whatever the macOS appearance is (decision C-APP-1; used by contact sheets and e2e cases, like <c>-dark</c> of the original).
		/// Null follows the macOS appearance.
		/// </summary>
		public string Theme { get; private set; }

		/// <summary>
		/// <c>--chrome SPEC</c> (or the environment variable COMICRACK_MAC_CHROME): the look of single chrome elements, <c>Element=Native</c> or
		/// <c>Element=Original</c> separated by commas, or <c>native</c> for every element that has a Native variant (<see cref="Themes.ChromeVariants"/>).
		/// Null keeps the defaults of <c>Themes/ChromeVariants.cs</c>.
		/// </summary>
		public string Chrome { get; private set; }

		/// <summary>
		/// Hidden <c>--show NAME</c>: opens the named window, dialog or view after launch (<see cref="Views.ViewRegistry"/> lists the names;
		/// <c>MainWindow</c> and <c>ChromeGallery</c> exist, every later UI task registers its views). The contact sheets of
		/// <c>mac/tools/chrome-sheet.sh</c> use it with <c>--theme</c>. Null opens only the library window.
		/// </summary>
		public string Show { get; private set; }

		/// <summary>Hidden <c>--list-views</c>: prints the names that <c>--show</c> accepts, one per line, and exits (no window, no host).</summary>
		public bool ListViews { get; private set; }

		/// <summary>
		/// Hidden <c>--fixture-library DIR</c>: after the engine opens, scans DIR (a folder made by <c>crcli fixtures make</c>) into the library, as
		/// File > Open Folder does. It needs a private data folder (<c>--data-dir</c> or a smoke test), so a real library is never filled with fixtures.
		/// </summary>
		public string FixtureLibrary { get; private set; }

		/// <summary>
		/// Hidden <c>--browser-height N</c>: the height of the browser dock at the bottom of the main window for this run (the saved value is replaced, as a drag of
		/// the grip does). The e2e cases that click in the book grid use it to keep their click points; it needs a private data folder.
		/// </summary>
		public double? BrowserHeight { get; private set; }

		/// <summary>
		/// Hidden <c>--test-info-page</c>: adds a test page to the info panel (the original's pages come only from scripts, P4.11a), so Browse &gt; Info Panel can be tried and tested.
		/// It needs a private data folder.
		/// </summary>
		public bool TestInfoPage { get; private set; }

		/// <summary>Root of a private data folder, or null for the real folders (~/Library/.../ComicRack CE (MVP)).</summary>
		public string DataDirectory { get; private set; }

		/// <summary>True if <see cref="DataDirectory"/> is a temporary folder the app created and must delete.</summary>
		public bool DataDirectoryIsTemporary { get; private set; }

		/// <summary>Set if the arguments were not understood.</summary>
		public string Error { get; private set; }

		public static AppOptions Parse(string[] args)
		{
			var options = new AppOptions();
			string dataDir = Environment.GetEnvironmentVariable(DataDirVariable);
			options.Language = Environment.GetEnvironmentVariable(LanguageVariable);
			string theme = Environment.GetEnvironmentVariable(ThemeVariable);
			string chrome = Environment.GetEnvironmentVariable(Themes.ChromeVariants.Variable);
			for (int i = 0; i < args.Length; i++)
			{
				string arg = args[i];
				if (arg == "--smoke-test" || arg.StartsWith("--smoke-test=", StringComparison.Ordinal))
				{
					options.SmokeTest = true;
					int eq = arg.IndexOf('=');
					if (eq >= 0)
					{
						if (!double.TryParse(arg.Substring(eq + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds) || seconds < 0 || seconds > 3600)
						{
							options.Error = "--smoke-test expects a number of seconds from 0 to 3600.";
							return options;
						}
						options.SmokeDuration = TimeSpan.FromSeconds(seconds);
					}
				}
				else if (arg == "--headless")
				{
					options.Headless = true;
				}
				else if (arg == "--read-book" && i + 1 < args.Length)
				{
					options.ReadBook = Path.GetFullPath(args[++i]);
				}
				else if (arg.StartsWith("--read-book=", StringComparison.Ordinal))
				{
					options.ReadBook = Path.GetFullPath(arg.Substring("--read-book=".Length));
				}
				else if (arg == "--script-selftest" && i + 1 < args.Length)
				{
					options.ScriptSelfTest = Path.GetFullPath(args[++i]);
				}
				else if (arg.StartsWith("--script-selftest=", StringComparison.Ordinal))
				{
					options.ScriptSelfTest = Path.GetFullPath(arg.Substring("--script-selftest=".Length));
				}
				else if (arg == "--data-dir" && i + 1 < args.Length)
				{
					dataDir = args[++i];
				}
				else if (arg.StartsWith("--data-dir=", StringComparison.Ordinal))
				{
					dataDir = arg.Substring("--data-dir=".Length);
				}
				else if (arg == "--language" && i + 1 < args.Length)
				{
					options.Language = args[++i];
				}
				else if (arg.StartsWith("--language=", StringComparison.Ordinal))
				{
					options.Language = arg.Substring("--language=".Length);
				}
				else if (arg == "--theme" && i + 1 < args.Length)
				{
					theme = args[++i];
				}
				else if (arg.StartsWith("--theme=", StringComparison.Ordinal))
				{
					theme = arg.Substring("--theme=".Length);
				}
				else if (arg == "--chrome" && i + 1 < args.Length)
				{
					chrome = args[++i];
				}
				else if (arg.StartsWith("--chrome=", StringComparison.Ordinal))
				{
					chrome = arg.Substring("--chrome=".Length);
				}
				else if (arg == "--list-views")
				{
					options.ListViews = true;
				}
				else if (arg == "--show" && i + 1 < args.Length)
				{
					options.Show = args[++i];
				}
				else if (arg.StartsWith("--show=", StringComparison.Ordinal))
				{
					options.Show = arg.Substring("--show=".Length);
				}
				else if (arg == "--fixture-library" && i + 1 < args.Length)
				{
					options.FixtureLibrary = Path.GetFullPath(args[++i]);
				}
				else if (arg.StartsWith("--fixture-library=", StringComparison.Ordinal))
				{
					options.FixtureLibrary = Path.GetFullPath(arg.Substring("--fixture-library=".Length));
				}
				else if (arg == "--test-info-page")
				{
					options.TestInfoPage = true;
				}
				else if ((arg == "--browser-height" && i + 1 < args.Length) || arg.StartsWith("--browser-height=", StringComparison.Ordinal))
				{
					string value = arg == "--browser-height" ? args[++i] : arg.Substring("--browser-height=".Length);
					if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double height) || height < 26 || height > 5000)
					{
						options.Error = "--browser-height expects a height from 26 to 5000.";
						return options;
					}
					options.BrowserHeight = height;
				}
				else if (arg.StartsWith("-psn_", StringComparison.Ordinal))
				{
					// Launch Services passes a process serial number to apps it starts.
				}
				else
				{
					options.Error = "Unknown argument: " + arg;
					return options;
				}
			}
			if (!string.IsNullOrWhiteSpace(theme))
			{
				switch (theme.Trim().ToLowerInvariant())
				{
					case "light":
						options.Theme = "light";
						break;
					case "dark":
						options.Theme = "dark";
						break;
					default:
						options.Error = "--theme expects light or dark.";
						return options;
				}
			}
			if (!string.IsNullOrWhiteSpace(chrome))
			{
				string chromeError = Themes.ChromeVariants.TryParse(chrome, out _);
				if (chromeError != null)
				{
					options.Error = "--chrome: " + chromeError;
					return options;
				}
				options.Chrome = chrome.Trim();
			}
			if (!string.IsNullOrWhiteSpace(dataDir))
			{
				options.DataDirectory = Path.GetFullPath(dataDir);
			}
			else if (options.SmokeTest)
			{
				options.DataDirectory = Path.Combine(Path.GetTempPath(), "comicrack-mac-smoke-" + Guid.NewGuid().ToString("N"));
				options.DataDirectoryIsTemporary = true;
			}
			if (options.Show != null)
			{
				string canonical = Views.ViewRegistry.Canonical(options.Show.Trim());
				if (canonical == null)
				{
					options.Error = "--show expects one of: " + string.Join(", ", Views.ViewRegistry.Names) + ".";
					return options;
				}
				options.Show = canonical;
			}
			if (options.BrowserHeight != null && options.DataDirectory == null)
			{
				options.Error = "--browser-height needs a private data folder: add --data-dir DIR (or --smoke-test).";
				return options;
			}
			if (options.TestInfoPage && options.DataDirectory == null)
			{
				options.Error = "--test-info-page needs a private data folder: add --data-dir DIR (or --smoke-test).";
				return options;
			}
			if (options.FixtureLibrary != null)
			{
				if (!Directory.Exists(options.FixtureLibrary))
				{
					options.Error = "--fixture-library: no such folder: " + options.FixtureLibrary;
					return options;
				}
				if (options.DataDirectory == null)
				{
					options.Error = "--fixture-library needs a private data folder: add --data-dir DIR (or --smoke-test).";
					return options;
				}
			}
			return options;
		}
	}
}
