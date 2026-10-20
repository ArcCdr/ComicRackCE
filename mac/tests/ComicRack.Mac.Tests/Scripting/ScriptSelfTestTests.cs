using System;
using System.IO;
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
	}
}
