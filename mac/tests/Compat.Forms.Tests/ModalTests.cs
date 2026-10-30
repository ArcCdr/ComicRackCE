using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using cYo.Compat.Forms;
using Xunit;

namespace Compat.Forms.Tests
{
	/// <summary>
	/// Modal semantics of the facade (P4.9f): ShowDialog and the DialogResult that closes a dialog, FormClosing that cancels, Close during a dialog, nested dialogs and what happens
	/// when an outer one is answered or closed while an inner one runs, owners and OwnedForms, the keys of a dialog, and a dialog that a script thread shows. The UI thread is the
	/// test thread and its loop is <see cref="NullFormsBackend.RunModal"/>; a dialog is answered from a handler of the dialog (Shown) or from an action posted before ShowDialog,
	/// so no test depends on timing. The orders are those of Form.ShowDialog, CheckCloseDialog and WmClose of dotnet/winforms (P4.9g compares them with Windows traces).
	/// </summary>
	[Collection("Forms runtime")]
	public sealed class ModalTests : IDisposable
	{
		private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(60);

		private readonly NullFormsBackend backend;
		private readonly Trace trace = new Trace();
		private readonly List<Exception> workerErrors = new List<Exception>();

		public ModalTests()
		{
			FormsRuntime.Reset();
			backend = new NullFormsBackend { ThreadAffinity = true };
			backend.AdoptCurrentThread();
			FormsRuntime.Backend = backend;
		}

		public void Dispose()
		{
			FormsRuntime.Reset();
		}

		// ---------------------------------------------------------------- helpers

		private sealed class Dlg
		{
			public Form Form;
			public TextBox Text;
			public Button Ok;
			public Button Cancel;
		}

		/// <summary>A form with a text box and an OK and a Cancel button (AcceptButton and CancelButton); its events are traced as "name.Event".</summary>
		private Dlg NewDialog(string name, bool watch = true)
		{
			var d = new Dlg
			{
				Form = new Form { Name = name },
				Text = new TextBox { Name = name + ".text" },
				Ok = new Button { Name = name + ".ok", Text = "OK", DialogResult = DialogResult.OK },
				Cancel = new Button { Name = name + ".cancel", Text = "Cancel", DialogResult = DialogResult.Cancel }
			};
			d.Form.Controls.Add(d.Text);
			d.Form.Controls.Add(d.Ok);
			d.Form.Controls.Add(d.Cancel);
			d.Form.AcceptButton = d.Ok;
			d.Form.CancelButton = d.Cancel;
			if (watch)
			{
				trace.Watch(d.Form, name);
			}
			return d;
		}

		private static void On(Form f, Action act) => f.Shown += (s, e) => act();

		private NullPeer PeerOf(Control c) => backend.Peers.Last(p => p.Control == c);

		private static string Reason(string name, string ev, CloseReason r) => name + "." + ev + "(" + r + ")";

		// ---------------------------------------------------------------- ShowDialog and DialogResult

		[Fact]
		public void ShowDialog_returns_the_answer_of_the_button_and_hides_the_form_without_disposing_it()
		{
			Dlg d = NewDialog("dlg");
			bool modalInside = false;
			On(d.Form, () =>
			{
				modalInside = d.Form.Modal;
				d.Ok.PerformClick();
			});
			DialogResult result = d.Form.ShowDialog();
			Assert.Equal(DialogResult.OK, result);
			Assert.True(modalInside);
			Assert.False(d.Form.Modal);
			Assert.False(d.Form.Visible);
			Assert.False(d.Form.IsDisposed);
			Assert.Equal(DialogResult.OK, d.Form.DialogResult);
			// A dialog is closed through its answer: CloseReason.None; Load and Activated come before the loop, Shown is its first message.
			Assert.Equal(new[]
			{
				"dlg.Load", "dlg.Activated", "dlg.Shown", "dlg.Closing", "dlg.FormClosing(None)", "dlg.Closed", "dlg.FormClosed(None)", "dlg.Deactivate"
			}, trace.Entries);
			NullPeer peer = PeerOf(d.Form);
			Assert.Contains("ShowModal", peer.Log);
			Assert.DoesNotContain("Show", peer.Log);
			Assert.Contains("Hide", peer.Log);
			Assert.False(peer.Disposed);
			Assert.Equal(0, backend.ModalDepth);
			Assert.Null(Form.ActiveForm);
			d.Form.Dispose();
		}

		[Fact]
		public void A_dialog_can_be_shown_again_Load_comes_again_and_Shown_does_not()
		{
			Dlg d = NewDialog("dlg");
			On(d.Form, d.Ok.PerformClick);
			Assert.Equal(DialogResult.OK, d.Form.ShowDialog());
			trace.Clear();
			d.Text.Text = "kept";
			backend.Post(d.Cancel.PerformClick);
			Assert.Equal(DialogResult.Cancel, d.Form.ShowDialog());
			Assert.Equal(new[] { "dlg.Load", "dlg.Activated", "dlg.Closing", "dlg.FormClosing(None)", "dlg.Closed", "dlg.FormClosed(None)", "dlg.Deactivate" }, trace.Entries);
			Assert.Equal("kept", d.Text.Text);
			// the window (peer) stays; it is shown modally again
			Assert.Equal(1, backend.Peers.Count(p => p.Control == d.Form));
			Assert.Equal(2, PeerOf(d.Form).Log.Count(l => l == "ShowModal"));
		}

