using System;
using System.Collections.Generic;
using System.Linq;
using ComicRack.Mac.Editor;
using ComicRack.Mac.Host;
using ComicRack.Mac.Host.Library;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Engine.Database;
using Xunit;

namespace ComicRack.Mac.Tests.Editor
{
	/// <summary>
	/// P5.8: what the multi edit dialog shows and writes (<c>MultipleComicBooksDialog.cs</c> without a window; DL.02): the value the books have in common, the check boxes, the prompts,
	/// the list fields and what <c>Store</c> writes.
	/// </summary>
	public class MultiBookEditorModelTests
	{
		private static ComicBook Book(string series, int number, string file = "/nowhere/book.cbz")
		{
			return new ComicBook { Series = series, Number = number.ToString(), FilePath = file, PageCount = 10 };
		}

		private static ComicBook[] Books(int count = 3, bool linked = true) => Enumerable.Range(1, count).Select(i => Book("Series " + i, i, linked ? "/nowhere/" + i + ".cbz" : string.Empty)).ToArray();

		private static MultiBookEditorModel Model(IEnumerable<ComicBook> books, AppPreferences preferences = null, EditorValueLists lists = null)
		{
			return new MultiBookEditorModel(books, preferences, lists);
		}

		// ---- what the box shows

		[Fact]
		public void A_value_all_the_books_share_is_shown_and_checked_and_a_value_that_differs_is_empty_and_unchecked()
		{
			ComicBook[] books = Books();
			foreach (ComicBook book in books)
			{
				book.Publisher = "Marvel";
				book.Year = 1999;
			}
			books[0].Month = 3;
			books[1].Month = 3;
			MultiBookEditorModel model = Model(books);
			Assert.Equal("Marvel", model["cbPublisher"].Text);
			Assert.Equal(MultiCheck.Checked, model["cbPublisher"].Check);
			Assert.Equal("1999", model["txYear"].Text);
			Assert.Equal(MultiCheck.Checked, model["txYear"].Check);
			Assert.Equal(string.Empty, model["txSeries"].Text); // the series differ
			Assert.Equal(MultiCheck.Unchecked, model["txSeries"].Check);
			Assert.Equal(string.Empty, model["txMonth"].Text);
			Assert.Equal(MultiCheck.Unchecked, model["txMonth"].Check);
			Assert.Equal(string.Empty, model["txCount"].Text); // -1: unknown, the same in every book, shown as nothing but still checked
			Assert.Equal(MultiCheck.Checked, model["txCount"].Check);
		}

		[Fact]
		public void Yes_and_No_boxes_show_the_texts_of_the_Core_and_numbers_and_dates_are_converted()
		{
			ComicBook[] books = Books(2);
			foreach (ComicBook book in books)
			{
				book.BlackAndWhite = YesNo.Yes;
				book.Manga = MangaYesNo.YesAndRightToLeft;
				book.SeriesComplete = YesNo.No;
				book.Rating = 3.5f;
				book.AddedTime = new DateTime(2020, 5, 17);
			}
			MultiBookEditorModel model = Model(books);
			Assert.Equal(ComicInfo.YesText, model["cbBlackAndWhite"].Text);
			Assert.Equal(ComicInfo.YesRightToLeftText, model["cbManga"].Text);
			Assert.Equal(ComicInfo.NoText, model["cbSeriesComplete"].Text);
			Assert.Equal((3.5f).ToString(), model["txRating"].Text);
			Assert.Equal(new DateTime(2020, 5, 17), model["dtpAddedTime"].Date);
			Assert.Equal(MultiCheck.Checked, model["dtpAddedTime"].Check);
			books[1].AddedTime = new DateTime(2021, 1, 1);
			MultiBookEditorModel differing = Model(books);
			Assert.Equal(DateTime.MinValue, differing["dtpAddedTime"].Date);
			Assert.Equal(MultiCheck.Unchecked, differing["dtpAddedTime"].Check);
		}

		[Fact]
		public void The_title_counts_the_books_and_every_field_the_original_lists_is_a_book_property()
		{
			MultiBookEditorModel model = Model(Books(4));
			Assert.Equal(4, model.Count);
			Assert.Equal(56, model.Fields.Count);
			foreach (MultiField field in model.Fields)
			{
				Assert.NotNull(field.PropertyInfo);
				Assert.True(field.PropertyInfo.CanWrite, field.Name + " (" + field.Property + ") has no setter");
			}
			Assert.Equal(model.Fields.Count, model.Fields.Select(f => f.Name).Distinct().Count());
		}

