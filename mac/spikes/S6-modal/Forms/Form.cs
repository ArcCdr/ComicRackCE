using System;
using System.ComponentModel;
using System.Drawing;
using System.Threading;

namespace System.Windows.Forms
{
	public enum DialogResult
	{
		None = 0,
		OK = 1,
		Cancel = 2,
		Abort = 3,
		Retry = 4,
		Ignore = 5,
		Yes = 6,
		No = 7,
		TryAgain = 10,
		Continue = 11
	}

	public enum FormBorderStyle
	{
		None = 0,
		FixedSingle = 1,
		Fixed3D = 2,
		FixedDialog = 3,
		Sizable = 4,
		FixedToolWindow = 5,
		SizableToolWindow = 6
	}

	public enum FormStartPosition
	{
		Manual = 0,
		CenterScreen = 1,
		WindowsDefaultLocation = 2,
		WindowsDefaultBounds = 3,
		CenterParent = 4
	}

	public enum AutoScaleMode
	{
		None = 0,
		Font = 1,
		Dpi = 2,
		Inherit = 3
	}

	public enum CloseReason
	{
		None = 0,
		WindowsShutDown = 1,
		MdiFormClosing = 2,
		UserClosing = 3,
		TaskManagerClosing = 4,
		FormOwnerClosing = 5,
		ApplicationExitCall = 6
	}

	public delegate void FormClosingEventHandler(object sender, FormClosingEventArgs e);

	public delegate void FormClosedEventHandler(object sender, FormClosedEventArgs e);

	public class FormClosingEventArgs : CancelEventArgs
	{
		public CloseReason CloseReason
		{
			get;
		}

		public FormClosingEventArgs(CloseReason closeReason, bool cancel)
			: base(cancel)
		{
			CloseReason = closeReason;
		}
	}

	public class FormClosedEventArgs : EventArgs
	{
		public CloseReason CloseReason
		{
			get;
		}

		public FormClosedEventArgs(CloseReason closeReason)
		{
			CloseReason = closeReason;
		}
	}

	/// <summary>
	/// The facade of System.Windows.Forms.Form. ShowDialog runs a nested loop of the backend on the UI thread (from another thread it is marshalled there and
	/// the calling thread blocks), closes through the WinForms rules (FormClosing can cancel, a button DialogResult or Form.DialogResult closes a modal form).
	/// </summary>
	public class Form : Control
	{
		private enum State
		{
			Hidden,
			Showing,
			Closing
		}

		private State state;
		private bool modal;
		private bool loaded;
		private DialogResult dialogResult;
		private IModalLoop loop;
		private Form owner;
		private IButtonControl acceptButton;
		private IButtonControl cancelButton;
		private bool closeFinished;

		public Form()
		{
			Visible = false;
			Size = new Size(300, 300);
		}

		public DialogResult DialogResult
		{
			get => dialogResult;
			set
			{
				dialogResult = value;
				if (value != DialogResult.None && modal && state == State.Showing)
				{
					Close();
					if (state != State.Hidden && dialogResult != DialogResult.None && !closeFinished)
					{
						// FormClosing was cancelled: the result is reset as in WinForms.
						dialogResult = DialogResult.None;
					}
				}
			}
		}

		public IButtonControl AcceptButton
		{
			get => acceptButton;
			set => acceptButton = value;
		}

		public IButtonControl CancelButton
		{
			get => cancelButton;
			set => cancelButton = value;
		}

		public FormBorderStyle FormBorderStyle
		{
			get;
			set;
		} = FormBorderStyle.Sizable;

		public bool MaximizeBox
		{
			get;
			set;
		} = true;

		public bool MinimizeBox
		{
			get;
			set;
		} = true;

		public bool ShowIcon
		{
			get;
			set;
		} = true;

		public bool ShowInTaskbar
		{
			get;
			set;
		} = true;

		public bool TopMost
		{
			get;
			set;
		}

		public FormStartPosition StartPosition
		{
			get;
			set;
		} = FormStartPosition.WindowsDefaultLocation;

		public AutoScaleMode AutoScaleMode
		{
			get;
			set;
		} = AutoScaleMode.Inherit;

		public SizeF AutoScaleDimensions
		{
			get;
			set;
		}

