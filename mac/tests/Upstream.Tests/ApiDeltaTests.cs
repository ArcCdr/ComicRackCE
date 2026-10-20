using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ApiInventory;
using Xunit;

namespace Upstream.Tests
{
	/// <summary>Platform API delta (P0.5b): the committed baseline against modified copies of itself, with the real shims for the coverage answer.</summary>
	public class ApiDeltaTests : IDisposable
	{
		private readonly string macDir = FindMac();
		private readonly ApiSet current;
		private readonly Lazy<ShimCoverage> coverage;

		public ApiDeltaTests()
		{
			current = ApiSet.Load(Path.Combine(macDir, "reports", "api-baseline.json"));
			coverage = new Lazy<ShimCoverage>(() =>
			{
				var (d, f) = ShimCoverage.Locate(macDir);
				return new ShimCoverage(d, f);
			});
		}

		public void Dispose()
		{
			if (coverage.IsValueCreated)
				coverage.Value.Dispose();
		}

		private static string FindMac()
		{
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			while (dir != null && !File.Exists(Path.Combine(dir.FullName, "mac", "ComicRack.Mac.slnx")))
				dir = dir.Parent;
			Assert.NotNull(dir);
			return Path.Combine(dir.FullName, "mac");
		}

		/// <summary>A copy of the committed baseline, so a test can remove entries from it.</summary>
		private ApiSet Copy() => ApiSet.FromJson(current.ToJson());

		[Fact]
		public void The_committed_baseline_is_sorted_and_has_the_expected_shape()
		{
			Assert.True(current.Types.Count > 50);
			Assert.True(current.Members.Count > 200);
			Assert.True(current.DllImports.Count > 50);
			Assert.Equal(current.Types.Select(t => t.Key).OrderBy(k => k, StringComparer.Ordinal), current.Types.Select(t => t.Key));
			Assert.Equal(current.Members.Select(m => m.Key).OrderBy(k => k, StringComparer.Ordinal), current.Members.Select(m => m.Key));
			Assert.Equal(current.DllImports.Select(p => p.Key).OrderBy(k => k, StringComparer.Ordinal), current.DllImports.Select(p => p.Key));
			// No call-site data, only identities.
			string text = File.ReadAllText(Path.Combine(macDir, "reports", "api-baseline.json"));
			Assert.DoesNotContain("\"files\"", text);
			Assert.DoesNotContain("\"calls\"", text, StringComparison.OrdinalIgnoreCase);
			Assert.DoesNotContain("\r", text);
		}

		[Fact]
		public void Writing_and_reading_the_baseline_gives_the_same_text()
		{
			string text = File.ReadAllText(Path.Combine(macDir, "reports", "api-baseline.json"));
			Assert.Equal(text, current.ToJson());
			var again = ApiSet.FromJson(text);
			again.Sort();
			Assert.Equal(text, again.ToJson());
		}

		[Fact]
		public void The_baseline_against_itself_has_no_new_platform_API()
		{
			var delta = ApiSet.Compare(Copy(), current);
			Assert.True(delta.IsEmpty);
			Assert.Equal("no new platform API\n", delta.Render("api-baseline.json").Replace("\r\n", "\n"));
		}

		[Fact]
		public void A_baseline_with_one_member_removed_reports_it_as_new_and_covered()
		{
			var baseline = Copy();
			var removed = baseline.Members.First(m => m.Type == "System.Drawing.Bitmap" && m.Kind == "ctor" && m.Sig == "(int, int, PixelFormat)");
			Assert.True(baseline.Members.Remove(removed));

			var delta = ApiSet.Compare(current, baseline);
			Assert.Empty(delta.Types);
			Assert.Empty(delta.DllImports);
			var reported = Assert.Single(delta.Members);
			Assert.Equal(removed.Key, reported.Key);

			delta.Evaluate(coverage.Value);
			Assert.True(delta.MemberCovered[reported.Key]);
			Assert.Equal(0, delta.Uncovered);
			string text = delta.Render("api-baseline.json");
			Assert.Contains("1 members", text);
			Assert.Contains("System.Drawing.Bitmap(int, int, PixelFormat)", text);
			Assert.Contains("covered by the shim: yes", text);
		}

