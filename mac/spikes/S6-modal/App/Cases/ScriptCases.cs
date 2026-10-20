using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using S6.Backend;
using S6.Host;
using SWF = System.Windows.Forms;

namespace S6.Cases
{
	/// <summary>Python cases: a Control subclass with owner draw, handlers in Python, and the two bundled UI scripts run unmodified.</summary>
	public static class ScriptCases
	{
		private static T Visual<T>(SWF.Control c) where T : Avalonia.Controls.Control => Ctx.Visual<T>(c);

		private static PythonScript Load(Ctx c, string file)
		{
			return new PythonScript(file, c.ComicRack);
		}

		private static T Member<T>(PythonScript ps, object target, string name)
		{
			return ps.Engine.Operations.GetMember<T>(target, name);
		}

		[Case("P01", "A Python subclass of Control overrides OnPaint and OnResize; its drawing reaches the frame; Invalidate repaints")]
		public static async Task P01(Ctx c)
		{
			PythonScript ps = null;
			SWF.Control swatch = null;
			SWF.Form host = null;
			await c.Ui(() =>
			{
				ps = Load(c, Path.Combine(c.SpikeScriptsDir, "s6-probe.py"));
				swatch = (SWF.Control)ps.Engine.Operations.Invoke(ps.Scope.GetVariable("make_swatch"));
				host = new SWF.Form { Name = "P01 host", Text = "P01 host", ClientSize = new Size(180, 130), FormBorderStyle = SWF.FormBorderStyle.FixedDialog, StartPosition = SWF.FormStartPosition.CenterParent };
				host.Controls.Add(swatch);
				host.Show(c.MainForm);
			});
			c.Report.Check("the instance is an IronPython new-style subclass of the facade Control", swatch.GetType().FullName.StartsWith("IronPython.NewTypes.System.Windows.Forms.Control_", StringComparison.Ordinal), swatch.GetType().FullName);
			string png = await c.Snapshot(host, "P01-swatch");
			int paints = await c.Ui(() => Member<int>(ps, swatch, "paints"));
			c.Report.Check("OnPaint, overridden in Python, was called by the peer", paints >= 1, "paints=" + paints);
			Rectangle clip = await c.Ui(() => Member<Rectangle>(ps, swatch, "clip"));
			c.Report.Check("PaintEventArgs.ClipRectangle is the control's client area", clip == new Rectangle(0, 0, 120, 80), clip.ToString());
			double scale = await c.Ui(() => AvaloniaBackend.WindowOf(host).RenderScaling);
			(byte r, byte g, byte b) = Ctx.Pixel(png, (int)((20 + 10 + 25) * scale), (int)((20 + 10 + 20) * scale));
			c.Report.Check("the pixels drawn by the Python FillRectangle are in the window frame", Math.Abs(r - 220) <= 2 && Math.Abs(g - 30) <= 2 && Math.Abs(b - 30) <= 2, "rgb " + r + "," + g + "," + b + " at scale " + scale);
			string baseCall = await c.Ui(() => Member<string>(ps, swatch, "base_call"));
			c.Report.Info("a Python override calling the base method Control.OnPaint(self, e)", baseCall);

			// Invalidate: change the colour in Python, invalidate, render again.
			await c.Ui(() =>
			{
				ps.Engine.Operations.SetMember(swatch, "blue", 200);
				ps.Engine.Operations.SetMember(swatch, "red", 30);
				swatch.Invalidate();
			});
			string png2 = await c.Snapshot(host, "P01-swatch-blue");
			(byte r2, byte g2, byte b2) = Ctx.Pixel(png2, (int)(55 * scale), (int)(50 * scale));
			c.Report.Check("Invalidate() repaints: the new colour shows", Math.Abs(r2 - 30) <= 2 && Math.Abs(b2 - 200) <= 2, "rgb " + r2 + "," + g2 + "," + b2);

			// Resize.
			await c.Ui(() => swatch.Size = new Size(100, 60));
			int resizes = await c.Ui(() => Member<int>(ps, swatch, "resizes"));
			c.Report.Check("OnResize, overridden in Python, is called when the size changes", resizes >= 1, "resizes=" + resizes);

			// An exception in OnPaint is reported and does not break the frame.
			List<Exception> seen = new List<Exception>();
			ThreadExceptionHandler handler = (s, e) => seen.Add(e.Exception);
			SWF.Application.ThreadException += handler.Invoke;
			await c.Ui(() =>
			{
				ps.Engine.Operations.SetMember(swatch, "fail", true);
				swatch.Invalidate();
			});
			await c.Snapshot(host, "P01-swatch-failing");
			SWF.Application.ThreadException -= handler.Invoke;
			c.Report.Check("an exception in the Python OnPaint reaches Application.ThreadException and the app keeps rendering", seen.Count >= 1 && seen[0].Message.Contains("OnPaint failed"), seen.Count + " seen: " + (seen.Count > 0 ? seen[0].Message : ""));
			await c.Ui(() => host.Dispose());
		}

