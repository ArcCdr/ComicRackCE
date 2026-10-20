using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using S6.Backend;
using SWF = System.Windows.Forms;

namespace S6.Cases
{
	/// <summary>The modal loop cases (section 5.9.4): nested frame, other threads, closing, nesting, exceptions, DoEvents, cross-thread updates.</summary>
	public static class ModalCases
	{
		public sealed class Dlg
		{
			public SWF.Form Form;
			public SWF.TextBox Text;
			public SWF.NumericUpDown Num;
			public SWF.CheckBox Check;
			public SWF.ComboBox Combo;
			public SWF.Button Ok;
			public SWF.Button Cancel;
		}

		/// <summary>A form with the controls scripts use, built from any thread (state only, no peer).</summary>
		public static Dlg Build(string name, bool accept = true)
		{
			Dlg d = new Dlg();
			d.Form = new SWF.Form { Name = name, Text = name, ClientSize = new Size(280, 190), FormBorderStyle = SWF.FormBorderStyle.FixedDialog, StartPosition = SWF.FormStartPosition.CenterParent, MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false };
			d.Text = new SWF.TextBox { Name = "text", Text = "a", Location = new Point(10, 10), Size = new Size(200, 22) };
			d.Num = new SWF.NumericUpDown { Name = "num", Location = new Point(10, 40), Size = new Size(80, 22), Maximum = 1000, Value = 5 };
			d.Check = new SWF.CheckBox { Name = "check", Text = "Check", Location = new Point(110, 40), Size = new Size(100, 22) };
			d.Combo = new SWF.ComboBox { Name = "combo", Location = new Point(10, 70), Size = new Size(200, 24), DropDownStyle = SWF.ComboBoxStyle.DropDownList };
			d.Combo.Items.Add("one");
			d.Combo.Items.Add("two");
			d.Combo.SelectedIndex = 0;
			d.Ok = new SWF.Button { Name = "ok", Text = "&OK", DialogResult = SWF.DialogResult.OK, Location = new Point(40, 140), Size = new Size(80, 26) };
			d.Cancel = new SWF.Button { Name = "cancel", Text = "&Cancel", DialogResult = SWF.DialogResult.Cancel, Location = new Point(140, 140), Size = new Size(80, 26) };
			d.Form.Controls.Add(d.Text);
			d.Form.Controls.Add(d.Num);
			d.Form.Controls.Add(d.Check);
			d.Form.Controls.Add(d.Combo);
			d.Form.Controls.Add(d.Ok);
			d.Form.Controls.Add(d.Cancel);
			if (accept)
			{
				d.Form.AcceptButton = d.Ok;
				d.Form.CancelButton = d.Cancel;
			}
			return d;
		}

		[Case("M01", "ShowDialog on the UI thread blocks inside a nested frame, the UI keeps running, typed values come back")]
		public static async Task M01(Ctx c)
		{
			Dlg d = Build("M01 dialog");
			List<string> order = new List<string>();
			d.Form.Load += (s, e) => order.Add("Load");
			d.Form.Shown += (s, e) => order.Add("Shown");
			d.Form.Activated += (s, e) => order.Add("Activated");
			d.Form.FormClosing += (s, e) => order.Add("FormClosing");
			d.Form.FormClosed += (s, e) => order.Add("FormClosed");
			Task<SWF.DialogResult> shown = c.UiAsync(() =>
			{
				order.Add("before ShowDialog");
				SWF.DialogResult r = d.Form.ShowDialog(c.MainForm);
				order.Add("after ShowDialog");
				return r;
			});
			await c.WaitForForm(f => ReferenceEquals(f, d.Form), "M01 dialog");
			bool ran = await c.Ui(() => true);
			c.Report.Check("a job queued while the dialog is open runs; ShowDialog has not returned", ran && !shown.IsCompleted);
			c.Report.Check("the dialog is a modal form", await c.Ui(() => d.Form.Modal));
			await c.Ui(() =>
			{
				Visual<TextBox>(d.Text).Text = "hello";
				Visual<NumericUpDown>(d.Num).Value = 42;
				Visual<CheckBox>(d.Check).IsChecked = true;
				Visual<ComboBox>(d.Combo).SelectedIndex = 1;
			});
			c.Report.Check("typing in the peers reaches the facade", d.Text.Text == "hello" && d.Num.Value == 42 && d.Check.Checked && d.Combo.SelectedIndex == 1, "text=" + d.Text.Text + " num=" + d.Num.Value + " checked=" + d.Check.Checked + " index=" + d.Combo.SelectedIndex);
			string png = await c.Snapshot(d.Form, "M01-dialog");
			c.Report.Info("snapshot", System.IO.Path.GetFileName(png));
			await c.Click(d.Ok);
			SWF.DialogResult result = await c.Within(shown, "M01 ShowDialog return");
			c.Report.Check("ShowDialog returns the button's DialogResult (OK) after a pointer click", result == SWF.DialogResult.OK, result.ToString());
			c.Report.Check("the values are readable after the dialog closed", d.Text.Text == "hello" && d.Num.Value == 42 && d.Check.Checked && d.Combo.SelectedIndex == 1);
			c.Report.Check("event order is WinForms-like", string.Join(",", order) == "before ShowDialog,Load,Activated,Shown,FormClosing,FormClosed,after ShowDialog", string.Join(",", order));
			c.Report.Check("the closed form is not in the open list", !SWF.FormsRuntime.OpenForms.Contains(d.Form));
			await c.Ui(() => d.Form.Dispose());
		}

