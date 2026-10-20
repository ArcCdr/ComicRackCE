using System;
using System.IO;
using ComicRack.Mac.Host;
using cYo.Projects.ComicRack.Engine;

namespace ComicRack.Characterization
{
	/// <summary>
	/// Entry point used only by <see cref="LanguageRaceTests"/>: <c>dotnet ComicRack.Characterization.dll --list-count &lt;SmallDb.xml&gt; &lt;list name&gt;</c>
	/// opens a copy of the database in a fresh process, evaluates that one list first and prints its count. The test runner never calls Main.
	/// </summary>
	internal static class Program
	{
		public static int Main(string[] args)
		{
			if (args.Length != 3 || args[0] != "--list-count")
			{
				Console.Error.WriteLine("Usage: --list-count <database.xml> <list name>");
				return 2;
			}
			using (var temp = new TempFolder())
			{
				string copy = temp.Combine("ComicDb.xml");
				File.Copy(args[1], copy);
				using (MacHost host = MacHost.Open(new MacHostOptions { Paths = temp.Paths, DatabaseFile = copy }))
				{
					var rows = ListCounts.Evaluate(host.DatabaseManager.Database, item => item.Name != args[2]);
					if (rows.Count != 1 || rows[0].Error != null)
					{
						Console.Error.WriteLine("list not evaluated: " + args[2]);
						return 3;
					}
					Console.WriteLine("COUNT " + rows[0].Count);
					// ComicBook.languages (private, filled by GetIsoCulture) must hold every code the host warms before the first list runs.
					var languages = (System.Collections.IDictionary)typeof(ComicBook).GetField("languages", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)?.GetValue(null);
					Console.WriteLine("LANGUAGES " + (languages == null ? -1 : languages.Count));
					return 0;
				}
			}
		}
	}
}