		public override bool AutoSize
		{
			get => base.AutoSize;
			set => base.AutoSize = value;
		}

		public bool Modal => modal;

		public Form Owner
		{
			get => owner;
			set => owner = value;
		}

		/// <summary>The result of ShowDialog when the form was closed without a button (the X): Cancel for forms; a message box with only OK returns OK.</summary>
		public DialogResult DefaultCloseResult
		{
			get;
			set;
		} = DialogResult.Cancel;

		/// <summary>Set by hosts for dialogs whose close box must not work (a Yes/No message box).</summary>
		public bool UserCloseAllowed
		{
			get;
			set;
		} = true;

		public bool IsShown => state != State.Hidden;

		public event EventHandler Load;

		public event EventHandler Shown;

		public event EventHandler Activated;

		public event FormClosingEventHandler FormClosing;

		public event FormClosedEventHandler FormClosed;

		protected virtual void OnLoad(EventArgs e)
		{
			Load?.Invoke(this, e);
		}

		protected virtual void OnShown(EventArgs e)
		{
			Shown?.Invoke(this, e);
		}

		protected virtual void OnActivated(EventArgs e)
		{
			Activated?.Invoke(this, e);
		}

		protected virtual void OnFormClosing(FormClosingEventArgs e)
		{
			FormClosing?.Invoke(this, e);
		}

		protected virtual void OnFormClosed(FormClosedEventArgs e)
		{
			FormClosed?.Invoke(this, e);
		}

		// ---------------------------------------------------------------- showing

		public void Show()
		{
			Show(null);
		}

		public void Show(IWin32Window ownerWindow)
		{
			if (!FormsRuntime.Backend.CheckAccess())
			{
				FormsRuntime.Backend.Invoke(() => Show(ownerWindow));
				return;
			}
			if (state != State.Hidden)
			{
				Visible = true;
				return;
			}
			Open(ownerWindow, isModal: false);
		}

		public void Hide()
		{
			Visible = false;
		}

		public void Activate()
		{
		}

		public DialogResult ShowDialog()
		{
			return ShowDialog(null);
		}

		public DialogResult ShowDialog(IWin32Window ownerWindow)
		{
			IFormsBackend backend = FormsRuntime.Backend;
			if (!backend.CheckAccess())
			{
				// A form shown from a script thread: the loop runs on the UI thread and this thread waits for the result (section 5.9.4).
				DialogResult result = DialogResult.None;
				backend.Invoke(() => result = ShowDialog(ownerWindow));
				return result;
			}
			if (IsDisposed)
			{
				throw new ObjectDisposedException(GetType().Name);
			}
			if (state != State.Hidden)
			{
				throw new InvalidOperationException("Form that is already visible cannot be displayed as a modal dialog. Set the form's visible property to false before calling showDialog.");
			}
			dialogResult = DialogResult.None;
			IModalLoop myLoop = backend.CreateModalLoop();
			loop = myLoop;
			Open(ownerWindow, isModal: true);
			myLoop.Run();
			loop = null;
			return dialogResult;
		}

		private void Open(IWin32Window ownerWindow, bool isModal)
		{
			IFormsBackend backend = FormsRuntime.Backend;
			owner = ownerWindow as Form ?? (ownerWindow as Control)?.FindForm() ?? owner;
			modal = isModal;
			closeFinished = false;
			state = State.Showing;
			FormsRuntime.Opened(this);
			try
			{
				if (!loaded)
				{
					loaded = true;
					PerformLayout();
					OnLoad(EventArgs.Empty);
				}
				IFormPeer peer = (IFormPeer)EnsurePeer();
				IFormPeer ownerPeer = owner?.Peer as IFormPeer;
				bool wasVisible = Visible;
				Visible = true;
				if (isModal)
				{
					peer.ShowDialog(ownerPeer);
				}
				else
				{
					peer.Show(ownerPeer);
				}
				if (!wasVisible)
				{
					// VisibleChanged was raised by the property setter.
				}
				OnActivated(EventArgs.Empty);
				OnShown(EventArgs.Empty);
			}
			catch
			{
				state = State.Hidden;
				modal = false;
				FormsRuntime.Closed(this);
				throw;
			}
		}

		// ---------------------------------------------------------------- closing

