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
			if (!string.IsNullOrWhiteSpace(dataDir))
			{
				options.DataDirectory = Path.GetFullPath(dataDir);
			}
			else if (options.SmokeTest)
			{
				options.DataDirectory = Path.Combine(Path.GetTempPath(), "comicrack-mac-smoke-" + Guid.NewGuid().ToString("N"));
				options.DataDirectoryIsTemporary = true;
			}
			return options;
		}
	}
}
