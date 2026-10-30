using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using cYo.Compat.Forms;
using Xunit;

namespace Compat.Forms.Tests
{
	/// <summary>
	/// MessageBox.Show (P4.9f): every overload shows a dialog, returns the answer of the button that closed it, and follows the Win32 message box for the buttons of each set, the
	/// default button, Escape and the close box. The dialog is a facade form (<see cref="MessageBoxDialog"/>); the test answers it from an action that it posts to the backend before
	/// the call, which runs as soon as the dialog is shown.
	/// </summary>
	[Collection("Forms runtime")]
	public sealed class MessageBoxTests : IDisposable
	{
		private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(60);

		private readonly NullFormsBackend backend;
		private readonly Trace trace = new Trace();

		public MessageBoxTests()
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

		/// <summary>Posts an action that runs when the next message box is up; it gets the dialog.</summary>
		private void WhenShown(Action<MessageBoxDialog> act)
		{
			backend.Post(() =>
			{
				var dialog = Form.ActiveForm as MessageBoxDialog;
				Assert.NotNull(dialog);
				act(dialog);
			});
		}

		private static string[] Texts(MessageBoxDialog d) => d.ButtonList.Select(b => b.Text).ToArray();

		// ---------------------------------------------------------------- the button sets

		public static IEnumerable<object[]> Sets()
		{
			yield return new object[] { MessageBoxButtons.OK, new[] { "OK" }, new[] { DialogResult.OK } };
			yield return new object[] { MessageBoxButtons.OKCancel, new[] { "OK", "Cancel" }, new[] { DialogResult.OK, DialogResult.Cancel } };
			yield return new object[] { MessageBoxButtons.AbortRetryIgnore, new[] { "Abort", "Retry", "Ignore" }, new[] { DialogResult.Abort, DialogResult.Retry, DialogResult.Ignore } };
			yield return new object[] { MessageBoxButtons.YesNoCancel, new[] { "Yes", "No", "Cancel" }, new[] { DialogResult.Yes, DialogResult.No, DialogResult.Cancel } };
			yield return new object[] { MessageBoxButtons.YesNo, new[] { "Yes", "No" }, new[] { DialogResult.Yes, DialogResult.No } };
			yield return new object[] { MessageBoxButtons.RetryCancel, new[] { "Retry", "Cancel" }, new[] { DialogResult.Retry, DialogResult.Cancel } };
			yield return new object[] { MessageBoxButtons.CancelTryContinue, new[] { "Cancel", "Try Again", "Continue" }, new[] { DialogResult.Cancel, DialogResult.TryAgain, DialogResult.Continue } };
		}

		[Theory]
		[MemberData(nameof(Sets))]
		public void Each_set_has_its_buttons_in_order_and_the_clicked_button_gives_the_answer(MessageBoxButtons set, string[] texts, DialogResult[] answers)
		{
			for (int i = 0; i < answers.Length; i++)
			{
				int click = i;
				string[] seen = null;
				WhenShown(d =>
				{
					seen = Texts(d);
					d.ButtonList[click].PerformClick();
				});
				DialogResult result = MessageBox.Show("question", "caption", set);
				Assert.Equal(texts, seen);
				Assert.Equal(answers[i], result);
			}
		}

		[Theory]
		[MemberData(nameof(Sets))]
		public void The_first_button_is_the_default_unless_another_is_asked_for(MessageBoxButtons set, string[] texts, DialogResult[] answers)
		{
			WhenShown(d =>
			{
				Assert.Same(d.ButtonList[0], d.AcceptButton);
				Assert.Same(d.ButtonList[0], d.ActiveControl);
				Assert.True(((Button)d.ButtonList[0]).IsDefault);
				Assert.True(d.ActiveControl.PeerPreProcessKey(Keys.Return));
			});
			Assert.Equal(answers[0], MessageBox.Show("q", "c", set));
			for (int i = 1; i < answers.Length && i < 3; i++)
			{
				int index = i;
				WhenShown(d =>
				{
					Assert.Same(d.ButtonList[index], d.AcceptButton);
					d.ButtonList[index].Focus();
					d.ActiveControl.PeerPreProcessKey(Keys.Return);
				});
				Assert.Equal(answers[i], MessageBox.Show("q", "c", set, MessageBoxIcon.Question, (MessageBoxDefaultButton)(256 * i)));
			}
			// a default button that is not there is the first
			WhenShown(d => d.AcceptButton.PerformClick());
			Assert.Equal(answers[0], MessageBox.Show("q", "c", set, MessageBoxIcon.None, MessageBoxDefaultButton.Button3 + 256));
		}

