using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using cYo.Common.ComponentModel;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Engine.Database;

namespace ComicRack.Characterization
{
	/// <summary>
	/// Builds the synthetic library of <c>Golden/SmallDb.xml</c> from ComicBook objects: 200 books in 12 series (8 of them duplicates), and lists that use
	/// 10 or more matcher kinds, a base list, a negated matcher, a group, limits, a fixed list and folders. Everything is derived from the book index, so the
	/// result is the same on every run: ids are computed, dates are fixed and no list depends on today's date.
	/// </summary>
	internal static class SmallDbBuilder
	{
		private sealed record SeriesSpec(string Name, string Publisher, string Writer, string Genre, string Language, string Format, int StartYear, int Books, MangaYesNo Manga, YesNo BlackAndWhite, YesNo Complete);

		private static readonly SeriesSpec[] Series =
		{
			new("Alpha Force", "Apex Comics", "Ann Lee", "Superhero", "en", "Series", 2001, 26, MangaYesNo.No, YesNo.No, YesNo.Yes),
			new("Beta Blades", "Apex Comics", "Bo Chen", "Fantasy", "en", "Series", 1998, 22, MangaYesNo.No, YesNo.Yes, YesNo.No),
			new("Gamma Ray", "Northern Press", "Ann Lee", "Science Fiction", "en", "Annual", 2005, 20, MangaYesNo.No, YesNo.No, YesNo.Unknown),
			new("Delta Dawn", "Northern Press", "Cy Dunn", "Horror", "en", "Series", 2010, 18, MangaYesNo.No, YesNo.No, YesNo.Yes),
			new("Epsilon Eight", "Orbit Books", "Bo Chen", "Fantasy, Adventure", "fr", "Series", 1990, 18, MangaYesNo.No, YesNo.Yes, YesNo.No),
			new("Zeta Zero", "Orbit Books", "Di Evans", "Crime", "fr", "Series", 2015, 16, MangaYesNo.No, YesNo.No, YesNo.Unknown),
			new("Sakura Road", "Rising Sun", "Eri Sato", "Romance", "ja", "Series", 2003, 16, MangaYesNo.YesAndRightToLeft, YesNo.Yes, YesNo.Yes),
			new("Ronin Tales", "Rising Sun", "Eri Sato", "Adventure", "ja", "Series", 2008, 14, MangaYesNo.Yes, YesNo.Yes, YesNo.No),
			new("The Long Night", "Apex Comics", "Ann Lee", "Horror", "en", "Graphic Novel", 2012, 12, MangaYesNo.No, YesNo.No, YesNo.Yes),
			new("Quiet Harbor", "Northern Press", "Fay Gray", "Drama", "en", "Series", 1985, 12, MangaYesNo.Unknown, YesNo.Unknown, YesNo.Unknown),
			new("Iron Orchard", "Orbit Books", "Cy Dunn", "Western", "en", "Series", 1975, 10, MangaYesNo.No, YesNo.Yes, YesNo.No),
			new("Misc One-Shots", "Indie Hub", "Gus Hall", "Humor", "en", "One-Shot", 2019, 8, MangaYesNo.No, YesNo.No, YesNo.Unknown),
		};

		/// <summary>Books that are copies (same metadata, other file) of earlier ones: indexes in <see cref="Series"/> order.</summary>
		private static readonly int[] DuplicateOf = { 0, 1, 2, 3, 40, 41, 42, 43 };

		public const int BookCount = 200;

		public static Guid Id(int ns, int n) => new Guid(0x0C0A0000 + ns, 0, 0, new byte[] { 0, 0, 0, 0, 0, 0, (byte)(n >> 8), (byte)n });

		public static IReadOnlyList<ComicBook> CreateBooks()
		{
			var books = new List<ComicBook>();
			foreach (SeriesSpec s in Series)
			{
				for (int j = 0; j < s.Books; j++)
				{
					books.Add(NewBook(books.Count, Array.IndexOf(Series, s), s, j));
				}
			}
			foreach (int source in DuplicateOf)
			{
				ComicBook copy = new ComicBook(books[source]);
				copy.Id = Id(1, books.Count);
				copy.FilePath = "/library/Duplicates/" + System.IO.Path.GetFileName(books[source].FilePath);
				books.Add(copy);
			}
			if (books.Count != BookCount)
			{
				throw new InvalidOperationException($"The series table gives {books.Count} books, expected {BookCount}.");
			}
			return books;
		}