		/// <summary>Form.Close(): FormClosing can cancel; a modal form is hidden and its ShowDialog returns, a modeless one is disposed.</summary>
		public void Close()
		{
			IFormsBackend backend = FormsRuntime.Backend;
			if (!backend.CheckAccess())
			{
				backend.Invoke(Close);
				return;
			}
			if (state != State.Showing)
			{
				return;
			}
			if (!AskClosing(CloseReason.None))
			{
				return;
			}
			Finish(CloseReason.None, peerAlreadyClosing: false);
		}

		/// <summary>The window's close button (peer): returns true when the close must be cancelled.</summary>
		public bool PeerClosing(CloseReason reason)
		{
			if (state != State.Showing)
			{
				return false;
			}
			if (!UserCloseAllowed && dialogResult == DialogResult.None)
			{
				return true;
			}
			bool cancel = false;
			FormsRuntime.Guard(() => cancel = !AskClosing(reason));
			return cancel;
		}

		/// <summary>The window has closed (peer): finish the facade side.</summary>
		public void PeerClosed()
		{
			if (state == State.Showing || state == State.Closing)
			{
				Finish(CloseReason.UserClosing, peerAlreadyClosing: true);
			}
		}

		/// <summary>Enter on the dialog: clicks the accept button. Escape: the cancel button.</summary>
		public void PeerKey(bool enter)
		{
			IButtonControl b = enter ? acceptButton : cancelButton;
			if (b != null && ((Control)b).Enabled)
			{
				FormsRuntime.Guard(b.PerformClick);
			}
		}

		private bool AskClosing(CloseReason reason)
		{
			FormClosingEventArgs e = new FormClosingEventArgs(reason, false);
			try
			{
				OnFormClosing(e);
			}
			catch (Exception ex)
			{
				Application.OnThreadException(ex);
			}
			if (e.Cancel)
			{
				dialogResult = DialogResult.None;
				return false;
			}
			return true;
		}

		private void Finish(CloseReason reason, bool peerAlreadyClosing)
		{
			if (closeFinished)
			{
				return;
			}
			closeFinished = true;
			state = State.Closing;
			if (modal && dialogResult == DialogResult.None)
			{
				dialogResult = DefaultCloseResult;
			}
			IFormPeer peer = Peer as IFormPeer;
			if (peer != null && !peerAlreadyClosing)
			{
				peer.Hide();
			}
			state = State.Hidden;
			bool wasModal = modal;
			modal = false;
			FormsRuntime.Closed(this);
			Visible = false;
			try
			{
				OnFormClosed(new FormClosedEventArgs(reason));
			}
			catch (Exception ex)
			{
				Application.OnThreadException(ex);
			}
			loop?.Exit();
			if (!wasModal)
			{
				Dispose();
			}
			else
			{
				DiscardPeers();
			}
		}

		protected override void Dispose(bool disposing)
		{
			if (state != State.Hidden && !closeFinished)
			{
				// Disposed while shown (even inside its own ShowDialog): close without events and release the loop.
				closeFinished = true;
				state = State.Hidden;
				bool wasModal = modal;
				modal = false;
				if (wasModal && dialogResult == DialogResult.None)
				{
					dialogResult = DialogResult.Cancel;
				}
				FormsRuntime.Closed(this);
				(Peer as IFormPeer)?.Hide();
				loop?.Exit();
			}
			base.Dispose(disposing);
		}

		public override void PeerStateChanged(string property, object value)
		{
			if (property == "Size" && value is Size s)
			{
				ClientSize = s;
				return;
			}
			base.PeerStateChanged(property, value);
		}
	}

	public enum MessageBoxButtons
	{
		OK = 0,
		OKCancel = 1,
		AbortRetryIgnore = 2,
		YesNoCancel = 3,
		YesNo = 4,
		RetryCancel = 5,
		CancelTryContinue = 6
	}

	public enum MessageBoxIcon
	{
		None = 0,
		Error = 16,
		Hand = 16,
		Stop = 16,
		Question = 32,
		Exclamation = 48,
		Warning = 48,
		Asterisk = 64,
		Information = 64
	}

