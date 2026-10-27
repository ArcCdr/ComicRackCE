using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using ComicRack.Mac.Host.Scripting.Scan;
using Xunit;

namespace ComicRack.Mac.Host.Tests.Scripting
{
	/// <summary>The script compatibility scanner (P4.6a): names, attribute chains, script classes and Tier C findings in Python 2 scripts, member references of assemblies, plugin packages, the report.</summary>
	public class ScriptScanTests : IDisposable
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

		private readonly TempFolder temp = new TempFolder();

		public void Dispose() => temp.Dispose();

		private static ScriptScanner Scanner() => new ScriptScanner(SharedCatalog.Value);

		private static ScanUnit Python(string source) => Scanner().ScanPython(source);

		private static int Members(ScanUnit unit, string type, string member) => unit.Members.TryGetValue("System.Windows.Forms." + type + "::" + member, out int n) ? n : 0;

		/// <summary>Uses of a member by name, whichever type of the hierarchy declares it (a form redeclares members of a control).</summary>
		private static int Uses(ScanUnit unit, string member) => unit.Members.Where(m => m.Key.EndsWith("::" + member, StringComparison.Ordinal)).Sum(m => m.Value);

		private static int Drawing(ScanUnit unit, string type, string member) => unit.Members.TryGetValue("System.Drawing." + type + "::" + member, out int n) ? n : 0;

		private static int Type(ScanUnit unit, string name) => unit.Types.TryGetValue(name.Contains('.') ? name : "System.Windows.Forms." + name, out int n) ? n : 0;

		[Fact]
		public void The_catalog_is_the_real_surface_with_inherited_members_declared_on_their_base_type()
		{
			ApiCatalog c = SharedCatalog.Value;
			ApiType button = c.Type("System.Windows.Forms", "Button");
			Assert.NotNull(button);
			Assert.NotNull(c.Type("System.Windows.Forms", "SaveFileDialog"));
			Assert.NotNull(c.Type("System.Drawing", "Graphics"));
			Assert.NotNull(c.Type("System.Drawing", "Point"));
			Assert.Equal(ApiTypeKind.Enum, c.Type("System.Windows.Forms", "DialogResult").Kind);
			Assert.NotNull(c.FindMember(button, "Text", out ApiType declaring));
			Assert.Equal("ButtonBase", declaring.Name);
			Assert.NotNull(c.FindMember(button, "Controls", out ApiType controls));
			Assert.Equal("Control", controls.Name);
			Assert.Equal("Control.ControlCollection", c.Type("System.Windows.Forms.Control+ControlCollection").Name);
			Assert.True(c.IsNamespace("System.Windows"));
			Assert.True(c.IsComplete);
		}

		[Fact]
		public void Without_a_reference_pack_the_catalog_falls_back_to_the_facade_assemblies_and_says_it_is_incomplete()
		{
			ApiCatalog facade = ApiCatalog.FromFacade(AppContext.BaseDirectory);
			Assert.False(facade.IsComplete);
			Assert.NotNull(facade.Type("System.Windows.Forms", "Form"));
			Assert.Contains("facade", facade.Source);
			Assert.True(facade.TypeCount < SharedCatalog.Value.TypeCount);
			Assert.Equal(SharedCatalog.Value.Source, ReferenceSurface.Load().Source);
		}