		[Fact]
		public void The_answer_is_reset_when_the_dialog_is_shown_again()
		{
			var f = new Form();
			f.DialogResult = DialogResult.Yes;
			backend.Post(() => Assert.Equal(DialogResult.None, f.DialogResult));
			Assert.Equal(DialogResult.Cancel, f.ShowDialog());
		}

		[Fact]
		public void A_value_set_in_a_handler_closes_the_dialog_when_the_handler_has_returned()
		{
			Dlg d = NewDialog("dlg");
			var plain = new Button { Name = "plain" };
			d.Form.Controls.Add(plain);
			plain.Click += (s, e) =>
			{
				trace.Add("handler: before");
				d.Form.DialogResult = DialogResult.Retry;
				trace.Add("handler: after, still open " + d.Form.Visible);
			};
			On(d.Form, plain.PerformClick);
			Assert.Equal(DialogResult.Retry, d.Form.ShowDialog());
			Assert.Equal(new[]
			{
				"dlg.Load", "dlg.Activated", "dlg.Shown", "handler: before", "handler: after, still open True", "dlg.Closing", "dlg.FormClosing(None)", "dlg.Closed", "dlg.FormClosed(None)", "dlg.Deactivate"
			}, trace.Entries);
		}

		[Fact]
		public void A_button_with_a_result_sets_it_before_Click_so_that_a_handler_can_take_it_back_and_keep_the_dialog_open()
		{
			Dlg d = NewDialog("dlg", watch: false);
			int clicks = 0;
			d.Ok.Click += (s, e) =>
			{
				Assert.Equal(DialogResult.OK, d.Form.DialogResult);
				if (++clicks == 1)
				{
					d.Form.DialogResult = DialogResult.None;
				}
			};
			On(d.Form, () =>
			{
				d.Ok.PerformClick();
				backend.Post(() =>
				{
					// the look at the dialog that the first click queued has run: nothing to close
					Assert.True(d.Form.Visible);
					trace.Add("open after the first click");
					d.Ok.PerformClick();
				});
			});
			Assert.Equal(DialogResult.OK, d.Form.ShowDialog());
			Assert.Equal(2, clicks);
			Assert.Equal(new[] { "open after the first click" }, trace.Entries);
		}

		[Fact]
		public void A_cancelled_FormClosing_keeps_the_dialog_open_and_takes_the_answer_back()
		{
			Dlg d = NewDialog("dlg");
			int attempts = 0;
			d.Form.FormClosing += (s, e) => e.Cancel = ++attempts == 1;
			On(d.Form, () =>
			{
				d.Ok.PerformClick();
				backend.Post(() =>
				{
					trace.Add("after the first attempt: answer " + d.Form.DialogResult + ", visible " + d.Form.Visible);
					d.Cancel.PerformClick();
				});
			});
			Assert.Equal(DialogResult.Cancel, d.Form.ShowDialog());
			Assert.Equal(2, attempts);
			Assert.Equal(new[]
			{
				"dlg.Load", "dlg.Activated", "dlg.Shown",
				"dlg.Closing", "dlg.FormClosing(None)",
				"after the first attempt: answer None, visible True",
				"dlg.Closing", "dlg.FormClosing(None)", "dlg.Closed", "dlg.FormClosed(None)", "dlg.Deactivate"
			}, trace.Entries);
		}

		[Fact]
		public void Close_on_a_dialog_answers_Cancel_with_UserClosing()
		{
			Dlg d = NewDialog("dlg");
			On(d.Form, d.Form.Close);
			Assert.Equal(DialogResult.Cancel, d.Form.ShowDialog());
			Assert.False(d.Form.IsDisposed);
			Assert.Equal(new[]
			{
				"dlg.Load", "dlg.Activated", "dlg.Shown", "dlg.Closing", Reason("dlg", "FormClosing", CloseReason.UserClosing), "dlg.Closed", Reason("dlg", "FormClosed", CloseReason.UserClosing), "dlg.Deactivate"
			}, trace.Entries);
		}

		[Fact]
		public void The_close_box_of_a_dialog_is_Close_and_a_cancelled_close_leaves_the_dialog_and_its_answer_alone()
		{
			Dlg d = NewDialog("dlg");
			int tries = 0;
			d.Form.FormClosing += (s, e) => e.Cancel = ++tries == 1;
			bool firstClosed = true;
			bool secondClosed = false;
			On(d.Form, () =>
			{
				firstClosed = d.Form.CloseByUser();
				trace.Add("after the first close: answer " + d.Form.DialogResult + ", visible " + d.Form.Visible);
				secondClosed = d.Form.CloseByUser();
			});
			Assert.Equal(DialogResult.Cancel, d.Form.ShowDialog());
			Assert.False(firstClosed);
			Assert.True(secondClosed);
			Assert.Contains("after the first close: answer None, visible True", trace.Entries);
		}

		[Fact]
		public void Hiding_a_dialog_ends_it_with_Cancel_and_raises_no_closing_events()
		{
			Dlg d = NewDialog("dlg");
			On(d.Form, d.Form.Hide);
			Assert.Equal(DialogResult.Cancel, d.Form.ShowDialog());
			Assert.Equal(new[] { "dlg.Load", "dlg.Activated", "dlg.Shown", "dlg.Deactivate" }, trace.Entries);
			Assert.False(d.Form.Visible);
			Assert.False(d.Form.IsDisposed);
		}