		// ---- the check boxes

		[Fact]
		public void Typing_checks_the_box_a_click_toggles_it_and_an_empty_text_does_not_check()
		{
			ComicBook[] books = Books();
			books[0].AddedTime = new DateTime(2000, 5, 5); // the dates differ, so the date box starts unchecked and empty
			MultiBookEditorModel model = Model(books);
			MultiField series = model["txSeries"];
			series.Text = string.Empty;
			Assert.Equal(MultiCheck.Unchecked, series.Check);
			series.Text = "X";
			Assert.Equal(MultiCheck.Checked, series.Check);
			series.Toggle();
			Assert.Equal(MultiCheck.Unchecked, series.Check);
			Assert.Equal("X", series.Text); // unchecking leaves the text
			series.Toggle();
			Assert.Equal(MultiCheck.Checked, series.Check);
			MultiField date = model["dtpAddedTime"];
			Assert.Equal(MultiCheck.Unchecked, date.Check);
			date.Date = new DateTime(2000, 1, 1);
			Assert.Equal(MultiCheck.Checked, date.Check);
		}

		[Fact]
		public void The_rating_Proposed_Values_and_Series_complete_boxes_are_for_books_in_the_library_only()
		{
			MultiBookEditorModel loose = Model(Books());
			Assert.False(loose.AllInContainer);
			Assert.False(loose["txRating"].Enabled);
			Assert.False(loose["cbEnableProposed"].Enabled);
			Assert.False(loose["cbSeriesComplete"].Enabled);
			Assert.True(loose["txCommunityRating"].Enabled);
			Assert.True(loose["cbManga"].Enabled);
		}

		[Fact]
		public void Opened_and_Pages_are_for_books_without_a_file_and_Scan_Information_and_the_Catalog_depend_on_the_files()
		{
			MultiBookEditorModel withFiles = Model(Books(2, linked: true), new AppPreferences());
			Assert.False(withFiles["dtpOpenedTime"].Enabled);
			Assert.False(withFiles["dtpOpenedTime"].Visible);
			Assert.False(withFiles["txPagesAsTextSimple"].Visible);
			Assert.True(withFiles["txScanInformation"].Visible);
			Assert.True(withFiles.AnyLinked);
			Assert.False(withFiles.ShowCatalog); // CatalogOnlyForFileless is on by default and there is a file
			Assert.True(Model(Books(2, linked: true), new AppPreferences { CatalogOnlyForFileless = false }).ShowCatalog);
			MultiBookEditorModel fileless = Model(Books(2, linked: false));
			Assert.True(fileless["dtpOpenedTime"].Enabled);
			Assert.True(fileless["txPagesAsTextSimple"].Visible);
			Assert.False(fileless["txScanInformation"].Visible);
			Assert.True(fileless.ShowCatalog);
			// one book with a file is enough
			ComicBook[] mixed = Books(2, linked: false);
			mixed[1].FilePath = "/nowhere/x.cbz";
			Assert.False(Model(mixed)["txPagesAsTextSimple"].Visible);
		}

		// ---- prompts (SetGrayText)

		[Fact]
		public void The_prompt_is_the_value_all_the_books_propose_and_checking_a_box_fills_it_and_unchecking_empties_it_again()
		{
			ComicBook[] books = { Book("A", 1, "/nowhere/Batman 01.cbz"), Book("B", 2, "/nowhere/Batman 02.cbz") };
			MultiBookEditorModel model = Model(books);
			MultiField series = model["txSeries"], number = model["txNumber"];
			Assert.True(series.HasPrompt);
			Assert.Equal(books[0].ProposedSeries, books[1].ProposedSeries);
			Assert.False(string.IsNullOrEmpty(series.PromptText));
			Assert.Equal(books[0].ProposedSeries, series.PromptText);
			Assert.Equal(string.Empty, number.PromptText); // the proposed numbers differ
			Assert.Equal(MultiCheck.Unchecked, series.Check); // the series differ
			series.Check = MultiCheck.Checked;
			Assert.Equal(books[0].ProposedSeries, series.Text);
			series.Check = MultiCheck.Unchecked;
			Assert.Equal(string.Empty, series.Text);
			// a text the user typed stays when the box is unchecked
			series.Text = "Typed";
			series.Check = MultiCheck.Unchecked;
			Assert.Equal("Typed", series.Text);
			// the other boxes have prompts too, and the rest have none
			Assert.True(new[] { "txNumber", "txCount", "txYear", "txVolume", "cbFormat" }.All(n => model[n].HasPrompt));
			Assert.Equal(6, model.Fields.Count(f => f.HasPrompt));
		}

