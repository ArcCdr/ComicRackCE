using System;
using System.Diagnostics;
using System.IO;

namespace ComicRack.Cli.Fixtures
{
	/// <summary>Where the fixtures live and how much disk they may use. Tests substitute their own.</summary>
	public sealed class FixtureEnvironment
	{
		public const long DefaultBudgetBytes = 1536L * 1024 * 1024;
		public const long DefaultMinFreeBytes = 3L * 1024 * 1024 * 1024;

		/// <summary>The folder that the budget covers (mac/fixtures).</summary>
		public string FixturesRoot { get; set; }

		public long BudgetBytes { get; set; } = DefaultBudgetBytes;

		/// <summary>Free space that must remain on the volume after the estimated write.</summary>
		public long MinFreeBytes { get; set; } = DefaultMinFreeBytes;

		/// <summary>Prints "df -h ~" like the plan asks; tests turn it off.</summary>
		public bool PrintDiskFree { get; set; } = true;

		/// <summary>Command that creates 7z archives, or null. Looked up on PATH by default.</summary>
		public string SevenZip { get; set; }

		public static FixtureEnvironment Default()
		{
			return new FixtureEnvironment { FixturesRoot = FindFixturesRoot(), SevenZip = FindOnPath("7zz") };
		}

		private static string FindFixturesRoot()
		{
			foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
			{
				for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
				{
					if (File.Exists(Path.Combine(dir.FullName, "mac", "ComicRack.Mac.slnx")))
					{
						return Path.Combine(dir.FullName, "mac", "fixtures");
					}
				}
			}
			return Path.Combine(Directory.GetCurrentDirectory(), "mac", "fixtures");
		}

		public static string FindOnPath(string name)
		{
			string path = Environment.GetEnvironmentVariable("PATH") ?? "";
			foreach (string dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
			{
				string candidate = Path.Combine(dir, name);
				if (File.Exists(candidate))
				{
					return candidate;
				}
			}
			return null;
		}

		public static long DirectorySize(string dir)
		{
			if (!Directory.Exists(dir))
			{
				return 0;
			}
			long total = 0;
			foreach (string file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
			{
				try
				{
					total += new FileInfo(file).Length;
				}
				catch (IOException)
				{
				}
			}
			return total;
		}

		public static string DiskFreeReport()
		{
			try
			{
				var psi = new ProcessStartInfo("df", "-h \"" + Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\"")
				{
					RedirectStandardOutput = true,
					UseShellExecute = false,
				};
				using (var p = Process.Start(psi))
				{
					string text = p.StandardOutput.ReadToEnd();
					p.WaitForExit();
					return text.TrimEnd();
				}
			}
			catch (Exception ex)
			{
				return "df failed: " + ex.Message;
			}
		}
	}
}
