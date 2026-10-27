using System;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace ComicRack.Mac.Scripting
{
	/// <summary>
	/// How a script reaches the user interface. A script runs on a worker thread (a command, a hook) or, for a few hooks, on the UI thread itself (<c>Shutdown</c>); the calls it makes into the
	/// window (a question, the Info dialog, a change of the reader) must run on the UI thread and the script waits for the answer, as it did in the WinForms program where it was always on the UI
	/// thread. From a worker thread the call is posted and waited for. From the UI thread itself the work starts at once and a nested message loop (<see cref="Dispatcher.PushFrame"/>, the
	/// loop the modal windows of the program use) runs until it is done, so the window stays alive while the script waits (spike S6).
	/// </summary>
	public static class UiCall
	{
		/// <summary>Runs an asynchronous piece of UI work and returns its result.</summary>
		public static T Run<T>(Func<Task<T>> work)
		{
			if (work == null)
			{
				throw new ArgumentNullException(nameof(work));
			}
			if (Dispatcher.UIThread.CheckAccess())
			{
				Task<T> task = work();
				if (!task.IsCompleted)
				{
					var frame = new DispatcherFrame();
					task.ContinueWith(_ => Dispatcher.UIThread.Post(() => frame.Continue = false), TaskScheduler.Default);
					Dispatcher.UIThread.PushFrame(frame);
				}
				return task.GetAwaiter().GetResult();
			}
			var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
			Dispatcher.UIThread.Post(async () =>
			{
				try
				{
					done.SetResult(await work());
				}
				catch (Exception ex)
				{
					done.SetException(ex);
				}
			});
			return done.Task.GetAwaiter().GetResult();
		}

		/// <summary>Runs a piece of UI work that finishes at once (a different name from <c>Run</c>, so that a lambda is never taken for the asynchronous kind).</summary>
		public static T Invoke<T>(Func<T> work)
		{
			if (work == null)
			{
				throw new ArgumentNullException(nameof(work));
			}
			if (Dispatcher.UIThread.CheckAccess())
			{
				return work();
			}
			return Dispatcher.UIThread.Invoke(work);
		}

		public static void Invoke(Action work)
		{
			Invoke<object>(() =>
			{
				work();
				return null;
			});
		}
	}
}
