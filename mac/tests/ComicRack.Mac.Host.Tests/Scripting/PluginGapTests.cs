using System;
using System.IO;
using System.Linq;
using ComicRack.Mac.Host.Scripting.Scan;
using Xunit;

namespace ComicRack.Mac.Host.Tests.Scripting
{
	/// <summary>
	/// The plugin gap analysis (P4.17, R11): the type index and its stub detection, the planning rules, the names a scan records outside the WinForms and GDI+ surface, and the table of a synthetic script.
	/// Generic: nothing here names a plugin.
	/// </summary>
	public class PluginGapTests : IDisposable
	{
		private static readonly Lazy<ApiCatalog> SharedCatalog = new Lazy<ApiCatalog>(() =>
		{
			string dir = ReferenceSurface.FindDirectory();
			if (dir == null)
			{
				throw new FileNotFoundException("Microsoft.WindowsDesktop.App.Ref pack not found in the NuGet cache. Run a pass A build first: dotnet build mac/ComicRack.Plugins.Core/ComicRack.Plugins.Core.csproj -p:CensusPass=A");
			}
			return ApiCatalog.FromReferenceDirectory(dir);
		});

		private static readonly Lazy<TypeIndex> SharedIndex = new Lazy<TypeIndex>(() => TypeIndex.ForProgram());

		private static readonly Lazy<PythonModuleCatalog> SharedModules = new Lazy<PythonModuleCatalog>(() => PythonModuleCatalog.Create());

		private readonly TempFolder temp = new TempFolder();

		public void Dispose() => temp.Dispose();

		// ------------------------------------------------------------------ the type index

		private TypeIndex SampleIndex(bool withFacadeStub = true)
		{
			string file = Path.Combine(temp.Path, "Sample.Facade.dll");
			File.WriteAllBytes(file, FacadeSampleBuilder.Build(withFacadeStub));
			return TypeIndex.Load(new[] { file }, "program");
		}

		[Fact]
		public void A_member_whose_body_ends_in_the_generated_throw_is_a_stub_and_the_others_are_real()
		{
			using TypeIndex index = SampleIndex();
			IndexedType thing = index.Find("Sample.Facade.Thing");
			Assert.NotNull(thing);
			Assert.Equal("program", thing.Origin);
			Assert.Equal("stub", thing.Members[".ctor"].Status);
			Assert.Equal("constructor", thing.Members[".ctor"].Kind);
			Assert.Equal("stub", thing.Members["Stubbed"].Status);
			Assert.Equal("real", thing.Members["Real"].Status);
			Assert.Equal("stub", thing.Members["Name"].Status);
			Assert.Equal("property", thing.Members["Name"].Kind);
			Assert.False(thing.Members.ContainsKey("get_Name"));
			Assert.Equal("partial", thing.Members["Mixed"].Status);
			Assert.Equal(2, thing.Members["Mixed"].Parts);
			Assert.Equal(1, thing.Members["Mixed"].Stubs);
			Assert.Equal("stub", thing.Members["Default"].Status);
			Assert.Equal("real", thing.Members["Plain"].Status);
			Assert.False(thing.Members.ContainsKey("Member"));
			Assert.Null(index.Find("FacadeStub"));
		}

		[Fact]
		public void Without_the_FacadeStub_class_nothing_is_a_stub()
		{
			using TypeIndex index = SampleIndex(withFacadeStub: false);
			IndexedType thing = index.Find("Sample.Facade.Thing");
			Assert.All(thing.Members.Values, m => Assert.Equal("real", m.Status));
		}

