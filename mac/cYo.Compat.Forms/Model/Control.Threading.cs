using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using cYo.Compat.Forms;

// Threads (P4.9e, MACOS_NATIVE_PATH.md 5.9.4). ComicRack sets Control.CheckForIllegalCrossThreadCalls to false, so scripts change controls from worker threads with and without Invoke.
// The facade state is the truth and is safe to read and write from any thread (see the locks in Control.cs). What a change tells the peer is posted to the backend when the caller is
// not on the UI thread, and posts are coalesced: a worker that writes Text 10,000 times leaves one pending update for the peer, which reads the last value when it runs. Invoke,
// BeginInvoke, EndInvoke and InvokeRequired follow Control.MarshaledInvoke of dotnet/winforms: the control with a handle that is nearest on the way up is the marshaler, its queue of
// calls runs in order on the UI thread, a synchronous call from the UI thread runs at once (after the calls queued before it), an exception of the delegate comes back as its base
// exception, pending calls fail with ObjectDisposedException when the control is disposed, and an exception of a BeginInvoke call that nobody collects goes to Application.ThreadException.
namespace System.Windows.Forms
{
	public partial class Control
	{
		private const string NoMarshalingThread = "Invoke or BeginInvoke cannot be called on a control until the window handle has been created.";

		private readonly object postSync = new object();
		private HashSet<string> postedProperties;
		private bool childrenPosted;
		private bool invalidatePosted;
		private Rectangle postedArea;

		private readonly object callSync = new object();
		private Queue<MarshaledCall> calls;

		// ---------------------------------------------------------------- what the peer is told

		/// <summary>
		/// Tells the peer that a facade property changed. On the UI thread the peer is called at once; from another thread the call is posted, and a second change of the same property
		/// while the first post has not run is not posted again (the peer reads the state when its call runs). The post goes to the peer that exists when it runs.
		/// </summary>
		internal void Notify(string property)
		{
			IControlPeer p = peer;
			if (p == null)
			{
				return;
			}
			IFormsBackend backend = FormsRuntime.Backend;
			if (backend.CheckAccess())
			{
				FormsRuntime.Guard(() => p.PropertyChanged(property));
				return;
			}
			lock (postSync)
			{
				if (postedProperties == null)
				{
					postedProperties = new HashSet<string>(StringComparer.Ordinal);
				}
				if (!postedProperties.Add(property))
				{
					return;
				}
			}
			try
			{
				backend.Post(() => DeliverProperty(property));
			}
			catch
			{
				lock (postSync)
				{
					postedProperties.Remove(property);
				}
				throw;
			}
		}

		private void DeliverProperty(string property)
		{
			lock (postSync)
			{
				postedProperties.Remove(property);
			}
			IControlPeer current = peer;
			if (current != null)
			{
				FormsRuntime.Guard(() => current.PropertyChanged(property));
			}
		}

		/// <summary>Tells the peer that children were added, removed or moved (coalesced like <see cref="Notify"/>).</summary>
		internal void NotifyPeerChildren()
		{
			IControlPeer p = peer;
			if (p == null)
			{
				return;
			}
			IFormsBackend backend = FormsRuntime.Backend;
			if (backend.CheckAccess())
			{
				FormsRuntime.Guard(p.ChildrenChanged);
				return;
			}
			lock (postSync)
			{
				if (childrenPosted)
				{
					return;
				}
				childrenPosted = true;
			}
			try
			{
				backend.Post(() =>
				{
					lock (postSync)
					{
						childrenPosted = false;
					}
					IControlPeer current = peer;
					if (current != null)
					{
						FormsRuntime.Guard(current.ChildrenChanged);
					}
				});
			}
			catch
			{
				lock (postSync)
				{
					childrenPosted = false;
				}
				throw;
			}
		}

		/// <summary>Tells the peer that an area must be drawn again; the areas of the calls that are posted together are joined into one.</summary>
		private void NotifyPeerInvalidate(Rectangle area)
		{
			IControlPeer p = peer;
			if (p == null)
			{
				return;
			}
			IFormsBackend backend = FormsRuntime.Backend;
			if (backend.CheckAccess())
			{
				FormsRuntime.Guard(() => p.Invalidate(area));
				return;
			}
			lock (postSync)
			{
				if (invalidatePosted)
				{
					postedArea = Rectangle.Union(postedArea, area);
					return;
				}
				invalidatePosted = true;
				postedArea = area;
			}
			try
			{
				backend.Post(() =>
				{
					Rectangle joined;
					lock (postSync)
					{
						joined = postedArea;
						invalidatePosted = false;
					}
					IControlPeer current = peer;
					if (current != null)
					{
						FormsRuntime.Guard(() => current.Invalidate(joined));
					}
				});
			}
			catch
			{
				lock (postSync)
				{
					invalidatePosted = false;
				}
				throw;
			}
		}