		[Fact]
		public void Hiding_a_dialog_that_has_an_answer_returns_the_answer()
		{
			Dlg d = NewDialog("dlg", watch: false);
			On(d.Form, () =>
			{
				d.Form.DialogResult = DialogResult.No;
				d.Form.Hide();
			});
			Assert.Equal(DialogResult.No, d.Form.ShowDialog());
		}

		[Fact]
		public void Disposing_a_dialog_from_its_own_loop_ends_it_and_ShowDialog_returns_what_was_set()
		{
			Dlg d = NewDialog("dlg", watch: false);
			On(d.Form, d.Form.Dispose);
			Assert.Equal(DialogResult.None, d.Form.ShowDialog());
			Assert.True(d.Form.IsDisposed);
			Assert.Equal(0, backend.ModalDepth);
			var e = NewDialog("e", watch: false);
			On(e.Form, () =>
			{
				e.Form.DialogResult = DialogResult.Abort;
				e.Form.Dispose();
			});
			Assert.Equal(DialogResult.Abort, e.Form.ShowDialog());
		}

		[Fact]
		public void A_dialog_that_nobody_answers_ends_with_Cancel_when_the_loop_has_nothing_to_run()
		{
			Dlg d = NewDialog("dlg");
			Assert.Equal(DialogResult.Cancel, d.Form.ShowDialog());
			Assert.Equal(new[] { "dlg.Load", "dlg.Activated", "dlg.Shown", "dlg.Deactivate" }, trace.Entries);
			Assert.False(d.Form.Visible);
		}

		[Fact]
		public void An_answer_set_in_Load_closes_a_dialog_at_once_and_a_dialog_that_is_shown_again_is_not_shown_at_all()
		{
			Dlg d = NewDialog("dlg");
			d.Form.Load += (s, e) => d.Form.DialogResult = DialogResult.Yes;
			Assert.Equal(DialogResult.Yes, d.Form.ShowDialog());
			// the answer is there before the loop looks at its first message: Shown, which is such a message, never comes
			Assert.Equal(new[]
			{
				"dlg.Load", "dlg.Activated", "dlg.Closing", "dlg.FormClosing(None)", "dlg.Closed", "dlg.FormClosed(None)", "dlg.Deactivate"
			}, trace.Entries);
			trace.Clear();
			int shows = PeerOf(d.Form).Log.Count(l => l == "ShowModal");
			Assert.Equal(DialogResult.Yes, d.Form.ShowDialog());
			Assert.Equal(new[] { "dlg.Load" }, trace.Entries);
			Assert.Equal(shows, PeerOf(d.Form).Log.Count(l => l == "ShowModal"));
			Assert.False(d.Form.Visible);
		}

		[Fact]
		public void An_exception_of_FormClosing_goes_to_ThreadException_and_the_dialog_stays_open()
		{
			Dlg d = NewDialog("dlg", watch: false);
			int tries = 0;
			d.Form.FormClosing += (s, e) =>
			{
				if (++tries == 1)
				{
					throw new InvalidOperationException("in FormClosing");
				}
			};
			var seen = new List<Exception>();
			ThreadExceptionEventHandler handler = (s, e) => seen.Add(e.Exception);
			Application.ThreadException += handler;
			try
			{
				On(d.Form, () =>
				{
					d.Ok.PerformClick();
					backend.Post(() =>
					{
						Assert.True(d.Form.Visible);
						Assert.Equal(DialogResult.None, d.Form.DialogResult);
						d.Ok.PerformClick();
					});
				});
				Assert.Equal(DialogResult.OK, d.Form.ShowDialog());
			}
			finally
			{
				Application.ThreadException -= handler;
			}
			Assert.Equal("in FormClosing", Assert.Single(seen).Message);
			Assert.Equal(2, tries);
		}

		[Fact]
		public void ShowDialog_refuses_a_form_that_cannot_be_a_dialog_and_stays_usable()
		{
			var shown = new Form();
			shown.Show();
			Assert.Throws<InvalidOperationException>(() => shown.ShowDialog());
			Assert.False(shown.Modal);
			var disabled = new Form { Enabled = false };
			Assert.Throws<InvalidOperationException>(() => disabled.ShowDialog());
			var child = new Form { TopLevel = false };
			Assert.Throws<InvalidOperationException>(() => child.ShowDialog());
			var self = new Form();
			Assert.Throws<ArgumentException>(() => self.ShowDialog(self));
			var disposed = new Form();
			disposed.Dispose();
			Assert.Throws<ObjectDisposedException>(() => disposed.ShowDialog());
			var f = new Form();
			Assert.Throws<NotSupportedException>(() => f.ShowDialog(new Win32Owner()));
			Assert.False(f.Modal);
			Assert.Equal(DialogResult.Cancel, f.ShowDialog());
		}

		private sealed class Win32Owner : IWin32Window
		{
			public IntPtr Handle => IntPtr.Zero;
		}

