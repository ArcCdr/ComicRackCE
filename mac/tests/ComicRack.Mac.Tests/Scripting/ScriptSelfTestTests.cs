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

		private static string RepoRoot()
		{
			string dir = AppContext.BaseDirectory;
			while (dir != null && !File.Exists(Path.Combine(dir, "ComicRack", "Config", "Settings.cs")))
			{
				dir = Path.GetDirectoryName(dir);
			}
			return dir ?? throw new InvalidOperationException("The repository root was not found above " + AppContext.BaseDirectory);
		}

		/// <summary>The probe of P4.8: the names of the originals resolve to the facades, in this process (the bundle runs the same file with <c>--script-selftest</c>).</summary>
		public static string IdentityProbe => Path.Combine(RepoRoot(), "mac", "scripting", "identity-probe.py");

		[Fact]
		public void The_identity_probe_and_the_unmodified_Sample_py_pass_with_the_names_of_the_originals()
		{
			string old = Environment.GetEnvironmentVariable(ScriptSelfTest.ScriptsDirVariable);
			try
			{
				Environment.SetEnvironmentVariable(ScriptSelfTest.ScriptsDirVariable, Path.Combine(RepoRoot(), "ComicRack", "Output", "Scripts"));
				StringWriter output = new StringWriter();
				int code = ScriptSelfTest.Run(IdentityProbe, output);
				string text = output.ToString();
				Assert.True(code == 0, text);
				Assert.DoesNotContain("FAIL", text);
				// Sample.py is read from the program's own folder, with its reference lines as they are
				Assert.Contains("PASS Sample.py compiles", text);
				Assert.Contains("PASS hook ParseComicPath", text);
				Assert.Contains("PASS derived_class: identity: class UIControl(Control) is defined", text);
				Assert.Contains("PASS hook GetBooksWith", text);
				Assert.Contains("PASS partial_names: identity: Form is System.Windows.Forms 10.0.0.0", text);
				Assert.Contains("PASS partial_names: identity: Bitmap is System.Drawing.Common 10.0.0.0", text);
				Assert.Contains("PASS exact_names: identity: Bitmap (AddReference) is System.Drawing.Common 10.0.0.0", text);
				Assert.Contains("PASS core_names: identity: ComicBook is ComicRack.Engine", text);
				Assert.Contains("PASS core_names: identity: FormUtility is cYo.Common.Windows", text);
				Assert.Contains("PASS visual_basic_file_system: identity: FileSystem is Microsoft.VisualBasic 10.0.1.0", text);
				Assert.Contains("PASS visual_basic_file_system: identity: DeleteFile with a UIOption deletes", text);
			}
			finally
			{
				Environment.SetEnvironmentVariable(ScriptSelfTest.ScriptsDirVariable, old);
			}
		}
	}
}
