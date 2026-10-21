using System;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Serialization;

namespace ComicRack.Mac.Host
{
	/// <summary>Runs an action once after a delay; asking again for the same slot replaces the pending action. A test supplies one it can release by hand.</summary>
	public interface ISaveScheduler
	{
		/// <summary>Runs <paramref name="action"/> after <paramref name="delay"/> unless the returned handle is disposed first.</summary>
		IDisposable Schedule(TimeSpan delay, Action action);
	}

	/// <summary>The scheduler of the app: a thread pool timer.</summary>
	public sealed class TimerSaveScheduler : ISaveScheduler
	{
		public IDisposable Schedule(TimeSpan delay, Action action)
		{
			return new System.Threading.Timer(_ => action(), null, delay, System.Threading.Timeout.InfiniteTimeSpan);
		}
	}

	/// <summary>
	/// Loads and saves <see cref="AppPreferences"/> as <c>&lt;data&gt;/Settings.xml</c> (XmlSerializer). A change is saved after a short quiet time
	/// (<see cref="SaveDelay"/>, so dragging a window or a slider writes once), and <see cref="Flush"/> saves at once what is still pending (at quit).
	/// A save writes a temporary file next to the settings and renames it over them, so a crash never leaves half a file.
	/// A file that is damaged (XML that does not parse, wrong root, a value of the wrong kind, empty) is copied to <c>Settings.xml.bad</c>, the defaults are used,
	/// and the next save replaces the damaged file; <see cref="LoadNote"/> says what happened. A file that could not be <b>read</b> (an IO or access error) is not
	/// known to be damaged: the defaults are used but nothing is ever written over it (<see cref="SavingBlocked"/>), and the same holds when the copy of a
	/// damaged file could not be kept, so the user's values are never destroyed by a transient problem.
	/// A save serializes under a short lock and writes outside it, so changing a setting never waits for the disk; a sequence number keeps an older snapshot from
	/// replacing a newer one.
	/// </summary>
	public sealed class SettingsStore : IDisposable
	{
		public const string FileName = "Settings.xml";

		/// <summary>The quiet time after the last change before a save.</summary>
		public static readonly TimeSpan DefaultSaveDelay = TimeSpan.FromMilliseconds(750);

		private static readonly XmlSerializer serializer = new XmlSerializer(typeof(AppPreferences));

		private readonly object gate = new object();
		private readonly object ioGate = new object();
		private readonly ISaveScheduler scheduler;
		private readonly Action<string> log;
		private readonly Action<string, byte[]> writer;
		private IDisposable pending;
		private bool dirty;
		private bool disposed;
		private long sequence; // the last snapshot taken (under gate)
		private long writtenSequence; // the snapshot the file holds (under ioGate)

		/// <summary>The file this store reads and writes.</summary>
		public string Path { get; }

		/// <summary>The preferences; every change is saved.</summary>
		public AppPreferences Current { get; }

		/// <summary>Null, or what was wrong with the file that was read (it was damaged, or it could not be read); the host logs it as a warning.</summary>
		public string LoadNote { get; }

		/// <summary>
		/// True when the file could not be read (an IO or access error) or when a damaged file could not be copied to <c>.bad</c>: the defaults are used and
		/// nothing is written to the file during this run, because that would destroy values that may still be good.
		/// </summary>
		public bool SavingBlocked { get; }

		/// <summary>Test seam: called after a snapshot was taken and before it waits to be written (the sequence number of the snapshot).</summary>
		internal Action<long> AfterSnapshot { get; set; }

		/// <summary>The quiet time after the last change before a save.</summary>
		public TimeSpan SaveDelay { get; set; } = DefaultSaveDelay;

		/// <summary>How many times the file was written by this store.</summary>
		public int SaveCount { get; private set; }

		/// <summary>The exception of the last failed save, or null. A failed save is logged and tried again at the next change and at <see cref="Flush"/>.</summary>
		public Exception LastSaveError { get; private set; }

		private SettingsStore(string path, AppPreferences preferences, string note, bool replaceable, ISaveScheduler scheduler, Action<string> log, Action<string, byte[]> writer)
		{
			Path = path;
			Current = preferences;
			LoadNote = note;
			SavingBlocked = !replaceable;
			this.scheduler = scheduler ?? new TimerSaveScheduler();
			this.log = log;
			this.writer = writer ?? WriteAtomic;
			Current.PropertyChanged += OnChanged;
			if (note != null && replaceable)
			{
				// the damaged file is replaced by the defaults soon, so it is not reported again at every start
				QueueSave();
			}
		}

		/// <summary>The settings file inside a data folder.</summary>
		public static string PathIn(string dataFolder) => System.IO.Path.Combine(dataFolder, FileName);

		/// <summary>
		/// Reads <paramref name="path"/> (the defaults if it does not exist or is damaged; <see cref="LoadNote"/> says which) and starts watching the result for changes.
		/// <paramref name="log"/> receives the message of a save that failed. <paramref name="writer"/> replaces the file write (a test blocks or records it).
		/// </summary>
		public static SettingsStore Open(string path, ISaveScheduler scheduler = null, Action<string> log = null, Action<string, byte[]> writer = null)
		{
			path = System.IO.Path.GetFullPath(path);
			AppPreferences preferences = Load(path, out string note, out bool replaceable);
			return new SettingsStore(path, preferences, note, replaceable, scheduler, log, writer);
		}

