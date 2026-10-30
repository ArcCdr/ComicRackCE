using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using cYo.Compat.Forms;

// MessageBox (P4.9f): every Show overload shows a dialog and returns the DialogResult of the button that closed it. The Framework's message box is a native Win32 window, not a
// Form; here it is a Form of the facade (MessageBoxDialog) that the backend shows like any other dialog, so that a headless run, the tests and the first backend need nothing
// of their own. P4.10f may map it to a native alert: the dialog keeps what that needs (message, caption, icon, buttons, default button) as internal properties.
namespace System.Windows.Forms
{
	public partial class MessageBox
	{
		public static DialogResult Show(IWin32Window owner, string text) => ShowCore(owner, text, null, MessageBoxButtons.OK, MessageBoxIcon.None, MessageBoxDefaultButton.Button1, 0, false);
		public static DialogResult Show(IWin32Window owner, string text, string caption) => ShowCore(owner, text, caption, MessageBoxButtons.OK, MessageBoxIcon.None, MessageBoxDefaultButton.Button1, 0, false);
		public static DialogResult Show(IWin32Window owner, string text, string caption, MessageBoxButtons buttons) => ShowCore(owner, text, caption, buttons, MessageBoxIcon.None, MessageBoxDefaultButton.Button1, 0, false);
		public static DialogResult Show(IWin32Window owner, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon) => ShowCore(owner, text, caption, buttons, icon, MessageBoxDefaultButton.Button1, 0, false);
		public static DialogResult Show(IWin32Window owner, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton) => ShowCore(owner, text, caption, buttons, icon, defaultButton, 0, false);
		public static DialogResult Show(IWin32Window owner, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton, MessageBoxOptions options) => ShowCore(owner, text, caption, buttons, icon, defaultButton, options, false);
		public static DialogResult Show(string text) => ShowCore(null, text, null, MessageBoxButtons.OK, MessageBoxIcon.None, MessageBoxDefaultButton.Button1, 0, false);
		public static DialogResult Show(string text, string caption) => ShowCore(null, text, caption, MessageBoxButtons.OK, MessageBoxIcon.None, MessageBoxDefaultButton.Button1, 0, false);
		public static DialogResult Show(string text, string caption, MessageBoxButtons buttons) => ShowCore(null, text, caption, buttons, MessageBoxIcon.None, MessageBoxDefaultButton.Button1, 0, false);
		public static DialogResult Show(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon) => ShowCore(null, text, caption, buttons, icon, MessageBoxDefaultButton.Button1, 0, false);
		public static DialogResult Show(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton) => ShowCore(null, text, caption, buttons, icon, defaultButton, 0, false);
		public static DialogResult Show(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton, MessageBoxOptions options) => ShowCore(null, text, caption, buttons, icon, defaultButton, options, false);
		public static DialogResult Show(IWin32Window owner, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton, MessageBoxOptions options, string helpFilePath) => ShowCore(owner, text, caption, buttons, icon, defaultButton, options, true);
		public static DialogResult Show(IWin32Window owner, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton, MessageBoxOptions options, string helpFilePath, HelpNavigator navigator) => ShowCore(owner, text, caption, buttons, icon, defaultButton, options, true);
		public static DialogResult Show(IWin32Window owner, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton, MessageBoxOptions options, string helpFilePath, HelpNavigator navigator, object param) => ShowCore(owner, text, caption, buttons, icon, defaultButton, options, true);
		public static DialogResult Show(IWin32Window owner, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton, MessageBoxOptions options, string helpFilePath, string keyword) => ShowCore(owner, text, caption, buttons, icon, defaultButton, options, true);
		public static DialogResult Show(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton, MessageBoxOptions options, bool displayHelpButton) => ShowCore(null, text, caption, buttons, icon, defaultButton, options, displayHelpButton);
		public static DialogResult Show(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton, MessageBoxOptions options, string helpFilePath) => ShowCore(null, text, caption, buttons, icon, defaultButton, options, true);
		public static DialogResult Show(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton, MessageBoxOptions options, string helpFilePath, HelpNavigator navigator) => ShowCore(null, text, caption, buttons, icon, defaultButton, options, true);
		public static DialogResult Show(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton, MessageBoxOptions options, string helpFilePath, HelpNavigator navigator, object param) => ShowCore(null, text, caption, buttons, icon, defaultButton, options, true);
		public static DialogResult Show(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton, MessageBoxOptions options, string helpFilePath, string keyword) => ShowCore(null, text, caption, buttons, icon, defaultButton, options, true);

