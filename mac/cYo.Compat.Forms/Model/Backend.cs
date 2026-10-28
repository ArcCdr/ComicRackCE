using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

// The seam of the script UI layer (MACOS_NATIVE_PATH.md 5.9.3). Everything a script can observe lives in the facade (state, events and their order, layout, modal logic); a
// backend only turns facade controls into toolkit objects (peers) and runs the UI thread. This namespace is not part of the System.Windows.Forms surface: ShimSurfaceTests
// allows exactly these types besides the reference and fails on any other extra type.
namespace cYo.Compat.Forms
{
	/// <summary>
	/// The UI toolkit behind the facade. One backend per process (<see cref="FormsRuntime.Backend"/>); the facade works without a window server on <see cref="NullFormsBackend"/>.
	/// Every method that creates or touches a peer runs on the UI thread: the facade checks <see cref="CheckAccess"/> and posts when it is on another thread.
	/// </summary>
	public interface IFormsBackend
	{
		/// <summary>True on the UI thread.</summary>
		bool CheckAccess();

		/// <summary>Runs <paramref name="action"/> on the UI thread and returns when it has finished (directly when already there). An exception of the action is rethrown to the caller.</summary>
		void Invoke(Action action);

		/// <summary>Queues <paramref name="action"/> on the UI thread and returns at once.</summary>
		void Post(Action action);

		/// <summary>
		/// A nested loop on the UI thread: pumps the queue until <see cref="ModalLoop.Exit"/> was requested, then returns. An exit requested before the loop runs is remembered
		/// (<see cref="ModalLoop.Attach"/> calls the wake-up at once). Must be called on the UI thread; the facade marshals a call from another thread.
		/// </summary>
		void RunModal(ModalLoop loop);

		/// <summary>Application.DoEvents: runs what is queued, then returns. Does nothing off the UI thread.</summary>
		void DoEvents();

		/// <summary>
		/// Creates the toolkit timer behind a <see cref="System.Windows.Forms.Timer"/> (a dispatcher timer on the UI thread). Called on the UI thread; <paramref name="tick"/> is called on the UI thread, never
		/// while a tick is still running, and a timer that is late ticks once and not once per missed interval (the WM_TIMER rule). The timer ticks only between <see cref="ITimerPeer.Start"/>
		/// and <see cref="ITimerPeer.Stop"/> or <see cref="IDisposable.Dispose"/>.
		/// </summary>
		ITimerPeer CreateTimer(Action tick);

		/// <summary>
		/// Creates the peer of a control (a <see cref="Form"/> gets an <see cref="IFormPeer"/>). Called on the UI thread when the control's handle is created, parent before children;
		/// the backend finds the peer of the parent with <see cref="FormsRuntime.PeerOf"/>.
		/// </summary>
		IControlPeer CreatePeer(Control control);

		/// <summary>Size in pixels of <paramref name="text"/> in <paramref name="font"/> (the default font when null), wrapped at <paramref name="maxWidth"/> when it is above 0.</summary>
		Size MeasureText(string text, Font font, int maxWidth);

		/// <summary>The primary screen's bounds in pixels (forms centre on it; 96 dpi logical units).</summary>
		Rectangle GetScreenBounds();

		/// <summary>The state of the modifier keys now (Control.ModifierKeys). Command is reported as Control (5.9.4).</summary>
		Keys GetModifierKeys();

		/// <summary>The pressed mouse buttons now (Control.MouseButtons).</summary>
		MouseButtons GetMouseButtons();

		/// <summary>The mouse position in screen coordinates (Control.MousePosition).</summary>
		Point GetMousePosition();
	}

	/// <summary>What the facade tells a peer. The state itself stays in the facade; a peer is rebuilt from it at every show.</summary>
	public interface IControlPeer : IDisposable
	{
		/// <summary>The facade property <paramref name="property"/> (the WinForms name: Text, Bounds, Enabled, Visible, Font, Checked, SelectedIndex, Items, Value ...) changed.</summary>
		void PropertyChanged(string property);

		/// <summary>The area (client coordinates) must be drawn again.</summary>
		void Invalidate(Rectangle area);

