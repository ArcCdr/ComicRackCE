using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ComicRack.Mac.Controls;
using ComicRack.Mac.Platform;
using ComicRack.Mac.Tests.HiDpi;
using ComicRack.Mac.Views;
using Xunit;
using static ComicRack.Mac.Tests.Theme.ChromeTestSupport;

namespace ComicRack.Mac.Tests.Theme
{
	/// <summary>
	/// P5.T4: the dialog window (C-DLG-2: button row of the original, Return and Esc, modal over an owner), the alert (C-DLG-1: the NSAlert bindings are
	/// checked without creating an alert, the window fallback end to end) and the two start-up settings of the native window layer (C-WIN-3, C-SCROLL-1).
	/// </summary>
	public class ChromeDialogTests
	{
		private static async Task<T> InDialog<T>(Func<Window, Task<T>> work)
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

		private static void Settle(Window w) => Dpi.Settle(w);

		[Fact]
		public async Task The_button_row_is_OK_Cancel_Apply_80_by_24_six_apart_and_15_from_the_right_edge()
		{
			await InAppearance("Light", () =>
			{
				var dialog = new CrDialogWindow { Buttons = DialogButtons.OkCancelApply, Body = new Border { Width = 360, Height = 120 } };
				dialog.Show();
				Settle(dialog);
				Button ok = dialog.OkButton, cancel = dialog.CancelButton, apply = dialog.ApplyButton;
				Assert.Equal(new Size(80, 24), ok.Bounds.Size);
				Assert.Equal(new Size(80, 24), cancel.Bounds.Size);
				Assert.Equal(new Size(80, 24), apply.Bounds.Size);
				Rect o = Dpi.Area(ok, dialog), c = Dpi.Area(cancel, dialog), a = Dpi.Area(apply, dialog);
				Assert.Equal(o.Right + 6, c.X);
				Assert.Equal(c.Right + 6, a.X);
				Assert.Equal(dialog.Bounds.Width - 15, a.Right);
				Assert.Equal(dialog.Bounds.Height - 12, o.Bottom);
				Assert.True(ok.IsDefault);
				Assert.True(cancel.IsCancel);
				Assert.Equal("OK", dialog.OkText);
				Assert.Equal("Cancel", dialog.CancelText);
				Assert.Equal("Apply", dialog.ApplyText);
				dialog.Buttons = DialogButtons.Ok;
				Settle(dialog);
				Assert.False(cancel.IsVisible && cancel.Bounds.Width > 0 && cancel.GetVisualParent() != null);
				dialog.OkText = "Close";
				Assert.Equal("Close", dialog.OkText);
				dialog.Close();
			});
		}

		[Theory]
		[InlineData("&OK", "OK")]
		[InlineData("&Cancel", "Cancel")]
		[InlineData("A&&B", "A&B")]
		[InlineData("Plain", "Plain")]
		[InlineData("", "")]
		public void Mnemonics_are_dropped_as_on_macOS(string text, string expected)
		{
			Assert.Equal(expected, CrDialogWindow.StripMnemonic(text));
		}

		[Fact]
		public async Task Return_presses_OK_and_the_dialog_returns_true_Esc_presses_Cancel_and_returns_false()
		{
			bool accepted = await InDialog(async owner =>
			{
				var dialog = new CrDialogWindow { Body = new Border { Width = 300, Height = 100 } };
				int ok = 0;
				dialog.OkClicked += (sender, e) => ok++;
				Task<bool> result = dialog.ShowDialogAsync(owner);
				Settle(dialog);
				dialog.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
				bool r = await result;
				Assert.Equal(1, ok);
				return r;
			});
			Assert.True(accepted);

			bool cancelled = await InDialog(async owner =>
			{
				var dialog = new CrDialogWindow { Body = new Border { Width = 300, Height = 100 } };
				int cancels = 0;
				dialog.CancelClicked += (sender, e) => cancels++;
				Task<bool> result = dialog.ShowDialogAsync(owner);
				Settle(dialog);
				dialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
				bool r = await result;
				Assert.Equal(1, cancels);
				return r;
			});
			Assert.False(cancelled);
		}

		[Fact]
		public async Task A_validation_can_keep_the_dialog_open_and_Apply_does_not_close_it()
		{
			await InDialog(async owner =>
			{
				var dialog = new CrDialogWindow { Buttons = DialogButtons.OkCancelApply, Body = new Border { Width = 300, Height = 100 } };
				int applies = 0;
				bool invalid = true;
				dialog.ApplyClicked += (sender, e) => applies++;
				dialog.OkClicked += (sender, e) => e.Cancel = invalid;
				Task<bool> result = dialog.ShowDialogAsync(owner);
				Settle(dialog);
				dialog.Accept();
				Assert.False(result.IsCompleted);
				dialog.ApplyButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
				Assert.Equal(1, applies);
				Assert.False(result.IsCompleted);
				invalid = false;
				dialog.Accept();
				Assert.True(await result);
				return 0;
			});
		}

