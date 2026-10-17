using System;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace ComicRack.Mac
{
	/// <summary>
	/// The only way Core code reaches the UI thread. The Core raises its events on worker threads (queues, scanner, cache), so
	/// <b>every subscription to a Core event goes through <see cref="Wrap(EventHandler)"/> or one of its overloads</b>, or through
	/// <see cref="Post(Action)"/> inside the handler. Review checklist item: no "core.Event += handler" that touches a control or
	/// a view model property without UiDispatch.
	/// </summary>
	public static class UiDispatch
	{
		/// <summary>True on the UI thread.</summary>
		public static bool IsUiThread => Dispatcher.UIThread.CheckAccess();

		/// <summary>Runs <paramref name="action"/> on the UI thread later, whichever thread calls (never inline).</summary>
		public static void Post(Action action, DispatcherPriority priority = default)
		{
			if (action == null)
			{
				throw new ArgumentNullException(nameof(action));
			}
			Dispatcher.UIThread.Post(action, priority);
		}

		/// <summary>Runs <paramref name="action"/> on the UI thread and completes afterwards (inline if already there).</summary>
		public static Task InvokeAsync(Action action)
		{
			if (action == null)
			{
				throw new ArgumentNullException(nameof(action));
			}
			if (IsUiThread)
			{
				action();
				return Task.CompletedTask;
			}
			return Dispatcher.UIThread.InvokeAsync(action).GetTask();
		}

		/// <summary>
		/// A handler that posts <paramref name="handler"/> to the UI thread. Keep the returned delegate to unsubscribe:
		/// <c>var h = UiDispatch.Wrap(OnChanged); core.Changed += h; ... core.Changed -= h;</c>.
		/// The arguments are used later, so a handler must not rely on state that the raiser resets afterwards.
		/// </summary>
		public static EventHandler Wrap(EventHandler handler)
		{
			if (handler == null)
			{
				throw new ArgumentNullException(nameof(handler));
			}
			return (sender, e) => Post(() => handler(sender, e));
		}

		/// <inheritdoc cref="Wrap(EventHandler)"/>
		public static EventHandler<TEventArgs> Wrap<TEventArgs>(EventHandler<TEventArgs> handler)
		{
			if (handler == null)
			{
				throw new ArgumentNullException(nameof(handler));
			}
			return (sender, e) => Post(() => handler(sender, e));
		}
	}
}