	/// <summary>MessageBox.Show: blocking, returns the DialogResult. Built from the facade's own Form and Button (same nested loop as every other dialog).</summary>
	public static class MessageBox
	{
		public static DialogResult Show(string text)
		{
			return Show(null, text, string.Empty, MessageBoxButtons.OK, MessageBoxIcon.None);
		}

		public static DialogResult Show(string text, string caption)
		{
			return Show(null, text, caption, MessageBoxButtons.OK, MessageBoxIcon.None);
		}

		public static DialogResult Show(string text, string caption, MessageBoxButtons buttons)
		{
			return Show(null, text, caption, buttons, MessageBoxIcon.None);
		}

		public static DialogResult Show(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
		{
			return Show(null, text, caption, buttons, icon);
		}

		public static DialogResult Show(IWin32Window owner, string text)
		{
			return Show(owner, text, string.Empty, MessageBoxButtons.OK, MessageBoxIcon.None);
		}

		public static DialogResult Show(IWin32Window owner, string text, string caption, MessageBoxButtons buttons)
		{
			return Show(owner, text, caption, buttons, MessageBoxIcon.None);
		}

		public static DialogResult Show(IWin32Window owner, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
		{
			IFormsBackend backend = FormsRuntime.Backend;
			DialogResult result = DialogResult.None;
			if (!backend.CheckAccess())
			{
				backend.Invoke(() => result = Show(owner, text, caption, buttons, icon));
				return result;
			}
			DialogResult[] results;
			switch (buttons)
			{
				case MessageBoxButtons.OKCancel:
					results = new[] { DialogResult.OK, DialogResult.Cancel };
					break;
				case MessageBoxButtons.AbortRetryIgnore:
					results = new[] { DialogResult.Abort, DialogResult.Retry, DialogResult.Ignore };
					break;
				case MessageBoxButtons.YesNoCancel:
					results = new[] { DialogResult.Yes, DialogResult.No, DialogResult.Cancel };
					break;
				case MessageBoxButtons.YesNo:
					results = new[] { DialogResult.Yes, DialogResult.No };
					break;
				case MessageBoxButtons.RetryCancel:
					results = new[] { DialogResult.Retry, DialogResult.Cancel };
					break;
				case MessageBoxButtons.CancelTryContinue:
					results = new[] { DialogResult.Cancel, DialogResult.TryAgain, DialogResult.Continue };
					break;
				default:
					results = new[] { DialogResult.OK };
					break;
			}
			using (Form form = new Form())
			{
				form.Name = "MessageBox";
				form.Text = caption ?? string.Empty;
				form.FormBorderStyle = FormBorderStyle.FixedDialog;
				form.MinimizeBox = false;
				form.MaximizeBox = false;
				form.ShowInTaskbar = false;
				form.StartPosition = FormStartPosition.CenterParent;
				Size textSize = backend.MeasureText(text ?? string.Empty, null, 320);
				int buttonRow = 8 + results.Length * 83;
				int width = Math.Max(Math.Max(textSize.Width + 40, buttonRow + 8), 160);
				int height = Math.Max(textSize.Height + 24 + 23 + 24, 110);
				form.ClientSize = new Size(width, height);
				Label label = new Label { Name = "text", Text = text ?? string.Empty, AutoSize = false, Location = new Point(20, 14), Size = new Size(width - 40, textSize.Height + 2) };
				form.Controls.Add(label);
				int x = width - 8 - results.Length * 83 + 8;
				bool hasCancel = Array.IndexOf(results, DialogResult.Cancel) >= 0;
				for (int i = 0; i < results.Length; i++)
				{
					Button b = new Button { Name = "button" + results[i], Text = results[i] == DialogResult.TryAgain ? "Try Again" : results[i].ToString(), DialogResult = results[i], Location = new Point(x + i * 83, height - 23 - 10), Size = new Size(75, 23) };
					form.Controls.Add(b);
					if (i == 0)
					{
						form.AcceptButton = b;
					}
					if (results[i] == DialogResult.Cancel || (results.Length == 1 && results[0] == DialogResult.OK))
					{
						form.CancelButton = b;
					}
				}
				form.UserCloseAllowed = hasCancel || results.Length == 1;
				if (results.Length == 1)
				{
					form.DefaultCloseResult = results[0];
				}
				result = form.ShowDialog(owner);
			}
			return result;
		}
	}
}
