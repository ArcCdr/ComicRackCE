extern alias vbcore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ComicRack.Compat.VisualBasic;
using vbcore::Microsoft.VisualBasic.FileIO;
using FileSystem = Microsoft.VisualBasic.FileIO.FileSystem;
using Xunit;

namespace Compat.VisualBasic.Tests
{
	/// <summary>
	/// P4.17.4: <c>FileSystem</c> with <c>UIOption</c> and <c>RecycleOption</c> over temporary files, a fake Trash and a recording dialog. The real Trash is never touched; the one test of the real
	/// framework type shows why this assembly exists.
	/// </summary>
	public sealed class FileSystemTests : IDisposable
	{
		private readonly string root;
		private readonly ITrash savedTrash = FileSystemServices.Trash;
		private readonly IFileSystemUi savedUi = FileSystemServices.Ui;
		private readonly FakeTrash trash;
		private readonly RecordingUi ui = new RecordingUi();

		public FileSystemTests()
		{
			root = Path.Combine(Path.GetTempPath(), "crmac-vb-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(root);
			trash = new FakeTrash(Path.Combine(root, ".trash"));
			FileSystemServices.Trash = trash;
			FileSystemServices.Ui = ui;
		}

		public void Dispose()
		{
			FileSystemServices.Trash = savedTrash;
			FileSystemServices.Ui = savedUi;
			try
			{
				Directory.Delete(root, recursive: true);
			}
			catch (IOException)
			{
			}
		}

		private sealed class FakeTrash : ITrash
		{
			private readonly string folder;

			public FakeTrash(string folder)
			{
				this.folder = folder;
			}

			public List<string> Moved { get; } = new List<string>();

			public Exception Fail { get; set; }

			public void MoveToTrash(string fullPath)
			{
				if (Fail != null)
				{
					throw Fail;
				}
				Directory.CreateDirectory(folder);
				string target = Path.Combine(folder, Path.GetFileName(fullPath));
				if (Directory.Exists(fullPath))
				{
					Directory.Move(fullPath, target);
				}
				else
				{
					File.Move(fullPath, target);
				}
				Moved.Add(fullPath);
			}
		}

		private sealed class RecordingUi : IFileSystemUi
		{
			public List<string> Errors { get; } = new List<string>();

			public void ShowError(string message) => Errors.Add(message);
		}

		private string File_(string name, string text = "x")
		{
			string path = Path.Combine(root, name);
			Directory.CreateDirectory(Path.GetDirectoryName(path));
			File.WriteAllText(path, text);
			return path;
		}

		[Theory]
		[InlineData(UIOption.OnlyErrorDialogs)]
		[InlineData(UIOption.AllDialogs)]
		public void DeleteFile_to_the_Recycle_Bin_moves_the_file_to_the_Trash(UIOption ui)
		{
			string file = File_("a.cbz");
			FileSystem.DeleteFile(file, ui, RecycleOption.SendToRecycleBin);
			Assert.False(File.Exists(file));
			Assert.Equal(new[] { file }, trash.Moved);
			Assert.True(File.Exists(Path.Combine(root, ".trash", "a.cbz")));
			Assert.Empty(this.ui.Errors);
		}

		[Fact]
		public void DeleteFile_without_the_Recycle_Bin_deletes_for_good_and_leaves_the_Trash_alone()
		{
			string file = File_("a.cbz");
			FileSystem.DeleteFile(file, UIOption.OnlyErrorDialogs, RecycleOption.DeletePermanently);
			Assert.False(File.Exists(file));
			Assert.Empty(trash.Moved);
		}

		[Fact]
		public void DeleteFile_with_the_cancel_option_overload_behaves_the_same()
		{
			string file = File_("a.cbz");
			FileSystem.DeleteFile(file, UIOption.AllDialogs, RecycleOption.SendToRecycleBin, UICancelOption.DoNothing);
			Assert.False(File.Exists(file));
			Assert.Single(trash.Moved);
		}

		[Fact]
		public void A_failing_Trash_throws_shows_the_error_once_and_never_deletes_the_file_for_good()
		{
			string file = File_("a.cbz", "keep me");
			trash.Fail = new IOException("Cannot move to the Trash: disk full");
			IOException ex = Assert.Throws<IOException>(() => FileSystem.DeleteFile(file, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin));
			Assert.Contains("disk full", ex.Message);
			Assert.Equal("keep me", File.ReadAllText(file));
			Assert.Equal(new[] { ex.Message }, ui.Errors);
		}

		[Fact]
		public void A_Trash_that_throws_another_kind_of_exception_still_ends_in_an_IOException_and_keeps_the_file()
		{
			string file = File_("a.cbz");
			trash.Fail = new PlatformNotSupportedException("not here");
			IOException ex = Assert.Throws<IOException>(() => FileSystem.DeleteFile(file, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin));
			Assert.IsType<PlatformNotSupportedException>(ex.InnerException);
			Assert.True(File.Exists(file));
		}

		[Fact]
		public void Without_a_Trash_the_Recycle_Bin_route_throws_and_keeps_the_file()
		{
			string file = File_("a.cbz");
			FileSystemServices.Trash = null;
			Assert.Throws<IOException>(() => FileSystem.DeleteFile(file, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin));
			Assert.True(File.Exists(file));
		}

		[Fact]
		public void Without_a_dialog_the_error_is_only_thrown()
		{
			string file = File_("a.cbz");
			FileSystemServices.Ui = null;
			trash.Fail = new IOException("no");
			Assert.Throws<IOException>(() => FileSystem.DeleteFile(file, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin));
		}

		[Fact]
		public void A_dialog_that_fails_does_not_hide_the_error()
		{
			string file = File_("a.cbz");
			FileSystemServices.Ui = new ThrowingUi();
			trash.Fail = new IOException("the real problem");
			IOException ex = Assert.Throws<IOException>(() => FileSystem.DeleteFile(file, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin));
			Assert.Equal("the real problem", ex.Message);
		}

		private sealed class ThrowingUi : IFileSystemUi
		{
			public void ShowError(string message) => throw new InvalidOperationException("no window");
		}

		[Fact]
		public void Problems_with_the_arguments_are_thrown_without_a_dialog_and_without_touching_the_Trash()
		{
			string missing = Path.Combine(root, "missing.cbz");
			Assert.Throws<FileNotFoundException>(() => FileSystem.DeleteFile(missing, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin));
			Assert.Throws<ArgumentNullException>(() => FileSystem.DeleteFile("", UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin));
			Assert.Throws<ArgumentNullException>(() => FileSystem.DeleteFile(null, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin));
			Assert.Throws<DirectoryNotFoundException>(() => FileSystem.DeleteDirectory(missing, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin));
			Assert.Throws<System.ComponentModel.InvalidEnumArgumentException>(() => FileSystem.DeleteFile(File_("b.cbz"), (UIOption)7, RecycleOption.SendToRecycleBin));
			Assert.Throws<System.ComponentModel.InvalidEnumArgumentException>(() => FileSystem.DeleteFile(File_("b.cbz"), UIOption.AllDialogs, (RecycleOption)7));
			Assert.Empty(ui.Errors);
			Assert.Empty(trash.Moved);
		}

		[Fact]
		public void A_directory_is_not_a_file_to_DeleteFile()
		{
			string dir = Path.Combine(root, "folder");
			Directory.CreateDirectory(dir);
			Assert.Throws<FileNotFoundException>(() => FileSystem.DeleteFile(dir, UIOption.OnlyErrorDialogs, RecycleOption.DeletePermanently));
			Assert.True(Directory.Exists(dir));
		}

		[Fact]
		public void DeleteDirectory_moves_the_whole_folder_to_the_Trash_or_deletes_it_with_its_content()
		{
			string a = Path.GetDirectoryName(File_("a/inner/x.txt"));
			string a0 = Path.Combine(root, "a");
			FileSystem.DeleteDirectory(a0, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
			Assert.False(Directory.Exists(a0));
			Assert.True(File.Exists(Path.Combine(root, ".trash", "a", "inner", "x.txt")));
			Assert.NotNull(a);

			File_("b/inner/y.txt");
			FileSystem.DeleteDirectory(Path.Combine(root, "b"), UIOption.AllDialogs, RecycleOption.DeletePermanently, UICancelOption.ThrowException);
			Assert.False(Directory.Exists(Path.Combine(root, "b")));
			Assert.Single(trash.Moved);
		}

		[Fact]
		public void A_relative_path_is_taken_from_the_current_directory()
		{
			string file = File_("rel.cbz");
			string before = Directory.GetCurrentDirectory();
			try
			{
				Directory.SetCurrentDirectory(root);
				FileSystem.DeleteFile("rel.cbz", UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
			}
			finally
			{
				Directory.SetCurrentDirectory(before);
			}
			Assert.False(File.Exists(file));
			Assert.Equal("rel.cbz", Path.GetFileName(Assert.Single(trash.Moved)));
			Assert.True(Path.IsPathRooted(trash.Moved[0]));
		}

		[Fact]
		public void Names_with_a_backslash_or_non_ascii_letters_are_ordinary_names()
		{
			string file = File_("Tintin\\Café é.cbz");
			FileSystem.DeleteFile(file, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
			Assert.Single(trash.Moved);
			Assert.False(File.Exists(file));
		}

		[Fact]
		public void CopyFile_and_MoveFile_with_a_UIOption_work_and_create_the_target_folder()
		{
			string src = File_("src.cbz", "data");
			string copy = Path.Combine(root, "x", "y", "copy.cbz");
			FileSystem.CopyFile(src, copy, UIOption.AllDialogs);
			Assert.Equal("data", File.ReadAllText(copy));
			Assert.True(File.Exists(src));

			string moved = Path.Combine(root, "moved.cbz");
			FileSystem.MoveFile(src, moved, UIOption.OnlyErrorDialogs, UICancelOption.DoNothing);
			Assert.False(File.Exists(src));
			Assert.Equal("data", File.ReadAllText(moved));
			Assert.Empty(ui.Errors);
		}

		[Fact]
		public void CopyFile_onto_an_existing_file_is_an_error_that_is_shown_and_thrown_and_keeps_the_target()
		{
			string src = File_("src.cbz", "new");
			string dst = File_("dst.cbz", "old");
			IOException ex = Assert.Throws<IOException>(() => FileSystem.CopyFile(src, dst, UIOption.OnlyErrorDialogs));
			Assert.Equal("old", File.ReadAllText(dst));
			Assert.Equal(new[] { ex.Message }, ui.Errors);
		}

		[Fact]
		public void MoveFile_onto_an_existing_file_is_an_error_that_keeps_both_files()
		{
			string src = File_("src.cbz", "new");
			string dst = File_("dst.cbz", "old");
			Assert.Throws<IOException>(() => FileSystem.MoveFile(src, dst, UIOption.AllDialogs));
			Assert.Equal("new", File.ReadAllText(src));
			Assert.Equal("old", File.ReadAllText(dst));
			Assert.Single(ui.Errors);
		}

		[Fact]
		public void A_missing_source_is_thrown_without_a_dialog()
		{
			Assert.Throws<FileNotFoundException>(() => FileSystem.CopyFile(Path.Combine(root, "nope"), Path.Combine(root, "dst"), UIOption.AllDialogs));
			Assert.Throws<FileNotFoundException>(() => FileSystem.MoveFile(Path.Combine(root, "nope"), Path.Combine(root, "dst"), UIOption.AllDialogs));
			Assert.Empty(ui.Errors);
		}

		[Fact]
		public void CopyDirectory_and_MoveDirectory_with_a_UIOption_work()
		{
			File_("d/one.txt", "1");
			File_("d/sub/two.txt", "2");
			FileSystem.CopyDirectory(Path.Combine(root, "d"), Path.Combine(root, "copy"), UIOption.AllDialogs);
			Assert.Equal("2", File.ReadAllText(Path.Combine(root, "copy", "sub", "two.txt")));
			Assert.True(File.Exists(Path.Combine(root, "d", "one.txt")));

			FileSystem.MoveDirectory(Path.Combine(root, "d"), Path.Combine(root, "moved"), UIOption.OnlyErrorDialogs, UICancelOption.ThrowException);
			Assert.False(Directory.Exists(Path.Combine(root, "d")));
			Assert.Equal("1", File.ReadAllText(Path.Combine(root, "moved", "one.txt")));
			Assert.Empty(ui.Errors);
		}

		[Fact]
		public void The_members_without_a_UIOption_are_the_frameworks()
		{
			string file = File_("t.txt", "hello");
			Assert.True(FileSystem.FileExists(file));
			Assert.Equal("hello", FileSystem.ReadAllText(file));
			Assert.Equal(Path.Combine(root, "t.txt"), FileSystem.CombinePath(root, "t.txt"));
			Assert.Equal(new[] { file }, FileSystem.GetFiles(root).ToArray());
			FileSystem.DeleteFile(file);
			Assert.False(File.Exists(file));
			Assert.NotEmpty(FileSystem.Drives);
			Assert.Equal(Directory.GetCurrentDirectory(), FileSystem.CurrentDirectory);
		}

		[Fact]
		public void The_framework_type_refuses_what_this_one_does_which_is_why_the_assembly_exists()
		{
			string file = File_("a.cbz");
			Exception ex = Record.Exception(() => vbcore::Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(file, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin));
			Assert.NotNull(ex);
			Assert.True(File.Exists(file));
		}

		private static string Sig(MethodInfo m) => m.ReturnType.FullName + " " + m.Name + "(" + string.Join(",", m.GetParameters().Select(p => p.ParameterType.FullName + " " + p.Name)) + ")";

		[Fact]
		public void Every_public_member_of_the_frameworks_FileSystem_exists_here_with_the_same_signature()
		{
			Type framework = typeof(vbcore::Microsoft.VisualBasic.FileIO.FileSystem);
			Type ours = typeof(FileSystem);
			Assert.NotSame(framework, ours);
			Assert.True(ours.IsAbstract && ours.IsSealed, "static class");
			const BindingFlags flags = BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly;
			var expected = framework.GetMethods(flags).Select(Sig).OrderBy(s => s, StringComparer.Ordinal).ToList();
			var actual = ours.GetMethods(flags).Select(Sig).OrderBy(s => s, StringComparer.Ordinal).ToList();
			Assert.Equal(expected, actual);
			Assert.Equal(framework.GetProperties(flags).Select(p => p.Name + ":" + p.PropertyType.FullName + ":" + p.CanWrite).OrderBy(s => s).ToList(),
				ours.GetProperties(flags).Select(p => p.Name + ":" + p.PropertyType.FullName + ":" + p.CanWrite).OrderBy(s => s).ToList());
		}

		[Fact]
		public void Every_other_exported_type_of_the_frameworks_assembly_resolves_through_this_assembly_to_the_frameworks_own()
		{
			Assembly core = typeof(vbcore::Microsoft.VisualBasic.FileIO.FileSystem).Assembly;
			Assembly mine = typeof(FileSystem).Assembly;
			Assert.Equal("Microsoft.VisualBasic", mine.GetName().Name);
			Assert.True(mine.GetName().Version > new Version(10, 0, 0, 0));
			var forwarded = mine.GetForwardedTypes().Select(t => t.FullName).ToHashSet();
			foreach (Type type in core.GetExportedTypes().Where(t => !t.IsNested))
			{
				if (type.FullName == typeof(FileSystem).FullName)
				{
					Assert.DoesNotContain(type.FullName, forwarded);
					continue;
				}
				Assert.True(forwarded.Contains(type.FullName), type.FullName + " is not forwarded");
			}
			Assert.Same(core, typeof(UIOption).Assembly);
			Assert.Same(core, typeof(RecycleOption).Assembly);
			Assert.Equal(typeof(vbcore::Microsoft.VisualBasic.FileIO.TextFieldParser), mine.GetType("Microsoft.VisualBasic.FileIO.TextFieldParser"));
		}

		[Fact]
		public void The_name_that_a_program_built_for_the_NET_Framework_uses_resolves_to_this_assembly()
		{
			// the deps entry of the output folder carries the version (mac/Directory.Build.targets), so the host takes this file and not the shared framework's
			Assembly byFrameworkName = Assembly.Load(new AssemblyName("Microsoft.VisualBasic, Version=10.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a"));
			Assert.Same(typeof(FileSystem).Assembly, byFrameworkName);
			Assert.Equal(Path.Combine(AppContext.BaseDirectory, "Microsoft.VisualBasic.dll"), byFrameworkName.Location);
		}
	}
}
