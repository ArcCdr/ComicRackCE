using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using ComicRack.Mac.Browser;
using Xunit;
using Xunit.Abstractions;

namespace ComicRack.Mac.Tests.Browser
{
	public class BookCatalogTests
	{
		private readonly ITestOutputHelper output;

		public BookCatalogTests(ITestOutputHelper output)
		{
			this.output = output;
		}

		private static BookItem Item(string title, string series, string number, int day = 1)
		{
			return new BookItem(null, title, series, number, new DateTime(2020, 1, 1).AddDays(day));
		}

		private static string[] Titles(BookItem[] items) => items.Select(i => i.Title).ToArray();

		[Fact]
		public void Series_sort_orders_by_series_then_number_then_title_and_puts_books_without_a_series_last()
		{
			var catalog = new BookCatalog(new[]
			{
				Item("loose", null, "1"),
				Item("b2", "Beta", "2"),
				Item("a10", "alpha", "10"),
				Item("a2", "Alpha", "2"),
				Item("b-x", "Beta", "x"),
				Item("b1", "Beta", "1"),
				Item("b-none", "Beta", null),
				Item("e", "Été", "1"),
				Item("f", "Fin", "1"),
			});
			string[] titles = Titles(catalog.Query(BookSort.Series, "", CancellationToken.None));
			// alpha (2 before 10: a number, not text), Beta (1, 2, then the ones without a number), Été sorts as "ete" between Beta and Fin, no series last.
			Assert.Equal(new[] { "a2", "a10", "b1", "b2", "b-none", "b-x", "e", "f", "loose" }, titles);
		}

		[Fact]
		public void Title_sort_ignores_case_and_accents_and_added_sort_is_newest_first()
		{
			var catalog = new BookCatalog(new[]
			{
				Item("Zèbre", "s", "1", day: 5),
				Item("zoo", "s", "2", day: 9),
				Item("Élan", "s", "3", day: 1),
				Item("eau", "s", "4", day: 7),
				Item("Fin", "s", "5", day: 3),
			});
			Assert.Equal(new[] { "eau", "Élan", "Fin", "Zèbre", "zoo" }, Titles(catalog.Query(BookSort.Title, "", CancellationToken.None)));
			Assert.Equal(new[] { "zoo", "eau", "Zèbre", "Fin", "Élan" }, Titles(catalog.Query(BookSort.Added, "", CancellationToken.None)));
		}

		[Fact]
		public void Ligatures_and_letters_that_do_not_decompose_sort_and_search_as_their_spelling()
		{
			var catalog = new BookCatalog(new[]
			{
				Item("Zoo", "Zorro", "1"),
				Item("Œil de Jade", "Œil de Jade", "1"),
				Item("Oasis", "Oasis", "1"),
				Item("Pirate", "Pirate", "1"),
				Item("L'œuf du monde", "Œuf", "1"),
				Item("Straße", "Æsir", "1"),
			});
			// "Œil" sorts as "oeil", between Oasis and Pirate, not after Z.
			Assert.Equal(new[] { "Straße", "Oasis", "Œil de Jade", "L'œuf du monde", "Pirate", "Zoo" }, Titles(catalog.Query(BookSort.Series, "", CancellationToken.None)));
			Assert.Equal(new[] { "Œil de Jade" }, Titles(catalog.Query(BookSort.Title, "oeil", CancellationToken.None)));
			Assert.Equal(new[] { "L'œuf du monde" }, Titles(catalog.Query(BookSort.Title, "OEUF", CancellationToken.None)));
			Assert.Equal(new[] { "Straße" }, Titles(catalog.Query(BookSort.Title, "strasse aesir", CancellationToken.None)));
		}

		[Fact]
		public void Search_matches_title_or_series_ignoring_case_and_accents_and_every_word_must_match()
		{
			var catalog = new BookCatalog(new[]
			{
				Item("Le Château des étoiles", "Légendes", "1"),
				Item("Retour", "Étoile Noire", "3"),
				Item("Retour au château", "Légendes", "2"),
				Item("Autre", "Autre série", "1"),
			});
			Assert.Equal(new[] { "Le Château des étoiles", "Retour" }, Titles(catalog.Query(BookSort.Title, "ETOILE", CancellationToken.None)));
			Assert.Equal(new[] { "Le Château des étoiles", "Retour au château" }, Titles(catalog.Query(BookSort.Title, "chateau legendes", CancellationToken.None)));
			Assert.Equal(new[] { "Le Château des étoiles" }, Titles(catalog.Query(BookSort.Title, "  château   étoiles ", CancellationToken.None)));
			Assert.Empty(catalog.Query(BookSort.Title, "zzz", CancellationToken.None));
			Assert.Equal(4, catalog.Query(BookSort.Title, "   ", CancellationToken.None).Length);
			// Series are searched too: "noire" is only in a series name.
			Assert.Equal(new[] { "Retour" }, Titles(catalog.Query(BookSort.Series, "noire", CancellationToken.None)));
		}