		/// <summary>A child was added or removed at run time (children that exist when the handle is created get their own <see cref="IFormsBackend.CreatePeer"/>).</summary>
		void ChildrenChanged();

		/// <summary>The facade wants the keyboard focus on this control. Returns true when the peer took it.</summary>
		bool Focus();
	}

	/// <summary>The peer of a <see cref="Form"/>: a top-level window.</summary>
	public interface IFormPeer : IControlPeer
	{
		/// <summary>Shows the window (modeless). <paramref name="owner"/> is null for a form without an owner.</summary>
		void Show(IFormPeer owner);

		/// <summary>Shows the window modally (input to the owner is blocked) and returns at once; the facade runs the loop through <see cref="IFormsBackend.RunModal"/>.</summary>
		void ShowModal(IFormPeer owner);

		/// <summary>Hides the window.</summary>
		void Hide();

		/// <summary>Asks the window to close as its close box does; the peer answers with the form's closing entry point.</summary>
		void Close();

		/// <summary>Makes the window the active one.</summary>
		void Activate();
	}

	/// <summary>The toolkit timer of a <see cref="System.Windows.Forms.Timer"/>. Every member is called on the UI thread, in the order the script made the changes.</summary>
	public interface ITimerPeer : IDisposable
	{
		/// <summary>Starts ticking every <paramref name="interval"/> milliseconds; the first tick comes one interval from now. Starting a running timer restarts it.</summary>
		void Start(int interval);

		/// <summary>Stops ticking. A tick that was already queued is not delivered.</summary>
		void Stop();
	}

	/// <summary>
	/// A nested loop (the modal loop of <c>ShowDialog</c>). The facade calls <see cref="Exit"/> from its close path, on any thread; the backend pumps until it was called.
	/// </summary>
	public sealed class ModalLoop
	{
		private readonly object sync = new object();
		private Action wake;
		private bool exitRequested;

		/// <summary>True once <see cref="Exit"/> was called.</summary>
		public bool ExitRequested
		{
			get
			{
				lock (sync)
				{
					return exitRequested;
				}
			}
		}

		/// <summary>
		/// For the backend: <paramref name="wakeUp"/> is called (on any thread) when the exit is requested so that the pump notices; when the exit was requested already it is called at once.
		/// </summary>
		public void Attach(Action wakeUp)
		{
			bool now;
			lock (sync)
			{
				wake = wakeUp;
				now = exitRequested;
			}
			if (now)
			{
				wakeUp?.Invoke();
			}
		}

		/// <summary>Ends the loop (callable from any thread, any number of times).</summary>
		public void Exit()
		{
			Action action;
			lock (sync)
			{
				exitRequested = true;
				action = wake;
			}
			action?.Invoke();
		}
	}

	/// <summary>Where the facade finds its backend and what it does with an exception that nobody handled.</summary>
	public static class FormsRuntime
	{
		private static readonly object Sync = new object();
		private static IFormsBackend backend;
		private static readonly List<Exception> unhandled = new List<Exception>();

		/// <summary>The installed backend; <see cref="NullFormsBackend"/> (created on first use, the calling thread is its UI thread) when none was installed.</summary>
		public static IFormsBackend Backend
		{
			get
			{
				IFormsBackend b = Volatile.Read(ref backend);
				if (b != null)
				{
					return b;
				}
				lock (Sync)
				{
					return backend ?? (backend = new NullFormsBackend());
				}
			}
			set
			{
				Volatile.Write(ref backend, value);
			}
		}

		/// <summary>True when a backend was installed or the default one was created.</summary>
		public static bool HasBackend => Volatile.Read(ref backend) != null;

		/// <summary>Forgets the backend and the recorded exceptions (tests; a process that restarts its script UI).</summary>
		public static void Reset()
		{
			lock (Sync)
			{
				backend = null;
				unhandled.Clear();
			}
			Control.ResetStaticState();
		}

		/// <summary>The peer of a control, or null when it has none (its form was never shown).</summary>
		public static IControlPeer PeerOf(Control control) => control?.Peer;

		/// <summary>Raised for an exception of a script handler that neither <c>Application.ThreadException</c> nor the caller took (the host logs it).</summary>
		public static event Action<Exception> UnhandledException;