		[Fact]
		public void Star_imports_constructors_properties_and_method_results_are_resolved_to_the_declaring_type()
		{
			ScanUnit u = Python(
				"import clr\nclr.AddReferenceByPartialName('System.Windows.Forms')\nfrom System.Windows.Forms import *\n" +
				"def Show(books):\n\tf = Form()\n\tb = Button()\n\tb.Text = 'OK'\n\tb.DialogResult = DialogResult.OK\n\tf.Controls.Add(b)\n\tif f.ShowDialog() == DialogResult.Cancel:\n\t\treturn\n\tMessageBox.Show('done')\n");
			Assert.True(u.Parsed);
			Assert.Equal(1, Type(u, "Form"));
			Assert.Equal(1, Type(u, "Button"));
			Assert.Equal(1, Type(u, "MessageBox"));
			Assert.Equal(2, Type(u, "DialogResult"));
			Assert.Equal(1, Members(u, "Form", ".ctor"));
			Assert.Equal(1, Members(u, "Button", ".ctor"));
			Assert.Equal(1, Members(u, "ButtonBase", "Text"));
			Assert.Equal(1, Members(u, "Button", "DialogResult"));
			Assert.Equal(1, Members(u, "Control", "Controls"));
			Assert.Equal(1, Members(u, "Control+ControlCollection", "Add"));
			Assert.Equal(1, Members(u, "Form", "ShowDialog"));
			Assert.Equal(1, Members(u, "MessageBox", "Show"));
			Assert.Equal(1, Members(u, "DialogResult", "OK"));
			Assert.Equal(1, Members(u, "DialogResult", "Cancel"));
			Assert.Equal("A", u.Tier);
			Assert.Contains(u.References, r => r.Name == "System.Windows.Forms" && r.Kind == "forms");
			Assert.Empty(u.Findings);
		}

		[Fact]
		public void Aliases_and_fully_qualified_chains_name_the_same_types()
		{
			ScanUnit u = Python(
				"import System.Windows.Forms as WF\nimport System.Drawing\nimport System\n" +
				"def Go():\n\tWF.MessageBox.Show('a')\n\tb = System.Windows.Forms.Button()\n\tp = System.Drawing.Point(1, 2)\n\tb.Location = p\n\tx = p.X\n");
			Assert.Equal(1, Type(u, "MessageBox"));
			Assert.Equal(1, Type(u, "Button"));
			Assert.Equal(1, Type(u, "System.Drawing.Point"));
			Assert.Equal(1, Members(u, "MessageBox", "Show"));
			Assert.Equal(1, Members(u, "Control", "Location"));
			Assert.Equal(1, Drawing(u, "Point", "X"));
		}

		[Fact]
		public void Explicit_imports_bind_the_names_and_a_local_name_hides_a_star_import()
		{
			ScanUnit u = Python(
				"from System.Windows.Forms import Label as Caption, TextBox\nfrom System.Windows.Forms import *\n" +
				"def Go():\n\tc = Caption()\n\tc.Text = 't'\n\tButton = 5\n\tx = Button.real\n\tt = TextBox()\n");
			Assert.Equal(1, Type(u, "Label"));
			Assert.Equal(1, Type(u, "TextBox"));
			Assert.Equal(0, Type(u, "Button"));
			Assert.Equal(1, Members(u, "Label", "Text"));
		}

		[Fact]
		public void A_class_that_derives_from_a_form_uses_the_members_it_inherits_and_overrides_but_not_its_own()
		{
			ScanUnit u = Python(
				"from System.Windows.Forms import *\nfrom System.Drawing import *\n" +
				"class Dlg(Form):\n\tdef __init__(self):\n\t\tself.Text = 'x'\n\t\tself.count = 0\n\t\tself._ok = Button()\n\t\tself._ok.Click += self.OnOk\n\t\tself.Controls.Add(self._ok)\n" +
				"\tdef OnOk(self, sender, e):\n\t\tself.count += 1\n\t\tself.Close()\n\tdef helper(self):\n\t\treturn self.count\n\tdef OnPaint(self, e):\n\t\te.Graphics.Clear(Color.Red)\n");
			Assert.Equal(1, Type(u, "Form"));
			Assert.Equal(1, Uses(u, "Text"));
			Assert.Equal(1, Uses(u, "Click"));
			Assert.Equal(1, Uses(u, "Controls"));
			Assert.Equal(1, Uses(u, "Close"));
			Assert.Equal(1, Uses(u, "OnPaint"));
			Assert.Equal(0, Uses(u, "helper"));
			Assert.Equal(0, Uses(u, "OnOk"));
			Assert.Equal(0, Uses(u, "count"));
		}