		[Case("M02", "ShowDialog from a background thread: the thread blocks, the UI thread keeps pumping (timer ticks), the thread continues with the result")]
		public static async Task M02(Ctx c)
		{
			Dlg d = null;
			int workerBefore = 0, workerAfter = 0;
			Task<SWF.DialogResult> shown = Task.Run(() =>
			{
				workerBefore = Environment.CurrentManagedThreadId;
				d = Build("M02 dialog");
				SWF.DialogResult r = d.Form.ShowDialog(c.MainForm);
				workerAfter = Environment.CurrentManagedThreadId;
				d.Text.Text = "set by the worker after the dialog closed";
				return r;
			});
			await c.WaitFor(() => d != null && SWF.FormsRuntime.OpenForms.Contains(d.Form), "M02 dialog shown");
			int ticks = 0;
			SWF.Timer timer = null;
			await c.Ui(() =>
			{
				timer = new SWF.Timer { Interval = 5 };
				timer.Tick += (s, e) => Interlocked.Increment(ref ticks);
				timer.Start();
			});
			await c.WaitFor(() => Volatile.Read(ref ticks) >= 3 && !shown.IsCompleted, "timer ticks while the worker thread is blocked in ShowDialog");
			c.Report.Check("the UI thread kept running (3 timer ticks) while the worker thread was blocked in ShowDialog", true, "ticks=" + ticks);
			c.Report.Check("the worker thread is still inside ShowDialog", !shown.IsCompleted);
			bool invokeRequired = d.Form.InvokeRequired;
			c.Report.Check("InvokeRequired is true off the UI thread", invokeRequired);
			await c.Click(d.Cancel);
			SWF.DialogResult r2 = await c.Within(shown, "M02 ShowDialog return");
			c.Report.Check("ShowDialog returned Cancel to the worker thread", r2 == SWF.DialogResult.Cancel, r2.ToString());
			c.Report.Check("the code after ShowDialog ran on the same worker thread", workerBefore == workerAfter && workerBefore != c.UiThreadId, "thread " + workerBefore + " (UI thread is " + c.UiThreadId + ")");
			await c.Ui(() => timer.Dispose());
			c.Report.Check("the form, built and finished on the worker thread, keeps its state", d.Text.Text == "set by the worker after the dialog closed");
			await c.Ui(() => d.Form.Dispose());
		}

