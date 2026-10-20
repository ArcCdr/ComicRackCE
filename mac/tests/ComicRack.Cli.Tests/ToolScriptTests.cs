using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace ComicRack.Cli.Tests
{
	/// <summary>Helpers for the tests of the plan tooling scripts (RV4a): the repository root and a bash runner.</summary>
	internal static class Tools
	{
		public static readonly string Repo = FindRepo();

		private static string FindRepo()
		{
			for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
			{
				if (File.Exists(Path.Combine(dir.FullName, "mac", "tools", "plan-status.sh")))
				{
					return dir.FullName;
				}
			}
			throw new FileNotFoundException("mac/tools/plan-status.sh not found above " + AppContext.BaseDirectory);
		}

		public static string Path_(string relative) => Path.Combine(Repo, relative.Replace('/', Path.DirectorySeparatorChar));

		public static (int code, string output, string error) Run(string file, IEnumerable<string> args, string cwd = null, IDictionary<string, string> env = null, IEnumerable<string> removeEnv = null)
		{
			var psi = new ProcessStartInfo(file) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
			if (cwd != null)
			{
				psi.WorkingDirectory = cwd;
			}
			foreach (string a in args)
			{
				psi.ArgumentList.Add(a);
			}
			if (env != null)
			{
				foreach (var kv in env)
				{
					psi.Environment[kv.Key] = kv.Value;
				}
			}
			if (removeEnv != null)
			{
				foreach (string k in removeEnv)
				{
					psi.Environment.Remove(k);
				}
			}
			using (var p = Process.Start(psi))
			{
				var err = p.StandardError.ReadToEndAsync();
				string o = p.StandardOutput.ReadToEnd();
				p.WaitForExit();
				return (p.ExitCode, o, err.Result);
			}
		}

		public static (int code, string output, string error) Bash(string script, string[] args, string cwd = null, IDictionary<string, string> env = null, IEnumerable<string> removeEnv = null)
		{
			var all = new List<string> { script };
			all.AddRange(args);
			return Run("/bin/bash", all, cwd, env, removeEnv);
		}

		public static string Git(string dir, params string[] args)
		{
			var r = Run("git", new[] { "-C", dir }.Concat(args));
			if (r.code != 0)
			{
				throw new InvalidOperationException("git " + string.Join(' ', args) + ": " + r.error);
			}
			return r.output;
		}
	}

	/// <summary>
	/// mac/tools/plan-status.sh (RV4a): the status table and the next task of the plan are generated from the Status lines, and the run
	/// log is backfilled from git log. Everything runs on a synthetic plan in a temp folder; the real plan is only checked by the
	/// Accept command of the task (a test of it would fail while an orchestrator has a task in state "doing").
	/// </summary>
	public class PlanStatusScriptTests : IDisposable
	{
		private readonly string temp = Path.Combine(Path.GetTempPath(), "plan-status-test-" + Guid.NewGuid().ToString("N"));
		private static readonly string Script = Tools.Path_("mac/tools/plan-status.sh");

		public PlanStatusScriptTests()
		{
			Directory.CreateDirectory(Path.Combine(temp, "mac", "plans"));
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

		private string Plan => Path.Combine(temp, "mac", "plans", "full-port.md");

		private string Log => Path.Combine(temp, "mac", "plans", "run-log.md");

		private const string TableHeader = "| Phase | Tasks | Done | Next todo | Notes |\n|---|---|---|---|---|\n";

		private static string Block(string id, string depends, string status) =>
			"#### " + id + " Title of " + id + "\n- Depends: " + depends + " · Effort: medium · Unattended: yes · Status: " + status + "\n- Goal: x\n\n";

		private void WritePlan(string table, string runState, params string[] blocks)
		{
			var sb = new StringBuilder();
			sb.Append("# Plan\n\nRun state: " + runState + "\n\n" + table + "\n## RESUME\n\ntext\n\n");
			sb.Append("## 6. Execution order\n\n- **Wave A, first:** P0.1, P0.2, P1.1, P1.2.\n- **Wave B:** P2.1 (GO check), P2.2.\n\n---\n\n");
			sb.Append("## 7. Tasks\n\n### P0\n\n");
			foreach (string b in blocks)
			{
				sb.Append(b);
			}
			sb.Append("---\n\n## 8. Mapping\n");
			File.WriteAllText(Plan, sb.ToString());
			File.WriteAllText(Log, "# Run log\n\n| Date time | Task | Result | Commit | Minutes | Note |\n|---|---|---|---|---|---|\n");
		}

		private (int code, string output, string error) Run(params string[] args)
		{
			var all = new List<string> { "--root", temp };
			all.AddRange(args);
			return Tools.Bash(Script, all.ToArray());
		}

		private const string StaleTable =
			"| Phase | Tasks | Done | Next todo | Notes |\n|---|---|---|---|---|\n" +
			"| P0 Safety net | 9 | 9 | P0.9 | my note for P0 |\n" +
			"| P1 Runtime | 0 | 0 | P1.1 | my note for P1 |\n" +
			"| P2 Platform | 0 | 0 | P2.1 | |\n" +
			"| **Total** | **9** | **9** | | estimate in section 10 |\n";

		private void WriteStandardPlan(string runState = "RUNNING (2026-10-20, orchestrator; next task: P0.2)")
		{
			WritePlan(StaleTable, runState,
				Block("P0.1", "none", "done 2026-10-17"),
				Block("P0.2", "P0.1", "todo"),
				Block("P1.1", "none", "done-with-caveat 2026-10-18 (one thing unverified)"),
				Block("P1.2", "P1.1", "todo"),
				Block("P2.1", "P1.2 (GO)", "todo"),
				Block("P2.2", "P2.1, C9", "todo"),
				Block("RV1", "none", "done 2026-10-19"),
				Block("RV2a", "none", "todo"));
		}

		[Fact]
		public void The_script_is_valid_bash_and_python()
		{
			Assert.Equal(0, Tools.Run("/bin/bash", new[] { "-n", Script }).code);
			Assert.Equal(0, Tools.Run("python3", new[] { "-I", "-c", "import ast, sys; ast.parse(open(sys.argv[1]).read())", Tools.Path_("mac/tools/plan-status/plan_status.py") }).code);
		}

		[Fact]
		public void Check_reports_a_table_that_differs_from_the_Status_lines_and_write_fixes_it_keeping_the_notes()
		{
			WriteStandardPlan();
			var check = Run("--check");
			Assert.Equal(1, check.code);
			Assert.Contains("differs from its Status lines", check.output);

			var write = Run("--write");
			Assert.Equal(0, write.code);
			Assert.Equal(0, Run("--check").code);

			string text = File.ReadAllText(Plan);
			// P0: P0.1 done, P0.2 todo; notes kept; Next todo is the first todo of the phase in section 6 order
			Assert.Contains("| P0 Safety net | 2 | 1 | P0.2 | my note for P0 |", text);
			// P1: P1.1 done-with-caveat counts as done
			Assert.Contains("| P1 Runtime | 2 | 1 | P1.2 | my note for P1 |", text);
			// P2: nothing done, next is the first todo in order
			Assert.Contains("| P2 Platform | 2 | 0 | P2.1 | |", text);
			// review fix and upstream tasks get their own row; the total counts everything
			Assert.Matches(new Regex(@"\| RV Review fixes and upstream sync \| 2 \| 1 \| RV2a \|"), text);
			Assert.Contains("| **Total** | **8** | **3** | | estimate in section 10 |", text);
			// RV tasks come first: the next task is the review fix, not P0.2
			Assert.Contains("Run state: RUNNING (2026-10-20, orchestrator; next task: RV2a)", text);
		}

		[Fact]
		public void The_next_task_respects_dependencies_golden_verdicts_and_checkpoints()
		{
			WriteStandardPlan();
			// RV2a first, then P0.2 (its dependency P0.1 is done), then P1.2; P2.1 needs "P1.2 (GO)": a task that is only
			// simulated as done does not satisfy a (GO) dependency, and P2.2 needs the unanswered checkpoint C9.
			var pick = Run("--pick", "6");
			Assert.Equal(0, pick.code);
			Assert.Equal(new[] { "RV2a", "P0.2", "P1.2" }, pick.output.Split('\n', StringSplitOptions.RemoveEmptyEntries));

			// a finished spike with a GO verdict makes P2.1 runnable, an answered checkpoint makes P2.2 runnable
			string text = File.ReadAllText(Plan)
				.Replace("#### P1.2 Title of P1.2\n- Depends: P1.1 · Effort: medium · Unattended: yes · Status: todo", "#### P1.2 Title of P1.2\n- Depends: P1.1 · Effort: medium · Unattended: yes · Status: done 2026-10-20, verdict GO");
			File.WriteAllText(Plan, text);
			File.WriteAllText(Path.Combine(temp, "mac", "plans", "OWNER-ACTIONS.md"), "# Owner\n\n### OA-1: answered C9\n- **Status:** DONE\n");
			var pick2 = Run("--pick", "6");
			Assert.Equal(new[] { "RV2a", "P0.2", "P2.1", "P2.2" }, pick2.output.Split('\n', StringSplitOptions.RemoveEmptyEntries));
		}

		[Fact]
		public void Nothing_runnable_prints_NONE_and_the_run_state_says_so()
		{
			WritePlan(StaleTable, "STOPPED (waiting for C2, 2026-10-20)", Block("P0.1", "none", "done 2026-10-17"));
			Assert.Equal("NONE", Run("--pick", "1").output.Trim());
			Assert.Equal(0, Run("--write").code);
			Assert.Contains("Run state: STOPPED (waiting for C2, 2026-10-20; next task: none runnable)", File.ReadAllText(Plan));
		}

		[Fact]
		public void Bad_arguments_exit_2()
		{
			WriteStandardPlan();
			Assert.Equal(2, Run("--bogus").code);
			Assert.Equal(2, Run("--pick", "x").code);
			Assert.Equal(2, Tools.Bash(Script, new[] { "--plan", Path.Combine(temp, "missing.md"), "--root", temp }).code);
		}

		[Fact]
		public void The_log_is_backfilled_from_the_task_commits_once()
		{
			WriteStandardPlan();
			string repo = Path.Combine(temp, "repo");
			Directory.CreateDirectory(repo);
			Tools.Git(repo, "init", "-q", "-b", "macos");
			Tools.Git(repo, "config", "user.email", "test@example.invalid");
			Tools.Git(repo, "config", "user.name", "test");
			Tools.Git(repo, "config", "commit.gpgsign", "false");
			void Commit(string name, string subject)
			{
				File.WriteAllText(Path.Combine(repo, name), name);
				Tools.Git(repo, "add", "-A");
				Tools.Git(repo, "commit", "-q", "-m", subject);
			}
			Commit("a", "mac: P0.1 First task (it worked)");
			Commit("b", "mac: R1 Old name of a review fix (before the rename)");   // legacy name R1 -> RV1
			Commit("c", "mac: M2.1 an MVP task that is not in the plan");
			Commit("d", "mac: run log P0.1");
			Commit("e", "mac: P1.1 Second | task with a pipe");
			Commit("f", "unrelated commit");

			var first = Tools.Bash(Script, new[] { "--root", repo, "--plan", Plan, "--log", Log, "--backfill-log" });
			Assert.Equal(0, first.code);
			Assert.Contains("3 row(s) appended", first.output);
			string log = File.ReadAllText(Log);
			var rows = log.Split('\n').Where(l => l.StartsWith("| 20")).ToArray();
			Assert.Equal(3, rows.Length);
			Assert.Contains("| P0.1 | done |", rows[0]);
			Assert.Contains("| RV1 | done |", rows[1]);
			Assert.Contains("(commit subject says R1)", rows[1]);
			Assert.Contains("| P1.1 | done-with-caveat |", rows[2]);
			Assert.DoesNotContain("| M2.1 |", log);
			Assert.DoesNotContain("run log", log);
			Assert.Contains("Second / task with a pipe", rows[2]);   // a pipe in the subject must not break the table

			var second = Tools.Bash(Script, new[] { "--root", repo, "--plan", Plan, "--log", Log, "--backfill-log" });
			Assert.Contains("0 row(s) appended", second.output);
			Assert.Equal(0, Tools.Bash(Script, new[] { "--root", repo, "--plan", Plan, "--log", Log, "--check-log" }).code);

			// a new task commit is reported by --check-log (exit 1) and appended by the next backfill
			Commit("g", "mac: P0.2 Third task (done)");
			var missing = Tools.Bash(Script, new[] { "--root", repo, "--plan", Plan, "--log", Log, "--check-log" });
			Assert.Equal(1, missing.code);
			Assert.Contains("P0.2", missing.output);
		}

		[Fact]
		public void A_log_row_that_names_a_task_with_a_non_hash_commit_cell_covers_it()
		{
			WriteStandardPlan();
			File.AppendAllText(Log, "| 2026-10-17 | P0.1 | done | (planning commit) | | written in the planning session |\n");
			string repo = Path.Combine(temp, "repo2");
			Directory.CreateDirectory(repo);
			Tools.Git(repo, "init", "-q", "-b", "macos");
			Tools.Git(repo, "config", "user.email", "test@example.invalid");
			Tools.Git(repo, "config", "user.name", "test");
			Tools.Git(repo, "config", "commit.gpgsign", "false");
			File.WriteAllText(Path.Combine(repo, "a"), "a");
			Tools.Git(repo, "add", "-A");
			Tools.Git(repo, "commit", "-q", "-m", "mac: P0.1 First task");
			var r = Tools.Bash(Script, new[] { "--root", repo, "--plan", Plan, "--log", Log, "--backfill-log" });
			Assert.Contains("0 row(s) appended", r.output);
		}
	}

	/// <summary>mac/tools/claude-allowlist.json (RV4a): scoped, no raw osascript, and it covers every command that RUNBOOK.md has the orchestrator run.</summary>
	public class AllowlistTests
	{
		private static readonly string[] Rules = LoadRules();

		private static string[] LoadRules()
		{
			using (var doc = JsonDocument.Parse(File.ReadAllText(Tools.Path_("mac/tools/claude-allowlist.json"))))
			{
				return doc.RootElement.GetProperty("permissions").GetProperty("allow").EnumerateArray().Select(e => e.GetString()).ToArray();
			}
		}

		/// <summary>The Bash rule bodies: a trailing * is a prefix match, anything else must match exactly.</summary>
		private static bool Allowed(string command)
		{
			command = command.Trim();
			foreach (string rule in Rules)
			{
				if (!rule.StartsWith("Bash(") || !rule.EndsWith(")"))
				{
					continue;
				}
				string body = rule.Substring(5, rule.Length - 6);
				if (body.EndsWith("*") ? command.StartsWith(body.Substring(0, body.Length - 1), StringComparison.Ordinal) : command == body)
				{
					return true;
				}
			}
			return false;
		}

		[Fact]
		public void The_file_is_valid_json_with_a_list_of_rules()
		{
			Assert.NotEmpty(Rules);
			Assert.All(Rules, r => Assert.False(string.IsNullOrWhiteSpace(r)));
		}

		[Fact]
		public void Nothing_is_allowed_that_can_type_push_merge_or_delete_outside_mac()
		{
			Assert.DoesNotContain(Rules, r => r.Contains("osascript"));
			Assert.DoesNotContain("Edit", Rules);
			Assert.DoesNotContain("Write", Rules);
			foreach (string forbidden in new[] { "git push", "git merge ", "git rebase", "git reset", "git checkout", "git clean", "git stash drop", "git stash pop", "git stash clear", "git switch", "git branch", "git restore ." })
			{
				Assert.DoesNotContain(Rules, r => r.Contains(forbidden));
			}
			Assert.DoesNotContain(Rules, r => r.StartsWith("Bash(rm") && r.Contains("*"));
			Assert.DoesNotContain(Rules, r => r.StartsWith("Bash(find") && r.Contains("*"));
			Assert.DoesNotContain("Bash(git add*)", Rules);
			Assert.DoesNotContain("Bash(git stash*)", Rules);
			// git add and git restore only name paths under mac/ (or the two files outside it that a task may change)
			foreach (string r in Rules.Where(r => r.StartsWith("Bash(git add") || r.StartsWith("Bash(git restore")))
			{
				Assert.True(r.Contains(" mac") || r.Contains("MACOS_NATIVE_PATH.md") || r.Contains(".github/workflows/mac-"), r);
			}
		}

		[Fact]
		public void Edit_and_Write_are_scoped_to_mac_and_the_two_allowed_outside_files()
		{
			foreach (string r in Rules.Where(r => r.StartsWith("Edit(") || r.StartsWith("Write(")))
			{
				Assert.True(r.Contains("(/mac/**)") || r.Contains("(/MACOS_NATIVE_PATH.md)") || r.Contains("(/.github/workflows/mac-*.yml)"), r);
			}
			foreach (string tool in new[] { "Edit", "Write" })
			{
				Assert.Contains(tool + "(/mac/**)", Rules);
				Assert.Contains(tool + "(/MACOS_NATIVE_PATH.md)", Rules);
			}
		}

		[Fact]
		public void The_stash_commands_are_exactly_the_three_the_plan_uses()
		{
			Assert.Contains("Bash(git stash push -u -m *)", Rules);
			Assert.Contains("Bash(git stash list)", Rules);
			Assert.Contains("Bash(git stash show *)", Rules);
			Assert.Equal(3, Rules.Count(r => r.StartsWith("Bash(git stash")));
		}

		[Fact]
		public void The_cleanup_commands_are_exact_patterns()
		{
			Assert.Contains("Bash(find mac -type d -name bin -prune -exec rm -rf {} +)", Rules);
			Assert.Contains("Bash(find mac -type d -name obj -prune -exec rm -rf {} +)", Rules);
			foreach (string d in new[] { "verify", "ui-drive", "upstream-sync" })
			{
				Assert.Contains("Bash(rm -rf mac/artifacts/" + d + ")", Rules);
			}
		}

		/// <summary>Splits a shell line at |, &amp;&amp;, ; and || outside quotes.</summary>
		private static IEnumerable<string> Segments(string line)
		{
			var sb = new StringBuilder();
			char quote = '\0';
			for (int i = 0; i < line.Length; i++)
			{
				char c = line[i];
				if (quote != '\0')
				{
					sb.Append(c);
					if (c == quote)
					{
						quote = '\0';
					}
				}
				else if (c == '\'' || c == '"')
				{
					quote = c;
					sb.Append(c);
				}
				else if (c == '#' && (i == 0 || char.IsWhiteSpace(line[i - 1])))
				{
					break;   // a comment
				}
				else if (c == '|' || c == ';' || (c == '&' && i + 1 < line.Length && line[i + 1] == '&'))
				{
					if (c == '&' || (c == '|' && i + 1 < line.Length && line[i + 1] == '|'))
					{
						i++;
					}
					if (sb.ToString().Trim().Length > 0)
					{
						yield return sb.ToString().Trim();
					}
					sb.Clear();
				}
				else
				{
					sb.Append(c);
				}
			}
			if (sb.ToString().Trim().Length > 0)
			{
				yield return sb.ToString().Trim();
			}
		}

		private static string Section(string text, string numberPrefix)
		{
			var m = Regex.Match(text, @"^## " + Regex.Escape(numberPrefix) + @"\. .*?(?=^## \d+\. |\z)", RegexOptions.Singleline | RegexOptions.Multiline);
			Assert.True(m.Success, "RUNBOOK section " + numberPrefix + " not found");
			return m.Value;
		}

		[Fact]
		public void Every_command_the_RUNBOOK_has_the_orchestrator_run_is_allowed()
		{
			string runbook = File.ReadAllText(Tools.Path_("mac/plans/RUNBOOK.md"));
			var commands = new List<string>();
			// fenced bash blocks of sections 2, 4 and 5
			foreach (string number in new[] { "2", "4", "5" })
			{
				string section = Section(runbook, number);
				foreach (Match m in Regex.Matches(section, "```bash\\n(.*?)```", RegexOptions.Singleline))
				{
					foreach (string line in m.Groups[1].Value.Split('\n'))
					{
						string trimmed = line.TrimStart();
						// continuation lines of a comment column start with a #
						if (trimmed.Length == 0 || trimmed.StartsWith("#"))
						{
							continue;
						}
						commands.AddRange(Segments(line));
					}
				}
				// inline code spans that start with a program the orchestrator runs itself
				foreach (Match m in Regex.Matches(section, "`([^`\\n]+)`"))
				{
					string span = m.Groups[1].Value.Trim();
					if (Regex.IsMatch(span, @"^(git|find|rm|df|test|dotnet|python3|mac/tools/|grep)\b|^mac/tools/"))
					{
						commands.AddRange(Segments(span));
					}
				}
			}
			Assert.True(commands.Count > 15, "found only " + commands.Count + " commands in the RUNBOOK");
			// echo only prints a marker word in the model-id check
			var notAllowed = commands.Where(c => !c.StartsWith("echo ") && !Allowed(c)).Distinct().ToList();
			Assert.True(notAllowed.Count == 0, "not covered by mac/tools/claude-allowlist.json:\n" + string.Join("\n", notAllowed));
		}

		[Fact]
		public void The_RUNBOOK_does_not_use_commands_the_allowlist_forbids()
		{
			string runbook = File.ReadAllText(Tools.Path_("mac/plans/RUNBOOK.md"));
			foreach (string number in new[] { "2", "4", "5" })
			{
				string section = Section(runbook, number);
				Assert.DoesNotContain("osascript", section);
				Assert.DoesNotContain("git stash drop", section);
				Assert.DoesNotContain("git reset", section);
				Assert.DoesNotContain("$(", section);
			}
		}
	}

	/// <summary>mac/tools/run-plan.sh, mac/tools/verify.sh and mac/tools/e2e.sh (RV4a).</summary>
	public class LoopScriptTests : IDisposable
	{
		private readonly string temp = Path.Combine(Path.GetTempPath(), "loop-script-test-" + Guid.NewGuid().ToString("N"));

		public LoopScriptTests()
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

		private static readonly string[] TierNames = { "sonnet", "opus", "haiku" };

		[Fact]
		public void No_script_under_mac_tools_holds_a_model_tier_name()
		{
			var files = Directory.GetFiles(Tools.Path_("mac/tools"), "*.sh", SearchOption.AllDirectories)
				.Concat(Directory.GetFiles(Tools.Path_("mac/tools"), "*.py", SearchOption.AllDirectories))
				.Concat(Directory.GetFiles(Tools.Path_("mac/tests/e2e"), "*.sh", SearchOption.AllDirectories))
				.Where(f => !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) && !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
				.ToArray();
			Assert.NotEmpty(files);
			foreach (string f in files)
			{
				string text = File.ReadAllText(f).ToLowerInvariant();
				foreach (string tier in TierNames)
				{
					Assert.False(Regex.IsMatch(text, @"\b" + tier + @"\b"), f + " mentions " + tier);
				}
			}
		}

		[Fact]
		public void The_model_comes_from_the_option_the_environment_or_the_RUNBOOK_in_that_order()
		{
			string script = Tools.Path_("mac/tools/run-plan.sh");
			var env = new Dictionary<string, string>();
			// RUNBOOK default: the name on its "Loop model:" line
			string runbook = File.ReadAllText(Tools.Path_("mac/plans/RUNBOOK.md"));
			string fromRunbook = Regex.Match(runbook, @"^\**Loop model:\**\s*`?([A-Za-z0-9._-]+)`?", RegexOptions.Multiline).Groups[1].Value;
			Assert.NotEmpty(fromRunbook);
			var plain = Tools.Bash(script, new[] { "--dry-run" }, Tools.Repo, env, new[] { "COMICRACK_PLAN_MODEL" });
			Assert.Equal(0, plain.code);
			Assert.Contains("--model " + fromRunbook + " ", plain.output);
			var viaEnv = Tools.Bash(script, new[] { "--dry-run" }, Tools.Repo, new Dictionary<string, string> { ["COMICRACK_PLAN_MODEL"] = "model-from-env" });
			Assert.Contains("--model model-from-env ", viaEnv.output);
			var viaOption = Tools.Bash(script, new[] { "--dry-run", "--model", "model-from-option" }, Tools.Repo, new Dictionary<string, string> { ["COMICRACK_PLAN_MODEL"] = "model-from-env" });
			Assert.Contains("--model model-from-option ", viaOption.output);
		}

		[Fact]
		public void The_dry_run_picks_review_fix_tasks_before_everything_else()
		{
			// The ordering rule lives in plan-status.sh; run-plan.sh asks it.
			string text = File.ReadAllText(Tools.Path_("mac/tools/plan-status/plan_status.py"));
			Assert.Contains(@"RV\d+[a-z]?|U\d{8}\.\d+", text);
			string runPlan = File.ReadAllText(Tools.Path_("mac/tools/run-plan.sh"));
			Assert.Contains("mac/tools/plan-status.sh --pick", runPlan);
		}

		[Fact]
		public void The_gate_never_lets_UPDATE_GOLDENS_reach_the_tests()
		{
			string text = File.ReadAllText(Tools.Path_("mac/tools/verify.sh"));
			int unset = text.IndexOf("unset UPDATE_GOLDENS", StringComparison.Ordinal);
			// the first dotnet call outside a comment line
			int firstDotnet = Regex.Match(text, @"^[^#\n]*\bdotnet ", RegexOptions.Multiline).Index;
			Assert.True(unset > 0, "verify.sh must unset UPDATE_GOLDENS");
			Assert.True(unset < firstDotnet, "the unset must come before the first dotnet call");
			// and it says so when the variable was set
			var r = Tools.Bash(Tools.Path_("mac/tools/verify.sh"), new[] { "--bogus" }, Tools.Repo, new Dictionary<string, string> { ["UPDATE_GOLDENS"] = "1" });
			Assert.Equal(2, r.code);
			Assert.Contains("UPDATE_GOLDENS is set", r.error);
		}

		/// <summary>A folder of fake e2e cases, each one exits with the code given in its name.</summary>
		private string FakeCases(params (string name, int code)[] cases)
		{
			string dir = Path.Combine(temp, "cases-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(dir);
			foreach (var (name, code) in cases)
			{
				File.WriteAllText(Path.Combine(dir, name + ".sh"), "echo \"case " + name + "\"\nexit " + code + "\n");
			}
			return dir;
		}

		private (int code, string output, string error) E2e(string casesDir, params string[] args) =>
			Tools.Bash(Tools.Path_("mac/tools/e2e.sh"), args, Tools.Repo, new Dictionary<string, string> { ["E2E_CASES_DIR"] = casesDir });

		[Fact]
		public void E2e_exits_0_only_when_a_case_passed_and_none_failed()
		{
			Assert.Equal(0, E2e(FakeCases(("a", 0), ("b", 0))).code);
			Assert.Equal(0, E2e(FakeCases(("a", 0), ("b", 4), ("c", 3), ("d", 6))).code);
		}

		[Fact]
		public void E2e_exits_1_when_a_case_failed_even_if_others_were_skipped()
		{
			Assert.Equal(1, E2e(FakeCases(("a", 0), ("b", 1))).code);
			Assert.Equal(1, E2e(FakeCases(("a", 4), ("b", 1))).code);
			Assert.Equal(1, E2e(FakeCases(("a", 7))).code);   // an unknown exit code is a failure
		}

		[Fact]
		public void E2e_exits_4_when_every_case_was_skipped_for_a_locked_screen()
		{
			Assert.Equal(4, E2e(FakeCases(("a", 4), ("b", 4))).code);
		}

		[Fact]
		public void E2e_exits_6_when_nothing_passed_and_a_launch_was_refused()
		{
			var r = E2e(FakeCases(("a", 6), ("b", 4)));
			Assert.Equal(6, r.code);
			Assert.Contains("1 for a refused launch", r.output);
			Assert.Equal(6, E2e(FakeCases(("a", 6), ("b", 3))).code);
		}

		[Fact]
		public void E2e_exits_5_when_nothing_passed_for_any_other_reason()
		{
			var r = E2e(FakeCases(("a", 3), ("b", 3)));
			Assert.Equal(5, r.code);
			Assert.Contains("no case passed", r.error);
			Assert.Equal(5, E2e(FakeCases(("a", 3), ("b", 4))).code);   // missing fixture plus locked screen: still not a pass
		}

		[Fact]
		public void E2e_lib_gives_a_refused_launch_its_own_exit_code()
		{
			string lib = File.ReadAllText(Tools.Path_("mac/tests/e2e/lib.sh"));
			Assert.Contains("6) skip_case 6 ", lib);
			Assert.DoesNotContain("6) skip_case 3", lib);
		}

		[Fact]
		public void The_sync_check_makes_unrecorded_upstream_UI_changes_a_warning()
		{
			string text = File.ReadAllText(Tools.Path_("mac/tools/upstream-sync-check.sh"));
			Assert.Contains("range_verdict ui-parity WARN", text);
			Assert.DoesNotContain("verdict ui-parity INFO \"$n changed Windows UI file(s): parity candidates", text);
			Assert.True(File.Exists(Tools.Path_("mac/plans/upstream-ui-log.md")));
			Assert.Equal(0, Tools.Run("/bin/bash", new[] { "-n", Tools.Path_("mac/tools/upstream-sync-check.sh") }).code);
		}
	}

	/// <summary>The safety checks of ui-drive.sh that need no window server (RV4a): pid ownership, protected data folders, session file quoting.</summary>
	public class UiDriveSafetyTests : IDisposable
	{
		private readonly string temp = Path.Combine(Path.GetTempPath(), "ui-drive-safety-" + Guid.NewGuid().ToString("N"));
		private readonly List<Process> started = new List<Process>();
		private static readonly string Script = Tools.Path_("mac/tools/ui-drive/ui-drive.sh");

		public UiDriveSafetyTests()
		{
			Directory.CreateDirectory(temp);
		}

		public void Dispose()
		{
			foreach (var p in started)
			{
				try
				{
					if (!p.HasExited)
					{
						p.Kill();
					}
				}
				catch (InvalidOperationException)
				{
				}
				p.Dispose();
			}
			try
			{
				Directory.Delete(temp, recursive: true);
			}
			catch (IOException)
			{
			}
		}

		private (int code, string output, string error) Ui(params string[] args) =>
			Tools.Bash(Script, args, Tools.Repo, new Dictionary<string, string> { ["UI_DRIVE_DIR"] = temp }, new[] { "UI_DRIVE_REACTIVATE" });

		private Process Sleeper()
		{
			var psi = new ProcessStartInfo("/bin/sleep", "120") { UseShellExecute = false };
			var p = Process.Start(psi);
			started.Add(p);
			return p;
		}

		private string ExitFile => Path.Combine(temp, "app.exit");

		private void WriteSession(int pid, string exe)
		{
			File.WriteAllText(Path.Combine(temp, "session.env"),
				"UD_PID=" + pid + "\nUD_MODE=custom\nUD_EXE='" + exe + "'\nUD_DATA=" + temp + "\nUD_LOG=" + temp + "/log\nUD_STDOUT=" + temp + "/out\nUD_EXITFILE=" + ExitFile + "\nUD_STARTED=0\n");
		}

		[Fact]
		public void Input_is_refused_with_exit_5_when_the_pid_belongs_to_another_program()
		{
			var other = Sleeper();
			WriteSession(other.Id, "/Applications/Not/The/App");
			foreach (string[] args in new[] { new[] { "key", "cmd+a" }, new[] { "type", "hello" }, new[] { "keycode", "36" }, new[] { "menu", "File", "Open" }, new[] { "click", "5", "5" }, new[] { "choose-path", "/tmp" }, new[] { "quit" }, new[] { "front" } })
			{
				var r = Ui(args);
				Assert.True(r.code == 5, string.Join(" ", args) + " -> " + r.code + " " + r.error);
				Assert.Contains("no longer the app this tool launched", r.error);
			}
			Assert.False(other.HasExited, "no command may signal a pid that is not the launched app");
			Assert.False(File.Exists(Path.Combine(temp, "windowtool")), "the GUI helper must not even be compiled for these refusals");
		}

		[Fact]
		public void Kill_does_not_signal_a_pid_that_is_no_longer_the_app_and_drops_the_session()
		{
			var other = Sleeper();
			WriteSession(other.Id, "/Applications/Not/The/App");
			var r = Ui("kill");
			Assert.Equal(0, r.code);
			Assert.Contains("nothing was killed", r.output);
			Assert.False(other.HasExited);
			Assert.False(File.Exists(Path.Combine(temp, "session.env")));
			Assert.True(File.Exists(Path.Combine(temp, "session.last")));
		}

		[Fact]
		public void Kill_ends_the_process_when_it_is_the_launched_app()
		{
			var app = Sleeper();
			WriteSession(app.Id, "/bin/sleep");   // the command line of the process starts with the executable of the session
			Assert.Contains("running: yes", Ui("status").output);
			var r = Ui("kill");
			Assert.Equal(0, r.code);
			Assert.Contains("killed pid " + app.Id, r.output);
			Assert.True(app.WaitForExit(10000), "the process should be gone");
		}

		[Fact]
		public void A_recorded_exit_code_means_the_app_is_gone_even_if_the_pid_still_exists()
		{
			var app = Sleeper();
			WriteSession(app.Id, "/bin/sleep");
			File.WriteAllText(ExitFile, "0\n");
			Assert.Contains("running: no", Ui("status").output);
			var key = Ui("key", "cmd+a");
			Assert.Equal(1, key.code);
			Assert.Contains("is not running", key.error);
			var kill = Ui("kill");
			Assert.Contains("had already exited", kill.output);
			Assert.False(app.HasExited, "the exit file says the app is gone: its pid must not be signalled");
		}

		[Fact]
		public void Protected_data_folders_are_refused_before_anything_starts()
		{
			string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
			foreach (string bad in new[]
			{
				"/", "/Users", home, "~", home + "/Documents", home + "/Documents/anything", home + "/Desktop/x", home + "/Downloads",
				home + "/Library", home + "/Library/Application Support", home + "/Library/Application Support/ComicRack CE", home + "/Library/Caches/ComicRack",
				home + "/dev/ComicRackAPI", home + "/dev/ComicRackAPI/x", "/Volumes/Backup/x", "/Applications/x",
			})
			{
				var r = Ui("check-data-dir", bad);
				Assert.True(r.code == 2, bad + " -> " + r.code + " " + r.output + r.error);
				Assert.Contains("refused", r.error);
				// the same through launch: refused (exit 2) before the build or any window check
				var launch = Ui("launch", "--data-dir", bad);
				Assert.True(launch.code == 2, "launch " + bad + " -> " + launch.code + " " + launch.error);
				Assert.Contains("refused", launch.error);
			}
		}

		[Fact]
		public void A_link_to_a_protected_folder_is_resolved_before_the_check()
		{
			string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
			string link = Path.Combine(temp, "innocent");
			File.CreateSymbolicLink(link, home);
			var r = Ui("check-data-dir", link);
			Assert.Equal(2, r.code);
			Assert.Contains("refused", r.error);
			var below = Ui("check-data-dir", link + "/Documents/x");
			Assert.Equal(2, below.code);
		}

		[Fact]
		public void Temporary_and_repository_folders_are_accepted_and_nothing_is_created()
		{
			string fresh = Path.Combine(temp, "a", "b", "data");
			var r = Ui("check-data-dir", fresh);
			Assert.Equal(0, r.code);
			Assert.EndsWith("/a/b/data", r.output.Trim());
			Assert.False(Directory.Exists(Path.Combine(temp, "a")), "checking must not create the folder");
			Assert.Equal(0, Ui("check-data-dir", Path.Combine(Tools.Repo, "mac", "artifacts", "ui-drive-data-test")).code);
			// a missing part that contains ".." cannot be resolved and is refused
			Assert.Equal(2, Ui("check-data-dir", Path.Combine(temp, "missing", "..", "..", "..")).code);
		}

		[Fact]
		public void The_session_file_is_written_with_quoting_and_typing_is_checked_per_chunk()
		{
			string text = File.ReadAllText(Script);
			Assert.Contains("printf 'UD_EXE=%q", text);
			Assert.DoesNotContain("printf \"UD_EXE='%s'", text);
			Assert.Contains("type_checked", text);
			Assert.Contains("${text:$i:8}", text);
			// the idiom: a value with quotes, spaces and a command substitution survives a round trip through source
			string file = Path.Combine(temp, "q.env");
			string nasty = "a b'c\"d$(touch " + temp + "/pwned)`x`";
			var w = Tools.Bash("-c", new[] { "printf 'V=%q\\n' \"$1\" > \"$2\"", "w", nasty, file });
			Assert.Equal(0, w.code);
			var r = Tools.Bash("-c", new[] { ". \"$1\"; printf '%s' \"$V\"", "r", file });
			Assert.Equal(nasty, r.output);
			Assert.False(File.Exists(Path.Combine(temp, "pwned")));
		}
	}
}
