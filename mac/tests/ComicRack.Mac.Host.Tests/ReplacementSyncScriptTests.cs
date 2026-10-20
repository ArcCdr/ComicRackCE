using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace ComicRack.Mac.Host.Tests
{
	/// <summary>mac/tools/replacement-sync.sh against a throw-away git repository (the script finds its repository from its own location).</summary>
	public class ReplacementSyncScriptTests
	{
		private const string Original = "ComicRack.Engine/Thing.cs";
		private const string Replacement = "mac/ComicRack.Engine.Core/Replacements/PassB/Thing.cs";

		private static readonly string[] BaseLines = Enumerable.Range(1, 30).Select(i => "line " + i).ToArray();

		private sealed class Repo : IDisposable
		{
			private readonly TempFolder folder = new TempFolder();
			public string Path => folder.Path;

			public Repo()
			{
				Git("init", "-q");
				Directory.CreateDirectory(System.IO.Path.Combine(Path, "mac", "tools"));
				File.Copy(System.IO.Path.Combine(ReplacementManifest.RepoRoot(), "mac", "tools", "replacement-sync.sh"), System.IO.Path.Combine(Path, "mac", "tools", "replacement-sync.sh"));
				Write(Original, string.Join("\n", BaseLines) + "\n");
				// the replacement is the original with a header and its line 3 changed
				string[] patched = (string[])BaseLines.Clone();
				patched[2] = "line 3 patched";
				Write(Replacement, "// header\n" + string.Join("\n", patched) + "\n");
				string sha = ReplacementManifest.HashText(string.Join("\n", BaseLines) + "\n");
				string head = Commit("base");
				Write("mac/replacements.json", JsonSerializer.Serialize(new
				{
					version = 1,
					entries = new object[] { new { original = Original, replacement = Replacement, kind = "patched-copy", pass = "B", @base = head, sha256 = sha, maxChangedLines = 2, reason = "test" } },
				}, new JsonSerializerOptions { WriteIndented = true }));
				Commit("manifest");
			}

			public void Write(string relative, string text)
			{
				string full = System.IO.Path.Combine(Path, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));
				Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full));
				File.WriteAllText(full, text);
			}

			public string Read(string relative) => File.ReadAllText(System.IO.Path.Combine(Path, relative));

			public string Commit(string message)
			{
				Git("add", "-A");
				Git("-c", "user.name=test", "-c", "user.email=test@example.com", "-c", "commit.gpgsign=false", "commit", "-q", "-m", message);
				return Git("rev-parse", "HEAD").Output.Trim();
			}

			public (int Code, string Output) Git(params string[] args) => Run("git", args);

			public (int Code, string Output) Sync(params string[] args) => Run("bash", new[] { System.IO.Path.Combine(Path, "mac", "tools", "replacement-sync.sh") }.Concat(args).ToArray());

			private (int Code, string Output) Run(string file, string[] args)
			{
				var info = new ProcessStartInfo(file) { WorkingDirectory = Path, RedirectStandardOutput = true, RedirectStandardError = true };
				foreach (string a in args) info.ArgumentList.Add(a);
				info.Environment["GIT_CONFIG_GLOBAL"] = "/dev/null";
				info.Environment["GIT_CONFIG_SYSTEM"] = "/dev/null";
				info.Environment.Remove(ReplacementManifest.OverrideVariable);
				using Process p = Process.Start(info);
				string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
				p.WaitForExit();
				return (p.ExitCode, output);
			}

			public JsonElement Entry()
			{
				using JsonDocument doc = JsonDocument.Parse(Read("mac/replacements.json"));
				return doc.RootElement.GetProperty("entries")[0].Clone();
			}

			public void Dispose() => folder.Dispose();
		}

		[Fact]
		public void An_unchanged_original_is_reported_as_unchanged_since_base()
		{
			using var repo = new Repo();
			var (code, output) = repo.Sync(Original);
			Assert.Equal(0, code);
			Assert.Contains("unchanged since base", output);
		}

		[Fact]
		public void A_change_in_another_place_merges_cleanly_and_the_hash_is_recorded()
		{
			using var repo = new Repo();
			string[] changed = (string[])BaseLines.Clone();
			changed[19] = "line 20 changed upstream";
			repo.Write(Original, string.Join("\n", changed) + "\n");
			string head = repo.Commit("upstream change");

			var (dryCode, dryOutput) = repo.Sync(Original);
			Assert.Equal(1, dryCode);
			Assert.Contains("CHANGED since base", dryOutput);
			Assert.Contains("merges cleanly", dryOutput);
			Assert.DoesNotContain("line 20 changed upstream", repo.Read(Replacement)); // a dry run changes nothing

			var (code, output) = repo.Sync(Original, "--accept");
			Assert.True(code == 0, output);
			string merged = repo.Read(Replacement);
			Assert.Contains("line 3 patched", merged);
			Assert.Contains("line 20 changed upstream", merged);
			Assert.StartsWith("// header", merged);
			Assert.Equal(head, repo.Entry().GetProperty("base").GetString());
			Assert.Equal(ReplacementManifest.HashText(string.Join("\n", changed) + "\n"), repo.Entry().GetProperty("sha256").GetString());

			var (againCode, again) = repo.Sync(Original);
			Assert.Equal(0, againCode);
			Assert.Contains("unchanged since base", again);
		}

		[Fact]
		public void A_change_to_a_patched_line_is_a_conflict_and_changes_nothing()
		{
			using var repo = new Repo();
			string[] changed = (string[])BaseLines.Clone();
			changed[2] = "line 3 changed upstream";
			repo.Write(Original, string.Join("\n", changed) + "\n");
			repo.Commit("upstream change");
			string before = repo.Read(Replacement);
			string manifest = repo.Read("mac/replacements.json");

			var (code, output) = repo.Sync(Original, "--accept");
			Assert.Equal(1, code);
			Assert.Contains("conflict", output);
			Assert.Equal(before, repo.Read(Replacement));
			Assert.Equal(manifest, repo.Read("mac/replacements.json"));
			Assert.Contains("<<<<<<<", File.ReadAllText(Path.Combine(repo.Path, "mac", "artifacts", "replacement-sync", "Thing.cs.merged")));
		}

		[Fact]
		public void A_rewrite_shows_the_upstream_diff_and_is_recorded_after_the_port()
		{
			using var repo = new Repo();
			string manifest = repo.Read("mac/replacements.json").Replace("\"patched-copy\"", "\"rewrite\"");
			repo.Write("mac/replacements.json", manifest);
			repo.Commit("rewrite");
			repo.Write(Original, "changed\n");
			string head = repo.Commit("upstream change");

			var (code, output) = repo.Sync(Original);
			Assert.Equal(1, code);
			Assert.Contains("-line 1", output);
			Assert.Contains("+changed", output);

			var (acceptCode, accepted) = repo.Sync(Original, "--accept");
			Assert.True(acceptCode == 0, accepted);
			Assert.Equal(head, repo.Entry().GetProperty("base").GetString());
			Assert.Equal(0, repo.Sync(Original).Code);
		}

		[Fact]
		public void An_unknown_original_and_bad_arguments_exit_with_2()
		{
			using var repo = new Repo();
			Assert.Equal(2, repo.Sync("No/Such/File.cs").Code);
			Assert.Equal(2, repo.Sync("--bogus").Code);
			Assert.Equal(2, repo.Sync().Code);
		}
	}
}
