using System;
using System.IO;
using System.Linq;
using ComicRack.Mac.Host;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Engine.Database;
using Xunit;
using Xunit.Abstractions;

namespace ComicRack.Characterization
{
	/// <summary>A fact that needs <c>mac/fixtures/ComicDb.xml</c> (the 14,706 book library of the MVP); reported as skipped when the file is missing.</summary>
	public sealed class FixtureDbFactAttribute : FactAttribute
	{
		public FixtureDbFactAttribute()
		{
			if (!File.Exists(FixtureDbGoldenTests.FixtureFile))
			{
				Skip = "mac/fixtures/ComicDb.xml is missing (generate or copy the fixtures, see mac/MVP-QUICKSTART.md).";
			}
		}
	}

	/// <summary>The counts of every list of the fixture library, as in the sidebar of M2.2c (P0.7a).</summary>
	public class FixtureDbGoldenTests
	{
		private const string Golden = "FixtureDb.lists.txt";

		public static string FixtureFile => Path.Combine(Goldens.RepoRoot, "mac", "fixtures", "ComicDb.xml");

		private readonly ITestOutputHelper output;

		public FixtureDbGoldenTests(ITestOutputHelper output)
		{
			this.output = output;
		}

		/// <summary>True for a smart list whose result depends on the date of the run ("Recently Added" and friends).</summary>
		private static bool TimeDependent(ComicListItem item)
		{
			return item is ComicSmartListItem smart && smart.Matchers.Any(m => m.TimeDependant);
		}

		[FixtureDbFact]
		public void Every_list_of_the_fixture_library_has_its_golden_count()
		{
			CoreRaces.WarmLanguageNames();
			using (var temp = new TempFolder())
			{
				// A copy, opened read only: the fixture itself is never touched.
				string copy = temp.Combine("ComicDb.xml");
				File.Copy(FixtureFile, copy);
				using (MacHost host = MacHost.Open(new MacHostOptions { Paths = temp.Paths, DatabaseFile = copy, DatabaseReadOnly = true }))
				{
					ComicDatabase db = host.DatabaseManager.Database;
					Assert.True(db.Books.Count > 10000, "books: " + db.Books.Count);
					var rows = ListCounts.Evaluate(db, TimeDependent);
					Assert.All(rows, r => Assert.Null(r.Error));
					Assert.Contains(rows, r => r.Name == "Library" && r.Count == db.Books.Count);
					Goldens.Check(Golden, "# books: " + db.Books.Count + "\n" + ListCounts.Format(rows), output.WriteLine);
				}
			}
		}
	}
}