		[Fact]
		public void The_parameters_of_a_thumbnail_overlay_hook_are_typed_from_the_plugin_contract()
		{
			ScanUnit u = Python(
				"from System.Drawing import *\n#@Name Overlay\n#@Hook DrawThumbnailOverlay\n#@Enabled false\ndef Overlay(book, graphics, bounds, flags):\n\tbr = SolidBrush(Color.FromArgb(1, 2, 3, 4))\n\tgraphics.FillRectangle(br, bounds)\n\tbr.Dispose()\n");
			Assert.Equal(1, Drawing(u, "Graphics", "FillRectangle"));
			Assert.Equal(1, Drawing(u, "SolidBrush", ".ctor"));
			Assert.Equal(new[] { "DrawThumbnailOverlay" }, u.Hooks);
			Assert.DoesNotContain("FillRectangle", u.UntypedNames.Keys);
		}

		[Fact]
		public void Python_2_syntax_is_understood_and_a_syntax_error_is_reported_without_a_guess()
		{
			ScanUnit ok = Python("import os\ntry:\n\tprint 'a', os.sep\nexcept Exception, e:\n\tprint >>sys.stderr, e\nx = ur'abc'\nexec 'pass' in {}\nraise ValueError, 'x'\n");
			Assert.True(ok.Parsed);
			Assert.Equal("none", ok.Tier);
			ScanUnit bad = Python("def broken(:\n\tpass\n");
			Assert.False(bad.Parsed);
			Assert.Equal("?", bad.Tier);
			Assert.NotNull(bad.Error);
			Assert.Contains(bad.Findings, f => f.Kind == "parse-error");
		}

		[Fact]
		public void Hooks_are_read_from_the_comment_lines()
		{
			ScanUnit u = Python("#@Name x\n#@Hook Books, Editor\ndef x(books): pass\n#@Hook Library\ndef y(): pass\n");
			Assert.Equal(new[] { "Books", "Editor", "Library" }, u.Hooks);
		}

		[Fact]
		public void Calls_into_windows_libraries_through_ctypes_are_tier_C_with_the_line_and_the_function()
		{
			ScanUnit u = Python("import ctypes\nuser32 = ctypes.windll.user32\nx = 1\nuser32.SendMessageW(1, 2, 3, 4)\nctypes.windll.kernel32.GetTickCount()\nctypes.WinDLL('gdi32')\nctypes.cdll.LoadLibrary('shell32.dll')\n");
			Assert.Equal("C", u.Tier);
			ScanFinding f = Assert.Single(u.Findings, x => x.Symbol == "user32.SendMessageW");
			Assert.Equal("error", f.Severity);
			Assert.Equal("windows-library", f.Kind);
			Assert.Equal(4, f.Line);
			Assert.Equal("Uses Windows-only API `user32.SendMessageW` at line 4; this script cannot run on macOS (a native Windows library).", f.Message);
			Assert.Contains(u.Findings, x => x.Symbol == "kernel32.GetTickCount" && x.Line == 5);
			Assert.Contains(u.Findings, x => x.Symbol == "gdi32" && x.Line == 6);
			Assert.Contains(u.Findings, x => x.Symbol == "shell32" && x.Line == 7);
			Assert.DoesNotContain(u.Findings, x => x.Symbol == "user32" && x.Line == 4);
			Assert.Contains(u.Findings, x => x.Kind == "ctypes" && x.Severity == "warning");
		}