		private static DialogResult ShowCore(IWin32Window owner, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton, MessageBoxOptions options, bool showHelp)
		{
			if (!Enum.IsDefined(typeof(MessageBoxButtons), buttons))
			{
				throw new InvalidEnumArgumentException(nameof(buttons), (int)buttons, typeof(MessageBoxButtons));
			}
			if (!Enum.IsDefined(typeof(MessageBoxIcon), icon))
			{
				throw new InvalidEnumArgumentException(nameof(icon), (int)icon, typeof(MessageBoxIcon));
			}
			if (!Enum.IsDefined(typeof(MessageBoxDefaultButton), defaultButton))
			{
				throw new InvalidEnumArgumentException(nameof(defaultButton), (int)defaultButton, typeof(MessageBoxDefaultButton));
			}
			if (owner != null && (options & (MessageBoxOptions.ServiceNotification | MessageBoxOptions.DefaultDesktopOnly)) != 0)
			{
				throw new ArgumentException("Showing a service notification message box with an owner window is not a valid operation. Remove the owner from the call to MessageBox.Show().", nameof(options));
			}
			using (var dialog = new MessageBoxDialog(text ?? string.Empty, caption ?? string.Empty, buttons, icon, defaultButton, options, showHelp, owner != null))
			{
				DialogResult result = dialog.ShowDialog(owner);
				return result == DialogResult.None ? dialog.DefaultCloseResult : result;
			}
		}
	}

	/// <summary>
	/// The window of a MessageBox.Show call: a fixed dialog with the text and one button for each answer, in the order and with the default and cancel behaviour of the Win32 message
	/// box. Escape (and the close box) answers Cancel when there is a Cancel button and OK when OK is the only button; with other sets (Yes/No, Abort/Retry/Ignore) the window cannot
	/// be closed without an answer. It is not an open form of the application (<c>Application.OpenForms</c>) and does not become an owned form of its owner.
	/// </summary>
	internal sealed class MessageBoxDialog : Form
	{
		private const int Gap = 12;
		private const int ButtonGap = 6;
		private const int ButtonWidth = 75;
		private const int ButtonHeight = 23;
		private const int MinimumClientWidth = 160;

		private readonly bool userCanClose;
		private readonly DialogResult closeResult;
		private readonly List<Button> buttons = new List<Button>();

		public MessageBoxDialog(string message, string caption, MessageBoxButtons set, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton, MessageBoxOptions options, bool showHelp, bool hasOwner)
		{
			Message = message;
			BoxIcon = icon;
			Buttons = set;
			Options = options;
			Text = caption;
			FormBorderStyle = FormBorderStyle.FixedDialog;
			MinimizeBox = false;
			MaximizeBox = false;
			ShowInTaskbar = false;
			StartPosition = hasOwner ? FormStartPosition.CenterParent : FormStartPosition.CenterScreen;
			if ((options & MessageBoxOptions.RtlReading) != 0)
			{
				RightToLeft = RightToLeft.Yes;
			}
			DialogResult[] answers = Answers(set);
			Button cancel = null;
			Button ok = null;
			foreach (DialogResult answer in answers)
			{
				Button b = new Button { Text = AnswerText(answer), DialogResult = answer, Name = "Button" + (buttons.Count + 1) };
				buttons.Add(b);
				if (answer == DialogResult.Cancel)
				{
					cancel = b;
				}
				else if (answer == DialogResult.OK)
				{
					ok = b;
				}
			}
			if (showHelp)
			{
				// the help button shows no help and closes nothing
				buttons.Add(new Button { Text = AnswerText(DialogResult.None), Name = "Help" });
			}
			userCanClose = cancel != null || set == MessageBoxButtons.OK;
			closeResult = cancel != null ? DialogResult.Cancel : set == MessageBoxButtons.OK ? DialogResult.OK : answers[answers.Length - 1] == DialogResult.Ignore ? DialogResult.Abort : DialogResult.No;
			ControlBox = userCanClose;
			Arrange(message, options);
			int index = (int)defaultButton / 256;
			Button first = index < buttons.Count ? buttons[index] : buttons[0];
			AcceptButton = first;
			CancelButton = cancel ?? (set == MessageBoxButtons.OK ? ok : null);
			ActiveControl = first;
		}