		private delegate void ThreadExceptionHandler(object sender, ThreadExceptionEventArgs e);

		[Case("P04", "Cost of the Python OnPaint path (information): dispatch into the override, FillRectangle, bitmap round trip")]
		public static async Task P04(Ctx c)
		{
			PythonScript ps = null;
			SWF.Control swatch = null;
			await c.Ui(() =>
			{
				ps = Load(c, Path.Combine(c.SpikeScriptsDir, "s6-probe.py"));
				swatch = (SWF.Control)ps.Engine.Operations.Invoke(ps.Scope.GetVariable("make_swatch"));
			});
			double dispatchMicros = 0, paintMicros = 0;
			await c.Ui(() =>
			{
				const int n = 2000;
				using (Bitmap bitmap = new Bitmap(240, 160))
				using (Graphics g = Graphics.FromImage(bitmap))
				{
					for (int i = 0; i < 100; i++)
					{
						swatch.PeerPaint(g, new Rectangle(0, 0, 120, 80));
					}
					System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
					for (int i = 0; i < n; i++)
					{
						swatch.PeerPaint(g, new Rectangle(0, 0, 120, 80));
					}
					dispatchMicros = sw.Elapsed.TotalMilliseconds * 1000 / n;
					sw.Restart();
					for (int i = 0; i < n; i++)
					{
						using (Bitmap b2 = new Bitmap(240, 160, System.Drawing.Imaging.PixelFormat.Format32bppPArgb))
						using (Graphics g2 = Graphics.FromImage(b2))
						{
							g2.ScaleTransform(2, 2);
							swatch.PeerPaint(g2, new Rectangle(0, 0, 120, 80));
						}
					}
					paintMicros = sw.Elapsed.TotalMilliseconds * 1000 / n;
				}
			});
			c.Report.Info("Python OnPaint (one FillRectangle of the stand-in Graphics) called through the facade", dispatchMicros.ToString("F0") + " microseconds per call on a reused bitmap; " + paintMicros.ToString("F0") + " microseconds per call with a new 240x160 bitmap and Graphics each time (what the peer does per frame)");
			c.Report.Check("the cost is far below one 60 Hz frame (16 ms) for a single control", paintMicros < 16000, paintMicros.ToString("F0") + " us");
		}

		[Case("P02", "A Paint event handler assigned from Python draws with the stand-in Graphics")]
		public static async Task P02(Ctx c)
		{
			PythonScript ps = null;
			SWF.Control ctl = null;
			SWF.Form host = null;
			await c.Ui(() =>
			{
				ps = Load(c, Path.Combine(c.SpikeScriptsDir, "s6-probe.py"));
				ctl = (SWF.Control)ps.Engine.Operations.Invoke(ps.Scope.GetVariable("make_paint_event_control"));
				ps.Engine.Operations.Invoke(ps.Scope.GetVariable("attach_paint_handler"), ctl);
				host = new SWF.Form { Name = "P02 host", Text = "P02 host", ClientSize = new Size(180, 190), FormBorderStyle = SWF.FormBorderStyle.FixedDialog, StartPosition = SWF.FormStartPosition.CenterParent };
				host.Controls.Add(ctl);
				host.Show(c.MainForm);
			});
			string png = await c.Snapshot(host, "P02-paint-event");
			double scale = await c.Ui(() => AvaloniaBackend.WindowOf(host).RenderScaling);
			(byte r, byte g, byte b) = Ctx.Pixel(png, (int)((20 + 10) * scale), (int)((110 + 10) * scale));
			c.Report.Check("the Paint handler of a plain Control, written in Python, drew a green rectangle", Math.Abs(r - 20) <= 2 && Math.Abs(g - 160) <= 2 && Math.Abs(b - 40) <= 2, "rgb " + r + "," + g + "," + b);
			await c.Ui(() => host.Dispose());
		}

