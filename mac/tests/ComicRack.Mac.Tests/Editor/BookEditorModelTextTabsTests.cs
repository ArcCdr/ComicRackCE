using System;
using System.Collections.Generic;
using System.Linq;
using ComicRack.Mac.Editor;
using ComicRack.Mac.Host;
using ComicRack.Mac.Host.Library;
using cYo.Projects.ComicRack.Engine;
using Xunit;

namespace ComicRack.Mac.Tests.Editor
{
	/// <summary>P5.7b: the fields of Plot &amp; Notes, Catalog and Custom (<c>ComicBookDialog.SetDataToEditor</c> and <c>SaveBook</c>; IN.03, IN.04, IN.05, PB.32, PB.33).</summary>
	public class BookEditorModelTextTabsTests
	{
		private static ComicBook Book(string series, int number, string file = "/nowhere/book.cbz")
		{
			return new ComicBook { Series = series, Number = number.ToString(), FilePath = file, PageCount = 10 };
		}

		private static AppPreferences Allow() => new AppPreferences { UpdateComicFiles = true, UpdateComicBookFiles = true };

		// ---- Plot & Notes

		[Fact]
		public void The_plot_and_notes_fields_show_the_book_with_the_line_endings_of_the_Windows_edit_box()
		{
			ComicBook book = Book("A", 1);
			book.Summary = "one\ntwo";
			book.Notes = "n1\r\nn2";
			book.Review = "great";
			book.Characters = "Batman, Robin";
			book.Teams = "Justice League";
			book.MainCharacterOrTeam = "Batman";
			book.Locations = "Gotham";
			book.ScanInformation = "Scan by me";
			book.Web = "https://example.com/a";
			var model = new BookEditorModel(book, null, Allow());
			Assert.Equal("one\r\ntwo", model.Summary);
			Assert.Equal("n1\r\nn2", model.Notes);
			Assert.Equal("great", model.Review);
			Assert.Equal("Batman, Robin", model.Characters);
			Assert.Equal("Justice League", model.Teams);
			Assert.Equal("Batman", model.MainCharacterOrTeam);
			Assert.Equal("Gotham", model.Locations);
			Assert.Equal("Scan by me", model.ScanInformation);
			Assert.Equal("https://example.com/a", model.Web);
			Assert.Equal("https://example.com/a", model.WebLink);
		}

		[Fact]
		public void OK_writes_the_plot_and_notes_fields_trimmed_and_the_Summary_link_keeps_the_address_it_had()
		{
			ComicBook book = Book("A", 1);
			book.Web = "https://old";
			var model = new BookEditorModel(book, null, Allow());
			model.Summary = "  A summary \r\nin two lines  ";
			model.Notes = "note";
			model.Review = " review ";
			model.Characters = " Batman ";
			model.Teams = "JLA";
			model.MainCharacterOrTeam = "Bruce";
			model.Locations = "Gotham, Metropolis";
			model.ScanInformation = "scan";
			model.Web = " https://new ";
			Assert.Equal(string.Empty, book.Summary);
			model.Ok();
			Assert.Equal("A summary \r\nin two lines", book.Summary);
			Assert.Equal("note", book.Notes);
			Assert.Equal("review", book.Review);
			Assert.Equal("Batman", book.Characters);
			Assert.Equal("JLA", book.Teams);
			Assert.Equal("Bruce", book.MainCharacterOrTeam);
			Assert.Equal("Gotham, Metropolis", book.Locations);
			Assert.Equal("scan", book.ScanInformation);
			Assert.Equal("https://new", book.Web);
			Assert.Equal("https://old", model.WebLink); // linkLabel.Text is set when the book is shown
			model.Apply();
			Assert.Equal("https://new", model.WebLink);
		}

		[Fact]
		public void A_multi_line_text_that_is_the_same_apart_from_its_line_endings_does_not_change_the_book()
		{
			ComicBook book = Book("A", 1);
			book.Summary = "one\ntwo";
			book.Notes = "x\ny";
			var changed = new List<string>();
			book.BookChanged += (s, e) => changed.Add(e.PropertyName);
			var model = new BookEditorModel(book, null, Allow());
			model.Ok();
			Assert.DoesNotContain("Summary", changed);
			Assert.DoesNotContain("Notes", changed);
			Assert.Equal("one\ntwo", book.Summary);
		}

