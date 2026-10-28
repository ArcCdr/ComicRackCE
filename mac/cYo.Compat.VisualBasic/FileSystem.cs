extern alias vbcore;
using System;
using System.ComponentModel;
using System.IO;
using ComicRack.Compat.VisualBasic;
using Core = vbcore::Microsoft.VisualBasic.FileIO.FileSystem;
using vbcore::Microsoft.VisualBasic.FileIO;

namespace Microsoft.VisualBasic.FileIO
{
	/// <summary>
	/// <c>Microsoft.VisualBasic.FileIO.FileSystem</c> for macOS (P4.17.4). The framework's type refuses every overload with a <see cref="UIOption"/> on this platform ("UI not available for copy
	/// or move"). Here those overloads do what the Windows shell does without its progress window: copy and move without an overwrite (the shell would ask), delete for good or send to the Trash
	/// (<c>RecycleOption.SendToRecycleBin</c>, through <see cref="FileSystemServices.Trash"/>; if the Trash cannot take it the call throws and nothing is deleted), and show a failure through
	/// <see cref="FileSystemServices.Ui"/> before it is thrown. A problem with the arguments (a missing source, an empty path) is thrown without a dialog, as the original checks it before the shell.
	/// Every other member calls the framework's type.
	/// </summary>
	public static partial class FileSystem
	{
		public static void CopyFile(string sourceFileName, string destinationFileName, UIOption showUI)
			=> CopyFile(sourceFileName, destinationFileName, showUI, UICancelOption.ThrowException);

		public static void CopyFile(string sourceFileName, string destinationFileName, UIOption showUI, UICancelOption onUserCancel)
		{
			Check(showUI, onUserCancel);
			Guard(() => Core.CopyFile(sourceFileName, destinationFileName, false));
		}

		public static void MoveFile(string sourceFileName, string destinationFileName, UIOption showUI)
			=> MoveFile(sourceFileName, destinationFileName, showUI, UICancelOption.ThrowException);

		public static void MoveFile(string sourceFileName, string destinationFileName, UIOption showUI, UICancelOption onUserCancel)
		{
			Check(showUI, onUserCancel);
			Guard(() => Core.MoveFile(sourceFileName, destinationFileName, false));
		}

		public static void CopyDirectory(string sourceDirectoryName, string destinationDirectoryName, UIOption showUI)
			=> CopyDirectory(sourceDirectoryName, destinationDirectoryName, showUI, UICancelOption.ThrowException);

		public static void CopyDirectory(string sourceDirectoryName, string destinationDirectoryName, UIOption showUI, UICancelOption onUserCancel)
		{
			Check(showUI, onUserCancel);
			Guard(() => Core.CopyDirectory(sourceDirectoryName, destinationDirectoryName, false));
		}

		public static void MoveDirectory(string sourceDirectoryName, string destinationDirectoryName, UIOption showUI)
			=> MoveDirectory(sourceDirectoryName, destinationDirectoryName, showUI, UICancelOption.ThrowException);

		public static void MoveDirectory(string sourceDirectoryName, string destinationDirectoryName, UIOption showUI, UICancelOption onUserCancel)
		{
			Check(showUI, onUserCancel);
			Guard(() => Core.MoveDirectory(sourceDirectoryName, destinationDirectoryName, false));
		}

		public static void DeleteFile(string file, UIOption showUI, RecycleOption recycle)
			=> DeleteFile(file, showUI, recycle, UICancelOption.ThrowException);

		public static void DeleteFile(string file, UIOption showUI, RecycleOption recycle, UICancelOption onUserCancel)
		{
			Check(showUI, onUserCancel);
			Check(recycle);
			string full = FullPath(file, "file");
			if (!File.Exists(full))
			{
				throw new FileNotFoundException("Could not find file '" + full + "'.", full);
			}
			Guard(() =>
			{
				if (recycle == RecycleOption.SendToRecycleBin)
				{
					MoveToTrash(full);
				}
				else
				{
					Core.DeleteFile(full);
				}
			});
		}

		public static void DeleteDirectory(string directory, UIOption showUI, RecycleOption recycle)
			=> DeleteDirectory(directory, showUI, recycle, UICancelOption.ThrowException);

		public static void DeleteDirectory(string directory, UIOption showUI, RecycleOption recycle, UICancelOption onUserCancel)
		{
			Check(showUI, onUserCancel);
			Check(recycle);
			string full = FullPath(directory, "directory");
			if (!Directory.Exists(full))
			{
				throw new DirectoryNotFoundException("Could not find a part of the path '" + full + "'.");
			}
			Guard(() =>
			{
				if (recycle == RecycleOption.SendToRecycleBin)
				{
					MoveToTrash(full);
				}
				else
				{
					Core.DeleteDirectory(full, DeleteDirectoryOption.DeleteAllContents);
				}
			});
		}

		private static string FullPath(string path, string argument)
		{
			if (string.IsNullOrEmpty(path))
			{
				throw new ArgumentNullException(argument);
			}
			return Path.GetFullPath(path);
		}

		private static void Check(UIOption showUI, UICancelOption onUserCancel)
		{
			if (!Enum.IsDefined(typeof(UIOption), showUI))
			{
				throw new InvalidEnumArgumentException(nameof(showUI), (int)showUI, typeof(UIOption));
			}
			if (!Enum.IsDefined(typeof(UICancelOption), onUserCancel))
			{
				throw new InvalidEnumArgumentException(nameof(onUserCancel), (int)onUserCancel, typeof(UICancelOption));
			}
		}

		private static void Check(RecycleOption recycle)
		{
			if (!Enum.IsDefined(typeof(RecycleOption), recycle))
			{
				throw new InvalidEnumArgumentException(nameof(recycle), (int)recycle, typeof(RecycleOption));
			}
		}

		private static void MoveToTrash(string fullPath)
		{
			ITrash trash = FileSystemServices.Trash;
			if (trash == null)
			{
				throw new IOException("Cannot move '" + fullPath + "' to the Trash: the Trash is not available.");
			}
			try
			{
				trash.MoveToTrash(fullPath);
			}
			catch (IOException)
			{
				throw;
			}
			catch (Exception ex)
			{
				throw new IOException("Cannot move '" + fullPath + "' to the Trash: " + ex.Message, ex);
			}
		}

		/// <summary>Runs the operation; a failure of the operation itself (not of its arguments) is shown through the script UI and thrown again.</summary>
		private static void Guard(Action operation)
		{
			try
			{
				operation();
			}
			catch (Exception ex) when (IsOperationFailure(ex))
			{
				try
				{
					FileSystemServices.Ui?.ShowError(ex.Message);
				}
				catch (Exception)
				{
					// a dialog that cannot be shown must not hide the failure
				}
				throw;
			}
		}

		private static bool IsOperationFailure(Exception ex)
			=> ex is UnauthorizedAccessException || (ex is IOException && !(ex is FileNotFoundException) && !(ex is DirectoryNotFoundException));
	}
}