		[Fact]
		public void Window_procedures_native_handles_wpf_com_and_windows_modules_are_tier_C()
		{
			ScanUnit wnd = Python("from System.Windows.Forms import *\nclass F(Form):\n\tdef WndProc(self, m):\n\t\tForm.WndProc(self, m)\n");
			Assert.Equal("C", wnd.Tier);
			Assert.Contains(wnd.Findings, f => f.Kind == "windows-member" && f.Symbol.EndsWith(".WndProc") && f.Line == 3);

			ScanUnit handle = Python("from System.Windows.Forms import *\ndef Go():\n\tf = Form()\n\th = f.Handle\n\tn = NativeWindow()\n");
			Assert.Contains(handle.Findings, f => f.Symbol.EndsWith(".Handle") && f.Line == 4);
			Assert.Contains(handle.Findings, f => f.Kind == "windows-type" && f.Symbol == "NativeWindow");

			ScanUnit wpf = Python("import clr\nclr.AddReference('PresentationFramework')\nimport System.Windows.Controls\nfrom System.Windows import Window\nfrom System.Windows.Forms import Button\n");
			Assert.Contains(wpf.Findings, f => f.Kind == "wpf" && f.Symbol == "PresentationFramework" && f.Line == 2);
			Assert.Contains(wpf.Findings, f => f.Kind == "wpf" && f.Symbol == "System.Windows.Controls" && f.Line == 3);
			Assert.DoesNotContain(wpf.Findings, f => f.Line == 5);

			ScanUnit com = Python("import clr\nclr.AddReference('Microsoft.Office.Interop.Excel')\nimport win32api\nfrom System.Runtime.InteropServices import Marshal\nMarshal.GetActiveObject('Excel.Application')\nimport _winreg\n");
			Assert.Contains(com.Findings, f => f.Kind == "com" && f.Line == 2);
			Assert.Contains(com.Findings, f => f.Kind == "windows-module" && f.Symbol == "win32api" && f.Line == 3);
			Assert.Contains(com.Findings, f => f.Kind == "windows-member" && f.Symbol == "Marshal.GetActiveObject" && f.Line == 5);
			Assert.Contains(com.Findings, f => f.Kind == "windows-module" && f.Symbol == "_winreg");
			Assert.Equal("C", com.Tier);
		}

		[Fact]
		public void Windows_only_code_behind_a_platform_check_or_an_optional_import_is_a_warning_not_an_error()
		{
			ScanUnit guarded = Python(
				"import os, sys\nimport ctypes\nif os.name == 'nt':\n\tctypes.windll.user32.MessageBeep(0)\nelse:\n\tpass\n" +
				"try:\n\timport win32api\nexcept ImportError:\n\twin32api = None\n" +
				"if sys.platform != 'win32':\n\tx = 1\nelse:\n\tctypes.windll.kernel32.GetTickCount()\n" +
				"ok = sys.platform == 'win32' and ctypes.windll.shell32.IsUserAnAdmin()\n");
			Assert.Equal("none", guarded.Tier);
			Assert.DoesNotContain(guarded.Findings, f => f.Severity == "error");
			Assert.Contains(guarded.Findings, f => f.Symbol == "user32.MessageBeep" && f.Line == 4 && f.Severity == "warning");
			Assert.Contains(guarded.Findings, f => f.Symbol == "win32api" && f.Line == 8 && f.Severity == "warning");
			Assert.Contains(guarded.Findings, f => f.Symbol == "kernel32.GetTickCount" && f.Severity == "warning");
			Assert.Contains(guarded.Findings, f => f.Symbol == "shell32.IsUserAnAdmin" && f.Severity == "warning");
			Assert.Contains("behind a platform check or a guarded import", guarded.Findings.First(f => f.Symbol == "user32.MessageBeep").Message);

			ScanUnit open = Python("import ctypes\nctypes.windll.user32.MessageBeep(0)\ntry:\n\timport win32api\nexcept KeyError:\n\tpass\n");
			Assert.Equal("C", open.Tier);
			Assert.Contains(open.Findings, f => f.Symbol == "user32.MessageBeep" && f.Severity == "error");
			Assert.Contains(open.Findings, f => f.Symbol == "win32api" && f.Severity == "error");
		}

		[Fact]
		public void A_windows_only_python_module_is_reported_once_at_its_import_not_at_every_use()
		{
			ScanUnit u = Python("import win32api\nwin32api.Beep(1, 2)\nwin32api.MessageBox(0, 'a')\n");
			Assert.Single(u.Findings, f => f.Kind == "windows-module");
			Assert.Equal(1, u.Findings.Single(f => f.Kind == "windows-module").Line);
		}

