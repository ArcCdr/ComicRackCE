using System;
using System.Diagnostics;
using System.IO;
using Xunit;

namespace ComicRack.Cli.Tests
{
	/// <summary>
	/// mac/tools/ui-drive/ui-drive.sh (P0.6a): the checks that need no window server. Every run uses its own UI_DRIVE_DIR, so a real
	/// session of the owner or of another task is never read, and no GUI command is reached (the argument and session checks
	/// come first).
	/// </summary>
	public class UiDriveScriptTests : IDisposable
	{
		private readonly string temp = Path.Combine(Path.GetTempPath(), "ui-drive-test-" + Guid.NewGuid().ToString("N"));
		private readonly string script = FindScript();

		public UiDriveScriptTests()
		{
			Directory.CreateDirectory(temp);
		}

		public void Dispose()
		{
			try
			{
				Directory.Delete(temp, recursive: true);
			}
			catch (IOException)
			{
			}
		}

		private static string FindScript()
		{
			for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
			{
				string candidate = Path.Combine(dir.FullName, "mac", "tools", "ui-drive", "ui-drive.sh");
				if (File.Exists(candidate))
				{
					return candidate;
				}
			}
			throw new FileNotFoundException("mac/tools/ui-drive/ui-drive.sh not found above " + AppContext.BaseDirectory);
		}

		private (int code, string output, string error) Run(string file, params string[] args)
		{
			var psi = new ProcessStartInfo("/bin/bash") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
			psi.ArgumentList.Add(file);
			foreach (string a in args)
			{
				psi.ArgumentList.Add(a);
			}
			psi.Environment["UI_DRIVE_DIR"] = temp;
			psi.Environment.Remove("UI_DRIVE_REACTIVATE");
			using (var p = Process.Start(psi))
			{
				string o = p.StandardOutput.ReadToEnd();
				string e = p.StandardError.ReadToEnd();
				p.WaitForExit();
				return (p.ExitCode, o, e);
			}
		}

		[Fact]
		public void The_script_is_valid_bash()
		{
			var r = Run("-n", script);
			Assert.True(r.code == 0, r.error);
		}

		[Fact]
		public void Help_exits_0_and_an_unknown_command_or_no_command_exits_2()
		{
			var help = Run(script, "help");
			Assert.Equal(0, help.code);
			Assert.Contains("launch", help.output);
			Assert.Contains("choose-path", help.output);
			Assert.Equal(2, Run(script, "bogus").code);
			Assert.Equal(2, Run(script).code);
		}

		[Fact]
		public void Input_commands_without_a_session_exit_2_and_send_nothing()
		{
			foreach (string[] args in new[] { new[] { "key", "cmd+o" }, new[] { "keycode", "36" }, new[] { "type", "x" }, new[] { "click", "10", "10" }, new[] { "menu", "File", "Open" }, new[] { "choose-path", "/tmp" } })
			{
				var r = Run(script, args);
				Assert.True(r.code == 2, string.Join(" ", args) + " -> " + r.code + " " + r.error);
				Assert.Contains("no session", r.error);
			}
			Assert.False(File.Exists(Path.Combine(temp, "windowtool")), "no GUI helper should have been compiled for these checks");
		}

		[Fact]
		public void Bad_arguments_are_rejected_before_anything_else()
		{
			Assert.Equal(2, Run(script, "key").code);
			Assert.Equal(2, Run(script, "key", "hyper+a").code);
			Assert.Equal(2, Run(script, "key", "cmd+notakey").code);
			Assert.Equal(2, Run(script, "key", "cmd+code1x").code);
			Assert.Equal(2, Run(script, "keycode", "abc").code);
			Assert.Equal(2, Run(script, "click", "1").code);
			Assert.Equal(2, Run(script, "shot").code);
			Assert.Equal(2, Run(script, "shot", "../evil").code);
			Assert.Equal(2, Run(script, "probe", "x.png").code);
			Assert.Equal(2, Run(script, "launch", "--bogus").code);
			Assert.Equal(2, Run(script, "wait-window").code);
			Assert.Equal(2, Run(script, "expect-log").code);
		}

		[Fact]
		public void A_physical_key_code_is_a_valid_key_and_reaches_the_session_check()
		{
			// code<N> is accepted by the parser: the first failure is the missing session (exit 2 with "no session"), not "unknown key".
			var r = Run(script, "key", "cmd+code20");
			Assert.Equal(2, r.code);
			Assert.Contains("no session", r.error);
		}

		[Fact]
		public void Status_and_kill_without_a_session_are_harmless()
		{
			Assert.Contains("no session", Run(script, "status").output);
			Assert.Equal(0, Run(script, "kill").code);
		}
	}
}
