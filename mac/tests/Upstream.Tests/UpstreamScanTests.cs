using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UpstreamScan;
using Xunit;

namespace Upstream.Tests
{
	public class UpstreamScanTests
	{
		private const string Manifest = @"{ ""version"": 1, ""entries"": [
  { ""original"": ""ComicRack.Engine/CacheManager.cs"", ""replacement"": ""mac/ComicRack.Engine.Core/Replacements/PassB/CacheManager.cs"", ""kind"": ""patched-copy"", ""pass"": ""B"" },
  { ""original"": ""cYo.Common/Win32/MemoryInfo.cs"", ""kind"": ""excluded"", ""pass"": ""both"" },
  { ""pattern"": ""ComicRack.Engine/Controls/**"", ""kind"": ""excluded"", ""pass"": ""both"" },
  { ""pattern"": ""ComicRack.Engine/Drawing/Thumb*Renderer.cs"", ""kind"": ""excluded"", ""pass"": ""both"" }
] }";

		private const string EngineCsprojOld = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <ItemGroup>
    <Reference Include=""System.ServiceModel"" />
    <PackageReference Include=""bblanchon.PDFium.Win32"">
      <Version>148.0.7749</Version>
    </PackageReference>
    <PackageReference Include=""CSJ2K"" Version=""3.0.0"" />
    <PackageReference Include=""SharpCompress"" Version=""0.48.0"" />
  </ItemGroup>
</Project>";

		private const string MacEngineSources = @"<Project>
  <ItemGroup>
    <PackageReference Include=""bblanchon.PDFium.macOS"" Version=""153.0.7999"" />
    <PackageReference Include=""CSJ2K"" Version=""3.0.0"" />
    <PackageReference Include=""SharpCompress"" Version=""0.48.0"" />
  </ItemGroup>
</Project>";

		/// <summary>Base commit of the synthetic upstream history; returns its hash.</summary>
		private static string Base(SyntheticRepo r, string macSources = MacEngineSources)
		{
			r.Write("mac/replacements.json", Manifest);
			r.Write("mac/ComicRack.Mac.slnx", "<Solution />");
			r.Write("mac/ComicRack.Engine.Core/Sources.props", macSources);
			r.Write("mac/readme.md", "mac\n");
			r.Write("cYo.Common/Keep.cs", "class Keep {}\n");
			r.Write("cYo.Common/Old.cs", "class Old {}\n");
			r.Write("cYo.Common/Win32/MemoryInfo.cs", "class MemoryInfo {}\n");
			r.Write("ComicRack.Engine/CacheManager.cs", "line1\nline2\nline3\n");
			r.Write("ComicRack.Engine/Controls/Ctl.cs", "class Ctl {}\n");
			r.Write("ComicRack.Engine/ComicRack.Engine.csproj", EngineCsprojOld);
			r.Write("cYo.Common.Windows/Forms/ItemViewConfig.cs", "class ItemViewConfig {}\n");
			r.Write("cYo.Common.Windows/Forms/Other.cs", "class Other {}\n");
			r.Write("ComicRack/MainForm.cs", "class MainForm {}\n");
			r.Write("ComicRack/ComicRack.csproj", "<Project />");
			r.Write("ComicRack/Output/Languages/en/Main.xml", "<a/>");
			r.Write(".github/workflows/nightly.yml", "a: 1\n");
			r.Write("LICENSE.txt", "licence\n");
			return r.Commit("base");
		}

		private static HashSet<string> Linked(params string[] extra)
		{
			var set = new HashSet<string>(StringComparer.Ordinal)
			{
				"cYo.Common/Keep.cs", "cYo.Common/Old.cs", "cYo.Common.Windows/Forms/ItemViewConfig.cs",
			};
			foreach (var e in extra) set.Add(e);
			return set;
		}

		private static FileEntry One(ScanResult r, string path, string status = null)
		{
			return r.Files.Single(f => f.Path == path && (status == null || f.Status == status));
		}

		[Fact]
		public void Every_changed_file_of_an_upstream_commit_lands_in_its_category()
		{
			using var r = new SyntheticRepo();
			string b = Base(r);

			r.Write("cYo.Common/New.cs", "class New {}\n");                         // new file in a linked folder
			r.Write("ComicRack.Engine/CacheManager.cs", "line1\nlineX\nline3\n");  // replaced original
			r.Write("cYo.Common/Win32/MemoryInfo.cs", "class MemoryInfo { int x; }\n"); // excluded file
			r.Write("ComicRack.Engine/Controls/Ctl2.cs", "class Ctl2 {}\n");        // new file in an excluded folder
			r.Write("cYo.Common.Windows/Forms/ItemViewConfig.cs", "class ItemViewConfig { int a; }\n"); // single linked UI file
			r.Write("cYo.Common.Windows/Forms/Other.cs", "class Other { int a; }\n");  // UI, not linked
			r.Write("ComicRack/Dialogs/NewDialog.cs", "class NewDialog {}\n");         // new UI file
			r.Write("ComicRack/Output/Languages/en/Main.xml", "<b/>");                 // payload
			r.Write(".github/workflows/nightly.yml", "a: 2\n");                        // CI
			r.Write("mac/readme.md", "changed by upstream? no, ours\n");               // ours
			r.Write("MACOS_NATIVE_PATH.md", "plan\n");                                 // ours (allowed file outside mac/)
			r.Write("LICENSE.txt", "licence 2\n");                                     // other
			r.Delete("cYo.Common/Old.cs");                                              // deleted linked file
			string e = r.Commit("upstream");

			var result = Scanner.Run(new ScanOptions { RepoDir = r.Dir, Range = b + ".." + e, CompileItems = Linked("cYo.Common/New.cs") });

			Assert.Equal(b, result.From);
			Assert.Equal(e, result.To);
			Assert.Equal(Categories.Linked, One(result, "cYo.Common/New.cs").Category);
			Assert.True(One(result, "cYo.Common/New.cs").Compiles);
			Assert.Equal(Categories.Linked, One(result, "cYo.Common/Old.cs").Category);
			Assert.Equal("D", One(result, "cYo.Common/Old.cs").Status);

			var cache = One(result, "ComicRack.Engine/CacheManager.cs");
			Assert.Equal(Categories.Replaced, cache.Category);
			Assert.Contains("patched-copy", cache.Detail);
			Assert.Contains("replacement-sync.sh ComicRack.Engine/CacheManager.cs", cache.Action);

			Assert.Equal(Categories.Excluded, One(result, "cYo.Common/Win32/MemoryInfo.cs").Category);
			var ctl2 = One(result, "ComicRack.Engine/Controls/Ctl2.cs");
			Assert.Equal(Categories.Excluded, ctl2.Category);
			Assert.Contains("ComicRack.Engine/Controls/**", ctl2.Action);

			Assert.Equal(Categories.Linked, One(result, "cYo.Common.Windows/Forms/ItemViewConfig.cs").Category);
			Assert.Equal(Categories.UiNotLinked, One(result, "cYo.Common.Windows/Forms/Other.cs").Category);
			Assert.Equal(Categories.UiNotLinked, One(result, "ComicRack/Dialogs/NewDialog.cs").Category);
			Assert.Equal(Categories.Payload, One(result, "ComicRack/Output/Languages/en/Main.xml").Category);
			Assert.Equal(Categories.BuildCi, One(result, ".github/workflows/nightly.yml").Category);
			Assert.Equal(Categories.Ours, One(result, "mac/readme.md").Category);
			Assert.Equal(Categories.Ours, One(result, "MACOS_NATIVE_PATH.md").Category);
			Assert.Equal(Categories.Other, One(result, "LICENSE.txt").Category);

			Assert.Equal(result.Files.Count, result.Counts.Values.Sum());
			Assert.Equal(2, result.Counts[Categories.Ours]);
		}

		[Fact]
		public void A_glob_file_is_picked_up_when_the_tree_is_at_the_range_end_and_is_reported_as_not_compiled_when_no_shadow_project_has_it()
		{
			using var r = new SyntheticRepo();
			string b = Base(r);
			r.Write("cYo.Common/Picked.cs", "class Picked {}\n");
			r.Write("cYo.Common/Orphan.cs", "class Orphan {}\n");  // in the tree, but no shadow project compiles it
			string e = r.Commit("upstream");

			var result = Scanner.Run(new ScanOptions { RepoDir = r.Dir, Range = b + ".." + e, CompileItems = Linked("cYo.Common/Picked.cs") });

			var picked = One(result, "cYo.Common/Picked.cs");
			Assert.Equal(Categories.Linked, picked.Category);
			Assert.True(picked.Compiles);
			var orphan = One(result, "cYo.Common/Orphan.cs");
			Assert.Equal(Categories.Other, orphan.Category);
			Assert.False(orphan.Compiles);
		}

		[Fact]
		public void A_new_source_that_is_not_in_the_working_tree_yet_is_predicted_from_the_glob()
		{
			using var r = new SyntheticRepo();
			string b = Base(r);
			r.Write("cYo.Common/Future.cs", "class Future {}\n");
			r.Write("ComicRack.Engine/Controls/Skipped.cs", "class Skipped {}\n");
			string e = r.Commit("upstream");
			r.Git("checkout", "-q", b); // the "macos" tree has not merged the range yet

			var result = Scanner.Run(new ScanOptions { RepoDir = r.Dir, Range = b + ".." + e, CompileItems = Linked() });

			var future = One(result, "cYo.Common/Future.cs");
			Assert.Equal(Categories.Linked, future.Category);
			Assert.True(future.Compiles);
			Assert.Equal(Categories.Excluded, One(result, "ComicRack.Engine/Controls/Skipped.cs").Category);
		}

		[Fact]
		public void A_renamed_replaced_original_is_reported_as_a_deletion_of_the_original()
		{
			using var r = new SyntheticRepo();
			string b = Base(r);
			r.Write("ComicRack.Engine/CacheManager.cs", "line1\nline2\nline3\nline4\nline5\nline6\n");
			r.Commit("grow");
			string b2 = r.Git("rev-parse", "HEAD").Trim();
			r.Move("ComicRack.Engine/CacheManager.cs", "ComicRack.Engine/Caching/CacheManager.cs");
			string e = r.Commit("upstream moves it");

			var result = Scanner.Run(new ScanOptions { RepoDir = r.Dir, Range = b2 + ".." + e, CompileItems = Linked() });

			var gone = One(result, "ComicRack.Engine/CacheManager.cs");
			Assert.Equal("D", gone.Status);
			Assert.Equal(Categories.Replaced, gone.Category);
			Assert.Equal("ComicRack.Engine/Caching/CacheManager.cs", gone.RenamedTo);
			var added = One(result, "ComicRack.Engine/Caching/CacheManager.cs");
			Assert.Equal("A", added.Status);
			Assert.Equal("ComicRack.Engine/CacheManager.cs", added.RenamedFrom);
		}

		[Fact]
		public void A_deleted_single_UI_file_that_a_sources_props_still_links_needs_an_action()
		{
			using var r = new SyntheticRepo();
			string b = Base(r, MacEngineSources.Replace("</Project>",
				@"<ItemGroup><Compile Include=""$(MSBuildThisFileDirectory)..\..\cYo.Common.Windows\Forms\ItemViewConfig.cs"" /></ItemGroup></Project>"));
			r.Delete("cYo.Common.Windows/Forms/ItemViewConfig.cs");
			string e = r.Commit("upstream deletes it");

			var result = Scanner.Run(new ScanOptions { RepoDir = r.Dir, Range = b + ".." + e, CompileItems = Linked() });

			var f = One(result, "cYo.Common.Windows/Forms/ItemViewConfig.cs");
			Assert.Equal(Categories.Linked, f.Category);
			Assert.NotNull(f.Action); // the tree still has the file (merge not done): it is a compile item

			// After the merge the file is gone from the tree and from the compile items.
			var merged = Scanner.Run(new ScanOptions { RepoDir = r.Dir, Range = b + ".." + e, CompileItems = new HashSet<string>() });
			var g = One(merged, "cYo.Common.Windows/Forms/ItemViewConfig.cs");
			Assert.Equal(Categories.Linked, g.Category);
			Assert.NotNull(g.Action);
		}

		[Fact]
		public void Project_file_changes_are_compared_with_the_mac_mirror()
		{
			using var r = new SyntheticRepo();
			string b = Base(r);
			r.Write("ComicRack.Engine/ComicRack.Engine.csproj", @"<Project Sdk=""Microsoft.NET.Sdk"">
  <ItemGroup>
    <Reference Include=""System.ServiceModel"" />
    <Reference Include=""System.Brand.New"" />
    <PackageReference Include=""bblanchon.PDFium.Win32"">
      <Version>153.0.7999</Version>
    </PackageReference>
    <PackageReference Include=""CSJ2K"" Version=""3.0.0"" />
    <PackageReference Include=""SharpCompress"" Version=""0.50.4"" />
    <PackageReference Include=""Brand.New.Package"" Version=""1.2.3"" />
  </ItemGroup>
</Project>");
			r.Write("ComicRack/ComicRack.csproj", "<Project><ItemGroup><PackageReference Include=\"UiOnly\" Version=\"1.0\" /></ItemGroup></Project>");
			string e = r.Commit("upstream");

			var result = Scanner.Run(new ScanOptions { RepoDir = r.Dir, Range = b + ".." + e, CompileItems = Linked() });

			var eng = One(result, "ComicRack.Engine/ComicRack.Engine.csproj");
			Assert.Equal(Categories.ProjectFiles, eng.Category);
			Assert.Contains("mac/ComicRack.Engine.Core/Sources.props", eng.Action);
			Assert.Contains("Brand.New.Package", eng.Action);
			Assert.Contains("System.Brand.New", eng.Action);

			PackageChange P(string id) => result.Packages.Single(p => p.Id == id && p.Project == "ComicRack.Engine/ComicRack.Engine.csproj");
			Assert.Equal("added", P("Brand.New.Package").Change);
			Assert.Equal("absent", P("Brand.New.Package").Mac);
			Assert.Equal("changed", P("SharpCompress").Change);
			Assert.Equal("different", P("SharpCompress").Mac); // mac pins 0.48.0, upstream 0.50.4
			Assert.Equal("replaced", P("bblanchon.PDFium.Win32").Mac);
			Assert.Equal("absent", P("System.Brand.New").Mac);
			Assert.DoesNotContain(result.Packages, p => p.Id == "CSJ2K"); // unchanged

			var ui = One(result, "ComicRack/ComicRack.csproj");
			Assert.Equal(Categories.ProjectFiles, ui.Category);
			Assert.Null(ui.Action);
			Assert.Equal("not-applicable", result.Packages.Single(p => p.Id == "UiOnly").Mac);
		}

		[Fact]
		public void A_range_with_only_our_files_lists_nothing_else()
		{
			using var r = new SyntheticRepo();
			string b = Base(r);
			r.Write("mac/new.cs", "x\n");
			r.Write("mac/tools/t.sh", "x\n");
			r.Write(".github/workflows/mac-ci.yml", "a: 1\n");
			string e = r.Commit("ours");

			var result = Scanner.Run(new ScanOptions { RepoDir = r.Dir, Range = b + ".." + e, CompileItems = Linked() });

			Assert.Equal(3, result.Files.Count);
			Assert.All(result.Files, f => Assert.Equal(Categories.Ours, f.Category));
			Assert.Empty(result.Packages);
		}

		[Fact]
		public void A_bad_range_is_an_argument_error()
		{
			using var r = new SyntheticRepo();
			Base(r);
			Assert.Throws<ArgumentException>(() => Scanner.Run(new ScanOptions { RepoDir = r.Dir, Range = "HEAD", CompileItems = Linked() }));
			Assert.Throws<InvalidOperationException>(() => Scanner.Run(new ScanOptions { RepoDir = r.Dir, Range = "nothere..HEAD", CompileItems = Linked() }));
		}

		[Fact]
		public void The_text_and_json_reports_name_every_category_and_the_range()
		{
			using var r = new SyntheticRepo();
			string b = Base(r);
			r.Write("cYo.Common/New.cs", "class New {}\n");
			string e = r.Commit("upstream");
			var result = Scanner.Run(new ScanOptions { RepoDir = r.Dir, Range = b + ".." + e, CompileItems = Linked("cYo.Common/New.cs") });

			string text = Report.ToText(result, false);
			foreach (var c in Categories.All)
				Assert.Contains("== " + c + ": ", text);
			Assert.Contains("cYo.Common/New.cs", text);
			Assert.Contains("[compiles: yes]", text);
			string json = Report.ToJson(result);
			Assert.Contains("\"Path\": \"cYo.Common/New.cs\"", json);
			Assert.Contains("\"Counts\"", json);
		}

		[Fact]
		public void Glob_patterns_follow_the_manifest_rules()
		{
			Assert.Matches(Manifest_.Rx("ComicRack.Engine/Drawing/Thumb*Renderer.cs"), "ComicRack.Engine/Drawing/ThumbTileRenderer.cs");
			Assert.DoesNotMatch(Manifest_.Rx("ComicRack.Engine/Drawing/Thumb*Renderer.cs"), "ComicRack.Engine/Drawing/Sub/ThumbTileRenderer.cs");
			Assert.Matches(Manifest_.Rx("a/**"), "a/b/c/d.cs");
			Assert.DoesNotMatch(Manifest_.Rx("a/**"), "ab/c.cs");
		}

		[Fact]
		public void The_msbuild_compile_items_of_this_repository_match_the_pass_B_rules()
		{
			string repo = AppContext.BaseDirectory;
			while (repo != null && !File.Exists(Path.Combine(repo, "mac", "ComicRack.Mac.slnx")))
				repo = Path.GetDirectoryName(repo);
			Assert.NotNull(repo);

			CompileItemSource.FromMsBuild(repo, out var compile, out var embedded);

			Assert.Contains("ComicRack.Engine/ComicBook.cs", compile);
			Assert.Contains("cYo.Common/Text/ExtendedStringComparer.cs", compile);
			Assert.Contains("cYo.Common.Windows/Forms/ItemViewConfig.cs", compile);   // single file linked by Engine Sources.props
			Assert.DoesNotContain("ComicRack.Engine/CacheManager.cs", compile);        // pass B replaces it
			Assert.DoesNotContain("ComicRack.Engine/Controls/ListStyles.cs", compile);
			Assert.DoesNotContain(compile, p => p.StartsWith("mac/", StringComparison.Ordinal));
			Assert.DoesNotContain(compile, p => p.Contains("/obj/"));
			Assert.NotEmpty(embedded);
			Assert.All(embedded, p => Assert.Contains(p, compile));
		}

		private static class Manifest_
		{
			public static string Rx(string glob) => UpstreamScan.Manifest.GlobToRegex(glob).ToString();
		}
	}
}
