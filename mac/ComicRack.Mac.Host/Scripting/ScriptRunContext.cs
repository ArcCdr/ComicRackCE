using System;
using System.Threading;

namespace ComicRack.Mac.Host.Scripting
{
	/// <summary>
	/// The run a script is part of, as the code that runs a script command by hand sees it (<see cref="ScriptCommandRunner"/>). A Python function cannot be interrupted from outside
	/// (the original never offered a way), so cancelling is cooperative: the calls a script makes into the host (<c>print</c>, which writes to the script output, and the calls of
	/// <c>ComicRack.App</c>) check the run they belong to and raise <see cref="OperationCanceledException"/> once it was cancelled, which ends the script at its next such call.
	/// The context flows with the thread of the script (and the tasks it starts), so a call from another run is not affected.
	/// </summary>
	public sealed class ScriptRunContext
	{
		private static readonly AsyncLocal<ScriptRunContext> current = new AsyncLocal<ScriptRunContext>();

		private ScriptRunContext(string name, CancellationToken token)
		{
			Name = name;
			Token = token;
		}

		/// <summary>The run the calling code belongs to, or null (a hook of the Core, a test, <c>crcli</c>).</summary>
		public static ScriptRunContext Current => current.Value;

		/// <summary>What is running (the name of the command), for messages.</summary>
		public string Name { get; }

		public CancellationToken Token { get; }

		/// <summary>Makes <paramref name="token"/> the cancellation of everything the calling thread does until the result is disposed.</summary>
		public static IDisposable Enter(string name, CancellationToken token)
		{
			ScriptRunContext previous = current.Value;
			current.Value = new ScriptRunContext(name, token);
			return new Scope(previous);
		}

		/// <summary>Raises <see cref="OperationCanceledException"/> if the run the caller belongs to was cancelled. Does nothing outside a run.</summary>
		public static void ThrowIfCancelled()
		{
			current.Value?.Token.ThrowIfCancellationRequested();
		}

		private sealed class Scope : IDisposable
		{
			private readonly ScriptRunContext previous;
			private bool disposed;

			public Scope(ScriptRunContext previous)
			{
				this.previous = previous;
			}

			public void Dispose()
			{
				if (!disposed)
				{
					disposed = true;
					current.Value = previous;
				}
			}
		}
	}
}
