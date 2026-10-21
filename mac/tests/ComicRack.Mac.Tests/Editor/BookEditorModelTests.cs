using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ComicRack.Mac.Editor;
using ComicRack.Mac.Host;
using ComicRack.Mac.Host.Library;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Engine.Database;
using Xunit;

namespace ComicRack.Mac.Tests.Editor
{
	/// <summary>P5.7a: what the Info dialog edits and how it moves between books (<c>ComicBookDialog.cs</c> without a window; DL.01, IN.01, IN.02, IN.08, ME.02, ME.03).</summary>
	public class BookEditorModelTests
	{
		private static ComicBook Book(string series, int number, string file = "/nowhere/book.cbz")
		{
			return new ComicBook
			{
				Series = series,
				Number = number.ToString(),
				Title = "Title " + number,
				Volume = 2020,
				Year = 1999,
				Month = 3,
				Day = -1,
				Count = 12,
				Writer = "Stan Lee",
				Publisher = "Marvel",
				Format = "Annual",
				FilePath = file,
				PageCount = 10,
			};
		}

		// a linked book may be edited when the settings allow it (the Windows default is off: a book in the library is edited because it is in the container)
		private static AppPreferences Allow() => new AppPreferences { UpdateComicFiles = true, UpdateComicBookFiles = true };

		[Fact]
		public void Every_field_shows_the_book_and_unknown_numbers_are_empty()
		{
			ComicBook book = Book("Batman", 5);
			var model = new BookEditorModel(book, null, Allow());
			Assert.Equal("Batman", model.Series);
			Assert.Equal("5", model.Number);
			Assert.Equal("Title 5", model.Title);
			Assert.Equal("2020", model.Volume);
			Assert.Equal("1999", model.Year);
			Assert.Equal("3", model.Month);
			Assert.Equal(string.Empty, model.Day);
			Assert.Equal("12", model.Count);
			Assert.Equal("Stan Lee", model.Writer);
			Assert.Equal("Marvel", model.Publisher);
			Assert.Equal("Annual", model.Format);
			Assert.Equal(book.Caption, model.DialogTitle);
			Assert.True(model.IsLinked);
			Assert.Equal("/nowhere/book.cbz", model.PathText);
			Assert.False(model.HasNavigation);
			Assert.False(model.CanGoPrevious);
			Assert.False(model.CanGoNext);
		}

		[Fact]
		public void OK_writes_every_field_trimmed_and_unknown_numbers_become_minus_one()
		{
			ComicBook book = Book("Batman", 5);
			var model = new BookEditorModel(book, null, Allow());
			model.Series = "  Detective Comics  ";
			model.Number = " 7 ";
			model.Title = "The Joker";
			model.Volume = "abc";
			model.Year = "2001";
			model.Month = "-4";
			model.Day = "15";
			model.Count = string.Empty;
			model.Writer = "Bill Finger";
			model.Penciller = "Bob Kane";
			model.Genre = "Superhero";
			model.Tags = "classic, noir";
			model.SeriesComplete = ComicInfo.YesText;
			model.Manga = ComicInfo.YesRightToLeftText;
			model.BlackAndWhite = ComicInfo.NoText;
			model.Rating = 4;
			model.CommunityRating = 2;
			model.Ok();
			Assert.Equal("Detective Comics", book.Series);
			Assert.Equal("7", book.Number);
			Assert.Equal("The Joker", book.Title);
			Assert.Equal(-1, book.Volume);
			Assert.Equal(2001, book.Year);
			Assert.Equal(-1, book.Month);
			Assert.Equal(15, book.Day);
			Assert.Equal(-1, book.Count);
			Assert.Equal("Bill Finger", book.Writer);
			Assert.Equal("Bob Kane", book.Penciller);
			Assert.Equal("Superhero", book.Genre);
			Assert.Equal("classic, noir", book.Tags);
			Assert.Equal(YesNo.Yes, book.SeriesComplete);
			Assert.Equal(MangaYesNo.YesAndRightToLeft, book.Manga);
			Assert.Equal(YesNo.No, book.BlackAndWhite);
			Assert.Equal(4f, book.Rating);
			Assert.Equal(2f, book.CommunityRating);
			Assert.Equal("Marvel", book.Publisher); // not touched
		}

		[Fact]
		public void Nothing_is_written_until_OK_Apply_Previous_or_Next()
		{
			ComicBook book = Book("Batman", 5);
			var model = new BookEditorModel(book, null, Allow());
			model.Series = "Changed";
			model.Year = "1850";
			model.Rating = 5;
			Assert.Equal("Batman", book.Series);
			Assert.Equal(1999, book.Year);
			Assert.Equal(0f, book.Rating);
			// Cancel is not calling anything: the book stays as it was
			Assert.Equal("Batman", book.Series);
		}

