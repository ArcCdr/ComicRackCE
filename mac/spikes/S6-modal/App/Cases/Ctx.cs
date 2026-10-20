using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using S6.Backend;
using S6.Host;
using SWF = System.Windows.Forms;

namespace S6.Cases
{
	/// <summary>One line per check: PASS, FAIL, INFO or SKIP. Thread safe; also written to a file.</summary>
	public sealed class Report
	{
		private readonly object gate = new object();
		private readonly List<string> lines = new List<string>();
		private readonly TextWriter console;
		public int Failures;
		public int Passes;

		public Report(TextWriter console)
		{
			this.console = console;
		}

		public string Case { get; set; } = string.Empty;

		public void Line(string kind, string name, string detail)
		{
			string text = kind + " " + (Case.Length > 0 ? Case + " " : string.Empty) + name + (string.IsNullOrEmpty(detail) ? string.Empty : ": " + detail);
			lock (gate)
			{
				lines.Add(text);
				if (kind == "FAIL")
				{
					Failures++;
				}
				else if (kind == "PASS")
				{
					Passes++;
				}
				console.WriteLine(text);
				console.Flush();
			}
		}

		public void Check(string name, bool ok, string detail = null) => Line(ok ? "PASS" : "FAIL", name, detail);

		public void Info(string name, string detail = null) => Line("INFO", name, detail);

		public void Skip(string name, string detail = null) => Line("SKIP", name, detail);

		public void Save(string path)
		{
			lock (gate)
			{
				File.WriteAllLines(path, lines);
			}
		}
	}

	[AttributeUsage(AttributeTargets.Method)]
	public sealed class CaseAttribute : Attribute
	{
		public string Id { get; }

		public string Title { get; }

		public bool Interactive { get; set; }

		public CaseAttribute(string id, string title)
		{
			Id = id;
			Title = title;
		}
	}

	/// <summary>Everything a case needs: marshalling to the UI thread, waiting with a hang deadline, driving peers, snapshots.</summary>
	public sealed class Ctx
	{
		public Report Report { get; }

		public AvaloniaBackend Backend { get; }

		public bool Real { get; }

		public string OutDir { get; }

		public string ScriptsDir { get; }

		public string SpikeScriptsDir { get; }

		public ComicRackStub ComicRack { get; } = new ComicRackStub();

		public SWF.Form MainForm { get; private set; }

		public int UiThreadId { get; private set; }

		public Ctx(Report report, AvaloniaBackend backend, bool real, string outDir, string scriptsDir, string spikeScriptsDir)
		{
			Report = report;
			Backend = backend;
			Real = real;
			OutDir = outDir;
			ScriptsDir = scriptsDir;
			SpikeScriptsDir = spikeScriptsDir;
		}

		public static readonly TimeSpan HangLimit = TimeSpan.FromSeconds(25);

		// ---------------------------------------------------------------- threads

		public Task<T> Ui<T>(Func<T> f) => Dispatcher.UIThread.InvokeAsync(f).GetTask();

		public Task Ui(Action a) => Dispatcher.UIThread.InvokeAsync(a).GetTask();

		/// <summary>Starts a job on the UI thread and returns its task; the job may run a nested loop for a long time.</summary>
		public Task<T> UiAsync<T>(Func<T> f) => Dispatcher.UIThread.InvokeAsync(f, DispatcherPriority.Normal).GetTask();

		public async Task<T> Within<T>(Task<T> task, string what)
		{
			Task done = await Task.WhenAny(task, Task.Delay(HangLimit));
			if (done != task)
			{
				throw new TimeoutException("hang: " + what);
			}
			return await task;
		}

		public async Task Within(Task task, string what)
		{
			Task done = await Task.WhenAny(task, Task.Delay(HangLimit));
			if (done != task)
			{
				throw new TimeoutException("hang: " + what);
			}
			await task;
		}