		[Fact]
		public void A_removed_type_and_a_removed_forms_member_are_reported_with_their_coverage()
		{
			var baseline = Copy();
			var type = baseline.Types.First(t => t.Name == "System.Drawing.SolidBrush");
			baseline.Types.Remove(type);
			var forms = baseline.Members.First(m => m.Scope == "System.Windows.Forms" && m.Type == "System.Windows.Forms.Clipboard" && m.Name == "SetDataObject");
			baseline.Members.Remove(forms);

			var delta = ApiSet.Compare(current, baseline);
			delta.Evaluate(coverage.Value);
			Assert.Equal(type.Key, Assert.Single(delta.Types).Key);
			Assert.Equal(forms.Key, Assert.Single(delta.Members).Key);
			Assert.True(delta.TypeCovered[type.Key]);
			// Whatever the Forms shim says, the answer is a definite yes or no and it appears in the report.
			Assert.Contains("Clipboard.SetDataObject", delta.Render("api-baseline.json"));
		}

		[Fact]
		public void New_API_that_a_shim_lacks_is_reported_as_not_covered()
		{
			var live = Copy();
			live.Types.Add(new ApiType { Scope = "System.Drawing.Common", Name = "System.Drawing.NoSuchBrushType", Kind = "class" });
			live.Members.Add(new ApiMember { Scope = "System.Drawing.Common", Type = "System.Drawing.Bitmap", Kind = "method", Name = "NoSuchMethod", Sig = "(int) : void", Files = new[] { "cYo.Common/New.cs" } });
			live.Members.Add(new ApiMember { Scope = "System.Drawing.Common", Type = "System.Drawing.Bitmap", Kind = "method", Name = "GetPixel", Sig = "(string) : Color" });
			live.Members.Add(new ApiMember { Scope = "System.Drawing.Common", Type = "System.Drawing.Bitmap", Kind = "property", Name = "Width", Sig = " : int", Accessors = "get,set" });
			live.Members.Add(new ApiMember { Scope = "System.Windows.Forms", Type = "System.Windows.Forms.NoSuchControl", Kind = "ctor", Name = ".ctor", Sig = "()" });
			live.Sort();

			var delta = ApiSet.Compare(live, current);
			delta.Evaluate(coverage.Value);
			Assert.Single(delta.Types);
			Assert.Equal(4, delta.Members.Count);
			Assert.Equal(5, delta.Uncovered);
			Assert.All(delta.Types, t => Assert.False(delta.TypeCovered[t.Key]));
			Assert.All(delta.Members, m => Assert.False(delta.MemberCovered[m.Key]));
			string text = delta.Render("api-baseline.json");
			Assert.Contains("5 types and members are not covered", text);
			Assert.Contains("NoSuchMethod(int) : void", text);
			Assert.Contains("in cYo.Common/New.cs", text);
		}

		[Fact]
		public void The_coverage_check_accepts_every_drawing_member_of_the_baseline()
		{
			// The Drawing shim is complete for the inventory (mac/tools/drawing-coverage); the coverage answer must agree.
			var drawing = current.Members.Where(m => m.Scope.StartsWith("System.Drawing", StringComparison.Ordinal)).ToList();
			Assert.True(drawing.Count > 100);
			var missing = drawing.Where(m => !coverage.Value.CoversMember(m)).Select(m => m.Display()).ToList();
			Assert.True(missing.Count == 0, "Not covered: " + string.Join("; ", missing));
			var missingTypes = current.Types.Where(t => t.Scope.StartsWith("System.Drawing", StringComparison.Ordinal) && !coverage.Value.CoversType(t.Scope, t.Name)).Select(t => t.Name).ToList();
			Assert.True(missingTypes.Count == 0, "Types not covered: " + string.Join("; ", missingTypes));
		}

