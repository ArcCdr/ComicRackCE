using System.Diagnostics;

namespace ComicRack.Mac.Host
{
	/// <summary>
	/// Forwards the warnings and errors that the Core writes with <c>Trace.TraceWarning</c> and <c>Trace.TraceError</c> to the host log.
	/// Plain <c>Trace.Write</c> and <c>Debug</c> output is ignored on purpose (it is diagnostic noise, not an event).
	/// </summary>
	internal sealed class HostTraceListener : TraceListener
	{
		private readonly HostLog log;

		public HostTraceListener(HostLog log)
		{
			this.log = log;
		}

		public override void Write(string message)
		{
		}

		public override void WriteLine(string message)
		{
		}

		public override void TraceEvent(TraceEventCache eventCache, string source, TraceEventType eventType, int id, string message)
		{
			Forward(eventType, message);
		}

		public override void TraceEvent(TraceEventCache eventCache, string source, TraceEventType eventType, int id, string format, params object[] args)
		{
			Forward(eventType, args == null || args.Length == 0 ? format : string.Format(format, args));
		}

		private void Forward(TraceEventType eventType, string message)
		{
			switch (eventType)
			{
			case TraceEventType.Critical:
			case TraceEventType.Error:
				log.Write("ERROR", message);
				break;
			case TraceEventType.Warning:
				log.Write("WARN", message);
				break;
			}
		}
	}
}
