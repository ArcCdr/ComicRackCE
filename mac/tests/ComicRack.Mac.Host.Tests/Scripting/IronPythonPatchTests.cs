using System;
using System.IO;
using System.Linq;
using ComicRack.Mac.Host.Scripting;
using cYo.Projects.ComicRack.Plugins;
using Xunit;

namespace ComicRack.Mac.Host.Tests.Scripting
{
	/// <summary>
	/// IronPython 2.7.12 cannot create a Python class on .NET 9 and later; <c>mac/tools/patch-ironpython</c> fixes the copy next to every executable (build step,
	/// <c>mac/Directory.Build.targets</c>). These tests run a script with new-style classes (they fail when the patch did not run) and test the tool itself.
	/// </summary>
	public class IronPythonPatchTests
	{
		private static string StockIronPython()
		{
			string root = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
			if (string.IsNullOrEmpty(root))
			{
				root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
			}
			return Path.Combine(root, "ironpython", "2.7.12", "lib", "netstandard2.0", "IronPython.dll");
		}

		[Fact]
		public void A_script_can_create_new_style_python_classes()
		{
			using (var temp = new TempFolder())
			{
				string dir = ScriptTestSupport.WriteScripts(temp, "scripts", ("classes.py", @"
import os, re, datetime

class Counter(object):
	def __init__(self):
		self.n = 0
	def add(self, k):
		self.n += k
		return self.n

class Doubler(Counter):
	def add(self, k):
		return Counter.add(self, k * 2)

#@Name Classes
#@Hook Books
def Classes(books):
	c = Doubler()
	for b in books:
		c.add(1)
	print 'counted', c.n, os.path.basename('/a/b.txt'), re.sub('a', 'b', 'banana'), datetime.date(2020, 1, 2).year
"));
				using (MacHost host = ScriptTestSupport.Open(temp, scriptFolders: dir))
				{
					ScriptTestSupport.Add(host, ScriptTestSupport.Book("A", "1"), ScriptTestSupport.Book("A", "2"), ScriptTestSupport.Book("A", "3"));
					var output = new StringWriter();
					var error = new StringWriter();
					int code = ScriptRunner.Run(host, Path.Combine(dir, "classes.py"), "Books", null, output, error);
					Assert.True(code == 0, output + error.ToString());
					Assert.Contains("counted 6 b.txt bbnbnb 2020", output.ToString());
				}
			}
		}

		[Fact]
		public void The_copy_of_IronPython_next_to_the_tests_is_patched()
		{
			string loaded = typeof(PythonCommand).Assembly.Location;
			string ironPython = Path.Combine(Path.GetDirectoryName(loaded), "IronPython.dll");
			Assert.True(File.Exists(ironPython), ironPython);
			Assert.True(PatchIronPython.Program.IsPatched(ironPython), "the build step (mac/Directory.Build.targets) did not patch " + ironPython);
		}

		[Fact]
		public void The_tool_patches_a_stock_file_once_and_leaves_a_patched_one_alone()
		{
			string stock = StockIronPython();
			Assert.True(File.Exists(stock), "the IronPython package is not at " + stock);
			using (var temp = new TempFolder())
			{
				string here = Path.GetDirectoryName(typeof(PythonCommand).Assembly.Location);
				string dir = Path.Combine(temp.Path, "ip");
				Directory.CreateDirectory(dir);
				foreach (string neighbour in new[] { "Microsoft.Dynamic.dll", "Microsoft.Scripting.dll" })
				{
					File.Copy(Path.Combine(here, neighbour), Path.Combine(dir, neighbour));
				}
				string copy = Path.Combine(dir, "IronPython.dll");
				File.Copy(stock, copy);
				Assert.False(PatchIronPython.Program.IsPatched(copy), "the package file is the stock file");

				Assert.Equal(0, PatchIronPython.Program.Patch(copy, copy));
				Assert.True(PatchIronPython.Program.IsPatched(copy));
				byte[] once = File.ReadAllBytes(copy);
				Assert.Equal(0, PatchIronPython.Program.Patch(copy, copy));
				Assert.Equal(once, File.ReadAllBytes(copy));

				string other = Path.Combine(dir, "Other.dll");
				Assert.Equal(0, PatchIronPython.Program.Patch(copy, other));
				Assert.Equal(once, File.ReadAllBytes(other));
				Assert.Empty(Directory.GetFiles(dir, "*.patching"));
			}
		}

		[Fact]
		public void The_tool_refuses_a_file_that_is_not_IronPython()
		{
			using (var temp = new TempFolder())
			{
				string other = Path.Combine(temp.Path, "Other.dll");
				File.Copy(typeof(ScriptHost).Assembly.Location, other);
				Assert.Equal(2, PatchIronPython.Program.Main(new[] { other }));
				Assert.Equal(2, PatchIronPython.Program.Main(new string[0]));
			}
		}
	}
}