		[Fact]
		public void Apply_writes_the_book_and_shows_it_again_as_the_book_holds_it()
		{
			ComicBook book = Book("Batman", 5);
			var model = new BookEditorModel(book, null, Allow());
			var loaded = 0;
			model.BookLoaded += (s, e) => loaded++;
			model.Series = "  Robin  ";
			model.Apply();
			Assert.Equal("Robin", book.Series);
			Assert.Equal("Robin", model.Series);
			Assert.Equal(1, loaded);
			// the changes made after Apply are again only in the editor
			model.Series = "Nightwing";
			Assert.Equal("Robin", book.Series);
		}

		[Fact]
		public void Previous_and_Next_write_the_edits_of_the_book_they_leave_and_select_the_new_one()
		{
			ComicBook[] books = { Book("A", 1, "/n/a.cbz"), Book("B", 2, "/n/b.cbz"), Book("C", 3, "/n/c.cbz") };
			var selected = new List<ComicBook>();
			var model = new BookEditorModel(books[1], books, Allow(), selectComic: b =>
			{
				selected.Add(b);
				return true;
			});
			Assert.True(model.HasNavigation);
			Assert.True(model.CanGoPrevious);
			Assert.True(model.CanGoNext);
			model.Series = "B edited";
			Assert.True(model.GoNext());
			Assert.Equal("B edited", books[1].Series);
			Assert.Equal("C", model.Series);
			Assert.Same(books[2], model.Current);
			Assert.False(model.CanGoNext);
			Assert.False(model.GoNext());
			model.Series = "C edited";
			Assert.True(model.GoPrevious());
			Assert.True(model.GoPrevious());
			Assert.Equal("C edited", books[2].Series);
			Assert.Equal("A", model.Series);
			Assert.False(model.CanGoPrevious);
			Assert.False(model.GoPrevious());
			// the first book is selected when the dialog opens, then once per move
			Assert.Equal(new[] { books[1], books[2], books[1], books[0] }, selected);
		}

		[Fact]
		public void A_book_with_a_file_that_is_not_in_the_library_is_read_only_unless_the_settings_allow_editing_the_files()
		{
			ComicBook book = Book("Batman", 5);
			var locked = new BookEditorModel(book, null, new AppPreferences());
			Assert.False(locked.DetailsEnabled);
			Assert.False(locked.PlotEnabled);
			Assert.False(locked.RatingEnabled);
			Assert.False(locked.CatalogEnabled);
			var open = new BookEditorModel(book, null, Allow());
			Assert.True(open.DetailsEnabled);
			Assert.True(open.PlotEnabled);
			Assert.True(open.RatingEnabled);
			Assert.True(open.CatalogEnabled);
		}

		[Fact]
		public void A_book_without_a_file_has_no_Pages_and_Colors_tab_and_shows_the_thumbnail_and_link_buttons()
		{
			var fileless = new ComicBook { Series = "Mine", Number = "1" };
			var model = new BookEditorModel(fileless, null, new AppPreferences());
			Assert.False(model.IsLinked);
			Assert.True(model.DetailsEnabled);
			Assert.True(model.RatingEnabled);
			Assert.False(model.ShowPagesTab);
			Assert.False(model.ShowColorsTab);
			Assert.True(model.ShowCatalogTab);
			Assert.True(model.ShowThumbnailButtons);
			Assert.False(model.ShowFileInfo);
			Assert.False(model.ShowWhere);
			Assert.False(model.ShowProposedValues);

			var linked = new BookEditorModel(Book("A", 1), null, Allow());
			Assert.True(linked.ShowPagesTab);
			Assert.True(linked.ShowColorsTab);
			Assert.False(linked.ShowThumbnailButtons);
			Assert.True(linked.ShowFileInfo);
			Assert.True(linked.ShowWhere);
			Assert.True(linked.ShowProposedValues);
		}

		[Fact]
		public void The_Catalog_tab_follows_CatalogOnlyForFileless_and_the_Custom_tab_follows_ShowCustomBookFields()
		{
			ComicBook book = Book("A", 1);
			var preferences = Allow();
			Assert.True(preferences.CatalogOnlyForFileless); // the defaults of Settings.cs
			Assert.False(preferences.ShowCustomBookFields);
			var model = new BookEditorModel(book, null, preferences);
			Assert.False(model.ShowCatalogTab);
			Assert.False(model.ShowCustomTab);
			preferences.CatalogOnlyForFileless = false;
			preferences.ShowCustomBookFields = true;
			model = new BookEditorModel(book, null, preferences);
			Assert.True(model.ShowCatalogTab);
			Assert.True(model.ShowCustomTab);
			// without the permission to write the book files the tabs are not shown for a book of a file
			model = new BookEditorModel(book, null, new AppPreferences { UpdateComicFiles = true, CatalogOnlyForFileless = false, ShowCustomBookFields = true });
			Assert.False(model.ShowCatalogTab);
			Assert.False(model.ShowCustomTab);
			preferences.CatalogOnlyForFileless = true;
			// a book without a file always has the Catalog tab
			model = new BookEditorModel(new ComicBook { Series = "Mine" }, null, preferences);
			Assert.True(model.ShowCatalogTab);
		}