		[Fact]
		public void Cancel_and_moving_on_keep_the_plot_and_notes_of_each_book_apart()
		{
			ComicBook a = Book("A", 1, "/n/a.cbz"), b = Book("B", 2, "/n/b.cbz");
			a.Summary = "sa";
			b.Summary = "sb";
			b.Characters = "cb";
			var model = new BookEditorModel(a, new[] { a, b }, Allow());
			model.Summary = "edited a";
			Assert.Equal("sa", a.Summary);
			Assert.True(model.GoNext());
			Assert.Equal("edited a", a.Summary); // Next writes the book it leaves
			Assert.Equal("sb", model.Summary);
			Assert.Equal("cb", model.Characters);
			Assert.Equal(string.Empty, a.Characters);
		}

		[Fact]
		public void The_scan_information_is_for_a_book_with_a_file()
		{
			Assert.True(new BookEditorModel(Book("A", 1), null, Allow()).ShowProposedValues);
			Assert.False(new BookEditorModel(new ComicBook { Series = "Mine" }, null, Allow()).ShowProposedValues);
		}

		// ---- Catalog

		[Fact]
		public void The_catalog_fields_show_the_book()
		{
			ComicBook book = new ComicBook { Series = "Mine", Number = "1" };
			book.BookStore = "Forbidden Planet";
			book.BookPrice = 12.5f;
			book.BookAge = "Modern";
			book.BookCondition = "Mint";
			book.BookOwner = "Me";
			book.BookLocation = "Shelf 3";
			book.BookCollectionStatus = "Owned";
			book.BookNotes = "line1\nline2";
			book.ISBN = "123-456";
			book.PageCount = 32;
			book.AddedTime = new DateTime(2024, 5, 6, 7, 8, 9);
			book.ReleasedTime = new DateTime(1995, 1, 2);
			book.OpenedTime = new DateTime(2025, 3, 4, 5, 6, 7);
			var model = new BookEditorModel(book, null, Allow());
			Assert.Equal("Forbidden Planet", model.BookStore);
			Assert.Equal(book.BookPriceAsText, model.BookPrice);
			Assert.Equal("Modern", model.BookAge);
			Assert.Equal("Mint", model.BookCondition);
			Assert.Equal("Me", model.BookOwner);
			Assert.Equal("Shelf 3", model.BookLocation);
			Assert.Equal("Owned", model.BookCollectionStatus);
			Assert.Equal("line1\r\nline2", model.BookNotes);
			Assert.Equal("123-456", model.Isbn);
			Assert.Equal("32", model.PagesAsTextSimple);
			Assert.Equal(new DateTime(2024, 5, 6, 7, 8, 9), model.AddedTime);
			Assert.Equal(new DateTime(1995, 1, 2), model.ReleasedTime);
			Assert.Equal(new DateTime(2025, 3, 4, 5, 6, 7), model.OpenedTime);
			Assert.True(model.ShowFilelessCatalogFields);
		}

		[Fact]
		public void OK_writes_the_catalog_fields_trimmed_and_a_price_that_is_not_a_number_becomes_unknown()
		{
			ComicBook book = new ComicBook { Series = "Mine", Number = "1" };
			var model = new BookEditorModel(book, null, Allow());
			model.BookStore = " Store ";
			model.BookPrice = " " + 7.25f.ToString("0.00") + " "; // the price is written and read in the culture of the system
			model.BookAge = "Silver";
			model.BookCondition = "Good";
			model.BookOwner = "Owner";
			model.BookLocation = "Box";
			model.BookCollectionStatus = " For sale ";
			model.BookNotes = " notes \r\nmore ";
			model.Isbn = " 978 ";
			model.PagesAsTextSimple = "48";
			model.AddedTime = new DateTime(2020, 2, 3, 4, 5, 6);
			model.ReleasedTime = new DateTime(2019, 7, 8);
			model.OpenedTime = new DateTime(2021, 9, 10, 11, 12, 13);
			model.Ok();
			Assert.Equal("Store", book.BookStore);
			Assert.Equal(7.25f, book.BookPrice);
			Assert.Equal("Silver", book.BookAge);
			Assert.Equal("Good", book.BookCondition);
			Assert.Equal("Owner", book.BookOwner);
			Assert.Equal("Box", book.BookLocation);
			Assert.Equal("For sale", book.BookCollectionStatus);
			Assert.Equal("notes \r\nmore", book.BookNotes);
			Assert.Equal("978", book.ISBN);
			Assert.Equal(48, book.PageCount);
			Assert.Equal(new DateTime(2020, 2, 3, 4, 5, 6), book.AddedTime);
			Assert.Equal(new DateTime(2019, 7, 8), book.ReleasedTime);
			Assert.Equal(new DateTime(2021, 9, 10, 11, 12, 13), book.OpenedTime);
			model.BookPrice = "cheap";
			model.Ok();
			Assert.Equal(-1f, book.BookPrice);
			model.BookPrice = "-3";
			model.Ok();
			Assert.Equal(-1f, book.BookPrice);
		}