		[Fact]
		public void A_web_browser_document_is_tier_C_and_the_control_itself_is_tier_B()
		{
			ScanUnit u = Python("from System.Windows.Forms import *\ndef Go():\n\tw = WebBrowser()\n\tw.Navigate('http://example.org')\n\td = w.Document\n");
			Assert.Equal("C", u.Tier);
			Assert.Contains(u.Findings, f => f.Symbol == "WebBrowser.Document");
			ScanUnit b = Python("from System.Windows.Forms import *\ndef Go():\n\tw = WebBrowser()\n\tw.Navigate('http://example.org')\n\tt = TreeView()\n");
			Assert.Equal("B", b.Tier);
			Assert.Empty(b.Findings);
		}

		[Fact]
		public void Windows_paths_programs_and_synchronous_html_property_reads_are_warnings()
		{
			ScanUnit u = Python(
				"import os\np = 'C:\\\\Temp\\\\x.txt'\nq = os.environ['APPDATA']\nr = '%APPDATA%\\\\ComicRack'\nos.startfile(p)\ns = 'notepad.exe x'\nok = '/Users/me/x'\n" +
				"html = '''<html>\\n<script>var a = window.external.Config;\\nwindow.external.ComicRack.OpenBooks.OpenFile(\"x\");\\nvar b = window.external.App.Version;</script></html>'''\n");
			Assert.Equal("none", u.Tier);
			Assert.All(u.Findings, f => Assert.Equal("warning", f.Severity));
			Assert.Contains(u.Findings, f => f.Kind == "windows-path" && f.Line == 2);
			Assert.Contains(u.Findings, f => f.Kind == "windows-path" && f.Line == 4);
			Assert.Contains(u.Findings, f => f.Kind == "windows-path" && f.Line == 6);
			Assert.Contains(u.Findings, f => f.Kind == "os-startfile" && f.Line == 5);
			Assert.DoesNotContain(u.Findings, f => f.Line == 3 || f.Line == 7);
			ScanFinding[] html = u.Findings.Where(f => f.Kind == "html-sync-property").OrderBy(f => f.Line).ToArray();
			Assert.Equal(new[] { "window.external.Config", "window.external.App.Version" }, html.Select(f => f.Symbol).ToArray());
			Assert.Equal(new[] { 9, 11 }, html.Select(f => f.Line).ToArray());
		}

		[Fact]
		public void The_bundled_sample_scripts_list_the_forms_types_with_counts()
		{
			ScanReport report = Scanner().Scan(new[] { ScriptTestSupport.BundledScripts });
			Assert.Equal(7, report.Units.Count(u => u.Kind == "python"));
			Assert.All(report.Units, u => Assert.True(u.Parsed, u.Path + ": " + u.Error));
			TypeUsage Find(string full) => Assert.Single(report.Types, t => t.Type == full);
			Assert.Equal(3, Find("System.Windows.Forms.Button").Scripts);
			Assert.True(Find("System.Windows.Forms.Form").Scripts >= 3);
			Assert.True(Find("System.Windows.Forms.MessageBox").Scripts >= 1);
			Assert.True(Find("System.Windows.Forms.SaveFileDialog").Scripts >= 1);
			Assert.True(Find("System.Windows.Forms.DialogResult").Scripts >= 4);
			Assert.Equal("A", Find("System.Windows.Forms.Button").Tier);
			Assert.Contains(report.Members, m => m.Key == "Control.Controls" && m.Scripts >= 3);
			// The ranking is sorted by the number of scripts, then packages, then uses.
			Assert.Equal(report.Types.OrderByDescending(t => t.Scripts).ThenByDescending(t => t.Packages).ThenByDescending(t => t.Uses).ThenBy(t => t.Type, StringComparer.Ordinal).Select(t => t.Type), report.Types.Select(t => t.Type));
			Assert.Equal(0, report.Units.Count(u => u.Tier == "C"));
			Assert.Contains(report.Units, u => u.Path == "RefreshView.py" && u.Findings.Any(f => f.Kind == "missing-assembly" && f.Symbol == "cYo.Common.Windows"));
			Assert.Contains(report.References, r => r.Name == "System.Windows.Forms" && r.Kind == "forms" && r.Scripts >= 4);
		}