		[Case("M03", "MessageBox.Show blocks on the UI thread and on a background thread; Esc/close box/Enter follow the WinForms rules")]
		public static async Task M03(Ctx c)
		{
			// YesNo from the UI thread: close box and Esc do nothing, Enter takes the first button.
			Task<SWF.DialogResult> t = c.UiAsync(() => SWF.MessageBox.Show(c.MainForm, "Continue with the operation?", "S6", SWF.MessageBoxButtons.YesNo));
			SWF.Form box = await c.WaitForForm("S6");
			c.Report.Check("MessageBox is a dialog form with a Yes and a No button", box.Controls.OfType<SWF.Button>().Select(b => b.DialogResult).SequenceEqual(new[] { SWF.DialogResult.Yes, SWF.DialogResult.No }));
			c.Report.Info("snapshot", System.IO.Path.GetFileName(await c.Snapshot(box, "M03-messagebox")));
			await c.Ui(() => AvaloniaBackend.WindowOf(box).Close());
			bool stillOpen = await c.Ui(() => box.IsShown);
			c.Report.Check("YesNo: the close box is ignored", stillOpen && !t.IsCompleted);
			await c.Press(box, Key.Escape, PhysicalKey.Escape);
			stillOpen = await c.Ui(() => box.IsShown);
			c.Report.Check("YesNo: Escape is ignored", stillOpen && !t.IsCompleted);
			await c.Click(box.Controls.OfType<SWF.Button>().First(b => b.DialogResult == SWF.DialogResult.No));
			SWF.DialogResult r = await c.Within(t, "M03 YesNo");
			c.Report.Check("YesNo: clicking No returns No", r == SWF.DialogResult.No, r.ToString());

			// The same from a background thread.
			int before = 0, after = 0;
			Task<SWF.DialogResult> tb = Task.Run(() =>
			{
				before = Environment.CurrentManagedThreadId;
				SWF.DialogResult x = SWF.MessageBox.Show("Really?", "S6 bg", SWF.MessageBoxButtons.YesNo);
				after = Environment.CurrentManagedThreadId;
				return x;
			});
			SWF.Form box2 = await c.WaitForForm("S6 bg");
			c.Report.Check("MessageBox from a background thread blocks that thread", !tb.IsCompleted);
			await c.Press(box2, Key.Enter, PhysicalKey.Enter);
			SWF.DialogResult r2 = await c.Within(tb, "M03 background YesNo");
			c.Report.Check("Enter takes the first button (Yes); the thread continues where it was", r2 == SWF.DialogResult.Yes && before == after, r2 + ", thread " + before + " -> " + after);

			// OKCancel: Escape cancels. OK: Escape and the close box give OK.
			Task<SWF.DialogResult> t3 = c.UiAsync(() => SWF.MessageBox.Show("Sure?", "S6 oc", SWF.MessageBoxButtons.OKCancel));
			SWF.Form box3 = await c.WaitForForm("S6 oc");
			await c.Press(box3, Key.Escape, PhysicalKey.Escape);
			c.Report.Check("OKCancel: Escape returns Cancel", await c.Within(t3, "M03 OKCancel") == SWF.DialogResult.Cancel);
			Task<SWF.DialogResult> t4 = c.UiAsync(() => SWF.MessageBox.Show("Done."));
			SWF.Form box4 = await c.WaitForForm(f => f.Name == "MessageBox", "OK box");
			await c.Ui(() => AvaloniaBackend.WindowOf(box4).Close());
			SWF.DialogResult r4 = await c.Within(t4, "M03 OK");
			c.Report.Check("OK only: the close box returns OK, as the native message box does", r4 == SWF.DialogResult.OK, r4.ToString());
		}