		/// <summary>
		/// Reads the file. A missing file gives the defaults and no note. A damaged one (it does not parse) gives the defaults, a copy of it as <c>.bad</c> beside
		/// it (an older copy is replaced) and a note. A file that cannot be read for another reason (an IO or access error) gives the defaults and a note, and no copy.
		/// </summary>
		public static AppPreferences Load(string path, out string note)
		{
			return Load(path, out note, out _);
		}

		/// <summary>
		/// Like <see cref="Load(string, out string)"/>; <paramref name="replaceable"/> says whether a later save may replace the file: false when it could not be
		/// read, or when it is damaged and the copy as <c>.bad</c> failed.
		/// </summary>
		public static AppPreferences Load(string path, out string note, out bool replaceable)
		{
			note = null;
			replaceable = true;
			if (!File.Exists(path))
			{
				return new AppPreferences();
			}
			try
			{
				using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
				using (XmlReader reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
				{
					var loaded = serializer.Deserialize(reader) as AppPreferences;
					if (loaded == null)
					{
						throw new InvalidDataException("The file does not hold settings.");
					}
					return loaded;
				}
			}
			catch (Exception ex) when (ex is FileNotFoundException || ex is DirectoryNotFoundException)
			{
				// removed between the check and the open: nothing to read
				return new AppPreferences();
			}
			catch (Exception ex) when (ex is InvalidOperationException || ex is XmlException || ex is InvalidDataException || ex is FormatException)
			{
				string bad = path + ".bad";
				string kept;
				try
				{
					File.Copy(path, bad, overwrite: true);
					kept = "a copy was kept as " + System.IO.Path.GetFileName(bad);
				}
				catch (Exception copyError)
				{
					replaceable = false;
					kept = "the copy as " + System.IO.Path.GetFileName(bad) + " failed (" + copyError.Message + "), so the file is left as it is and the settings are not saved during this run";
				}
				string reason = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
				note = "The settings file " + path + " could not be read (" + reason + "); the defaults are used and " + kept + ".";
				return new AppPreferences();
			}
			catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
			{
				replaceable = false;
				note = "The settings file " + path + " could not be opened (" + ex.Message + "); the defaults are used and the file is left as it is, so the settings are not saved during this run.";
				return new AppPreferences();
			}
		}

		/// <summary>Writes the preferences to <paramref name="path"/> now: a temporary file in the same folder, renamed over the target.</summary>
		public static void Save(string path, AppPreferences preferences)
		{
			WriteAtomic(path, Serialize(preferences));
		}

		private static byte[] Serialize(AppPreferences preferences)
		{
			using (var memory = new MemoryStream())
			{
				using (XmlWriter writer = XmlWriter.Create(memory, new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false), CloseOutput = false }))
				{
					serializer.Serialize(writer, preferences);
					writer.Flush();
				}
				return memory.ToArray();
			}
		}

		private static void WriteAtomic(string path, byte[] content)
		{
			string folder = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));
			Directory.CreateDirectory(folder);
			string temp = System.IO.Path.Combine(folder, System.IO.Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N").Substring(0, 8) + ".tmp");
			try
			{
				using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
				{
					stream.Write(content, 0, content.Length);
					stream.Flush(true);
				}
				File.Move(temp, path, overwrite: true);
			}
			catch (Exception)
			{
				try
				{
					File.Delete(temp);
				}
				catch (Exception)
				{
				}
				throw;
			}
		}

		private void OnChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e) => QueueSave();

		private void QueueSave()
		{
			lock (gate)
			{
				if (disposed || SavingBlocked)
				{
					return;
				}
				dirty = true;
				pending?.Dispose();
				pending = scheduler.Schedule(SaveDelay, SaveNow);
			}
		}

		// The state is serialized to memory under the lock (short, no IO); the file is written outside it, so a setter never waits for the disk. Writers
		// take turns on ioGate, and a snapshot older than the one the file holds is dropped, so the file never goes back to older values.
		private void SaveNow()
		{
			byte[] content;
			long mine;
			lock (gate)
			{
				if (!dirty || SavingBlocked)
				{
					return;
				}
				pending?.Dispose();
				pending = null;
				dirty = false;
				try
				{
					content = Serialize(Current);
				}
				catch (Exception ex)
				{
					dirty = true;
					RecordFailure(ex);
					return;
				}
				mine = ++sequence;
			}
			AfterSnapshot?.Invoke(mine);
			Exception failure = null;
			lock (ioGate)
			{
				if (mine < writtenSequence)
				{
					return;
				}
				try
				{
					writer(Path, content);
					writtenSequence = mine;
					SaveCount++;
					LastSaveError = null;
				}
				catch (Exception ex)
				{
					failure = ex;
				}
			}
			if (failure != null)
			{
				lock (gate)
				{
					if (mine == sequence)
					{
						dirty = true; // tried again at the next change and at the flush
					}
				}
				RecordFailure(failure);
			}
		}

		private void RecordFailure(Exception ex)
		{
			LastSaveError = ex;
			log?.Invoke("The settings could not be saved to " + Path + ": " + ex.Message);
		}

		/// <summary>True while a change has not been written yet.</summary>
		public bool IsDirty
		{
			get
			{
				lock (gate)
				{
					return dirty;
				}
			}
		}

		/// <summary>Writes what is pending now (at quit). Does nothing if nothing changed since the last save.</summary>
		public void Flush()
		{
			SaveNow();
			lock (ioGate)
			{
				// a write that another thread started before this call has finished when the lock is free
			}
		}

		/// <summary>Writes what is pending and stops watching.</summary>
		public void Dispose()
		{
			Flush();
			lock (gate)
			{
				disposed = true;
				pending?.Dispose();
				pending = null;
			}
			Current.PropertyChanged -= OnChanged;
		}
	}
}
