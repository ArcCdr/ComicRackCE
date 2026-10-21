using System.IO;
using ComicRack.Mac.Host.Platform;
using cYo.Projects.ComicRack.Engine.IO.Provider.Readers.Archive;
using Xunit;

namespace ComicRack.Mac.Host.Tests
{
	/// <summary>P2.4b: the pass B SevenZipEngine runs the bundled 7zz that the host hands it (the write itself is tested by crcli's InfoCommandTests).</summary>
	public class SevenZipEngineTests
	{
		[Fact]
		public void The_Windows_console_path_is_built_with_Path_Combine_not_backslashes()
		{
			string path = SevenZipEngine.ConsoleExe64;
			Assert.DoesNotContain('\\', path);
			Assert.EndsWith(Path.Combine("Resources", "x64", "7z.exe"), path);
		}

		[Fact]
		public void The_host_points_the_engine_at_the_located_7zz()
		{
			string located = ToolLocator.SevenZip;
			if (located == null)
			{
				return; // mac/native/fetch-7zz.sh has not been run on this machine
			}
			using (var temp = new TempFolder())
			using (var host = MacHost.Open(new MacHostOptions { Paths = temp.Paths }))
			{
				Assert.Equal(located, SevenZipEngine.PackExe);
				Assert.True(File.Exists(SevenZipEngine.PackExe));
			}
		}
	}
}
