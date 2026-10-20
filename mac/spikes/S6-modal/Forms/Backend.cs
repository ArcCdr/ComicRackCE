using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;

namespace System.Windows.Forms
{
	/// <summary>
	/// The seam between the facade (all state, events, layout, modal logic) and a UI toolkit (peers). One backend per process.
	/// Every method that creates or touches a peer runs on the UI thread; the facade marshals.
	/// </summary>
	public interface IFormsBackend
	{
		/// <summary>True on the UI thread.</summary>
		bool CheckAccess();

		/// <summary>Runs <paramref name="action"/> on the UI thread and returns when it has finished (directly when already there).</summary>
		void Invoke(Action action);

		/// <summary>Queues <paramref name="action"/> on the UI thread.</summary>
		void Post(Action action);

		/// <summary>A nested message loop. <see cref="IModalLoop.Run"/> pumps the UI thread until <see cref="IModalLoop.Exit"/> was called.</summary>
		IModalLoop CreateModalLoop();

		/// <summary>Application.DoEvents: process what is queued, then return (no-op off the UI thread).</summary>
		void DoEvents();

		/// <summary>Creates the peer of a control (a form gets an <see cref="IFormPeer"/>); the peer builds the peers of the children itself.</summary>
		IControlPeer CreatePeer(Control control);

		/// <summary>A timer that ticks on the UI thread.</summary>
		ITimerPeer CreateTimer(Action tick);

		/// <summary>Text size in pixels at the default font (wrapped at <paramref name="maxWidth"/> when it is above 0), used by AutoSize controls.</summary>
		Size MeasureText(string text, Font font, int maxWidth);
	}

	public interface IModalLoop
	{
		/// <summary>Pumps until Exit. Returns at once when Exit was called before.</summary>
		void Run();

		/// <summary>Ends Run (callable from any thread, any number of times).</summary>
		void Exit();
	}

	public interface IControlPeer : IDisposable
	{
		/// <summary>The facade property <paramref name="name"/> changed (Text, Bounds, Enabled, Visible, Checked, Value, SelectedIndex, Items, ...).</summary>
		void PropertyChanged(string name);

		void Invalidate();

		void ChildAdded(Control child);

		void ChildRemoved(Control child);
	}

	public interface IFormPeer : IControlPeer
	{
		void Show(IFormPeer owner);

		/// <summary>Shows the window modally (input to the owner is blocked) and returns at once; the facade runs the loop.</summary>
		void ShowDialog(IFormPeer owner);

		/// <summary>Asks the window to close, as the close button does (the peer raises PeerClosing).</summary>
		void Close();

		void Hide();
	}

	public interface ITimerPeer : IDisposable
	{
		int Interval { get; set; }

		void Start();

		void Stop();
	}

	/// <summary>Where the facade finds its backend.</summary>
	public static class FormsRuntime
	{
		private static IFormsBackend backend;

		public static IFormsBackend Backend
		{
			get
			{
				IFormsBackend b = Volatile.Read(ref backend);
				if (b == null)
				{
					throw new InvalidOperationException("No System.Windows.Forms backend is installed (script UI is not available in this process).");
				}
				return b;
			}
			set
			{
				Volatile.Write(ref backend, value);
			}
		}

		public static bool HasBackend => Volatile.Read(ref backend) != null;

		/// <summary>Forms that are shown (modal or not) right now, in the order they were shown. For the host and for tests.</summary>
		public static IReadOnlyList<Form> OpenForms
		{
			get
			{
				lock (open)
				{
					return open.ToArray();
				}
			}
		}

		private static readonly List<Form> open = new List<Form>();

		internal static void Opened(Form f)
		{
			lock (open)
			{
				open.Add(f);
			}
		}

		internal static void Closed(Form f)
		{
			lock (open)
			{
				open.Remove(f);
			}
		}

		/// <summary>Runs a facade callback that came from a peer: an exception goes to Application.ThreadException, never into the toolkit's loop.</summary>
		public static void Guard(Action action)
		{
			try
			{
				action();
			}
			catch (Exception e)
			{
				Application.OnThreadException(e);
			}
		}
	}
}