		[Case("M04", "Closing during the nested frame: Close() from another thread, user close with a cancelling FormClosing, DialogResult assignment, Dispose")]
		public static async Task M04(Ctx c)
		{
			// Close() called from the runner thread while the UI thread is in the nested frame.
			{
				Dlg d = Build("M04a");
				List<string> log = new List<string>();
				d.Form.FormClosing += (s, e) => log.Add("closing:" + e.CloseReason);
				d.Form.FormClosed += (s, e) => log.Add("closed:" + e.CloseReason);
				Task<SWF.DialogResult> t = c.UiAsync(() => d.Form.ShowDialog(c.MainForm));
				await c.WaitForForm(f => ReferenceEquals(f, d.Form), "M04a");
				d.Form.Close();
				SWF.DialogResult r = await c.Within(t, "M04a");
				c.Report.Check("Close() from another thread ends ShowDialog with Cancel", r == SWF.DialogResult.Cancel && string.Join(",", log) == "closing:None,closed:None", r + " " + string.Join(",", log));
				await c.Ui(() => d.Form.Dispose());
			}
			// The user closes the window; FormClosing cancels the first time.
			{
				Dlg d = Build("M04b");
				int closingCalls = 0;
				d.Form.FormClosing += (s, e) =>
				{
					closingCalls++;
					e.Cancel = closingCalls == 1;
				};
				Task<SWF.DialogResult> t = c.UiAsync(() => d.Form.ShowDialog(c.MainForm));
				await c.WaitForForm(f => ReferenceEquals(f, d.Form), "M04b");
				await c.Ui(() => AvaloniaBackend.WindowOf(d.Form).Close());
				bool open = await c.Ui(() => AvaloniaBackend.WindowOf(d.Form).IsVisible);
				c.Report.Check("a cancelling FormClosing keeps the window open and the loop running", open && !t.IsCompleted && closingCalls == 1);
				await c.Ui(() => AvaloniaBackend.WindowOf(d.Form).Close());
				SWF.DialogResult r = await c.Within(t, "M04b");
				c.Report.Check("the second user close ends ShowDialog with Cancel", r == SWF.DialogResult.Cancel && closingCalls == 2, r + ", FormClosing called " + closingCalls + " times");
				await c.Ui(() => d.Form.Dispose());
			}
			// Assigning DialogResult closes a modal form.
			{
				Dlg d = Build("M04c");
				Task<SWF.DialogResult> t = c.UiAsync(() => d.Form.ShowDialog(c.MainForm));
				await c.WaitForForm(f => ReferenceEquals(f, d.Form), "M04c");
				d.Form.DialogResult = SWF.DialogResult.Retry;
				SWF.DialogResult r = await c.Within(t, "M04c");
				c.Report.Check("Form.DialogResult = Retry closes the form and ShowDialog returns Retry", r == SWF.DialogResult.Retry, r.ToString());
				await c.Ui(() => d.Form.Dispose());
			}
			// Dispose inside the frame.
			{
				Dlg d = Build("M04d");
				Task<SWF.DialogResult> t = c.UiAsync(() => d.Form.ShowDialog(c.MainForm));
				await c.WaitForForm(f => ReferenceEquals(f, d.Form), "M04d");
				await c.Ui(() => d.Form.Dispose());
				SWF.DialogResult r = await c.Within(t, "M04d");
				c.Report.Check("Dispose() while ShowDialog runs releases the loop (Cancel)", r == SWF.DialogResult.Cancel, r.ToString());
			}
			// A modal form can be shown again; its state survived.
			{
				Dlg d = Build("M04e");
				Task<SWF.DialogResult> t = c.UiAsync(() => d.Form.ShowDialog(c.MainForm));
				await c.WaitForForm(f => ReferenceEquals(f, d.Form), "M04e 1");
				await c.Ui(() => Visual<TextBox>(d.Text).Text = "kept");
				await c.Click(d.Ok);
				await c.Within(t, "M04e 1");
				t = c.UiAsync(() => d.Form.ShowDialog(c.MainForm));
				await c.WaitForForm(f => ReferenceEquals(f, d.Form), "M04e 2");
				string peerText = await c.Ui(() => Visual<TextBox>(d.Text).Text);
				c.Report.Check("the same form can be shown modally again and starts with its old values", peerText == "kept", "peer text '" + peerText + "'");
				InvalidOperationException twice = null;
				await c.Ui(() =>
				{
					try
					{
						d.Form.ShowDialog(c.MainForm);
					}
					catch (InvalidOperationException e)
					{
						twice = e;
					}
				});
				c.Report.Check("ShowDialog on a form that is already shown throws InvalidOperationException", twice != null);
				await c.Click(d.Cancel);
				await c.Within(t, "M04e 2");
				await c.Ui(() => d.Form.Dispose());
			}
		}