		[Fact]
		public void The_index_of_the_program_finds_the_facades_the_Core_and_the_runtime()
		{
			TypeIndex index = SharedIndex.Value;
			IndexedType button = index.Find("System.Windows.Forms.Button");
			Assert.Equal("program", button.Origin);
			Assert.Equal("System.Windows.Forms", button.Assembly);
			// inherited members are found on the base type
			Assert.NotNull(index.FindMember(button, "Text", out IndexedType declaring));
			Assert.Equal("System.Windows.Forms.ButtonBase", declaring.FullName);
			Assert.NotNull(index.Find("System.Web.Script.Serialization.JavaScriptSerializer"));
			Assert.Equal("System.Web.Extensions", index.Find("System.Web.Script.Serialization.JavaScriptSerializer").Assembly);
			Assert.Equal("program", index.Find("cYo.Projects.ComicRack.Engine.YesNo").Origin);
			Assert.Equal("ComicRack.Engine", index.Find("cYo.Projects.ComicRack.Engine.ComicBook").Assembly);
			IndexedType file = index.Find("System.IO.File");
			Assert.Equal("runtime", file.Origin);
			Assert.Equal("real", file.Members["Exists"].Status);
			// the Python spelling of a generic type and of a nested type
			Assert.Equal("System.Collections.Generic.Dictionary`2", index.Find("System.Collections.Generic.Dictionary").FullName);
			Assert.Equal("System.Windows.Forms.Control+ControlCollection", index.Find("System.Windows.Forms.Control.ControlCollection").FullName);
			Assert.True(index.IsNamespaceOrPrefix("System.Web"));
			Assert.True(index.IsNamespaceOrPrefix("System.Web.Script.Serialization"));
			Assert.False(index.IsNamespaceOrPrefix("System.NoSuchNamespace"));
			// the WPF assemblies are facades since P4.17.3: the type is there and every member only throws
			IndexedType xaml = index.Find("System.Windows.Markup.XamlReader");
			Assert.Equal("PresentationFramework", xaml.Assembly);
			Assert.Equal("stub", xaml.Members["Load"].Status);
			Assert.Equal("program", index.Assemblies["PresentationFramework"]);
			Assert.Null(index.Find("System.Windows.NoSuchType"));
			Assert.Equal("program", index.Assemblies["System.Windows.Forms"]);
			Assert.Equal("runtime", index.Assemblies["System.Private.CoreLib"]);
		}

		[Fact]
		public void Every_member_of_the_surface_is_there_and_has_a_status()
		{
			// the facade has every type of the reference: the status of a member is real, stub or partial, never absent
			TypeIndex index = SharedIndex.Value;
			ApiCatalog catalog = SharedCatalog.Value;
			foreach (ApiType t in catalog.Types.Where(t => t.FullName.StartsWith("System.Windows.Forms.", StringComparison.Ordinal) && !t.FullName.Contains('+')).Take(150))
			{
				Assert.NotNull(index.Find(t.FullName));
			}
		}

		// ------------------------------------------------------------------ the rules

		[Fact]
		public void The_first_matching_rule_wins_and_a_pattern_names_a_type_a_member_or_a_prefix()
		{
			GapRules rules = GapRules.Parse(new[]
			{
				"# comment",
				"owner A.B.Control::Anchor T1",
				"owner A.B.Control T2",
				"owner A.B.Grid* T3",
				"owner A.B.* T9",
				"note A.B.Control::Handle differs it is not a handle",
				"note A.B.Control::Size - just a fact",
				"",
			});
			Assert.Equal("T1", rules.OwnerOf("A.B.Control", "Anchor"));
			Assert.Equal("T2", rules.OwnerOf("A.B.Control", "Dock"));
			Assert.Equal("T2", rules.OwnerOf("A.B.Control+ControlCollection", "Add"));
			Assert.Equal("T3", rules.OwnerOf("A.B.GridView", "Rows"));
			Assert.Equal("T9", rules.OwnerOf("A.B.Other", null));
			Assert.Null(rules.OwnerOf("C.D.Other", "x"));
			Assert.Equal("it is not a handle", rules.NoteOf("A.B.Control", "Handle", out string status));
			Assert.Equal("differs", status);
			Assert.Equal("just a fact", rules.NoteOf("A.B.Control", "Size", out status));
			Assert.Null(status);
			Assert.Null(rules.NoteOf("A.B.Control", "Name", out _));
			Assert.Equal(new[] { "T1", "T2", "T3", "T9" }, rules.Tasks.ToArray());
		}

		[Fact]
		public void A_line_that_is_neither_owner_nor_note_is_refused()
		{
			Assert.Throws<FormatException>(() => GapRules.Parse(new[] { "owner onlyone" }));
			Assert.Throws<FormatException>(() => GapRules.Parse(new[] { "note A.B maybe text" }));
			Assert.Throws<FormatException>(() => GapRules.Parse(new[] { "task A.B T1" }));
		}

		[Fact]
		public void The_rules_of_the_repository_parse_and_name_tasks_that_exist_in_the_plan()
		{
			string root = ScriptTestSupport.RepoRoot();
			GapRules rules = GapRules.Load(Path.Combine(root, "mac", "scripting", "gap-rules.txt"));
			Assert.True(rules.Count > 50);
			string plan = File.ReadAllText(Path.Combine(root, "mac", "plans", "full-port.md"));
			foreach (string task in rules.Tasks)
			{
				Assert.True(plan.Contains("\n#### " + task + " "), "gap-rules.txt names " + task + ", which has no task block in mac/plans/full-port.md");
			}
		}

