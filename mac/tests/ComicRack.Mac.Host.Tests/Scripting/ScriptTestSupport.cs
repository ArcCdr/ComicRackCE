using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ComicRack.Mac.Host.Scripting;
using cYo.Projects.ComicRack.Engine;

namespace ComicRack.Mac.Host.Tests.Scripting
{
	/// <summary>Helpers of the scripting tests: the repository's bundled scripts, copies with the reference lines rewritten (as the S4 self test did), and a host with chosen script folders.</summary>
	internal static class ScriptTestSupport
	{
		public static string RepoRoot()
		{
			string dir = AppContext.BaseDirectory;
			while (dir != null && !File.Exists(Path.Combine(dir, "ComicRack", "Config", "Settings.cs")))
			{
				dir = Path.GetDirectoryName(dir);
			}
			if (dir == null)
			{
				throw new InvalidOperationException("The repository root was not found above " + AppContext.BaseDirectory);
			}
			return dir;
		}

		/// <summary>The scripts the program ships (<c>ComicRack/Output/Scripts</c>), read only.</summary>
		public static string BundledScripts => Path.Combine(RepoRoot(), "ComicRack", "Output", "Scripts");

		/// <summary>Stand-in script lists for the tests (<c>TestScripts</c> next to the test assembly).</summary>
		public static string TestScripts => Path.Combine(AppContext.BaseDirectory, "TestScripts");

		/// <summary>
		/// A copy of the bundled scripts in <paramref name="target"/> whose <c>clr.AddReference[ByPartialName]</c> lines for <c>System.Windows.Forms</c> and
		/// <c>System.Drawing</c> name the stand-in assemblies. The real names resolve only after P4.8 (assembly identity), where the unmodified files are accepted.
		/// </summary>
		public static string RewrittenCopyOfBundledScripts(string target)
		{
			Directory.CreateDirectory(target);
			foreach (string source in Directory.GetFiles(BundledScripts))
			{
				string file = Path.Combine(target, Path.GetFileName(source));
				if (source.EndsWith(".py", StringComparison.OrdinalIgnoreCase))
				{
					string text = File.ReadAllText(source);
					text = Regex.Replace(text, @"clr\.AddReference(ByPartialName)?\(\s*[""']System\.Windows\.Forms[""']\s*\)", "clr.AddReference(\"cYo.Compat.Forms\")");
					text = Regex.Replace(text, @"clr\.AddReference(ByPartialName)?\(\s*[""']System\.Drawing[""']\s*\)", "clr.AddReference(\"cYo.Compat.Drawing\")");
					File.WriteAllText(file, text);
				}
				else
				{
					File.Copy(source, file);
				}
			}
			return target;
		}

		/// <summary>A host on <paramref name="temp"/> that reads only the given script folders.</summary>
		public static MacHost Open(TempFolder temp, bool? scripting = null, string pluginStates = null, IScriptUi ui = null, params string[] scriptFolders)
		{
			var settings = new MacSettings();
			settings.Preferences.PluginsStates = pluginStates;
			return MacHost.Open(new MacHostOptions
			{
				Paths = temp.Paths,
				Settings = settings,
				Scripting = scripting,
				ScriptFolders = ScriptFolders.Only(scriptFolders),
				ScriptUi = ui,
				DatabaseBackgroundSaveSeconds = 0,
			});
		}

		public static ComicBook Book(string series, string number, int year = 2000, string file = null)
		{
			return new ComicBook { Series = series, Number = number, Year = year, FilePath = file ?? "/nowhere/" + series + " " + number + ".cbz" };
		}

		public static void Add(MacHost host, params ComicBook[] books)
		{
			foreach (ComicBook b in books)
			{
				host.DatabaseManager.Database.Books.Add(b);
			}
		}

		/// <summary>The Python scripts of <paramref name="scripts"/> in a new folder under <paramref name="temp"/>.</summary>
		public static string WriteScripts(TempFolder temp, string name, params (string file, string text)[] files)
		{
			string dir = Path.Combine(temp.Path, name);
			Directory.CreateDirectory(dir);
			foreach ((string file, string text) in files)
			{
				File.WriteAllText(Path.Combine(dir, file), text);
			}
			return dir;
		}
	}
}