		[Case("M05", "Nested dialogs: LIFO closing, and closing the lower dialog first")]
		public static async Task M05(Ctx c)
		{
			List<string> log = new List<string>();
			Dlg a = Build("M05 A");
			Dlg b = Build("M05 B");
			a.Form.Shown += (s, e) =>
			{
				log.Add("A shown");
				// A script opens a second dialog from inside the first one's event.
				SWF.DialogResult rb = b.Form.ShowDialog(a.Form);
				log.Add("B returned " + rb);
			};
			Task<SWF.DialogResult> ta = c.UiAsync(() =>
			{
				SWF.DialogResult r = a.Form.ShowDialog(c.MainForm);
				log.Add("A returned " + r);
				return r;
			});
			await c.WaitForForm(f => ReferenceEquals(f, b.Form), "M05 B");
			c.Report.Check("a dialog opened from the Shown event of another runs on top of it (two nested frames)", SWF.FormsRuntime.OpenForms.Contains(a.Form) && SWF.FormsRuntime.OpenForms.Contains(b.Form));
			// B is the active frame: closing it first returns B, then A's Shown handler ends, and A's own loop starts.
			await c.Click(b.Ok);
			await c.WaitFor(() => log.Contains("B returned OK"), "B returned");
			await c.WaitForForm(f => ReferenceEquals(f, a.Form), "A still open");
			c.Report.Check("closing B ends only B; A stays open", !ta.IsCompleted && !SWF.FormsRuntime.OpenForms.Contains(b.Form));
			await c.Click(a.Cancel);
			c.Report.Check("A returns Cancel after B", await c.Within(ta, "M05 A") == SWF.DialogResult.Cancel && string.Join("|", log) == "A shown|B returned OK|A returned Cancel", string.Join("|", log));
			await c.Ui(() => { a.Form.Dispose(); b.Form.Dispose(); });

			// The lower dialog is closed first (a timer of A's code closes A while B is on top).
			log.Clear();
			Dlg a2 = Build("M05 A2");
			Dlg b2 = Build("M05 B2");
			a2.Form.Shown += (s, e) =>
			{
				SWF.DialogResult r = b2.Form.ShowDialog(a2.Form);
				log.Add("B2 returned " + r);
			};
			Task<SWF.DialogResult> ta2 = c.UiAsync(() =>
			{
				SWF.DialogResult r = a2.Form.ShowDialog(c.MainForm);
				log.Add("A2 returned " + r);
				return r;
			});
			await c.WaitForForm(f => ReferenceEquals(f, b2.Form), "M05 B2");
			a2.Form.Close();
			await c.Ui(() => { });
			bool bStillOpen = SWF.FormsRuntime.OpenForms.Contains(b2.Form);
			c.Report.Info("A2.Close() while B2 is on top", "B2 still open afterwards: " + bStillOpen + "; A2 ShowDialog returned already: " + ta2.IsCompleted + "; log so far: " + string.Join("|", log));
			if (bStillOpen)
			{
				await c.Click(b2.Ok);
			}
			SWF.DialogResult r2 = await c.Within(ta2, "M05 A2");
			string order = string.Join("|", log);
			c.Report.Check("both ShowDialog calls return and the innermost one returns first", order.StartsWith("B2 returned") && order.EndsWith("A2 returned " + r2), order);
			await c.Ui(() => { a2.Form.Dispose(); b2.Form.Dispose(); });
		}