		/// <summary>Polls <paramref name="condition"/> (from this pool thread, so it must be thread safe) until true. The deadline only bounds a hang.</summary>
		public async Task WaitFor(Func<bool> condition, string what)
		{
			DateTime end = DateTime.UtcNow + HangLimit;
			while (!condition())
			{
				if (DateTime.UtcNow > end)
				{
					throw new TimeoutException("hang: " + what);
				}
				await Task.Delay(2);
			}
		}

		public async Task<SWF.Form> WaitForForm(Func<SWF.Form, bool> predicate, string what)
		{
			SWF.Form found = null;
			await WaitFor(() => (found = SWF.FormsRuntime.OpenForms.FirstOrDefault(predicate)) != null, "form " + what);
			// The facade raises Shown after the peer is up; make sure the UI thread has processed the show.
			await Ui(() => { });
			return found;
		}

		public async Task<SWF.Form> WaitForForm(string name) => await WaitForForm(f => f.Name == name || f.Text == name, name);

		public async Task Initialize()
		{
			await Ui(() =>
			{
				UiThreadId = Environment.CurrentManagedThreadId;
				MainForm = new SWF.Form { Name = "main", Text = "S6 main window (ComicRack.MainWindow)", ClientSize = new System.Drawing.Size(420, 200), StartPosition = SWF.FormStartPosition.CenterScreen };
				SWF.Label l = new SWF.Label { Name = "clicks", Text = "clicks: 0", Location = new System.Drawing.Point(20, 20), Size = new System.Drawing.Size(200, 20) };
				SWF.Button b = new SWF.Button { Name = "mainButton", Text = "Count a click", Location = new System.Drawing.Point(20, 60), Size = new System.Drawing.Size(140, 30) };
				int clicks = 0;
				b.Click += (s, e) =>
				{
					clicks++;
					l.Text = "clicks: " + clicks;
					Console.WriteLine("MAIN-CLICK " + clicks);
				};
				MainForm.Controls.Add(l);
				MainForm.Controls.Add(b);
				MainForm.Show();
				Backend.DefaultOwner = AvaloniaBackend.WindowOf(MainForm);
				ComicRack.MainWindow = MainForm;
			});
		}

		// ---------------------------------------------------------------- driving peers

		public static T Visual<T>(SWF.Control c) where T : Avalonia.Controls.Control
		{
			return (T)AvaloniaBackend.VisualOf(c);
		}

		/// <summary>
		/// A click on a button peer. Headless: through the window's input pipeline (pointer down and up at the button's centre). Real window: the Click event.
		/// The returned task completes when the whole click has been handled, which includes any modal loop the handler runs:
		/// use <see cref="ClickAsync"/> for a click whose handler shows a dialog.
		/// </summary>
		public Task Click(SWF.Control button)
		{
			return Ui(() => DoClick(button));
		}

		/// <summary>Posts the click and returns at once (the handler may run a modal loop that only ends when the test answers it).</summary>
		public Task ClickAsync(SWF.Control button)
		{
			return Task.FromResult(Dispatcher.UIThread.InvokeAsync(() => DoClick(button)).GetTask());
		}

		private void DoClick(SWF.Control button)
		{
			Button visual = Visual<Button>(button);
			Window window = TopLevel.GetTopLevel(visual) as Window;
			if (!Real && window != null && visual.IsEffectivelyEnabled && visual.IsEffectivelyVisible)
			{
				Point? center = visual.TranslatePoint(new Point(visual.Bounds.Width / 2, visual.Bounds.Height / 2), window);
				if (center.HasValue)
				{
					window.MouseMove(center.Value);
					window.MouseDown(center.Value, MouseButton.Left);
					window.MouseUp(center.Value, MouseButton.Left);
					return;
				}
			}
			visual.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
		}

		public Task Press(SWF.Form form, Key key, PhysicalKey physical)
		{
			return Ui(() =>
			{
				Window window = AvaloniaBackend.WindowOf(form);
				if (!Real)
				{
					window.KeyPress(key, RawInputModifiers.None, physical, null);
					if (form.IsShown)
					{
						window.KeyRelease(key, RawInputModifiers.None, physical, null);
					}
				}
				else
				{
					form.PeerKey(key == Key.Enter);
				}
			});
		}

