using System;
using System.IO;
using System.Linq;
using ComicRack.Mac.Host;
using ComicRack.Mac.Host.Library;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Engine.Database;
using Xunit;
using Xunit.Abstractions;

namespace ComicRack.Characterization
{
	/// <summary>The library file (ComicDb.xml): load, save and the lists, on a synthetic library of 200 books (P0.7a).</summary>
	public class DatabaseGoldenTests
	{
		private const string SmallDb = "SmallDb.xml";
		private const string SmallLists = "SmallDb.lists.txt";

		/// <summary>The date of the last cache update of a list is "now" when the cache is stored.</summary>
		private static readonly string[] VolatileElements = { "NewBookCountDate" };

		private readonly ITestOutputHelper output;

		public DatabaseGoldenTests(ITestOutputHelper output)
		{
			this.output = output;
		}

		private static MacHost Open(TempFolder temp, string databaseFile)
		{
			CoreRaces.WarmLanguageNames();
			return MacHost.Open(new MacHostOptions { Paths = temp.Paths, DatabaseFile = databaseFile });
		}

		/// <summary>A host on a copy of the committed SmallDb.xml; the copy is what the test may save.</summary>
		private static MacHost OpenSmall(TempFolder temp, out string copy)
		{
			copy = temp.Combine("SmallDb.xml");
			File.Copy(Goldens.PathOf(SmallDb), copy);
			return Open(temp, copy);
		}

		[Fact]
		public void The_generator_reproduces_the_committed_SmallDb_file()
		{
			using (var temp = new TempFolder())
			{
				string file = temp.Combine("generated.xml");
				using (MacHost host = Open(temp, file))
				{
					SmallDbBuilder.Fill(host.DatabaseManager.Database);
					Assert.Equal(SmallDbBuilder.BookCount, host.DatabaseManager.Database.Books.Count);
					SaveVerification saved = host.SaveDatabase();
					Assert.True(saved.Ok, saved.ToString());
				}
				Goldens.Check(SmallDb, CanonicalXml.FromFile(file), output.WriteLine);
			}
		}

		[Fact]
		public void Loading_and_saving_the_library_gives_the_same_xml()
		{
			using (var temp = new TempFolder())
			{
				string before = CanonicalXml.FromFile(Goldens.PathOf(SmallDb));
				using (MacHost host = OpenSmall(temp, out string copy))
				{
					Assert.False(host.IsNewDatabase);
					Assert.Equal(SmallDbBuilder.BookCount, host.DatabaseManager.Database.Books.Count);
					SaveVerification saved = host.SaveDatabase();
					Assert.True(saved.Ok, saved.ToString());
					string after = CanonicalXml.FromFile(copy);
					if (before != after)
					{
						output.WriteLine(Goldens.Diff(before, after));
					}
					Assert.True(before == after, $"The saved XML differs from the loaded one (saved text: {Goldens.SaveActual("roundtrip-" + SmallDb, after)}).\n" + Goldens.Diff(before, after));
				}
			}
		}

		[Fact]
		public void Every_list_has_its_golden_count()
		{
			using (var temp = new TempFolder())
			using (MacHost host = OpenSmall(temp, out _))
			{
				var rows = ListCounts.Evaluate(host.DatabaseManager.Database);
				Assert.All(rows, r => Assert.Null(r.Error));
				Goldens.Check(SmallLists, ListCounts.Format(rows), output.WriteLine);
			}
		}

		[Fact]
		public void The_list_counts_do_not_change_after_a_save_and_a_load()
		{
			using (var temp = new TempFolder())
			{
				string first;
				string copy;
				using (MacHost host = OpenSmall(temp, out copy))
				{
					first = ListCounts.Format(ListCounts.Evaluate(host.DatabaseManager.Database));
					Assert.True(host.SaveDatabase().Ok);
				}
				using (MacHost host = Open(temp, copy))
				{
					Assert.Equal(first, ListCounts.Format(ListCounts.Evaluate(host.DatabaseManager.Database)));
				}
			}
		}