		[Fact]
		public void The_result_of_a_search_is_the_sorted_list_filtered_not_a_new_sort()
		{
			BookItem[] all = SyntheticItems.Make(5000);
			var catalog = new BookCatalog(all);
			foreach (BookSort sort in Enum.GetValues(typeof(BookSort)))
			{
				BookItem[] sorted = catalog.Query(sort, "", CancellationToken.None);
				Assert.Equal(all.Length, sorted.Length);
				Assert.Equal(all.Length, sorted.Distinct().Count());
				BookItem[] found = catalog.Query(sort, "lune 1", CancellationToken.None);
				string[] terms = BookCatalog.SplitTerms("lune 1");
				BookItem[] expected = sorted.Where(i => terms.All(t => i.TitleKey.Contains(t) || i.SeriesKey.Contains(t))).ToArray();
				Assert.NotEmpty(found);
				Assert.Equal(expected, found);
			}
		}

		[Fact]
		public void Sorting_agrees_with_an_independent_ordering()
		{
			BookItem[] all = SyntheticItems.Make(5000);
			var catalog = new BookCatalog(all);
			var index = all.Select((item, i) => (item, i)).ToArray();
			BookItem[] expectedSeries = index
				.OrderBy(x => x.item.SeriesKey.Length == 0)
				.ThenBy(x => x.item.SeriesKey, StringComparer.Ordinal)
				.ThenBy(x => float.IsNaN(x.item.NumberValue))
				.ThenBy(x => x.item.NumberValue)
				.ThenBy(x => x.item.Number, StringComparer.Ordinal)
				.ThenBy(x => x.item.TitleKey, StringComparer.Ordinal)
				.ThenBy(x => x.i)
				.Select(x => x.item).ToArray();
			Assert.Equal(expectedSeries, catalog.Query(BookSort.Series, "", CancellationToken.None));
			BookItem[] expectedAdded = index.OrderByDescending(x => x.item.Added).ThenBy(x => x.i).Select(x => x.item).ToArray();
			// Equal dates fall back to the series order, so compare the dates only.
			Assert.Equal(expectedAdded.Select(i => i.Added), catalog.Query(BookSort.Added, "", CancellationToken.None).Select(i => i.Added));
		}

		[Fact]
		public void A_cancelled_query_stops_and_leaves_no_half_sorted_order_behind()
		{
			var catalog = new BookCatalog(SyntheticItems.Make(60000));
			using (var cts = new CancellationTokenSource())
			{
				cts.Cancel();
				Assert.ThrowsAny<OperationCanceledException>(() => catalog.Query(BookSort.Title, "", cts.Token));
			}
			Assert.False(catalog.HasOrder(BookSort.Title));
			BookItem[] sorted = catalog.Query(BookSort.Title, "", CancellationToken.None);
			Assert.True(catalog.HasOrder(BookSort.Title));
			for (int i = 1; i < sorted.Length; i++)
			{
				Assert.True(string.CompareOrdinal(sorted[i - 1].TitleKey, sorted[i].TitleKey) <= 0);
			}
		}

		[Theory]
		[InlineData(14706)]
		[InlineData(100000)]
		public void Sort_and_search_stay_fast_for_a_library_sized_list(int count)
		{
			long heap0 = GC.GetTotalMemory(true);
			var make = Stopwatch.StartNew();
			BookItem[] items = SyntheticItems.Make(count);
			double makeMs = make.Elapsed.TotalMilliseconds;
			var catalog = new BookCatalog(items);
			long heapItems = GC.GetTotalMemory(true) - heap0;

			double Time(Action action)
			{
				var sw = Stopwatch.StartNew();
				action();
				return sw.Elapsed.TotalMilliseconds;
			}

			double[] first = new double[3];
			foreach (BookSort sort in Enum.GetValues(typeof(BookSort)))
			{
				first[(int)sort] = Time(() => catalog.Query(sort, "", CancellationToken.None));
			}
			double cached = Time(() => catalog.Query(BookSort.Series, "", CancellationToken.None));

			string[] queries = { "l", "lu", "lune", "lune dragon", "étoile 12", "zzzzzz" };
			var searchMs = new double[queries.Length];
			var hits = new int[queries.Length];
			for (int q = 0; q < queries.Length; q++)
			{
				Time(() => catalog.Query(BookSort.Series, queries[q], CancellationToken.None)); // warm
				var best = double.MaxValue;
				for (int run = 0; run < 5; run++)
				{
					best = Math.Min(best, Time(() => hits[q] = catalog.Query(BookSort.Series, queries[q], CancellationToken.None).Length));
				}
				searchMs[q] = best;
			}
			long heapOrders = GC.GetTotalMemory(true) - heap0 - heapItems;
			output.WriteLine($"{count:N0} items: managed memory {heapItems / 1048576.0:0.0} MB for the items ({heapItems / count} bytes each), {heapOrders / 1048576.0:0.0} MB for the three sort orders; create {makeMs:0} ms; first sort series {first[0]:0} ms, title {first[1]:0} ms, added {first[2]:0} ms; cached order (copy of references) {cached:0.0} ms");
			for (int q = 0; q < queries.Length; q++)
			{
				output.WriteLine($"  search \"{queries[q]}\": {searchMs[q]:0.0} ms (best of 5), {hits[q]:N0} hits");
			}
			GC.KeepAlive(catalog);
			Assert.True(first.Max() < 4000, "first sort " + first.Max() + " ms");
			Assert.True(cached < 100 * (count / 14706.0 + 1), "cached order " + cached + " ms");
			Assert.True(searchMs.Max() < 250, "a search took " + searchMs.Max() + " ms");
		}
	}
}