		[Case("M06", "Exceptions in handlers during the nested frame go to Application.ThreadException and do not stop the loop")]
		public static async Task M06(Ctx c)
		{
			Dlg d = Build("M06");
			List<Exception> seen = new List<Exception>();
			SWF.Application.ThreadException += Handler;
			void Handler(object s, ThreadExceptionEventArgs e) => seen.Add(e.Exception);
			try
			{
				SWF.Button bad = new SWF.Button { Name = "bad", Text = "Throw", Location = new Point(10, 110), Size = new Size(80, 24) };
				bad.Click += (s, e) => throw new InvalidOperationException("script handler failed");
				d.Form.Controls.Add(bad);
				int goodClicks = 0;
				d.Ok.Click += (s, e) => goodClicks++;
				Task<SWF.DialogResult> t = c.UiAsync(() => d.Form.ShowDialog(c.MainForm));
				await c.WaitForForm(f => ReferenceEquals(f, d.Form), "M06");
				await c.Click(bad);
				c.Report.Check("the exception reached Application.ThreadException", seen.Count == 1 && seen[0] is InvalidOperationException && seen[0].Message == "script handler failed", seen.Count + " seen");
				c.Report.Check("the dialog is still open and its loop runs", !t.IsCompleted && await c.Ui(() => true));
				await c.Click(d.Ok);
				c.Report.Check("a later click is still handled; ShowDialog returns OK", await c.Within(t, "M06") == SWF.DialogResult.OK && goodClicks == 1);
			}
			finally
			{
				SWF.Application.ThreadException -= Handler;
			}
			// Without any handler: recorded, not fatal.
			Dlg d2 = Build("M06b");
			SWF.Button bad2 = new SWF.Button { Name = "bad", Text = "Throw", Location = new Point(10, 110), Size = new Size(80, 24) };
			bad2.Click += (s, e) => throw new ArgumentException("unhandled in the app");
			d2.Form.Controls.Add(bad2);
			Task<SWF.DialogResult> t2 = c.UiAsync(() => d2.Form.ShowDialog(c.MainForm));
			await c.WaitForForm(f => ReferenceEquals(f, d2.Form), "M06b");
			await c.Click(bad2);
			int unhandled;
			lock (SWF.Application.Unhandled)
			{
				unhandled = SWF.Application.Unhandled.Count;
			}
			c.Report.Check("without a ThreadException handler the facade records the exception and keeps the loop alive", unhandled == 1 && !t2.IsCompleted);
			await c.Click(d2.Cancel);
			await c.Within(t2, "M06b");
			await c.Ui(() => { d.Form.Dispose(); d2.Form.Dispose(); });
		}

		[Case("M07", "Application.DoEvents pumps the queue inside a UI-thread loop; it is a no-op off the UI thread")]
		public static async Task M07(Ctx c)
		{
			Dlg d = Build("M07");
			int clicks = 0;
			bool loopActive = false, clickedInsideLoop = false;
			d.Ok.Click += (s, e) =>
			{
				clicks++;
				clickedInsideLoop = loopActive;
			};
			await c.Ui(() => d.Form.Show(c.MainForm));
			bool flag = false;
			long spins = 0;
			Task<long> loop = c.UiAsync(() =>
			{
				loopActive = true;
				Stopwatch sw = Stopwatch.StartNew();
				while (!Volatile.Read(ref flag) && sw.Elapsed < Ctx.HangLimit)
				{
					SWF.Application.DoEvents();
					spins++;
				}
				loopActive = false;
				return spins;
			});
			await c.WaitFor(() => Volatile.Read(ref loopActive), "DoEvents loop started");
			Dispatcher.UIThread.Post(() => d.Ok.PerformClick());
			Dispatcher.UIThread.Post(() => Volatile.Write(ref flag, true));
			await c.Within(loop, "M07 loop");
			c.Report.Check("a click posted to the queue was handled inside the DoEvents loop, before the loop ended", clicks == 1 && clickedInsideLoop, "clicks=" + clicks + ", handled in loop=" + clickedInsideLoop);
			SWF.Application.DoEvents();
			c.Report.Check("DoEvents off the UI thread returns at once", true);
			await c.Ui(() => d.Form.Dispose());
		}

