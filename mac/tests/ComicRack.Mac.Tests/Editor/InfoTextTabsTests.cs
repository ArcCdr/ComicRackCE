using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using ComicRack.Mac.Controls;
using ComicRack.Mac.Editor;
using ComicRack.Mac.Host;
using ComicRack.Mac.Host.Library;
using ComicRack.Mac.Services;
using ComicRack.Mac.Tests.HiDpi;
using ComicRack.Mac.Views;
using cYo.Projects.ComicRack.Engine;
using Xunit;

namespace ComicRack.Mac.Tests.Editor
{
	/// <summary>P5.7b: the Plot &amp; Notes, Catalog and Custom tabs of the Info dialog as a window (IN.03, IN.04, IN.05): positions, texts, fields, lists and what OK writes.</summary>
	public class InfoTextTabsTests
	{
		private static AppPreferences Allow() => new AppPreferences { UpdateComicFiles = true, UpdateComicBookFiles = true, CatalogOnlyForFileless = false, ShowCustomBookFields = true };

		private sealed class Fixture : IDisposable
		{
			public readonly ComicBook[] Books;
			public readonly AppPreferences Preferences;
			public readonly BookEditorModel Model;
			public readonly ComicBookDialog Dialog;

			public Fixture(int count = 3, AppPreferences preferences = null, bool fileless = false, Func<ComicBook[], ComicBook[]> prepare = null)
			{
				Books = Enumerable.Range(1, count).Select(i =>
				{
					var book = new ComicBook { Series = "Series " + i, Number = i.ToString() };
					if (!fileless)
					{
						book.FilePath = "/nowhere/" + i + ".cbz";
						book.PageCount = 10;
					}
					return book;
				}).ToArray();
				Books = prepare?.Invoke(Books) ?? Books;
				Preferences = preferences ?? Allow();
				var lists = new EditorValueLists(() => Books, Array.Empty<string>());
				Model = new BookEditorModel(Books[0], Books, Preferences, lists);
				Dialog = new ComicBookDialog(new BookInfoRequest { Model = Model, Preferences = Preferences });
				Dialog.Show();
				Dpi.Settle(Dialog);
			}

			public void Dispose() => Dialog.Close();
		}

		private static void Click(Button button) => button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

		private static void Select(ComicBookDialog dialog, string tab)
		{
			dialog.Tabs.SelectedItem = dialog.TabPages.Single(t => (string)t.Tag == tab);
			Dpi.Settle(dialog);
		}

		private static void AssertAt(UserControl page, string name, double x, double y, double width, double? height = null)
		{
			Control control = page.FindControl<Control>(name);
			Assert.NotNull(control);
			Rect area = Dpi.Area(control, page);
			bool ok = Math.Abs(area.X - x) < 0.5 && Math.Abs(area.Y - y) < 1.5 && (width <= 0 || Math.Abs(area.Width - width) < 0.5) && (height == null || Math.Abs(area.Height - height.Value) < 1.5);
			Assert.True(ok, name + " is at " + area + ", the Designer has " + x + "," + y + " " + width + (height == null ? string.Empty : " x " + height));
		}

		// ---- Plot & Notes