		[Fact]
		public async Task The_dialog_is_modal_centred_on_its_owner_and_not_resizable()
		{
			await InAppearance("Light", () =>
			{
				var dialog = new CrDialogWindow();
				Assert.Equal(WindowStartupLocation.CenterOwner, dialog.WindowStartupLocation);
				Assert.False(dialog.CanResize);
				Assert.Equal(DialogButtons.OkCancel, dialog.Buttons);
			});
		}

		// ---- alerts ----

		[Fact]
		public void A_question_is_split_into_the_message_and_the_description_as_QuestionDialog_does()
		{
			MacAlertOptions o = MacAlertOptions.FromQuestion("Delete the list?\nThe books stay in the library.\nThis cannot be undone.", "OK", "Cancel");
			Assert.Equal("Delete the list?", o.Message);
			Assert.Equal("The books stay in the library.\nThis cannot be undone.", o.Informative);
			Assert.Equal(new[] { "OK", "Cancel" }, o.Buttons.ToArray());
			MacAlertOptions single = MacAlertOptions.FromQuestion("Only a message");
			Assert.Equal(string.Empty, single.Informative);
			Assert.Equal(new[] { "OK" }, single.Buttons.ToArray());
		}

		[Fact]
		public void The_system_alert_classes_and_selectors_that_MacAlert_calls_exist()
		{
			if (!OperatingSystem.IsMacOS())
			{
				return;
			}
			Assert.True(MacAlert.NativeSupported);
			foreach (string selector in new[] { "runModal", "setMessageText:", "setInformativeText:", "addButtonWithTitle:", "setShowsSuppressionButton:", "suppressionButton", "setAlertStyle:", "buttons" })
			{
				Assert.True(ObjC.HasInstanceMethod("NSAlert", selector), "NSAlert " + selector);
			}
			Assert.True(ObjC.HasInstanceMethod("NSButton", "setKeyEquivalent:"));
			Assert.True(ObjC.HasInstanceMethod("NSButton", "setTitle:"));
			Assert.True(ObjC.HasInstanceMethod("NSButton", "state"));
		}

		[Fact]
		public void An_NSString_round_trips_text_with_non_ascii_characters()
		{
			if (!OperatingSystem.IsMacOS())
			{
				return;
			}
			Assert.Equal("Abbruch: äöü 100 % –", ObjC.ToManaged(ObjC.NSString("Abbruch: äöü 100 % –")));
			Assert.Equal(string.Empty, ObjC.ToManaged(IntPtr.Zero));
		}

		[Fact]
		public async Task Where_the_system_alert_cannot_run_the_alert_window_answers_with_the_pressed_button_and_the_suppression_box()
		{
			bool before = MacAlert.UseNative;
			MacAlert.UseNative = false;
			try
			{
				MacAlertResult result = await InDialog(async owner =>
				{
					var options = new MacAlertOptions
					{
						Message = "Replace the file?",
						Informative = "It exists already.",
						Buttons = { },
						SuppressionText = "Do not ask again",
						CancelIndex = 1,
					};
					options.Buttons = new[] { "Replace", "Cancel" };
					Task<MacAlertResult> task = MacAlert.ShowAsync(owner, options);
					Settle(owner);
					AlertWindow window = owner.OwnedWindows.OfType<AlertWindow>().Single();
					Assert.Equal(2, window.AlertButtons.Count);
					Assert.True(window.AlertButtons[0].IsDefault);
					Assert.True(window.AlertButtons[1].IsCancel);
					Assert.Equal("Do not ask again", window.SuppressionBox.Content);
					window.SuppressionBox.IsChecked = true;
					window.AlertButtons[0].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
					return await task;
				});
				Assert.Equal(0, result.ButtonIndex);
				Assert.True(result.Suppressed);

				MacAlertResult escaped = await InDialog(async owner =>
				{
					var options = new MacAlertOptions { Message = "Sure?", Buttons = new[] { "OK", "Cancel" }, CancelIndex = 1 };
					Task<MacAlertResult> task = MacAlert.ShowAsync(owner, options);
					Settle(owner);
					AlertWindow window = owner.OwnedWindows.OfType<AlertWindow>().Single();
					Assert.Null(window.SuppressionBox);
					window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
					return await task;
				});
				Assert.Equal(1, escaped.ButtonIndex);
				Assert.False(escaped.Suppressed);

				MacAlertResult enter = await InDialog(async owner =>
				{
					Task<MacAlertResult> task = MacAlert.ShowAsync(owner, new MacAlertOptions { Message = "Done", Buttons = new[] { "OK" } });
					Settle(owner);
					AlertWindow window = owner.OwnedWindows.OfType<AlertWindow>().Single();
					window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
					return await task;
				});
				Assert.Equal(0, enter.ButtonIndex);
			}
			finally
			{
				MacAlert.UseNative = before;
			}
		}

