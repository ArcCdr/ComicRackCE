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
			+ "  script expr \"<python expression>\" [--db x.xml [--remap \"from=to\"]] [--list \"name\"] [--show N]";

		public static int Run(string[] args, TextWriter output, TextWriter error)
		{
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
	}
}