		public string Message { get; }

		public MessageBoxIcon BoxIcon { get; }

		public MessageBoxButtons Buttons { get; }

		public MessageBoxOptions Options { get; }

		/// <summary>The buttons in the order they are shown (the help button last).</summary>
		public IReadOnlyList<Button> ButtonList => buttons;

		internal override DialogResult DefaultCloseResult => closeResult;

		internal override bool ListedInOpenForms => false;

		protected override void OnFormClosing(FormClosingEventArgs e)
		{
			if (e.CloseReason == CloseReason.UserClosing && !userCanClose)
			{
				e.Cancel = true;
			}
			base.OnFormClosing(e);
		}

		private void Arrange(string message, MessageBoxOptions options)
		{
			IFormsBackend backend = FormsRuntime.Backend;
			int maxText = Math.Min(500, Math.Max(200, backend.GetScreenBounds().Width * 6 / 10));
			Size text = backend.MeasureText(message, null, maxText);
			int buttonsWidth = buttons.Count * ButtonWidth + (buttons.Count - 1) * ButtonGap;
			int clientWidth = Math.Max(MinimumClientWidth, Math.Max(text.Width, buttonsWidth) + 2 * Gap);
			var label = new Label
			{
				Name = "Message",
				AutoSize = false,
				Text = message,
				Bounds = new Rectangle(Gap, Gap, clientWidth - 2 * Gap, text.Height),
				TextAlign = (options & MessageBoxOptions.RightAlign) != 0 ? ContentAlignment.TopRight : ContentAlignment.TopLeft
			};
			Controls.Add(label);
			int top = Gap + text.Height + Gap;
			int left = (clientWidth - buttonsWidth) / 2;
			foreach (Button b in buttons)
			{
				b.Bounds = new Rectangle(left, top, ButtonWidth, ButtonHeight);
				Controls.Add(b);
				left += ButtonWidth + ButtonGap;
			}
			ClientSize = new Size(clientWidth, top + ButtonHeight + Gap);
		}

		private static DialogResult[] Answers(MessageBoxButtons set)
		{
			switch (set)
			{
			case MessageBoxButtons.OKCancel:
				return new[] { DialogResult.OK, DialogResult.Cancel };
			case MessageBoxButtons.AbortRetryIgnore:
				return new[] { DialogResult.Abort, DialogResult.Retry, DialogResult.Ignore };
			case MessageBoxButtons.YesNoCancel:
				return new[] { DialogResult.Yes, DialogResult.No, DialogResult.Cancel };
			case MessageBoxButtons.YesNo:
				return new[] { DialogResult.Yes, DialogResult.No };
			case MessageBoxButtons.RetryCancel:
				return new[] { DialogResult.Retry, DialogResult.Cancel };
			case MessageBoxButtons.CancelTryContinue:
				return new[] { DialogResult.Cancel, DialogResult.TryAgain, DialogResult.Continue };
			default:
				return new[] { DialogResult.OK };
			}
		}

		private static string AnswerText(DialogResult answer)
		{
			switch (answer)
			{
			case DialogResult.OK:
				return "OK";
			case DialogResult.Cancel:
				return "Cancel";
			case DialogResult.Abort:
				return "Abort";
			case DialogResult.Retry:
				return "Retry";
			case DialogResult.Ignore:
				return "Ignore";
			case DialogResult.Yes:
				return "Yes";
			case DialogResult.No:
				return "No";
			case DialogResult.TryAgain:
				return "Try Again";
			case DialogResult.Continue:
				return "Continue";
			default:
				return "Help";
			}
		}
	}
}
