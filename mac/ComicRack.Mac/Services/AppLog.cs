using System;
using System.Collections.Generic;
using System.Threading;
using ComicRack.Mac.Host;

namespace ComicRack.Mac.Services
{
	/// <summary>
	/// Logging of the app: goes to the host log file once the host is open, and to stderr (and a small buffer that is written
	/// to the file when the host opens) before and after. Never throws.
	/// </summary>
	public static class AppLog
	{
		private static readonly object sync = new object();
		private static readonly List<(string Level, string Message)> pending = new List<(string, string)>();
		private static HostLog target;
		private static int errorCount;

		/// <summary>Number of errors logged so far (a smoke test fails if it is not 0).</summary>
		public static int ErrorCount => Volatile.Read(ref errorCount);

		public static void Attach(HostLog log)
		{
			lock (sync)
			{
				target = log;
				if (log == null)
				{
					return;
				}
				foreach (var item in pending)
				{
					log.Write(item.Level, item.Message);
				}
				pending.Clear();
			}
		}

		public static void Detach()
		{
			lock (sync)
			{
				target = null;
			}
		}

		public static void Info(string message)
		{
			Write("INFO", message);
		}

		/// <summary>A problem the app copes with (it is not counted as an error).</summary>
		public static void Warn(string message)
		{
			Write("WARN", message);
		}

		public static void Error(string message, Exception ex = null)
		{
			Interlocked.Increment(ref errorCount);
			Write("ERROR", ex == null ? message : message + Environment.NewLine + ex);
		}

		private static void Write(string level, string message)
		{
			try
			{
				lock (sync)
				{
					if (target != null)
					{
						target.Write(level, message);
						return;
					}
					if (pending.Count < 200)
					{
						pending.Add((level, message));
					}
				}
				Console.Error.WriteLine("[{0}] {1}", level, message);
			}
			catch (Exception)
			{
			}
		}
	}
}
