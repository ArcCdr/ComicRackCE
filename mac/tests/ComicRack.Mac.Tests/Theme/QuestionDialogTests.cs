using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Avalonia.Threading;
using ComicRack.Mac.Controls;
using ComicRack.Mac.Platform;
using ComicRack.Mac.Services;
using ComicRack.Mac.Tests.HiDpi;
using ComicRack.Mac.Views;
using cYo.Projects.ComicRack.Engine;
using Xunit;
using static ComicRack.Mac.Tests.Theme.ChromeTestSupport;

namespace ComicRack.Mac.Tests.Theme
{
	/// <summary>P5.5: the question of <c>QuestionDialog</c> with its check boxes (DL.30 and the Remove question) and the Quick Rating dialog (DL.18).</summary>
	public class QuestionDialogTests
	{
		private static async Task<T> InOwner<T>(Func<Window, Task<T>> work)
		{
			bool before = MacAlert.UseNative;
			MacAlert.UseNative = false; // the system alert cannot run in a test
			try
			{
				return await Ui.Run(async () =>
				{
					var owner = new Window { Width = 400, Height = 300 };
					owner.Show();
					Dpi.Settle(owner);
					try
					{
						return await work(owner);
					}
					finally
					{
						owner.Close();
					}
				});
			}
			finally
			{
				MacAlert.UseNative = before;
			}
		}

		private static void Click(Button button) => button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

		[Fact]
		public async Task The_question_window_shows_the_two_options_and_the_second_only_while_the_first_is_checked()
		{
			MacAlertResult result = await InOwner(async owner =>
			{
				var options = new MacAlertOptions
				{
					Message = "Are you sure?",
					Informative = "The books stay in the files.",
					Buttons = new[] { "Remove", "Cancel" },
					CancelIndex = 1,
					Options = new[] { new MacAlertOption("Additionally remove from the library", isChecked: false), new MacAlertOption("Also move the files to the Trash", isChecked: true, onlyWhilePreviousChecked: true) },
				};
				Task<MacAlertResult> task = MacAlert.ShowAsync(owner, options);
				Dpi.Settle(owner);
				AlertWindow window = owner.OwnedWindows.OfType<AlertWindow>().Single();
				Assert.Equal(2, window.OptionBoxes.Count);
				Assert.False(window.OptionBoxes[0].IsChecked);
				Assert.True(window.OptionBoxes[1].IsChecked);
				Assert.False(window.OptionBoxes[1].IsVisible); // the first is not checked
				Assert.Equal(32, window.OptionBoxes[1].Margin.Left); // indented under the first
				window.OptionBoxes[0].IsChecked = true;
				Assert.True(window.OptionBoxes[1].IsVisible);
				Click(window.AlertButtons[0]);
				return await task;
			});
			Assert.Equal(0, result.ButtonIndex);
			Assert.Equal(new[] { true, true }, result.Options);

			// a hidden second option keeps its state, as QuestionDialog.Ask reads it
			MacAlertResult hidden = await InOwner(async owner =>
			{
				var options = new MacAlertOptions
				{
					Message = "Sure?",
					Buttons = new[] { "OK", "Cancel" },
					CancelIndex = 1,
					Options = new[] { new MacAlertOption("one"), new MacAlertOption("two", isChecked: true, onlyWhilePreviousChecked: true) },
				};
				Task<MacAlertResult> task = MacAlert.ShowAsync(owner, options);
				Dpi.Settle(owner);
				Click(owner.OwnedWindows.OfType<AlertWindow>().Single().AlertButtons[0]);
				return await task;
			});
			Assert.Equal(new[] { false, true }, hidden.Options);
		}

		[Fact]
		public async Task A_question_with_options_is_the_window_and_one_without_still_has_its_suppression_box()
		{
			Assert.Empty(new MacAlertOptions().Options);
			var plain = new MacAlertOptions();
			plain.Options.Add(new MacAlertOption("x"));
			Assert.Single(plain.Options);
			await Task.CompletedTask;
		}

		[Fact]
		public async Task The_dialogs_service_asks_the_remove_question_as_the_original_does()
		{
			(QuestionAnswer ok, string[] buttons, string message, string description, bool firstChecked) = await InOwner(async owner =>
			{
				var dialogs = new AvaloniaLibraryDialogs(owner);
				Task<QuestionAnswer> task = dialogs.AskQuestionAsync(new QuestionRequest
				{
					Question = "Are you sure you want to remove these books from the library?\nAll information not stored in the files will be lost!",
					OkText = "Remove",
					OptionText = "!&Also move the files to the Trash",
				});
				Dpi.Settle(owner);
				AlertWindow window = owner.OwnedWindows.OfType<AlertWindow>().Single();
				string[] names = window.AlertButtons.Select(b => (string)b.Content).ToArray();
				var texts = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToArray();
				bool first = window.OptionBoxes[0].IsChecked == true;
				Assert.Equal("Also move the files to the Trash", window.OptionBoxes[0].Content); // no mnemonic on macOS
				Click(window.AlertButtons[0]);
				return (await task, names, texts[0], texts[1], first);
			});
			Assert.True(ok.Accepted);
			Assert.True(ok.Option); // checked at the start and left so
			Assert.False(ok.Option2);
			Assert.Equal(new[] { "Remove", "Cancel" }, buttons);
			Assert.Equal("Are you sure you want to remove these books from the library?", message);
			Assert.Equal("All information not stored in the files will be lost!", description);
			Assert.True(firstChecked);

			QuestionAnswer cancelled = await InOwner(async owner =>
			{
				var dialogs = new AvaloniaLibraryDialogs(owner);
				Task<QuestionAnswer> task = dialogs.AskQuestionAsync(new QuestionRequest { Question = "Remove?", OkText = "Remove", OptionText = "Option" });
				Dpi.Settle(owner);
				AlertWindow window = owner.OwnedWindows.OfType<AlertWindow>().Single();
				window.OptionBoxes[0].IsChecked = true;
				window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
				return await task;
			});
			Assert.False(cancelled.Accepted);

			QuestionAnswer onlyOk = await InOwner(async owner =>
			{
				var dialogs = new AvaloniaLibraryDialogs(owner);
				Task<QuestionAnswer> task = dialogs.AskQuestionAsync(new QuestionRequest { Question = "Done", ShowCancel = false });
				Dpi.Settle(owner);
				AlertWindow window = owner.OwnedWindows.OfType<AlertWindow>().Single();
				Assert.Single(window.AlertButtons);
				Assert.Equal("OK", window.AlertButtons[0].Content);
				Click(window.AlertButtons[0]);
				return await task;
			});
			Assert.True(onlyOk.Accepted);
		}

