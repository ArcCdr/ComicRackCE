using System;
using System.Diagnostics;
using System.IO;
using Xunit;

namespace Upstream.Tests
{
	/// <summary>
	/// mac/tools/upstream-sync-check.sh up to the point where it would start a build: arguments and preconditions, run against a
	/// throw-away git repository (the script finds its repository from its own location). The full run is the rehearsal of P0.5c.
	/// </summary>
	public class SyncCheckScriptTests
	{
		private const string Script = "mac/tools/upstream-sync-check.sh";

		private static string RepoRoot()
		{
			string dir = AppContext.BaseDirectory;
			while (dir != null && !File.Exists(Path.Combine(dir, "mac", "ComicRack.Mac.slnx")))
				dir = Path.GetDirectoryName(dir);
			return dir ?? throw new InvalidOperationException("cannot find mac/ComicRack.Mac.slnx above " + AppContext.BaseDirectory);
		}

		/// <summary>A synthetic repository on branch macos with the script, a state file and a fake upstream/master one commit ahead.</summary>
		private sealed class Fixture : IDisposable
		{
			public SyntheticRepo Repo { get; } = new SyntheticRepo();
			public string Base { get; }
			public string Tip { get; }

			public Fixture()
			{
				string target = Path.Combine(Repo.Dir, "mac", "tools");
				Directory.CreateDirectory(target);
				File.Copy(Path.Combine(RepoRoot(), Script), Path.Combine(Repo.Dir, Script));
				Repo.Write("mac/ComicRack.Mac.slnx", "<Solution />");
				Repo.Write("ComicRack.Engine/Thing.cs", "class Thing {}\n");
				Base = Repo.Commit("base");
				Repo.Write("mac/upstream-sync.json", "{ \"lastSyncedUpstream\": \"" + Base + "\", \"date\": \"2026-10-20\", \"reportDigest\": null }\n");
				Repo.Commit("state");
				// upstream: one more commit on a side branch, published as upstream/master but not merged
				Repo.Git("checkout", "-q", "-b", "side", Base);
				Repo.Write("ComicRack.Engine/Thing.cs", "class Thing { int x; }\n");
				Tip = Repo.Commit("upstream change");
				Repo.Git("update-ref", "refs/remotes/upstream/master", Tip);
				Repo.Git("checkout", "-q", "macos");
			}

			public (int Code, string Output) Run(params string[] args)
			{
				var psi = new ProcessStartInfo("bash")
				{
					WorkingDirectory = Repo.Dir,
					RedirectStandardOutput = true,
					RedirectStandardError = true,
					UseShellExecute = false,
				};
				psi.ArgumentList.Add(Script);
				foreach (var a in args)
					psi.ArgumentList.Add(a);
				using var p = Process.Start(psi);
				var err = p.StandardError.ReadToEndAsync();
				string output = p.StandardOutput.ReadToEnd();
				p.WaitForExit();
				return (p.ExitCode, output + err.Result);
			}

			public void Dispose() => Repo.Dispose();
		}

		[Fact]
		public void The_script_has_valid_bash_syntax()
		{
			var psi = new ProcessStartInfo("bash") { RedirectStandardError = true, UseShellExecute = false };
			psi.ArgumentList.Add("-n");
			psi.ArgumentList.Add(Path.Combine(RepoRoot(), Script));
			using var p = Process.Start(psi);
			string err = p.StandardError.ReadToEnd();
			p.WaitForExit();
			Assert.True(p.ExitCode == 0, err);
		}

		[Fact]
		public void Help_exits_0_and_lists_the_options()
		{
			using var f = new Fixture();
			var (code, output) = f.Run("--help");
			Assert.Equal(0, code);
			Assert.Contains("--range A..B", output);
			Assert.Contains("--no-merge", output);
			Assert.Contains("--record", output);
		}

		[Theory]
		[InlineData("--frobnicate")]
		[InlineData("--range")]
		[InlineData("--range", "abc")]
		[InlineData("--range", "a...b")]
		[InlineData("--record", "--no-merge")]
		[InlineData("--record", "--skip-verify")]
		public void Bad_arguments_exit_2_before_anything_is_checked(params string[] args)
		{
			using var f = new Fixture();
			var (code, output) = f.Run(args);
			Assert.True(code == 2, output);
			Assert.DoesNotContain("PRECONDITION", output);
		}

		[Fact]
		public void Another_branch_is_a_failed_precondition()
		{
			using var f = new Fixture();
			f.Repo.Git("checkout", "-q", "-b", "other");
			var (code, output) = f.Run("--no-merge");
			Assert.True(code == 3, output);
			Assert.Contains("not macos", output);
			Assert.Contains("EXIT: 3", output);
		}

		[Fact]
		public void Uncommitted_changes_are_a_failed_precondition_but_DS_Store_and_artifacts_are_not()
		{
			using var f = new Fixture();
			f.Repo.Write("mac/artifacts/upstream-sync/old.txt", "x");
			f.Repo.Write(".DS_Store", "x");
			f.Repo.Write("mac/.DS_Store", "x");
			// without upstream/master the run stops right after the tree check, so nothing heavy starts
			f.Repo.Git("update-ref", "-d", "refs/remotes/upstream/master");
			var (code, output) = f.Run("--no-merge");
			Assert.True(code == 3, output);
			Assert.DoesNotContain("uncommitted", output);
			Assert.Contains("upstream/master does not exist", output);

			f.Repo.Write("ComicRack.Engine/Thing.cs", "class Thing { int dirty; }\n");
			(code, output) = f.Run("--no-merge");
			Assert.True(code == 3, output);
			Assert.Contains("uncommitted changes", output);
			Assert.Contains("ComicRack.Engine/Thing.cs", output);
		}

		[Fact]
		public void A_merge_in_progress_is_a_failed_precondition()
		{
			using var f = new Fixture();
			File.WriteAllText(Path.Combine(f.Repo.Dir, ".git", "MERGE_HEAD"), f.Tip + "\n");
			var (code, output) = f.Run("--no-merge");
			Assert.True(code == 3, output);
			Assert.Contains("a merge is in progress", output);
		}

		[Fact]
		public void A_missing_or_wrong_state_file_is_a_failed_precondition()
		{
			using var f = new Fixture();
			f.Repo.Write("mac/upstream-sync.json", "{ \"lastSyncedUpstream\": \"0000000000000000000000000000000000000001\" }\n");
			f.Repo.Commit("wrong state");
			var (code, output) = f.Run("--no-merge");
			Assert.True(code == 3, output);
			Assert.Contains("lastSyncedUpstream", output);

			f.Repo.Delete("mac/upstream-sync.json");
			f.Repo.Commit("no state");
			(code, output) = f.Run("--no-merge");
			Assert.True(code == 3, output);
			Assert.Contains("mac/upstream-sync.json is missing", output);
		}

		[Fact]
		public void Without_no_merge_an_unmerged_upstream_is_a_failed_precondition()
		{
			using var f = new Fixture();
			var (code, output) = f.Run();
			Assert.True(code == 3, output);
			Assert.Contains("is not merged into HEAD", output);
			Assert.Contains("git merge --no-ff upstream/master", output);
		}

		[Fact]
		public void Without_upstream_master_the_precondition_names_git_fetch()
		{
			using var f = new Fixture();
			f.Repo.Git("update-ref", "-d", "refs/remotes/upstream/master");
			var (code, output) = f.Run("--no-merge");
			Assert.True(code == 3, output);
			Assert.Contains("git fetch upstream", output);
		}
	}
}