		[Fact]
		public void A_removed_DllImport_is_reported_and_its_linked_state_follows_the_pass_B_items()
		{
			var baseline = Copy();
			var removed = baseline.DllImports.First(p => p.Module.StartsWith("user32", StringComparison.OrdinalIgnoreCase));
			baseline.DllImports.Remove(removed);

			var live = Copy();
			var delta = ApiSet.Compare(live, baseline);
			var reported = Assert.Single(delta.DllImports);
			Assert.Equal(removed.Key, reported.Key);

			var linked = new HashSet<string> { "cYo.Common/Win32/Linked.cs" };
			Assert.Equal("yes", ApiDelta.LinkedState(new ApiDllImport { Module = "m", EntryPoint = "e", Declaring = "d", Name = "n", DeclFile = "cYo.Common/Win32/Linked.cs" }, linked));
			Assert.Equal("no", ApiDelta.LinkedState(new ApiDllImport { Module = "m", EntryPoint = "e", Declaring = "d", Name = "n", DeclFile = "cYo.Common/Win32/Excluded.cs", CallFiles = new[] { "cYo.Common/Win32/Linked.cs" } }, linked));
			Assert.Equal("yes", ApiDelta.LinkedState(new ApiDllImport { Module = "m", EntryPoint = "e", Declaring = "d", Name = "n", CallFiles = new[] { "x.cs", "cYo.Common/Win32/Linked.cs" } }, linked));
			Assert.Equal("no", ApiDelta.LinkedState(new ApiDllImport { Module = "m", EntryPoint = "e", Declaring = "d", Name = "n", CallFiles = new[] { "x.cs" } }, linked));
			Assert.Equal("unknown", ApiDelta.LinkedState(new ApiDllImport { Module = "m", EntryPoint = "e", Declaring = "d", Name = "n" }, linked));

			reported.DeclFile = "cYo.Common/Win32/Linked.cs";
			delta.EvaluateLinked(linked);
			string text = delta.Render("api-baseline.json");
			Assert.Contains("1 DllImports", text);
			Assert.Contains("linked: yes", text);
			Assert.Contains(removed.EntryPoint, text);
		}

		[Fact]
		public void The_module_name_ignores_case_and_the_dll_extension_in_the_identity()
		{
			var a = new ApiDllImport { Module = "User32.dll", EntryPoint = "SendMessage", Declaring = "X", Name = "SendMessage", Sig = "(nint)" };
			var b = new ApiDllImport { Module = "user32", EntryPoint = "SendMessage", Declaring = "X", Name = "SendMessage", Sig = "(nint)" };
			Assert.Equal(a.Key, b.Key);
			b.Sig = "(nint, int)";
			Assert.NotEqual(a.Key, b.Key);
		}

		[Fact]
		public void Normalizing_an_inventory_drops_call_sites_and_untracked_scopes()
		{
			var inv = new Inventory();
			var t = inv.GetType("System.Drawing.Common", "System.Drawing.Bitmap", 0x12);
			var other = inv.GetType("System.Private.CoreLib", "System.String", 0x12);
			var m = new MemberRec { Type = t, Kind = "property", Name = "Width", Sig = " : int", Calls = 40 };
			m.Accessors.Add("get");
			m.Files["cYo.Common/A.cs"] = 30;
			m.Files["cYo.Common/B.cs"] = 10;
			inv.Members["w"] = m;
			inv.Members["s"] = new MemberRec { Type = other, Kind = "method", Name = "Trim", Sig = "() : string", Calls = 5 };
			inv.PInvokes.Add(new PInvokeRec { Module = "user32", EntryPoint = "Beep", Declaring = "N", Name = "Beep", Sig = "(int)", Calls = 3, DeclFile = "N.cs" });

			var set = ApiSet.FromInventory(inv);
			Assert.Single(set.Types);
			var member = Assert.Single(set.Members);
			Assert.Equal("get", member.Accessors);
			Assert.Equal(new[] { "cYo.Common/A.cs", "cYo.Common/B.cs" }, member.Files);
			string json = set.ToJson();
			Assert.DoesNotContain("A.cs", json);
			Assert.DoesNotContain("N.cs", json);
			Assert.Contains("\"beep\"", json, StringComparison.OrdinalIgnoreCase);
		}
	}
}