		private static ComicBook NewBook(int index, int seriesIndex, SeriesSpec s, int j)
		{
			int year = s.StartYear + j / 6;
			int pages = 20 + (seriesIndex * 7 + j * 5) % 60;
			int lastPage = ((seriesIndex + j) % 7) switch
			{
				2 => pages / 3,
				3 => pages / 2,
				4 => pages - 1,
				6 => pages - 2,
				_ => 0,
			};
			var book = new ComicBook
			{
				Id = Id(1, index),
				Series = s.Name,
				Number = (j + 1).ToString(CultureInfo.InvariantCulture),
				Count = s.Books,
				Volume = seriesIndex == 3 ? 2 : 1,
				Year = year,
				Month = 1 + j % 12,
				Day = 1 + j * 3 % 28,
				Title = $"{s.Name} chapter {j + 1}",
				Publisher = s.Publisher,
				Writer = s.Writer,
				Genre = s.Genre,
				LanguageISO = s.Language,
				Format = s.Format,
				Manga = s.Manga,
				BlackAndWhite = s.BlackAndWhite,
				SeriesComplete = s.Complete,
				Tags = new[] { "red", "green", "blue" }[index % 3] + (index % 10 == 0 ? ", favorite" : string.Empty),
				PageCount = pages,
				LastPageRead = lastPage,
				Rating = index % 11 / 2f,
				CommunityRating = index % 9 / 2f,
				FileSize = 10_000_000L + index * 12345L,
				AddedTime = new DateTime(2019, 1, 1).AddDays(index),
				// A book without file times gets FILETIME zero (1601-01-01) on its first load and save, so the file would not round trip.
				FileCreationTime = new DateTime(2018, 12, 1, 8, 0, 0, DateTimeKind.Utc).AddDays(index),
				FileModifiedTime = new DateTime(2018, 12, 2, 9, 30, 0, DateTimeKind.Utc).AddDays(index),
				FilePath = $"/library/{s.Name}/{s.Name} #{j + 1:00} ({year}).cbz",
			};
			if (lastPage > 0)
			{
				book.OpenedTime = new DateTime(2020, 1, 1).AddDays(index);
			}
			return book;
		}

		public static IEnumerable<ComicBook> BooksWithoutDuplicates(IEnumerable<ComicBook> books) => books.Where(b => !b.FilePath.Contains("/Duplicates/"));

		private static ComicSmartListItem Smart(int n, string name, params (Type type, int op, string v1, string v2, bool not)[] matchers)
		{
			var list = new ComicSmartListItem(name) { Id = Id(2, n) };
			list.Matchers.Clear();
			foreach (var m in matchers)
			{
				ComicBookValueMatcher matcher = ComicBookValueMatcher.Create(m.type, m.op, m.v1, m.v2);
				matcher.Not = m.not;
				list.Matchers.Add(matcher);
			}
			return list;
		}

		private static (Type, int, string, string, bool) M(Type type, int op, string v1 = "", string v2 = "", bool not = false) => (type, op, v1, v2, not);