		[Fact]
		public void An_unchanged_price_is_not_rewritten()
		{
			ComicBook book = new ComicBook { Series = "Mine", BookPrice = 3.456f }; // shown as 3.46
			var model = new BookEditorModel(book, null, Allow());
			Assert.Equal(3.456f.ToString("0.00"), model.BookPrice);
			model.Ok();
			Assert.Equal(3.456f, book.BookPrice);
		}

		[Fact]
		public void A_book_with_a_file_keeps_its_page_count_and_opened_time_and_hides_those_fields()
		{
			ComicBook book = Book("A", 1);
			book.OpenedTime = new DateTime(2022, 2, 2, 2, 2, 2);
			var model = new BookEditorModel(book, null, Allow());
			Assert.False(model.ShowFilelessCatalogFields);
			model.PagesAsTextSimple = "99";
			model.OpenedTime = DateTime.MinValue;
			model.Ok();
			Assert.Equal(10, book.PageCount);
			Assert.Equal(new DateTime(2022, 2, 2, 2, 2, 2), book.OpenedTime);
		}

		[Fact]
		public void A_date_a_date_box_cannot_hold_shows_as_not_set_and_is_saved_that_way()
		{
			Assert.Equal(DateTime.MinValue, BookEditorModel.PickerValue(DateTime.MinValue));
			Assert.Equal(DateTime.MinValue, BookEditorModel.PickerValue(new DateTime(1752, 12, 31)));
			Assert.Equal(new DateTime(1753, 1, 1), BookEditorModel.PickerValue(new DateTime(1753, 1, 1)));
			Assert.Equal(DateTime.MinValue, BookEditorModel.PickerValue(new DateTime(9999, 6, 1)));
			ComicBook book = new ComicBook { Series = "Mine", ReleasedTime = new DateTime(1700, 1, 1) };
			var model = new BookEditorModel(book, null, Allow());
			Assert.Equal(DateTime.MinValue, model.ReleasedTime);
			model.Ok();
			Assert.Equal(DateTime.MinValue, book.ReleasedTime);
		}

		[Fact]
		public void The_catalog_tab_follows_CatalogOnlyForFileless_and_the_custom_tab_follows_ShowCustomBookFields()
		{
			ComicBook linked = Book("A", 1), fileless = new ComicBook { Series = "Mine" };
			var preferences = Allow();
			Assert.False(new BookEditorModel(linked, null, preferences).ShowCatalogTab); // true by default: only for books without a file
			Assert.True(new BookEditorModel(fileless, null, preferences).ShowCatalogTab);
			Assert.False(new BookEditorModel(fileless, null, preferences).ShowCustomTab); // ShowCustomBookFields is off by default
			preferences.CatalogOnlyForFileless = false;
			preferences.ShowCustomBookFields = true;
			Assert.True(new BookEditorModel(linked, null, preferences).ShowCatalogTab);
			Assert.True(new BookEditorModel(linked, null, preferences).ShowCustomTab);
			Assert.True(new BookEditorModel(linked, null, preferences).CatalogEnabled);
			preferences.UpdateComicBookFiles = false;
			var restricted = new BookEditorModel(linked, null, preferences);
			Assert.False(restricted.ShowCatalogTab); // a book in a file that may not be updated has no catalog and no custom values
			Assert.False(restricted.ShowCustomTab);
		}

		[Fact]
		public void The_value_lists_of_the_catalog_come_from_the_library_and_the_defaults()
		{
			var books = new[] { Book("A", 1), Book("B", 2, "/n/b.cbz") };
			books[0].BookStore = "Shop";
			books[1].BookStore = "Shop";
			books[1].BookOwner = "Zed";
			books[0].BookOwner = "Amy";
			books[0].BookAge = "Golden";
			var lists = new EditorValueLists(() => books, Array.Empty<string>());
			var model = new BookEditorModel(books[0], books, Allow(), lists);
			Assert.Equal(new[] { "Shop", "Shop" }, model.Values(EditorList.BookStore));
			Assert.Equal(new[] { "Amy", "Zed" }, model.Values(EditorList.BookOwner)); // sorted
			Assert.Contains("Golden", model.Values(EditorList.BookAge));
		}

		// ---- Custom

