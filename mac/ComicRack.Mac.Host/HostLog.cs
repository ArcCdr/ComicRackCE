using System;
using System.IO;
using System.Text;

namespace ComicRack.Mac.Host
{
	/// <summary>
	/// A line-per-entry log file in the logs folder. Thread safe, flushed after every line (the process can die on an unhandled
	/// exception), never throws: a log that cannot be written must not take the app down. A file over 5 MB is moved to
	/// "ComicRack.log.1" when the host opens.
	/// </summary>
	public sealed class HostLog : IDisposable
	{
		private const long MaxBytes = 5 * 1024 * 1024;

		private readonly object sync = new object();
		private StreamWriter writer;

		public string File { get; }

		public HostLog(string file)
		{
			File = file;
			try
			{
				Directory.CreateDirectory(Path.GetDirectoryName(file));
				var info = new FileInfo(file);
				if (info.Exists && info.Length > MaxBytes)
				{
					string old = file + ".1";
					if (System.IO.File.Exists(old))
					{
						System.IO.File.Delete(old);
					}
					System.IO.File.Move(file, old);
				}
				writer = new StreamWriter(new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(false));
			}
			catch (Exception)
			{
				writer = null;
			}
		}

		public void Info(string message)
		{
			Write("INFO", message);
		}

		public void Error(string message, Exception ex = null)
		{
			Write("ERROR", ex == null ? message : message + Environment.NewLine + ex);
		}

		public void Write(string level, string message)
		{
			lock (sync)
			{
				if (writer == null)
				{
					return;
				}
				try
				{
					writer.WriteLine("{0:yyyy-MM-dd HH:mm:ss.fff} [{1}] [{2}] {3}", DateTime.Now, level, Environment.CurrentManagedThreadId, message);
					writer.Flush();
				}
				catch (Exception)
				{
				}
			}
		}

		public void Dispose()
		{
			lock (sync)
			{
				try
				{
					writer?.Dispose();
				}
				catch (Exception)
				{
				}
				writer = null;
			}
		}
	}
}
