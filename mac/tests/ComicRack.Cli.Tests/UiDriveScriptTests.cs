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
			foreach (string[] args in new[] { new[] { "key", "cmd+o" }, new[] { "keycode", "36" }, new[] { "type", "x" }, new[] { "click", "10", "10" }, new[] { "drag", "10", "10", "20", "20" }, new[] { "frame", "ComicRack", "0", "0", "800", "600" }, new[] { "menu", "File", "Open" }, new[] { "choose-path", "/tmp" } })
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
		public void Drag_and_frame_refuse_bad_arguments_before_a_session_is_looked_at()
		{
			// wrong number of points, not numbers
			Assert.Equal(2, Run(script, "drag", "1", "2", "3").code);
			Assert.Equal(2, Run(script, "drag", "1", "2", "3", "x").code);
			Assert.Equal(2, Run(script, "drag", "1", "2", "3", "4", "5").code);
			Assert.Equal(2, Run(script, "drag", "1", "2", "3", "4", "--bogus").code);
			Assert.Equal(2, Run(script, "drag", "1", "2", "3", "4", "--steps").code);
			Assert.Equal(2, Run(script, "drag", "1", "2", "3", "4", "--steps", "x").code);
			Assert.Equal(2, Run(script, "drag", "1", "2", "3", "4", "--hold-ms", "-5").code);
			// frame: five arguments, whole numbers
			Assert.Equal(2, Run(script, "frame").code);
			Assert.Equal(2, Run(script, "frame", "ComicRack", "0", "0", "800").code);
			Assert.Equal(2, Run(script, "frame", "ComicRack", "0", "0", "800", "6x0").code);
			Assert.Equal(2, Run(script, "frame", "ComicRack", "0", "0", "800", "6.5").code);
			Assert.Equal(2, Run(script, "frame", "ComicRack", "", "0", "800", "600").code);
			Assert.False(File.Exists(Path.Combine(temp, "windowtool")), "no GUI helper should have been compiled for these checks");
		}

		[Theory]
		[InlineData("--steps", "201")]
		[InlineData("--steps", "99999999999999999999")]
		[InlineData("--steps", "4294967296")]
		[InlineData("--hold-ms", "10001")]
		[InlineData("--hold-ms", "4294967296")]
		[InlineData("--hold-ms", "18446744073709551616")]
		[InlineData("--hold-ms", "00000000000000010001")]
		public void Drag_refuses_a_step_count_or_a_hold_time_over_the_cap(string option, string value)
		{
			// the cap is checked while the arguments are read, so even with a (missing) session the answer is the usage error and not "no session"
			var r = Run(script, "drag", "1", "2", "3", "4", option, value);
			Assert.Equal(2, r.code);
			Assert.Contains("too large", r.error);
		}

		[Theory]
		[InlineData("--steps", "200")]
		[InlineData("--steps", "1")]
		[InlineData("--hold-ms", "10000")]
		[InlineData("--hold-ms", "0")]
		public void Drag_accepts_the_limits_themselves_and_stops_at_the_missing_session(string option, string value)
		{
			var r = Run(script, "drag", "1", "2", "3", "4", option, value);
			Assert.Equal(2, r.code);
			Assert.Contains("no session", r.error);
		}

		[Fact]
		public void The_caps_of_the_script_and_of_the_helper_are_the_same_numbers()
		{
			string dir = Path.GetDirectoryName(script);
			string sh = File.ReadAllText(script);
			string swift = File.ReadAllText(Path.Combine(dir, "WindowTool.swift"));
			string shSteps = System.Text.RegularExpressions.Regex.Match(sh, @"DRAG_MAX_STEPS=(\d+)").Groups[1].Value;
			string shHold = System.Text.RegularExpressions.Regex.Match(sh, @"DRAG_MAX_HOLD_MS=(\d+)").Groups[1].Value;
			string swiftSteps = System.Text.RegularExpressions.Regex.Match(swift, @"let maxDragSteps = (\d+)").Groups[1].Value;
			string swiftHold = System.Text.RegularExpressions.Regex.Match(swift, @"let maxDragHoldMs = ([\d_]+)").Groups[1].Value.Replace("_", "");
			Assert.NotEqual("", shSteps);
			Assert.Equal(shSteps, swiftSteps);
			Assert.Equal(shHold, swiftHold);
			// the helper re-checks the application before every dragged event and before the release, and a signal ends the drag with the button released
			Assert.Contains("checkpoint(point)", swift);
			Assert.Contains("SIGTERM", swift);
			Assert.Contains("SIGINT", swift);
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
