using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using ComicRack.Mac.Controls;

namespace ComicRack.Mac.Platform
{
	/// <summary>The icon style of an alert (<c>NSAlertStyle</c>).</summary>
	public enum MacAlertStyle
	{
		Warning = 0,
		Informational = 1,
		Critical = 2,
	}

	/// <summary>
	/// An extra check box of a question (<c>QuestionDialog.chkOption</c> and <c>chkOption2</c>). <see cref="OnlyWhilePreviousChecked"/>: the box is
	/// shown, indented, only while the box before it is checked (the original's second option, unless it is independent).
	/// </summary>
	public sealed class MacAlertOption
	{
		public MacAlertOption(string text, bool isChecked = false, bool onlyWhilePreviousChecked = false)
		{
			Text = text;
			Checked = isChecked;
			OnlyWhilePreviousChecked = onlyWhilePreviousChecked;
		}

		public string Text { get; }

		public bool Checked { get; }

		public bool OnlyWhilePreviousChecked { get; }
	}

	/// <summary>
	/// What an alert says. <see cref="Message"/> is the first line of the original's question text (<c>QuestionDialog.Question</c>, the part before
	/// the first line break), <see cref="Informative"/> the rest. <see cref="Buttons"/> are left to right as the original shows them: the first is the
	/// default button (Return); <see cref="CancelIndex"/> names the one that Esc presses (-1: none). <see cref="SuppressionText"/> is the check box of
	/// the "do not ask again" questions (<c>QuestionDialog.OptionText</c>); null shows none.
	/// </summary>
	public sealed class MacAlertOptions
	{
		public string Message { get; set; } = string.Empty;

		public string Informative { get; set; } = string.Empty;

		public IList<string> Buttons { get; set; } = new List<string> { "OK" };

		public int CancelIndex { get; set; } = -1;

		public string SuppressionText { get; set; }

		/// <summary>
		/// Check boxes of a question that the system alert cannot show (it has one, the suppression box): an alert with options is always the window,
		/// ComicRack's own question dialog (chrome decision C-DLG-3).
		/// </summary>
		public IList<MacAlertOption> Options { get; set; } = new List<MacAlertOption>();

		public MacAlertStyle Style { get; set; } = MacAlertStyle.Informational;

		/// <summary>Splits a question as the original does: the first line is the message, the other lines the description.</summary>
		public static MacAlertOptions FromQuestion(string question, params string[] buttons)
		{
			string[] lines = (question ?? string.Empty).Split('\n');
			return new MacAlertOptions
			{
				Message = lines[0].TrimEnd('\r'),
				Informative = lines.Length > 1 ? string.Join("\n", lines, 1, lines.Length - 1).Trim('\r', '\n') : string.Empty,
				Buttons = buttons != null && buttons.Length > 0 ? new List<string>(buttons) : new List<string> { "OK" },
			};
		}
	}

	/// <summary>The outcome of an alert: the 0-based index of the pressed button (-1: the alert ended without one and has no cancel button), the state of the suppression check box and of the option check boxes (in the order of <see cref="MacAlertOptions.Options"/>).</summary>
	public sealed class MacAlertResult
	{
		public MacAlertResult(int buttonIndex, bool suppressed, IReadOnlyList<bool> options = null)
		{
			ButtonIndex = buttonIndex;
			Suppressed = suppressed;
			Options = options ?? Array.Empty<bool>();
		}

		public int ButtonIndex { get; }

		public bool Suppressed { get; }

		/// <summary>The state of each option box when the alert closed (false for a box that was hidden).</summary>
		public IReadOnlyList<bool> Options { get; }
	}

	/// <summary>
	/// C-DLG-1: message boxes and questions are the system alert, <c>NSAlert</c>, reached through the Objective-C runtime (no native library of our
	/// own). The first button is the default, the cancel button answers Esc, and the "do not ask again" question is the alert's suppression check box
	/// (<c>showsSuppressionButton</c>). Where the system alert cannot run (not macOS, the headless platform, tests) the same options are shown in an
	/// <see cref="AlertWindow"/>, the MVP message window restyled with the original's dialog look. Persisting the suppression is the caller's job.
	/// </summary>
	public static class MacAlert
	{
		/// <summary>Set to false (tests, the headless platform, <c>COMICRACK_MAC_NATIVE_ALERTS=0</c>) to always use the window.</summary>
		public static bool UseNative { get; set; } = Environment.GetEnvironmentVariable("COMICRACK_MAC_NATIVE_ALERTS") != "0";

		private const long FirstButtonReturn = 1000;