		// ---------------------------------------------------------------- Invoke, BeginInvoke, EndInvoke

		/// <summary>
		/// True when the caller is not on the thread of the control's window: the UI thread of the backend, and only once the control or a parent has a handle (before that there is no
		/// thread to marshal to and the answer is false, as in the Framework).
		/// </summary>
		public bool InvokeRequired => FindMarshalingControl() != null && !FormsRuntime.Backend.CheckAccess();

		/// <summary>The control or the nearest parent that has a handle.</summary>
		private Control FindMarshalingControl()
		{
			for (Control c = this; c != null; c = c.parent)
			{
				if (c.handleCreated)
				{
					return c;
				}
			}
			return null;
		}

		public object Invoke(Delegate method) => Invoke(method, null);

		/// <summary>Runs <paramref name="method"/> on the UI thread and returns what it returned (the caller waits); an exception of the method is thrown again here.</summary>
		public object Invoke(Delegate method, params object[] args)
		{
			if (method == null)
			{
				throw new ArgumentNullException(nameof(method));
			}
			MarshaledCall call = Marshal(method, args, synchronous: true);
			return call.GetResult();
		}

		public void Invoke(Action method) => Invoke((Delegate)method, null);

		public T Invoke<T>(Func<T> method) => (T)Invoke((Delegate)method, null);

		public IAsyncResult BeginInvoke(Delegate method) => BeginInvoke(method, null);

		/// <summary>Queues <paramref name="method"/> for the UI thread and returns at once. On a backend without a UI thread the method has run when this returns.</summary>
		public IAsyncResult BeginInvoke(Delegate method, params object[] args)
		{
			if (method == null)
			{
				throw new ArgumentNullException(nameof(method));
			}
			return Marshal(method, args, synchronous: false);
		}

		public IAsyncResult BeginInvoke(Action method) => BeginInvoke((Delegate)method, null);

		/// <summary>Waits for the call of <see cref="BeginInvoke(Delegate)"/> and returns its result; an exception of the method is thrown here.</summary>
		public object EndInvoke(IAsyncResult asyncResult)
		{
			if (asyncResult == null)
			{
				throw new ArgumentNullException(nameof(asyncResult));
			}
			var call = asyncResult as MarshaledCall;
			if (call == null || call.Caller != this)
			{
				throw new ArgumentException("The IAsyncResult object was not returned from the corresponding asynchronous method on this class.", nameof(asyncResult));
			}
			if (!call.IsCompleted)
			{
				// The UI thread runs what is queued (it may be the call itself); any other thread waits for the UI thread.
				if (FormsRuntime.Backend.CheckAccess())
				{
					call.Marshaler.InvokeMarshaledCallbacks();
				}
				call.Wait();
			}
			return call.GetResult();
		}

		private MarshaledCall Marshal(Delegate method, object[] args, bool synchronous)
		{
			Control marshaler = FindMarshalingControl();
			if (marshaler == null)
			{
				throw new InvalidOperationException(NoMarshalingThread);
			}
			var call = new MarshaledCall(this, marshaler, method, args, synchronous);
			lock (marshaler.callSync)
			{
				// The handle is checked under the lock that CancelPendingCalls takes: a call is either refused here or cancelled when the control goes away.
				if (!marshaler.handleCreated)
				{
					throw new InvalidOperationException(NoMarshalingThread);
				}
				if (marshaler.calls == null)
				{
					marshaler.calls = new Queue<MarshaledCall>();
				}
				marshaler.calls.Enqueue(call);
			}
			IFormsBackend backend = FormsRuntime.Backend;
			if (synchronous && backend.CheckAccess())
			{
				marshaler.InvokeMarshaledCallbacks();
			}
			else
			{
				backend.Post(marshaler.InvokeMarshaledCallbacks);
			}
			if (synchronous)
			{
				call.Wait();
			}
			return call;
		}

