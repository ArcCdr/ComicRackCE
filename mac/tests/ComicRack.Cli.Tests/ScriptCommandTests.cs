using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ComicRack.Cli.Fixtures;
using Xunit;

namespace ComicRack.Cli.Tests
{
	/// <summary>`crcli script run`, `script expr` and the script aware `smartlist` (P4.3), run in process through Program.Run.</summary>
	public class ScriptCommandTests : IDisposable
	{
		private readonly string temp = Path.Combine(Path.GetTempPath(), "crcli-script-" + Guid.NewGuid().ToString("N"));

		public ScriptCommandTests()
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

		private static string RepoRoot()
		{
			string dir = AppContext.BaseDirectory;
			while (dir != null && !File.Exists(Path.Combine(dir, "ComicRack", "Config", "Settings.cs")))
			{
				dir = Path.GetDirectoryName(dir);
			}
			return dir ?? throw new InvalidOperationException("repository root not found");
		}

		private static string TestScripts => Path.Combine(RepoRoot(), "mac", "tests", "ComicRack.Mac.Host.Tests", "Scripting", "TestScripts");

		/// <summary>A database of a few generated books, made by a scan.</summary>
		private string Database()
		{
			var env = new FixtureEnvironment { FixturesRoot = Path.Combine(temp, "fixtures"), BudgetBytes = FixtureEnvironment.DefaultBudgetBytes, PrintDiskFree = false, SevenZip = null };
			string dir = Path.Combine(env.FixturesRoot, "small");
			Assert.Equal(0, FixtureCommands.Run(new[] { "make", dir, "--books", "4", "--pages", "3" }, new StringWriter(), new StringWriter(), env));
			string db = Path.Combine(temp, "scanned.xml");
			var r = Crcli("scan", dir, "--db", db);
			Assert.True(r.code == 0, r.output + r.error);
			return db;
		}

		[Fact]
		public void Run_calls_a_books_script_on_the_books_of_a_database_and_prints_its_output()
		{
			string db = Database();
			string script = Path.Combine(temp, "count.py");
			File.WriteAllText(script, "class Counter(object):\n\tdef __init__(self):\n\t\tself.n = 0\n#@Name Count\n#@Hook Books\ndef Count(books):\n\tc = Counter()\n\tfor b in books:\n\t\tc.n += 1\n\tprint 'counted', c.n\n");
			var r = Crcli("script", "run", script, "--hook", "Books", "--db", db);
			Assert.True(r.code == 0, r.output + r.error);
			Assert.Contains("counted 4", r.output);
			Assert.Contains("script Books Count: ran on 4 books", r.output);
		}

		[Fact]
		public void Run_reports_a_failing_script_with_exit_code_1_and_a_bad_hook_or_file_as_usage_errors()
		{
			string script = Path.Combine(temp, "bad.py");
			File.WriteAllText(script, "#@Name Bad\n#@Hook Startup\ndef Bad():\n\traise ValueError('on purpose')\n");
			var r = Crcli("script", "run", script, "--hook", "Startup");
			Assert.Equal(1, r.code);
			Assert.Contains("on purpose", r.error);

			Assert.Equal(1, Crcli("script", "run", script, "--hook", "ComicInfoUI").code);
			Assert.Equal(2, Crcli("script", "run", script).code);
			Assert.Equal(2, Crcli("script", "run", Path.Combine(temp, "missing.py"), "--hook", "Books").code);
			Assert.Equal(2, Crcli("script").code);
		}

		[Fact]
		public void Run_calls_search_and_parse_hooks_with_their_arguments()
		{
			string script = Path.Combine(temp, "hooks.py");
			File.WriteAllText(script, "import clr\nfrom System.Collections.Generic import *\n#@Name Searcher\n#@Hook NetSearch\ndef Searcher(hint, text, limit):\n\td = Dictionary[str, str]()\n\td.Add(text, hint + str(limit))\n\treturn d\n#@Name Parser\n#@Hook ParseComicPath\ndef Parser(path, proposed):\n\tprint 'path', path, proposed.Series\n");
			var r = Crcli("script", "run", script, "--hook", "NetSearch", "--hint", "h", "--text", "t", "--limit", "7");
			Assert.True(r.code == 0, r.output + r.error);
			Assert.Contains("1 results: t = h7", r.output);
			r = Crcli("script", "run", script, "--hook", "ParseComicPath", "--path", "/c/Batman 007.cbz");
			Assert.True(r.code == 0, r.output + r.error);
			Assert.Contains("path /c/Batman 007.cbz Batman", r.output);
		}

		[Fact]
		public void Expr_counts_the_books_a_python_expression_matches()
		{
			string db = Database();
			var all = Crcli("script", "expr", "{Series} != ''", "--db", db);
			Assert.True(all.code == 0, all.output + all.error);
			Assert.Matches(new Regex(@"^matched\t4$", RegexOptions.Multiline), all.output);
			var none = Crcli("script", "expr", "{Series} == 'No such series'", "--db", db, "--show", "3");
			Assert.Matches(new Regex(@"^matched\t0$", RegexOptions.Multiline), none.output);
			Assert.Equal(1, Crcli("script", "expr", "{Series} != ''", "--db", db, "--list", "No such list").code);
		}

		[Fact]
		public void Smartlist_computes_a_script_list_when_its_script_is_there_and_names_it_when_it_is_not()
		{
			string db = Path.Combine(RepoRoot(), "mac", "fixtures", "ComicDb.xml");
			if (!File.Exists(db))
			{
				return; // the fixture library is not on this machine
			}
			string remap = @"S:\media\BD\=" + Path.Combine(RepoRoot(), "mac", "fixtures", "BD") + "/";
			var without = Crcli("smartlist", db, "--all", "--remap", remap);
			Assert.True(without.code == 0, without.error);
			Assert.Matches(new Regex(@"^list\t\s*Missing \(to fix\)\tSmart\t0\tscript not found: findMissingIssues$", RegexOptions.Multiline), without.output);

			var off = Crcli("smartlist", db, "--all", "--remap", remap, "--no-scripts", "--scripts-dir", TestScripts);
			Assert.Matches(new Regex(@"^list\t\s*Missing \(to fix\)\tSmart\t0\trequires scripts$", RegexOptions.Multiline), off.output);

			var with = Crcli("smartlist", db, "--all", "--remap", remap, "--scripts-dir", TestScripts);
			Assert.True(with.code == 0, with.error);
			Match missing = Regex.Match(with.output, @"^list\t\s*Missing \(to fix\)\tSmart\t(\d+)\t(.*)$", RegexOptions.Multiline);
			Match next = Regex.Match(with.output, @"^list\t\s*Next issues to read\tSmart\t(\d+)\t(.*)$", RegexOptions.Multiline);
			Assert.True(missing.Success && next.Success, with.output);
			Assert.Equal(string.Empty, missing.Groups[2].Value.Trim());
			Assert.True(int.Parse(next.Groups[1].Value) > 0, with.output);
		}
	}
}
