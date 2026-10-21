using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using ComicRack.Mac.Controls;
using ComicRack.Mac.Editor;
using ComicRack.Mac.Host;
using ComicRack.Mac.Host.Library;
using ComicRack.Mac.Services;
using ComicRack.Mac.Tests.HiDpi;
using ComicRack.Mac.Themes;
using ComicRack.Mac.Views;
using cYo.Projects.ComicRack.Engine;
using Xunit;

namespace ComicRack.Mac.Tests.Editor
{
	/// <summary>P5.8: the multi edit dialog as a window (DL.02): the Designer's positions, the check boxes, the groups, the tab mode and what OK writes.</summary>
	public class MultiEditDialogTests
	{
		private sealed class Fixture : IDisposable
		{
			public readonly ComicBook[] Books;
			public readonly AppPreferences Preferences;
			public readonly MultiBookEditorModel Model;
			public readonly MultipleComicBooksDialog Dialog;

			public Fixture(int count = 3, AppPreferences preferences = null, Action<ComicBook[]> prepare = null, bool custom = false, bool fileless = false)
			{
				Books = Enumerable.Range(1, count).Select(i =>
				{
					var book = new ComicBook { Series = "Series " + i, Number = i.ToString(), Publisher = "Marvel", Writer = "Stan Lee, Writer " + i };
					if (!fileless)
					{
						book.FilePath = "/nowhere/" + i + ".cbz";
						book.PageCount = 10;
					}
					return book;
				}).ToArray();
				prepare?.Invoke(Books);
				Preferences = preferences ?? new AppPreferences { CatalogOnlyForFileless = false, ShowCustomBookFields = custom };
				var lists = new EditorValueLists(() => Books, Array.Empty<string>());
				Model = new MultiBookEditorModel(Books, Preferences, lists);
				Dialog = new MultipleComicBooksDialog(new MultipleBooksRequest { Model = Model, Preferences = Preferences });
				Dialog.Show();
				Dpi.Settle(Dialog);
			}

			public MultipleBooksBody Body => Dialog.BodyView;

			public void Dispose() => Dialog.Close();
		}

		private static string DesignerSource()
		{
			string dir = AppContext.BaseDirectory;
			while (dir != null && !File.Exists(Path.Combine(dir, "ComicRack", "Dialogs", "MultipleComicBooksDialog.Designer.cs")))
			{
				dir = Path.GetDirectoryName(dir);
			}
			Assert.True(dir != null, "The repository root was not found.");
			return File.ReadAllText(Path.Combine(dir, "ComicRack", "Dialogs", "MultipleComicBooksDialog.Designer.cs"));
		}

		private static (double X, double Y, double W, double H) DesignerRect(string source, string name)
		{
			Match location = Regex.Match(source, @"this\." + name + @"\.Location = new System\.Drawing\.Point\((\d+), (\d+)\);");
			Match size = Regex.Match(source, @"this\." + name + @"\.Size = new System\.Drawing\.Size\((\d+), (\d+)\);");
			Assert.True(location.Success && size.Success, "The Designer has no position for " + name + ".");
			return (double.Parse(location.Groups[1].Value), double.Parse(location.Groups[2].Value), double.Parse(size.Groups[1].Value), double.Parse(size.Groups[2].Value));
		}