		/// <summary>Runs the queued calls in the order they were made. Runs on the UI thread, from a post or from a synchronous call or EndInvoke that was made on it.</summary>
		private void InvokeMarshaledCallbacks()
		{
			while (true)
			{
				MarshaledCall call;
				lock (callSync)
				{
					if (calls == null || calls.Count == 0)
					{
						return;
					}
					call = calls.Dequeue();
				}
				call.Run();
			}
		}

		/// <summary>The control goes away: the calls that did not run fail with ObjectDisposedException, so that nobody waits for them for ever.</summary>
		private void CancelPendingCalls()
		{
			List<MarshaledCall> pending = null;
			lock (callSync)
			{
				if (calls != null && calls.Count > 0)
				{
					pending = new List<MarshaledCall>(calls);
					calls.Clear();
				}
			}
			if (pending == null)
			{
				return;
			}
			var error = new ObjectDisposedException(GetType().Name);
			foreach (MarshaledCall call in pending)
			{
				call.Fail(error);
			}
		}
	}

	/// <summary>A call queued by Invoke or BeginInvoke (the ThreadMethodEntry of the original); also the <see cref="IAsyncResult"/> that BeginInvoke returns.</summary>
	internal sealed class MarshaledCall : IAsyncResult
	{
		private readonly Delegate method;
		private readonly object[] args;
		private readonly bool synchronous;
		private readonly ExecutionContext context;
		private readonly ManualResetEventSlim done = new ManualResetEventSlim(false);
		private object result;
		private Exception exception;

		public MarshaledCall(Control caller, Control marshaler, Delegate method, object[] args, bool synchronous)
		{
			Caller = caller;
			Marshaler = marshaler;
			this.method = method;
			this.args = args;
			this.synchronous = synchronous;
			context = ExecutionContext.Capture();
		}

		/// <summary>The control that Invoke or BeginInvoke was called on.</summary>
		public Control Caller { get; }

		/// <summary>The control whose queue holds the call.</summary>
		public Control Marshaler { get; }

		public bool IsCompleted => done.IsSet;

		public object AsyncState => null;

		public WaitHandle AsyncWaitHandle => done.WaitHandle;

		public bool CompletedSynchronously => synchronous && done.IsSet;

		public void Wait() => done.Wait();

		/// <summary>The result, or the exception of the method thrown again (its base exception, as the Framework hands it back).</summary>
		public object GetResult()
		{
			Exception e = exception;
			if (e != null)
			{
				ExceptionDispatchInfo.Capture(e).Throw();
			}
			return result;
		}

		/// <summary>Runs the method in the execution context of the caller; never throws.</summary>
		public void Run()
		{
			if (context != null)
			{
				ExecutionContext.Run(context, state => ((MarshaledCall)state).Execute(), this);
			}
			else
			{
				Execute();
			}
		}

		private void Execute()
		{
			try
			{
				result = Call();
			}
			catch (Exception e)
			{
				exception = e.GetBaseException();
			}
			finally
			{
				done.Set();
			}
			// The exception of a call that nobody waits for is untrapped: it goes to the exception handler of the application (and EndInvoke still has it).
			if (!synchronous && exception != null)
			{
				FormsRuntime.Report(exception);
			}
		}

		/// <summary>The delegate types the Framework calls directly; any other delegate goes through DynamicInvoke.</summary>
		private object Call()
		{
			switch (method)
			{
			case EventHandler handler:
				if (args == null || args.Length == 0)
				{
					handler(Caller, EventArgs.Empty);
				}
				else if (args.Length == 1)
				{
					handler(args[0], EventArgs.Empty);
				}
				else
				{
					handler(args[0], (EventArgs)args[1]);
				}
				return null;
			case MethodInvoker invoker:
				invoker();
				return null;
			case WaitCallback callback:
				callback(args?[0]);
				return null;
			default:
				try
				{
					return method.DynamicInvoke(args);
				}
				catch (TargetInvocationException e) when (e.InnerException != null)
				{
					ExceptionDispatchInfo.Capture(e.InnerException).Throw();
					throw;
				}
			}
		}

		/// <summary>Ends the call with an error without running it.</summary>
		public void Fail(Exception error)
		{
			exception = error;
			done.Set();
		}
	}
}
