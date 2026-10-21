using System.Diagnostics;
using System.IO;
using ComicRack.Mac.Host.Platform;
using Xunit;

namespace ComicRack.Mac.Host.Tests
{
	/// <summary>P2.4a: where the bundled 7zz is found (next to the executable, else the development tree), and that the fetched binary is the official one.</summary>
	public class ToolLocatorTests
	{
		private static void Touch(string file)
		{
			Directory.CreateDirectory(Path.GetDirectoryName(file));
			File.WriteAllText(file, "x");
		}

		[Fact]
		public void The_copy_next_to_the_executable_wins_over_the_development_tree()
		{
			using (var temp = new TempFolder())
			{
				string exe = Path.Combine(temp.Path, "mac", "ComicRack.Cli", "bin");
				Touch(Path.Combine(temp.Path, "mac", "ComicRack.Mac.slnx"));
				Touch(Path.Combine(temp.Path, "mac", "artifacts", "native", "7zz"));
				Touch(Path.Combine(exe, "7zz"));
				Assert.Equal(Path.Combine(exe, "7zz"), ToolLocator.FindSevenZip(exe));
			}
		}

		[Fact]
		public void Without_a_copy_next_to_the_executable_the_development_tree_is_used_from_either_start_folder()
		{
			using (var temp = new TempFolder())
			{
				Touch(Path.Combine(temp.Path, "mac", "ComicRack.Mac.slnx"));
				string dev = Path.Combine(temp.Path, "mac", "artifacts", "native", "7zz");
				Touch(dev);
				string exe = Path.Combine(temp.Path, "mac", "ComicRack.Cli", "bin", "Debug");
				Directory.CreateDirectory(exe);
				Assert.Equal(dev, ToolLocator.FindSevenZip(exe));
				using (var elsewhere = new TempFolder())
				{
					Assert.Equal(dev, ToolLocator.FindSevenZip(elsewhere.Path, exe));
				}
			}
		}

		[Fact]
		public void Nothing_is_found_when_neither_place_has_it_and_PATH_is_not_searched()
		{
			using (var temp = new TempFolder())
			{
				Touch(Path.Combine(temp.Path, "mac", "ComicRack.Mac.slnx"));
				Assert.Null(ToolLocator.FindSevenZip(Path.Combine(temp.Path, "mac"), temp.Path));
				using (var bare = new TempFolder())
				{
					Assert.Null(ToolLocator.FindSevenZip(bare.Path, bare.Path));
				}
			}
		}

		[Fact]
		public void The_fetched_7zz_runs_and_reports_the_official_banner()
		{
			string sevenZip = ToolLocator.SevenZip;
			if (sevenZip == null)
			{
				// Needs the network once: mac/native/fetch-7zz.sh. Everything above runs without it.
				return;
			}
			var psi = new ProcessStartInfo(sevenZip, "i") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
			using (var p = Process.Start(psi))
			{
				string text = p.StandardOutput.ReadToEnd();
				p.StandardError.ReadToEnd();
				p.WaitForExit();
				Assert.Equal(0, p.ExitCode);
				Assert.StartsWith("7-Zip (z)", text.TrimStart());
				Assert.Contains("Igor Pavlov", text);
			}
		}
	}
}
