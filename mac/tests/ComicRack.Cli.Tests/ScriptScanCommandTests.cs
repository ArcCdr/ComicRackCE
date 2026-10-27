using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace ComicRack.Cli.Tests
{
	/// <summary>`crcli script-scan` (P4.6a), run in process through Program.Run.</summary>
	public class ScriptScanCommandTests : IDisposable
	{
		private readonly string temp = Path.Combine(Path.GetTempPath(), "crcli-scan-" + Guid.NewGuid().ToString("N"));

		public ScriptScanCommandTests()
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

		private static (int code, string output, string error) Crcli(params string[] args)
		{
			var o = new StringWriter();
			var e = new StringWriter();
			int code = Program.Run(args, o, e);
			return (code, o.ToString(), e.ToString());
		}

		private static string BundledScripts()
		{
			string dir = AppContext.BaseDirectory;
			while (dir != null && !File.Exists(Path.Combine(dir, "ComicRack", "Config", "Settings.cs")))
			{
				dir = Path.GetDirectoryName(dir);
			}
			return Path.Combine(dir ?? throw new InvalidOperationException("repository root not found"), "ComicRack", "Output", "Scripts");
		}

		[Fact]
		public void Scanning_the_bundled_samples_lists_the_forms_types_with_counts_and_writes_the_json()
		{
			string json = Path.Combine(temp, "out", "scan.json");
			var r = Crcli("script-scan", BundledScripts(), "--json", json, "--top", "200");
			Assert.True(r.code == 0, r.output + r.error);
			Assert.Matches(@"type\t\d+\t\d+\t\d+\tA\tForm\n", r.output);
			Assert.Matches(@"type\t\d+\t\d+\t\d+\tA\tButton\n", r.output);
			Assert.Matches(@"type\t\d+\t\d+\t\d+\tA\tMessageBox\n", r.output);
			Assert.Matches(@"type\t\d+\t\d+\t\d+\tA\tSaveFileDialog\n", r.output);
			Assert.Contains("script-scan: 7 units (7 python, 0 assemblies, 0 html)", r.output);
			Assert.Contains("json: " + json, r.output);
			using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(json));
			var types = doc.RootElement.GetProperty("types").EnumerateArray().ToDictionary(t => t.GetProperty("name").GetString(), t => t.GetProperty("scripts").GetInt32());
			Assert.True(types["Form"] >= 3);
			Assert.True(types["Button"] >= 3);
			Assert.True(types["MessageBox"] >= 1);
			Assert.True(types["SaveFileDialog"] >= 1);
			Assert.Equal(7, doc.RootElement.GetProperty("units").GetArrayLength());
		}

		[Fact]
		public void A_tier_C_finding_is_printed_and_fail_on_error_turns_it_into_exit_code_1()
		{
			string file = Path.Combine(temp, "bad.py");
			File.WriteAllText(file, "import ctypes\nctypes.windll.user32.MessageBeep(0)\n");
			var plain = Crcli("script-scan", file);
			Assert.Equal(0, plain.code);
			Assert.Matches(@"error\tbad.py\t2\twindows-library\tUses Windows-only API `user32.MessageBeep` at line 2; this script cannot run on macOS", plain.output);
			var strict = Crcli("script-scan", file, "--fail-on-error");
			Assert.Equal(1, strict.code);
			string good = Path.Combine(temp, "good.py");
			File.WriteAllText(good, "x = 1\n");
			Assert.Equal(0, Crcli("script-scan", good, "--fail-on-error").code);
		}

		[Fact]
		public void Usage_errors_exit_with_2()
		{
			var none = Crcli("script-scan");
			Assert.Equal(2, none.code);
			Assert.Contains("script-scan", none.error);
			var missing = Crcli("script-scan", Path.Combine(temp, "nope"));
			Assert.Equal(2, missing.code);
			Assert.Contains("Nothing to scan", missing.error);
		}

		[Fact]
		public void The_exclude_file_and_the_markdown_report_are_applied_and_the_report_names_the_command()
		{
			string root = Path.Combine(temp, "corpus");
			Directory.CreateDirectory(Path.Combine(root, "plug", "tools"));
			File.WriteAllText(Path.Combine(root, "plug", "a.py"), "from System.Windows.Forms import *\nForm()\n");
			File.WriteAllText(Path.Combine(root, "plug", "tools", "b.py"), "from System.Windows.Forms import *\nForm()\nButton()\n");
			string list = Path.Combine(temp, "exclude.txt");
			File.WriteAllText(list, "# tools are not part of the plugin\nplug/tools\n");
			string md = Path.Combine(temp, "out", "usage.md");
			var r = Crcli("script-scan", root, "--exclude-file", list, "--markdown", md);
			Assert.Equal(0, r.code);
			Assert.Contains("script-scan: 1 units", r.output);
			string text = File.ReadAllText(md);
			Assert.StartsWith("# Script compatibility usage", text);
			Assert.Contains("--exclude-file " + list.Replace('\\', '/') + " --markdown " + md.Replace('\\', '/'), text);
			Assert.Contains("| plug | A | 1 |", text);
			Assert.DoesNotContain("Button", text);
			Assert.DoesNotContain("\r", text);
			Assert.Equal(2, Crcli("script-scan", root, "--exclude-file", Path.Combine(temp, "nope.txt")).code);
		}
	}
}
