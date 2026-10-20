using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace ComicRack.Cli.Tests
{
	/// <summary>
	/// mac/tools/designer-dump/designer-dump.py and mac/tools/ledger-check/ledger-check.py (P5.T3). Both read original files only. The
	/// ledger check is tested on synthetic ledgers, plans and Designer files in a temp folder (LEDGER, PLAN and ORIGINAL_ROOT redirect its
	/// inputs); the real ledger is checked by the Accept command and by verify.sh.
	/// </summary>
	public class ParityToolTests : IDisposable
	{
		private static readonly string Dump = Tools.Path_("mac/tools/designer-dump/designer-dump.py");
		private static readonly string Check = Tools.Path_("mac/tools/ledger-check/ledger-check.py");
		private readonly string temp = Path.Combine(Path.GetTempPath(), "parity-tools-" + Guid.NewGuid().ToString("N"));

		public ParityToolTests()
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

		private static (int code, string output, string error) Py(string script, IEnumerable<string> args, IDictionary<string, string> env = null) =>
			Tools.Run("python3", new[] { "-I", script }.Concat(args), env: env);

		private string Write(string relative, string text)
		{
			string path = Path.Combine(temp, relative);
			Directory.CreateDirectory(Path.GetDirectoryName(path));
			File.WriteAllText(path, text);
			return path;
		}

		// ---------------------------------------------------------------- designer-dump

		private const string Designer = @"
namespace Demo {
	partial class DemoDialog {
		private System.ComponentModel.IContainer components = null;
		private void InitializeComponent() {
			this.components = new System.ComponentModel.Container();
			this.mainMenu = new System.Windows.Forms.MenuStrip();
			this.fileMenu = new System.Windows.Forms.ToolStripMenuItem();
			this.miOpen = new System.Windows.Forms.ToolStripMenuItem();
			this.sep = new System.Windows.Forms.ToolStripSeparator();
			this.miExit = new System.Windows.Forms.ToolStripMenuItem();
			this.panel = new System.Windows.Forms.Panel();
			this.btOK = new System.Windows.Forms.Button();
			this.cbMode = new System.Windows.Forms.ComboBox();
			this.timer = new System.Windows.Forms.Timer(this.components);
			//
			// mainMenu
			//
			this.mainMenu.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
			this.fileMenu});
			this.mainMenu.Name = ""mainMenu"";
			this.fileMenu.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
			this.miOpen,
			this.sep,
			this.miExit});
			this.fileMenu.Name = ""fileMenu"";
			this.fileMenu.Text = ""&File"";
			this.miOpen.Image = global::Demo.Properties.Resources.Open;
			this.miOpen.Name = ""miOpen"";
			this.miOpen.ShortcutKeys = ((System.Windows.Forms.Keys)((System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.O)));
			this.miOpen.Text = ""&Open File..."";
			this.miOpen.Click += new System.EventHandler(this.miOpen_Click);
			this.miExit.ShortcutKeys = ((System.Windows.Forms.Keys)(((System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.Shift) 
			| System.Windows.Forms.Keys.F5)));
			this.miExit.Text = ""E&xit"";
			this.panel.Controls.Add(this.btOK);
			this.panel.Controls.Add(this.cbMode);
			this.panel.Dock = System.Windows.Forms.DockStyle.Fill;
			this.panel.Location = new System.Drawing.Point(0, 24);
			this.panel.Size = new System.Drawing.Size(300, 200);
			this.btOK.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
			this.btOK.Location = new System.Drawing.Point(200, 170);
			this.btOK.Text = ""&OK"";
			this.btOK.TabIndex = 3;
			this.cbMode.Items.AddRange(new object[] {
			""Single"",
			""Double; two""});
			this.cbMode.Text = ""long "" + ""text"";
			this.Controls.Add(this.panel);
			this.Controls.Add(this.mainMenu);
			this.ClientSize = new System.Drawing.Size(300, 224);
			this.Text = ""Demo"";
		}
	}
}";

		[Fact]
		public void The_dump_of_a_small_designer_shows_the_tree_with_names_texts_geometry_keys_and_images()
		{
			string file = Write("DemoDialog.Designer.cs", Designer);
			var r = Py(Dump, new[] { file, "--no-tr" });
			Assert.True(r.code == 0, r.output + r.error);
			string[] lines = r.output.Split('\n');
			Assert.Contains("# DemoDialog", lines[0]);
			Assert.Contains(lines, l => l.Contains("form") && l.Contains("clientSize=(300,224)") && l.Contains("text=\"Demo\""));
			// the panel comes first in the Controls order and holds the two controls; the menu strip is the second root
			int panel = Array.FindIndex(lines, l => l.TrimStart().StartsWith("Panel panel"));
			Assert.True(panel > 0, r.output);
			Assert.Contains("dock=Fill", lines[panel]);
			Assert.Contains("at (0,24)", lines[panel]);
			Assert.Contains("size (300,200)", lines[panel]);
			Assert.StartsWith("    Button btOK", lines[panel + 1]);
			Assert.Contains("anchor=BR", lines[panel + 1]);
			Assert.Contains("\"&OK\"", lines[panel + 1]);
			Assert.Contains("tab=3", lines[panel + 1]);
			Assert.Contains("items=[Single, Double; two]", lines[panel + 2]);
			Assert.Contains("\"long text\"", lines[panel + 2]);
			int menu = Array.FindIndex(lines, l => l.TrimStart().StartsWith("MenuStrip mainMenu"));
			Assert.True(menu > panel);
			Assert.StartsWith("    ToolStripMenuItem fileMenu", lines[menu + 1]);
			Assert.Contains(lines, l => l.Contains("ToolStripMenuItem miOpen") && l.Contains("keys=Ctrl+O") && l.Contains("image=Open") && l.Contains("on:miOpen_Click"));
			Assert.Contains(lines, l => l.Contains("ToolStripMenuItem miExit") && l.Contains("keys=Ctrl+Shift+F5"));
			Assert.Contains(lines, l => l.TrimStart().StartsWith("---"));
			Assert.Contains(lines, l => l.StartsWith("components:") && l.Contains("Timer timer"));
		}

		[Fact]
		public void Translation_keys_are_marked_by_the_section_and_whether_the_culture_file_has_them()
		{
			string file = Write("DemoDialog.Designer.cs", Designer);
			string languages = Path.Combine(temp, "de");
			Directory.CreateDirectory(languages);
			File.WriteAllText(Path.Combine(languages, "DemoDialog.xml"), "<TR><Texts><Text Key=\"miOpen\" Text=\"x\" /></Texts></TR>");
			var r = Py(Dump, new[] { file, "--languages", languages });
			Assert.Equal(0, r.code);
			Assert.Contains(r.output.Split('\n'), l => l.Contains("miOpen") && l.Contains("tr:DemoDialog/miOpen"));
			Assert.Contains(r.output.Split('\n'), l => l.Contains("btOK") && l.Contains("tr?DemoDialog/btOK"));
			Assert.DoesNotContain("tr:", Py(Dump, new[] { file, "--no-tr" }).output);
		}

		[Fact]
		public void The_dump_of_the_preferences_dialog_lists_its_five_panes()
		{
			var r = Py(Dump, new[] { Tools.Path_("ComicRack/Dialogs/PreferencesDialog.Designer.cs") });
			Assert.Equal(0, r.code);
			foreach (string pane in new[] { "pageReader", "pageLibrary", "pageBehavior", "pageScripts", "pageAdvanced" })
			{
				Assert.Contains(r.output.Split('\n'), l => l.TrimStart().StartsWith("Panel " + pane));
			}
			foreach (string tab in new[] { "Reader", "Libraries", "Behavior", "Scripts", "Advanced" })
			{
				Assert.Contains(r.output.Split('\n'), l => l.TrimStart().StartsWith("CheckBox tab" + tab) && l.Contains("\"" + tab + "\""));
			}
			Assert.True(r.output.Split('\n').Count(l => l.Contains("Reader") || l.Contains("Libraries") || l.Contains("Behavior") || l.Contains("Scripts") || l.Contains("Advanced")) >= 5);
			// the dump never changes the original
			Assert.Equal("", Tools.Git(Tools.Repo, "status", "--porcelain", "ComicRack").Trim());
		}

		[Fact]
		public void The_dump_of_the_main_form_reads_menus_with_shortcuts_and_images()
		{
			var r = Py(Dump, new[] { Tools.Path_("ComicRack/MainForm.Designer.cs"), "--no-tr" });
			Assert.Equal(0, r.code);
			Assert.Contains(r.output.Split('\n'), l => l.Contains("ToolStripMenuItem miOpenComic") && l.Contains("keys=Ctrl+O") && l.Contains("image=Open") && l.Contains("\"&Open File...\""));
			Assert.Contains(r.output.Split('\n'), l => l.Contains("ToolStripSplitButton tbPrevPage"));
		}

		[Fact]
		public void The_dump_json_has_the_same_tree()
		{
			string file = Write("DemoDialog.Designer.cs", Designer);
			var r = Py(Dump, new[] { file, "--json" });
			Assert.Equal(0, r.code);
			using (var doc = System.Text.Json.JsonDocument.Parse(r.output))
			{
				var children = doc.RootElement.GetProperty("children");
				Assert.Equal("panel", children[0].GetProperty("name").GetString());
				Assert.Equal("btOK", children[0].GetProperty("children")[0].GetProperty("name").GetString());
			}
		}

		[Fact]
		public void The_dump_needs_one_existing_file()
		{
			Assert.Equal(2, Py(Dump, Array.Empty<string>()).code);
			Assert.Equal(2, Py(Dump, new[] { Path.Combine(temp, "missing.Designer.cs") }).code);
			string empty = Write("Empty.Designer.cs", "partial class Empty { }");
			Assert.Equal(1, Py(Dump, new[] { empty }).code);
		}

		// ---------------------------------------------------------------- ledger-check

		private const string Plan = "# Plan\n\n## 7. Tasks\n\n#### P5.1 Settings\n- Status: todo\n\n#### P5.R1 Restyle\n- Status: todo\n\n";

		private const string Header = "| ID | Feature | Original location | Win → Mac | Mac task | Status | Deviation / note |\n|---|---|---|---|---|---|---|\n";

		private const string DesignerItems = @"