		[Fact]
		public void An_assembly_is_scanned_through_its_metadata_with_accessors_mapped_to_properties()
		{
			ScanUnit u = Scanner().ScanAssembly(ScanAssemblyBuilder.Build(), "Plugin.dll");
			Assert.Equal("assembly", u.Kind);
			Assert.True(u.Parsed, u.Error);
			Assert.Equal(1, Type(u, "Button"));
			Assert.Equal(1, Type(u, "Form"));
			Assert.Equal(1, Members(u, "Button", ".ctor"));
			Assert.Equal(1, Members(u, "ButtonBase", "Text"));
			Assert.Equal(1, Members(u, "Control", "Controls"));
			Assert.Equal(1, Members(u, "MessageBox", "Show"));
			Assert.Equal(1, Members(u, "DialogResult", "OK"));
			Assert.Equal(1, Drawing(u, "Color", "Red"));
			Assert.Contains("(String) -> DialogResult", u.Signatures["System.Windows.Forms.MessageBox::Show"]);
			Assert.Contains(u.References, r => r.Name == "System.Windows.Forms" && r.Kind == "forms");
			Assert.Contains(u.References, r => r.Name == "System.Drawing" && r.Kind == "drawing");
			// Tier C: P/Invoke into user32, the window procedure override, the message type and WPF.
			Assert.Equal("C", u.Tier);
			Assert.Contains(u.Findings, f => f.Kind == "p-invoke" && f.Severity == "error" && f.Symbol == "user32.SendMessageW");
			Assert.Contains(u.Findings, f => f.Kind == "wndproc" && f.Symbol == "PluginForm.WndProc");
			Assert.Contains(u.Findings, f => f.Kind == "windows-type" && f.Symbol == "Message");
			Assert.Contains(u.Findings, f => f.Kind == "wpf" && f.Symbol == "PresentationFramework");
			Assert.Contains(u.Findings, f => f.Kind == "windows-path" && f.Severity == "warning");
			Assert.Equal("Uses Windows-only API `user32.SendMessageW`; this plugin cannot run on macOS (a native Windows library).", u.Findings.Single(f => f.Kind == "p-invoke").Message);
		}

		[Fact]
		public void Something_that_is_not_an_assembly_is_reported_unreadable()
		{
			ScanUnit junk = Scanner().ScanAssembly(Encoding.ASCII.GetBytes("not an executable"), "junk.dll");
			Assert.False(junk.Parsed);
			Assert.Equal("?", junk.Tier);
			Assert.NotNull(junk.Error);
		}

		[Fact]
		public void A_native_windows_image_in_a_plugin_is_a_warning_about_what_depends_on_it()
		{
			byte[] native = ScanAssemblyBuilder.WithoutClrHeader(ScanAssemblyBuilder.Build());
			ScanUnit u = Scanner().ScanAssembly(native, "Helper.exe");
			Assert.Equal("native", u.Kind);
			ScanFinding f = Assert.Single(u.Findings);
			Assert.Equal("native-binary", f.Kind);
			Assert.Equal("warning", f.Severity);
			Assert.Equal("Helper.exe", f.Symbol);
			Assert.Equal("none", u.Tier);
		}

