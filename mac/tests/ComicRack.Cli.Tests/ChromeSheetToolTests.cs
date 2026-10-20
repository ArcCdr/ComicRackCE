using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace ComicRack.Cli.Tests
{
	/// <summary>
	/// mac/tools/chrome-sheet.sh and its helper chrome_sheet.py (P5.T3), without a window server: the argument checks of the script, and the
	/// reference card, decision list and HTML page of the helper on a synthetic repository in a temp folder. The real shots are made by the
	/// Accept command `mac/tools/chrome-sheet.sh T3 MainWindow` (exit 4 on a locked screen).
	/// </summary>
	public class ChromeSheetToolTests : IDisposable
	{
		private static readonly string Script = Tools.Path_("mac/tools/chrome-sheet.sh");
		private static readonly string Helper = Tools.Path_("mac/tools/chrome-sheet/chrome_sheet.py");
		private readonly string temp = Path.Combine(Path.GetTempPath(), "chrome-sheet-test-" + Guid.NewGuid().ToString("N"));

		public ChromeSheetToolTests()
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

		private string Write(string relative, string text)
		{
			string path = Path.Combine(temp, relative);
			Directory.CreateDirectory(Path.GetDirectoryName(path));
			File.WriteAllText(path, text);
			return path;
		}

		private static (int code, string output, string error) Py(params string[] args) => Tools.Run("python3", new[] { "-I", Helper }.Concat(args));

		/// <summary>A repository with one view spec, a light theme dictionary, an icon map, the decision table and one Designer file.</summary>
		private void SyntheticRepo()
		{
			Write("mac/specs/views/Demo.md",
				"# Demo\n\n## 1. Sources\n\n```\nnot the layout\n```\n\n## 2. Layout\n\n```\nDemo 300 x 200\n  Toolbar 25 high\n```\n\n" +
				"Tokens: `Cr.Window`, `Cr.HighlightBrush`, `Cr.Missing`. Icons: <Open>, <NotAnIcon>, <b>. Decisions C-TB-1 and C-SB-1, C-TB-1 again.\n");
			Write("mac/ComicRack.Mac/Themes/ComicRackTheme.Light.axaml",
				"<ResourceDictionary>\n<Color x:Key=\"Cr.Window\">#FFFFFFFF</Color><SolidColorBrush x:Key=\"Cr.WindowBrush\" Color=\"#FFFFFFFF\" />\n" +
				"<Color x:Key=\"Cr.Highlight\">#FF0078D7</Color>\n<Color x:Key=\"Cr.Control\">#80F0F0F0</Color>\n</ResourceDictionary>");
			string icon = Path.Combine(temp, "ComicRack", "Resources", "Open.png");
			Directory.CreateDirectory(Path.GetDirectoryName(icon));
			File.WriteAllBytes(icon, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=="));
			Write("mac/ComicRack.Mac/Assets/icon-map.json",
				"{ \"format\": 1, \"order\": [ \"ComicRack\" ], \"projects\": { \"ComicRack\": { \"names\": { \"Open\": { \"kind\": \"bitmap\", \"path\": \"ComicRack/Resources/Open.png\" } } } } }");
			Write("mac/plans/chrome-guidelines.md",
				"| ID | Element | Original look | Native macOS | Default | Rationale | Flip cost |\n|---|---|---|---|---|---|---|\n" +
				"| C-TB-1 | Toolbars | ToolStrip | NSToolbar | **O** (Avalonia toolbar, `light|dark` aware) | ComicRack-specific | medium |\n" +
				"| C-SB-1 | Status bar | StatusStrip | — | **O** | ComicRack-specific | — |\n");
			Write("ComicRack/Views/Bare.Designer.cs",
				"partial class Bare { void InitializeComponent() {\n this.panel = new System.Windows.Forms.Panel();\n this.btGo = new System.Windows.Forms.Button();\n" +
				" this.panel.Controls.Add(this.btGo);\n this.btGo.Text = \"Go\";\n this.btGo.Size = new System.Drawing.Size(80, 23);\n this.Controls.Add(this.panel);\n } }");
		}

		[Fact]
		public void The_script_and_the_helper_are_valid_bash_and_python()
		{
			Assert.Equal(0, Tools.Run("/bin/bash", new[] { "-n", Script }).code);
			Assert.Equal(0, Tools.Run("python3", new[] { "-I", "-c", "import ast, sys; ast.parse(open(sys.argv[1]).read())", Helper }).code);
		}

		[Fact]
		public void The_script_checks_its_arguments_before_it_builds_or_launches_anything()
		{
			Assert.Equal(0, Tools.Bash(Script, new[] { "--help" }).code);
			Assert.Contains("chrome-sheet.sh <label>", Tools.Bash(Script, new[] { "--help" }).output);
			Assert.Equal(2, Tools.Bash(Script, Array.Empty<string>()).code);
			Assert.Equal(2, Tools.Bash(Script, new[] { "T3" }).code);
			Assert.Equal(2, Tools.Bash(Script, new[] { "a/b", "MainWindow" }).code);
			Assert.Equal(2, Tools.Bash(Script, new[] { "x y", "--all" }).code);
		}

		[Fact]
		public void The_card_of_a_view_with_a_spec_has_its_layout_tokens_and_icons()
		{
			SyntheticRepo();
			string card = Path.Combine(temp, "card.json");
			var r = Py("card", "Demo", "--out", card, "--root", temp);
			Assert.True(r.code == 0, r.output + r.error);
			Assert.Contains("spec mac/specs/views/Demo.md", r.output);
			using (var doc = JsonDocument.Parse(File.ReadAllText(card)))
			{
				JsonElement root = doc.RootElement;
				Assert.Equal("Demo", root.GetProperty("title").GetString());
				Assert.Contains("mac/specs/views/Demo.md", root.GetProperty("subtitle").GetString());
				// the first code block under a heading that mentions the layout, not the first block of the file
				var lines = root.GetProperty("lines").EnumerateArray().Select(e => e.GetString()).ToArray();
				Assert.Equal(new[] { "Demo 300 x 200", "  Toolbar 25 high" }, lines);
				// Cr.Window and Cr.HighlightBrush resolve (Cr.Missing does not); the alpha of the dictionary is dropped
				var swatches = root.GetProperty("swatches").EnumerateArray().Select(e => (e.GetProperty("name").GetString(), e.GetProperty("color").GetString())).ToArray();
				Assert.Equal(new[] { ("Cr.Window", "#FFFFFF"), ("Cr.Highlight", "#0078D7") }, swatches);
				var icons = root.GetProperty("icons").EnumerateArray().ToArray();
				Assert.Single(icons);
				Assert.Equal("Open", icons[0].GetProperty("name").GetString());
				Assert.True(File.Exists(icons[0].GetProperty("file").GetString()));
			}
		}

		[Fact]
		public void Decisions_come_from_the_spec_and_default_to_the_general_ones()
		{
			SyntheticRepo();
			Assert.Equal("C-SB-1 C-TB-1", Py("decisions", "Demo", "--root", temp).output.Trim());
			Assert.Equal("C-APP-1 C-FONT-1 C-ICON-1", Py("decisions", "Bare", "--root", temp).output.Trim());
		}

		[Fact]
		public void A_view_without_a_spec_gets_an_outline_from_its_designer_file_and_says_so()
		{
			SyntheticRepo();
			string card = Path.Combine(temp, "bare.json");
			var r = Py("card", "Bare", "--out", card, "--root", temp);
			Assert.True(r.code == 0, r.output + r.error);
			Assert.Contains("designer ComicRack/Views/Bare.Designer.cs", r.output);
			using (var doc = JsonDocument.Parse(File.ReadAllText(card)))
			{
				Assert.Contains("No view spec yet", doc.RootElement.GetProperty("subtitle").GetString());
				var lines = doc.RootElement.GetProperty("lines").EnumerateArray().Select(e => e.GetString()).ToArray();
				Assert.Contains(lines, l => l.Contains("Panel panel"));
				Assert.Contains(lines, l => l.Contains("Button btGo") && l.Contains("\"Go\"") && l.Contains("size (80,23)"));
				// default tokens: only those of the dictionary (Window, Control; Highlight is in the default list too)
				Assert.Contains("Cr.Window", doc.RootElement.GetProperty("swatches").EnumerateArray().Select(e => e.GetProperty("name").GetString()));
			}
			// a name with neither spec nor designer still gives a card
			var none = Py("card", "Nothing", "--out", Path.Combine(temp, "none.json"), "--root", temp);
			Assert.Equal(0, none.code);
			Assert.Contains("none", none.output);
		}

		[Fact]
		public void The_real_main_window_card_is_built_from_the_view_spec_of_the_main_form()
		{
			string card = Path.Combine(temp, "main.json");
			var r = Py("card", "MainWindow", "--out", card);
			Assert.True(r.code == 0, r.output + r.error);
			using (var doc = JsonDocument.Parse(File.ReadAllText(card)))
			{
				var lines = doc.RootElement.GetProperty("lines").EnumerateArray().Select(e => e.GetString()).ToArray();
				Assert.Contains(lines, l => l.Contains("mainMenuStrip"));
				Assert.Contains(lines, l => l.Contains("statusStrip"));
			}
		}

		[Fact]
		public void The_html_page_lists_the_images_the_warning_and_the_decisions_with_their_default()
		{
			SyntheticRepo();
			string data = Write("rows.json", "[ { \"view\": \"Demo\", \"reference\": \"Demo-card.png\", \"referenceKind\": \"reference card\", \"light\": \"Demo-light.png\", \"dark\": null, " +
				"\"decisions\": [ \"C-TB-1\", \"C-XX-9\" ], \"notes\": [ \"a <note>\" ], \"warning\": \"The screen was locked & so on\" } ]");
			string page = Path.Combine(temp, "sheet.html");
			var r = Py("html", "--out", page, "--label", "T9", "--data", data, "--root", temp);
			Assert.True(r.code == 0, r.output + r.error);
			string html = File.ReadAllText(page);
			Assert.Contains("<title>Chrome contact sheet T9</title>", html);
			Assert.Contains("<img src=\"Demo-card.png\"", html);
			Assert.Contains("<img src=\"Demo-light.png\"", html);
			Assert.DoesNotContain("Demo-dark.png", html);
			Assert.Contains("The screen was locked &amp; so on", html);
			Assert.Contains("a &lt;note&gt;", html);
			Assert.Contains("<b>C-TB-1</b>: Toolbars (default: O (Avalonia toolbar, `light|dark` aware))", html);
			Assert.Contains("<b>C-XX-9</b>", html); // an unknown id is listed without text, not dropped
		}

		[Fact]
		public void The_helper_rejects_missing_arguments()
		{
			Assert.Equal(2, Py().code);
			Assert.Equal(2, Py("card", "Demo").code);
			Assert.Equal(2, Py("html", "--out", Path.Combine(temp, "x.html")).code);
			Assert.Equal(2, Py("bogus", "Demo").code);
		}
	}
}