		/// <summary>The exceptions that nobody handled, oldest first (at most 100 are kept).</summary>
		public static IReadOnlyList<Exception> Unhandled
		{
			get
			{
				lock (Sync)
				{
					return unhandled.ToArray();
				}
			}
		}

		/// <summary>
		/// Runs a facade callback that came from a peer: an exception goes to <c>Application.ThreadException</c> (and to <see cref="UnhandledException"/> when that has no handler),
		/// never into the toolkit's loop (Avalonia ends the process on an exception that escapes a job).
		/// </summary>
		public static void Guard(Action action)
		{
			try
			{
				action();
			}
			catch (Exception e)
			{
				Report(e);
			}
		}

		internal static void Report(Exception e)
		{
			if (Application.RaiseThreadException(e))
			{
				return;
			}
			lock (Sync)
			{
				unhandled.Add(e);
				if (unhandled.Count > 100)
				{
					unhandled.RemoveAt(0);
				}
			}
			UnhandledException?.Invoke(e);
		}
	}

	/// <summary>
	/// The backend of a process without a window server (the headless script host, tests): peers only record what the facade tells them. By default there is no UI thread and
	/// nothing waits for one: every thread may touch peers and <see cref="Post"/> runs its action at once (a script that shows a form on a worker thread must never hang, and
	/// <c>Shown</c> then comes right after <c>Activated</c>). With <see cref="ThreadAffinity"/> the "UI thread" is the thread that created the backend (or
	/// <see cref="AdoptCurrentThread"/>), <see cref="Post"/> queues and the queue is run by <see cref="Pump"/>, <see cref="DoEvents"/> and <see cref="RunModal"/>, as with a real toolkit (tests).
	/// </summary>
	public sealed class NullFormsBackend : IFormsBackend
	{
		private readonly object sync = new object();
		private readonly Queue<Action> queue = new Queue<Action>();
		private readonly List<NullTimer> timers = new List<NullTimer>();
		private int uiThread = Environment.CurrentManagedThreadId;
		private int modalDepth;
		private long clock;

		/// <summary>The peers created, in creation order (the oldest are dropped beyond 1000).</summary>
		public List<NullPeer> Peers { get; } = new List<NullPeer>();

		/// <summary>True: one thread is the UI thread, <see cref="Post"/> queues and <see cref="Invoke"/> from another thread waits for <see cref="Pump"/>. False (the default): no thread affinity, everything runs at once.</summary>
		public bool ThreadAffinity { get; set; }

		/// <summary>
		/// What <see cref="RunModal"/> does when the queue is empty and the loop was not asked to exit: true (the default) leaves the loop (a dialog nobody can answer returns at once,
		/// as the headless host does), false waits for another thread to post work or to end the loop.
		/// </summary>
		public bool ModalExitsWhenIdle { get; set; } = true;

		public Keys ModifierKeys { get; set; }

		public MouseButtons MouseButtons { get; set; }

		public Point MousePosition { get; set; }

		public Rectangle ScreenBounds { get; set; } = new Rectangle(0, 0, 1920, 1080);

		/// <summary>The number of modal loops that run now.</summary>
		public int ModalDepth => Volatile.Read(ref modalDepth);

		/// <summary>The number of posted actions that have not run yet (with thread affinity; always 0 without).</summary>
		public int PendingCount
		{
			get
			{
				lock (sync)
				{
					return queue.Count;
				}
			}
		}

		/// <summary>The manual clock in milliseconds: it moves only through <see cref="AdvanceTime"/>, so a test never waits for a timer.</summary>
		public long Clock
		{
			get
			{
				lock (sync)
				{
					return clock;
				}
			}
		}

		/// <summary>The number of timers that have been started and not stopped.</summary>
		public int RunningTimers
		{
			get
			{
				lock (sync)
				{
					return timers.Count(t => t.Running);
				}
			}
		}

