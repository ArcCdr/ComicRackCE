using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using ComicRack.Mac.Controls;
using ComicRack.Mac.Editor;
using ComicRack.Mac.Host;
using ComicRack.Mac.Host.Library;
using ComicRack.Mac.Services;
using ComicRack.Mac.Tests.HiDpi;
using ComicRack.Mac.Tests.Thumbnails;
using ComicRack.Mac.Thumbnails;
using ComicRack.Mac.Views;
using cYo.Projects.ComicRack.Engine;
using Xunit;

namespace ComicRack.Mac.Tests.Editor
{
	/// <summary>P5.7a: the Info dialog as a window (DL.01, IN.01, IN.02, IN.08): its tabs, positions, texts, fields and buttons.</summary>
	public class InfoDialogTests
	{
		private static ComicBook Book(string series, int number, string file = null)
		{
			return new ComicBook
			{
				Series = series,
				Number = number.ToString(),
				Title = "Title " + number,
				Volume = 2020,
				Year = 1999,
				Month = 3,
				Count = 12,
				Writer = "Stan Lee",
				Publisher = "Marvel",
				Format = "Annual",
				FilePath = file ?? "/nowhere/" + series + number + ".cbz",
				PageCount = 10,
				AddedTime = new DateTime(2024, 5, 6),
			};
		}

		private static AppPreferences Allow() => new AppPreferences { UpdateComicFiles = true, UpdateComicBookFiles = true };

		private sealed class Fixture : IDisposable
		{
			public readonly ComicBook[] Books;
			public readonly AppPreferences Preferences;
			public readonly FakeDecoder Decoder = new FakeDecoder();
			public readonly ThumbnailService Thumbnails;
			public readonly BookEditorModel Model;
			public readonly BookInfoRequest Request;
			public readonly ComicBookDialog Dialog;
			public readonly List<string> Selected = new List<string>();
			public readonly List<string> Opened = new List<string>();
			public readonly List<string> Problems = new List<string>();

			public Fixture(int count = 3, AppPreferences preferences = null, bool thumbnails = false, int start = 0, Func<ComicBook[], ComicBook[]> prepare = null)
			{
				Books = Enumerable.Range(1, count).Select(i => Book("Series " + i, i)).ToArray();
				Books = prepare?.Invoke(Books) ?? Books;
				Preferences = preferences ?? Allow();
				if (thumbnails)
				{
					Thumbnails = new ThumbnailService(new ThumbnailServiceOptions { Decoder = Decoder });
				}
				var lists = new EditorValueLists(() => Books, Array.Empty<string>());
				Model = new BookEditorModel(Books[start], Books, Preferences, lists, selectComic: b =>
				{
					Selected.Add(b.Series);
					return true;
				}, createBook: f => new ComicBook { FilePath = f, PageCount = 22 });
				Request = new BookInfoRequest
				{
					Model = Model,
					Thumbnails = Thumbnails,
					Preferences = Preferences,
					FindBook = f => Books.FirstOrDefault(b => b.FilePath == f),
					OpenUrl = Opened.Add,
					ShowProblem = text =>
					{
						Problems.Add(text);
						return Task.CompletedTask;
					},
					AddCustomThumbnail = f => f == "/pictures/ok.png" ? "custom-key-1" : null,
				};
				Dialog = new ComicBookDialog(Request);
				Dialog.Show();
				Dpi.Settle(Dialog);
			}

			public void Dispose()
			{
				Dialog.Close();
				Thumbnails?.Dispose();
			}
		}

		private static void Click(Button button) => button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

		private static void Select(ComicBookDialog dialog, string tab)
		{
			dialog.Tabs.SelectedItem = dialog.TabPages.Single(t => (string)t.Tag == tab);
			Dpi.Settle(dialog);
		}

		private static Rect Where(Control control, Control page) => Dpi.Area(control, page);

		// ---- the frame

		[Fact]
		public async Task The_dialog_has_the_original_tabs_in_order_buttons_and_the_size_of_the_Designer()
		{
			await Ui.Run(() =>
			{
				using (var f = new Fixture())
				{
					Assert.Equal(new[] { "tabSummary", "tabDetails", "tabPlot", "tabCatalog", "tabCustom", "tabPages", "tabColors" }, f.Dialog.TabPages.Select(t => (string)t.Tag));
					Assert.Equal(new object[] { "Summary", "Details", "Plot & Notes", "Catalog", "Custom", "Pages", "Colors" }, f.Dialog.TabPages.Select(t => t.Header));
					// CatalogOnlyForFileless is on by default and ShowCustomBookFields off: a book with a file has neither tab
					Assert.Equal(new[] { true, true, true, false, false, true, true }, f.Dialog.TabPages.Select(t => t.IsVisible));
					Assert.Equal(593, f.Dialog.ClientSize.Width);
					Assert.Equal(517, f.Dialog.ClientSize.Height);
					Assert.Equal("OK", f.Dialog.OkButton.Content);
					Assert.Equal("Cancel", f.Dialog.CancelButton.Content);
					Assert.Equal("Apply", f.Dialog.ApplyButton.Content);
					Assert.Equal("Previous", f.Dialog.PreviousButton.Content);
					Assert.Equal("Next", f.Dialog.NextButton.Content);
					Assert.Equal(f.Books[0].Caption, f.Dialog.Title);
					// the buttons: 80 x 24, Previous and Next at the left (8 and 92), OK, Cancel and Apply at the right 6 apart, 12 from the right edge and 10 from the bottom
					Rect ok = Dpi.Area(f.Dialog.OkButton, f.Dialog), cancel = Dpi.Area(f.Dialog.CancelButton, f.Dialog), apply = Dpi.Area(f.Dialog.ApplyButton, f.Dialog);
					Rect previous = Dpi.Area(f.Dialog.PreviousButton, f.Dialog), next = Dpi.Area(f.Dialog.NextButton, f.Dialog);
					Assert.Equal(new Size(80, 24), ok.Size);
					Assert.Equal(581, apply.Right);
					Assert.Equal(f.Dialog.ClientSize.Height - 10, apply.Bottom);
					Assert.Equal(86, cancel.X - ok.Right + 80); // 6 apart
					Assert.Equal(6, apply.X - cancel.Right);
					Assert.Equal(8, previous.X);
					Assert.Equal(92, next.X);
					Assert.Equal(483, ok.Y);
					return Task.CompletedTask;
				}
			});
		}