		[Fact]
		public void A_box_that_has_a_value_in_common_keeps_its_text_when_it_is_checked_again()
		{
			ComicBook[] books = Books(2);
			foreach (ComicBook b in books)
			{
				b.Series = "Same";
			}
			MultiBookEditorModel model = Model(books);
			MultiField series = model["txSeries"];
			Assert.Equal("Same", series.Text);
			series.Check = MultiCheck.Unchecked;
			Assert.Equal("Same", series.Text); // it is not the prompt, so it stays
			series.Check = MultiCheck.Checked;
			Assert.Equal("Same", series.Text);
		}

		// ---- list fields

		[Fact]
		public void A_list_field_shows_the_entries_every_book_has_and_is_indeterminate_when_the_books_differ()
		{
			ComicBook[] books = Books(3);
			books[0].Writer = "Stan Lee, Jack Kirby";
			books[1].Writer = "Stan Lee, Steve Ditko";
			books[2].Writer = "Stan Lee";
			MultiBookEditorModel model = Model(books);
			MultiField writer = model["txWriter"];
			Assert.Equal("Stan Lee", writer.Text);
			Assert.Equal(MultiCheck.Indeterminate, writer.Check);
			Assert.True(writer.IsChecked);
			// the books agree: the box is checked and shows the list
			books[1].Writer = "Stan Lee, Jack Kirby";
			books[2].Writer = "Stan Lee, Jack Kirby";
			MultiBookEditorModel same = Model(books);
			Assert.Equal("Stan Lee, Jack Kirby", same["txWriter"].Text);
			Assert.Equal(MultiCheck.Checked, same["txWriter"].Check);
			// no entries at all
			MultiBookEditorModel empty = Model(Books());
			Assert.Equal(string.Empty, empty["txGenre"].Text);
			Assert.Equal(MultiCheck.Checked, empty["txGenre"].Check);
		}

		[Fact]
		public void Unchecking_an_indeterminate_list_field_empties_its_box_and_a_click_cycles_to_unchecked()
		{
			ComicBook[] books = Books(2);
			books[0].Tags = "a, b";
			books[1].Tags = "a, c";
			MultiBookEditorModel model = Model(books);
			MultiField tags = model["txTags"];
			Assert.Equal(MultiCheck.Indeterminate, tags.Check);
			Assert.Equal("a", tags.Text);
			tags.Toggle(); // Indeterminate -> Unchecked
			Assert.Equal(MultiCheck.Unchecked, tags.Check);
			Assert.Equal(string.Empty, tags.Text);
			tags.Toggle();
			Assert.Equal(MultiCheck.Checked, tags.Check); // now a plain checked field
		}

		[Fact]
		public void OK_replaces_the_shared_entries_of_an_indeterminate_list_field_and_keeps_the_other_entries_of_each_book()
		{
			ComicBook[] books = Books(2);
			books[0].Writer = "Stan Lee, Jack Kirby";
			books[1].Writer = "Stan Lee, Steve Ditko";
			MultiBookEditorModel model = Model(books);
			model["txWriter"].Text = "Stan Lee, Roy Thomas";
			Assert.Equal(MultiCheck.Indeterminate, model["txWriter"].Check);
			model.Store();
			// the entries are a set: the order a book ends up with is the set's, as in the original
			Assert.Equal(new[] { "Jack Kirby", "Roy Thomas", "Stan Lee" }, Entries(books[0].Writer).OrderBy(e => e).ToArray());
			Assert.Equal(new[] { "Roy Thomas", "Stan Lee", "Steve Ditko" }, Entries(books[1].Writer).OrderBy(e => e).ToArray());
			// removing the shared entry takes it out of both
			books[0].Tags = "x, y";
			books[1].Tags = "x, z";
			MultiBookEditorModel second = Model(books);
			second["txTags"].Text = string.Empty;
			second.Store();
			Assert.Equal(new[] { "y" }, Entries(books[0].Tags));
			Assert.Equal(new[] { "z" }, Entries(books[1].Tags));
		}

		private static string[] Entries(string list) => list.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();

		// ---- Store