		[Fact]
		public void Proposed_values_are_prompts_only_while_the_book_asks_for_them()
		{
			ComicBook book = Book("A", 1, "/nowhere/Spider-Man 012 (1999).cbz");
			book.Series = string.Empty;
			book.Number = string.Empty;
			book.Year = -1;
			book.EnableProposed = true;
			var model = new BookEditorModel(book, null, Allow());
			Assert.Equal(ComicInfo.YesText, model.EnableProposed);
			Assert.False(string.IsNullOrEmpty(model.ProposedSeries));
			Assert.Equal(book.ProposedSeries, model.ProposedSeries);
			Assert.Equal(book.ProposedNumber, model.ProposedNumber);
			int changes = 0;
			model.PromptsChanged += (s, e) => changes++;
			model.EnableProposed = ComicInfo.NoText;
			Assert.Equal(string.Empty, model.ProposedSeries);
			Assert.Equal(string.Empty, model.ProposedNumber);
			Assert.Equal(1, changes);
			model.EnableProposed = ComicInfo.YesText;
			Assert.Equal(book.ProposedSeries, model.ProposedSeries);
			model.Ok();
			Assert.True(book.EnableProposed);
		}

		[Fact]
		public void The_language_box_lists_the_used_languages_first_and_stores_the_culture_name()
		{
			ComicBook book = Book("A", 1);
			book.LanguageISO = "fr";
			var books = new[] { book, Book("B", 2, "/n/b.cbz"), Book("C", 3, "/n/c.cbz") };
			books[1].LanguageISO = "de";
			var lists = new EditorValueLists(() => books, Array.Empty<string>());
			var model = new BookEditorModel(book, books, Allow(), lists);
			Assert.Equal(string.Empty, model.Languages[0].Name);
			Assert.Equal(new[] { "de", "fr" }, model.Languages.Skip(1).Take(2).Select(l => l.Name).OrderBy(n => n).ToArray());
			Assert.True(model.Languages[3].SeparatorBefore);
			Assert.Equal("fr", model.Language);
			model.Language = "de";
			model.Ok();
			Assert.Equal("de", book.LanguageISO);
			model.Language = string.Empty;
			model.Ok();
			Assert.Equal(string.Empty, book.LanguageISO);
		}

		[Fact]
		public void The_value_lists_hold_what_the_library_uses_then_the_defaults()
		{
			var books = new[] { Book("A", 1), Book("B", 2, "/n/b.cbz") };
			books[0].Writer = "Alan Moore";
			books[1].Writer = "Alan Moore";
			books[1].Format = "Special";
			var lists = new EditorValueLists(() => books, Array.Empty<string>());
			var model = new BookEditorModel(books[0], books, Allow(), lists);
			List<string> writers = model.Values(EditorList.Writer);
			Assert.Equal(new[] { "Alan Moore", "Alan Moore" }, writers);
			Assert.Equal(new[] { "Alan Moore" }, EditorValueLists.Completions(writers));
			List<string> formats = EditorValueLists.WithEmpty(model.Values(EditorList.Format));
			Assert.Equal(string.Empty, formats[0]);
			Assert.Equal(1, formats.IndexOf("Annual"));
			Assert.True(formats.IndexOf("Annual") < formats.IndexOf(EditorValueLists.Separator));
			Assert.Equal(formats.Count, formats.Distinct().Count());
		}

		[Fact]
		public void Default_lists_come_from_the_DefaultLists_file_after_a_separator()
		{
			string file = System.IO.Path.GetTempFileName();
			try
			{
				System.IO.File.WriteAllText(file, "; comment\n[Book Genres]\nAdventure\nHorror\n\n[Age Ratings]\nTeen\nAdults Only 18+\n\n[Book Conditions]\nMint\n  Near\n");
				var books = new[] { Book("A", 1) };
				books[0].AgeRating = "Everyone";
				var lists = new EditorValueLists(() => books, new[] { file });
				Assert.Equal(new[] { "Adventure", "Horror" }, lists.DefaultGenres);
				List<string> ratings = lists.GetAgeRatingList();
				Assert.Equal("Everyone", ratings[0]);
				Assert.Equal(EditorValueLists.Separator, ratings[1]);
				Assert.Contains("Teen", ratings);
				Assert.Contains("Adults Only 18+", ratings);
				Assert.Equal(new[] { "Mint", "Mint: Near" }, lists.DefaultBookConditions);
			}
			finally
			{
				System.IO.File.Delete(file);
			}
		}

