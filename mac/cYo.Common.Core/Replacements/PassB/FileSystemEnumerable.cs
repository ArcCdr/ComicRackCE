// Pass B replacement for cYo.Common/Win32/FileSystemEnumerable.cs (excluded from the pass B compile).
// The original lists a folder with kernel32 FindFirstFile/FindNextFile, which do not exist on macOS: every call throws
// DllNotFoundException, so FileUtility.GetFiles (and with it ComicScanner and ScanItemFileOrFolder) found nothing. This stand-in
// keeps the public surface and the semantics (one folder level, files or folders, hidden and system entries skipped unless asked
// for) on top of System.IO. Entries come back sorted by name (ordinal, case-insensitive) as NTFS lists them; the mask "*.*" means
// "everything", as on Windows.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace cYo.Common.Win32
{
	public class FileSystemEnumerable : IEnumerable<string>, IEnumerable
	{
		private readonly string path;

		private readonly string findMask;

		private readonly FileSystemEnumeratorType entryType = FileSystemEnumeratorType.File;

		public FileSystemEnumerable(string path, string findMask, FileSystemEnumeratorType entryType)
		{
			this.entryType = entryType;
			this.path = path;
			this.findMask = findMask;
		}

		public FileSystemEnumerable(string path, string findMask)
			: this(path, findMask, FileSystemEnumeratorType.File)
		{
		}

		public FileSystemEnumerable(string path, FileSystemEnumeratorType entryType)
			: this(path, "*.*", entryType)
		{
		}

		public FileSystemEnumerable(string path)
			: this(path, FileSystemEnumeratorType.File)
		{
		}

		public IEnumerator<string> GetEnumerator()
		{
			string mask = string.IsNullOrEmpty(findMask) || findMask == "*.*" ? "*" : findMask;
			var options = new EnumerationOptions
			{
				IgnoreInaccessible = true,
				RecurseSubdirectories = false,
				AttributesToSkip = 0,
				MatchType = MatchType.Win32,
			};
			var entries = new List<(string name, bool isDirectory)>();
			try
			{
				foreach (FileSystemInfo info in new DirectoryInfo(path).EnumerateFileSystemInfos(mask, options))
				{
					FileAttributes a = info.Attributes;
					if (((entryType & FileSystemEnumeratorType.IncludeSystem) == 0 && (a & FileAttributes.System) != 0) || ((entryType & FileSystemEnumeratorType.IncludeHidden) == 0 && (a & FileAttributes.Hidden) != 0))
					{
						continue;
					}
					bool isDirectory = (a & FileAttributes.Directory) != 0;
					if (isDirectory ? (entryType & FileSystemEnumeratorType.Folder) != 0 : (entryType & FileSystemEnumeratorType.File) != 0)
					{
						entries.Add((info.Name, isDirectory));
					}
				}
			}
			catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException)
			{
				// A folder that does not exist or cannot be read has no entries, as FindFirstFile failing did.
			}
			foreach (var entry in entries.OrderBy(e => e.name, StringComparer.OrdinalIgnoreCase))
			{
				yield return Path.Combine(path, entry.name);
			}
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return GetEnumerator();
		}
	}
}