		[Case("P03", "Python handlers: a MessageBox inside a Click handler inside a modal dialog; a failing handler; ShowDialog from a Python thread")]
		public static async Task P03(Ctx c)
		{
			PythonScript ps = null;
			SWF.Form form = null;
			await c.Ui(() =>
			{
				ps = Load(c, Path.Combine(c.SpikeScriptsDir, "s6-probe.py"));
				form = (SWF.Form)ps.Engine.Operations.Invoke(ps.Scope.GetVariable("build_ask_form"));
			});
			List<Exception> seen = new List<Exception>();
			ThreadExceptionHandler handler = (s, e) => seen.Add(e.Exception);
			SWF.Application.ThreadException += handler.Invoke;
			Task<SWF.DialogResult> t = c.UiAsync(() => form.ShowDialog(c.MainForm));
			await c.WaitForForm("Ask");
			SWF.Button ask = form.Controls.OfType<SWF.Button>().First(b => b.Name == "askButton");
			SWF.Button bad = form.Controls.OfType<SWF.Button>().First(b => b.Name == "badButton");
			SWF.Button done = form.Controls.OfType<SWF.Button>().First(b => b.Name == "doneButton");
			await c.ClickAsync(ask);
			SWF.Form box = await c.WaitForForm("Confirm");
			c.Report.Check("a Python Click handler opened a MessageBox: two nested frames, the outer ShowDialog is still waiting", !t.IsCompleted && box.Modal);
			await c.Click(box.Controls.OfType<SWF.Button>().First(b => b.DialogResult == SWF.DialogResult.Yes));
			await c.WaitFor(() => form.Tag != null, "handler continued after MessageBox.Show");
			c.Report.Check("the Python handler continued with the MessageBox result", form.Tag is SWF.DialogResult dr && dr == SWF.DialogResult.Yes, "Tag=" + form.Tag);
			await c.Click(bad);
			c.Report.Check("a Python handler that raises: ThreadException got it, dialog open", seen.Count == 1 && !t.IsCompleted, seen.Count + " seen: " + (seen.Count > 0 ? seen[0].GetType().Name + ": " + seen[0].Message : ""));
			SWF.Application.ThreadException -= handler.Invoke;
			await c.Click(done);
			c.Report.Check("the dialog closes through the button written in Python (DialogResult OK)", await c.Within(t, "P03") == SWF.DialogResult.OK);
			await c.Ui(() => form.Dispose());

			// A script that runs on its own thread and shows a dialog from there.
			SWF.Form form2 = null;
			await c.Ui(() => form2 = (SWF.Form)ps.Engine.Operations.Invoke(ps.Scope.GetVariable("build_ask_form")));
			int ui = c.UiThreadId;
			Task<object> worker = Task.Run(() => ps.Engine.Operations.Invoke(ps.Scope.GetVariable("run_in_thread"), (Func<object>)(() => form2.ShowDialog(c.MainForm))));
			await c.WaitForForm("Ask");
			c.Report.Check("a .NET Thread started from Python can show a dialog; it blocks there", !worker.IsCompleted);
			await c.Click(form2.Controls.OfType<SWF.Button>().First(b => b.Name == "doneButton"));
			object result = await c.Within(worker, "P03 python thread");
			c.Report.Check("the Python thread got the DialogResult", result is SWF.DialogResult r && r == SWF.DialogResult.OK, "result=" + result);
			await c.Ui(() => form2.Dispose());
		}