		[Fact]
		public void A_dialog_that_has_closed_is_no_longer_an_open_form_until_it_is_shown_again()
		{
			Dlg d = NewDialog("dlg", watch: false);
			bool listedInside = false;
			On(d.Form, () =>
			{
				listedInside = Application.OpenForms.Cast<Form>().Contains(d.Form);
				d.Ok.PerformClick();
			});
			Assert.Equal(DialogResult.OK, d.Form.ShowDialog());
			Assert.True(listedInside);
			// FormClosed removed it, as OnFormClosed does in the Framework; the form itself is still there
			Assert.DoesNotContain(d.Form, Application.OpenForms.Cast<Form>());
			bool listedAgain = false;
			backend.Post(() =>
			{
				listedAgain = Application.OpenForms.Cast<Form>().Contains(d.Form);
				d.Ok.PerformClick();
			});
			d.Form.ShowDialog();
			Assert.True(listedAgain);
			d.Form.Dispose();
			Assert.Empty(Application.OpenForms.Cast<Form>());
		}

		// ---------------------------------------------------------------- owners and activation

		[Fact]
		public void The_dialog_belongs_to_its_owner_while_it_runs_and_the_owner_is_active_again_afterwards()
		{
			Dlg main = NewDialog("main");
			main.Form.Show();
			backend.Pump();
			Dlg d = NewDialog("dlg");
			Form ownerInside = null;
			Form[] ownedInside = null;
			On(d.Form, () =>
			{
				ownerInside = d.Form.Owner;
				ownedInside = main.Form.OwnedForms;
				Assert.Same(PeerOf(main.Form), PeerOf(d.Form).LastOwner);
				d.Ok.PerformClick();
			});
			trace.Clear();
			Assert.Equal(DialogResult.OK, d.Form.ShowDialog(main.Form));
			Assert.Same(main.Form, ownerInside);
			Assert.Equal(new[] { d.Form }, ownedInside);
			Assert.Null(d.Form.Owner);
			Assert.Empty(main.Form.OwnedForms);
			Assert.Same(main.Form, Form.ActiveForm);
			Assert.Equal(new[]
			{
				"dlg.Load", "main.Deactivate", "dlg.Activated", "dlg.Shown", "dlg.Closing", "dlg.FormClosing(None)", "dlg.Closed", "dlg.FormClosed(None)", "dlg.Deactivate", "main.Activated"
			}, trace.Entries);
		}

		[Fact]
		public void Without_an_owner_the_active_form_owns_the_window_and_the_owner_of_ShowDialog_replaces_the_old_one_only_while_it_runs()
		{
			Dlg main = NewDialog("main", watch: false);
			main.Form.Show();
			backend.Pump();
			Dlg d = NewDialog("dlg", watch: false);
			On(d.Form, () =>
			{
				Assert.Same(main.Form, PeerOf(d.Form).LastOwner);
				Assert.Null(d.Form.Owner);
				d.Ok.PerformClick();
			});
			d.Form.ShowDialog();
			Dlg other = NewDialog("other", watch: false);
			Dlg e = NewDialog("e", watch: false);
			e.Form.Owner = other.Form;
			On(e.Form, () =>
			{
				Assert.Same(main.Form, e.Form.Owner);
				Assert.Empty(other.Form.OwnedForms);
				e.Ok.PerformClick();
			});
			e.Form.ShowDialog(main.Form);
			Assert.Same(other.Form, e.Form.Owner);
			Assert.Equal(new[] { e.Form }, other.Form.OwnedForms);
		}

		// ---------------------------------------------------------------- the keys of a dialog

		[Fact]
		public void Enter_presses_the_accept_button_from_a_text_box_and_Escape_the_cancel_button()
		{
			Dlg d = NewDialog("dlg", watch: false);
			d.Form.Show();
			backend.Pump();
			d.Text.Focus();
			d.Ok.Click += (s, e) => trace.Add("ok");
			d.Cancel.Click += (s, e) => trace.Add("cancel");
			Assert.True(d.Text.PeerPreProcessKey(Keys.Return));
			Assert.True(d.Text.PeerPreProcessKey(Keys.Escape));
			Assert.Equal(new[] { "ok", "cancel" }, trace.Entries);
			Assert.False(d.Text.PeerPreProcessKey(Keys.A));
			Assert.False(d.Text.PeerPreProcessKey(Keys.Return | Keys.Control));
			Assert.False(d.Text.PeerPreProcessKey(Keys.Escape | Keys.Alt));
			Assert.Equal(2, trace.Entries.Count);
		}

		[Fact]
		public void A_dialog_is_answered_by_the_keys_Enter_gives_OK_Escape_gives_Cancel()
		{
			Dlg d = NewDialog("dlg", watch: false);
			On(d.Form, () => d.Text.PeerPreProcessKey(Keys.Return));
			Assert.Equal(DialogResult.OK, d.Form.ShowDialog());
			Dlg e = NewDialog("e", watch: false);
			On(e.Form, () => e.Text.PeerPreProcessKey(Keys.Escape));
			Assert.Equal(DialogResult.Cancel, e.Form.ShowDialog());
		}

		[Fact]
		public void A_focused_button_is_the_default_button_and_the_accept_button_is_again_when_a_text_box_has_the_focus()
		{
			Dlg d = NewDialog("dlg", watch: false);
			var other = new Button { Name = "other" };
			d.Form.Controls.Add(other);
			Assert.True(d.Ok.IsDefault, "the accept button is the default button");
			d.Form.Show();
			backend.Pump();
			other.Focus();
			Assert.True(other.IsDefault);
			Assert.False(d.Ok.IsDefault);
			other.Click += (s, e) => trace.Add("other");
			d.Ok.Click += (s, e) => trace.Add("ok");
			Assert.True(other.PeerPreProcessKey(Keys.Return));
			Assert.Equal(new[] { "other" }, trace.Entries);
			d.Text.Focus();
			Assert.True(d.Ok.IsDefault);
			Assert.False(other.IsDefault);
			d.Form.AcceptButton = null;
			Assert.False(d.Ok.IsDefault);
			Assert.False(d.Text.PeerPreProcessKey(Keys.Return), "no default button: the key is not used");
		}