		[Fact]
		public void Folders_and_plugin_packages_are_scanned_in_memory_and_grouped_by_plugin()
		{
			string root = Path.Combine(temp.Path, "plugins");
			Directory.CreateDirectory(Path.Combine(root, "alpha", "sub"));
			File.WriteAllText(Path.Combine(root, "alpha", "a.py"), "from System.Windows.Forms import *\ndef A():\n\tForm()\n");
			File.WriteAllText(Path.Combine(root, "alpha", "sub", "b.py"), "from System.Windows.Forms import *\ndef B():\n\tForm()\n\tButton()\n");
			File.WriteAllBytes(Path.Combine(root, "alpha", "lib.dll"), ScanAssemblyBuilder.Build(withNativeCalls: false));
			File.WriteAllText(Path.Combine(root, "single.py"), "from System.Windows.Forms import *\nButton()\n");
			File.WriteAllText(Path.Combine(root, "ignored.txt"), "from System.Windows.Forms import *\nButton()\n");
			string package = Path.Combine(root, "beta.crplugin");
			using (ZipArchive zip = ZipFile.Open(package, ZipArchiveMode.Create))
			{
				Add(zip, "package.ini", "[Package]\nName=beta\n");
				Add(zip, "Script.py", "import clr\nclr.AddReferenceToFileAndPath('C:\\\\x\\\\Helper.dll')\nfrom System.Windows.Forms import *\nForm()\n");
				Add(zip, "html/page.html", "<script>var a = window.external.Value;</script>");
				ZipArchiveEntry dll = zip.CreateEntry("Helper.dll");
				using (Stream s = dll.Open())
				{
					byte[] bytes = ScanAssemblyBuilder.Build(withNativeCalls: false);
					s.Write(bytes, 0, bytes.Length);
				}
			}

			ScanReport report = Scanner().Scan(new[] { root });
			Assert.Equal(new[] { "alpha/a.py", "alpha/lib.dll", "alpha/sub/b.py", "beta.crplugin!/Helper.dll", "beta.crplugin!/Script.py", "beta.crplugin!/html/page.html", "single.py" }, report.Units.Select(u => u.Path).ToArray());
			Assert.Equal(new[] { "alpha", "alpha", "alpha", "beta", "beta", "beta", "single" }, report.Units.Select(u => u.Package).ToArray());
			Assert.Equal(3, report.Packages);
			TypeUsage form = Assert.Single(report.Types, t => t.Name == "Form");
			// Three scripts and the two assemblies (which derive from Form) use it, in two plugins.
			Assert.Equal(5, form.Scripts);
			Assert.Equal(2, form.Packages);
			Assert.Contains(report.Units, u => u.Path == "beta.crplugin!/html/page.html" && u.Findings.Any(f => f.Kind == "html-sync-property"));
		}

		private static void Add(ZipArchive zip, string name, string text)
		{
			ZipArchiveEntry e = zip.CreateEntry(name);
			using var w = new StreamWriter(e.Open(), Encoding.Latin1);
			w.Write(text);
		}

		[Fact]
		public void A_reference_to_an_assembly_that_ships_with_the_package_is_bundled_not_missing()
		{
			string root = Path.Combine(temp.Path, "p");
			Directory.CreateDirectory(Path.Combine(root, "plug"));
			File.WriteAllText(Path.Combine(root, "plug", "s.py"), "import clr\nclr.AddReferenceToFileAndPath('/x/cYo.Custom.Helper.dll')\nclr.AddReference('cYo.Other.Missing')\n");
			File.WriteAllBytes(Path.Combine(root, "plug", "cYo.Custom.Helper.dll"), ScanAssemblyBuilder.Build(withNativeCalls: false));
			ScanUnit s = Scanner().Scan(new[] { root }).Units.Single(u => u.Kind == "python");
			Assert.Contains(s.References, r => r.Name == "cYo.Custom.Helper" && r.Kind == "bundled");
			Assert.Contains(s.References, r => r.Name == "cYo.Other.Missing" && r.Kind == "core-missing");
			Assert.DoesNotContain(s.Findings, f => f.Symbol == "cYo.Custom.Helper");
			Assert.Contains(s.Findings, f => f.Kind == "missing-assembly" && f.Symbol == "cYo.Other.Missing" && f.Line == 3);
		}

