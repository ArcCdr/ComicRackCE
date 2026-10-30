using System;
using System.Collections.Generic;
using System.ComponentModel;
using cYo.Compat.Forms;

// The message loops of System.Windows.Forms.Application (P4.9f): Run, Exit, ExitThread, MessageLoop, OpenForms and the events around them. The loop itself is the backend's
// (IFormsBackend.RunModal); Run is "show the main form, pump until the context ends", ShowDialog and MessageBox.Show use the same call. All of it runs on the UI thread: a script
// thread that calls Run waits for it. Application.Exit ends the loops of the facade and tells the open forms; it does not quit the host (section 9 of the plan).
namespace System.Windows.Forms
{
	public sealed partial class Application
	{
		private static readonly object LoopSync = new object();
		private static readonly List<ModalLoop> runLoops = new List<ModalLoop>();
		private static readonly FormCollection openForms = new FormCollection();
		private static bool exiting;

		/// <summary>Raised when the application is about to quit (<see cref="Exit()"/>, or the end of the last <see cref="Run(ApplicationContext)"/>), after <see cref="ThreadExit"/>.</summary>
		public static event EventHandler ApplicationExit;

		/// <summary>Raised when the loop of a thread is about to end.</summary>
		public static event EventHandler ThreadExit;

		/// <summary>Raised when a thread is about to enter a modal state: before <c>ShowDialog</c> and <c>MessageBox.Show</c> show their window.</summary>
		public static event EventHandler EnterThreadModal;

		/// <summary>Raised after a modal state has ended.</summary>
		public static event EventHandler LeaveThreadModal;

		/// <summary>The forms that are open: shown at least once and neither closed nor disposed, in the order they were shown.</summary>
		public static FormCollection OpenForms => openForms;

		/// <summary>True while a message loop runs: the host's loop (the Mac app: always), a <see cref="Run(Form)"/> or a modal dialog.</summary>
		public static bool MessageLoop
		{
			get
			{
				lock (LoopSync)
				{
					if (runLoops.Count > 0)
					{
						return true;
					}
				}
				return FormsRuntime.Backend.IsMessageLoopRunning;
			}
		}

		internal static void OpenFormsAdd(Form form) => openForms.Add(form);

		internal static void OpenFormsRemove(Form form) => openForms.Remove(form);

		internal static void ResetLoopState()
		{
			lock (LoopSync)
			{
				runLoops.Clear();
				exiting = false;
			}
			openForms.Clear();
			Form.ResetModalState();
		}

		internal static void RaiseEnterThreadModal() => Raise(EnterThreadModal);

		internal static void RaiseLeaveThreadModal() => Raise(LeaveThreadModal);

		private static void Raise(EventHandler handler)
		{
			if (handler != null)
			{
				FormsRuntime.Guard(() => handler(null, EventArgs.Empty));
			}
		}

		// ---------------------------------------------------------------- Run

		/// <summary>Runs a loop without a form until <see cref="Exit()"/> or <see cref="ExitThread"/>.</summary>
		public static void Run() => Run(new ApplicationContext());

		/// <summary>Shows <paramref name="mainForm"/> and runs a loop until it closes.</summary>
		public static void Run(Form mainForm) => Run(new ApplicationContext(mainForm));

		/// <summary>
		/// Runs a loop until the context ends (<see cref="ApplicationContext.ExitThread"/>, which the closing of its main form calls) or <see cref="Exit()"/>; the main form is shown first
		/// and disposed with the context at the end. Only one loop of this kind runs at a time. A dialog nobody can answer on a backend without a window server ends the loop at once.
		/// </summary>
		public static void Run(ApplicationContext context)
		{
			IFormsBackend backend = FormsRuntime.Backend;
			if (!backend.CheckAccess())
			{
				backend.Invoke(() => RunOnUi(context));
				return;
			}
			RunOnUi(context);
		}

		private static void RunOnUi(ApplicationContext context)
		{
			context = context ?? new ApplicationContext();
			var loop = new ModalLoop();
			lock (LoopSync)
			{
				if (runLoops.Count > 0)
				{
					throw new InvalidOperationException("Starting a second message loop on a single thread is not a valid operation. Use Form.ShowDialog instead.");
				}
				runLoops.Add(loop);
			}
			EventHandler onExit = (s, e) => loop.Exit();
			context.ThreadExit += onExit;
			try
			{
				Form main = context.MainForm;
				if (main != null)
				{
					main.Visible = true;
				}
				if (!loop.ExitRequested)
				{
					FormsRuntime.Backend.RunModal(loop);
				}
			}
			finally
			{
				context.ThreadExit -= onExit;
				lock (LoopSync)
				{
					runLoops.Remove(loop);
				}
				Raise(ThreadExit);
				Raise(ApplicationExit);
				context.Dispose();
			}
		}

		// ---------------------------------------------------------------- Exit

		/// <summary>Asks every open form to close (FormClosing, then FormClosed, both with ApplicationExitCall; the forms are not disposed) and ends the loops.</summary>
		public static void Exit() => Exit(null);

		/// <summary>As <see cref="Exit()"/>; <c>e.Cancel</c> tells whether a form refused.</summary>
		public static void Exit(CancelEventArgs e)
		{
			bool cancelled;
			IFormsBackend backend = FormsRuntime.Backend;
			if (!backend.CheckAccess())
			{
				bool result = false;
				backend.Invoke(() => result = ExitInternal());
				cancelled = result;
			}
			else
			{
				cancelled = ExitInternal();
			}
			if (e != null)
			{
				e.Cancel = cancelled;
			}
		}

		/// <summary>Ends the loops of the thread without asking the forms: <see cref="Run(Form)"/> returns and the dialogs that are open end.</summary>
		public static void ExitThread()
		{
			ExitAllLoops();
		}

		private static bool ExitInternal()
		{
			lock (LoopSync)
			{
				if (exiting)
				{
					return false;
				}
				exiting = true;
			}
			try
			{
				Form[] forms = openForms.Snapshot();
				foreach (Form form in forms)
				{
					if (!form.IsDisposed && form.RaiseFormClosingOnAppExit())
					{
						return true;
					}
				}
				foreach (Form form in forms)
				{
					if (!form.IsDisposed)
					{
						form.RaiseFormClosedOnAppExit();
					}
				}
				if (!ExitAllLoops())
				{
					// with a Run loop the end of that loop raises these
					Raise(ThreadExit);
					Raise(ApplicationExit);
				}
				return false;
			}
			finally
			{
				lock (LoopSync)
				{
					exiting = false;
				}
			}
		}

		/// <summary>Ends every loop of the facade: the dialogs first, then the loop of Run. True when a Run loop was there.</summary>
		private static bool ExitAllLoops()
		{
			Form[] dialogs = Form.ModalForms();
			for (int i = dialogs.Length - 1; i >= 0; i--)
			{
				dialogs[i].ExitDialogLoop();
			}
			ModalLoop[] loops;
			lock (LoopSync)
			{
				loops = runLoops.ToArray();
			}
			foreach (ModalLoop loop in loops)
			{
				loop.Exit();
			}
			return loops.Length > 0;
		}
	}
}