		[Fact]
		public void A_custom_thumbnail_is_for_books_without_a_file_and_raises_the_cover_event()
		{
			var fileless = new ComicBook { Series = "Mine" };
			var model = new BookEditorModel(fileless, null, new AppPreferences());
			var covers = new List<ComicBook>();
			model.CoverChanged += (s, b) => covers.Add(b);
			model.CustomThumbnailKey = "thumb-1";
			Assert.Equal("thumb-1", model.CustomThumbnailKey);
			Assert.Equal("thumb-1", Assert.Single(covers).CustomThumbnailKey);
			Assert.NotSame(fileless, covers[0]); // a copy: the book itself is written by OK
			Assert.NotEqual("thumb-1", fileless.CustomThumbnailKey);
			model.Ok();
			Assert.Equal("thumb-1", fileless.CustomThumbnailKey);
			model.ResetThumbnail();
			Assert.True(string.IsNullOrEmpty(model.CustomThumbnailKey));
			model.Ok();
			Assert.True(string.IsNullOrEmpty(fileless.CustomThumbnailKey));

			var linked = new BookEditorModel(Book("A", 1), null, Allow());
			linked.CustomThumbnailKey = "other";
			Assert.NotEqual("other", linked.CustomThumbnailKey);
		}

		[Fact]
		public void Link_to_File_shows_a_book_made_from_the_file_with_the_fields_of_the_book_and_OK_writes_the_path()
		{
			var fileless = new ComicBook { Series = "Mine", Number = "3" };
			var made = new ComicBook { FilePath = "/n/real.cbz", PageCount = 22 };
			var model = new BookEditorModel(fileless, null, new AppPreferences(), createBook: f => made);
			Assert.True(model.CanLinkFile);
			Assert.False(model.LinkFile("/n/real.cbz", f => new ComicBook(), out string problem));
			Assert.NotNull(problem);
			Assert.True(model.LinkFile("/n/real.cbz", f => null, out problem));
			Assert.Null(problem);
			Assert.Same(made, model.Display);
			Assert.Equal("Mine", model.Series);
			Assert.True(model.IsLinked);
			Assert.False(fileless.IsLinked);
			model.Ok();
			Assert.Equal("/n/real.cbz", fileless.FilePath);
		}

		[Fact]
		public void The_3D_cover_follows_the_setting_and_writes_it_back()
		{
			var preferences = Allow();
			Assert.True(preferences.InformationCover3D);
			var model = new BookEditorModel(Book("A", 1), null, preferences);
			Assert.True(model.ThreeD);
			var changed = new List<string>();
			model.PropertyChanged += (s, e) => changed.Add(e.PropertyName);
			model.ThreeD = false;
			Assert.False(preferences.InformationCover3D);
			Assert.Contains(nameof(BookEditorModel.ThreeD), changed);
		}

		[Fact]
		public async Task One_Undo_takes_back_everything_the_dialog_wrote_and_Redo_does_it_again()
		{
			using (var temp = new TempDir())
			using (Launch launch = await Launch.Start(temp.Combine("app")))
			{
				ComicDatabase db = launch.Service.Host.DatabaseManager.Database;
				ComicBook a = Book("Alpha", 1, "/n/a.cbz");
				ComicBook b = Book("Beta", 2, "/n/b.cbz");
				db.Books.Add(a);
				db.Books.Add(b);
				Assert.False(db.Undo.CanUndo);

				db.Undo.SetMarker("Show Info"); // MainForm.ShowInfo sets the marker before the dialog opens
				var model = new BookEditorModel(a, new[] { a, b }, launch.Service.Preferences.Current);
				model.Series = "Alpha edited";
				model.Year = "2010";
				model.Writer = "Someone";
				model.GoNext(); // writes the first book
				model.Series = "Beta edited";
				model.Ok();
				Assert.Equal("Alpha edited", a.Series);
				Assert.Equal("Beta edited", b.Series);

				Assert.True(db.Undo.CanUndo);
				Assert.Equal("Show Info", db.Undo.UndoLabel);
				db.Undo.Undo();
				Assert.Equal("Alpha", a.Series);
				Assert.Equal(1999, a.Year);
				Assert.Equal("Stan Lee", a.Writer);
				Assert.Equal("Beta", b.Series); // the marker started one step: both books are back
				Assert.True(db.Undo.CanRedo);
				db.Undo.Redo();
				Assert.Equal("Alpha edited", a.Series);
				Assert.Equal(2010, a.Year);
				Assert.Equal("Beta edited", b.Series);
			}
		}
	}
}