		/// <summary>
		/// Moves the manual clock forward and ticks every running timer whose time came, oldest due time first (timers with the same time in the order they were created). A tick goes through
		/// <see cref="Post"/>, so with thread affinity it waits for <see cref="Pump"/>; a timer whose tick is still queued does not queue a second one (the WM_TIMER rule). A timer stopped
		/// before its queued tick runs does not tick. There is no real clock: a headless process has no message loop and nothing ticks unless a test advances time. Returns the number of ticks queued or run.
		/// </summary>
		public int AdvanceTime(int milliseconds)
		{
			if (milliseconds < 0)
			{
				throw new ArgumentOutOfRangeException(nameof(milliseconds));
			}
			int ticks = 0;
			long target;
			lock (sync)
			{
				target = clock + milliseconds;
			}
			while (true)
			{
				NullTimer due = null;
				lock (sync)
				{
					foreach (NullTimer t in timers)
					{
						if (t.Running && t.Due <= target && (due == null || t.Due < due.Due))
						{
							due = t;
						}
					}
					if (due == null)
					{
						clock = target;
						return ticks;
					}
					clock = Math.Max(clock, due.Due);
					due.Due += due.Interval;
				}
				if (due.Queue())
				{
					ticks++;
				}
			}
		}

		public ITimerPeer CreateTimer(Action tick)
		{
			if (tick == null)
			{
				throw new ArgumentNullException(nameof(tick));
			}
			var timer = new NullTimer(this, tick);
			lock (sync)
			{
				timers.Add(timer);
			}
			return timer;
		}

		private sealed class NullTimer : ITimerPeer
		{
			private readonly NullFormsBackend owner;
			private readonly Action tick;
			private int queued;

			public NullTimer(NullFormsBackend owner, Action tick)
			{
				this.owner = owner;
				this.tick = tick;
			}

			// These three are guarded by the backend's lock.
			public bool Running;
			public long Due;
			public int Interval;

			public void Start(int interval)
			{
				lock (owner.sync)
				{
					Interval = Math.Max(1, interval);
					Due = owner.clock + Interval;
					Running = true;
				}
			}

			public void Stop()
			{
				lock (owner.sync)
				{
					Running = false;
				}
			}

			public void Dispose()
			{
				lock (owner.sync)
				{
					Running = false;
					owner.timers.Remove(this);
				}
			}

			/// <summary>Queues one tick unless one is queued already. True when a tick was queued or run.</summary>
			public bool Queue()
			{
				if (Interlocked.Exchange(ref queued, 1) == 1)
				{
					return false;
				}
				owner.Post(() =>
				{
					Volatile.Write(ref queued, 0);
					bool running;
					lock (owner.sync)
					{
						running = Running;
					}
					if (running)
					{
						tick();
					}
				});
				return true;
			}
		}

		/// <summary>Makes the calling thread the UI thread.</summary>
		public void AdoptCurrentThread() => Volatile.Write(ref uiThread, Environment.CurrentManagedThreadId);

		public bool CheckAccess() => !ThreadAffinity || Environment.CurrentManagedThreadId == Volatile.Read(ref uiThread);

		public void Post(Action action)
		{
			if (action == null)
			{
				throw new ArgumentNullException(nameof(action));
			}
			if (!ThreadAffinity)
			{
				action();
				return;
			}
			lock (sync)
			{
				queue.Enqueue(action);
				Monitor.PulseAll(sync);
			}
		}

		public void Invoke(Action action)
		{
			if (action == null)
			{
				throw new ArgumentNullException(nameof(action));
			}
			if (CheckAccess())
			{
				action();
				return;
			}
			Exception error = null;
			using (var done = new ManualResetEventSlim(false))
			{
				Post(() =>
				{
					try
					{
						action();
					}
					catch (Exception e)
					{
						error = e;
					}
					finally
					{
						done.Set();
					}
				});
				done.Wait();
			}
			if (error != null)
			{
				System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
			}
		}

		/// <summary>Runs the queued actions (UI thread) until the queue is empty. Returns how many ran.</summary>
		public int Pump()
		{
			int count = 0;
			while (true)
			{
				Action action;
				lock (sync)
				{
					if (queue.Count == 0)
					{
						return count;
					}
					action = queue.Dequeue();
				}
				action();
				count++;
			}
		}

		public void DoEvents()
		{
			if (CheckAccess())
			{
				Pump();
			}
		}

