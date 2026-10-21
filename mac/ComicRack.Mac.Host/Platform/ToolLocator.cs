using System;
using System.IO;

namespace ComicRack.Mac.Host.Platform
{
	/// <summary>
	/// Finds the command line tools that the app bundles. <c>7zz</c> (the official 7-Zip macOS console binary, DD10) lives next to the
	/// executable in the app bundle; in a development tree it is the copy that <c>mac/native/fetch-7zz.sh</c> puts into
	/// <c>mac/artifacts/native</c>. Nothing is looked up on PATH (a Homebrew copy is never used).
	/// </summary>
	public static class ToolLocator
	{
		public const string SevenZipFileName = "7zz";

		/// <summary>Full path of <c>7zz</c>, or null when it is not available.</summary>
		public static string SevenZip => FindSevenZip(AppContext.BaseDirectory, Directory.GetCurrentDirectory());

		/// <summary>
		/// 1. <c>7zz</c> in <paramref name="executableFolder"/>; 2. <c>mac/artifacts/native/7zz</c> of the development tree found by walking
		/// up from the executable folder, then from <paramref name="workingFolder"/>. Null when neither exists.
		/// </summary>
		public static string FindSevenZip(string executableFolder, string workingFolder = null)
		{
			return Find(SevenZipFileName, executableFolder, workingFolder);
		}

		private static string Find(string name, string executableFolder, string workingFolder)
		{
			if (!string.IsNullOrEmpty(executableFolder))
			{
				string next = Path.Combine(executableFolder, name);
				if (File.Exists(next))
				{
					return Path.GetFullPath(next);
				}
			}
			foreach (string start in new[] { executableFolder, workingFolder })
			{
				if (string.IsNullOrEmpty(start))
				{
					continue;
				}
				for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
				{
					if (File.Exists(Path.Combine(dir.FullName, "mac", "ComicRack.Mac.slnx")))
					{
						string dev = Path.Combine(dir.FullName, "mac", "artifacts", "native", name);
						if (File.Exists(dev))
						{
							return dev;
						}
						break;
					}
				}
			}
			return null;
		}
	}
}