		[Fact]
		public void OK_writes_the_checked_boxes_only()
		{
			ComicBook[] books = Books(3);
			MultiBookEditorModel model = Model(books);
			model["cbPublisher"].Text = "DC";
			model["txSeries"].Text = "One Series";
			model["txSeries"].Check = MultiCheck.Unchecked; // typed, then unchecked: not written
			Assert.True(model.HasChanges);
			Assert.Equal(model.Fields.Count(f => f.IsChecked && f.Enabled), model.Store());
			Assert.All(books, b => Assert.Equal("DC", b.Publisher));
			Assert.Equal(new[] { "Series 1", "Series 2", "Series 3" }, books.Select(b => b.Series));
		}

		[Fact]
		public void Values_are_converted_as_the_property_wants_them()
		{
			ComicBook[] books = Books(2);
			MultiBookEditorModel model = Model(books, new AppPreferences());
			model["txVolume"].Text = "7";
			model["txCount"].Text = "x"; // not a number: the property's default
			model["txYear"].Text = "-4"; // negative: the default as well
			model["cbBlackAndWhite"].Text = ComicInfo.YesText;
			model["cbManga"].Text = ComicInfo.YesRightToLeftText;
			model["txCommunityRating"].Text = (4.5f).ToString();
			model["txPagesAsTextSimple"].Text = "12";
			model["dtpReleasedTime"].Date = new DateTime(2001, 2, 3);
			model.Store();
			Assert.All(books, b =>
			{
				Assert.Equal(7, b.Volume);
				Assert.Equal(-1, b.Count);
				Assert.Equal(-1, b.Year);
				Assert.Equal(YesNo.Yes, b.BlackAndWhite);
				Assert.Equal(MangaYesNo.YesAndRightToLeft, b.Manga);
				Assert.Equal(4.5f, b.CommunityRating);
				Assert.Equal(new DateTime(2001, 2, 3), b.ReleasedTime);
			});
		}

		[Fact]
		public void The_Proposed_Values_box_is_a_Yes_text_to_a_bool_and_anything_else_is_No()
		{
			ComicBook[] books = Books(2);
			foreach (ComicBook b in books)
			{
				b.EnableProposed = false;
			}
			var container = new ComicLibrary();
			foreach (ComicBook b in books)
			{
				container.Books.Add(b);
			}
			MultiBookEditorModel model = Model(books);
			Assert.True(model["cbEnableProposed"].Enabled);
			model["cbEnableProposed"].Text = "yes"; // compared without regard to case
			model.Store();
			Assert.All(books, b => Assert.True(b.EnableProposed));
			model["cbEnableProposed"].Text = ComicInfo.NoText;
			model.Store();
			Assert.All(books, b => Assert.False(b.EnableProposed));
		}

		[Fact]
		public void A_disabled_box_is_never_written_even_when_it_is_checked()
		{
			ComicBook[] books = Books(2);
			MultiBookEditorModel model = Model(books); // not in a library: the rating is disabled
			MultiField rating = model["txRating"];
			rating.Text = "4";
			Assert.True(rating.IsChecked);
			Assert.False(rating.Enabled);
			model.Store();
			Assert.All(books, b => Assert.Equal(0f, b.Rating));
		}

		[Fact]
		public void Store_changes_nothing_when_no_box_is_checked_and_reports_progress_for_every_book()
		{
			ComicBook[] books = Books(3);
			MultiBookEditorModel model = Model(books);
			var changes = new List<string>();
			foreach (ComicBook b in books)
			{
				b.BookChanged += (s, e) => changes.Add(e.PropertyName);
			}
			foreach (MultiField field in model.Fields)
			{
				field.Check = MultiCheck.Unchecked;
			}
			Assert.False(model.HasChanges);
			Assert.Equal(0, model.Store());
			Assert.Empty(changes);
			model["cbImprint"].Text = "Vertigo";
			var progress = new List<(int, int)>();
			Assert.Equal(1, model.Store((done, total) => progress.Add((done, total))));
			Assert.Equal(new[] { (1, 3), (2, 3), (3, 3) }, progress);
		}

		[Fact]
		public void A_checked_box_that_already_holds_the_common_value_writes_the_same_value_and_changes_nothing()
		{
			ComicBook[] books = Books(2);
			foreach (ComicBook b in books)
			{
				b.Publisher = "Marvel";
			}
			var changes = new List<string>();
			foreach (ComicBook b in books)
			{
				b.BookChanged += (s, e) => changes.Add(e.PropertyName);
			}
			MultiBookEditorModel model = Model(books);
			Assert.Equal(MultiCheck.Checked, model["cbPublisher"].Check);
			model.Store();
			Assert.DoesNotContain("Publisher", changes);
		}

