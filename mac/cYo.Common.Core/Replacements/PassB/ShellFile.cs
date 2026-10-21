// Pass B rewrite of cYo.Common/Win32/ShellFile.cs (the original is removed from the pass B compile in Sources.props). Same public
// surface. The original deletes through the Windows shell (IFileOperation, SHFileOperation); here a file goes to the Trash through the
// handler that ComicRack.Mac.Host installs (MacShell.MoveToTrash), or is deleted for good with NoRecycleBin.
// Common cannot reference the Host (the Host references Common), hence the handler. MacHost.Open installs it.
using System;
using System.IO;
using System.Windows.Forms;
using cYo.Common.Win32.FileOperations;

namespace cYo.Common.Win32
{
	public static class ShellFile
	{
		public static FileOperationsAPI DeleteAPI { get; set; } = FileOperationsAPI.IFileOperation;

		/// <summary>Moves one existing file to the Trash. Installed by the macOS host.</summary>
		public static Action<string> TrashHandler { get; set; }

		public static void DeleteFile(IWin32Window window, ShellFileDeleteOptions options, params string[] files)
		{
			if (files == null || files.Length == 0)
				return;

			foreach (string file in files)
			{
				if (string.IsNullOrEmpty(file) || !File.Exists(file))
					continue;

				if (options.HasFlag(ShellFileDeleteOptions.NoRecycleBin))
				{
					File.Delete(file);
					continue;
				}
				Action<string> trash = TrashHandler;
				if (trash == null)
					throw new InvalidOperationException("No Trash handler is installed; the file is not deleted: " + file);
				trash(file);
			}
		}

		public static void DeleteFile(ShellFileDeleteOptions options, params string[] files)
		{
			DeleteFile(null, options, files);
		}

		public static void DeleteFile(params string[] files)
		{
			DeleteFile(ShellFileDeleteOptions.None, files);
		}
	}
}
