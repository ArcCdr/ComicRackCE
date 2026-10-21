using System;
using System.IO;
using ComicRack.Mac.Host.Platform;
using cYo.Common.Win32;
using Xunit;

namespace ComicRack.Mac.Host.Tests
{
	/// <summary>P2.1: Trash, Reveal and Open. Nothing here opens a window: Reveal and Open are only checked for missing paths.</summary>
	public class MacShellTests
	{
		private static string NewTempFile(string name = null)
		{
			string path = Path.Combine(Path.GetTempPath(), (name ?? "crmac-shell-") + Guid.NewGuid().ToString("N") + ".txt");
			File.WriteAllText(path, "trash me");
			return path;
		}

		private static void DeleteFromTrash(string trashed)
		{
			if (trashed != null && File.Exists(trashed))
			{
				File.Delete(trashed);
			}
		}

		[Fact]
		public void MoveToTrash_moves_the_file_and_reports_its_new_path()
		{
			string path = NewTempFile();
			string trashed = null;
			try
			{
				trashed = MacShell.MoveToTrash(path);
				Assert.False(File.Exists(path));
				Assert.NotNull(trashed);
				Assert.True(File.Exists(trashed), trashed);
				Assert.Contains(".Trash", trashed);
				Assert.Equal("trash me", File.ReadAllText(trashed));
			}
			finally
			{
				DeleteFromTrash(trashed);
				if (File.Exists(path))
				{
					File.Delete(path);
				}
			}
		}

		[Fact]
		public void MoveToTrash_handles_folders_and_non_ascii_names()
		{
			string dir = Path.Combine(Path.GetTempPath(), "crmac-shell-éè-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(dir);
			File.WriteAllText(Path.Combine(dir, "a.txt"), "x");
			string trashed = null;
			try
			{
				trashed = MacShell.MoveToTrash(dir);
				Assert.False(Directory.Exists(dir));
				Assert.True(File.Exists(Path.Combine(trashed, "a.txt")), trashed);
			}
			finally
			{
				if (trashed != null && Directory.Exists(trashed))
				{
					Directory.Delete(trashed, recursive: true);
				}
				if (Directory.Exists(dir))
				{
					Directory.Delete(dir, recursive: true);
				}
			}
		}

		[Fact]
		public void MoveToTrash_of_a_missing_path_throws_and_changes_nothing()
		{
			string path = Path.Combine(Path.GetTempPath(), "crmac-shell-missing-" + Guid.NewGuid().ToString("N"));
			Assert.Throws<FileNotFoundException>(() => MacShell.MoveToTrash(path));
			Assert.Throws<ArgumentException>(() => MacShell.MoveToTrash(""));
		}

		[Fact]
		public void Reveal_and_Open_skip_paths_that_do_not_exist()
		{
			string path = Path.Combine(Path.GetTempPath(), "crmac-shell-missing-" + Guid.NewGuid().ToString("N"));
			Assert.False(MacShell.Reveal(path));
			Assert.False(MacShell.Reveal());
			Assert.False(MacShell.Reveal(null, ""));
			Assert.False(MacShell.OpenWithDefaultApp(path));
			Assert.False(MacShell.OpenWithDefaultApp(null));
		}

		[Fact]
		public void ShellFile_DeleteFile_sends_files_to_the_Trash_through_the_handler()
		{
			string path = NewTempFile();
			string trashed = null;
			Action<string> before = ShellFile.TrashHandler;
			try
			{
				ShellFile.TrashHandler = p => trashed = MacShell.MoveToTrash(p);
				ShellFile.DeleteFile(path, Path.Combine(Path.GetTempPath(), "crmac-shell-missing-" + Guid.NewGuid().ToString("N")));
				Assert.False(File.Exists(path));
				Assert.True(File.Exists(trashed), trashed);
			}
			finally
			{
				ShellFile.TrashHandler = before;
				DeleteFromTrash(trashed);
				if (File.Exists(path))
				{
					File.Delete(path);
				}
			}
		}

		[Fact]
		public void ShellFile_DeleteFile_with_NoRecycleBin_deletes_for_good()
		{
			string path = NewTempFile();
			Action<string> before = ShellFile.TrashHandler;
			try
			{
				ShellFile.TrashHandler = p => throw new InvalidOperationException("must not be called");
				ShellFile.DeleteFile(ShellFileDeleteOptions.NoRecycleBin, path);
				Assert.False(File.Exists(path));
			}
			finally
			{
				ShellFile.TrashHandler = before;
				if (File.Exists(path))
				{
					File.Delete(path);
				}
			}
		}

		[Fact]
		public void ShellFile_DeleteFile_without_a_handler_keeps_the_file()
		{
			string path = NewTempFile();
			Action<string> before = ShellFile.TrashHandler;
			try
			{
				ShellFile.TrashHandler = null;
				Assert.Throws<InvalidOperationException>(() => ShellFile.DeleteFile(path));
				Assert.True(File.Exists(path));
			}
			finally
			{
				ShellFile.TrashHandler = before;
				File.Delete(path);
			}
		}
	}
}