		public Task SetText(SWF.TextBox box, string text) => Ui(() => Visual<TextBox>(box).Text = text);

		public Task SetValue(SWF.NumericUpDown box, decimal value) => Ui(() => Visual<NumericUpDown>(box).Value = value);

		public Task SetChecked(SWF.CheckBox box, bool value) => Ui(() => Visual<CheckBox>(box).IsChecked = value);

		public Task SelectIndex(SWF.ListControl box, int index) => Ui(() => Visual<Avalonia.Controls.Primitives.SelectingItemsControl>(box).SelectedIndex = index);

		public IEnumerable<SWF.Control> Descendants(SWF.Control root)
		{
			foreach (SWF.Control child in root.Controls.OfType<SWF.Control>())
			{
				yield return child;
				foreach (SWF.Control d in Descendants(child))
				{
					yield return d;
				}
			}
		}

		public string Dump(SWF.Control root)
		{
			StringBuilder sb = new StringBuilder();
			sb.Append(root.GetType().Name).Append(" '").Append(root.Name).Append("' ").Append(root.Bounds).Append(" \"").Append(root.Text).Append('"');
			foreach (SWF.Control child in root.Controls.OfType<SWF.Control>())
			{
				sb.Append("\n  ").Append(Dump(child).Replace("\n", "\n  "));
			}
			return sb.ToString();
		}

		// ---------------------------------------------------------------- snapshots

		/// <summary>Renders a window (without the title bar) to a PNG with the compositor, so the same call works headless and in a real window.</summary>
		public Task<string> Snapshot(SWF.Form form, string name)
		{
			return Ui(() =>
			{
				Window window = AvaloniaBackend.WindowOf(form);
				return SnapshotWindow(window, name);
			});
		}

		public string SnapshotWindow(Window window, string name)
		{
			double scale = window.RenderScaling;
			int w = Math.Max(1, (int)Math.Ceiling(window.Bounds.Width * scale));
			int h = Math.Max(1, (int)Math.Ceiling(window.Bounds.Height * scale));
			using (RenderTargetBitmap bitmap = new RenderTargetBitmap(new PixelSize(w, h), new Vector(96 * scale, 96 * scale)))
			{
				bitmap.Render(window);
				string path = Path.Combine(OutDir, (Real ? "real-" : "headless-") + name + ".png");
				Directory.CreateDirectory(OutDir);
				using (FileStream fs = File.Create(path)) { bitmap.Save(fs); }
				return path;
			}
		}

		public static (byte r, byte g, byte b) Pixel(string png, int x, int y)
		{
			using (SkiaSharp.SKBitmap bitmap = SkiaSharp.SKBitmap.Decode(png))
			{
				SkiaSharp.SKColor c = bitmap.GetPixel(x, y);
				return (c.Red, c.Green, c.Blue);
			}
		}

		// ---------------------------------------------------------------- bookkeeping

		public async Task Cleanup()
		{
			await Ui(() =>
			{
				foreach (SWF.Form f in SWF.FormsRuntime.OpenForms.ToArray())
				{
					if (!ReferenceEquals(f, MainForm))
					{
						try
						{
							f.Dispose();
						}
						catch (Exception)
						{
						}
					}
				}
			});
			lock (SWF.Application.Unhandled)
			{
				SWF.Application.Unhandled.Clear();
			}
		}

		public static string Describe(Exception e)
		{
			StringBuilder sb = new StringBuilder();
			for (Exception x = e; x != null; x = x.InnerException)
			{
				if (sb.Length > 0)
				{
					sb.Append(" <- ");
				}
				sb.Append(x.GetType().Name).Append(": ").Append(x.Message.Replace('\n', ' ').Replace('\r', ' '));
			}
			return sb.ToString();
		}
	}
}
