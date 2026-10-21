using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Xunit;

namespace ComicRack.Cli.Tests
{
	/// <summary>
	/// mac/tools/e2e.sh, mac/tests/e2e/lib.sh and the cases (P0.6b): checks that need no window server. A case is never run here.
	/// </summary>
	public class E2eScriptTests
	{
		private static readonly string Repo = FindRepo();

		private static string FindRepo()
		{
			for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
			{
				if (File.Exists(Path.Combine(dir.FullName, "mac", "tools", "e2e.sh")))
				{
					return dir.FullName;
				}
			}
			throw new FileNotFoundException("mac/tools/e2e.sh not found above " + AppContext.BaseDirectory);
		}

		private static (int code, string output, string error) Bash(params string[] args)
		{
			var psi = new ProcessStartInfo("/bin/bash") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
			foreach (string a in args)
			{
				psi.ArgumentList.Add(a);
			}
			using (var p = Process.Start(psi))
			{
				string o = p.StandardOutput.ReadToEnd();
				string e = p.StandardError.ReadToEnd();
				p.WaitForExit();
				return (p.ExitCode, o, e);
			}
		}

		private static string[] CaseFiles() => Directory.GetFiles(Path.Combine(Repo, "mac", "tests", "e2e", "cases"), "*.sh");

		[Fact]
		public void The_runner_the_library_and_every_case_are_valid_bash()
		{
			var files = CaseFiles().Concat(new[] { Path.Combine(Repo, "mac", "tools", "e2e.sh"), Path.Combine(Repo, "mac", "tests", "e2e", "lib.sh") }).ToArray();
			Assert.True(files.Length >= 12);
			foreach (string f in files)
			{
				var r = Bash("-n", f);
				Assert.True(r.code == 0, f + ": " + r.error);
			}
		}

		[Fact]
		public void The_suite_has_exactly_the_listed_cases_so_a_new_case_must_be_listed_here()
		{
			string[] expected =
			{
				"book-tabs", "chrome", "chrome-gallery", "context-menu-1", "fullscreen", "grid-keys", "grid-look", "grid-marquee", "grid-multiselect", "hidpi", "import", "info-edit", "info-pages", "info-panel", "launch-quit", "multi-edit",
				"menu-structure", "open-folder", "overlay", "pdf-cb7", "quit-during-scan", "reader-keys", "reader-look", "refresh-after-read", "small-preview", "state-restore",
			};
			string[] names = CaseFiles().Select(f => Path.GetFileNameWithoutExtension(f)).OrderBy(n => n, StringComparer.Ordinal).ToArray();
			Assert.Equal(expected.OrderBy(n => n, StringComparer.Ordinal).ToArray(), names);
		}

		[Fact]
		public void Every_case_begins_and_ends_through_the_library_and_never_drives_the_system_directly()
		{
			foreach (string f in CaseFiles())
			{
				string text = File.ReadAllText(f);
				Assert.Contains("e2e_begin", text);
				Assert.Contains("e2e_end", text);
				// Input only through ui-drive (it refuses to type into other apps): no direct System Events, cliclick or osascript in a case.
				Assert.DoesNotContain("osascript", text);
				Assert.DoesNotContain("System Events", text);
				Assert.DoesNotContain("cliclick", text);
				// Never the owner's folders.
				Assert.DoesNotContain("Library/Application Support", text);
			}
		}

		[Fact]
		public void The_cleanup_of_a_case_ends_background_commands_and_their_children()
		{
			// a drag that holds the mouse button runs in the background (bg_run); a failed or interrupted case must not leave it running
			string lib = Path.Combine(Repo, "mac", "tests", "e2e", "lib.sh");
			string script = "source '" + lib + "'; E2E_CASE=cleanup-test; "
				+ "bg_run bash -c 'sleep 120; true'; top=$LAST_BG; sleep 0.5; child=$(pgrep -P $top); "
				+ "[ -n \"$child\" ] || { echo no-child; exit 3; }; "
				+ "e2e_stop_background; "
				+ "kill -0 $top 2>/dev/null && echo top-alive || echo top-gone; kill -0 $child 2>/dev/null && echo child-alive || echo child-gone";
			var r = Bash("-c", script);
			Assert.True(r.code == 0, r.output + r.error);
			Assert.Contains("top-gone", r.output);
			Assert.Contains("child-gone", r.output);
		}

		[Fact]
		public void The_marquee_case_holds_its_drag_through_the_cleanup_of_the_library()
		{
			string text = File.ReadAllText(Path.Combine(Repo, "mac", "tests", "e2e", "cases", "grid-marquee.sh"));
			Assert.Contains("bg_run", text);
			Assert.DoesNotContain(") &", text); // no unmanaged background subshell
		}

		[Fact]
		public void Unknown_options_and_unknown_cases_exit_2_before_anything_starts()
		{
			string script = Path.Combine(Repo, "mac", "tools", "e2e.sh");
			Assert.Equal(2, Bash(script, "--bogus").code);
			var r = Bash(script, "no-such-case");
			Assert.Equal(2, r.code);
			Assert.Contains("no such case", r.error);
		}
	}
}