		[Case("S01", "Autonumber.py, unmodified: RenumberBooks shows its dialog; values typed in the peers renumber the books (UI thread, worker thread, Cancel, Enter)")]
		public static async Task S01(Ctx c)
		{
			string path = Path.Combine(c.ScriptsDir, "Autonumber.py");
			PythonScript ps = null;
			await c.Ui(() => ps = Load(c, path));
			c.Report.Check("the script loaded: clr.AddReferenceByPartialName('System.Windows.Forms') and ('System.Drawing') resolved", ps != null);
			Action<Book[]> renumber = ps.Get<Action<Book[]>>("RenumberBooks");

			Book[] books = Enumerable.Range(0, 3).Select(i => new Book { ShadowSeries = "Alpha", ShadowNumber = "7", Number = (7 + i).ToString() }).ToArray();
			// 1. UI thread, OK by pointer click, second combo item (alternate series) and the total check box.
			Task t = c.UiAsync(() =>
			{
				renumber(books);
				return 0;
			});
			SWF.Form dlg = await c.WaitForForm("Autonumber Wizard");
			c.Report.Info("control tree of the dialog", "\n" + c.Dump(dlg));
			SWF.NumericUpDown start = dlg.Controls.OfType<SWF.NumericUpDown>().First(n => n.TabIndex == 1);
			SWF.NumericUpDown end = dlg.Controls.OfType<SWF.NumericUpDown>().First(n => n.TabIndex == 3);
			SWF.CheckBox total = dlg.Controls.OfType<SWF.CheckBox>().First();
			SWF.ComboBox combo = dlg.Controls.OfType<SWF.ComboBox>().First();
			SWF.Button ok = dlg.Controls.OfType<SWF.Button>().First(b => b.DialogResult == SWF.DialogResult.OK);
			SWF.Button cancel = dlg.Controls.OfType<SWF.Button>().First(b => b.DialogResult == SWF.DialogResult.Cancel);
			c.Report.Check("the dialog is the script's: title, client size, button set, AcceptButton/CancelButton", dlg.Text == "Autonumber Wizard" && dlg.ClientSize == new Size(243, 154) && ReferenceEquals(dlg.AcceptButton, ok) && ReferenceEquals(dlg.CancelButton, cancel) && dlg.Modal, dlg.Text + " " + dlg.ClientSize);
			c.Report.Check("the script's defaults are in the controls (Begin 7, End 9, series, 2 combo items)", start.Value == 7 && end.Value == 9 && combo.SelectedIndex == 0 && combo.Items.Count == 2 && !total.Checked, "start=" + start.Value + " end=" + end.Value);
			c.Report.Check("ComicRack.Theme.ApplyTheme(form) was called through the stub (ProductVersion >= '0.9.182')", c.ComicRack.Theme.Applied >= 1);
			c.Report.Info("snapshot", System.IO.Path.GetFileName(await c.Snapshot(dlg, "S01-autonumber")));
			await c.SetValue(start, 10);
			await c.SetValue(end, 12);
			await c.SelectIndex(combo, 1);
			await c.SetChecked(total, true);
			await c.Click(ok);
			await c.Within(t, "S01 first run");
			c.Report.Check("OK renumbered the alternate numbers 10, 11, 12 and the total 12 (decimal assigned to int properties by IronPython)", books.Select(b => b.AlternateNumber).SequenceEqual(new[] { "10", "11", "12" }) && books.All(b => b.AlternateCount == 12) && books.All(b => b.Number != "10"), string.Join(",", books.Select(b => b.AlternateNumber + "/" + b.AlternateCount + "/" + b.Number)));

			// 2. Escape cancels: nothing changes.
			Book[] books2 = Enumerable.Range(0, 2).Select(i => new Book { ShadowNumber = "1", Number = "1" }).ToArray();
			t = c.UiAsync(() =>
			{
				renumber(books2);
				return 0;
			});
			dlg = await c.WaitForForm("Autonumber Wizard");
			await c.SetValue(dlg.Controls.OfType<SWF.NumericUpDown>().First(n => n.TabIndex == 1), 50);
			await c.Press(dlg, Key.Escape, PhysicalKey.Escape);
			await c.Within(t, "S01 escape");
			c.Report.Check("Escape (CancelButton) leaves the books unchanged", books2.All(b => b.Number == "1"), string.Join(",", books2.Select(b => b.Number)));

			// 3. Worker thread (the way a Books hook runs), Enter accepts: series numbers.
			Book[] books3 = Enumerable.Range(0, 3).Select(i => new Book { ShadowNumber = "100", Number = "x" }).ToArray();
			int callerThread = 0, afterThread = 0;
			Task worker = Task.Run(() =>
			{
				callerThread = Environment.CurrentManagedThreadId;
				renumber(books3);
				afterThread = Environment.CurrentManagedThreadId;
			});
			dlg = await c.WaitForForm("Autonumber Wizard");
			c.Report.Check("the script runs on a worker thread and is blocked in ShowDialog", !worker.IsCompleted && callerThread != c.UiThreadId);
			await c.Press(dlg, Key.Enter, PhysicalKey.Enter);
			await c.Within(worker, "S01 worker");
			c.Report.Check("Enter (AcceptButton) accepted; the script continued on its own thread and numbered 100, 101, 102", books3.Select(b => b.Number).SequenceEqual(new[] { "100", "101", "102" }) && afterThread == callerThread, string.Join(",", books3.Select(b => b.Number)));
			c.Report.Info("script output", ps.Printed.Length == 0 ? "(none)" : ps.Printed);
		}