		[Fact]
		public void The_help_button_comes_last_is_the_fourth_default_and_closes_nothing()
		{
			string[] seen = null;
			WhenShown(d =>
			{
				seen = Texts(d);
				Assert.Same(d.ButtonList[2], d.AcceptButton);
				d.ButtonList[2].PerformClick();
				Assert.True(d.Visible, "the help button shows no help and the dialog stays");
				d.ButtonList[1].PerformClick();
			});
			DialogResult result = MessageBox.Show("q", "c", MessageBoxButtons.OKCancel, MessageBoxIcon.None, MessageBoxDefaultButton.Button3, 0, true);
			Assert.Equal(new[] { "OK", "Cancel", "Help" }, seen);
			Assert.Equal(DialogResult.Cancel, result);
			WhenShown(d =>
			{
				Assert.Equal(new[] { "OK", "Help" }, Texts(d));
				Assert.Same(d.ButtonList[1], d.AcceptButton);
				d.ButtonList[0].PerformClick();
			});
			MessageBox.Show("q", "c", MessageBoxButtons.OK, MessageBoxIcon.None, MessageBoxDefaultButton.Button2, 0, "help.chm");
		}

		// ---------------------------------------------------------------- Escape, the close box and a dialog nobody answers

		[Theory]
		[InlineData(MessageBoxButtons.OK, DialogResult.OK)]
		[InlineData(MessageBoxButtons.OKCancel, DialogResult.Cancel)]
		[InlineData(MessageBoxButtons.YesNoCancel, DialogResult.Cancel)]
		[InlineData(MessageBoxButtons.RetryCancel, DialogResult.Cancel)]
		[InlineData(MessageBoxButtons.CancelTryContinue, DialogResult.Cancel)]
		public void Escape_and_the_close_box_answer_Cancel_or_OK_when_that_is_all_there_is(MessageBoxButtons set, DialogResult expected)
		{
			WhenShown(d => Assert.True(d.ActiveControl.PeerPreProcessKey(Keys.Escape)));
			Assert.Equal(expected, MessageBox.Show("q", "c", set));
			WhenShown(d => Assert.True(d.CloseByUser()));
			Assert.Equal(expected, MessageBox.Show("q", "c", set));
		}

		[Theory]
		[InlineData(MessageBoxButtons.YesNo, 0, DialogResult.Yes)]
		[InlineData(MessageBoxButtons.AbortRetryIgnore, 1, DialogResult.Retry)]
		public void A_set_without_a_Cancel_button_cannot_be_closed_without_an_answer(MessageBoxButtons set, int click, DialogResult expected)
		{
			WhenShown(d =>
			{
				Assert.False(d.ControlBox);
				Assert.False(d.ActiveControl.PeerPreProcessKey(Keys.Escape), "Escape has no effect");
				Assert.False(d.CloseByUser(), "the close box is vetoed");
				Assert.True(d.Visible);
				Assert.Equal(DialogResult.None, d.DialogResult);
				d.ButtonList[click].PerformClick();
			});
			// the answers of the button, not the safe answer of a box that nobody answers (No, Abort)
			Assert.Equal(expected, MessageBox.Show("q", "c", set));
		}