		[Case("M08", "Cross-thread work during a dialog: property writes without Invoke, Invoke, BeginInvoke, Close from the worker (progress dialog pattern)")]
		public static async Task M08(Ctx c)
		{
			Dlg d = Build("M08");
			SWF.Label progress = new SWF.Label { Name = "progress", Text = "starting", Location = new Point(10, 100), Size = new Size(200, 20) };
			d.Form.Controls.Add(progress);
			Task<SWF.DialogResult> t = c.UiAsync(() => d.Form.ShowDialog(c.MainForm));
			await c.WaitForForm(f => ReferenceEquals(f, d.Form), "M08");
			int invokedOn = 0, begunOn = 0;
			object invokeResult = null;
			Task worker = Task.Run(() =>
			{
				for (int i = 0; i < 200; i++)
				{
					progress.Text = "step " + i;
				}
				invokeResult = d.Form.Invoke(new Func<int>(() =>
				{
					invokedOn = Environment.CurrentManagedThreadId;
					return 42;
				}));
				IAsyncResult ar = d.Form.BeginInvoke(new Action(() => begunOn = Environment.CurrentManagedThreadId));
				d.Form.EndInvoke(ar);
				progress.Text = "done";
			});
			await c.Within(worker, "M08 worker");
			string peerText = await c.Ui(() => Visual<TextBlock>(progress).Text);
			c.Report.Check("200 unsynchronized Text writes from the worker: facade and peer end with the last one", progress.Text == "done" && peerText == "done", "facade '" + progress.Text + "', peer '" + peerText + "'");
			c.Report.Check("Invoke runs the delegate on the UI thread and returns its value to the worker", invokedOn == c.UiThreadId && Equals(invokeResult, 42), "ran on " + invokedOn + " (UI " + c.UiThreadId + "), result " + invokeResult);
			c.Report.Check("BeginInvoke/EndInvoke run on the UI thread", begunOn == c.UiThreadId);
			d.Form.Close();
			c.Report.Check("Close() from the worker side ends the dialog", await c.Within(t, "M08") == SWF.DialogResult.Cancel);
			await c.Ui(() => d.Form.Dispose());
		}

		[Case("M09", "Modeless Show returns at once and the form stays alive; closing it disposes it")]
		public static async Task M09(Ctx c)
		{
			Dlg d = Build("M09");
			List<string> log = new List<string>();
			d.Form.FormClosed += (s, e) => log.Add("closed");
			await c.Ui(() => d.Form.Show(c.MainForm));
			GC.Collect();
			GC.WaitForPendingFinalizers();
			GC.Collect();
			c.Report.Check("a modeless form is registered, visible, and survives GC", SWF.FormsRuntime.OpenForms.Contains(d.Form) && await c.Ui(() => AvaloniaBackend.WindowOf(d.Form).IsVisible));
			c.Report.Check("a modeless form is not modal", !d.Form.Modal);
			await c.Click(d.Ok);
			c.Report.Check("a button's DialogResult does not close a modeless form (WinForms only closes modal forms that way)", d.Form.IsShown && !d.Form.IsDisposed && log.Count == 0);
			d.Form.Close();
			c.Report.Check("Close() raises FormClosed, hides and disposes a modeless form", d.Form.IsDisposed && log.Count == 1 && !SWF.FormsRuntime.OpenForms.Contains(d.Form), "disposed=" + d.Form.IsDisposed + " closed events=" + log.Count);
		}

		[Case("M10", "Modality: while a dialog is open the owner window does not take clicks (headless: informational)")]
		public static async Task M10(Ctx c)
		{
			Dlg d = Build("M10");
			SWF.Button mainButton = c.MainForm.Controls.OfType<SWF.Button>().First(b => b.Name == "mainButton");
			int clicks = 0;
			EventHandler counter = (s, e) => clicks++;
			mainButton.Click += counter;
			Task<SWF.DialogResult> t = c.UiAsync(() => d.Form.ShowDialog(c.MainForm));
			await c.WaitForForm(f => ReferenceEquals(f, d.Form), "M10");
			Window owner = AvaloniaBackend.WindowOf(c.MainForm);
			bool enabled = await c.Ui(() => owner.IsEffectivelyEnabled);
			c.Report.Info("owner window IsEffectivelyEnabled while the dialog is open", enabled.ToString());
			if (!c.Real)
			{
				await c.Click(mainButton);
				c.Report.Info("a pointer click on the owner's button during the dialog", clicks == 0 ? "ignored (blocked)" : "handled (the headless platform does not block the owner)");
			}
			else
			{
				c.Report.Skip("pointer click on the owner during the dialog", "done by real.sh through ui-drive (interactive scenario)");
			}
			await c.Click(d.Cancel);
			await c.Within(t, "M10");
			mainButton.Click -= counter;
			await c.Ui(() => d.Form.Dispose());
		}

		private static T Visual<T>(SWF.Control c) where T : Avalonia.Controls.Control => Ctx.Visual<T>(c);
	}
}