		[Fact]
		public void A_multi_line_text_box_that_accepts_Return_keeps_the_key_and_a_disabled_accept_button_uses_it_without_a_click()
		{
			Dlg d = NewDialog("dlg", watch: false);
			d.Form.Show();
			backend.Pump();
			d.Text.Multiline = true;
			d.Text.Focus();
			d.Ok.Click += (s, e) => trace.Add("ok");
			Assert.True(d.Text.PeerPreProcessKey(Keys.Return), "a multi-line text box without AcceptsReturn leaves Enter to the dialog");
			Assert.Equal(new[] { "ok" }, trace.Entries);
			d.Text.AcceptsReturn = true;
			Assert.False(d.Text.PeerPreProcessKey(Keys.Return));
			Assert.Equal(1, trace.Entries.Count);
			d.Text.AcceptsReturn = false;
			d.Ok.Enabled = false;
			Assert.True(d.Text.PeerPreProcessKey(Keys.Return), "the key is used even when the button cannot be clicked");
			Assert.Equal(1, trace.Entries.Count);
		}

		[Fact]
		public void CancelButton_gives_a_button_without_an_answer_the_answer_Cancel()
		{
			var f = new Form();
			var plain = new Button();
			var answered = new Button { DialogResult = DialogResult.Abort };
			f.CancelButton = plain;
			Assert.Equal(DialogResult.Cancel, plain.DialogResult);
			f.CancelButton = answered;
			Assert.Equal(DialogResult.Abort, answered.DialogResult);
		}

		[Fact]
		public void A_key_is_offered_to_the_command_keys_of_the_parents_before_the_dialog_keys()
		{
			var f = new ShortcutForm(trace);
			var ok = new Button { DialogResult = DialogResult.OK };
			var inner = new Panel();
			f.Controls.Add(inner);
			f.Controls.Add(ok);
			f.AcceptButton = ok;
			ok.Click += (s, e) => trace.Add("ok");
			f.Show();
			backend.Pump();
			Assert.True(inner.PeerPreProcessKey(Keys.F5));
			Assert.False(inner.PeerPreProcessKey(Keys.F6));
			Assert.Equal(new[] { "form takes F5" }, trace.Entries);
			f.TakeReturn = true;
			Assert.True(inner.PeerPreProcessKey(Keys.Return));
			Assert.Equal(new[] { "form takes F5", "form takes " + Keys.Return }, trace.Entries);
			f.TakeReturn = false;
			Assert.True(inner.PeerPreProcessKey(Keys.Return));
			Assert.Equal("ok", trace.Entries.Last());
		}

		private sealed class ShortcutForm : Form
		{
			private readonly Trace log;

			public ShortcutForm(Trace log)
			{
				this.log = log;
			}

			public bool TakeReturn { get; set; }

			protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
			{
				if (keyData == Keys.F5 || (TakeReturn && keyData == Keys.Return))
				{
					log.Add("form takes " + keyData);
					return true;
				}
				return base.ProcessCmdKey(ref msg, keyData);
			}
		}

		// ---------------------------------------------------------------- nested dialogs

		[Fact]
		public void A_dialog_opened_from_a_handler_of_a_dialog_runs_in_a_loop_of_its_own_and_returns_to_it()
		{
			Dlg a = NewDialog("A");
			Dlg b = NewDialog("B");
			int depthInInner = -1;
			On(a.Form, () =>
			{
				trace.Add("A handler: opens B");
				DialogResult inner = b.Form.ShowDialog(a.Form);
				trace.Add("A handler: B returned " + inner + ", A open " + a.Form.Visible + ", B open " + b.Form.Visible);
				a.Cancel.PerformClick();
			});
			On(b.Form, () =>
			{
				depthInInner = backend.ModalDepth;
				trace.Add("B handler: B owned by A " + (b.Form.Owner == a.Form));
				b.Ok.PerformClick();
			});
			Assert.Equal(DialogResult.Cancel, a.Form.ShowDialog());
			Assert.Equal(2, depthInInner);
			Assert.Equal(0, backend.ModalDepth);
			Assert.Equal(new[]
			{
				"A.Load", "A.Activated", "A.Shown",
				"A handler: opens B",
				"B.Load", "A.Deactivate", "B.Activated", "B.Shown", "B handler: B owned by A True",
				"B.Closing", "B.FormClosing(None)", "B.Closed", "B.FormClosed(None)", "B.Deactivate", "A.Activated",
				"A handler: B returned OK, A open True, B open False",
				"A.Closing", "A.FormClosing(None)", "A.Closed", "A.FormClosed(None)", "A.Deactivate"
			}, trace.Entries);
		}