		public void RunModal(ModalLoop loop)
		{
			if (loop == null)
			{
				throw new ArgumentNullException(nameof(loop));
			}
			Interlocked.Increment(ref modalDepth);
			try
			{
				loop.Attach(() =>
				{
					lock (sync)
					{
						Monitor.PulseAll(sync);
					}
				});
				while (!loop.ExitRequested)
				{
					Action action = null;
					lock (sync)
					{
						if (queue.Count > 0)
						{
							action = queue.Dequeue();
						}
						else if (loop.ExitRequested)
						{
							break;
						}
						else if (ModalExitsWhenIdle)
						{
							break;
						}
						else
						{
							Monitor.Wait(sync, 50);
							continue;
						}
					}
					action();
				}
			}
			finally
			{
				Interlocked.Decrement(ref modalDepth);
			}
		}

		public IControlPeer CreatePeer(Control control)
		{
			var peer = new NullPeer(control);
			lock (sync)
			{
				Peers.Add(peer);
				if (Peers.Count > 1000)
				{
					Peers.RemoveRange(0, 100);
				}
			}
			return peer;
		}

		/// <summary>A deterministic measure: 7 pixels per character and 15 per line, wrapped at <paramref name="maxWidth"/>.</summary>
		public Size MeasureText(string text, Font font, int maxWidth)
		{
			if (string.IsNullOrEmpty(text))
			{
				return new Size(0, 15);
			}
			int widest = 0;
			int lines = 0;
			foreach (string line in text.Replace("\r\n", "\n").Split('\n'))
			{
				int width = line.Length * 7;
				if (maxWidth > 0 && width > maxWidth)
				{
					int perLine = Math.Max(1, maxWidth / 7);
					lines += (line.Length + perLine - 1) / perLine;
					widest = Math.Max(widest, perLine * 7);
				}
				else
				{
					lines++;
					widest = Math.Max(widest, width);
				}
			}
			return new Size(widest, lines * 15);
		}

		public Rectangle GetScreenBounds() => ScreenBounds;

		public Keys GetModifierKeys() => ModifierKeys;

		public MouseButtons GetMouseButtons() => MouseButtons;

		public Point GetMousePosition() => MousePosition;
	}

	/// <summary>The peer of <see cref="NullFormsBackend"/>: a log of what the facade told it.</summary>
	public sealed class NullPeer : IFormPeer
	{
		private readonly List<string> log = new List<string>();

		internal NullPeer(Control control)
		{
			Control = control;
		}

		/// <summary>The facade control this peer stands for.</summary>
		public Control Control { get; }

		public bool Disposed { get; private set; }

		/// <summary>True after <see cref="IFormPeer.Show"/> or <see cref="IFormPeer.ShowModal"/> and before <see cref="IFormPeer.Hide"/>.</summary>
		public bool Shown { get; private set; }

		/// <summary>What the facade told the peer, in order: "PropertyChanged Text", "Invalidate", "ChildrenChanged", "Show", "ShowModal", "Hide", "Close", "Activate", "Focus", "Dispose".</summary>
		public IReadOnlyList<string> Log
		{
			get
			{
				lock (log)
				{
					return log.ToArray();
				}
			}
		}

		/// <summary>The area of the last <see cref="IControlPeer.Invalidate"/>.</summary>
		public Rectangle LastInvalidated { get; private set; }

		/// <summary>What <see cref="IControlPeer.Focus"/> answers.</summary>
		public bool AcceptsFocus { get; set; } = true;

		private void Add(string entry)
		{
			lock (log)
			{
				log.Add(entry);
			}
		}

		public void PropertyChanged(string property) => Add("PropertyChanged " + property);

		public void Invalidate(Rectangle area)
		{
			LastInvalidated = area;
			Add("Invalidate");
		}

		public void ChildrenChanged() => Add("ChildrenChanged");

		public bool Focus()
		{
			Add("Focus");
			return AcceptsFocus;
		}

		public void Show(IFormPeer owner)
		{
			Shown = true;
			Add("Show");
		}

		public void ShowModal(IFormPeer owner)
		{
			Shown = true;
			Add("ShowModal");
		}

		public void Hide()
		{
			Shown = false;
			Add("Hide");
		}

		public void Close() => Add("Close");

		public void Activate() => Add("Activate");

		public void Dispose()
		{
			Disposed = true;
			Add("Dispose");
		}
	}
}