		[Fact]
		public async Task The_dependent_option_of_the_list_question_and_the_do_not_ask_box_come_back_in_the_answer()
		{
			QuestionAnswer answer = await InOwner(async owner =>
			{
				var dialogs = new AvaloniaLibraryDialogs(owner);
				Task<QuestionAnswer> task = dialogs.AskQuestionAsync(new QuestionRequest
				{
					Question = "Remove from the list?",
					OkText = "Remove",
					OptionText = "!Additionally from the library",
					Option2Text = "!&Also move the files to the Trash",
					SuppressionText = "&Do not ask me again",
				});
				Dpi.Settle(owner);
				AlertWindow window = owner.OwnedWindows.OfType<AlertWindow>().Single();
				Assert.True(window.OptionBoxes[1].IsVisible); // the first one starts checked
				Assert.Equal("Do not ask me again", window.SuppressionBox.Content);
				window.OptionBoxes[0].IsChecked = false;
				Assert.False(window.OptionBoxes[1].IsVisible);
				window.SuppressionBox.IsChecked = true;
				Click(window.AlertButtons[0]);
				return await task;
			});
			Assert.True(answer.Accepted);
			Assert.False(answer.Option);
			Assert.True(answer.Option2); // hidden, still as it was
			Assert.True(answer.Suppressed);
		}

		// ---- Quick Rating

		private static ComicBook Book() => new ComicBook { Series = "Alpha", Number = "3", Title = "The Third", FilePath = "/nowhere/a3.cbz", PageCount = 10, Rating = 2.5f, Review = "Good start" };

		[Fact]
		public async Task The_quick_rating_dialog_has_the_layout_the_texts_and_the_values_of_the_original()
		{
			await InAppearance("Light", () =>
			{
				ComicBook book = Book();
				var window = new QuickRatingWindow(new QuickRatingRequest { Book = book, ShowWhenRead = true });
				window.Show();
				Dpi.Settle(window);
				Assert.Equal("Quick Rating - " + book.CaptionWithoutTitle, window.Title);
				// 564 wide body; cover (12,12) 170 x 227, stars (12,245) 170 x 21, review (188,12) 369 x 254; check box at the left of the buttons
				Assert.Equal(new Rect(12, 12, 170, 227), Dpi.Area(window.Cover, window.Body));
				Assert.Equal(new Rect(12, 245, 170, 21), Dpi.Area(window.Stars, window.Body));
				Assert.Equal(new Rect(188, 12, 369, 254), Dpi.Area(window.ReviewBox, window.Body));
				Assert.Equal(564, window.Body.Bounds.Width);
				Assert.Equal("Good start", window.ReviewBox.Text);
				Assert.Equal(2.5, window.Stars.Rating);
				Assert.True(window.ReviewBox.AcceptsReturn);
				Assert.Equal("Show when Book read", window.ShowWhenReadBox.Content);
				Assert.True(window.ShowWhenReadBox.IsChecked);
				Rect box = Dpi.Area(window.ShowWhenReadBox, window), ok = Dpi.Area(window.OkButton, window);
				Assert.True(box.Right < ok.X && Math.Abs(box.Center.Y - ok.Center.Y) < 4, "the check box is level with the buttons, at their left: " + box + " " + ok);
				Assert.Equal("OK", window.OkButton.Content);
				Assert.Equal("Cancel", window.CancelButton.Content);
				window.Close();
			});
		}

		[Fact]
		public async Task OK_returns_review_rating_and_the_check_box_and_Cancel_returns_nothing()
		{
			QuickRatingResult result = await InOwner(async owner =>
			{
				var dialogs = new AvaloniaLibraryDialogs(owner);
				Task<QuickRatingResult> task = dialogs.AskQuickRatingAsync(new QuickRatingRequest { Book = Book(), ShowWhenRead = false });
				Dpi.Settle(owner);
				QuickRatingWindow window = owner.OwnedWindows.OfType<QuickRatingWindow>().Single();
				window.ReviewBox.Text = "Better than I thought";
				window.Stars.Rating = 4.5;
				window.ShowWhenReadBox.IsChecked = true;
				Click(window.OkButton);
				return await task;
			});
			Assert.Equal("Better than I thought", result.Review);
			Assert.Equal(4.5f, result.Rating);
			Assert.True(result.ShowWhenRead);

			QuickRatingResult cancelled = await InOwner(async owner =>
			{
				var dialogs = new AvaloniaLibraryDialogs(owner);
				Task<QuickRatingResult> task = dialogs.AskQuickRatingAsync(new QuickRatingRequest { Book = Book() });
				Dpi.Settle(owner);
				QuickRatingWindow window = owner.OwnedWindows.OfType<QuickRatingWindow>().Single();
				window.Stars.Rating = 1;
				window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
				return await task;
			});
			Assert.Null(cancelled);
		}
	}
}