		[Fact]
		public void An_outer_dialog_that_is_answered_while_an_inner_one_runs_closes_after_the_inner_one()
		{
			Dlg a = NewDialog("A");
			Dlg b = NewDialog("B");
			On(a.Form, () =>
			{
				DialogResult inner = b.Form.ShowDialog(a.Form);
				trace.Add("A handler: B returned " + inner + ", A answer " + a.Form.DialogResult);
			});
			On(b.Form, () =>
			{
				a.Form.DialogResult = DialogResult.Abort;
				trace.Add("B handler: A answered, B continues");
				b.Ok.PerformClick();
			});
			Assert.Equal(DialogResult.Abort, a.Form.ShowDialog());
			Assert.Equal(new[]
			{
				"A.Load", "A.Activated", "A.Shown",
				"B.Load", "A.Deactivate", "B.Activated", "B.Shown", "B handler: A answered, B continues",
				"B.Closing", "B.FormClosing(None)", "B.Closed", "B.FormClosed(None)", "B.Deactivate", "A.Activated",
				"A handler: B returned OK, A answer Abort",
				"A.Closing", "A.FormClosing(None)", "A.Closed", "A.FormClosed(None)", "A.Deactivate"
			}, trace.Entries);
		}

		[Fact]
		public void Close_of_an_outer_dialog_during_an_inner_one_asks_at_once_and_finishes_when_the_inner_one_has_ended()
		{
			Dlg a = NewDialog("A");
			Dlg b = NewDialog("B");
			On(a.Form, () => b.Form.ShowDialog(a.Form));
			On(b.Form, () =>
			{
				a.Form.Close();
				trace.Add("B handler: A asked, A answer " + a.Form.DialogResult);
				b.Ok.PerformClick();
			});
			Assert.Equal(DialogResult.Cancel, a.Form.ShowDialog());
			Assert.Equal(new[]
			{
				"A.Load", "A.Activated", "A.Shown",
				"B.Load", "A.Deactivate", "B.Activated", "B.Shown",
				"A.Closing", Reason("A", "FormClosing", CloseReason.UserClosing),
				"B handler: A asked, A answer Cancel",
				"B.Closing", "B.FormClosing(None)", "B.Closed", "B.FormClosed(None)", "B.Deactivate", "A.Activated",
				"A.Closed", Reason("A", "FormClosed", CloseReason.UserClosing), "A.Deactivate"
			}, trace.Entries);
		}

		[Fact]
		public void A_cancelled_close_of_an_outer_dialog_during_an_inner_one_leaves_it_open()
		{
			Dlg a = NewDialog("A");
			Dlg b = NewDialog("B");
			int attempts = 0;
			a.Form.FormClosing += (s, e) => e.Cancel = ++attempts == 1;
			On(a.Form, () =>
			{
				b.Form.ShowDialog(a.Form);
				trace.Add("A handler: A open " + a.Form.Visible + ", answer " + a.Form.DialogResult);
				a.Form.Close();
			});
			On(b.Form, () =>
			{
				trace.Add("B handler: A closed " + a.Form.CloseByUser());
				b.Ok.PerformClick();
			});
			Assert.Equal(DialogResult.Cancel, a.Form.ShowDialog());
			Assert.Equal(new[]
			{
				"A.Load", "A.Activated", "A.Shown",
				"B.Load", "A.Deactivate", "B.Activated", "B.Shown",
				"A.Closing", Reason("A", "FormClosing", CloseReason.UserClosing),
				"B handler: A closed False",
				"B.Closing", "B.FormClosing(None)", "B.Closed", "B.FormClosed(None)", "B.Deactivate", "A.Activated",
				"A handler: A open True, answer None",
				"A.Closing", Reason("A", "FormClosing", CloseReason.UserClosing), "A.Closed", Reason("A", "FormClosed", CloseReason.UserClosing), "A.Deactivate"
			}, trace.Entries);
		}

		[Fact]
		public void Closing_the_owner_of_a_dialog_ends_the_dialog_and_the_owner_with_the_events_of_an_owned_form()
		{
			Dlg main = NewDialog("main");
			main.Form.Show();
			backend.Pump();
			Dlg d = NewDialog("dlg");
			On(d.Form, () =>
			{
				main.Form.Close();
				trace.Add("dlg handler: owner closed, owner disposed " + main.Form.IsDisposed + ", dialog disposed " + d.Form.IsDisposed);
			});
			trace.Clear();
			DialogResult result = d.Form.ShowDialog(main.Form);
			Assert.Equal(DialogResult.None, result);
			trace.Add("ShowDialog returned");
			Assert.True(main.Form.IsDisposed);
			Assert.True(d.Form.IsDisposed);
			Assert.Equal(0, backend.ModalDepth);
			Assert.Equal(new[]
			{
				"dlg.Load", "main.Deactivate", "dlg.Activated", "dlg.Shown",
				Reason("dlg", "FormClosing", CloseReason.FormOwnerClosing),
				"main.Closing", Reason("main", "FormClosing", CloseReason.UserClosing),
				Reason("dlg", "FormClosed", CloseReason.FormOwnerClosing),
				"main.Closed", Reason("main", "FormClosed", CloseReason.UserClosing),
				"dlg handler: owner closed, owner disposed True, dialog disposed True",
				"ShowDialog returned"
			}, trace.Entries);
		}