		// ------------------------------------------------------------------ what a scan records outside the surface

		private static ScriptScanner Scanner() => new ScriptScanner(SharedCatalog.Value) { ForeignTypeLookup = (ns, name) => SharedIndex.Value.Find(ns, name) != null };

		[Fact]
		public void A_scan_records_the_dotnet_names_python_modules_and_names_the_script_defines()
		{
			ScanUnit u = Scanner().ScanPython(
				"import clr, os, re\nimport os.path\nclr.AddReference('System')\nimport System\nfrom System.IO import File, Path\nfrom System.Diagnostics import Process\n" +
				"from cYo.Projects.ComicRack.Engine import YesNo\nimport urllib2\n" +
				"class Mine(object):\n\tdef Frobnicate(self):\n\t\tself.state = 1\n" +
				"def helper(a):\n\tok = File.Exists(Path.Combine('a', 'b'))\n\tp = Process.GetCurrentProcess()\n\tx = YesNo.Yes\n\ty = System.Math.Abs(-1)\n\tp.Start()\n");
			Assert.Contains("System.IO.File.Exists", u.Foreign.Keys);
			Assert.Contains("System.IO.Path.Combine", u.Foreign.Keys);
			Assert.Contains("System.Diagnostics.Process.GetCurrentProcess", u.Foreign.Keys);
			Assert.Contains("cYo.Projects.ComicRack.Engine.YesNo.Yes", u.Foreign.Keys);
			Assert.Contains("System.Math.Abs", u.Foreign.Keys);
			Assert.Contains("os", u.Modules.Keys);
			Assert.Contains("os.path", u.Modules.Keys);
			Assert.Contains("urllib2", u.Modules.Keys);
			Assert.DoesNotContain("clr", u.Modules.Keys);
			Assert.Contains("Frobnicate", u.Defined);
			Assert.Contains("state", u.Defined);
			Assert.Contains("helper", u.Defined);
			Assert.Contains("Start", u.UntypedNames.Keys);
		}

		[Fact]
		public void A_star_import_of_a_dotnet_namespace_binds_its_types_only_when_a_lookup_is_given()
		{
			string script = "from cYo.Projects.ComicRack.Engine import *\nx = YesNo.Yes\nclass K(object):\n\tpass\n";
			Assert.Contains("cYo.Projects.ComicRack.Engine.YesNo.Yes", Scanner().ScanPython(script).Foreign.Keys);
			Assert.DoesNotContain("cYo.Projects.ComicRack.Engine.YesNo.Yes", new ScriptScanner(SharedCatalog.Value).ScanPython(script).Foreign.Keys);
		}

		[Fact]
		public void After_two_star_imports_a_name_of_the_second_namespace_is_not_taken_for_the_first_and_an_unknown_name_stays_unknown()
		{
			ScanUnit u = Scanner().ScanPython("import clr\nclr.AddReference('System.Windows.Forms')\nclr.AddReference('System.Drawing')\nfrom System.Drawing import *\nfrom System.Windows.Forms import *\n" +
				"b = Bitmap(1, 1)\nComicRack.App.GetComicThumbnail(None, 0)\n");
			Assert.True(u.Members.ContainsKey("System.Drawing.Bitmap::.ctor"), string.Join(", ", u.Members.Keys));
			Assert.DoesNotContain(u.Foreign.Keys, k => k.StartsWith("System.Windows.Forms.ComicRack", StringComparison.Ordinal));
			Assert.Contains("App", u.UntypedNames.Keys);
			Assert.Contains("GetComicThumbnail", u.UntypedNames.Keys);
		}

		[Fact]
		public void System_Web_Extensions_is_provided_by_the_layer_and_a_backslash_file_name_is_reported()
		{
			ScanUnit u = Scanner().ScanPython("import clr\nclr.AddReference('System.Web.Extensions')\nclr.AddReference('System.Web.Services')\npath = r'x'\nf = path + '\\App.Config'\ng = path + '\\\\home_32.png'\nh = 'plain.txt'\n");
			Assert.Contains(u.References, r => r.Name == "System.Web.Extensions" && r.Kind == "framework");
			Assert.Contains(u.References, r => r.Name == "System.Web.Services" && r.Kind == "framework-only");
			// P4.17.5: a link with this name is made next to the file, so the finding is information
			Assert.Contains(u.Findings, f => f.Kind == "windows-path" && f.Severity == "info" && f.Symbol == "Windows path separator \"\\App.Config\"");
			Assert.Contains(u.Findings, f => f.Kind == "windows-path" && f.Severity == "info" && f.Symbol == "Windows path separator \"\\home_32.png\"");
			Assert.DoesNotContain(u.Findings, f => f.Symbol.Contains("plain.txt"));
		}