		[Fact]
		public async Task The_Plot_and_Notes_tab_is_laid_out_at_the_positions_of_the_Designer()
		{
			await Ui.Run(() =>
			{
				using (var f = new Fixture())
				{
					Select(f.Dialog, "tabPlot");
					InfoPlotPage page = f.Dialog.PlotPage;
					Assert.Equal(566, page.Bounds.Width);
					Assert.InRange(page.Bounds.Height, 438, 446);
					double drop = 442 - page.Bounds.Height; // the bottom anchored controls keep their distance to the bottom
					AssertAt(page, "tabNotes", 11, 15, 539, 233 - drop);
					// the boxes with a list are 16 narrower than the Designer's width and have the selector button at their right
					AssertAt(page, "txCharacters", 11, 278 - drop, 244, 93);
					AssertAt(page, "txCharactersSelector", 255, 278 - drop, 16, 93);
					AssertAt(page, "txMainCharacterOrTeam", 286, 278 - drop, 248, 20);
					AssertAt(page, "txMainCharacterOrTeamSelector", 534, 278 - drop, 16, 20);
					AssertAt(page, "txTeams", 286, 316 - drop, 248, 20);
					AssertAt(page, "txTeamsSelector", 534, 316 - drop, 16, 20);
					AssertAt(page, "txLocations", 287, 351 - drop, 247, 20);
					AssertAt(page, "txLocationsSelector", 534, 351 - drop, 16, 20);
					AssertAt(page, "txScanInformation", 11, 412 - drop, 260, 20);
					AssertAt(page, "txWeblink", 287, 412 - drop, 263, 20);
					AssertAt(page, "labelCharacters", 9, 263 - drop, 0);
					AssertAt(page, "labelMainCharacterOrTeam", 284, 263 - drop, 190);
					AssertAt(page, "labelTeams", 284, 301 - drop, 102);
					AssertAt(page, "labelLocations", 285, 336 - drop, 118);
					AssertAt(page, "labelScanInformation", 9, 397 - drop, 0);
					AssertAt(page, "labelWeb", 285, 397 - drop, 91);
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task The_Plot_and_Notes_tab_widens_with_the_window_the_way_the_anchors_say()
		{
			await Ui.Run(() =>
			{
				using (var f = new Fixture())
				{
					Select(f.Dialog, "tabPlot");
					InfoPlotPage page = f.Dialog.PlotPage;
					Rect characters = Dpi.Area(page.FindControl<Control>("txCharacters"), page), teams = Dpi.Area(page.FindControl<Control>("txTeams"), page), tabs = Dpi.Area(page.FindControl<Control>("tabNotes"), page);
					f.Dialog.Width += 100;
					Dpi.Settle(f.Dialog);
					Rect characters2 = Dpi.Area(page.FindControl<Control>("txCharacters"), page), teams2 = Dpi.Area(page.FindControl<Control>("txTeams"), page), tabs2 = Dpi.Area(page.FindControl<Control>("tabNotes"), page);
					Assert.Equal(characters.Width + 100, characters2.Width, 0.5); // anchored left and right
					Assert.Equal(teams.Width, teams2.Width, 0.5); // anchored bottom and right: keeps its size, moves
					Assert.Equal(teams.X + 100, teams2.X, 0.5);
					Assert.Equal(tabs.Width + 100, tabs2.Width, 0.5);
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task The_labels_and_sub_tabs_of_the_Plot_and_Notes_tab_are_the_original_texts()
		{
			await Ui.Run(() =>
			{
				using (var f = new Fixture())
				{
					InfoPlotPage page = f.Dialog.PlotPage;
					var expected = new Dictionary<string, string>
					{
						["labelCharacters"] = "Characters:", ["labelTeams"] = "Teams:", ["labelLocations"] = "Locations:", ["labelMainCharacterOrTeam"] = "Main Character or Team:",
						["labelScanInformation"] = "Scan Information:", ["labelWeb"] = "Web:",
					};
					foreach (KeyValuePair<string, string> pair in expected)
					{
						Assert.Equal(pair.Value, page.FindControl<Label>(pair.Key).Content);
					}
					Assert.Equal(new object[] { "Summary", "Notes", "Review" }, page.NotesTabs.Items.OfType<TabItem>().Select(t => t.Header));
					Assert.Equal(0, page.NotesTabs.SelectedIndex);
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task OK_writes_the_edits_of_the_Plot_and_Notes_tab_and_Cancel_writes_nothing()
		{
			await Ui.Run(() =>
			{
				using (var f = new Fixture())
				{
					InfoPlotPage page = f.Dialog.PlotPage;
					page.FindControl<TextBox>("txSummary").Text = "A summary\r\nof two lines";
					page.FindControl<TextBox>("txNotes").Text = "notes";
					page.FindControl<TextBox>("txReview").Text = "review";
					page.FindControl<TextBox>("txCharacters").Text = "Batman, Robin";
					page.FindControl<AutoCompleteBox>("txTeams").Text = "JLA";
					page.FindControl<AutoCompleteBox>("txLocations").Text = "Gotham";
					page.FindControl<AutoCompleteBox>("txMainCharacterOrTeam").Text = "Bruce";
					page.FindControl<AutoCompleteBox>("txScanInformation").Text = "scanned";
					page.FindControl<TextBox>("txWeblink").Text = "https://example.com";
					Assert.Equal(string.Empty, f.Books[0].Summary);
					Click(f.Dialog.OkButton);
					Assert.Equal("A summary\r\nof two lines", f.Books[0].Summary);
					Assert.Equal("notes", f.Books[0].Notes);
					Assert.Equal("review", f.Books[0].Review);
					Assert.Equal("Batman, Robin", f.Books[0].Characters);
					Assert.Equal("JLA", f.Books[0].Teams);
					Assert.Equal("Gotham", f.Books[0].Locations);
					Assert.Equal("Bruce", f.Books[0].MainCharacterOrTeam);
					Assert.Equal("scanned", f.Books[0].ScanInformation);
					Assert.Equal("https://example.com", f.Books[0].Web);
				}
				using (var f = new Fixture())
				{
					f.Dialog.PlotPage.FindControl<TextBox>("txSummary").Text = "never written";
					Click(f.Dialog.CancelButton);
					Assert.Equal(string.Empty, f.Books[0].Summary);
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task The_multi_line_boxes_make_the_line_endings_of_the_Windows_edit_box_and_Characters_takes_no_line_break()
		{
			await Ui.Run(() =>
			{
				using (var f = new Fixture())
				{
					InfoPlotPage page = f.Dialog.PlotPage;
					Assert.Equal("\r\n", page.FindControl<TextBox>("txSummary").NewLine);
					Assert.Equal("\r\n", page.FindControl<TextBox>("txNotes").NewLine);
					Assert.Equal("\r\n", page.FindControl<TextBox>("txReview").NewLine);
					Assert.True(page.FindControl<TextBox>("txSummary").AcceptsReturn);
					TextBox characters = page.FindControl<TextBox>("txCharacters");
					Assert.False(characters.AcceptsReturn);
					Assert.Equal(Avalonia.Media.TextWrapping.Wrap, characters.TextWrapping);
					Select(f.Dialog, "tabPlot");
					characters.Focus();
					characters.RaiseEvent(new TextInputEventArgs { Text = "a\nb", RoutedEvent = InputElement.TextInputEvent });
					Assert.Equal(string.Empty, characters.Text);
					characters.RaiseEvent(new TextInputEventArgs { Text = "Hulk", RoutedEvent = InputElement.TextInputEvent });
					Assert.Equal("Hulk", characters.Text);
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task The_boxes_of_the_Plot_and_Notes_tab_offer_the_values_the_library_uses_and_the_list_selector_gives_the_result_back()
		{
			await Ui.Run(() =>
			{
				using (var f = new Fixture(prepare: books =>
				{
					books[0].Teams = "Avengers";
					books[1].Teams = "X-Men";
					books[2].Teams = "Avengers";
					books[1].Characters = "Wolverine, Storm";
					books[0].Characters = "Storm";
					return books;
				}))
				{
					Select(f.Dialog, "tabPlot");
					InfoPlotPage page = f.Dialog.PlotPage;
					AutoCompleteBox teams = page.FindControl<AutoCompleteBox>("txTeams");
					Assert.Null(teams.ItemsSource);
					page.EnsureList(teams);
					Assert.Equal(new[] { "Avengers", "X-Men" }, ((IEnumerable<string>)teams.ItemsSource).OrderBy(s => s));
					ListSelectorModel.LastTab = 0;
					Avalonia.Controls.Flyout flyout = page.OpenSelector(page.FindControl<Button>("txTeamsSelector"), teams, EditorList.Teams);
					Dpi.Settle(f.Dialog);
					var view = (ListSelectorView)flyout.Content;
					Assert.Equal(new[] { "Avengers" }, view.Model.Own);
					Assert.Equal(new[] { "X-Men" }, view.Model.Pool);
					Click(view.FindControl<Button>("btAllToOwn"));
					flyout.Hide();
					Dpi.Settle(f.Dialog);
					Assert.Contains("X-Men", teams.Text);
					Assert.Contains("Avengers", teams.Text);
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task The_Scan_Information_box_shows_only_for_a_book_with_a_file()
		{
			await Ui.Run(() =>
			{
				using (var f = new Fixture())
				{
					Assert.True(f.Dialog.PlotPage.FindControl<Label>("labelScanInformation").IsVisible);
					Assert.True(f.Dialog.PlotPage.FindControl<AutoCompleteBox>("txScanInformation").IsVisible);
				}
				using (var f = new Fixture(fileless: true))
				{
					Assert.False(f.Dialog.PlotPage.FindControl<Label>("labelScanInformation").IsVisible);
					Assert.False(f.Dialog.PlotPage.FindControl<AutoCompleteBox>("txScanInformation").IsVisible);
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task The_sub_tab_the_user_chose_is_kept_for_the_next_dialog()
		{
			await Ui.Run(() =>
			{
				using (var f = new Fixture())
				{
					Select(f.Dialog, "tabPlot");
					f.Dialog.PlotPage.NotesTabs.SelectedIndex = 2;
					Click(f.Dialog.CancelButton);
				}
				using (var f = new Fixture())
				{
					Assert.Equal(2, f.Dialog.PlotPage.NotesTabs.SelectedIndex);
					f.Dialog.PlotPage.NotesTabs.SelectedIndex = 0;
					Click(f.Dialog.CancelButton);
				}
				using (var f = new Fixture())
				{
					Assert.Equal(0, f.Dialog.PlotPage.NotesTabs.SelectedIndex);
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task The_Plot_and_Notes_tab_is_disabled_for_a_book_that_cannot_be_edited()
		{
			await Ui.Run(() =>
			{
				using (var f = new Fixture(preferences: new AppPreferences()))
				{
					Assert.False(f.Dialog.PlotPage.IsEnabled);
				}
				using (var f = new Fixture())
				{
					Assert.True(f.Dialog.PlotPage.IsEnabled);
				}
				return Task.CompletedTask;
			});
		}

		// ---- Catalog

		[Fact]
		public async Task The_Catalog_tab_is_laid_out_at_the_positions_of_the_Designer()
		{
			await Ui.Run(() =>
			{
				using (var f = new Fixture(fileless: true))
				{
					Select(f.Dialog, "tabCatalog");
					InfoCatalogPage page = f.Dialog.CatalogPage;
					Assert.Equal(566, page.Bounds.Width);
					double drop = 442 - page.Bounds.Height;
					AssertAt(page, "cbBookStore", 11, 35, 150, 21);
					AssertAt(page, "cbBookPrice", 167, 34, 126, 21);
					AssertAt(page, "dtpReleasedTime", 315, 36, 235, 20);
					AssertAt(page, "txISBN", 11, 73, 150, 20);
					AssertAt(page, "txPagesAsTextSimple", 167, 73, 115, 20); // 11 narrower: the (hidden) spin button takes the room
					AssertAt(page, "dtpAddedTime", 315, 73, 235, 20);
					AssertAt(page, "cbBookAge", 11, 112, 150, 21);
					AssertAt(page, "cbBookCondition", 167, 112, 126, 21);
					AssertAt(page, "dtpOpenedTime", 316, 113, 234, 20);
					AssertAt(page, "cbBookOwner", 11, 168, 282, 21);
					AssertAt(page, "cbBookLocation", 317, 168, 233, 21);
					AssertAt(page, "txCollectionStatus", 11, 210, 523, 20);
					AssertAt(page, "txCollectionStatusSelector", 534, 210, 16, 20);
					AssertAt(page, "txBookNotes", 11, 265, 539, 157 - drop);
					AssertAt(page, "labelBookStore", 11, 21, 0);
					AssertAt(page, "labelBookPrice", 165, 19, 95);
					AssertAt(page, "labelReleasedTime", 314, 21, 116);
					AssertAt(page, "labelISBN", 9, 58, 0);
					AssertAt(page, "labelPagesAsTextSimple", 165, 58, 100);
					AssertAt(page, "labelAddedTime", 314, 58, 158);
					AssertAt(page, "labelBookAge", 9, 97, 0);
					AssertAt(page, "labelBookCondition", 165, 98, 117);
					AssertAt(page, "labelOpenedTime", 315, 98, 137);
					AssertAt(page, "labelBookOwner", 9, 154, 0);
					AssertAt(page, "labelBookLocation", 315, 154, 140);
					AssertAt(page, "labelBookCollectionStatus", 9, 195, 0);
					AssertAt(page, "labelBookNotes", 9, 250, 0);
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task The_labels_of_the_Catalog_tab_are_the_original_texts()
		{
			await Ui.Run(() =>
			{
				using (var f = new Fixture(fileless: true))
				{
					InfoCatalogPage page = f.Dialog.CatalogPage;
					var expected = new Dictionary<string, string>
					{
						["labelBookStore"] = "Store:", ["labelBookPrice"] = "Price:", ["labelReleasedTime"] = "Released:", ["labelISBN"] = "ISBN:", ["labelPagesAsTextSimple"] = "Pages:",
						["labelAddedTime"] = "Added/Purchased:", ["labelBookAge"] = "Age:", ["labelBookCondition"] = "Condition:", ["labelOpenedTime"] = "Opened/Read:",
						["labelBookOwner"] = "Owner:", ["labelBookLocation"] = "Book Location:", ["labelBookCollectionStatus"] = "Collection Status:", ["labelBookNotes"] = "Notes about this Book:",
					};
					foreach (KeyValuePair<string, string> pair in expected)
					{
						Assert.Equal(pair.Value, page.FindControl<Label>(pair.Key).Content);
					}
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task The_Catalog_tab_shows_Opened_and_Pages_for_a_book_without_a_file_only()
		{
			await Ui.Run(() =>
			{
				string[] fileless = { "labelOpenedTime", "dtpOpenedTime", "labelPagesAsTextSimple", "txPagesAsTextSimple" };
				using (var f = new Fixture(fileless: true))
				{
					Assert.All(fileless, name => Assert.True(f.Dialog.CatalogPage.FindControl<Control>(name).IsVisible, name));
				}
				using (var f = new Fixture())
				{
					Assert.All(fileless, name => Assert.False(f.Dialog.CatalogPage.FindControl<Control>(name).IsVisible, name));
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task The_Catalog_tab_follows_CatalogOnlyForFileless()
		{
			await Ui.Run(() =>
			{
				using (var f = new Fixture(preferences: new AppPreferences { UpdateComicFiles = true, UpdateComicBookFiles = true }))
				{
					// the Windows default: a book with a file has no Catalog tab, and no Custom tab either
					Assert.False(f.Dialog.TabPages.Single(t => (string)t.Tag == "tabCatalog").IsVisible);
					Assert.False(f.Dialog.TabPages.Single(t => (string)t.Tag == "tabCustom").IsVisible);
				}
				using (var f = new Fixture(preferences: Allow()))
				{
					Assert.True(f.Dialog.TabPages.Single(t => (string)t.Tag == "tabCatalog").IsVisible);
					Assert.True(f.Dialog.TabPages.Single(t => (string)t.Tag == "tabCustom").IsVisible);
				}
				using (var f = new Fixture(fileless: true, preferences: new AppPreferences { CatalogOnlyForFileless = true }))
				{
					Assert.True(f.Dialog.TabPages.Single(t => (string)t.Tag == "tabCatalog").IsVisible);
					Assert.False(f.Dialog.TabPages.Single(t => (string)t.Tag == "tabCustom").IsVisible);
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task OK_writes_the_edits_of_the_Catalog_tab()
		{
			await Ui.Run(() =>
			{
				using (var f = new Fixture(fileless: true))
				{
					Select(f.Dialog, "tabCatalog");
					InfoCatalogPage page = f.Dialog.CatalogPage;
					page.FindControl<CrValueComboBox>("cbBookStore").Value = "Shop";
					page.FindControl<CrValueComboBox>("cbBookPrice").Value = 4.5f.ToString("0.00");
					page.FindControl<CrValueComboBox>("cbBookAge").Value = "Modern";
					page.FindControl<CrValueComboBox>("cbBookCondition").Value = "Fine";
					page.FindControl<CrValueComboBox>("cbBookOwner").Value = "Me";
					page.FindControl<CrValueComboBox>("cbBookLocation").Value = "Shelf";
					page.FindControl<AutoCompleteBox>("txCollectionStatus").Text = "Owned";
					page.FindControl<TextBox>("txISBN").Text = "978-3";
					page.FindControl<TextBox>("txPagesAsTextSimple").Text = "44";
					page.FindControl<TextBox>("txBookNotes").Text = "some\r\nnotes";
					Click(f.Dialog.OkButton);
					ComicBook book = f.Books[0];
					Assert.Equal("Shop", book.BookStore);
					Assert.Equal(4.5f, book.BookPrice);
					Assert.Equal("Modern", book.BookAge);
					Assert.Equal("Fine", book.BookCondition);
					Assert.Equal("Me", book.BookOwner);
					Assert.Equal("Shelf", book.BookLocation);
					Assert.Equal("Owned", book.BookCollectionStatus);
					Assert.Equal("978-3", book.ISBN);
					Assert.Equal(44, book.PageCount);
					Assert.Equal("some\r\nnotes", book.BookNotes);
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task The_Pages_box_takes_digits_point_and_comma_only()
		{
			await Ui.Run(() =>
			{
				using (var f = new Fixture(fileless: true))
				{
					Select(f.Dialog, "tabCatalog");
					TextBox pages = f.Dialog.CatalogPage.FindControl<TextBox>("txPagesAsTextSimple");
					pages.Focus();
					pages.Text = string.Empty;
					pages.RaiseEvent(new TextInputEventArgs { Text = "x1", RoutedEvent = InputElement.TextInputEvent });
					Assert.Equal(string.Empty, pages.Text);
					pages.RaiseEvent(new TextInputEventArgs { Text = "12", RoutedEvent = InputElement.TextInputEvent });
					Assert.Equal("12", pages.Text);
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task The_date_boxes_show_the_dates_clear_with_Delete_and_keep_the_time_of_day()
		{
			await Ui.Run(() =>
			{
				using (var f = new Fixture(fileless: true, prepare: books =>
				{
					books[0].AddedTime = new DateTime(2024, 5, 6, 7, 8, 9);
					books[0].ReleasedTime = new DateTime(1995, 1, 2);
					return books;
				}))
				{
					Select(f.Dialog, "tabCatalog");
					InfoCatalogPage page = f.Dialog.CatalogPage;
					CalendarDatePicker added = page.FindControl<CalendarDatePicker>("dtpAddedTime"), released = page.FindControl<CalendarDatePicker>("dtpReleasedTime"), opened = page.FindControl<CalendarDatePicker>("dtpOpenedTime");
					Assert.Equal(CalendarDatePickerFormat.Long, added.SelectedDateFormat);
					Assert.Equal(new DateTime(2024, 5, 6, 7, 8, 9).Date, added.SelectedDate.Value.Date);
					Assert.Equal(new DateTime(1995, 1, 2), released.SelectedDate);
					Assert.Null(opened.SelectedDate); // not set: shows nothing
					Assert.Equal(" ", opened.PlaceholderText); // CustomFormat " "
					// another day keeps the time of day of the book
					added.SelectedDate = new DateTime(2024, 6, 1);
					Dpi.Settle(f.Dialog); // the box reports the new day a moment later
					Assert.Equal(new DateTime(2024, 6, 1, 7, 8, 9), f.Model.AddedTime);
					// Delete clears the box
					released.Focus();
					released.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Delete, PhysicalKey = PhysicalKey.Delete });
					Dpi.Settle(f.Dialog);
					Assert.Null(released.SelectedDate);
					Assert.Equal(DateTime.MinValue, f.Model.ReleasedTime);
					// choosing a day in a box that shows nothing starts at midnight
					opened.SelectedDate = new DateTime(2023, 3, 3);
					Dpi.Settle(f.Dialog);
					Assert.Equal(new DateTime(2023, 3, 3), f.Model.OpenedTime);
					Click(f.Dialog.OkButton);
					Assert.Equal(new DateTime(2024, 6, 1, 7, 8, 9), f.Books[0].AddedTime);
					Assert.Equal(DateTime.MinValue, f.Books[0].ReleasedTime);
					Assert.Equal(new DateTime(2023, 3, 3), f.Books[0].OpenedTime);
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public void Choosing_a_day_keeps_the_time_of_day_of_the_box()
		{
			Assert.Equal(new DateTime(2020, 1, 2, 3, 4, 5), InfoCatalogPage.Pick(new DateTime(2020, 1, 2), new DateTime(1999, 9, 9, 3, 4, 5)));
			Assert.Equal(new DateTime(2020, 1, 2), InfoCatalogPage.Pick(new DateTime(2020, 1, 2), DateTime.MinValue));
			Assert.Equal(DateTime.MinValue, InfoCatalogPage.Pick(null, new DateTime(1999, 9, 9)));
		}

		[Fact]
		public async Task The_Catalog_boxes_offer_the_values_the_library_uses()
		{
			await Ui.Run(() =>
			{
				using (var f = new Fixture(fileless: true, prepare: books =>
				{
					books[0].BookStore = "Shop";
					books[1].BookStore = "Market";
					books[2].BookStore = "Shop";
					books[0].BookCollectionStatus = "Owned";
					books[1].BookCollectionStatus = "Wanted";
					return books;
				}))
				{
					Select(f.Dialog, "tabCatalog");
					InfoCatalogPage page = f.Dialog.CatalogPage;
					CrValueComboBox store = page.FindControl<CrValueComboBox>("cbBookStore");
					Assert.True(store.IsEditable);
					page.EnsureList(store);
					Assert.Equal(new[] { string.Empty, "Market", "Shop" }, ((IEnumerable<ValueItem>)store.ItemsSource).Select(i => i.Text).ToArray());
					AutoCompleteBox status = page.FindControl<AutoCompleteBox>("txCollectionStatus");
					page.EnsureList(status);
					Assert.Equal(new[] { "Owned", "Wanted" }, ((IEnumerable<string>)status.ItemsSource).OrderBy(s => s));
					Assert.All(new[] { "cbBookStore", "cbBookPrice", "cbBookAge", "cbBookCondition", "cbBookOwner", "cbBookLocation", "txCollectionStatus" }, name => Assert.Contains(page.FindControl<Control>(name), page.ListBoxes.Keys));
				}
				return Task.CompletedTask;
			});
		}

		// ---- frames

		[Fact]
		public async Task The_three_tabs_draw_their_frames()
		{
			await Ui.Run(() =>
			{
				using (var f = new Fixture(fileless: true, prepare: books =>
				{
					books[0].Summary = "A plot summary that is long enough to wrap around the box of the Summary tab and show the text.";
					books[0].Characters = "Batman, Robin, Alfred";
					books[0].Teams = "Justice League";
					books[0].BookStore = "Forbidden Planet";
					books[0].AddedTime = new DateTime(2024, 5, 6);
					books[0].SetCustomValue("Grade", "9.8");
					return books;
				}))
				{
					foreach (string tab in new[] { "tabPlot", "tabCatalog", "tabCustom" })
					{
						Select(f.Dialog, tab);
						Raster frame = Dpi.Capture(f.Dialog, "info-" + tab);
						Assert.True(frame.Width >= 593 && frame.Height >= 517);
					}
				}
				return Task.CompletedTask;
			});
		}

		// ---- Custom

		[Fact]
		public async Task The_Custom_tab_has_the_grid_at_the_position_of_the_Designer_with_the_translated_headers()
		{
			await Ui.Run(() =>
			{
				using (var f = new Fixture(prepare: books =>
				{
					books[0].SetCustomValue("Alpha", "1");
					books[1].SetCustomValue("Beta", "2");
					return books;
				}))
				{
					Select(f.Dialog, "tabCustom");
					InfoCustomPage page = f.Dialog.CustomPage;
					double drop = 442 - page.Bounds.Height;
					AssertAt(page, "customValuesData", 11, 16, 537, 410 - drop);
					Assert.Equal(new[] { "Name", "Value" }, page.Headers.Select(h => h.Text));
					// one row for each key and the empty row for a new one
					Assert.Equal(3, page.Cells.Count);
					Assert.Equal(new[] { "Alpha", "Beta", string.Empty }, page.Cells.Select(c => c.Name.Text));
					Assert.Equal(new[] { "1", string.Empty, string.Empty }, page.Cells.Select(c => c.Value.Text));
					Assert.True(page.NameWidth > 90 && page.NameWidth < 110); // 100 until the user drags the splitter
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task Typing_in_the_last_row_adds_a_new_row_and_OK_writes_the_values_Previous_shows_the_next_book()
		{
			await Ui.Run(() =>
			{
				using (var f = new Fixture(prepare: books =>
				{
					books[0].SetCustomValue("Alpha", "1");
					books[1].SetCustomValue("Alpha", "one");
					return books;
				}))
				{
					Select(f.Dialog, "tabCustom");
					InfoCustomPage page = f.Dialog.CustomPage;
					(TextBox Name, AutoCompleteBox Value) alpha = page.Cells[0];
					alpha.Value.Text = "uno";
					Assert.Equal(2, page.Cells.Count);
					(TextBox Name, AutoCompleteBox Value) fresh = page.Cells[1];
					fresh.Name.Text = "Shelf";
					Assert.Equal(3, page.Cells.Count); // the grid's new row moves down
					page.Cells[1].Value.Text = "A3";
					Click(f.Dialog.NextButton);
					Assert.Equal("uno", f.Books[0].GetCustomValue("Alpha"));
					Assert.Equal("A3", f.Books[0].GetCustomValue("Shelf"));
					Assert.Equal(new[] { "Alpha", "Shelf", string.Empty }, page.Cells.Select(c => c.Name.Text));
					Assert.Equal(new[] { "one", string.Empty, string.Empty }, page.Cells.Select(c => c.Value.Text));
					Click(f.Dialog.OkButton);
					Assert.Null(f.Books[1].GetCustomValue("Shelf"));
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task Script_values_are_not_listed_on_the_Custom_tab_but_are_written_back()
		{
			await Ui.Run(() =>
			{
				using (var f = new Fixture(prepare: books =>
				{
					books[0].SetCustomValue("Plain", "p");
					books[0].SetCustomValue("script.Key", "s");
					return books;
				}))
				{
					Select(f.Dialog, "tabCustom");
					Assert.Equal(new[] { "Plain", string.Empty }, f.Dialog.CustomPage.Cells.Select(c => c.Name.Text));
					Click(f.Dialog.OkButton);
					Assert.Equal("s", f.Books[0].GetCustomValue("script.Key"));
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task The_value_cell_offers_the_values_other_books_have_under_the_key()
		{
			await Ui.Run(() =>
			{
				using (var f = new Fixture(prepare: books =>
				{
					books[0].SetCustomValue("Grade", "9.8");
					books[1].SetCustomValue("Grade", "9.4");
					return books;
				}))
				{
					Select(f.Dialog, "tabCustom");
					AutoCompleteBox value = f.Dialog.CustomPage.Cells[0].Value;
					value.Focus();
					Assert.Equal(new[] { "9.4", "9.8" }, ((IEnumerable<string>)value.ItemsSource).OrderBy(s => s));
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task The_Custom_tab_is_disabled_for_a_book_that_may_not_be_updated()
		{
			await Ui.Run(() =>
			{
				using (var f = new Fixture(fileless: true, preferences: new AppPreferences { ShowCustomBookFields = true }))
				{
					Assert.True(f.Dialog.CustomPage.IsEnabled);
				}
				return Task.CompletedTask;
			});
		}
	}
}
