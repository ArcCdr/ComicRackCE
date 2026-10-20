using System;
using System.IO;
using System.Linq;
using Xunit;

namespace ComicRack.Mac.Tests.Scripting
{
	/// <summary>
	/// The hidden <c>--script-selftest FILE</c> option of the app (spike S4). The real run needs the patched IronPython.dll and the
	/// StdLib (see mac/reports/s4-ironpython.md), so these tests only cover the parsing and the checks that work with the stock engine.
	/// </summary>
	public class ScriptSelfTestTests
	{
		[Fact]
		public void The_option_takes_a_file_in_both_forms_and_runs_no_window()
		{
			AppOptions a = AppOptions.Parse(new[] { "--script-selftest", "probe.py" });
			AppOptions b = AppOptions.Parse(new[] { "--script-selftest=probe.py" });
			Assert.Null(a.Error);
			Assert.Null(b.Error);
			Assert.Equal(Path.GetFullPath("probe.py"), a.ScriptSelfTest);
			Assert.Equal(a.ScriptSelfTest, b.ScriptSelfTest);
			Assert.False(a.SmokeTest);
			Assert.Null(AppOptions.Parse(new string[0]).ScriptSelfTest);
		}

		[Fact]
		public void A_probe_script_and_the_expression_checks_run_with_the_stock_engine()
		{
			string dir = Path.Combine(Path.GetTempPath(), "crmac-selftest-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(dir);
			try
			{
				string probe = Path.Combine(dir, "probe.py");
				File.WriteAllText(probe, "#@Name  Probe\n#@Hook  Startup\ndef selftest():\n\tprint 'PASS hello'\n\tprint 'RES answer = ' + str(6 * 7)\n");
				StringWriter output = new StringWriter();
				int code = ScriptSelfTest.Run(probe, output);
				string text = output.ToString();
				Assert.True(code == 0, text);
				Assert.Contains("PASS CompileExpression", text);
				Assert.Contains("PASS ComicBookExpressionMatcher", text);
				Assert.Contains("PASS selftest: hello", text);
				Assert.Contains("INFO selftest: RES answer = 42", text);
				Assert.Contains("INFO result: all checks passed", text);
			}
			finally
			{
				Directory.Delete(dir, true);
			}
		}

		[Fact]
		public void A_missing_probe_file_is_a_failure()
		{
			StringWriter output = new StringWriter();
			Assert.Equal(1, ScriptSelfTest.Run(Path.Combine(Path.GetTempPath(), "crmac-no-such-probe.py"), output));
			Assert.Contains("FAIL probe file", output.ToString());
		}

		[Fact]
		public void A_failing_check_of_the_probe_script_gives_exit_code_1()
		{
			string dir = Path.Combine(Path.GetTempPath(), "crmac-selftest-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(dir);
			try
			{
				string probe = Path.Combine(dir, "probe.py");
				File.WriteAllText(probe, "#@Name  Probe\n#@Hook  Startup\ndef selftest():\n\tprint 'FAIL deliberate'\n");
				StringWriter output = new StringWriter();
				Assert.Equal(1, ScriptSelfTest.Run(probe, output));
				Assert.Contains("FAIL selftest: deliberate", output.ToString());
			}
			finally
			{
				Directory.Delete(dir, true);
			}
		}

		[Fact]
		public void The_self_test_is_a_developer_option_that_a_release_build_runs_only_with_the_variable()
		{
#if DEBUG
			Assert.True(ScriptSelfTest.IsEnabled);
#else
			string old = Environment.GetEnvironmentVariable(ScriptSelfTest.EnableVariable);
			try
			{
				Environment.SetEnvironmentVariable(ScriptSelfTest.EnableVariable, null);
				Assert.False(ScriptSelfTest.IsEnabled);
				Environment.SetEnvironmentVariable(ScriptSelfTest.EnableVariable, "1");
				Assert.True(ScriptSelfTest.IsEnabled);
			}
			finally
			{
				Environment.SetEnvironmentVariable(ScriptSelfTest.EnableVariable, old);
			}
#endif
			Assert.Equal("COMICRACK_ENABLE_SELFTEST", ScriptSelfTest.EnableVariable);
		}

		[Theory]
		[InlineData("Library/Application Support/ComicRack Community Edition/Scripts", true)]
		[InlineData("Library/Application Support/cYo/ComicRack/Scripts", true)]
		[InlineData("Library/Application Support/ComicRack CE (MVP)", true)]
		[InlineData("Library/Application Support/Other/Scripts", true)]
		[InlineData("Library/Caches/ComicRack CE (MVP)/x", true)]
		[InlineData("Library/Application Support/Other/Notes", false)]
		[InlineData("dev/ComicRackCE/mac/spikes/S4-ironpython", false)]
		public void The_real_scripts_and_data_folders_are_recognised(string relativeToHome, bool real)
		{
			string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
			Assert.Equal(real, ScriptSelfTest.IsRealScriptsFolder(Path.Combine(home, relativeToHome)));
		}

		[Fact]
		public void A_temporary_folder_is_not_a_real_scripts_folder()
		{
			Assert.False(ScriptSelfTest.IsRealScriptsFolder(Path.Combine(Path.GetTempPath(), "crmac-selftest-x")));
		}

		[Fact]
		public void The_self_test_refuses_to_run_scripts_from_the_users_scripts_folder()
		{
			string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
			string probe = Path.Combine(home, "Library", "Application Support", "ComicRack Community Edition", "Scripts", "probe.py");
			string old = Environment.GetEnvironmentVariable(ScriptSelfTest.ScriptsDirVariable);
			StringWriter output = new StringWriter();
			try
			{
				Environment.SetEnvironmentVariable(ScriptSelfTest.ScriptsDirVariable, null);
				// The file does not need to exist for the folder check, but Run looks for the file first: use the folder variable with a file that exists.
				string dir = Path.Combine(Path.GetTempPath(), "crmac-selftest-" + Guid.NewGuid().ToString("N"));
				Directory.CreateDirectory(dir);
				try
				{
					string real = Path.GetDirectoryName(probe);
					string file = Path.Combine(dir, "probe.py");
					File.WriteAllText(file, "#@Name  Probe\n#@Hook  Startup\ndef selftest():\n\tprint 'PASS never'\n");
					Environment.SetEnvironmentVariable(ScriptSelfTest.ScriptsDirVariable, real);
					Assert.Equal(1, ScriptSelfTest.Run(file, output));
					Assert.Contains("FAIL scripts folder: refused", output.ToString());
					Assert.DoesNotContain("PASS never", output.ToString());
					Assert.DoesNotContain("probe script", output.ToString());
				}
				finally
				{
					Directory.Delete(dir, true);
				}
			}
			finally
			{
				Environment.SetEnvironmentVariable(ScriptSelfTest.ScriptsDirVariable, old);
			}
		}

		[Fact]
		public void The_temporary_copy_of_the_scripts_is_deleted_after_the_run()
		{
			string temp = Path.GetTempPath();
			string[] before = Directory.GetDirectories(temp, "comicrack-s4-*");
			string dir = Path.Combine(temp, "crmac-selftest-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(dir);
			string old = Environment.GetEnvironmentVariable(ScriptSelfTest.RewriteRefsVariable);
			try
			{
				File.WriteAllText(Path.Combine(dir, "probe.py"), "#@Name  Probe\n#@Hook  Startup\ndef selftest():\n\tprint 'PASS hello'\n");
				File.WriteAllText(Path.Combine(dir, "Sample.py"), "#@Name  Sample\n#@Hook  Startup\ndef nothing():\n\tpass\n");
				File.WriteAllText(Path.Combine(dir, "Sample.xml"), "<x/>");
				Environment.SetEnvironmentVariable(ScriptSelfTest.RewriteRefsVariable, "1");
				StringWriter output = new StringWriter();
				ScriptSelfTest.Run(Path.Combine(dir, "probe.py"), output); // the Sample checks fail (too few commands); only the cleanup matters here
				Assert.Contains("run from a copy of the scripts folder", output.ToString());
				string[] after = Directory.GetDirectories(temp, "comicrack-s4-*");
				Assert.Empty(after.Except(before));
			}
			finally
			{
				Environment.SetEnvironmentVariable(ScriptSelfTest.RewriteRefsVariable, old);
				Directory.Delete(dir, true);
			}
		}
	}
}
