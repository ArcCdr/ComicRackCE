// Forwarding half of Microsoft.VisualBasic.FileIO.FileSystem (P4.17.4): every member that needs no UI calls the framework's type in Microsoft.VisualBasic.Core (extern alias vbcore).
// The members with a UIOption are in FileSystem.cs. Drift test: VisualBasicAssemblyTests compares the surface with the framework's.
extern alias vbcore;
using System;

namespace Microsoft.VisualBasic.FileIO
{
	public static partial class FileSystem
	{
		public static System.Collections.ObjectModel.ReadOnlyCollection<System.IO.DriveInfo> Drives => vbcore::Microsoft.VisualBasic.FileIO.FileSystem.Drives;

		public static string CurrentDirectory
		{
			get => vbcore::Microsoft.VisualBasic.FileIO.FileSystem.CurrentDirectory;
			set => vbcore::Microsoft.VisualBasic.FileIO.FileSystem.CurrentDirectory = value;
		}

		public static string CombinePath(string baseDirectory, string relativePath) =>
			vbcore::Microsoft.VisualBasic.FileIO.FileSystem.CombinePath(baseDirectory, relativePath);
		public static void CopyDirectory(string sourceDirectoryName, string destinationDirectoryName) =>
			vbcore::Microsoft.VisualBasic.FileIO.FileSystem.CopyDirectory(sourceDirectoryName, destinationDirectoryName);
		public static void CopyDirectory(string sourceDirectoryName, string destinationDirectoryName, bool overwrite) =>
			vbcore::Microsoft.VisualBasic.FileIO.FileSystem.CopyDirectory(sourceDirectoryName, destinationDirectoryName, overwrite);
		public static void CopyFile(string sourceFileName, string destinationFileName) =>
			vbcore::Microsoft.VisualBasic.FileIO.FileSystem.CopyFile(sourceFileName, destinationFileName);
		public static void CopyFile(string sourceFileName, string destinationFileName, bool overwrite) =>
			vbcore::Microsoft.VisualBasic.FileIO.FileSystem.CopyFile(sourceFileName, destinationFileName, overwrite);
		public static void CreateDirectory(string directory) =>
			vbcore::Microsoft.VisualBasic.FileIO.FileSystem.CreateDirectory(directory);
		public static void DeleteDirectory(string directory, vbcore::Microsoft.VisualBasic.FileIO.DeleteDirectoryOption onDirectoryNotEmpty) =>
			vbcore::Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(directory, onDirectoryNotEmpty);
		public static void DeleteFile(string file) =>
			vbcore::Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(file);
		public static bool DirectoryExists(string directory) =>
			vbcore::Microsoft.VisualBasic.FileIO.FileSystem.DirectoryExists(directory);
		public static bool FileExists(string file) =>
			vbcore::Microsoft.VisualBasic.FileIO.FileSystem.FileExists(file);
		public static System.Collections.ObjectModel.ReadOnlyCollection<string> FindInFiles(string directory, string containsText, bool ignoreCase, vbcore::Microsoft.VisualBasic.FileIO.SearchOption searchType) =>
			vbcore::Microsoft.VisualBasic.FileIO.FileSystem.FindInFiles(directory, containsText, ignoreCase, searchType);
		public static System.Collections.ObjectModel.ReadOnlyCollection<string> FindInFiles(string directory, string containsText, bool ignoreCase, vbcore::Microsoft.VisualBasic.FileIO.SearchOption searchType, params string[] fileWildcards) =>
			vbcore::Microsoft.VisualBasic.FileIO.FileSystem.FindInFiles(directory, containsText, ignoreCase, searchType, fileWildcards);
		public static System.Collections.ObjectModel.ReadOnlyCollection<string> GetDirectories(string directory) =>
			vbcore::Microsoft.VisualBasic.FileIO.FileSystem.GetDirectories(directory);
		public static System.Collections.ObjectModel.ReadOnlyCollection<string> GetDirectories(string directory, vbcore::Microsoft.VisualBasic.FileIO.SearchOption searchType, params string[] wildcards) =>
			vbcore::Microsoft.VisualBasic.FileIO.FileSystem.GetDirectories(directory, searchType, wildcards);
		public static System.IO.DirectoryInfo GetDirectoryInfo(string directory) =>
			vbcore::Microsoft.VisualBasic.FileIO.FileSystem.GetDirectoryInfo(directory);
		public static System.IO.DriveInfo GetDriveInfo(string drive) =>
			vbcore::Microsoft.VisualBasic.FileIO.FileSystem.GetDriveInfo(drive);
		public static System.IO.FileInfo GetFileInfo(string file) =>
			vbcore::Microsoft.VisualBasic.FileIO.FileSystem.GetFileInfo(file);
		public static System.Collections.ObjectModel.ReadOnlyCollection<string> GetFiles(string directory) =>
			vbcore::Microsoft.VisualBasic.FileIO.FileSystem.GetFiles(directory);
		public static System.Collections.ObjectModel.ReadOnlyCollection<string> GetFiles(string directory, vbcore::Microsoft.VisualBasic.FileIO.SearchOption searchType, params string[] wildcards) =>
			vbcore::Microsoft.VisualBasic.FileIO.FileSystem.GetFiles(directory, searchType, wildcards);
		public static string GetName(string path) =>
			vbcore::Microsoft.VisualBasic.FileIO.FileSystem.GetName(path);
		public static string GetParentPath(string path) =>
			vbcore::Microsoft.VisualBasic.FileIO.FileSystem.GetParentPath(path);
		public static string GetTempFileName() =>
			vbcore::Microsoft.VisualBasic.FileIO.FileSystem.GetTempFileName();
		public static void MoveDirectory(string sourceDirectoryName, string destinationDirectoryName) =>
			vbcore::Microsoft.VisualBasic.FileIO.FileSystem.MoveDirectory(sourceDirectoryName, destinationDirectoryName);
		public static void MoveDirectory(string sourceDirectoryName, string destinationDirectoryName, bool overwrite) =>
			vbcore::Microsoft.VisualBasic.FileIO.FileSystem.MoveDirectory(sourceDirectoryName, destinationDirectoryName, overwrite);
		public static void MoveFile(string sourceFileName, string destinationFileName) =>
			vbcore::Microsoft.VisualBasic.FileIO.FileSystem.MoveFile(sourceFileName, destinationFileName);
		public static void MoveFile(string sourceFileName, string destinationFileName, bool overwrite) =>
			vbcore::Microsoft.VisualBasic.FileIO.FileSystem.MoveFile(sourceFileName, destinationFileName, overwrite);
		public static vbcore::Microsoft.VisualBasic.FileIO.TextFieldParser OpenTextFieldParser(string file) =>
			vbcore::Microsoft.VisualBasic.FileIO.FileSystem.OpenTextFieldParser(file);
		public static vbcore::Microsoft.VisualBasic.FileIO.TextFieldParser OpenTextFieldParser(string file, params string[] delimiters) =>
			vbcore::Microsoft.VisualBasic.FileIO.FileSystem.OpenTextFieldParser(file, delimiters);
		public static vbcore::Microsoft.VisualBasic.FileIO.TextFieldParser OpenTextFieldParser(string file, params int[] fieldWidths) =>
			vbcore::Microsoft.VisualBasic.FileIO.FileSystem.OpenTextFieldParser(file, fieldWidths);
		public static System.IO.StreamReader OpenTextFileReader(string file) =>
			vbcore::Microsoft.VisualBasic.FileIO.FileSystem.OpenTextFileReader(file);
		public static System.IO.StreamReader OpenTextFileReader(string file, System.Text.Encoding encoding) =>
			vbcore::Microsoft.VisualBasic.FileIO.FileSystem.OpenTextFileReader(file, encoding);
		public static System.IO.StreamWriter OpenTextFileWriter(string file, bool append) =>
			vbcore::Microsoft.VisualBasic.FileIO.FileSystem.OpenTextFileWriter(file, append);
		public static System.IO.StreamWriter OpenTextFileWriter(string file, bool append, System.Text.Encoding encoding) =>
			vbcore::Microsoft.VisualBasic.FileIO.FileSystem.OpenTextFileWriter(file, append, encoding);
		public static byte[] ReadAllBytes(string file) =>
			vbcore::Microsoft.VisualBasic.FileIO.FileSystem.ReadAllBytes(file);
		public static string ReadAllText(string file) =>
			vbcore::Microsoft.VisualBasic.FileIO.FileSystem.ReadAllText(file);
		public static string ReadAllText(string file, System.Text.Encoding encoding) =>
			vbcore::Microsoft.VisualBasic.FileIO.FileSystem.ReadAllText(file, encoding);
		public static void RenameDirectory(string directory, string newName) =>
			vbcore::Microsoft.VisualBasic.FileIO.FileSystem.RenameDirectory(directory, newName);
		public static void RenameFile(string file, string newName) =>
			vbcore::Microsoft.VisualBasic.FileIO.FileSystem.RenameFile(file, newName);
		public static void WriteAllBytes(string file, byte[] data, bool append) =>
			vbcore::Microsoft.VisualBasic.FileIO.FileSystem.WriteAllBytes(file, data, append);
		public static void WriteAllText(string file, string text, bool append) =>
			vbcore::Microsoft.VisualBasic.FileIO.FileSystem.WriteAllText(file, text, append);
		public static void WriteAllText(string file, string text, bool append, System.Text.Encoding encoding) =>
			vbcore::Microsoft.VisualBasic.FileIO.FileSystem.WriteAllText(file, text, append, encoding);
	}
}