		[Fact]
		public void The_custom_tab_has_a_row_for_every_key_of_the_library_in_order_and_an_empty_row_for_a_new_key()
		{
			var books = new[] { Book("A", 1), Book("B", 2, "/n/b.cbz") };
			books[0].SetCustomValue("Rating Source", "IMDB");
			books[1].SetCustomValue("Collection", "Mine");
			books[1].SetCustomValue("Rating Source", "Other");
			var lists = new EditorValueLists(() => books, Array.Empty<string>());
			var model = new BookEditorModel(books[0], books, Allow(), lists);
			Assert.Equal(new[] { "Collection", "Rating Source", string.Empty }, model.CustomRows.Select(r => r.Name));
			Assert.Equal(new[] { string.Empty, "IMDB", string.Empty }, model.CustomRows.Select(r => r.Value));
			Assert.True(model.GoNext());
			Assert.Equal(new[] { "Mine", "Other", string.Empty }, model.CustomRows.Select(r => r.Value));
			Assert.Equal(3, model.CustomRows.Count);
		}

		[Fact]
		public void Script_values_are_kept_but_hidden_unless_the_extended_setting_shows_them()
		{
			var books = new[] { Book("A", 1) };
			books[0].SetCustomValue("Plain", "1");
			books[0].SetCustomValue("script.Value", "2");
			var lists = new EditorValueLists(() => books, Array.Empty<string>());
			var model = new BookEditorModel(books[0], books, Allow(), lists);
			Assert.Equal(new[] { false, true, false }, model.CustomRows.Select(r => r.Hidden)); // Plain, script.Value, the empty row
			model.CustomRows.First(r => r.Name == "Plain").Value = "changed";
			model.Ok();
			Assert.Equal("changed", books[0].GetCustomValue("Plain"));
			Assert.Equal("2", books[0].GetCustomValue("script.Value")); // the hidden row is saved with the others
			var shown = new BookEditorModel(books[0], books, Allow(), lists) { ShowScriptValues = true };
			shown.Apply();
			Assert.All(shown.CustomRows, r => Assert.False(r.Hidden));
		}

		[Fact]
		public void OK_writes_the_rows_that_have_a_name_and_a_value_and_a_cleared_value_removes_the_key_from_the_book()
		{
			var books = new[] { Book("A", 1), Book("B", 2, "/n/b.cbz") };
			books[0].SetCustomValue("Keep", "k");
			books[0].SetCustomValue("Gone", "g");
			books[1].SetCustomValue("Other", "o");
			var lists = new EditorValueLists(() => books, Array.Empty<string>());
			var model = new BookEditorModel(books[0], books, Allow(), lists);
			model.CustomRows.First(r => r.Name == "Gone").Value = string.Empty;
			CustomValueRow added = model.CustomRows.Last();
			added.Name = "Brand New";
			model.EnsureNewCustomRow();
			Assert.Equal(model.CustomRows.Count - 2, model.CustomRows.IndexOf(added));
			Assert.True(model.CustomRows.Last().IsEmpty);
			added.Value = "fresh";
			model.CustomRows.Add(new CustomValueRow("Name only", string.Empty, false));
			model.CustomRows.Add(new CustomValueRow(string.Empty, "value only", false));
			model.Ok();
			Assert.Equal("k", books[0].GetCustomValue("Keep"));
			Assert.Null(books[0].GetCustomValue("Gone"));
			Assert.Equal("fresh", books[0].GetCustomValue("Brand New"));
			Assert.Null(books[0].GetCustomValue("Name only"));
			Assert.Null(books[0].GetCustomValue("Other"));
			Assert.Equal(new[] { "Brand New", "Keep" }, books[0].GetCustomValues().Select(p => p.Key).OrderBy(k => k));
			// the key the dialog just saved is a row of every book from now on
			Assert.True(model.GoNext());
			Assert.Contains(model.CustomRows, r => r.Name == "Brand New" && r.Value == string.Empty);
		}

		[Fact]
		public void The_values_other_books_have_under_a_key_are_the_suggestions_of_its_value_cell()
		{
			var books = new[] { Book("A", 1), Book("B", 2, "/n/b.cbz"), Book("C", 3, "/n/c.cbz") };
			books[0].SetCustomValue("Grade", "9.8");
			books[1].SetCustomValue("grade", "9.4");
			books[2].SetCustomValue("Grade", "9.8");
			books[2].SetCustomValue("Other", "x");
			var lists = new EditorValueLists(() => books, Array.Empty<string>());
			var model = new BookEditorModel(books[0], books, Allow(), lists);
			Assert.Equal(new[] { "9.4", "9.8" }, model.CustomValueSuggestions("Grade").OrderBy(s => s));
			Assert.Empty(model.CustomValueSuggestions(string.Empty));
		}

		[Fact]
		public void A_book_that_is_not_in_the_library_still_shows_its_own_custom_values()
		{
			ComicBook book = Book("A", 1);
			book.SetCustomValue("Mine", "yes");
			var model = new BookEditorModel(book, null, Allow());
			Assert.Equal(new[] { "Mine", string.Empty }, model.CustomRows.Select(r => r.Name));
		}
	}
}