		/// <summary>True if the Objective-C classes and selectors this class uses exist (checked without creating an alert).</summary>
		public static bool NativeSupported =>
			ObjC.IsAvailable
			&& ObjC.HasInstanceMethod("NSAlert", "runModal")
			&& ObjC.HasInstanceMethod("NSAlert", "setMessageText:")
			&& ObjC.HasInstanceMethod("NSAlert", "setInformativeText:")
			&& ObjC.HasInstanceMethod("NSAlert", "addButtonWithTitle:")
			&& ObjC.HasInstanceMethod("NSAlert", "setShowsSuppressionButton:")
			&& ObjC.HasInstanceMethod("NSAlert", "suppressionButton")
			&& ObjC.HasInstanceMethod("NSAlert", "setAlertStyle:")
			&& ObjC.HasInstanceMethod("NSAlert", "buttons");

		/// <summary>Shows the alert and waits. Native when allowed and supported, else the window (which needs <paramref name="owner"/> or a main window).</summary>
		public static async Task<MacAlertResult> ShowAsync(Window owner, MacAlertOptions options)
		{
			if (options == null)
			{
				throw new ArgumentNullException(nameof(options));
			}
			if (!Dispatcher.UIThread.CheckAccess())
			{
				// the system alert and the window both belong to the UI thread: a caller on a worker is moved there (the result comes back to the caller)
				return await Dispatcher.UIThread.InvokeAsync(() => ShowAsync(owner, options));
			}
			if (UseNative && NativeSupported && options.Options.Count == 0)
			{
				return ShowNative(options);
			}
			var window = new AlertWindow(options);
			bool accepted = owner != null ? await window.ShowDialogAsync(owner) : await ShowWithoutOwner(window);
			return window.Result(accepted);
		}

		private static Task<bool> ShowWithoutOwner(AlertWindow window)
		{
			var done = new TaskCompletionSource<bool>();
			window.Closed += (sender, e) => done.TrySetResult(window.ClosedWithButton);
			window.Show();
			return done.Task;
		}

		/// <summary>The system alert. UI thread only; blocks until a button is pressed.</summary>
		public static MacAlertResult ShowNative(MacAlertOptions o)
		{
			// the strings, the button list and the suppression button are autoreleased: the pool goes when the alert is gone
			using (ObjC.AutoreleasePool())
			{
				IntPtr alert = ObjC.Call(ObjC.Call(ObjC.Class("NSAlert"), "alloc"), "init");
				try
				{
					ObjC.Call(alert, "setMessageText:", ObjC.NSString(o.Message));
					ObjC.Call(alert, "setInformativeText:", ObjC.NSString(o.Informative));
					ObjC.Call(alert, "setAlertStyle:", (long)o.Style);
					foreach (string title in o.Buttons)
					{
						ObjC.Call(alert, "addButtonWithTitle:", ObjC.NSString(title));
					}
					if (o.CancelIndex >= 0 && o.CancelIndex < o.Buttons.Count)
					{
						// Esc presses the cancel button even when its (translated) title is not "Cancel"
						IntPtr button = ObjC.Call(ObjC.Call(alert, "buttons"), "objectAtIndex:", (long)o.CancelIndex);
						ObjC.Call(button, "setKeyEquivalent:", ObjC.NSString("\u001b"));
					}
					IntPtr suppression = IntPtr.Zero;
					if (!string.IsNullOrEmpty(o.SuppressionText))
					{
						ObjC.CallBool(alert, "setShowsSuppressionButton:", true);
						suppression = ObjC.Call(alert, "suppressionButton");
						ObjC.Call(suppression, "setTitle:", ObjC.NSString(o.SuppressionText));
					}
					long response = ObjC.CallReturnsLong(alert, "runModal");
					bool suppressed = suppression != IntPtr.Zero && ObjC.CallReturnsLong(suppression, "state") == 1;
					return new MacAlertResult(ButtonOf(response, o), suppressed);
				}
				finally
				{
					ObjC.Call(alert, "release");
				}
			}
		}

		/// <summary><c>NSModalResponseStop</c>: the modal session was ended by the application (<c>stopModal</c>), no button was pressed.</summary>
		public const long ModalResponseStop = -1000;

		/// <summary><c>NSModalResponseAbort</c>: the modal session was aborted (<c>abortModal</c>, the application is quitting), no button was pressed.</summary>
		public const long ModalResponseAbort = -1001;

		/// <summary>
		/// The 0-based button for a response of <c>runModal</c>. A button gives <c>NSAlertFirstButtonReturn</c> (1000) plus its position. Stop, Abort and
		/// any other value are no button: they answer as the cancel button (<see cref="MacAlertOptions.CancelIndex"/>), or -1 when the alert has none, so
		/// that an ended session never counts as a press of the default (first) button.
		/// </summary>
		public static int ButtonOf(long response, MacAlertOptions options)
		{
			if (response >= FirstButtonReturn && response - FirstButtonReturn < options.Buttons.Count)
			{
				return (int)(response - FirstButtonReturn);
			}
			return options.CancelIndex >= 0 && options.CancelIndex < options.Buttons.Count ? options.CancelIndex : -1;
		}
	}
}
