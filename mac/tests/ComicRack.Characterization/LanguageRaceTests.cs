using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Xunit;

namespace ComicRack.Characterization
{
	/// <summary>
	/// R3: the first evaluation of a smart list with a language matcher in a fresh process used to lose books (ComicBook.GetIsoCulture fills a static
	/// Dictionary without a lock; P0.7a saw 30 or 31 of 64 in 2 of 12 processes). MacHost warms the dictionary, so every process must get the golden count.
	/// </summary>
	public class LanguageRaceTests
	{
		private const string ListName = "Language ja or fr";
		private const int Processes = 10;

		[Fact]
		public void A_language_list_has_its_golden_count_in_every_fresh_process()
		{
			int expected = Goldens.Read("SmallDb.lists.txt").Split('\n')
				.Select(l => l.TrimStart().Split('\t'))
				.Where(f => f.Length > 3 && f[0] == ListName)
				.Select(f => int.Parse(f[2]))
				.Single();
			string dll = typeof(LanguageRaceTests).Assembly.Location;
			string database = Goldens.PathOf("SmallDb.xml");
			for (int i = 0; i < Processes; i++)
			{
				var info = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
				info.ArgumentList.Add(dll);
				info.ArgumentList.Add("--list-count");
				info.ArgumentList.Add(database);
				info.ArgumentList.Add(ListName);
				using (Process p = Process.Start(info))
				{
					string output = p.StandardOutput.ReadToEnd();
					string error = p.StandardError.ReadToEnd();
					Assert.True(p.WaitForExit(120000), "child process " + i + " timed out");
					Assert.True(p.ExitCode == 0, "child process " + i + " failed: " + error + output);
					Assert.Equal("COUNT " + expected, output.Trim());
				}
			}
		}
	}
}