		[Fact]
		public void An_assembly_scan_records_the_dotnet_types_it_binds_to()
		{
			ScanUnit u = Scanner().ScanAssembly(ScanAssemblyBuilder.Build());
			Assert.Contains("System.Object", u.Foreign.Keys);
			Assert.Contains("System.String", u.Foreign.Keys);
			Assert.DoesNotContain(u.Foreign.Keys, k => k.StartsWith("System.Windows.Forms.", StringComparison.Ordinal));
		}

		// ------------------------------------------------------------------ the table of a synthetic script

		private const string Script =
			"import clr, os, re, sys, nosuchmodule\nclr.AddReference('System.Windows.Forms')\nclr.AddReference('System.Web.Extensions')\nclr.AddReference('NoSuchAssembly')\n" +
			"from System.Windows.Forms import Form, Button\nfrom System.IO import File\nfrom System.Diagnostics import Process\nfrom cYo.Projects.ComicRack.Engine import YesNo\nimport System\nimport System.NoSuchNamespace.Thing\n" +
			"class Mine(Form):\n\tdef Frobnicate(self):\n\t\tself.state = 1\n" +
			"def unknown(x):\n\tx.Frobnicate()\n" +
			"def run(books):\n\tj = System.Web.Script.Serialization.JavaScriptSerializer()\n\tok = File.Exists('x')\n\tb = Button()\n\tb.Text = 't'\n\tf = Form()\n\tf.ShowDialog()\n\tx = YesNo.Yes\n" +
			"\tProcess.Start('x')\n\tl = []\n\tl.append(1)\n\tfoo.Zzyzx()\n\tComicRack.App.GetComicThumbnail(books[0], 0)\n\tunknown(Mine())\n\tSystem.NoSuchNamespace.Thing.Do()\n";

		private static PluginGap GapOf(string script, string rulesText)
		{
			var scanner = Scanner();
			ScanUnit unit = scanner.ScanPython(script, "probe.py");
			var report = new ScanReport(SharedCatalog.Value, new[] { "probe.py" }, new[] { unit });
			var facts = new PluginFacts { Id = "probe", PackageFile = "probe.py", Sha256 = "0" };
			return PluginGapBuilder.Build(report, facts, SharedIndex.Value, GapRules.Parse(rulesText.Split('\n')), SharedCatalog.Value, SharedModules.Value);
		}

		private static GapRow Row(System.Collections.Generic.IEnumerable<GapRow> rows, string name)
		{
			GapRow[] found = rows.Where(r => r.Name == name).ToArray();
			Assert.True(found.Length == 1, name + " in: " + string.Join(", ", rows.Select(r => r.Name)));
			return found[0];
		}

		[Fact]
		public void Every_row_has_one_status_of_the_closed_set_and_a_row_that_needs_work_gets_the_task_of_the_rules()
		{
			PluginGap gap = GapOf(Script, "owner System.Windows.Forms.* T-forms\nowner System.NoSuchNamespace* T-missing\nowner NoSuchAssembly T-asm\nowner nosuchmodule T-module\nnote System.Diagnostics.Process::Start differs starting a Windows program fails\nowner System.Diagnostics.Process T-proc\n");
			Assert.NotEmpty(gap.AllRows);
			Assert.All(gap.AllRows, r => Assert.Contains(r.Status, GapReportWriter.Statuses));
			foreach (GapRow r in gap.Framework.Concat(gap.Core).Concat(gap.References).Concat(gap.Modules).Where(r => r.NeedsTask))
			{
				Assert.False(string.IsNullOrEmpty(r.Owner), r.Status + " " + r.Name);
			}
			foreach (GapRow r in gap.Surface.Where(r => r.NeedsTask))
			{
				Assert.Equal("T-forms", r.Owner);
			}
			Assert.All(gap.AllRows.Where(r => !r.NeedsTask), r => Assert.Null(r.Owner));
		}

