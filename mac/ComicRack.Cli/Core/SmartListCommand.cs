using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using ComicRack.Cli.Fixtures;
using ComicRack.Mac.Host;
using ComicRack.Mac.Host.Library;

namespace ComicRack.Cli.Core
{
	/// <summary>
	/// `crcli smartlist &lt;ComicDb.xml&gt; (--list "name" | --all) [--remap "from=to"] [--cached]` (M2.2c): the book count of the database's
	/// lists, through <see cref="LibraryLists.Evaluate"/>, which is also what the sidebar of the app calls. The database is opened read
	/// only. Output lines of the table start with `list`, fields are separated by tabs:
	/// `list&lt;TAB&gt;name&lt;TAB&gt;kind&lt;TAB&gt;count&lt;TAB&gt;note` (the name is indented two spaces per level of nesting).
	/// </summary>
	internal static class SmartListCommand
	{
		public static int Run(string[] args, TextWriter output, TextWriter error)
		{
			var o = Options.Parse(args);
			string db = Path.GetFullPath(o.Positional(0, "smartlist <ComicDb.xml> (--list \"name\" | --all) [--remap \"from=to\"] [--cached]"));
			if (!File.Exists(db))
			{
				throw new UsageException("DB not found: " + db);
			}
			string name = o.Get("list");
			bool all = o.Flag("all");
			if (name == null && !all)
			{
				throw new UsageException("smartlist: give --list \"<name>\" or --all.");
			}
			bool cached = o.Flag("cached");
			var evaluator = new ListEvaluator(useStoredCaches: cached);
			PathRemap remap = PathRemap.Parse(o.Get("remap"));
			using (CliSession session = CliSession.Create(o))
			using (MacHost host = MacHost.Open(new MacHostOptions { Paths = session.Paths, DatabaseFile = db, DatabaseReadOnly = true }))
			{
				var database = host.DatabaseManager.Database;
				remap?.Apply(database);
				var rows = new List<(ListNode node, int depth)>();
				foreach (ListNode root in LibraryLists.BuildTree(database))
				{
					Collect(root, 0, rows);
				}
				if (!all)
				{
					rows = rows.Where(r => string.Equals(r.node.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
					if (rows.Count == 0)
					{
						error.WriteLine("No list named \"" + name + "\". Lists: " + string.Join("; ", LibraryLists.BuildTree(database).SelectMany(r => r.Flatten()).Select(n => n.Name)));
						return 1;
					}
				}
				output.WriteLine("smartlist: " + db + " (" + database.Books.Count + " books" + (cached ? ", query caches as stored in the file" : "") + ")");
				foreach ((ListNode node, int depth) in rows)
				{
					var sw = Stopwatch.StartNew();
					ListResult r = evaluator.Evaluate(node.Item);
					string note = r.RequiresScripts ? "requires scripts" : r.Error != null ? "error: " + r.Error : string.Empty;
					output.WriteLine("list\t" + new string(' ', depth * 2) + node.Name + "\t" + node.Kind + "\t" + r.Count + "\t" + note + (sw.ElapsedMilliseconds >= 1000 ? " (" + sw.ElapsedMilliseconds + " ms)" : ""));
				}
			}
			return 0;
		}

		private static void Collect(ListNode node, int depth, List<(ListNode, int)> rows)
		{
			rows.Add((node, depth));
			foreach (ListNode child in node.Children)
			{
				Collect(child, depth + 1, rows);
			}
		}
	}
}