		// ---- spin buttons and other helpers

		[Fact]
		public void The_number_boxes_step_as_the_constructor_of_the_original_says()
		{
			DateTime now = new DateTime(2024, 9, 17);
			Assert.Equal("2024", SpinRules.Step(string.Empty, string.Empty, MultiBookEditorModel.SpinFor("txYear", now), up: true));
			Assert.Equal("9", SpinRules.Step(string.Empty, string.Empty, MultiBookEditorModel.SpinFor("txMonth", now), up: true));
			Assert.Equal("17", SpinRules.Step(string.Empty, string.Empty, MultiBookEditorModel.SpinFor("txDay", now), up: true)); // the day starts at today's day of the month
			Assert.Equal("12", SpinRules.Step("12", string.Empty, MultiBookEditorModel.SpinFor("txMonth", now), up: true));
			Assert.Equal("1", SpinRules.Step("1", string.Empty, MultiBookEditorModel.SpinFor("txPagesAsTextSimple", now), up: false)); // Pages start at 1, at least 1
			Assert.Equal("0", SpinRules.Step("1", string.Empty, MultiBookEditorModel.SpinFor("txCount", now), up: false));
			Assert.Equal("1", SpinRules.Step(string.Empty, string.Empty, MultiBookEditorModel.SpinFor("txVolume", now), up: true));
			Assert.Equal("5", SpinRules.Step(string.Empty, "4", MultiBookEditorModel.SpinFor("txVolume", now), up: true)); // the prompt is the start
		}

		[Fact]
		public void A_rating_text_is_a_number_of_stars_between_0_and_5()
		{
			Assert.Equal(3.5, MultiBookEditorModel.RatingOf((3.5f).ToString()));
			Assert.Equal(0, MultiBookEditorModel.RatingOf(string.Empty));
			Assert.Equal(5, MultiBookEditorModel.RatingOf("9"));
			Assert.Equal((4f).ToString(), MultiBookEditorModel.RatingText(4));
		}

		[Fact]
		public void The_custom_fields_are_the_keys_of_the_library_without_a_point_and_are_written_as_custom_values()
		{
			ComicBook[] books = Books(2);
			books[0].SetCustomValue("Rank", "1");
			books[1].SetCustomValue("Rank", "2");
			books[0].SetCustomValue("Origin", "Paris");
			books[1].SetCustomValue("Origin", "Paris");
			books[0].SetCustomValue("script.hidden", "x");
			var lists = new EditorValueLists(() => books, Array.Empty<string>());
			// ShowCustomBookFields is off by default
			Assert.False(Model(books, new AppPreferences(), lists).ShowCustom);
			MultiBookEditorModel model = Model(books, new AppPreferences { ShowCustomBookFields = true }, lists);
			Assert.True(model.ShowCustom);
			Assert.Equal(new[] { "Origin", "Rank" }, model.CustomFields.Select(c => c.Key).ToArray());
			Assert.Equal("Origin:", model.CustomFields[0].Label);
			MultiField origin = model.CustomFields[0].Field, rank = model.CustomFields[1].Field;
			Assert.Equal("Paris", origin.Text);
			Assert.Equal(MultiCheck.Checked, origin.Check); // the same in both books
			Assert.Equal(string.Empty, rank.Text);
			Assert.Equal(MultiCheck.Unchecked, rank.Check);
			rank.Text = "9";
			origin.Text = "Berlin";
			model.Store();
			Assert.All(books, b => Assert.Equal("9", b.GetCustomValue("Rank")));
			Assert.All(books, b => Assert.Equal("Berlin", b.GetCustomValue("Origin")));
			Assert.Equal("x", books[0].GetCustomValue("script.hidden"));
		}

		[Fact]
		public void The_values_a_box_offers_come_from_the_library_and_the_title_list_is_the_raw_title()
		{
			ComicBook[] books = Books(2);
			books[0].Title = "Raw";
			var lists = new EditorValueLists(() => books, Array.Empty<string>());
			MultiBookEditorModel model = Model(books, null, lists);
			Assert.Contains("Raw", model.Values(EditorList.Title));
			Assert.Contains("Series 1", model.Values(EditorList.Series));
			Assert.NotEmpty(model.Languages);
		}
	}
}
