using System;
using System.IO;
using System.Linq;
using ComicRack.Cli.Fixtures;
using ComicRack.Mac.Host;
using ComicRack.Mac.Host.Library;
using ComicRack.Mac.Host.Scripting;
using cYo.Projects.ComicRack.Engine;

namespace ComicRack.Cli.Core
{
	/// <summary>
	/// `crcli script run` and `crcli script expr` (P4.3): the scripting engine of the host without a window. `run` calls the commands of one script file
	/// for a hook with the arguments the WinForms app passes (<see cref="ScriptRunner"/>); `expr` evaluates a Python smart list expression over the books of a database.
	/// A database that is named is opened read only (a script may change books in memory, nothing is saved); without `--db` the library is empty.
	/// </summary>
	internal static class ScriptCommands
	{
		private const string Usage = "script run <file.py> --hook <Hook> [--method M] [--db x.xml [--remap \"from=to\"]] [--list \"name\"] [--path P] [--hint H --text T --limit N] [--a A --b B] [--user-closing] [--width W --height H] [--scripts-dir D]\n"
			+ "  script expr \"<python expression>\" [--db x.xml [--remap \"from=to\"]] [--list \"name\"] [--show N]\n"
			+ "  script install <file.crplugin> [--stage] | script list | script remove <name> [--stage] [--data-dir D]";

		public static int Run(string[] args, TextWriter output, TextWriter error)
		{
			if (args.Length > 0 && (args[0] == "install" || args[0] == "list" || args[0] == "remove"))
			{
				return Packages(args[0], Options.Parse(args.Skip(1).ToArray()), output, error);
			}
			if (args.Length == 0 || (args[0] != "run" && args[0] != "expr"))
			{
				throw new UsageException("script: expected `" + Usage + "`.");
			}
			bool run = args[0] == "run";
			var o = Options.Parse(args.Skip(1).ToArray());
			string subject = o.Positional(0, Usage);
			string file = null;
			if (run)
			{
				file = Path.GetFullPath(subject);
				if (!File.Exists(file))
				{
					throw new UsageException("Script not found: " + file);
				}
				if (o.Get("hook") == null)
				{
					throw new UsageException("script run: --hook <Hook> is required. Hooks: " + string.Join(", ", ScriptRunner.SupportedHooks));
				}
			}
			string db = o.Get("db");
			if (db != null)
			{
				db = Path.GetFullPath(db);
				if (!File.Exists(db))
				{
					throw new UsageException("DB not found: " + db);
				}
			}
			PathRemap remap = PathRemap.Parse(o.Get("remap"));
			string[] extra = (o.Get("scripts-dir") ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries);
			using (CliSession session = CliSession.Create(o))
			using (MacHost host = MacHost.Open(new MacHostOptions
			{
				Paths = session.Paths,
				DatabaseFile = db,
				DatabaseReadOnly = db != null,
				Scripting = !o.Flag("no-scripts"),
				ScriptFolders = ScriptFolders.For(session.Paths, extra),
			}))
			{
				remap?.Apply(host.DatabaseManager.Database);
				if (run)
				{
					var arguments = new ScriptRunArguments
					{
						Method = o.Get("method"),
						List = o.Get("list"),
						Path = o.Get("path"),
						Hint = o.Get("hint") ?? string.Empty,
						Text = o.Get("text") ?? string.Empty,
						Limit = o.Int("limit", 10),
						A = o.Get("a") ?? string.Empty,
						B = o.Get("b") ?? string.Empty,
						UserClosing = o.Flag("user-closing"),
						Width = o.Int("width", 1024),
						Height = o.Int("height", 768),
					};
					return ScriptRunner.Run(host, file, o.Get("hook"), arguments, output, error);
				}
				string list = o.Get("list");
				ComicBook[] source = ScriptRunner.SelectBooks(host, list);
				if (source == null)
				{
					error.WriteLine("No list named \"" + list + "\".");
					return 1;
				}
				ComicBook[] matched = ScriptRunner.MatchExpression(host, subject, list);
				output.WriteLine("expr: " + subject + " (" + source.Length + " books" + (list == null ? string.Empty : " of the list " + list) + ")");
				output.WriteLine("matched\t" + matched.Length);
				foreach (ComicBook book in matched.Take(o.Int("show", 0)))
				{
					output.WriteLine("book\t" + book.Caption);
				}
			}
			return 0;
		}

		/// <summary>
		/// `script install|list|remove` (P4.4): plugin packages in the user script folder of the data folder (`--data-dir`, `CRCLI_DATA_DIR`). Without `--stage`, install and remove are committed at once;
		/// with it they stay pending (as in the WinForms program until the next start).
		/// </summary>
		private static int Packages(string verb, Options o, TextWriter output, TextWriter error)
		{
			using (CliSession session = CliSession.Create(o))
			{
				var folders = ScriptFolders.For(session.Paths);
				using (MacHost host = MacHost.Open(new MacHostOptions { Paths = session.Paths, DatabaseReadOnly = true, Scripting = true, ScriptFolders = folders }))
				{
					var packages = new PluginPackages(folders, host.Scripts);
					bool now = !o.Flag("stage");
					switch (verb)
					{
					case "install":
					{
						string file = Path.GetFullPath(o.Positional(0, Usage));
						if (!File.Exists(file))
						{
							throw new UsageException("Package not found: " + file);
						}
						string name = PluginPackages.NameOf(file);
						if (name == null)
						{
							error.WriteLine("Not a plugin package: " + file);
							return 1;
						}
						bool replaces = packages.Exists(file);
						if (!(now ? packages.InstallNow(file) : packages.Install(file)))
						{
							error.WriteLine("Install failed: " + file);
							return 1;
						}
						output.WriteLine((now ? "installed" : "pending") + "\t" + name + (replaces ? "\treplaces an existing package" : string.Empty));
						return 0;
					}
					case "remove":
					{
						string name = o.Positional(0, Usage);
						if (!packages.Uninstall(name))
						{
							error.WriteLine("No package named \"" + name + "\".");
							return 1;
						}
						if (now)
						{
							packages.Commit();
						}
						output.WriteLine((now ? "removed" : "pending removal") + "\t" + name);
						return 0;
					}
					default:
						output.WriteLine("name\tversion\tstate\tcommands\tauthor\tdescription");
						foreach (PluginPackageInfo p in packages.List())
						{
							int commands = packages.CommandsOf(p.Name).Count;
							output.WriteLine(p.Name + "\t" + p.Version + "\t" + p.State + (p.Enabled == false ? " (disabled)" : string.Empty) + "\t" + commands + "\t" + p.Author + "\t" + p.Description);
						}
						return 0;
					}
				}
			}
		}
	}
}