		[Fact]
		public void Stored_list_caches_round_trip_and_give_the_same_counts()
		{
			using (var temp = new TempFolder())
			{
				string evaluated;
				string firstXml;
				string copy;
				using (MacHost host = OpenSmall(temp, out copy))
				{
					// The evaluation fills the lists' caches; the save writes them into the file (CacheStorage, BookCount, ...).
					evaluated = ListCounts.Format(ListCounts.Evaluate(host.DatabaseManager.Database));
					Assert.True(host.SaveDatabase().Ok);
					firstXml = CanonicalXml.FromFile(copy, VolatileElements);
				}
				Assert.Contains("CacheStorage", firstXml);
				using (MacHost host = Open(temp, copy))
				{
					// The caches come back from the file and give the counts again without a new evaluation.
					var cached = new ListEvaluator(useStoredCaches: true);
					Assert.Equal(evaluated, ListCounts.Format(ListCounts.Evaluate(host.DatabaseManager.Database, cached)));
					Assert.True(host.SaveDatabase().Ok);
					string secondXml = CanonicalXml.FromFile(copy, VolatileElements);
					Assert.True(firstXml == secondXml, $"A second save changed the file (saved text: {Goldens.SaveActual("caches-" + SmallDb, secondXml)}).\n" + Goldens.Diff(firstXml, secondXml));
				}
			}
		}

		[Fact]
		public void The_counts_agree_with_a_plain_query_of_the_books()
		{
			using (var temp = new TempFolder())
			using (MacHost host = OpenSmall(temp, out _))
			{
				ComicDatabase db = host.DatabaseManager.Database;
				ComicBook[] all = db.Books.ToArray();
				var evaluator = new ListEvaluator();
				ComicListItem List(string name) => db.ComicLists.GetItems<ComicListItem>().Single(l => l.Name == name);
				int Count(string name) => evaluator.Evaluate(List(name)).Count;

				Assert.Equal(all.Length, Count("Library"));
				Assert.Equal(all.Count(b => b.Series.Contains("alpha", StringComparison.OrdinalIgnoreCase)), Count("Alpha series"));
				Assert.Equal(all.Count(b => b.Publisher == "Apex Comics"), Count("Apex publisher"));
				Assert.Equal(all.Count(b => b.Year >= 2005 && b.Year <= 2010), Count("Years 2005 to 2010"));
				Assert.Equal(all.Count(b => b.PageCount > 60), Count("Long books"));
				Assert.Equal(all.Count(b => b.BlackAndWhite == YesNo.Yes), Count("Black and white"));
				Assert.Equal(all.Count(b => b.Manga == MangaYesNo.YesAndRightToLeft), Count("Manga right to left"));
				Assert.Equal(all.Count(b => b.AddedTime.Date > new DateTime(2019, 7, 1)), Count("Added after July 2019"));
				Assert.Equal(all.Count(b => b.ReadPercentage >= 1 && b.ReadPercentage <= 94), Count("In progress"));
				Assert.Equal(all.Count(b => b.Genre.Contains("Horror") || b.Genre.Contains("Fantasy")), Count("Horror or fantasy"));
				Assert.Equal(all.Count(b => b.Writer.StartsWith("Ann ") || b.Writer.StartsWith("Bo ")), Count("Writers Ann or Bo"));
				Assert.Equal(all.Count(b => b.Series.StartsWith("Beta") || b.Series.EndsWith("Force")), Count("Group beta or force"));
				Assert.Equal(all.Count(b => b.Rating != 0), Count("Not rated"));
				Assert.Equal(all.Count(b => b.Series.Contains("Alpha") && b.Year > 2003), Count("Alpha on 2003 or later"));
				Assert.Equal(all.Count(b => b.Publisher == "Apex Comics" && !b.Series.Contains("Alpha")), Count("Apex without Alpha"));
				Assert.Equal(all.Count(b => b.Tags.Contains("favorite")), Count("Tag favorite"));
				Assert.Equal(all.Count(b => b.LanguageISO == "ja" || b.LanguageISO == "fr"), Count("Language ja or fr"));
				Assert.Equal(all.Count(b => b.SeriesComplete == YesNo.Yes), Count("Series complete"));
				Assert.Equal(all.Count(b => b.Series.Contains("Alpha") && b.Publisher == "Apex Comics"), Count("Alpha and Apex"));
				Assert.Equal(10, Count("First ten by series"));
				Assert.Equal(5, Count("Five random"));
				Assert.Equal(12, Count("Picks")); // the 13th id is not in the library
				Assert.Equal(16, Count("Only duplicates"));
			}
		}
	}
}