partial class A {
	void InitializeComponent() {
		this.miOpen = new System.Windows.Forms.ToolStripMenuItem();
		this.miSave = new System.Windows.Forms.ToolStripMenuItem();
		this.tbGo = new System.Windows.Forms.ToolStripSplitButton();
		this.tbNote = new System.Windows.Forms.ToolStripButton();
		this.cbNotAnItem = new System.Windows.Forms.ComboBox();
	}
}";

		private (int code, string output, string error) RunCheck(string ledger, params string[] args)
		{
			Write("ComicRack/Dialogs/A.Designer.cs", DesignerItems);
			string plan = Write("plan.md", Plan);
			string ledgerFile = Write("ledger.md", "# Ledger\n\n## Menu\n\n" + Header + ledger);
			var env = new Dictionary<string, string> { ["LEDGER"] = ledgerFile, ["PLAN"] = plan, ["ORIGINAL_ROOT"] = temp };
			return Py(Check, args, env);
		}

		private const string GoodRows =
			"| MF.01 | Open | File `miOpen` | Ctrl+O → Cmd+O | P5.1 | todo | |\n" +
			"| MF.02 | Save | File `miSave`, `tbGo*`, `tbNote` | Ctrl+S → Cmd+S | P5.R1 | done 2026-10-20 | |\n" +
			"| MF.03 | Quit | File | Ctrl+Q → Cmd+Q | P5.1 | deviation | native: lives in the app menu |\n" +
			"| MF.04 | Later | — | — | — | deferred (D4) | |\n" +
			"| MF.05 | Mac | macOS | Cmd+M | P5.R1 | mac-only | C-MENU-2 |\n";

		[Fact]
		public void A_good_ledger_passes_and_prefix_patterns_cover_names()
		{
			var r = RunCheck(GoodRows);
			Assert.True(r.code == 0, r.output + r.error);
			Assert.Contains("OK, 5 rows, 4 menu and toolbar items covered", r.output);
		}

		[Fact]
		public void An_item_without_a_row_is_named_and_makes_the_check_fail()
		{
			var r = RunCheck(GoodRows.Replace("`miSave`, ", ""));
			Assert.Equal(1, r.code);
			Assert.Contains("FAIL coverage: ToolStripMenuItem miSave", r.output);
			Assert.DoesNotContain("miOpen", r.output);
		}

		[Fact]
		public void Format_rules_are_reported_one_by_one()
		{
			string bad =
				"| MF.01 | Open | `miOpen`, `miSave`, `tbGo`, `tbNote` | — | P5.1 | todo | |\n" +
				"| MF.01 | Dup | — | — | P5.1 | todo | |\n" +
				"| X1 | Bad id | — | — | P5.1 | todo | |\n" +
				"| MF.03 | Status | — | — | P5.1 | finished | |\n" +
				"| MF.04 | Date | — | — | P5.1 | done yesterday | |\n" +
				"| MF.05 | Dev | — | — | P5.1 | deviation | because |\n" +
				"| MF.06 | Task | — | — | P9.9 | todo | |\n" +
				"| MF.07 | No task | — | — | — | todo | |\n";
			var r = RunCheck(bad);
			Assert.Equal(1, r.code);
			Assert.Contains("MF.01: duplicate ID", r.output);
			Assert.Contains("malformed ID 'X1'", r.output);
			Assert.Contains("MF.03: invalid status 'finished'", r.output);
			Assert.Contains("MF.04: invalid status 'done yesterday'", r.output);
			Assert.Contains("MF.05: a deviation row needs a note that starts with 'impossible:' or 'native:'", r.output);
			Assert.Contains("MF.06: Mac task P9.9 is not a task", r.output);
			Assert.Contains("MF.07: a todo row must name the Mac task", r.output);
		}

		[Fact]
		public void The_report_counts_rows_per_area_and_status()
		{
			var r = RunCheck(GoodRows, "--report");
			Assert.Equal(0, r.code);
			string line = r.output.Split('\n').First(l => l.StartsWith("MF"));
			Assert.Matches(@"MF\s+Menu\s+5\s+1\s+1\s+1\s+1\s+1", line);
			Assert.Contains(r.output.Split('\n'), l => l.StartsWith("all") && System.Text.RegularExpressions.Regex.IsMatch(l, @"all\s+5\s+1\s+1\s+1\s+1\s+1"));
		}

		[Fact]
		public void A_menu_dump_is_compared_with_the_labels_and_shortcuts_of_the_done_rows()
		{
			string rows =
				"| MF.01 | Open | File `miOpen` | Ctrl+O → Cmd+O | P5.1 | done 2026-10-20 | |\n" +
				"| MF.02 | Save | File `miSave`, `tbGo`, `tbNote` | Ctrl+S → Option+Cmd+S, alias Cmd+Shift+S | P5.R1 | done 2026-10-20 | |\n" +
				"| MF.03 | Not yet | File `miOpen` | — | P5.1 | todo | |\n";
			string good = Write("menu-good.txt", "## File\nOpen\tCmd+O\t\ttrue\n-\nSave\tShift+Cmd+S\t\ttrue\nHide ComicRack\tCmd+H\t\ttrue\n");
			var ok = RunCheck(rows, "--menus", good);
			Assert.True(ok.code == 0, ok.output + ok.error);
			Assert.Contains("menus: 2 done rows compared, 0 mismatches, 1 dump items not named by a row", ok.output);

			string wrong = Write("menu-wrong.txt", "## File\nOpen\tCmd+P\t\ttrue\n");
			var bad = RunCheck(rows, "--menus", wrong);
			Assert.Equal(1, bad.code);
			Assert.Contains("MF.01 Open: the menu shows Cmd+P, the ledger wants Cmd+O", bad.output);
			Assert.Contains("MF.02 Save is done but the menu dump has no item 'Save'", bad.output);
		}

		[Fact]
		public void An_azerty_dump_is_read_with_the_digits_of_the_key_positions()
		{
			string rows = "| MD.03 | Fit All | Display `miFitAll`, `miOpen`, `miSave`, `tbGo`, `tbNote` | Ctrl+2 → Cmd+2 | P5.R1 | done 2026-10-20 | |\n";
			string dump = Write("menu-azerty.txt", "## Display\nFit All\tCmd+É\t\ttrue\n");
			var plain = RunCheck(rows, "--menus", dump);
			Assert.Equal(1, plain.code);
			Assert.Contains("the menu shows Cmd+É", plain.output);
			var azerty = RunCheck(rows, "--menus", dump, "--azerty");
			Assert.True(azerty.code == 0, azerty.output + azerty.error);
		}

		[Fact]
		public void Bad_arguments_and_missing_inputs_exit_with_2()
		{
			Assert.Equal(2, RunCheck(GoodRows, "--bogus").code);
			Assert.Equal(2, RunCheck(GoodRows, "--menus").code);
			Assert.Equal(2, RunCheck(GoodRows, "--menus", Path.Combine(temp, "missing.txt")).code);
			var env = new Dictionary<string, string> { ["LEDGER"] = Path.Combine(temp, "nope.md") };
			Assert.Equal(2, Py(Check, Array.Empty<string>(), env).code);
		}

		[Fact]
		public void The_real_ledger_passes_and_the_gate_calls_the_check()
		{
			var r = Py(Check, Array.Empty<string>());
			Assert.True(r.code == 0, r.output + r.error);
			Assert.Contains("ledger-check.py", File.ReadAllText(Tools.Path_("mac/tools/verify.sh")));
		}
	}
}
