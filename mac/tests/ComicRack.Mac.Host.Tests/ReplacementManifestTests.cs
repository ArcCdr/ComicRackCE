using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace ComicRack.Mac.Host.Tests
{
	/// <summary>
	/// Drift guard for every original that the pass B build replaces, stubs or excludes (mac/replacements.json, full-port.md section 5.1).
	/// A failure of the hash test names the command that shows the upstream change and re-syncs the replacement.
	/// The manifest can be redirected with the environment variable COMICRACK_REPLACEMENT_MANIFEST (hash test only).
	/// </summary>
	public class ReplacementManifestTests
	{
		private static readonly string Root = ReplacementManifest.RepoRoot();

		private static List<ManifestEntry> Entries() => ReplacementManifest.Load(ReplacementManifest.ManifestPath(Root, allowOverride: false));

		[Fact]
		public void Every_original_still_has_the_recorded_hash()
		{
			List<ManifestEntry> entries = ReplacementManifest.Load(ReplacementManifest.ManifestPath(Root, allowOverride: true));
			Assert.NotEmpty(entries);
			List<string> failures = ReplacementManifest.HashFailures(Root, entries);
			Assert.True(failures.Count == 0, "Originals changed since the manifest was recorded (see mac/UPSTREAM-SYNC.md):\n  " + string.Join("\n  ", failures));
		}

		[Fact]
		public void Every_exclusion_of_a_shadow_project_has_an_entry()
		{
			var tracked = new HashSet<string>(Entries().Where(e => e.Key != null).Select(e => e.Key));
			List<string> missing = ReplacementManifest.SourceExclusions(Root).Where(x => !tracked.Contains(x)).ToList();
			Assert.True(missing.Count == 0,
				"Exclude= or Compile Remove= without an entry in mac/replacements.json (add kind excluded/stub/patched-copy/rewrite with the original's hash):\n  " + string.Join("\n  ", missing));
		}

		[Fact]
		public void Every_file_under_a_Replacements_folder_has_an_entry()
		{
			var covered = new HashSet<string>();
			foreach (ManifestEntry e in Entries())
			{
				if (e.Replacement != null) covered.Add(e.Replacement);
				foreach (string c in e.Companions) covered.Add(c);
			}
			List<string> missing = ReplacementManifest.ReplacementFiles(Root).Where(f => !covered.Contains(f)).ToList();
			Assert.True(missing.Count == 0, "Files under mac/*/Replacements/ without an entry (as replacement or companion) in mac/replacements.json:\n  " + string.Join("\n  ", missing));
		}

		[Fact]
		public void Every_entry_is_well_formed_and_points_at_something_that_exists()
		{
			var problems = new List<string>();
			foreach (ManifestEntry e in Entries())
			{
				string name = e.ToString();
				if (!ReplacementManifest.Kinds.Contains(e.Kind)) problems.Add(name + ": unknown kind '" + e.Kind + "'");
				if (!new[] { "A", "B", "both" }.Contains(e.Pass)) problems.Add(name + ": pass must be A, B or both");
				if (string.IsNullOrWhiteSpace(e.Reason)) problems.Add(name + ": no reason");
				if (e.Original != null && e.Pattern != null) problems.Add(name + ": both original and pattern");
				if (e.Kind == "excluded" && e.Key == null) problems.Add(name + ": an exclusion needs an original or a pattern");
				if ((e.Kind == "patched-copy" || e.Kind == "rewrite") && (e.Key == null || e.Replacement == null)) problems.Add(name + ": needs an original and a replacement");
				if (e.Kind == "patched-copy" && (e.Pattern != null || e.MaxChangedLines <= 0)) problems.Add(name + ": a patched copy needs a single original and maxChangedLines");
				if (e.Key != null)
				{
					if (!Regex.IsMatch(e.Base ?? "", "^[0-9a-f]{40}$")) problems.Add(name + ": base must be a 40 digit commit hash");
					if (!Regex.IsMatch(e.Sha256 ?? "", "^[0-9a-f]{64}$")) problems.Add(name + ": sha256 must be 64 hex digits");
					if (e.Original != null && !File.Exists(ReplacementManifest.ToPath(Root, e.Original))) problems.Add(name + ": the original does not exist");
					if (e.Pattern != null && ReplacementManifest.Expand(Root, e.Pattern).Count == 0) problems.Add(name + ": the pattern matches no file");
				}
				if (e.Replacement != null && !File.Exists(ReplacementManifest.ToPath(Root, e.Replacement))) problems.Add(name + ": the replacement does not exist: " + e.Replacement);
				foreach (string c in e.Companions)
				{
					if (!File.Exists(ReplacementManifest.ToPath(Root, c))) problems.Add(name + ": the companion does not exist: " + c);
				}
			}
			Assert.True(problems.Count == 0, string.Join("\n", problems));
		}

		[Fact]
		public void Patched_copies_stay_within_their_changed_line_budget()
		{
			List<ManifestEntry> copies = Entries().Where(e => e.Kind == "patched-copy").ToList();
			Assert.NotEmpty(copies);
			foreach (ManifestEntry e in copies)
			{
				string[] original = ReplacementManifest.BodyLines(ReplacementManifest.ToPath(Root, e.Original));
				string[] copy = ReplacementManifest.BodyLines(ReplacementManifest.ToPath(Root, e.Replacement));
				int changed = ReplacementManifest.ChangedLines(original, copy);
				Assert.True(changed <= e.MaxChangedLines,
					e.Replacement + " differs from " + e.Original + " by " + changed + " lines (budget " + e.MaxChangedLines + "); make it a rewrite or raise the budget with a reason");
			}
		}

		[Fact]
		public void ChangedLines_counts_removed_and_added_lines()
		{
			Assert.Equal(0, ReplacementManifest.ChangedLines(new[] { "a", "b" }, new[] { "a", "b" }));
			Assert.Equal(2, ReplacementManifest.ChangedLines(new[] { "a", "b", "c" }, new[] { "a", "x", "c" }));
			Assert.Equal(1, ReplacementManifest.ChangedLines(new[] { "a" }, new[] { "a", "b" }));
		}

		[Fact]
		public void A_changed_original_is_reported_with_the_command_that_re_syncs_it()
		{
			using var folder = new TempFolder();
			string file = Path.Combine(folder.Path, "Foo.cs");
			File.WriteAllText(file, "class Foo {}\r\n");
			var entry = new ManifestEntry
			{
				Original = file, Kind = "rewrite", Base = new string('a', 40), Sha256 = ReplacementManifest.HashText("class Foo {}\n"),
			};
			Assert.Empty(ReplacementManifest.HashFailures(folder.Path, new[] { entry })); // CRLF and a byte order mark do not count
			File.WriteAllText(file, "﻿class Foo {}\n");
			Assert.Empty(ReplacementManifest.HashFailures(folder.Path, new[] { entry }));
			File.WriteAllText(file, "class Foo {} \n"); // the mutation: one space
			List<string> failures = ReplacementManifest.HashFailures(folder.Path, new[] { entry });
			Assert.Single(failures);
			Assert.Contains("mac/tools/replacement-sync.sh " + file, failures[0]);
		}

		[Fact]
		public void A_new_file_in_an_excluded_folder_changes_the_hash_of_the_pattern()
		{
			using var folder = new TempFolder();
			Directory.CreateDirectory(Path.Combine(folder.Path, "Lib", "Controls", "Sub"));
			Directory.CreateDirectory(Path.Combine(folder.Path, "Lib", "Controls", "obj"));
			File.WriteAllText(Path.Combine(folder.Path, "Lib", "Controls", "A.cs"), "a");
			File.WriteAllText(Path.Combine(folder.Path, "Lib", "Controls", "Sub", "B.cs"), "b");
			File.WriteAllText(Path.Combine(folder.Path, "Lib", "Controls", "obj", "C.cs"), "ignored");
			File.WriteAllText(Path.Combine(folder.Path, "Lib", "Other.cs"), "other");
			var entry = new ManifestEntry { Pattern = "Lib/Controls/**", Kind = "excluded", Base = new string('a', 40) };
			Assert.Equal(new[] { "Lib/Controls/A.cs", "Lib/Controls/Sub/B.cs" }, ReplacementManifest.Expand(folder.Path, entry.Pattern));
			string before = ReplacementManifest.CurrentHash(folder.Path, entry);
			File.WriteAllText(Path.Combine(folder.Path, "Lib", "Controls", "New.cs"), "new");
			Assert.NotEqual(before, ReplacementManifest.CurrentHash(folder.Path, entry));
			Assert.Equal(new[] { "Lib/Thumb1Renderer.cs" }, MatchSingleLevel(folder.Path));
		}

		private static string[] MatchSingleLevel(string root)
		{
			File.WriteAllText(Path.Combine(root, "Lib", "Thumb1Renderer.cs"), "t");
			Directory.CreateDirectory(Path.Combine(root, "Lib", "Deep"));
			File.WriteAllText(Path.Combine(root, "Lib", "Deep", "Thumb2Renderer.cs"), "t");
			return ReplacementManifest.Expand(root, "Lib/Thumb*Renderer.cs").ToArray();
		}
	}
}