		[Fact]
		public void An_assembly_of_the_dotnet_framework_that_dotnet_10_lacks_is_a_warning()
		{
			ScanUnit u = Python("import clr\nclr.AddReference('System.Web.Extensions')\nclr.AddReference('System.Xml')\nclr.AddReference('System.Core')\n");
			Assert.Contains(u.References, r => r.Name == "System.Web.Extensions" && r.Kind == "framework-only");
			Assert.Contains(u.References, r => r.Name == "System.Xml" && r.Kind == "framework");
			ScanFinding f = Assert.Single(u.Findings);
			Assert.Equal("framework-only-assembly", f.Kind);
			Assert.Equal("warning", f.Severity);
			Assert.Equal(2, f.Line);
			Assert.Equal("none", u.Tier);
		}

		[Fact]
		public void The_whole_python_2_standard_library_parses_and_most_of_it_has_nothing_to_report()
		{
			string lib = Path.Combine(AppContext.BaseDirectory, "Lib");
			Assert.True(Directory.Exists(lib), "The Python standard library (IronPython.StdLib) is copied next to the test assembly: " + lib);
			ScanReport report = Scanner().Scan(new[] { lib });
			Assert.True(report.Units.Count > 400, "units: " + report.Units.Count);
			Assert.All(report.Units, u => Assert.True(u.Parsed, u.Path + ": " + u.Error));
			Assert.Empty(report.Types);
			// The Windows only parts of the library (msvcrt, _subprocess, the registry) sit behind platform checks or optional imports for the most part.
			Assert.True(report.Units.Count(u => u.Tier == "C") < 10, string.Join(", ", report.Units.Where(u => u.Tier == "C").Select(u => u.Path)));
		}

		[Fact]
		public void The_json_report_is_stable_and_carries_the_ranked_tables_and_findings()
		{
			string root = Path.Combine(temp.Path, "j");
			Directory.CreateDirectory(root);
			File.WriteAllText(Path.Combine(root, "a.py"), "from System.Windows.Forms import *\nimport ctypes\nButton().Text = 'x'\nctypes.windll.user32.MessageBeep(0)\n");
			File.WriteAllText(Path.Combine(root, "b.py"), "from System.Windows.Forms import *\nButton()\n");
			string first = Scanner().Scan(new[] { root }).ToJson();
			string second = Scanner().Scan(new[] { root }).ToJson();
			Assert.Equal(first, second);
			using JsonDocument doc = JsonDocument.Parse(first);
			JsonElement r = doc.RootElement;
			Assert.Equal(1, r.GetProperty("schema").GetInt32());
			Assert.Equal(2, r.GetProperty("summary").GetProperty("units").GetInt32());
			Assert.Equal(1, r.GetProperty("summary").GetProperty("tiers").GetProperty("C").GetInt32());
			JsonElement button = r.GetProperty("types").EnumerateArray().Single(t => t.GetProperty("name").GetString() == "Button");
			Assert.Equal(2, button.GetProperty("scripts").GetInt32());
			Assert.Equal("A", button.GetProperty("tier").GetString());
			JsonElement ctor = r.GetProperty("members").EnumerateArray().Single(m => m.GetProperty("key").GetString() == "Button..ctor");
			Assert.Equal(2, ctor.GetProperty("scripts").GetInt32());
			Assert.Equal("constructor", ctor.GetProperty("kind").GetString());
			JsonElement unit = r.GetProperty("units").EnumerateArray().Single(u => u.GetProperty("path").GetString() == "a.py");
			JsonElement finding = unit.GetProperty("findings").EnumerateArray().First(f => f.GetProperty("severity").GetString() == "error");
			Assert.Equal("user32.MessageBeep", finding.GetProperty("symbol").GetString());
			Assert.Equal(4, finding.GetProperty("line").GetInt32());
		}

		[Fact]
		public void A_missing_path_is_an_error()
		{
			Assert.Throws<FileNotFoundException>(() => Scanner().Scan(new[] { Path.Combine(temp.Path, "nothing-here") }));
		}
	}
}
