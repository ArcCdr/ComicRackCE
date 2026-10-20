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
	/// A file that cannot be read (damaged XML, wrong root, empty) is copied to <c>Settings.xml.bad</c>, the defaults are used, and the next save replaces the
	/// damaged file; <see cref="LoadNote"/> says what happened.
	/// </summary>
	public sealed class SettingsStore : IDisposable
	{
		public const string FileName = "Settings.xml";

		/// <summary>The quiet time after the last change before a save.</summary>
		public static readonly TimeSpan DefaultSaveDelay = TimeSpan.FromMilliseconds(750);

		private static readonly XmlSerializer serializer = new XmlSerializer(typeof(AppPreferences));

		private readonly object gate = new object();
		private readonly ISaveScheduler scheduler;
		private readonly Action<string> log;
		private IDisposable pending;
		private bool dirty;
		private bool disposed;

		/// <summary>The file this store reads and writes.</summary>
		public string Path { get; }

		/// <summary>The preferences; every change is saved.</summary>
		public AppPreferences Current { get; }

		/// <summary>Null, or what was wrong with the file that was read (it was damaged and a copy was kept).</summary>
		public string LoadNote { get; }

		/// <summary>The quiet time after the last change before a save.</summary>
		public TimeSpan SaveDelay { get; set; } = DefaultSaveDelay;

		/// <summary>How many times the file was written by this store.</summary>
		public int SaveCount { get; private set; }

		/// <summary>The exception of the last failed save, or null. A failed save is logged and tried again at the next change and at <see cref="Flush"/>.</summary>
		public Exception LastSaveError { get; private set; }

		private SettingsStore(string path, AppPreferences preferences, string note, ISaveScheduler scheduler, Action<string> log)
		{
			Path = path;
			Current = preferences;
			LoadNote = note;
			this.scheduler = scheduler ?? new TimerSaveScheduler();
			this.log = log;
			Current.PropertyChanged += OnChanged;
			if (note != null)
			{
				// the damaged file is replaced by the defaults soon, so it is not reported again at every start
				QueueSave();
			}
		}

		/// <summary>The settings file inside a data folder.</summary>
		public static string PathIn(string dataFolder) => System.IO.Path.Combine(dataFolder, FileName);

		/// <summary>
		/// Reads <paramref name="path"/> (the defaults if it does not exist or is damaged; <see cref="LoadNote"/> says which) and starts watching the result for changes.
		/// <paramref name="log"/> receives the message of a save that failed.
		/// </summary>
		public static SettingsStore Open(string path, ISaveScheduler scheduler = null, Action<string> log = null)
		{
			path = System.IO.Path.GetFullPath(path);
			AppPreferences preferences = Load(path, out string note);
			return new SettingsStore(path, preferences, note, scheduler, log);
		}

		/// <summary>
		/// Reads the file. A missing file gives the defaults and no note. A damaged one gives the defaults, a copy of it as <c>.bad</c> beside it (an older copy is
		/// replaced) and a note.
		/// </summary>
		public static AppPreferences Load(string path, out string note)
		{
			note = null;
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
			catch (Exception ex) when (ex is InvalidOperationException || ex is XmlException || ex is InvalidDataException || ex is IOException || ex is UnauthorizedAccessException || ex is FormatException || ex is OverflowException)
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
					kept = "the copy as " + System.IO.Path.GetFileName(bad) + " failed (" + copyError.Message + ")";
				}
				string reason = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
				note = "The settings file " + path + " could not be read (" + reason + "); the defaults are used and " + kept + ".";
				return new AppPreferences();
			}
		}

		/// <summary>Writes the preferences to <paramref name="path"/> now: a temporary file in the same folder, renamed over the target.</summary>
		public static void Save(string path, AppPreferences preferences)
		{
			string folder = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));
			Directory.CreateDirectory(folder);
			string temp = System.IO.Path.Combine(folder, System.IO.Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N").Substring(0, 8) + ".tmp");
			try
			{
				using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
				using (XmlWriter writer = XmlWriter.Create(stream, new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false) }))
				{
					serializer.Serialize(writer, preferences);
					writer.Flush();
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
				if (disposed)
				{
					return;
				}
				dirty = true;
				pending?.Dispose();
				pending = scheduler.Schedule(SaveDelay, SaveNow);
			}
		}

		private void SaveNow()
		{
			lock (gate)
			{
				if (!dirty)
				{
					return;
				}
				pending?.Dispose();
				pending = null;
				dirty = false;
				try
				{
					Save(Path, Current);
					SaveCount++;
					LastSaveError = null;
				}
				catch (Exception ex)
				{
					dirty = true;
					LastSaveError = ex;
					log?.Invoke("The settings could not be saved to " + Path + ": " + ex.Message);
				}
			}
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
