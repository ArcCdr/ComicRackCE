using System;
using System.Collections.Generic;
using System.ComponentModel;
using cYo.Compat.Forms;

// The modal half of System.Windows.Forms.Form (P4.9f, MACOS_NATIVE_PATH.md 5.9.4): ShowDialog, a DialogResult that closes the dialog, FormClosing that cancels, closing while an
// inner dialog is open, the keys of the dialog (Enter presses the default button, Escape the cancel button) and the closing of an application. It follows Form.ShowDialog,
// Form.CheckCloseDialog and Form.WmClose of dotnet/winforms (the points taken from memory of that source, not from a Windows run, are listed in the facts of P4.9f).
//
// The loop itself belongs to the backend (IFormsBackend.RunModal): ShowDialog shows the window, asks the backend to pump until ModalLoop.Exit, and returns the answer. What the
// Framework does when the loop looks at the dialog (Form.CheckCloseDialog) is a post to the backend queue here, so that it runs when the code that set DialogResult has returned
// to the loop, and an inner dialog holds the check of the outer one back until it has ended (the outer loop cannot look before it is running again). Everything runs on the UI
// thread; a script thread that calls ShowDialog waits in IFormsBackend.Invoke for the whole dialog.
namespace System.Windows.Forms
{
	public partial class Form
	{
		/// <summary>The dialogs whose loop runs, the innermost last. Only the UI thread changes it.</summary>
		private static readonly List<Form> modalForms = new List<Form>();

		// read by other threads (a script thread that sets DialogResult)
		private volatile bool modal;
		private volatile bool modalDone;
		private volatile ModalLoop modalLoop;
		private Form modalPeerOwner;
		private bool calledClosing;
		private CloseReason closeReason;
		private IButtonControl defaultButton;

		/// <summary>True while the form is shown by <see cref="ShowDialog()"/>.</summary>
		public bool Modal => modal;

		/// <summary>The answer of a dialog that was closed without one (the close box, a loop that ended, hiding): Cancel; a message box chooses its own.</summary>
		internal virtual DialogResult DefaultCloseResult => DialogResult.Cancel;

		/// <summary>False for a window that is not a Form of the script in the Framework (the message box): it is not in <c>Application.OpenForms</c> and does not become the owner's owned form.</summary>
		internal virtual bool ListedInOpenForms => true;

		internal static void ResetModalState()
		{
			lock (modalForms)
			{
				modalForms.Clear();
			}
		}

		internal static Form[] ModalForms()
		{
			lock (modalForms)
			{
				return modalForms.ToArray();
			}
		}

		private static Form TopModalForm()
		{
			lock (modalForms)
			{
				return modalForms.Count == 0 ? null : modalForms[modalForms.Count - 1];
			}
		}

		// ---------------------------------------------------------------- ShowDialog

		public DialogResult ShowDialog() => ShowDialog(null);

		/// <summary>
		/// Shows the form as a dialog and returns its <see cref="DialogResult"/> when it has closed. The form is hidden, not disposed, when the dialog ends, and can be shown again (Load is
		/// raised again). From a script thread the dialog runs on the UI thread and the caller waits for it. <paramref name="owner"/> is the form the dialog belongs to (it becomes the
		/// form's Owner while the dialog runs); without one the form that is active now owns the window.
		/// </summary>
		public DialogResult ShowDialog(IWin32Window owner)
		{
			IFormsBackend backend = FormsRuntime.Backend;
			if (!backend.CheckAccess())
			{
				DialogResult result = DialogResult.None;
				backend.Invoke(() => result = ShowDialogOnUi(owner));
				return result;
			}
			return ShowDialogOnUi(owner);
		}