		[Theory]
		[InlineData(MessageBoxButtons.OK, DialogResult.OK)]
		[InlineData(MessageBoxButtons.OKCancel, DialogResult.Cancel)]
		[InlineData(MessageBoxButtons.AbortRetryIgnore, DialogResult.Abort)]
		[InlineData(MessageBoxButtons.YesNoCancel, DialogResult.Cancel)]
		[InlineData(MessageBoxButtons.YesNo, DialogResult.No)]
		[InlineData(MessageBoxButtons.RetryCancel, DialogResult.Cancel)]
		[InlineData(MessageBoxButtons.CancelTryContinue, DialogResult.Cancel)]
		public void A_message_box_that_nobody_answers_gives_the_safe_answer_of_its_set(MessageBoxButtons set, DialogResult expected)
		{
			Assert.Equal(expected, MessageBox.Show("q", "c", set));
			Assert.Equal(0, backend.ModalDepth);
			Assert.Null(Form.ActiveForm);
		}

		// ---------------------------------------------------------------- arguments

		[Fact]
		public void A_value_outside_an_enum_is_refused_before_anything_is_shown()
		{
			var ex = Assert.Throws<InvalidEnumArgumentException>(() => MessageBox.Show("q", "c", (MessageBoxButtons)99));
			Assert.Equal("buttons", ex.ParamName);
			ex = Assert.Throws<InvalidEnumArgumentException>(() => MessageBox.Show("q", "c", MessageBoxButtons.OK, (MessageBoxIcon)7));
			Assert.Equal("icon", ex.ParamName);
			ex = Assert.Throws<InvalidEnumArgumentException>(() => MessageBox.Show("q", "c", MessageBoxButtons.OK, MessageBoxIcon.None, (MessageBoxDefaultButton)5));
			Assert.Equal("defaultButton", ex.ParamName);
			var owner = new Form();
			var arg = Assert.Throws<ArgumentException>(() => MessageBox.Show(owner, "q", "c", MessageBoxButtons.OK, MessageBoxIcon.None, MessageBoxDefaultButton.Button1, MessageBoxOptions.ServiceNotification));
			Assert.Equal("options", arg.ParamName);
			// without an owner the option is allowed
			Assert.Equal(DialogResult.OK, MessageBox.Show("q", "c", MessageBoxButtons.OK, MessageBoxIcon.None, MessageBoxDefaultButton.Button1, MessageBoxOptions.ServiceNotification));
			Assert.Empty(backend.Peers.Where(p => p.Control is MessageBoxDialog && p.Shown));
		}

		[Fact]
		public void The_text_the_caption_the_icon_and_the_options_reach_the_dialog_and_a_missing_text_is_empty()
		{
			WhenShown(d =>
			{
				Assert.Equal("a\r\nb", d.Message);
				Assert.Equal("cap", d.Text);
				Assert.Equal(MessageBoxIcon.Warning, d.BoxIcon);
				Assert.Equal(MessageBoxButtons.YesNo, d.Buttons);
				Assert.Equal(MessageBoxOptions.RightAlign | MessageBoxOptions.RtlReading, d.Options);
				Assert.Equal(RightToLeft.Yes, d.RightToLeft);
				var label = d.Controls.OfType<Label>().Single();
				Assert.Equal("a\r\nb", label.Text);
				Assert.Equal(ContentAlignment.TopRight, label.TextAlign);
				d.ButtonList[0].PerformClick();
			});
			MessageBox.Show("a\r\nb", "cap", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button1, MessageBoxOptions.RightAlign | MessageBoxOptions.RtlReading);
			WhenShown(d =>
			{
				Assert.Equal(string.Empty, d.Message);
				Assert.Equal(string.Empty, d.Text);
				d.ButtonList[0].PerformClick();
			});
			MessageBox.Show(null);
		}

