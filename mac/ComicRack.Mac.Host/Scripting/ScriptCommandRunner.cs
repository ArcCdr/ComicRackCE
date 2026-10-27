using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using cYo.Projects.ComicRack.Plugins;

namespace ComicRack.Mac.Host.Scripting
{
	/// <summary>How a script command ended.</summary>
	public enum ScriptRunOutcome
	{
		/// <summary>The command returned.</summary>
		Completed,

		/// <summary>The command raised an exception (<see cref="ScriptRunResult.Error"/>).</summary>
		Failed,

		/// <summary>The run was cancelled: the script stopped at its next call into the host, or it was let go (<see cref="ScriptRunResult.Detached"/>).</summary>
		Cancelled,
	}

	/// <summary>What <see cref="ScriptCommandRunner.RunAsync"/> reports.</summary>
	public sealed class ScriptRunResult
	{
		public ScriptRunResult(ScriptRunOutcome outcome, object value, Exception error, TimeSpan elapsed, bool detached)
		{
			Outcome = outcome;
			Value = value;
			Error = error;
			Elapsed = elapsed;
			Detached = detached;
		}

		public ScriptRunOutcome Outcome { get; }

		/// <summary>What the command returned (null for most hooks).</summary>
		public object Value { get; }

		/// <summary>The exception of the script itself: a command that is invoked through a delegate raises <see cref="TargetInvocationException"/>, which is unwrapped here.</summary>
		public Exception Error { get; }

		public TimeSpan Elapsed { get; }

		/// <summary>True when the run was cancelled while the script was still going: the result was given at once and the script goes on until it makes its next call into the host.</summary>
		public bool Detached { get; }

		/// <summary>The message the user sees for <see cref="Error"/> (the exception of the script, not the one of the reflection call).</summary>
		public string Message => Error?.Message;
	}

	/// <summary>
	/// Runs one script command on a worker thread (the original runs it on the UI thread behind a wait cursor, <c>ScriptUtility.CreateBookCode</c>) and reports how it
	/// ended. The run can be cancelled: see <see cref="ScriptRunContext"/>. A script that never prints and never calls <c>ComicRack.App</c> cannot be stopped, so a cancelled
	/// run answers at once and lets the script finish in the background, which <see cref="ScriptRunResult.Detached"/> says.
	/// </summary>
	public static class ScriptCommandRunner
	{
		/// <summary>Runs <paramref name="command"/> with <paramref name="args"/> (the books, or nothing) on the thread pool. Never faults: a failure is the outcome.</summary>
		public static Task<ScriptRunResult> RunAsync(Command command, object[] args, CancellationToken cancellation = default)
		{
			if (command == null)
			{
				throw new ArgumentNullException(nameof(command));
			}
			string name = command.GetLocalizedName() ?? command.Key ?? "script";
			var finished = new TaskCompletionSource<ScriptRunResult>(TaskCreationOptions.RunContinuationsAsynchronously);
			DateTime start = DateTime.UtcNow;
			Task.Run(() =>
			{
				ScriptRunResult result;
				using (ScriptRunContext.Enter(name, cancellation))
				{
					try
					{
						ScriptRunContext.ThrowIfCancelled();
						object value = command.Invoke(args);
						result = new ScriptRunResult(ScriptRunOutcome.Completed, value, null, DateTime.UtcNow - start, false);
					}
					catch (Exception ex)
					{
						Exception inner = Unwrap(ex);
						result = inner is OperationCanceledException
							? new ScriptRunResult(ScriptRunOutcome.Cancelled, null, null, DateTime.UtcNow - start, false)
							: new ScriptRunResult(ScriptRunOutcome.Failed, null, inner, DateTime.UtcNow - start, false);
					}
				}
				finished.TrySetResult(result);
			});
			if (cancellation.CanBeCanceled)
			{
				cancellation.Register(() => finished.TrySetResult(new ScriptRunResult(ScriptRunOutcome.Cancelled, null, null, DateTime.UtcNow - start, true)));
			}
			return finished.Task;
		}

		/// <summary>The exception a script raised: the inner exception of the reflection call (and of the wrappers IronPython adds), not the call itself.</summary>
		public static Exception Unwrap(Exception ex)
		{
			while (ex is TargetInvocationException || ex is AggregateException)
			{
				Exception next = ex.InnerException;
				if (next == null)
				{
					break;
				}
				ex = next;
			}
			return ex;
		}
	}
}