		private DialogResult ShowDialogOnUi(IWin32Window ownerWindow)
		{
			if (IsDisposed)
			{
				throw new ObjectDisposedException(GetType().Name);
			}
			if (ownerWindow == this)
			{
				throw new ArgumentException("A form cannot own itself or its owner.", "owner");
			}
			if (Visible)
			{
				throw new InvalidOperationException("Forms that are already visible cannot be displayed as a modal dialog. Set the form's visible property to false before calling ShowDialog.");
			}
			if (!Enabled)
			{
				throw new InvalidOperationException("Forms that are not enabled cannot be displayed as a modal dialog. Set the form's enabled property to true before calling ShowDialog.");
			}
			if (!TopLevel)
			{
				throw new InvalidOperationException("Forms that are not top level forms cannot be displayed as a modal dialog. Remove the form from any parent form before calling ShowDialog.");
			}
			Form ownerForm = null;
			if (ownerWindow != null)
			{
				ownerForm = ownerWindow as Form ?? (ownerWindow as Control)?.FindForm();
				if (ownerForm == null)
				{
					throw new NotSupportedException("An owner that is not a form is not supported on macOS.");
				}
				if (ownerForm == this)
				{
					throw new ArgumentException("A form cannot own itself or its owner.", "owner");
				}
			}
			Form oldOwner = owner;
			Form previousActive = ActiveForm;
			bool takesOwner = ownerForm != null && ownerForm != oldOwner && ListedInOpenForms;
			if (takesOwner)
			{
				Owner = ownerForm;
			}
			Form peerOwner = ownerForm ?? oldOwner ?? previousActive;
			if (peerOwner == this || (peerOwner != null && (peerOwner.IsDisposed || !peerOwner.IsHandleCreated)))
			{
				peerOwner = null;
			}
			modalPeerOwner = peerOwner;
			calledOnLoad = false;
			calledMakeVisible = false;
			closeReason = CloseReason.None;
			calledClosing = false;
			modalDone = false;
			dialogResult = DialogResult.None;
			var loop = new ModalLoop();
			modalLoop = loop;
			modal = true;
			lock (modalForms)
			{
				modalForms.Add(this);
			}
			Application.RaiseEnterThreadModal();
			try
			{
				Visible = true;
				if (!IsDisposed && modal && GetVisibleCore())
				{
					if (dialogResult != DialogResult.None)
					{
						// the answer was given in Load, before the window was there
						PostModalContinue();
					}
					FormsRuntime.Backend.RunModal(loop);
				}
			}
			finally
			{
				EndDialog(takesOwner, oldOwner, ownerForm, previousActive);
			}
			return dialogResult;
		}

		private void EndDialog(bool tookOwner, Form oldOwner, Form ownerForm, Form previousActive)
		{
			try
			{
				if (!IsDisposed && GetVisibleCore())
				{
					// The loop ended without a close request: nobody could answer (the null backend with nothing queued) or the application exited.
					if (dialogResult == DialogResult.None)
					{
						dialogResult = DefaultCloseResult;
					}
					Visible = false;
				}
			}
			finally
			{
				modal = false;
				modalLoop = null;
				modalPeerOwner = null;
				lock (modalForms)
				{
					modalForms.Remove(this);
				}
				if (tookOwner && !IsDisposed)
				{
					Owner = oldOwner;
				}
				Application.RaiseLeaveThreadModal();
				Form next = Reachable(previousActive) ? previousActive : (Reachable(ownerForm) ? ownerForm : null);
				next?.Activate();
				// an outer dialog that was answered while this one ran can look at its answer now
				Form top = TopModalForm();
				if (top != null && top.dialogResult != DialogResult.None)
				{
					top.PostModalContinue();
				}
			}
		}

		private bool Reachable(Form form) => form != null && form != this && !form.IsDisposed && form.IsHandleCreated && form.Visible;

		// ---------------------------------------------------------------- the loop looks at the dialog

		/// <summary>Posts the look at the dialog (CheckCloseDialog of the loop in the Framework): it runs when the code that asked has returned to the loop.</summary>
		private void PostModalContinue()
		{
			FormsRuntime.Backend.Post(() => FormsRuntime.Guard(ContinueModal));
		}

		private void ContinueModal()
		{
			if (!modal || modalDone || IsDisposed || !GetVisibleCore())
			{
				return;
			}
			if (!ReferenceEquals(TopModalForm(), this))
			{
				// an inner dialog is open: this loop is not running; EndDialog of the inner one posts this again
				return;
			}
			if (CheckCloseDialog(closingOnly: false))
			{
				modalDone = true;
				Visible = false;
			}
		}