		[Case("S02", "Sample.py, unmodified: the MessageBox hooks, the Control subclass UIControl hosted as an info panel, ShowInfo")]
		public static async Task S02(Ctx c)
		{
			string path = Path.Combine(c.ScriptsDir, "Sample.py");
			PythonScript ps = null;
			await c.Ui(() => ps = Load(c, path));
			c.Report.Check("the script loaded: three import lines, class UIControl(Control) at the top level", ps != null);

			// BookHasBeenOpened: MessageBox.Show(text) from a worker thread.
			Action<Book> opened = ps.Get<Action<Book>>("BookHasBeenOpened");
			Book book = new Book { ShadowSeries = "Batman", Number = "12" };
			Task worker = Task.Run(() => opened(book));
			SWF.Form box = await c.WaitForForm(f => f.Name == "MessageBox", "BookHasBeenOpened box");
			string text = await c.Ui(() => box.Controls.OfType<SWF.Label>().First().Text);
			c.Report.Check("BookHasBeenOpened shows 'opened Batman #12' in a MessageBox and blocks the hook thread", text == "opened Batman #12" && !worker.IsCompleted, text);
			await c.Click(box.Controls.OfType<SWF.Button>().First());
			await c.Within(worker, "S02 opened");

			// ConfigureBookHasBeenOpened (ConfigScript hook), UI thread.
			Action configure = ps.Get<Action>("ConfigureBookHasBeenOpened");
			Task tc = c.UiAsync(() =>
			{
				configure();
				return 0;
			});
			box = await c.WaitForForm(f => f.Name == "MessageBox", "ConfigureBookHasBeenOpened box");
			await c.Press(box, Key.Enter, PhysicalKey.Enter);
			await c.Within(tc, "S02 configure");
			c.Report.Check("ConfigureBookHasBeenOpened: MessageBox on the UI thread closed with Enter", true);

			// ShowShutdownMessage: returns a bool from MessageBox(YesNo) == DialogResult.Yes.
			Func<bool, bool> shutdown = ps.Get<Func<bool, bool>>("ShowShutdownMessage");
			Task<bool> ts = c.UiAsync(() => shutdown(true));
			box = await c.WaitForForm(f => f.Name == "MessageBox", "shutdown box");
			c.Report.Check("the shutdown box has the caption 'Shutdown Script' and Yes/No", box.Text == "Shutdown Script" && box.Controls.OfType<SWF.Button>().Count() == 2, box.Text);
			await c.Click(box.Controls.OfType<SWF.Button>().First(b => b.DialogResult == SWF.DialogResult.No));
			bool answerNo = await c.Within(ts, "S02 shutdown no");
			ts = c.UiAsync(() => shutdown(true));
			box = await c.WaitForForm(f => f.Name == "MessageBox", "shutdown box 2");
			await c.Click(box.Controls.OfType<SWF.Button>().First(b => b.DialogResult == SWF.DialogResult.Yes));
			bool answerYes = await c.Within(ts, "S02 shutdown yes");
			c.Report.Check("ShowShutdownMessage returns False for No and True for Yes (the comparison with DialogResult.Yes works in Python)", !answerNo && answerYes);

			// DummyUIInfoPanel (ComicInfoUI): returns an instance of the Python subclass; the host puts it in a window and calls ShowInfo.
			Func<SWF.Control> panelHook = ps.Get<Func<SWF.Control>>("DummyUIInfoPanel");
			SWF.Control panel = null;
			SWF.Form host = null;
			Book[] books = Enumerable.Range(0, 3).Select(i => new Book { ShadowSeries = "Panel", Number = (i + 1).ToString() }).ToArray();
			await c.Ui(() =>
			{
				panel = panelHook();
				host = new SWF.Form { Name = "info host", Text = "Info panel (ScriptPanelHost)", ClientSize = new Size(320, 200), StartPosition = SWF.FormStartPosition.CenterParent };
				panel.Dock = SWF.DockStyle.Fill;
				host.Controls.Add(panel);
				host.Show(c.MainForm);
				ps.Engine.Operations.InvokeMember(panel, "ShowInfo", (object)books);
			});
			c.Report.Check("the ComicInfoUI hook returned the Python Control subclass", panel.GetType().FullName.StartsWith("IronPython.NewTypes.System.Windows.Forms.Control_", StringComparison.Ordinal), panel.GetType().FullName);
			SWF.ListBox list = panel.Controls.OfType<SWF.ListBox>().First();
			int peerCount = await c.Ui(() => Visual<ListBox>(list).ItemCount);
			c.Report.Check("ShowInfo(books) filled the ListBox child (facade and peer), Dock=Fill gave it the panel's size", list.Items.Count == 3 && peerCount == 3 && list.Size == panel.Size && panel.Size == host.ClientSize, "facade " + list.Items.Count + ", peer " + peerCount + ", list " + list.Size + ", panel " + panel.Size);
			c.Report.Check("print output of the script arrives", ps.Printed.Contains("Calling...3") && ps.Printed.Contains("Adding Panel #1"), ps.Printed.Replace("\r", "").Replace("\n", " | "));
			c.Report.Info("snapshot", System.IO.Path.GetFileName(await c.Snapshot(host, "S02-info-panel")));
			await c.Ui(() => host.Dispose());
		}
	}
}