		[Fact]
		public void Every_overload_shows_a_message_box_and_hands_over_what_it_has()
		{
			MethodInfo[] shows = typeof(MessageBox).GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.Name == "Show").ToArray();
			Assert.Equal(21, shows.Length);
			foreach (MethodInfo show in shows)
			{
				var owner = new Form { Name = "owner" };
				owner.Show();
				backend.Pump();
				ParameterInfo[] ps = show.GetParameters();
				bool hasOwner = ps.Any(p => p.ParameterType == typeof(IWin32Window));
				bool hasButtons = ps.Any(p => p.Name == "buttons");
				bool hasDefault = ps.Any(p => p.Name == "defaultButton");
				bool hasCaption = ps.Any(p => p.Name == "caption");
				bool hasHelp = ps.Any(p => p.Name == "helpFilePath" || p.Name == "keyword" || p.Name == "displayHelpButton");
				object[] args = ps.Select(p => ArgumentFor(p, owner)).ToArray();
				string label = show + "";
				bool ran = false;
				WhenShown(d =>
				{
					ran = true;
					Assert.True("hello" == d.Message, label);
					Assert.True((hasCaption ? "cap" : string.Empty) == d.Text, label);
					Assert.True((hasButtons ? "OK Cancel" : "OK") + (hasHelp ? " Help" : string.Empty) == string.Join(" ", Texts(d)), label);
					Assert.True(hasDefault ? "Cancel" == ((Button)d.AcceptButton).Text : "OK" == ((Button)d.AcceptButton).Text, label);
					Assert.True(!hasOwner || ReferenceEquals(backend.Peers.Last(p => p.Control == d).LastOwner, backend.Peers.Last(p => p.Control == owner)), label);
					Assert.Equal(new[] { owner }, Application.OpenForms.Cast<Form>());
					Assert.Empty(owner.OwnedForms);
					d.AcceptButton.PerformClick();
				});
				var result = (DialogResult)show.Invoke(null, args);
				Assert.True(ran, label);
				Assert.True((hasDefault ? DialogResult.Cancel : DialogResult.OK) == result, label);
				owner.Dispose();
			}
		}

		private static object ArgumentFor(ParameterInfo p, Form owner)
		{
			switch (p.Name)
			{
			case "owner": return owner;
			case "text": return "hello";
			case "caption": return "cap";
			case "buttons": return MessageBoxButtons.OKCancel;
			case "icon": return MessageBoxIcon.Information;
			case "defaultButton": return MessageBoxDefaultButton.Button2;
			case "options": return (MessageBoxOptions)0;
			case "helpFilePath": return "help.chm";
			case "keyword": return "kw";
			case "navigator": return HelpNavigator.Topic;
			case "param": return "p";
			case "displayHelpButton": return true;
			default: throw new InvalidOperationException(p.Name);
			}
		}

		// ---------------------------------------------------------------- owner, layout, threads

		[Fact]
		public void The_owner_is_deactivated_while_the_box_is_up_and_active_again_afterwards_and_the_box_is_not_one_of_its_forms()
		{
			var owner = new Form { Name = "owner" };
			trace.Watch(owner, "owner");
			owner.Show();
			backend.Pump();
			trace.Clear();
			WhenShown(d =>
			{
				trace.Add("box up");
				Assert.Null(owner.OwnedForms.FirstOrDefault());
				d.ButtonList[0].PerformClick();
			});
			MessageBox.Show(owner, "q", "c");
			Assert.Equal(new[] { "owner.Deactivate", "box up", "owner.Activated" }, trace.Entries);
			Assert.Same(owner, Form.ActiveForm);
		}

