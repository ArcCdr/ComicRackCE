using System;

namespace ComicRack.Mac.Services
{
	/// <summary>
	/// An <see cref="IProgress{T}"/> whose handler always runs on the UI thread (posted through <see cref="UiDispatch"/>, in the
	/// order of the reports). Unlike <see cref="Progress{T}"/> it does not depend on the synchronization context of whoever
	/// created it.
	/// </summary>
	public sealed class UiProgress<T> : IProgress<T>
	{
		private readonly Action<T> handler;

		public UiProgress(Action<T> handler)
		{
			this.handler = handler ?? throw new ArgumentNullException(nameof(handler));
		}

		public void Report(T value)
		{
			UiDispatch.Post(() => handler(value));
		}
	}
}
