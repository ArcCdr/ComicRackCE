using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using S6.Backend;
using S6.Host;
using SWF = System.Windows.Forms;

namespace S6.Cases
{
	/// <summary>Application shutdown while a script dialog is open (the user quits with Cmd+Q).</summary>
	public static class ShutdownCase
	{
		public static async Task Run(Ctx c, IClassicDesktopStyleApplicationLifetime desktop)
		{
			ModalCases.Dlg d = ModalCases.Build("shutdown dialog");
			Task<SWF.DialogResult> t = c.UiAsync(() =>
			{
				SWF.DialogResult r = d.Form.ShowDialog(c.MainForm);
				Console.WriteLine("SHUTDOWN ShowDialog returned " + r + " (the nested frame ended)");
				return r;
			});
			await c.WaitForForm(f => ReferenceEquals(f, d.Form), "shutdown dialog");
			Console.WriteLine("SHUTDOWN dialog is open, requesting application shutdown");
			Thread watchdog = new Thread(() =>
			{
				Thread.Sleep(15000);
				Console.WriteLine("SHUTDOWN HANG: the process did not exit within 15 s of the shutdown request");
				Environment.Exit(9);
			})
			{ IsBackground = true };
			watchdog.Start();
			await c.Ui(() => desktop.Shutdown(0));
			Console.WriteLine("SHUTDOWN Shutdown() returned on the UI thread; waiting for the process to end");
			await Task.Delay(Timeout.Infinite);
		}
	}

	/// <summary>An exception that escapes a job on the UI thread: what Avalonia does with it, with and without Dispatcher.UnhandledException.</summary>
	public static class UnhandledCase
	{
		public static async Task Run(Ctx c, IClassicDesktopStyleApplicationLifetime desktop, bool handler)
		{
			if (handler)
			{
				Dispatcher.UIThread.UnhandledException += (s, e) =>
				{
					Console.WriteLine("UNHANDLED handler got " + e.Exception.GetType().Name + ": " + e.Exception.Message + " (marking it handled)");
					e.Handled = true;
				};
			}
			Console.WriteLine("UNHANDLED posting a job that throws" + (handler ? ", with a Dispatcher.UnhandledException handler" : ", without a handler"));
			Dispatcher.UIThread.Post(() => throw new InvalidOperationException("thrown by a job"));
			await c.Ui(() => { });
			await Task.Delay(300);
			Console.WriteLine("UNHANDLED the UI thread is still running afterwards");
			await c.Ui(() => desktop.Shutdown(0));
			await Task.Delay(Timeout.Infinite);
		}
	}

	/// <summary>
	/// The scenario that real.sh drives through ui-drive: dialogs that wait for real keys and clicks. Every step prints READY before it
	/// waits and RESULT when it is done, so the shell can synchronise on the log.
	/// </summary>
	public static class Interactive
	{
		/// <summary>With <paramref name="auto"/> the scenario answers its own dialogs (keys through the input pipeline of the headless platform), to test its own plumbing without a person or ui-drive.</summary>
		public static async Task Run(Ctx c, bool auto)
		{
			ComicRackStub cr = c.ComicRack;
			PythonScript autonumber = null;
			PythonScript sample = null;
			await c.Ui(() =>
			{
				autonumber = new PythonScript(Path.Combine(c.ScriptsDir, "Autonumber.py"), cr);
				sample = new PythonScript(Path.Combine(c.ScriptsDir, "Sample.py"), cr);
			});
			Action<Book[]> renumber = autonumber.Get<Action<Book[]>>("RenumberBooks");
			Func<bool, bool> shutdown = sample.Get<Func<bool, bool>>("ShowShutdownMessage");

			// A: Autonumber from the UI thread. The shell clicks the owner window (blocked), then presses Return.
			Book[] a = Enumerable.Range(0, 3).Select(i => new Book { ShadowNumber = "7", Number = "x" }).ToArray();
			Console.WriteLine("READY A autonumber dialog, UI thread");
			Task ta = c.UiAsync(() =>
			{
				renumber(a);
				return 0;
			});
			if (auto)
			{
				await c.Press(await c.WaitForForm("Autonumber Wizard"), Avalonia.Input.Key.Enter, Avalonia.Input.PhysicalKey.Enter);
			}
			await ta;
			Console.WriteLine("RESULT A numbers=" + string.Join(",", a.Select(b => b.Number)));

			// B: Autonumber from a worker thread; the shell presses Escape.
			Book[] b2 = Enumerable.Range(0, 3).Select(i => new Book { ShadowNumber = "7", Number = "x" }).ToArray();
			Console.WriteLine("READY B autonumber dialog, worker thread");
			Task tb = Task.Run(() => renumber(b2));
			if (auto)
			{
				await c.Press(await c.WaitForForm("Autonumber Wizard"), Avalonia.Input.Key.Escape, Avalonia.Input.PhysicalKey.Escape);
			}
			await tb;
			Console.WriteLine("RESULT B numbers=" + string.Join(",", b2.Select(b => b.Number)));

			// C: the shutdown script's Yes/No box from a worker thread; the shell presses Return (Yes).
			Console.WriteLine("READY C message box YesNo, worker thread");
			Task<bool> tc = Task.Run(() => shutdown(true));
			if (auto)
			{
				await c.Press(await c.WaitForForm("Shutdown Script"), Avalonia.Input.Key.Enter, Avalonia.Input.PhysicalKey.Enter);
			}
			bool answer = await tc;
			Console.WriteLine("RESULT C answer=" + answer);

			// D: a non-modal window with a Python control that draws itself; it closes itself after a few seconds.
			PythonScript probe = null;
			SWF.Form host = null;
			await c.Ui(() =>
			{
				probe = new PythonScript(Path.Combine(c.SpikeScriptsDir, "s6-probe.py"), cr);
				SWF.Control swatch = (SWF.Control)probe.Engine.Operations.Invoke(probe.Scope.GetVariable("make_swatch"));
				host = new SWF.Form { Name = "panel", Text = "S6 owner-draw panel", ClientSize = new System.Drawing.Size(180, 130), StartPosition = SWF.FormStartPosition.CenterParent };
				host.Controls.Add(swatch);
				host.Show(c.MainForm);
			});
			Console.WriteLine("READY D owner-draw panel");
			await Task.Delay(auto ? 200 : 4000);
			await c.Ui(() => host.Close());
			Console.WriteLine("RESULT D closed");
			Console.WriteLine("DONE");
		}
	}
}