		private static void Toggle(CheckBox box) => typeof(ToggleButton).GetMethod("Toggle", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(box, null);

		private static void Click(Button button) => button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

		private static Rect InGroup(Control control, MultipleBooksBody body)
		{
			Control panel = body.Groups.Select(g => (Control)g.Content).Single(p => p != null && p.GetVisualDescendants().Contains(control));
			return Dpi.Area(control, panel);
		}

		// ---- the frame

		[Fact]
		public async Task The_window_is_the_525_by_460_form_with_the_panel_at_12_12_and_OK_and_Cancel_at_the_bottom_right()
		{
			await Ui.Run(() =>
			{
				using (var f = new Fixture(3))
				{
					Assert.Equal("Multiple Book Information (3)", f.Dialog.Title);
					Assert.Equal(525, f.Dialog.ClientSize.Width, 0.5);
					Assert.Equal(460, f.Dialog.ClientSize.Height, 0.5);
					Assert.False(f.Dialog.CanResize);
					Rect panel = Dpi.Area(f.Body.PageData, f.Dialog);
					Assert.Equal(13, panel.X, 0.5); // the frame is one unit, pageData is inside it
					Assert.Equal(13, panel.Y, 0.5);
					Rect frame = Dpi.Area(f.Body.FindControl<Border>("pageDataFrame"), f.Dialog);
					Assert.Equal(new Rect(12, 12, 497, 407), new Rect(frame.X, frame.Y, frame.Width, frame.Height));
					Rect ok = Dpi.Area(f.Dialog.OkButton, f.Dialog), cancel = Dpi.Area(f.Dialog.CancelButton, f.Dialog);
					Assert.Equal(new Rect(346, 427, 77, 24), new Rect(Math.Round(ok.X), Math.Round(ok.Y), ok.Width, ok.Height));
					Assert.Equal(new Rect(429, 427, 80, 24), new Rect(Math.Round(cancel.X), Math.Round(cancel.Y), cancel.Width, cancel.Height));
					Assert.Equal("OK", f.Dialog.OkButton.Content);
					Assert.Equal("Cancel", f.Dialog.CancelButton.Content);
					Assert.True(f.Dialog.OkButton.IsDefault);
					Assert.True(f.Dialog.CancelButton.IsCancel);
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task The_groups_are_stacked_in_the_order_of_the_Designer_with_the_heights_of_the_Designer()
		{
			await Ui.Run(() =>
			{
				using (var f = new Fixture())
				{
					string[] names = { "grpMain", "grpArtists", "grpPlotNotes", "grpCatalog" };
					double[] heights = { 400, 204, 265, 392 };
					double top = 0;
					for (int i = 0; i < names.Length; i++)
					{
						CrCollapsibleGroup group = f.Body.Group(names[i]);
						Rect area = Dpi.Area(group, f.Body.PageData.Content as Visual);
						Assert.Equal(478, group.Bounds.Width, 0.5);
						Assert.Equal(heights[i], group.Bounds.Height, 0.5);
						Assert.Equal(top, area.Y, 0.5);
						top += heights[i];
					}
					Assert.False(f.Body.Group("grpCustom").IsVisible); // ShowCustomBookFields is off
					var headers = f.Body.Groups.Select(g => (string)g.Header).ToArray();
					Assert.Equal(new[] { "Main", "Artists / People Involved", "Plot & Notes", "Catalog", "Custom Fields" }, headers);
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task Every_box_its_check_box_selector_and_spin_button_is_where_the_original_puts_it()
		{
			string source = DesignerSource();
			var checkedNames = new HashSet<string>();
			await Ui.Run(() =>
			{
				// books with files show Scan Information, books without show Opened/Read and Pages: between them every box is on screen once
				foreach (bool fileless in new[] { true, false })
				{
					using (var f = new Fixture(fileless: fileless))
					{
						foreach (MultiField field in f.Model.Fields.Where(fl => f.Body.Box(fl.Name).IsVisible))
						{
							checkedNames.Add(field.Name);
							var (x, y, w, h) = DesignerRect(source, field.Name);
							Control box = f.Body.Box(field.Name);
							CheckBox check = f.Body.Check(field.Name);
							Assert.NotNull(check);
							bool selector = f.Body.FindControl<Control>(field.Name + "Selector") != null;
							bool spin = f.Body.FindControl<Control>(field.Name + "Spin") != null;
							double boxWidth = w - 18 - (selector ? 16 : 0) - (spin ? 11 : 0);
							Rect area = InGroup(box, f.Body), chk = InGroup(check, f.Body);
							// the Designer's position is measured from the top of the group, which has a 24 high header
							Assert.True(Math.Abs(area.X - (x + 18)) < 0.5 && Math.Abs(area.Y - (y - 24)) < 1 && Math.Abs(area.Width - boxWidth) < 0.5 && Math.Abs(area.Height - h) < 1.5,
								field.Name + " is at " + area + ", expected " + (x + 18) + "," + (y - 24) + " " + boxWidth + " x " + h);
							Assert.True(Math.Abs(chk.X - x) < 0.5 && Math.Abs(chk.Y - (y - 24 + (int)((h - 17) / 2))) < 1 && Math.Abs(chk.Width - 16) < 0.5 && Math.Abs(chk.Height - 17) < 0.5,
								field.Name + "'s check box is at " + chk + ", expected " + x + "," + (y - 24 + (int)((h - 17) / 2)) + " 16 x 17");
							if (selector)
							{
								Rect sel = InGroup(f.Body.FindControl<Control>(field.Name + "Selector"), f.Body);
								Assert.True(Math.Abs(sel.X - (x + w - 16)) < 0.5 && Math.Abs(sel.Width - 16) < 0.5 && Math.Abs(sel.Y - (y - 24)) < 1, field.Name + "'s selector button is at " + sel);
							}
							if (spin)
							{
								Rect up = InGroup(f.Body.FindControl<Control>(field.Name + "Spin"), f.Body);
								Assert.True(Math.Abs(up.X - (x + w - 10)) < 0.5 && Math.Abs(up.Width - 11) < 0.5 && Math.Abs(up.Y - (y - 24)) < 1, field.Name + "'s spin button is at " + up);
							}
						}
					}
				}
				return Task.CompletedTask;
			});
			Assert.Equal(56, checkedNames.Count);
		}

		[Fact]
		public async Task The_labels_stay_where_the_Designer_puts_them_and_are_the_original_captions()
		{
			string source = DesignerSource();
			await Ui.Run(() =>
			{
				using (var f = new Fixture())
				{
					foreach (string name in new[] { "labelSeries", "labelVolume", "labelWriter", "labelSummary", "labelBookStore", "labelISBN", "labelRating", "labelCommunityRating" })
					{
						var (x, y, _, _) = DesignerRect(source, name);
						Label label = f.Body.FindControl<Label>(name);
						Assert.NotNull(label);
						Rect area = InGroup(label, f.Body);
						Assert.True(Math.Abs(area.X - x) < 0.5 && Math.Abs(area.Y - (y - 24)) < 1, name + " is at " + area + ", expected " + x + "," + (y - 24));
						Assert.Contains("cr-caption", label.Classes);
					}
					Assert.Equal("Series:", f.Body.FindControl<Label>("labelSeries").Content);
					Assert.Equal("Alternate Series or Storyline Title:", f.Body.FindControl<Label>("labelAlternateSeries").Content);
					Assert.Equal("Main Character/Team:", f.Body.FindControl<Label>("labelMainCharacterOrTeam").Content);
					Assert.Equal("Notes about this Book:", f.Body.FindControl<Label>("labelBookNotes").Content);
				}
				return Task.CompletedTask;
			});
		}

		// ---- the check boxes

		[Fact]
		public async Task A_box_with_a_value_in_common_is_checked_and_typing_into_an_empty_box_checks_it()
		{
			await Ui.Run(() =>
			{
				using (var f = new Fixture())
				{
					Assert.Equal("Marvel", f.Body.FindControl<CrValueComboBox>("cbPublisher").Value);
					Assert.True(f.Body.Check("cbPublisher").IsChecked);
					AutoCompleteBox series = f.Body.FindControl<AutoCompleteBox>("txSeries");
					Assert.Equal(string.Empty, series.Text);
					Assert.False(f.Body.Check("txSeries").IsChecked);
					series.Text = "New Series";
					Dpi.Settle(f.Dialog);
					Assert.True(f.Body.Check("txSeries").IsChecked);
					Assert.Equal(MultiCheck.Checked, f.Model["txSeries"].Check);
					Toggle(f.Body.Check("txSeries")); // a click unchecks it and leaves the text
					Assert.False(f.Body.Check("txSeries").IsChecked);
					Assert.Equal(MultiCheck.Unchecked, f.Model["txSeries"].Check);
					Assert.Equal("New Series", series.Text);
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task A_list_box_whose_books_differ_has_an_indeterminate_check_box_and_a_click_empties_the_box()
		{
			await Ui.Run(() =>
			{
				using (var f = new Fixture())
				{
					CheckBox check = f.Body.Check("txWriter");
					Assert.Null(check.IsChecked);
					Assert.Equal("Stan Lee", f.Body.FindControl<AutoCompleteBox>("txWriter").Text);
					Toggle(check);
					Assert.False(check.IsChecked);
					Assert.Equal(MultiCheck.Unchecked, f.Model["txWriter"].Check);
					Assert.Equal(string.Empty, f.Body.FindControl<AutoCompleteBox>("txWriter").Text);
					Toggle(check);
					Assert.True(check.IsChecked);
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task A_real_click_on_the_check_box_toggles_it()
		{
			await Ui.Run(() =>
			{
				using (var f = new Fixture())
				{
					CheckBox check = f.Body.Check("cbPublisher");
					Assert.True(check.IsChecked);
					Rect area = Dpi.Area(check, f.Dialog);
					var center = new Point(area.X + area.Width / 2, area.Y + area.Height / 2);
					f.Dialog.MouseMove(center);
					f.Dialog.MouseDown(center, MouseButton.Left);
					f.Dialog.MouseUp(center, MouseButton.Left);
					Dpi.Settle(f.Dialog);
					Assert.False(check.IsChecked);
					Assert.Equal(MultiCheck.Unchecked, f.Model["cbPublisher"].Check);
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task The_boxes_a_book_cannot_take_are_disabled_with_their_check_boxes_and_the_catalog_fields_follow_the_files()
		{
			await Ui.Run(() =>
			{
				using (var f = new Fixture())
				{
					foreach (string name in new[] { "txRating", "cbEnableProposed", "cbSeriesComplete" })
					{
						Assert.False(f.Body.Box(name).IsEnabled, name);
						Assert.False(f.Body.Check(name).IsEnabled, name);
					}
					Assert.True(f.Body.Box("txCommunityRating").IsEnabled);
					// the books have files: no Opened/Read and no Pages, but Scan Information
					Assert.False(f.Body.Box("dtpOpenedTime").IsVisible);
					Assert.False(f.Body.Label("dtpOpenedTime").IsVisible);
					Assert.False(f.Body.Box("txPagesAsTextSimple").IsVisible);
					Assert.False(f.Body.Check("txPagesAsTextSimple").IsVisible);
					Assert.False(f.Body.FindControl<Control>("txPagesAsTextSimpleSpin").IsVisible);
					Assert.True(f.Body.Box("txScanInformation").IsVisible);
					Assert.True(f.Body.Label("txScanInformation").IsVisible);
				}
				using (var f = new Fixture(fileless: true))
				{
					Assert.True(f.Body.Box("dtpOpenedTime").IsVisible);
					Assert.True(f.Body.Box("txPagesAsTextSimple").IsVisible);
					Assert.False(f.Body.Box("txScanInformation").IsVisible);
					Assert.False(f.Body.Label("txScanInformation").IsVisible);
				}
				using (var f = new Fixture(preferences: new AppPreferences())) // CatalogOnlyForFileless is on and the books have files
				{
					Assert.False(f.Body.Group("grpCatalog").IsVisible);
				}
				return Task.CompletedTask;
			});
		}

		// ---- the boxes

		[Fact]
		public async Task The_drop_down_boxes_hold_the_lists_of_the_original()
		{
			await Ui.Run(() =>
			{
				using (var f = new Fixture(fileless: true))
				{
					Assert.Equal(new[] { string.Empty, ComicInfo.YesText, ComicInfo.NoText }, Items(f.Body.FindControl<CrValueComboBox>("cbBlackAndWhite")));
					Assert.Equal(new[] { string.Empty, ComicInfo.YesText, ComicInfo.YesRightToLeftText, ComicInfo.NoText }, Items(f.Body.FindControl<CrValueComboBox>("cbManga")));
					Assert.Equal(new[] { string.Empty, ComicInfo.YesText, ComicInfo.NoText }, Items(f.Body.FindControl<CrValueComboBox>("cbEnableProposed")));
					Assert.Equal(new[] { string.Empty, ComicInfo.YesText, ComicInfo.NoText }, Items(f.Body.FindControl<CrValueComboBox>("cbSeriesComplete")));
					CrValueComboBox language = f.Body.FindControl<CrValueComboBox>("cbLanguage");
					Assert.False(language.IsEditable);
					Assert.True(((IEnumerable<ValueItem>)language.ItemsSource).Count() > 50);
					Assert.True(f.Body.FindControl<CrValueComboBox>("cbPublisher").IsEditable);
					// the library's values arrive when a box gets the focus
					f.Body.EnsureList("cbPublisher");
					Assert.Contains("Marvel", Items(f.Body.FindControl<CrValueComboBox>("cbPublisher")));
					f.Body.EnsureList("txSeries");
					Assert.Contains("Series 2", (IEnumerable<string>)f.Body.FindControl<AutoCompleteBox>("txSeries").ItemsSource);
				}
				return Task.CompletedTask;
			});
		}

		private static string[] Items(CrValueComboBox box) => ((IEnumerable<ValueItem>)box.ItemsSource).Select(i => i.Value).ToArray();

		[Fact]
		public async Task The_rating_boxes_show_the_stars_of_the_text_and_a_click_on_the_stars_writes_the_text_and_checks_the_box()
		{
			await Ui.Run(() =>
			{
				using (var f = new Fixture(prepare: books => { foreach (ComicBook b in books) { b.CommunityRating = 2.5f; } }))
				{
					var stars = f.Body.FindControl<CrRatingStars>("txCommunityRating");
					Assert.Equal(2.5, stars.Rating, 0.01);
					Assert.True(f.Body.Check("txCommunityRating").IsChecked);
					Assert.Equal("StarBlue", stars.StarName);
					Assert.Equal("StarYellow", f.Body.FindControl<CrRatingStars>("txRating").StarName);
					Assert.True(stars.DrawText);
					// a press on the stars is the user's change
					Rect area = Dpi.Area(stars, f.Dialog);
					Rect strip = stars.Strip;
					var point = new Point(area.X + strip.X + strip.Width * 0.9, area.Y + strip.Y + strip.Height / 2);
					f.Dialog.MouseMove(point);
					f.Dialog.MouseDown(point, MouseButton.Left);
					f.Dialog.MouseUp(point, MouseButton.Left);
					Dpi.Settle(f.Dialog);
					Assert.True(f.Model["txCommunityRating"].Text.Length > 0);
					Assert.Equal(MultiBookEditorModel.RatingOf(f.Model["txCommunityRating"].Text), stars.Rating, 0.01);
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task The_number_boxes_take_digits_only_where_the_original_does_and_the_spin_buttons_step()
		{
			await Ui.Run(() =>
			{
				using (var f = new Fixture())
				{
					TextBox year = f.Body.FindControl<TextBox>("txYear");
					year.Focus();
					f.Dialog.KeyTextInput("19x9");
					Dpi.Settle(f.Dialog);
					Assert.Equal(string.Empty, year.Text.Replace("19", string.Empty).Replace("9", string.Empty).Replace("1", string.Empty));
					Assert.DoesNotContain("x", year.Text);
					// the day and the number boxes take any text
					TextBox day = f.Body.FindControl<TextBox>("txDay");
					day.Focus();
					f.Dialog.KeyTextInput("a");
					Dpi.Settle(f.Dialog);
					Assert.Equal("a", day.Text);
					// the spin buttons: an empty month starts at this month, then steps
					TextBox month = f.Body.FindControl<TextBox>("txMonth");
					month.Text = string.Empty;
					var up = f.Body.FindControl<CrSpinButton>("txMonthSpin");
					var raise = typeof(CrSpinButton).GetField("ButtonUp", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
					Assert.NotNull(raise);
					var handler = (MulticastDelegate)raise.GetValue(up);
					handler.DynamicInvoke(up, EventArgs.Empty);
					Assert.Equal(DateTime.Now.Month.ToString(), month.Text);
					handler.DynamicInvoke(up, EventArgs.Empty);
					Assert.Equal((DateTime.Now.Month % 12 + 1).ToString(), month.Text);
					Assert.Equal(MultiCheck.Checked, f.Model["txMonth"].Check);
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task The_selector_button_opens_the_list_selector_and_its_result_goes_into_the_box()
		{
			await Ui.Run(() =>
			{
				using (var f = new Fixture())
				{
					AutoCompleteBox writer = f.Body.FindControl<AutoCompleteBox>("txWriter");
					var flyout = f.Body.OpenSelector(f.Body.FindControl<Button>("txWriterSelector"), writer, EditorList.Writer);
					Dpi.Settle(f.Dialog);
					Assert.NotNull(flyout);
					flyout.Hide();
					Dpi.Settle(f.Dialog);
					Assert.Contains("Stan Lee", writer.Text);
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task A_box_with_a_prompt_shows_it_gray_and_takes_it_when_it_gets_the_focus()
		{
			await Ui.Run(() =>
			{
				using (var f = new Fixture(2, prepare: books =>
				{
					books[0].FilePath = "/nowhere/Batman 01.cbz";
					books[1].FilePath = "/nowhere/Batman 02.cbz";
				}))
				{
					AutoCompleteBox series = f.Body.FindControl<AutoCompleteBox>("txSeries");
					string prompt = f.Model["txSeries"].PromptText;
					Assert.False(string.IsNullOrEmpty(prompt));
					Assert.Equal(prompt, series.PlaceholderText);
					Assert.Equal(string.Empty, series.Text);
					series.Focus();
					Dpi.Settle(f.Dialog);
					Assert.Equal(prompt, series.Text);
					Assert.True(f.Body.Check("txSeries").IsChecked);
				}
				return Task.CompletedTask;
			});
		}

		// ---- OK and Cancel

		[Fact]
		public async Task OK_writes_the_checked_boxes_into_every_book_and_closes_with_true()
		{
			await Ui.Run(async () =>
			{
				using (var f = new Fixture())
				{
					f.Body.FindControl<CrValueComboBox>("cbPublisher").Value = "DC";
					f.Body.FindControl<AutoCompleteBox>("txSeries").Text = "One Series";
					f.Body.FindControl<AutoCompleteBox>("txTags").Text = "tag one";
					Dpi.Settle(f.Dialog);
					Click(f.Dialog.OkButton);
					Dpi.Settle(f.Dialog);
					Assert.All(f.Books, b => Assert.Equal("DC", b.Publisher));
					Assert.All(f.Books, b => Assert.Equal("One Series", b.Series));
					Assert.All(f.Books, b => Assert.Equal("tag one", b.Tags));
					Assert.All(f.Books, b => Assert.Contains("Stan Lee", b.Writer));
				}
				await Task.CompletedTask;
			});
		}

		[Fact]
		public async Task Cancel_and_Escape_write_nothing()
		{
			await Ui.Run(() =>
			{
				using (var f = new Fixture())
				{
					f.Body.FindControl<AutoCompleteBox>("txSeries").Text = "Never written";
					Click(f.Dialog.CancelButton);
					Dpi.Settle(f.Dialog);
					Assert.Equal(new[] { "Series 1", "Series 2", "Series 3" }, f.Books.Select(b => b.Series).ToArray());
				}
				using (var f = new Fixture())
				{
					f.Body.FindControl<CrValueComboBox>("cbPublisher").Value = "Never written";
					f.Dialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
					Dpi.Settle(f.Dialog);
					Assert.All(f.Books, b => Assert.Equal("Marvel", b.Publisher));
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task The_dialog_shown_modally_answers_true_after_OK_and_false_after_Cancel()
		{
			await Ui.Run(async () =>
			{
				var owner = new Window { Width = 300, Height = 200 };
				owner.Show();
				var books = new[] { new ComicBook { Series = "A", FilePath = "/n/a.cbz" }, new ComicBook { Series = "B", FilePath = "/n/b.cbz" } };
				var request = new MultipleBooksRequest { Model = new MultiBookEditorModel(books), Preferences = new AppPreferences() };
				var dialog = new MultipleComicBooksDialog(request);
				Task<bool> shown = dialog.ShowDialogAsync(owner);
				Dpi.Settle(dialog);
				request.Model["cbPublisher"].Text = "Image";
				dialog.Accept();
				Assert.True(await shown);
				Assert.All(books, b => Assert.Equal("Image", b.Publisher));
				var second = new MultipleComicBooksDialog(new MultipleBooksRequest { Model = new MultiBookEditorModel(books), Preferences = new AppPreferences() });
				shown = second.ShowDialogAsync(owner);
				Dpi.Settle(second);
				second.Model["cbPublisher"].Text = "Never";
				second.Cancel();
				Assert.False(await shown);
				Assert.All(books, b => Assert.Equal("Image", b.Publisher));
				owner.Close();
			});
		}

		// ---- the groups

		[Fact]
		public async Task A_collapsed_group_is_only_its_header_high_and_the_next_dialog_remembers_it_and_the_scroll_position()
		{
			await Ui.Run(() =>
			{
				using (var f = new Fixture())
				{
					CrCollapsibleGroup artists = f.Body.Group("grpArtists");
					Assert.Equal(204, artists.Bounds.Height, 0.5);
					artists.Collapsed = true;
					Dpi.Settle(f.Dialog);
					Assert.Equal(24, artists.Bounds.Height, 0.5);
					f.Body.PageData.Offset = new Vector(0, 100);
					Dpi.Settle(f.Dialog);
					Assert.Equal(100, f.Body.PageData.Offset.Y, 0.5);
				}
				using (var f = new Fixture())
				{
					Assert.True(f.Body.Group("grpArtists").Collapsed);
					Assert.False(f.Body.Group("grpMain").Collapsed);
					Assert.Equal(24, f.Body.Group("grpArtists").Bounds.Height, 0.5);
					Assert.Equal(100, f.Body.PageData.Offset.Y, 0.5);
				}
				return Task.CompletedTask;
			});
		}

		private static void DoubleClick(Window window, Point point)
		{
			window.MouseMove(point);
			window.MouseDown(point, MouseButton.Left);
			window.MouseUp(point, MouseButton.Left);
			window.MouseDown(point, MouseButton.Left);
			window.MouseUp(point, MouseButton.Left);
			Dpi.Settle(window);
		}

		// a point in the empty bottom strip of the Main group's panel (the ratings end 359 below the header, the panel is 376 high), inside the part of the panel that shows
		private static Point EmptyPlace(Fixture f) => Dpi.Origin((Control)f.Body.Group("grpMain").Content, f.Dialog) + new Point(240, 368);

		[Fact]
		public async Task A_double_click_on_the_empty_part_of_a_group_turns_the_groups_into_tabs_and_the_setting_keeps_it()
		{
			await Ui.Run(() =>
			{
				var preferences = new AppPreferences { CatalogOnlyForFileless = false };
				using (var f = new Fixture(preferences: preferences))
				{
					Assert.False(f.Body.IsTabMode);
					Assert.False(preferences.TabLayouts.HasFlag(TabLayouts.Multiple));
					// a double click on a label is the label's
					Rect label = Dpi.Area(f.Body.FindControl<Label>("labelSeries"), f.Dialog);
					DoubleClick(f.Dialog, new Point(label.X + 4, label.Y + 4));
					Assert.False(f.Body.IsTabMode);
					DoubleClick(f.Dialog, EmptyPlace(f));
					Assert.True(f.Body.IsTabMode);
					Assert.True(preferences.TabLayouts.HasFlag(TabLayouts.Multiple));
					Assert.Equal(new[] { "Main", "Artists / People Involved", "Plot & Notes", "Catalog" }, f.Body.Tabs.Items.OfType<TabItem>().Select(t => (string)t.Header).ToArray());
					Assert.False(f.Body.FindControl<Border>("pageDataFrame").IsVisible);
					// the boxes are in the pages now and still work
					f.Body.FindControl<AutoCompleteBox>("txSeries").Text = "In a tab";
					Assert.True(f.Body.Check("txSeries").IsChecked);
					Assert.True(f.Body.Box("txSeries").IsVisible);
				}
				// the next dialog starts in tab mode, and a double click on a page leaves it
				using (var f = new Fixture(preferences: preferences))
				{
					Assert.True(f.Body.IsTabMode);
					var panel = (Control)((ScrollViewer)f.Body.Tabs.Items.OfType<TabItem>().First().Content).Content;
					DoubleClick(f.Dialog, Dpi.Origin(panel, f.Dialog) + new Point(240, 360));
					Assert.False(f.Body.IsTabMode);
					Assert.False(preferences.TabLayouts.HasFlag(TabLayouts.Multiple));
					Assert.True(f.Body.FindControl<Border>("pageDataFrame").IsVisible);
					Assert.Same(panel, f.Body.Group("grpMain").Content);
				}
				return Task.CompletedTask;
			});
		}

		// ---- the custom fields

		[Fact]
		public async Task The_Custom_Fields_group_has_a_label_a_check_box_and_a_box_for_every_key()
		{
			await Ui.Run(() =>
			{
				using (var f = new Fixture(prepare: books =>
				{
					books[0].SetCustomValue("Rank", "1");
					books[1].SetCustomValue("Rank", "1");
					books[2].SetCustomValue("Rank", "1");
					books[0].SetCustomValue("Origin", "Paris");
					books[1].SetCustomValue("Origin", "Rome");
				}, custom: true))
				{
					CrCollapsibleGroup group = f.Body.Group("grpCustom");
					Assert.True(group.IsVisible);
					Assert.Equal(2, f.Body.CustomBoxes.Count);
					// the first label is at (10,8) and its box at (11,23) 451 wide (18 more for the check box); the next label is 8 under the box
					var origin = f.Body.CustomBoxes[0];
					var rank = f.Body.CustomBoxes[1];
					Rect first = Dpi.Area(origin, f.Body.FindControl<Canvas>("customFieldsCanvas")), second = Dpi.Area(rank, f.Body.FindControl<Canvas>("customFieldsCanvas"));
					Assert.Equal(29, first.X, 0.5);
					Assert.Equal(23, first.Y, 0.5);
					Assert.Equal(433, first.Width, 0.5);
					Assert.Equal(20, first.Height, 0.5);
					Assert.Equal(67, second.Y, 0.5); // 43 + 8 + 12 + 4
					Assert.Equal("Origin", origin.Tag);
					Assert.Equal("Rank", rank.Tag);
					var labels = f.Body.FindControl<Canvas>("customFieldsCanvas").Children.OfType<Label>().Select(l => (string)l.Content).ToArray();
					Assert.Equal(new[] { "Origin:", "Rank:" }, labels);
					Assert.Equal(87 + 8, f.Body.FindControl<Border>("customGroupsPanel").Height, 0.5);
					Assert.Equal(string.Empty, f.Model.CustomFields[0].Field.Text); // Origin differs between the books
					Assert.False(f.Body.Check("custom:Origin").IsChecked);
					Assert.True(f.Body.Check("custom:Rank").IsChecked);
					Assert.Equal("1", rank.Text);
					origin.Text = "Berlin";
					Assert.True(f.Body.Check("custom:Origin").IsChecked);
					Click(f.Dialog.OkButton);
					Dpi.Settle(f.Dialog);
					Assert.All(f.Books, b => Assert.Equal("Berlin", b.GetCustomValue("Origin")));
				}
				return Task.CompletedTask;
			});
		}

		// ---- the look

		[Theory]
		[MemberData(nameof(Looks))]
		public async Task The_panel_has_the_one_unit_WindowFrame_line_and_the_groups_have_their_header_in_light_and_dark_at_1x_and_2x(string appearance, double scale)
		{
			var variant = appearance == "dark" ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light;
			await Ui.Run(() =>
			{
				using (var f = new Fixture())
				{
					f.Dialog.RequestedThemeVariant = variant;
					Dpi.Apply(f.Dialog, scale);
					Raster frame = Dpi.Capture(f.Dialog, "multi-edit-" + appearance);
					SkiaSharp.SKColor Token(string name) { var c = ThemeTokens.Get(name, variant); return new SkiaSharp.SKColor(c.R, c.G, c.B); }
					int px(double logical) => Scales.Device(logical, scale);
					// the frame: the panel is at (12,12), 497 x 407, the line is one unit on each side of it
					SkiaSharp.SKColor line = Token(ThemeTokens.Names.WindowFrame);
					Assert.True(Raster.Near(frame[px(12), px(200)], line, 1), "left line " + frame[px(12), px(200)]);
					Assert.True(Raster.Near(frame[px(12 + 497 - 1), px(200)], line, 1), "right line");
					Assert.True(Raster.Near(frame[px(100), px(12)], line, 1), "top line");
					Assert.True(Raster.Near(frame[px(100), px(12 + 407 - 1)], line, 1), "bottom line");
					// outside the frame is the dialog's surface, crisp
					SkiaSharp.SKColor surface = Token(ThemeTokens.Names.Control);
					Assert.True(Raster.Near(frame[px(6), px(200)], surface, 1));
					// the header of the Main group is a gradient from CollapsibleGroupBoxHeaderStart to ...End
					Rect header = Dpi.Area(f.Body.Group("grpMain"), f.Dialog);
					SkiaSharp.SKColor start = Token(ThemeTokens.Names.CollapsibleGroupBoxHeaderStart);
					Assert.True(Raster.Near(frame[(int)(header.X * scale) + px(60), (int)(header.Y * scale) + px(2)], start, 40), "header " + frame[(int)(header.X * scale) + px(60), (int)(header.Y * scale) + px(2)]);
				}
				return Task.CompletedTask;
			});
		}

		public static IEnumerable<object[]> Looks => new[] { "light", "dark" }.SelectMany(a => Scales.Main.Select(s => new object[] { a, s }));
	}
}