		/// <summary>Adds the books and the lists to an empty database. A new database starts with the default lists (their matchers look at today's date); they are removed.</summary>
		public static void Fill(ComicDatabase db)
		{
			db.ComicLists.Clear();
			db.Id = Id(0, 1);
			foreach (ComicBook book in CreateBooks())
			{
				db.Books.Add(book);
			}
			db.ComicLists.Add(new ComicLibraryListItem("Library") { Id = Id(2, 0) });

			var smart = new ComicListItemFolder("Smart Lists") { Id = Id(3, 1) };
			db.ComicLists.Add(smart);
			const int Contains = ComicBookStringMatcher.OperatorContains;
			smart.Items.Add(Smart(1, "Alpha series", M(typeof(ComicBookSeriesMatcher), Contains, "alpha")));
			smart.Items.Add(Smart(2, "Apex publisher", M(typeof(ComicBookPublisherMatcher), ComicBookStringMatcher.OperatorEquals, "Apex Comics")));
			smart.Items.Add(Smart(3, "Years 2005 to 2010", M(typeof(ComicBookYearMatcher), ComicBookNumericMatcher.InRange, "2005", "2010")));
			smart.Items.Add(Smart(4, "Long books", M(typeof(ComicBookPageCountMatcher), ComicBookNumericMatcher.Greater, "60")));
			smart.Items.Add(Smart(5, "Black and white", M(typeof(ComicBookBlackAndWhiteMatcher), 0)));
			smart.Items.Add(Smart(6, "Manga right to left", M(typeof(ComicBookMangaMatcher), 1)));
			smart.Items.Add(Smart(7, "Added after July 2019", M(typeof(ComicBookAddedMatcher), ComicBookDateMatcher.OperatorIsAfter, "2019-07-01")));
			smart.Items.Add(Smart(8, "In progress", M(typeof(ComicBookReadPercentageMatcher), ComicBookNumericMatcher.InRange, "1", "94")));
			smart.Items.Add(Smart(9, "Horror or fantasy", M(typeof(ComicBookGenreMatcher), ComicBookStringMatcher.OperatorContainsAny, "Horror;Fantasy")));
			smart.Items.Add(Smart(10, "Writers Ann or Bo", M(typeof(ComicBookWriterMatcher), ComicBookStringMatcher.OperatorRegex, "^(Ann|Bo) ")));

			var more = new ComicListItemFolder("More Lists") { Id = Id(3, 2) };
			db.ComicLists.Add(more);
			more.Items.Add(Smart(11, "Only duplicates", M(typeof(ComicBookDuplicateMatcher), 0)));
			var group = new ComicBookGroupMatcher { MatcherMode = MatcherMode.Or };
			group.Matchers.Add(typeof(ComicBookSeriesMatcher), ComicBookStringMatcher.OperatorStartsWith, "Beta", string.Empty);
			group.Matchers.Add(typeof(ComicBookSeriesMatcher), ComicBookStringMatcher.OperatorEndsWith, "Force", string.Empty);
			ComicSmartListItem grouped = Smart(12, "Group beta or force");
			grouped.Matchers.Add(group);
			more.Items.Add(grouped);
			more.Items.Add(Smart(13, "Not rated", M(typeof(ComicBookRatingMatcher), ComicBookNumericMatcher.Equal, "0", "", not: true)));
			ComicSmartListItem baseList = (ComicSmartListItem)smart.Items.First(i => i.Id == Id(2, 1));
			ComicSmartListItem onBase = Smart(14, "Alpha on 2003 or later", M(typeof(ComicBookYearMatcher), ComicBookNumericMatcher.Greater, "2003"));
			onBase.BaseListId = baseList.Id;
			more.Items.Add(onBase);
			ComicSmartListItem notInBase = Smart(15, "Apex without Alpha", M(typeof(ComicBookPublisherMatcher), ComicBookStringMatcher.OperatorEquals, "Apex Comics"));
			notInBase.BaseListId = baseList.Id;
			notInBase.NotInBaseList = true;
			more.Items.Add(notInBase);
			more.Items.Add(Smart(16, "Tag favorite", M(typeof(ComicBookTagsMatcher), ComicBookStringMatcher.OperatorListContains, "favorite")));
			more.Items.Add(Smart(17, "Language ja or fr", M(typeof(ComicBookLanguageMatcher), ComicBookStringMatcher.OperatorContainsAny, "ja,fr")));
			more.Items.Add(Smart(18, "Series complete", M(typeof(ComicBookSeriesCompleteMatcher), 0)));

			var nested = new ComicListItemFolder("Nested") { Id = Id(3, 3) };
			more.Items.Add(nested);
			nested.Items.Add(Smart(19, "Series with 20 or more books", M(typeof(SmartListSeriesCountMatcher), ComicBookNumericMatcher.Greater, "19")));
			ComicSmartListItem either = Smart(20, "Rated 4 or more, or manga");
			either.MatcherMode = MatcherMode.Or;
			either.Matchers.Add(typeof(ComicBookRatingMatcher), ComicBookNumericMatcher.Greater, "3.5", string.Empty);
			either.Matchers.Add(typeof(ComicBookMangaMatcher), 0, string.Empty, string.Empty);
			nested.Items.Add(either);
			ComicSmartListItem firstTen = Smart(21, "First ten by series", M(typeof(ComicBookPublisherMatcher), ComicBookStringMatcher.OperatorContains, ""));
			firstTen.Limit = true;
			firstTen.LimitType = ComicSmartListLimitType.Count;
			firstTen.LimitValue = 10;
			firstTen.LimitSelectionType = ComicSmartListLimitSelectionType.SortedBySeries;
			nested.Items.Add(firstTen);
			ComicSmartListItem randomFive = Smart(22, "Five random", M(typeof(ComicBookPublisherMatcher), ComicBookStringMatcher.OperatorContains, "Orbit"));
			randomFive.Limit = true;
			randomFive.LimitValue = 5;
			randomFive.LimitSelectionType = ComicSmartListLimitSelectionType.Random;
			randomFive.LimitRandomSeed = 4711;
			nested.Items.Add(randomFive);

			var both = new ComicListItemFolder("Alpha and Apex") { Id = Id(3, 4), CombineMode = ComicFolderCombineMode.And };
			db.ComicLists.Add(both);
			both.Items.Add(Smart(23, "Alpha (in intersection)", M(typeof(ComicBookSeriesMatcher), Contains, "alpha")));
			both.Items.Add(Smart(24, "Apex (in intersection)", M(typeof(ComicBookPublisherMatcher), ComicBookStringMatcher.OperatorEquals, "Apex Comics")));

			var picks = new ComicIdListItem("Picks") { Id = Id(2, 25) };
			for (int i = 0; i < 12; i++)
			{
				picks.BookIds.Add(Id(1, i * 16));
			}
			picks.BookIds.Add(Id(9, 999)); // a book that is not in the library
			db.ComicLists.Add(picks);

			// The library adds this folder with a new random id when it is missing; a file written by the apps always has it.
			db.ComicLists.Add(new ComicListItemFolder("Temporary Lists") { Id = Id(3, 5), Temporary = true });
		}
	}
}
