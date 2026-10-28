using System;
using System.ComponentModel;
using System.Globalization;
using System.Threading;
using cYo.Compat.Forms;

// System.Windows.Forms.Timer (P4.9e). The state (Enabled, Interval, Tag) lives here and can be read and changed from any thread; the ticking is a toolkit timer of the backend
// (cYo.Compat.Forms.ITimerPeer, a dispatcher timer) that calls back on the UI thread. In the Framework a timer ticks on the thread that enabled it, through that thread's message loop;
// here the UI thread of the backend always does, which is what a script written for a form on the UI thread sees. A late timer ticks once (WM_TIMER), a tick that was queued
// before Stop is not delivered, and an exception of a Tick handler goes to Application.ThreadException.
namespace System.Windows.Forms
{
	public partial class Timer : Component
	{
		private readonly object sync = new object();
		private int interval = 100;
		private bool enabled;
		private object tag;
		private TimerRun run;

		public Timer()
		{
		}

		public Timer(IContainer container)
			: this()
		{
			if (container == null)
			{
				throw new ArgumentNullException(nameof(container));
			}
			container.Add(this);
		}

		public event EventHandler Tick;

		public object Tag
		{
			get => Volatile.Read(ref tag);
			set => Volatile.Write(ref tag, value);
		}

		/// <summary>Starts or stops the ticking. Setting the value it has changes nothing (starting a running timer does not restart it).</summary>
		public virtual bool Enabled
		{
			get
			{
				lock (sync)
				{
					return enabled;
				}
			}
			set
			{
				TimerRun started = null;
				TimerRun stopped = null;
				lock (sync)
				{
					if (enabled == value)
					{
						return;
					}
					enabled = value;
					if (value)
					{
						started = run = new TimerRun(this, interval);
					}
					else
					{
						stopped = run;
						run = null;
					}
				}
				stopped?.End();
				started?.Begin();
			}
		}

		/// <summary>The time between two ticks in milliseconds, at least 1 (default 100). Changing it while the timer runs starts the interval again.</summary>
		public int Interval
		{
			get
			{
				lock (sync)
				{
					return interval;
				}
			}
			set
			{
				if (value < 1)
				{
					throw new ArgumentOutOfRangeException(nameof(Interval), value, "Value of '" + value.ToString(CultureInfo.CurrentCulture) + "' is not valid for 'Interval'. 'Interval' must be greater than 0.");
				}
				TimerRun started = null;
				TimerRun stopped = null;
				lock (sync)
				{
					if (interval == value)
					{
						return;
					}
					interval = value;
					if (enabled)
					{
						stopped = run;
						started = run = new TimerRun(this, value);
					}
				}
				stopped?.End();
				started?.Begin();
			}
		}

		public void Start() => Enabled = true;

		public void Stop() => Enabled = false;

		protected virtual void OnTick(EventArgs e)
		{
			Tick?.Invoke(this, e);
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				Enabled = false;
			}
			base.Dispose(disposing);
		}

		public override string ToString() => base.ToString() + ", Interval: " + Interval.ToString(CultureInfo.CurrentCulture);

		/// <summary>Runs an action on the UI thread: at once when the caller is there, else posted (posts keep their order, so Start and Stop of one thread arrive in that order).</summary>
		private static void OnUi(Action action)
		{
			IFormsBackend backend = FormsRuntime.Backend;
			if (backend.CheckAccess())
			{
				action();
			}
			else
			{
				backend.Post(action);
			}
		}

		private void RaiseTick(TimerRun source)
		{
			lock (sync)
			{
				if (run != source)
				{
					return;
				}
			}
			FormsRuntime.Guard(() => OnTick(EventArgs.Empty));
		}

		/// <summary>One period of ticking, from Enabled = true (or a new Interval) to the next stop: its toolkit timer, and the flag that drops the ticks of an ended period.</summary>
		private sealed class TimerRun
		{
			private readonly Timer owner;
			private readonly int interval;
			private volatile bool active = true;
			private ITimerPeer peer;

			public TimerRun(Timer owner, int interval)
			{
				this.owner = owner;
				this.interval = interval;
			}

			public void Begin() => OnUi(() =>
			{
				if (!active)
				{
					return;
				}
				peer = FormsRuntime.Backend.CreateTimer(Tick);
				peer.Start(interval);
			});

			public void End()
			{
				active = false;
				OnUi(() =>
				{
					ITimerPeer p = peer;
					peer = null;
					p?.Dispose();
				});
			}

			private void Tick()
			{
				if (active)
				{
					owner.RaiseTick(this);
				}
			}
		}
	}
}