		/// <summary>RV8: <c>runModal</c> answers 1000 + the button, or Stop (-1000) / Abort (-1001) when the session was ended from outside: that is no button, and never the first one.</summary>
		[Fact]
		public void A_response_of_the_system_alert_is_a_button_and_Stop_or_Abort_answers_as_the_cancel_button()
		{
			var three = new MacAlertOptions { Buttons = new[] { "Replace", "Cancel", "Skip" }, CancelIndex = 1 };
			Assert.Equal(0, MacAlert.ButtonOf(1000, three));
			Assert.Equal(1, MacAlert.ButtonOf(1001, three));
			Assert.Equal(2, MacAlert.ButtonOf(1002, three));
			Assert.Equal(1, MacAlert.ButtonOf(MacAlert.ModalResponseStop, three));
			Assert.Equal(1, MacAlert.ButtonOf(MacAlert.ModalResponseAbort, three));
			Assert.Equal(1, MacAlert.ButtonOf(0, three)); // NSModalResponseCancel
			Assert.Equal(1, MacAlert.ButtonOf(1003, three)); // a response past the last button
			var withoutCancel = new MacAlertOptions { Buttons = new[] { "Delete", "Keep" } };
			Assert.Equal(-1, MacAlert.ButtonOf(MacAlert.ModalResponseStop, withoutCancel));
			Assert.Equal(-1, MacAlert.ButtonOf(MacAlert.ModalResponseAbort, withoutCancel));
			Assert.Equal(1, MacAlert.ButtonOf(1001, withoutCancel));
		}

		[Fact]
		public void An_autorelease_pool_can_be_nested_and_disposed_twice_and_the_strings_made_inside_it_are_read_before_it_goes()
		{
			if (!OperatingSystem.IsMacOS())
			{
				return;
			}
			IntPtr text;
			using (IDisposable outer = ObjC.AutoreleasePool())
			{
				using (IDisposable inner = ObjC.AutoreleasePool())
				{
					text = ObjC.NSString("Abbruch");
					Assert.Equal("Abbruch", ObjC.ToManaged(text));
					inner.Dispose();
				}
				Assert.Equal("Abbruch", ObjC.ToManaged(ObjC.NSString("Abbruch")));
			}
		}

		/// <summary>An alert asked for from a worker thread is shown on the UI thread (the window and the system alert both belong to it).</summary>
		[Fact]
		public async Task An_alert_asked_for_from_a_worker_thread_is_shown_on_the_UI_thread_and_the_answer_comes_back()
		{
			bool before = MacAlert.UseNative;
			MacAlert.UseNative = false;
			try
			{
				MacAlertResult result = await InDialog(async owner =>
				{
					Task<MacAlertResult> task = Task.Run(() => MacAlert.ShowAsync(owner, new MacAlertOptions { Message = "From a worker", Buttons = new[] { "OK", "Cancel" }, CancelIndex = 1 }));
					for (int i = 0; i < 2000 && !owner.OwnedWindows.OfType<AlertWindow>().Any(); i++)
					{
						Avalonia.Threading.Dispatcher.UIThread.RunJobs();
						await Task.Delay(5);
					}
					Settle(owner);
					AlertWindow window = owner.OwnedWindows.OfType<AlertWindow>().Single();
					Assert.True(Avalonia.Threading.Dispatcher.UIThread.CheckAccess());
					window.AlertButtons[1].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
					return await task;
				});
				Assert.Equal(1, result.ButtonIndex);
			}
			finally
			{
				MacAlert.UseNative = before;
			}
		}

		// ---- window layer ----

		[Fact]
		public void Automatic_window_tabbing_can_be_switched_off_and_reads_back_off()
		{
			if (!OperatingSystem.IsMacOS())
			{
				return;
			}
			Assert.True(WindowTabbing.Disable());
			Assert.Equal(false, WindowTabbing.IsAutomaticTabbingAllowed());
		}

		[Fact]
		public void The_scroller_style_of_the_system_can_be_read()
		{
			// either answer is valid; the call must not throw and must be stable
			Assert.Equal(ScrollerStyle.IsOverlay, ScrollerStyle.IsOverlay);
		}
	}
}