		[Fact]
		public void Closing_a_form_whose_dialogs_are_nested_ends_the_inner_one_first()
		{
			Dlg main = NewDialog("main", watch: false);
			main.Form.Show();
			backend.Pump();
			Dlg a = NewDialog("A", watch: false);
			Dlg b = NewDialog("B", watch: false);
			On(a.Form, () =>
			{
				DialogResult r = b.Form.ShowDialog(a.Form);
				trace.Add("B returned " + r);
			});
			On(b.Form, () => main.Form.Close());
			DialogResult outer = a.Form.ShowDialog(main.Form);
			trace.Add("A returned " + outer);
			Assert.Equal(new[] { "B returned None", "A returned None" }, trace.Entries);
			Assert.True(main.Form.IsDisposed && a.Form.IsDisposed && b.Form.IsDisposed);
		}

		[Fact]
		public void The_owned_forms_of_a_closing_form_are_asked_last_one_first_a_refusal_keeps_the_owner_open_and_the_closed_events_follow_the_closing_of_the_owner()
		{
			var owner = new Form { Name = "owner" };
			var one = new Form { Name = "one", Owner = owner };
			var two = new Form { Name = "two", Owner = owner };
			owner.Show();
			one.Show();
			two.Show();
			backend.Pump();
			trace.Watch(owner, "owner");
			trace.Watch(one, "one");
			trace.Watch(two, "two");
			bool refuse = true;
			one.FormClosing += (s, e) =>
			{
				if (e.CloseReason == CloseReason.FormOwnerClosing)
				{
					e.Cancel = refuse;
				}
			};
			trace.Clear();
			owner.Close();
			Assert.Equal(new[]
			{
				Reason("two", "FormClosing", CloseReason.FormOwnerClosing),
				Reason("one", "FormClosing", CloseReason.FormOwnerClosing),
				"owner.Closing", Reason("owner", "FormClosing", CloseReason.UserClosing)
			}, trace.Entries);
			Assert.False(owner.IsDisposed || one.IsDisposed || two.IsDisposed);
			refuse = false;
			trace.Clear();
			owner.Close();
			Assert.Equal(new[]
			{
				Reason("two", "FormClosing", CloseReason.FormOwnerClosing),
				Reason("one", "FormClosing", CloseReason.FormOwnerClosing),
				"owner.Closing", Reason("owner", "FormClosing", CloseReason.UserClosing),
				Reason("two", "FormClosed", CloseReason.FormOwnerClosing),
				Reason("one", "FormClosed", CloseReason.FormOwnerClosing),
				"owner.Closed", Reason("owner", "FormClosed", CloseReason.UserClosing)
			}, trace.Entries.Where(e => !e.EndsWith("Deactivate")).ToArray());
			Assert.True(owner.IsDisposed && one.IsDisposed && two.IsDisposed);
		}

		[Fact]
		public void The_owner_hears_that_an_owned_form_refused_in_the_cancel_flag_of_its_own_FormClosing()
		{
			var owner = new Form { Name = "owner" };
			var owned = new Form { Name = "owned", Owner = owner };
			owner.Show();
			owned.Show();
			backend.Pump();
			owned.FormClosing += (s, e) => e.Cancel = true;
			bool sawCancel = false;
			owner.FormClosing += (s, e) => sawCancel = e.Cancel;
			owner.Close();
			Assert.True(sawCancel);
			Assert.False(owner.IsDisposed);
		}

		[Fact]
		public void A_validating_handler_that_refuses_keeps_a_modeless_form_open_unless_FormClosing_takes_the_cancel_back()
		{
			var form = new Form { Name = "form" };
			var box = new TextBox { Name = "box" };
			form.Controls.Add(box);
			box.Validating += (s, e) =>
			{
				trace.Add("box.Validating");
				e.Cancel = true;
			};
			form.Show();
			backend.Pump();
			form.Close();
			Assert.False(form.IsDisposed);
			Assert.Equal(new[] { "box.Validating" }, trace.Entries);
			form.FormClosing += (s, e) =>
			{
				Assert.True(e.Cancel, "FormClosing is raised with the result of the validation");
				e.Cancel = false;
			};
			form.Close();
			Assert.True(form.IsDisposed);
		}

		[Fact]
		public void Disposing_a_form_disposes_its_owned_forms_last_one_first()
		{
			var owner = new Form { Name = "owner" };
			var one = new Form { Name = "one", Owner = owner };
			var two = new Form { Name = "two", Owner = owner };
			trace.Watch(one, "one", "Disposed");
			trace.Watch(two, "two", "Disposed");
			trace.Watch(owner, "owner", "Disposed");
			owner.Dispose();
			Assert.Equal(new[] { "two.Disposed", "one.Disposed", "owner.Disposed" }, trace.Entries);
			Assert.True(one.IsDisposed && two.IsDisposed);
			Assert.Empty(owner.OwnedForms);
		}

		// ---------------------------------------------------------------- script threads

		private Thread Worker(Action body)
		{
			return new Thread(() =>
			{
				try
				{
					body();
				}
				catch (Exception e)
				{
					lock (workerErrors)
					{
						workerErrors.Add(e);
					}
				}
			})
			{ IsBackground = true };
		}

		private void Finish(params Thread[] threads)
		{
			foreach (Thread t in threads)
			{
				Assert.True(t.Join(Deadline), "a worker did not finish (hang)");
			}
			lock (workerErrors)
			{
				if (workerErrors.Count > 0)
				{
					throw new AggregateException(workerErrors);
				}
			}
		}

		/// <summary>Ends the loop after the deadline so that a hang fails the test instead of blocking the run.</summary>
		private sealed class Guard : IDisposable
		{
			private readonly System.Threading.Timer timer;