		/// <summary>
		/// Form.CheckCloseDialog: false while the dialog has no answer and is visible. Otherwise Closing and FormClosing are raised once (a cancel resets the answer to None and keeps
		/// the dialog open) and, unless <paramref name="closingOnly"/>, FormClosed follows. True when the dialog has an answer that no handler took back.
		/// </summary>
		private bool CheckCloseDialog(bool closingOnly)
		{
			if (dialogResult == DialogResult.None && GetVisibleCore())
			{
				return false;
			}
			try
			{
				if (!calledClosing)
				{
					var closing = new FormClosingEventArgs(closeReason, false);
					OnClosing(closing);
					OnFormClosing(closing);
					if (closing.Cancel)
					{
						// the close did not happen: the next one has its own reason
						dialogResult = DialogResult.None;
						closeReason = CloseReason.None;
					}
					else
					{
						calledClosing = true;
					}
				}
				if (!closingOnly && dialogResult != DialogResult.None)
				{
					var closed = new FormClosedEventArgs(closeReason);
					OnClosed(closed);
					OnFormClosed(closed);
					calledClosing = false;
				}
			}
			catch (Exception e)
			{
				dialogResult = DialogResult.None;
				calledClosing = false;
				FormsRuntime.Report(e);
			}
			return dialogResult != DialogResult.None;
		}

		/// <summary>WM_CLOSE of a dialog (Close, the close box): Cancel is the answer when there is none; the end of the dialog follows when no handler cancelled.</summary>
		private bool CloseDialog()
		{
			if (dialogResult == DialogResult.None)
			{
				dialogResult = DefaultCloseResult;
			}
			calledClosing = false;
			bool accepted = CheckCloseDialog(closingOnly: true);
			if (accepted)
			{
				PostModalContinue();
			}
			return accepted;
		}

		// ---------------------------------------------------------------- application exit

		/// <summary>Application.Exit asks every open form: FormClosing with ApplicationExitCall. True when one cancelled.</summary>
		internal bool RaiseFormClosingOnAppExit()
		{
			var closing = new FormClosingEventArgs(CloseReason.ApplicationExitCall, false);
			if (!calledClosing)
			{
				OnClosing(closing);
				OnFormClosing(closing);
				if (!closing.Cancel)
				{
					calledClosing = true;
				}
			}
			return closing.Cancel;
		}

		/// <summary>Application.Exit: FormClosed with ApplicationExitCall, without disposing the form.</summary>
		internal void RaiseFormClosedOnAppExit()
		{
			var closed = new FormClosedEventArgs(CloseReason.ApplicationExitCall);
			OnClosed(closed);
			OnFormClosed(closed);
			calledClosing = false;
		}

		/// <summary>Ends the loop of this dialog (Application.Exit, ExitThread); ShowDialog then closes the window.</summary>
		internal void ExitDialogLoop() => modalLoop?.Exit();

		// ---------------------------------------------------------------- the default button and the keys of a dialog

		/// <summary>The button that Enter presses now: the focused button, otherwise AcceptButton.</summary>
		internal IButtonControl DefaultButton => defaultButton;

		/// <summary>The default button follows the focus: a button that has the focus is the default button, otherwise the AcceptButton is (ContainerControl.UpdateDefaultButton).</summary>
		protected override void UpdateDefaultButton()
		{
			Control focus = FocusedControl;
			IButtonControl candidate = focus as IButtonControl;
			SetDefaultButton(candidate != null && Contains(focus) ? candidate : acceptButton);
		}

		internal void RefreshDefaultButton() => UpdateDefaultButton();

		private void SetDefaultButton(IButtonControl button)
		{
			IButtonControl old = defaultButton;
			if (ReferenceEquals(old, button))
			{
				return;
			}
			defaultButton = button;
			old?.NotifyDefault(false);
			button?.NotifyDefault(true);
		}

		/// <summary>Enter presses the default button, Escape the cancel button (Form.ProcessDialogKey); a key with Ctrl or Alt is not a dialog key.</summary>
		protected override bool ProcessDialogKey(Keys keyData)
		{
			if ((keyData & (Keys.Alt | Keys.Control)) == Keys.None)
			{
				switch (keyData & Keys.KeyCode)
				{
				case Keys.Return:
					UpdateDefaultButton();
					IButtonControl accept = defaultButton;
					if (accept != null)
					{
						if (accept is Control)
						{
							accept.PerformClick();
						}
						return true;
					}
					break;
				case Keys.Escape:
					IButtonControl cancel = cancelButton;
					if (cancel != null)
					{
						cancel.PerformClick();
						return true;
					}
					break;
				}
			}
			return base.ProcessDialogKey(keyData);
		}
	}
}