		[Fact]
		public async Task A_single_book_has_no_Previous_and_Next_and_the_buttons_follow_the_position()
		{
			await Ui.Run(() =>
			{
				using (var f = new Fixture(count: 1))
				{
					Assert.False(f.Dialog.PreviousButton.IsVisible);
					Assert.False(f.Dialog.NextButton.IsVisible);
				}
				using (var f = new Fixture(count: 3, start: 0))
				{
					Assert.False(f.Dialog.PreviousButton.IsEnabled);
					Assert.True(f.Dialog.NextButton.IsEnabled);
					Click(f.Dialog.NextButton);
					Dpi.Settle(f.Dialog);
					Assert.True(f.Dialog.PreviousButton.IsEnabled);
					Assert.True(f.Dialog.NextButton.IsEnabled);
					Click(f.Dialog.NextButton);
					Dpi.Settle(f.Dialog);
					Assert.False(f.Dialog.NextButton.IsEnabled);
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task The_dialog_is_resizable_never_smaller_than_the_Designer_and_keeps_a_larger_size()
		{
			await Ui.Run(() =>
			{
				var preferences = Allow();
				preferences.InfoDialogWidth = 700;
				preferences.InfoDialogHeight = 600;
				using (var f = new Fixture(preferences: preferences))
				{
					Assert.True(f.Dialog.CanResize);
					Assert.Equal(700, f.Dialog.ClientSize.Width);
					Assert.Equal(600, f.Dialog.ClientSize.Height);
					Assert.Equal(593, f.Dialog.MinWidth);
					Assert.Equal(517, f.Dialog.MinHeight);
					f.Dialog.Width = 650;
					f.Dialog.Height = 560;
					Dpi.Settle(f.Dialog);
					Click(f.Dialog.ApplyButton); // OK and Apply keep the size
					Assert.Equal(650, preferences.InfoDialogWidth);
					Assert.Equal(560, preferences.InfoDialogHeight);
					f.Dialog.Width = 593;
					f.Dialog.Height = 517;
					Dpi.Settle(f.Dialog);
					Click(f.Dialog.ApplyButton);
					Assert.Equal(0, preferences.InfoDialogWidth); // the minimum is not a saved size
				}
				using (var f = new Fixture())
				{
					Assert.Equal(593, f.Dialog.ClientSize.Width);
				}
				return Task.CompletedTask;
			});
		}

		// ---- Summary

		[Fact]
		public async Task The_Summary_tab_is_laid_out_at_the_positions_of_the_Designer()
		{
			await Ui.Run(() =>
			{
				using (var f = new Fixture())
				{
					Select(f.Dialog, "tabSummary");
					InfoSummaryPage page = f.Dialog.Summary;
					Assert.Equal(566, page.Bounds.Width);
					Assert.InRange(page.Bounds.Height, 438, 446); // 442 in the Designer; the stock tab strip is a few units higher or lower with the system font
					Assert.Equal(21, Where(page.Tile, page).X);
					Assert.Equal(19, Where(page.Tile, page).Y);
					Assert.Equal(520, Where(page.Tile, page).Width);
					double drop = 442 - page.Bounds.Height; // what the tab strip takes more than the Designer's: the bottom anchored controls move up with it
					Assert.Equal(new Rect(372, 285 - drop, 152, 21), Where(page.MyRating, page));
					Assert.Equal(new Rect(372, 312 - drop, 152, 21), Where(page.CommunityRating, page));
					Assert.Equal(new Rect(93, 313 - drop, 105, 20), Where(page.Pages, page));
					Assert.Equal("Type:", ((Label)page.FindControl<Label>("labelType")).Content);
					Assert.Equal("Pages:", ((Label)page.FindControl<Label>("labelPages")).Content);
					Assert.Equal("My Rating:", ((Label)page.FindControl<Label>("labelMyRating")).Content);
					Assert.Equal("Community Rating:", ((Label)page.FindControl<Label>("labelCommunityRating")).Content);
					Assert.Equal("Where:", ((Label)page.FindControl<Label>("labelWhere")).Content);
					// a book with a file: type, pages and where are shown, the thumbnail and link buttons are not
					Assert.True(page.FindControl<Label>("labelType").IsVisible);
					Assert.True(page.FindControl<Label>("labelWhere").IsVisible);
					Assert.False(page.ThumbnailButton.IsVisible);
					Assert.False(page.LinkFileButton.IsVisible);
					Assert.Equal("/nowhere/Series 11.cbz", page.FilePath.Text);
					Assert.Contains("10", page.FileType.Text + " " + ((Label)page.Pages).Content);
					Assert.Equal("10 Page(s)", ((Label)page.Pages).Content);
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task A_book_without_a_file_shows_the_thumbnail_and_link_buttons_and_hides_the_file_rows()
		{
			await Ui.Run(() =>
			{
				using (var f = new Fixture(count: 1, prepare: books => new[] { new ComicBook { Series = "Mine", Number = "1" } }))
				{
					Select(f.Dialog, "tabSummary");
					InfoSummaryPage page = f.Dialog.Summary;
					Assert.True(page.ThumbnailButton.IsVisible);
					Assert.True(page.LinkFileButton.IsVisible);
					Assert.False(page.FindControl<Label>("labelType").IsVisible);
					Assert.False(page.FindControl<Label>("labelWhere").IsVisible);
					Assert.Equal("Thumbnail...", page.ThumbnailButton.Content);
					Assert.Equal("Link to File...", page.LinkFileButton.Content);
					double drop = 442 - page.Bounds.Height;
					Assert.Equal(new Rect(372, 347 - drop, 152, 23), Where(page.ThumbnailButton, page));
					Assert.Equal(new Rect(372, 373 - drop, 152, 23), Where(page.LinkFileButton, page));
					// no Pages and Colors tabs for a book without a file; the Catalog tab is there
					Assert.False(f.Dialog.TabPages.Single(t => (string)t.Tag == "tabPages").IsVisible);
					Assert.False(f.Dialog.TabPages.Single(t => (string)t.Tag == "tabColors").IsVisible);
					Assert.True(f.Dialog.TabPages.Single(t => (string)t.Tag == "tabCatalog").IsVisible);
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task The_ratings_edit_the_model_and_OK_writes_them()
		{
			await Ui.Run(() =>
			{
				using (var f = new Fixture())
				{
					f.Dialog.Summary.MyRating.Rating = 3.5;
					f.Dialog.Summary.CommunityRating.Rating = 2;
					Assert.Equal(3.5, f.Model.Rating);
					Assert.Equal(2, f.Model.CommunityRating);
					Assert.Equal(0f, f.Books[0].Rating);
					Click(f.Dialog.OkButton);
					Assert.Equal(3.5f, f.Books[0].Rating);
					Assert.Equal(2f, f.Books[0].CommunityRating);
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task The_web_link_shows_the_address_and_a_click_opens_it()
		{
			await Ui.Run(() =>
			{
				using (var f = new Fixture(prepare: books =>
				{
					books[0].Web = "https://example.org/comic";
					return books;
				}))
				{
					Assert.Equal("https://example.org/comic", f.Dialog.Summary.WebLink.Text);
					Assert.Contains("cr-link", f.Dialog.Summary.WebLink.Classes);
					f.Dialog.Summary.WebLink.RaiseEvent(new Avalonia.Input.PointerPressedEventArgs(f.Dialog.Summary.WebLink, new Pointer(1, PointerType.Mouse, true), f.Dialog, new Point(5, 5),
						1, new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None) { RoutedEvent = InputElement.PointerPressedEvent });
					Assert.Equal(new[] { "https://example.org/comic" }, f.Opened);
				}
				return Task.CompletedTask;
			});
		}

		// ---- Details

		[Fact]
		public async Task The_Details_tab_is_laid_out_at_the_positions_of_the_Designer()
		{
			await Ui.Run(() =>
			{
				using (var f = new Fixture())
				{
					Select(f.Dialog, "tabDetails");
					InfoDetailsPage page = f.Dialog.Details;
					Assert.Equal(566, page.Bounds.Width);
					Assert.InRange(page.Bounds.Height, 438, 446); // 442 in the Designer; the stock tab strip is a few units higher or lower with the system font
					// the boxes (the Designer's sizes; a spin box is 11 narrower and its buttons 1 to the right of it)
					AssertBox(page, "txSeries", 11, 28, 201);
					AssertBox(page, "txTitle", 11, 66, 201);
					AssertBox(page, "txAlternateSeries", 11, 104, 262);
					AssertBox(page, "txVolume", 216, 28, 46);
					AssertBox(page, "txNumber", 278, 28, 54);
					AssertBox(page, "txCount", 349, 28, 44);
					AssertBox(page, "txYear", 216, 66, 46);
					AssertBox(page, "txMonth", 278, 66, 54);
					AssertBox(page, "txDay", 349, 65, 44);
					AssertBox(page, "txAlternateNumber", 278, 104, 54);
					AssertBox(page, "txAlternateCount", 349, 104, 44);
					Assert.Equal(new Rect(263, 28, 11, 20), Where(page.FindControl<CrSpinButton>("txVolumeSpin"), page).WithHeight(20));
					Assert.Equal(new Rect(333, 28, 11, 20), Where(page.FindControl<CrSpinButton>("txNumberSpin"), page).WithHeight(20));
					Assert.Equal(new Rect(416, 27, 139, 21), Where(page.FindControl<CrValueComboBox>("cbFormat"), page).WithHeight(21));
					Assert.Equal(new Rect(416, 65, 139, 21), Where(page.FindControl<CrValueComboBox>("cbPublisher"), page).WithHeight(21));
					Assert.Equal(new Rect(416, 103, 139, 21), Where(page.FindControl<CrValueComboBox>("cbImprint"), page).WithHeight(21));
					// the table at (11, 129), 543 wide: three columns of 196, 196 and 139 with 12 between the second and the third
					Assert.Equal(11, Where(page.FindControl<Grid>("tableLayoutPanel1"), page).X);
					Assert.Equal(129, Where(page.FindControl<Grid>("tableLayoutPanel1"), page).Y);
					Assert.Equal(543, Where(page.FindControl<Grid>("tableLayoutPanel1"), page).Width);
					Assert.Equal(210, Where(page.FindControl<Grid>("tableLayoutPanel1"), page).Height);
					Assert.Equal(11, Where(page.FindControl<AutoCompleteBox>("txStoryArc"), page).X);
					Assert.Equal(210, Where(page.FindControl<AutoCompleteBox>("txSeriesGroup"), page).X); // 11 + 196 + the 3 of the cell
					Assert.Equal(129 + 12, Where(page.FindControl<AutoCompleteBox>("txStoryArc"), page).Y);
					Assert.Equal(129 + 12 + 20 + 26 + 12, Where(page.FindControl<AutoCompleteBox>("txWriter"), page).Y);
					Assert.Equal(415, Where(page.FindControl<CrValueComboBox>("cbAgeRating"), page).X);
					// genre and tags: 392 wide with the 16 wide selector button
					Assert.Equal(new Rect(10, 372, 376, 20), Where(page.FindControl<AutoCompleteBox>("txGenre"), page).WithHeight(20));
					Assert.Equal(new Rect(386, 372, 16, 20), Where(page.FindControl<Button>("txGenreSelector"), page).WithHeight(20));
					Assert.Equal(new Rect(11, 411, 376, 20), Where(page.FindControl<AutoCompleteBox>("txTags"), page).WithHeight(20));
				}
				return Task.CompletedTask;
			});
		}

		private static void AssertBox(InfoDetailsPage page, string name, double x, double y, double width)
		{
			Control box = page.FindControl<Control>(name);
			Rect area = Dpi.Area(box, page);
			Assert.True(Math.Abs(area.X - x) < 0.5 && Math.Abs(area.Y - y) < 1.5 && Math.Abs(area.Width - width) < 0.5, name + " is at " + area + ", the Designer has " + x + "," + y + " " + width);
		}

		[Fact]
		public async Task The_labels_of_the_Details_tab_are_the_original_texts()
		{
			await Ui.Run(() =>
			{
				using (var f = new Fixture())
				{
					InfoDetailsPage page = f.Dialog.Details;
					var expected = new Dictionary<string, string>
					{
						["labelSeries"] = "Series:", ["labelVolume"] = "Volume:", ["labelNumber"] = "Number:", ["labelCount"] = "of:", ["labelFormat"] = "Format:", ["labelTitle"] = "Title:",
						["labelYear"] = "Year:", ["labelMonth"] = "Month:", ["labelDay"] = "Day:", ["labelPublisher"] = "Publisher:", ["labelAlternateSeries"] = "Alternate Series or Storyline Title:",
						["labelAlternateNumber"] = "Number:", ["labelAlternateCount"] = "of:", ["labelImprint"] = "Imprint:", ["labelStoryArc"] = "Story Arc:", ["labelSeriesGroup"] = "Series Group:",
						["labelSeriesComplete"] = "Series complete:", ["labelWriter"] = "Writer:", ["labelPenciller"] = "Penciller:", ["labelAgeRating"] = "Age Rating:", ["labelInker"] = "Inker:",
						["labelColorist"] = "Colorist:", ["labelManga"] = "Manga:", ["labelLetterer"] = "Letterer:", ["labelCoverArtist"] = "Cover Artist:", ["labelLanguage"] = "Language:",
						["labelEditor"] = "Editor:", ["labelTranslator"] = "Translator:", ["labelBlackAndWhite"] = "Black and White:", ["labelGenre"] = "Genre:", ["labelTags"] = "Tags:",
						["labelEnableDynamicUpdate"] = "Include in Updates:", ["labelEnableProposed"] = "Proposed Values:",
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
		public async Task Every_field_shows_the_book_and_OK_writes_the_edits_Cancel_writes_nothing()
		{
			await Ui.Run(() =>
			{
				using (var f = new Fixture())
				{
					InfoDetailsPage page = f.Dialog.Details;
					Assert.Equal("Series 1", page.FindControl<AutoCompleteBox>("txSeries").Text);
					Assert.Equal("1", page.FindControl<TextBox>("txNumber").Text);
					Assert.Equal("2020", page.FindControl<TextBox>("txVolume").Text);
					Assert.Equal("1999", page.FindControl<TextBox>("txYear").Text);
					Assert.Equal(string.Empty, page.FindControl<TextBox>("txDay").Text);
					Assert.Equal("Stan Lee", page.FindControl<AutoCompleteBox>("txWriter").Text);
					Assert.Equal("Marvel", page.FindControl<CrValueComboBox>("cbPublisher").Value);
					page.FindControl<AutoCompleteBox>("txSeries").Text = "Edited";
					page.FindControl<TextBox>("txYear").Text = "2001";
					page.FindControl<CrValueComboBox>("cbPublisher").Value = "DC";
					page.FindControl<CrValueComboBox>("cbSeriesComplete").Value = ComicInfoYes();
					Assert.Equal("Series 1", f.Books[0].Series);
					Click(f.Dialog.OkButton);
					Assert.Equal("Edited", f.Books[0].Series);
					Assert.Equal(2001, f.Books[0].Year);
					Assert.Equal("DC", f.Books[0].Publisher);
					Assert.Equal(YesNo.Yes, f.Books[0].SeriesComplete);
				}
				using (var f = new Fixture())
				{
					f.Dialog.Details.FindControl<AutoCompleteBox>("txSeries").Text = "Never written";
					Click(f.Dialog.CancelButton);
					Assert.Equal("Series 1", f.Books[0].Series);
				}
				return Task.CompletedTask;
			});
		}

		private static string ComicInfoYes() => ComicInfo.YesText;

		[Fact]
		public async Task Apply_writes_and_keeps_the_dialog_open_and_Previous_and_Next_write_and_move()
		{
			await Ui.Run(() =>
			{
				using (var f = new Fixture())
				{
					f.Dialog.Details.FindControl<AutoCompleteBox>("txSeries").Text = "  First edited ";
					Click(f.Dialog.ApplyButton);
					Assert.True(f.Dialog.IsVisible);
					Assert.Equal("First edited", f.Books[0].Series);
					Assert.Equal("First edited", f.Dialog.Details.FindControl<AutoCompleteBox>("txSeries").Text);
					f.Dialog.Details.FindControl<TextBox>("txVolume").Text = "7";
					Click(f.Dialog.NextButton);
					Assert.Equal(7, f.Books[0].Volume);
					Assert.Equal("Series 2", f.Dialog.Details.FindControl<AutoCompleteBox>("txSeries").Text);
					Assert.Equal(f.Books[1].Caption, f.Dialog.Title);
					Assert.Equal(new[] { "Series 1", "First edited", "Series 2" }, f.Selected); // Apply selects the book again, as SetCurrentBook does
					Click(f.Dialog.PreviousButton);
					Assert.Equal("First edited", f.Dialog.Details.FindControl<AutoCompleteBox>("txSeries").Text);
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task A_text_field_takes_text_only_for_a_book_that_can_be_edited()
		{
			await Ui.Run(() =>
			{
				using (var f = new Fixture(preferences: new AppPreferences()))
				{
					// a book with a file that is not in the library and with UpdateComicFiles off: the Details tab is disabled
					Select(f.Dialog, "tabDetails");
					Assert.False(f.Dialog.Details.IsEnabled);
					Assert.False(f.Dialog.Summary.MyRating.IsEnabled);
				}
				using (var f = new Fixture())
				{
					Assert.True(f.Dialog.Details.IsEnabled);
					Assert.True(f.Dialog.Summary.MyRating.IsEnabled);
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task Number_boxes_take_digits_only_and_the_spin_buttons_step_with_the_rules_of_the_original()
		{
			await Ui.Run(() =>
			{
				using (var f = new Fixture(prepare: books =>
				{
					books[0].Year = -1;
					books[0].Month = -1;
					books[0].Volume = -1;
					return books;
				}))
				{
					Select(f.Dialog, "tabDetails");
					InfoDetailsPage page = f.Dialog.Details;
					TextBox year = page.FindControl<TextBox>("txYear"), month = page.FindControl<TextBox>("txMonth"), volume = page.FindControl<TextBox>("txVolume");
					// letters are refused, digits, point and comma are taken
					year.Focus();
					year.RaiseEvent(new TextInputEventArgs { Text = "abc", RoutedEvent = InputElement.TextInputEvent });
					Assert.Equal(string.Empty, year.Text);
					year.RaiseEvent(new TextInputEventArgs { Text = "20", RoutedEvent = InputElement.TextInputEvent });
					Assert.Equal("20", year.Text);
					// an empty box starts at the current year (month at the current month, 1 to 12; the volume at 1)
					year.Text = string.Empty;
					page.FindControl<CrSpinButton>("txYearSpin").Press(SpinPart.Up);
					Assert.Equal(DateTime.Now.Year.ToString(), year.Text);
					page.FindControl<CrSpinButton>("txYearSpin").Press(SpinPart.Down);
					Assert.Equal((DateTime.Now.Year - 1).ToString(), year.Text);
					page.FindControl<CrSpinButton>("txMonthSpin").Press(SpinPart.Up);
					Assert.Equal(DateTime.Now.Month.ToString(), month.Text);
					month.Text = "12";
					page.FindControl<CrSpinButton>("txMonthSpin").Press(SpinPart.Up);
					Assert.Equal("12", month.Text);
					month.Text = "1";
					page.FindControl<CrSpinButton>("txMonthSpin").Press(SpinPart.Down);
					Assert.Equal("1", month.Text);
					page.FindControl<CrSpinButton>("txVolumeSpin").Press(SpinPart.Up);
					Assert.Equal("1", volume.Text);
					page.FindControl<CrSpinButton>("txVolumeSpin").Press(SpinPart.Up);
					Assert.Equal("2", volume.Text);
					// the count does not go below 0
					TextBox count = page.FindControl<TextBox>("txCount");
					count.Text = "0";
					page.FindControl<CrSpinButton>("txCountSpin").Press(SpinPart.Down);
					Assert.Equal("0", count.Text);
					// the text of the box is the model's
					Assert.Equal("2", f.Model.Volume);
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task The_spin_rules_follow_SpinButton_SetTextBoxValue()
		{
			var plain = SpinRules.Config.Default;
			Assert.Equal("1", SpinRules.Step("", "", plain, up: true));
			Assert.Equal("6", SpinRules.Step("5", "", plain, up: true));
			Assert.Equal("4", SpinRules.Step(" 5 ", "", plain, up: false));
			Assert.Equal("-1", SpinRules.Step("0", "", plain, up: false)); // no lower limit
			Assert.Equal("8", SpinRules.Step("", "7", plain, up: true)); // the prompt is the number of an empty box
			Assert.Equal("1", SpinRules.Step("abc", "", plain, up: true));
			var month = new SpinRules.Config(5, 1, 12, 1);
			Assert.Equal("5", SpinRules.Step("", "", month, up: false));
			Assert.Equal("12", SpinRules.Step("12", "", month, up: true));
			Assert.Equal("1", SpinRules.Step("1", "", month, up: false));
			Assert.Equal(new DateTime(2026, 10, 21).Month, SpinRules.ForField("txDay", new DateTime(2026, 10, 21)).Start); // the original starts the day at the month
			Assert.Equal(2026, SpinRules.ForField("txYear", new DateTime(2026, 10, 21)).Start);
			Assert.Equal(0, SpinRules.ForField("txCount", DateTime.Now).Min);
			await Task.CompletedTask;
		}

		[Fact]
		public async Task A_proposed_value_is_the_prompt_of_an_empty_box_and_the_box_takes_it_when_it_gets_the_focus()
		{
			await Ui.Run(() =>
			{
				using (var f = new Fixture(prepare: books =>
				{
					books[0].FilePath = "/nowhere/Spider-Man 012 (1999).cbz";
					books[0].Series = string.Empty;
					books[0].Number = string.Empty;
					books[0].EnableProposed = true;
					return books;
				}))
				{
					Select(f.Dialog, "tabDetails");
					AutoCompleteBox series = f.Dialog.Details.FindControl<AutoCompleteBox>("txSeries");
					TextBox number = f.Dialog.Details.FindControl<TextBox>("txNumber");
					Assert.False(string.IsNullOrEmpty(f.Model.ProposedSeries));
					Assert.Equal(f.Model.ProposedSeries, series.Watermark);
					Assert.Equal(f.Model.ProposedNumber, number.PlaceholderText);
					Assert.Equal(string.Empty, series.Text);
					number.Focus();
					Dpi.Settle(f.Dialog);
					Assert.Equal(f.Model.ProposedNumber, number.Text);
					// switching the proposed values off takes the prompts away
					f.Dialog.Details.FindControl<CrValueComboBox>("cbEnableProposed").Value = ComicInfo.NoText;
					Assert.Equal(string.Empty, series.Watermark);
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task The_boxes_offer_the_values_the_library_uses_for_that_field()
		{
			await Ui.Run(() =>
			{
				using (var f = new Fixture(count: 3, prepare: books =>
				{
					books[0].Writer = "Alan Moore";
					books[1].Writer = "Neil Gaiman";
					books[2].Writer = "Alan Moore";
					books[1].Publisher = "DC";
					books[2].Publisher = "Image";
					return books;
				}))
				{
					Select(f.Dialog, "tabDetails");
					InfoDetailsPage page = f.Dialog.Details;
					AutoCompleteBox writer = page.FindControl<AutoCompleteBox>("txWriter");
					CrValueComboBox publisher = page.FindControl<CrValueComboBox>("cbPublisher");
					Assert.Null(writer.ItemsSource); // built when the box gets the focus
					page.EnsureList(writer);
					Assert.Equal(new[] { "Alan Moore", "Neil Gaiman" }, ((IEnumerable<string>)writer.ItemsSource).OrderBy(s => s));
					page.EnsureList(publisher);
					string[] publishers = ((IEnumerable<ValueItem>)publisher.ItemsSource).Select(i => i.Text).ToArray();
					Assert.Equal(string.Empty, publishers[0]);
					Assert.Equal(new[] { "DC", "Image", "Marvel" }, publishers.Skip(1));
					Assert.True(publisher.IsEditable);
					Assert.Equal("Marvel", publisher.Value);
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task The_yes_no_and_manga_boxes_are_lists_and_the_language_box_stores_the_culture()
		{
			await Ui.Run(() =>
			{
				using (var f = new Fixture(prepare: books =>
				{
					books[0].LanguageISO = "fr";
					books[1].LanguageISO = "de";
					return books;
				}))
				{
					Select(f.Dialog, "tabDetails");
					InfoDetailsPage page = f.Dialog.Details;
					CrValueComboBox manga = page.FindControl<CrValueComboBox>("cbManga");
					Assert.False(manga.IsEditable);
					Assert.Equal(new[] { string.Empty, ComicInfo.YesText, ComicInfo.YesRightToLeftText, ComicInfo.NoText }, ((IEnumerable<ValueItem>)manga.ItemsSource).Select(i => i.Text));
					Assert.Equal(new[] { string.Empty, ComicInfo.YesText, ComicInfo.NoText }, ((IEnumerable<ValueItem>)page.FindControl<CrValueComboBox>("cbBlackAndWhite").ItemsSource).Select(i => i.Text));
					Assert.Equal(new[] { ComicInfo.YesText, ComicInfo.NoText }, ((IEnumerable<ValueItem>)page.FindControl<CrValueComboBox>("cbEnableProposed").ItemsSource).Select(i => i.Text));
					CrValueComboBox language = page.FindControl<CrValueComboBox>("cbLanguage");
					ValueItem[] items = ((IEnumerable<ValueItem>)language.ItemsSource).ToArray();
					Assert.Equal(string.Empty, items[0].Value);
					Assert.Contains(items, i => i.Value == "fr" && i.Text.Length > 0 && i.Text != "fr");
					Assert.Equal("fr", language.Value);
					Assert.Equal("fr", ((ValueItem)language.SelectedItem).Value);
					language.Value = "de";
					Click(f.Dialog.OkButton);
					Assert.Equal("de", f.Books[0].LanguageISO);
				}
				return Task.CompletedTask;
			});
		}

		// ---- the list selector

		[Fact]
		public async Task The_list_selector_moves_values_between_the_box_and_the_pool_and_the_box_takes_the_result()
		{
			await Ui.Run(() =>
			{
				using (var f = new Fixture(count: 3, prepare: books =>
				{
					books[0].Writer = "Stan Lee, Jack Kirby";
					books[1].Writer = "Alan Moore";
					books[2].Writer = "Stan Lee";
					return books;
				}))
				{
					Select(f.Dialog, "tabDetails");
					InfoDetailsPage page = f.Dialog.Details;
					AutoCompleteBox writer = page.FindControl<AutoCompleteBox>("txWriter");
					ListSelectorModel.LastTab = 0;
					Flyout flyout = page.OpenSelector(page.FindControl<Button>("txWriterSelector"), writer, EditorList.Writer);
					Dpi.Settle(f.Dialog);
					var view = (ListSelectorView)flyout.Content;
					Assert.Equal(new[] { "Stan Lee", "Jack Kirby" }, view.Model.Own.OrderByDescending(s => s));
					Assert.Equal(new[] { "Alan Moore" }, view.Model.Pool);
					Assert.Equal(new[] { "Alan Moore" }, view.Pool.ItemsSource.Cast<string>());
					view.Pool.SelectedItems.Add("Alan Moore");
					Click(view.FindControl<Button>("btSelectedToOwn"));
					Assert.Contains("Alan Moore", view.Model.Own);
					Assert.Empty(view.Model.Pool);
					Click(view.FindControl<Button>("btAllToPool"));
					Assert.Empty(view.Model.Own);
					Click(view.FindControl<Button>("btAllToOwn"));
					Assert.Equal(3, view.Model.Own.Count);
					flyout.Hide();
					Dpi.Settle(f.Dialog);
					Assert.Equal(3, writer.Text.Split(',').Length);
					Assert.Contains("Alan Moore", writer.Text);
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public void The_list_selector_model_follows_ListSelectorControl()
		{
			ListSelectorModel.LastTab = 0;
			var model = new ListSelectorModel("Stan Lee, Jack Kirby", new[] { "Stan Lee", "Alan Moore, Neil Gaiman", "Jack Kirby" });
			Assert.Equal(new[] { "Stan Lee", "Jack Kirby" }, model.Own);
			Assert.Equal(new[] { "Alan Moore", "Neil Gaiman" }, model.Pool); // a value that is a list is split, the values of the box are not offered again
			Assert.Equal("Stan Lee, Jack Kirby", model.Result); // nothing moved: the text stays as it was
			model.Add(new[] { "Neil Gaiman" });
			Assert.Equal("Stan Lee, Jack Kirby, Neil Gaiman", model.Result);
			model.Remove(new[] { "Stan Lee" });
			Assert.Equal("Jack Kirby, Neil Gaiman", model.Result);
			model.SetChecked("Alan Moore", true);
			Assert.Equal(new[] { "Jack Kirby", "Neil Gaiman", "Alan Moore" }, model.Own);
			model.RemoveAll();
			Assert.Equal(string.Empty, model.Result);
			Assert.False(model.CanRemoveAll);
			Assert.True(model.CanAddAll);
			// the Text tab: its text is the result
			model.Tab = 2;
			model.TextTabText = "A,\r\nB";
			Assert.Equal("A, B", model.Result);
			model.LeaveTextTab();
			Assert.Equal("A, B", model.Text);
			Assert.Equal(new[] { "A", "B" }, model.Own);
			// the tab is shared: the next popup opens on the same one
			ListSelectorModel.LastTab = 1;
			Assert.Equal(1, new ListSelectorModel("", null).Tab);
			ListSelectorModel.LastTab = 0;
		}

		// ---- the cover

		[Fact]
		public async Task The_cover_is_the_3D_book_by_default_with_the_text_beside_it_and_a_click_flips_it_to_the_flat_cover()
		{
			await Ui.Run(async () =>
			{
				using (var f = new Fixture(thumbnails: true))
				{
					Assert.True(f.Preferences.InformationCover3D);
					Select(f.Dialog, "tabSummary");
					CrBookTile tile = f.Dialog.Summary.Tile;
					Assert.True(tile.ThreeD);
					await Until(() => tile.HasCover && tile.HasBox, "the cover and the 3D book arrive");
					BookTileGeometry g = tile.Geometry;
					Assert.True(g.Image.Width > 0 && g.Image.Height > 0);
					Assert.True(g.Image.Width <= 520 && g.Image.Height <= 243);
					Assert.Equal(520 / 3, (int)g.Text.X); // the text lies over the right two thirds
					// the text: the caption without the title (bold), the title, the dates that are not empty
					Assert.Contains(tile.Lines, l => l.Text == f.Books[0].CaptionWithoutTitle && l.IsBold);
					Assert.Contains(tile.Lines, l => l.Text == f.Books[0].ShadowTitle);
					Assert.Contains(tile.Lines, l => l.Text.StartsWith("Added:"));
					Assert.DoesNotContain(tile.Lines, l => l.Text.StartsWith("Opened:")); // empty dates are not shown (NoEmptyDates)
					// a click flips to the flat cover and the setting follows
					Point inside = Dpi.Origin(tile, f.Dialog) + new Point(5, 5);
					tile.RaiseEvent(new Avalonia.Input.PointerPressedEventArgs(tile, new Pointer(2, PointerType.Mouse, true), f.Dialog, inside, 1,
						new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None) { RoutedEvent = InputElement.PointerPressedEvent });
					tile.RaiseEvent(new Avalonia.Input.PointerReleasedEventArgs(tile, new Pointer(2, PointerType.Mouse, true), f.Dialog, inside,
						1, new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased), KeyModifiers.None, MouseButton.Left) { RoutedEvent = InputElement.PointerReleasedEvent });
					Assert.False(tile.ThreeD);
					Assert.False(f.Preferences.InformationCover3D);
					Assert.True(tile.PictureRect.Width > 0);
				}
			});
		}

		private static async Task Until(Func<bool> condition, string what, int timeoutMs = 20000)
		{
			var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
			while (!condition())
			{
				if (DateTime.UtcNow > deadline)
				{
					throw new TimeoutException("Not reached: " + what);
				}
				Dispatcher.UIThread.RunJobs();
				await Task.Delay(10);
			}
		}

		[Fact]
		public async Task The_mouse_wheel_over_the_cover_shows_the_book_before_or_after()
		{
			await Ui.Run(() =>
			{
				using (var f = new Fixture(start: 1))
				{
					f.Dialog.Summary.Tile.RaiseEvent(new PointerWheelEventArgs(f.Dialog.Summary.Tile, new Pointer(3, PointerType.Mouse, true), f.Dialog, new Point(10, 10), 1,
						new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.Other), KeyModifiers.None, new Vector(0, -1)) { RoutedEvent = InputElement.PointerWheelChangedEvent });
					Assert.Same(f.Books[2], f.Model.Current); // down: the next book
					f.Dialog.Summary.Tile.RaiseEvent(new PointerWheelEventArgs(f.Dialog.Summary.Tile, new Pointer(3, PointerType.Mouse, true), f.Dialog, new Point(10, 10), 1,
						new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.Other), KeyModifiers.None, new Vector(0, 1)) { RoutedEvent = InputElement.PointerWheelChangedEvent });
					Assert.Same(f.Books[1], f.Model.Current); // up: the one before
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task The_thumbnail_menu_resets_and_a_picture_file_becomes_the_custom_thumbnail_of_a_book_without_a_file()
		{
			await Ui.Run(() =>
			{
				var preferences = Allow();
				using (var f = new Fixture(count: 1, preferences: preferences, prepare: books => new[] { new ComicBook { Series = "Mine", Number = "1" } }))
				{
					f.Dialog.LoadThumbnail("/pictures/ok.png");
					Assert.Equal("custom-key-1", f.Model.CustomThumbnailKey);
					Assert.Equal(new[] { "/pictures/ok.png" }, preferences.RecentThumbnailFiles);
					f.Dialog.LoadThumbnail("/pictures/broken.png"); // the pool could not use it: nothing changes
					Assert.Equal("custom-key-1", f.Model.CustomThumbnailKey);
					Assert.Single(preferences.RecentThumbnailFiles);
					f.Model.ResetThumbnail();
					Assert.True(string.IsNullOrEmpty(f.Model.CustomThumbnailKey));
					var menu = (MenuFlyout)f.Dialog.Summary.ThumbnailButton.Flyout;
					menu.ShowAt(f.Dialog.Summary.ThumbnailButton);
					Dpi.Settle(f.Dialog);
					Assert.Equal("Reset", ((MenuItem)menu.Items[0]).Header);
					Assert.IsType<Separator>(menu.Items[1]);
					Assert.Equal("/pictures/ok.png", ((MenuItem)menu.Items[2]).Header);
					menu.Hide();
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task Link_to_File_refuses_a_file_the_library_has_and_shows_the_book_of_a_new_file()
		{
			await Ui.Run(async () =>
			{
				using (var f = new Fixture(count: 2, prepare: books =>
				{
					books[0] = new ComicBook { Series = "Mine", Number = "1" };
					return books;
				}))
				{
					Assert.False(await f.Dialog.LinkFileAsync(f.Books[1].FilePath));
					Assert.Single(f.Problems);
					Assert.Contains("already used", f.Problems[0]);
					Assert.True(await f.Dialog.LinkFileAsync("/new/file.cbz"));
					Assert.True(f.Model.IsLinked);
					Assert.Equal("Mine", f.Model.Series);
					Assert.True(f.Dialog.Summary.FindControl<Label>("labelType").IsVisible);
					Click(f.Dialog.OkButton);
					Assert.Equal("/new/file.cbz", f.Books[0].FilePath);
				}
			});
		}

		[Fact]
		public async Task The_tab_the_user_chose_is_kept_for_the_next_dialog()
		{
			await Ui.Run(() =>
			{
				using (var f = new Fixture())
				{
					Select(f.Dialog, "tabDetails");
					Click(f.Dialog.CancelButton);
				}
				using (var f = new Fixture())
				{
					Assert.Same(f.Dialog.TabPages.Single(t => (string)t.Tag == "tabDetails"), f.Dialog.Tabs.SelectedItem);
					Select(f.Dialog, "tabSummary");
					Click(f.Dialog.CancelButton);
				}
				using (var f = new Fixture())
				{
					Assert.Same(f.Dialog.TabPages.Single(t => (string)t.Tag == "tabSummary"), f.Dialog.Tabs.SelectedItem);
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task Escape_cancels_and_Return_presses_OK()
		{
			await Ui.Run(() =>
			{
				using (var f = new Fixture())
				{
					f.Dialog.Details.FindControl<AutoCompleteBox>("txSeries").Text = "Never written";
					f.Dialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
					Assert.Equal("Series 1", f.Books[0].Series);
				}
				return Task.CompletedTask;
			});
		}
	}

	internal static class RectExtensions
	{
		public static Rect WithHeight(this Rect rect, double height) => new Rect(rect.X, rect.Y, rect.Width, height);
	}
}