			public Guard(params ModalLoop[] loops)
			{
				timer = new System.Threading.Timer(_ =>
				{
					foreach (ModalLoop l in loops)
					{
						l.Exit();
					}
				}, null, Deadline, Timeout.InfiniteTimeSpan);
			}

			public void Dispose() => timer.Dispose();
		}

		[Fact]
		public void ShowDialog_from_a_script_thread_blocks_that_thread_runs_the_dialog_on_the_ui_thread_and_returns_the_answer()
		{
			backend.ModalExitsWhenIdle = false;
			int ui = Environment.CurrentManagedThreadId;
			Dlg d = NewDialog("dlg", watch: false);
			var pump = new ModalLoop();
			int loadThread = -1;
			int shownThread = -1;
			int workerThread = -1;
			int closingThread = -1;
			var returned = new ManualResetEventSlim(false);
			bool returnedWhenAnswered = true;
			d.Form.Load += (s, e) => loadThread = Environment.CurrentManagedThreadId;
			d.Form.FormClosing += (s, e) => closingThread = Environment.CurrentManagedThreadId;
			On(d.Form, () =>
			{
				shownThread = Environment.CurrentManagedThreadId;
				// the script thread is still inside ShowDialog: the dialog has no answer yet
				returnedWhenAnswered = returned.IsSet;
				d.Ok.PerformClick();
			});
			DialogResult result = DialogResult.None;
			Thread script = Worker(() =>
			{
				workerThread = Environment.CurrentManagedThreadId;
				result = d.Form.ShowDialog();
				returned.Set();
				pump.Exit();
			});
			using (new Guard(pump))
			{
				script.Start();
				backend.RunModal(pump);
			}
			Finish(script);
			Assert.Equal(DialogResult.OK, result);
			Assert.False(returnedWhenAnswered);
			Assert.Equal(ui, loadThread);
			Assert.Equal(ui, shownThread);
			Assert.Equal(ui, closingThread);
			Assert.NotEqual(ui, workerThread);
			Assert.False(d.Form.Visible);
			Assert.False(d.Form.Modal);
		}

		[Fact]
		public void An_exception_of_ShowDialog_reaches_the_script_thread_that_called_it()
		{
			backend.ModalExitsWhenIdle = false;
			var f = new Form();
			f.Show();
			backend.Pump();
			var pump = new ModalLoop();
			Exception seen = null;
			Thread script = Worker(() =>
			{
				try
				{
					f.ShowDialog();
				}
				catch (Exception e)
				{
					seen = e;
				}
				pump.Exit();
			});
			using (new Guard(pump))
			{
				script.Start();
				backend.RunModal(pump);
			}
			Finish(script);
			Assert.IsType<InvalidOperationException>(seen);
		}

		[Fact]
		public void Show_Hide_and_Close_from_a_script_thread_run_on_the_ui_thread()
		{
			backend.ModalExitsWhenIdle = false;
			int ui = Environment.CurrentManagedThreadId;
			var f = new Form { Name = "f" };
			var threads = new Dictionary<string, int>();
			f.Load += (s, e) => threads["Load"] = Environment.CurrentManagedThreadId;
			f.VisibleChanged += (s, e) => threads["VisibleChanged " + f.Visible] = Environment.CurrentManagedThreadId;
			f.FormClosing += (s, e) => threads["FormClosing"] = Environment.CurrentManagedThreadId;
			f.FormClosed += (s, e) => threads["FormClosed"] = Environment.CurrentManagedThreadId;
			var pump = new ModalLoop();
			int workerThread = -1;
			Thread script = Worker(() =>
			{
				workerThread = Environment.CurrentManagedThreadId;
				f.Show();
				f.Hide();
				f.Show();
				f.Close();
				pump.Exit();
			});
			using (new Guard(pump))
			{
				script.Start();
				backend.RunModal(pump);
			}
			Finish(script);
			Assert.NotEqual(ui, workerThread);
			Assert.Equal(new[] { "Load", "VisibleChanged True", "VisibleChanged False", "FormClosing", "FormClosed" }.OrderBy(x => x), threads.Keys.OrderBy(x => x));
			Assert.All(threads.Values, t => Assert.Equal(ui, t));
			Assert.True(f.IsDisposed);
		}

		[Fact]
		public void A_script_thread_can_answer_a_dialog_by_setting_DialogResult()
		{
			backend.ModalExitsWhenIdle = false;
			Dlg d = NewDialog("dlg", watch: false);
			var pump = new ModalLoop();
			var shown = new ManualResetEventSlim(false);
			d.Form.Shown += (s, e) => shown.Set();
			DialogResult result = DialogResult.None;
			Thread dialogThread = Worker(() => result = d.Form.ShowDialog());
			Thread answerThread = Worker(() =>
			{
				Assert.True(shown.Wait(Deadline));
				d.Form.DialogResult = DialogResult.Ignore;
			});
			Thread finisher = Worker(() =>
			{
				dialogThread.Join();
				pump.Exit();
			});
			using (new Guard(pump))
			{
				dialogThread.Start();
				answerThread.Start();
				finisher.Start();
				backend.RunModal(pump);
			}
			Finish(dialogThread, answerThread, finisher);
			Assert.Equal(DialogResult.Ignore, result);
		}
	}
}