		[Fact]
		public void The_box_is_centred_on_its_owner_or_on_the_screen_and_its_controls_are_inside_it()
		{
			var owner = new Form { Name = "owner", Bounds = new Rectangle(100, 50, 600, 400) };
			owner.Show();
			backend.Pump();
			Rectangle box = Rectangle.Empty;
			WhenShown(d =>
			{
				box = d.Bounds;
				Rectangle client = d.ClientRectangle;
				foreach (Control c in d.Controls)
				{
					Assert.True(client.Contains(c.Bounds), c.Name + " " + c.Bounds + " in " + client);
				}
				var buttons = d.ButtonList.ToArray();
				for (int i = 1; i < buttons.Length; i++)
				{
					Assert.False(buttons[i - 1].Bounds.IntersectsWith(buttons[i].Bounds));
					Assert.Equal(buttons[0].Top, buttons[i].Top);
				}
				Label label = d.Controls.OfType<Label>().Single();
				Assert.True(label.Bottom < buttons[0].Top);
				Assert.Equal(FormBorderStyle.FixedDialog, d.FormBorderStyle);
				Assert.False(d.ShowInTaskbar);
				d.ButtonList[0].PerformClick();
			});
			MessageBox.Show(owner, "a rather long text that has to be wrapped somewhere when it is wider than the box can be, and it is", "c", MessageBoxButtons.YesNoCancel);
			Assert.Equal(new Point(owner.Left + (owner.Width - box.Width) / 2, owner.Top + (owner.Height - box.Height) / 2), box.Location);
			WhenShown(d =>
			{
				box = d.Bounds;
				d.ButtonList[0].PerformClick();
			});
			MessageBox.Show("short");
			Assert.Equal(new Point((1920 - box.Width) / 2, (1080 - box.Height) / 2), box.Location);
		}

		[Fact]
		public void A_message_box_from_a_script_thread_blocks_it_and_runs_on_the_ui_thread()
		{
			backend.ModalExitsWhenIdle = false;
			int ui = Environment.CurrentManagedThreadId;
			var pump = new ModalLoop();
			int boxThread = -1;
			int workerThread = -1;
			var answered = new ManualResetEventSlim(false);
			bool waitingWhenUp = false;
			// the box is shown by the UI thread when it serves the call of the script thread: answer it as soon as it is up
			EventHandler enter = (s, e) => WhenShown(d =>
			{
				boxThread = Environment.CurrentManagedThreadId;
				waitingWhenUp = !answered.IsSet;
				d.ButtonList[1].PerformClick();
			});
			Application.EnterThreadModal += enter;
			DialogResult result = DialogResult.None;
			Exception error = null;
			var worker = new Thread(() =>
			{
				try
				{
					workerThread = Environment.CurrentManagedThreadId;
					result = MessageBox.Show("q", "c", MessageBoxButtons.YesNo);
					answered.Set();
				}
				catch (Exception e)
				{
					error = e;
				}
				finally
				{
					pump.Exit();
				}
			})
			{ IsBackground = true };
			try
			{
				using (new System.Threading.Timer(_ => pump.Exit(), null, Deadline, Timeout.InfiniteTimeSpan))
				{
					worker.Start();
					backend.RunModal(pump);
				}
			}
			finally
			{
				Application.EnterThreadModal -= enter;
			}
			Assert.True(worker.Join(Deadline));
			Assert.Null(error);
			Assert.Equal(DialogResult.No, result);
			Assert.True(waitingWhenUp);
			Assert.Equal(ui, boxThread);
			Assert.NotEqual(ui, workerThread);
		}

		[Fact]
		public void The_modal_state_events_are_raised_around_every_dialog_and_message_box()
		{
			var log = new List<string>();
			EventHandler enter = (s, e) => log.Add("enter");
			EventHandler leave = (s, e) => log.Add("leave");
			Application.EnterThreadModal += enter;
			Application.LeaveThreadModal += leave;
			try
			{
				WhenShown(d =>
				{
					log.Add("box, message loop " + Application.MessageLoop);
					d.ButtonList[0].PerformClick();
				});
				MessageBox.Show("q");
				var f = new Form();
				f.Shown += (s, e) =>
				{
					log.Add("dialog, modal " + f.Modal);
					f.Close();
				};
				f.ShowDialog();
			}
			finally
			{
				Application.EnterThreadModal -= enter;
				Application.LeaveThreadModal -= leave;
			}
			Assert.Equal(new[] { "enter", "box, message loop True", "leave", "enter", "dialog, modal True", "leave" }, log);
			Assert.False(Application.MessageLoop);
		}
	}
}
