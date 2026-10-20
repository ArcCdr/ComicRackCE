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

	/// <summary>The outcome of an alert: the 0-based index of the pressed button and the state of the suppression check box.</summary>
	public sealed class MacAlertResult
	{
		public MacAlertResult(int buttonIndex, bool suppressed)
		{
			ButtonIndex = buttonIndex;
			Suppressed = suppressed;
		}

		public int ButtonIndex { get; }

		public bool Suppressed { get; }
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
			if (UseNative && NativeSupported && Dispatcher.UIThread.CheckAccess())
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
				return new MacAlertResult((int)Math.Max(0, response - FirstButtonReturn), suppressed);
			}
			finally
			{
				ObjC.Call(alert, "release");
			}
		}
	}
}