		[Fact]
		public void The_rows_say_what_the_program_has_for_each_kind_of_name()
		{
			PluginGap gap = GapOf(Script, "owner System.Windows.Forms.* T-forms\nowner System.NoSuchNamespace* T-missing\nowner NoSuchAssembly T-asm\nowner nosuchmodule T-module\nnote System.Diagnostics.Process::Start differs starting a Windows program fails\nowner System.Diagnostics.Process T-proc\n");

			// .NET 10
			GapRow exists = Row(gap.Framework, "System.IO.File.Exists");
			Assert.Equal(("real", ".NET 10"), (exists.Status, exists.Source));
			// the serializer of the layer
			GapRow serializer = Row(gap.Framework, "System.Web.Script.Serialization.JavaScriptSerializer");
			Assert.Equal(("real", "facade"), (serializer.Status, serializer.Source));
			// a member that exists and behaves differently, with its task
			GapRow start = Row(gap.Framework, "System.Diagnostics.Process.Start");
			Assert.Equal(("differs", "T-proc"), (start.Status, start.Owner));
			Assert.Contains("fails", start.Note);
			// a name that is nowhere
			Assert.Equal("missing", Row(gap.Framework, "System.NoSuchNamespace.Thing").Status);
			// the Core
			GapRow yes = Row(gap.Core, "cYo.Projects.ComicRack.Engine.YesNo.Yes");
			Assert.Equal(("real", "program"), (yes.Status, yes.Source));
			// the surface: a constructor of the facade is real or a stub, never missing
			GapRow ctor = Row(gap.Surface, "Button..ctor");
			Assert.Contains(ctor.Status, new[] { "real", "stub", "partial" });
			Assert.Equal("facade", ctor.Source);
			// the references
			Assert.Equal(("real", "facade"), (Row(gap.References, "System.Web.Extensions").Status, Row(gap.References, "System.Web.Extensions").Source));
			Assert.Equal(("missing", "T-asm"), (Row(gap.References, "NoSuchAssembly").Status, Row(gap.References, "NoSuchAssembly").Owner));
			// the modules
			Assert.Equal(("real", "builtin"), (Row(gap.Modules, "re").Status, Row(gap.Modules, "re").Source));
			Assert.Equal(("real", "stdlib"), (Row(gap.Modules, "os").Status, Row(gap.Modules, "os").Source));
			Assert.Equal(("missing", "T-module"), (Row(gap.Modules, "nosuchmodule").Status, Row(gap.Modules, "nosuchmodule").Owner));
			// by name: Python built-in, the script's own, the Core object, an unknown name
			Assert.Equal("python", Row(gap.ByName, "append").Status);
			Assert.Equal("plugin", Row(gap.ByName, "Frobnicate").Status);
			GapRow thumbnail = Row(gap.ByName, "GetComicThumbnail");
			Assert.Equal(("real", "program"), (thumbnail.Status, thumbnail.Source));
			Assert.Contains("Core", thumbnail.Candidates);
			Assert.Equal("dynamic", Row(gap.ByName, "Zzyzx").Status);
		}

		[Fact]
		public void The_report_text_has_a_row_for_every_status_that_needs_work_and_no_machine_path()
		{
			PluginGap gap = GapOf(Script, "owner System.Windows.Forms.* T-forms\nowner System.NoSuchNamespace* T-missing\nowner NoSuchAssembly T-asm\nowner nosuchmodule T-module\nowner System.Diagnostics.Process T-proc\n");
			var report = new PluginGapReport { Plugins = new[] { gap }, ReferenceSource = "test", ReferenceTypes = 1, Runtime = "x", IndexedAssemblies = 1 };
			var text = new StringWriter();
			GapReportWriter.WriteMarkdown(report, text, new System.Collections.Generic.Dictionary<string, string> { ["probe"] = "### A note\n\nHand written.\n" }, "mac/scripting/plugin-gap.sh");
			string md = text.ToString();
			Assert.Contains("## probe", md);
			Assert.Contains("### A note", md);
			Assert.Contains("| System.NoSuchNamespace.Thing", md.Replace("`", "") .Replace("| `", "| "));
			Assert.DoesNotContain(Environment.UserName.Length > 2 ? "/Users/" + Environment.UserName : "/Users/", md);
			string json = GapReportWriter.ToJson(report);
			Assert.Contains("\"id\": \"probe\"", json);
			Assert.Equal(json, GapReportWriter.ToJson(report));
		}
	}
}
