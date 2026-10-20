# Runbook: running the macOS full port unattended

> For the **orchestrating session** (the one the owner starts) and for the owner. The plan is `mac/plans/full-port.md`; this file says how to execute it. Everything a session needs is in files: the status lines and facts in `full-port.md`, the owner's answers in `mac/plans/OWNER-ACTIONS.md`, the history in `mac/plans/run-log.md` and `git log`. A new session never depends on an earlier session's memory.

## 1. Resume prompts (copy and paste)

Run them from the **VS Code integrated terminal** (Screen Recording and Accessibility are granted to VS Code; from another terminal app the UI automation fails), in `/Users/vince/dev/ComicRackCE`.

**Normal run, and after you answered something in OWNER-ACTIONS.md:**
```bash
claude --model sonnet --effort high "You are the orchestrator of the ComicRack CE macOS port in /Users/vince/dev/ComicRackCE, branch macos. Read mac/plans/RUNBOOK.md completely and follow it exactly: apply the owner's answers in mac/plans/OWNER-ACTIONS.md, then run the tasks of mac/plans/full-port.md one at a time, each in a fresh subagent, verify and commit each, until a checkpoint or a STOP condition. Never push, merge or switch branches."
```

**After you merged upstream** (`mac/UPSTREAM-SYNC.md` §2):
```bash
claude --model sonnet --effort high "You are the orchestrator of the ComicRack CE macOS port in /Users/vince/dev/ComicRackCE, branch macos. The owner has just merged upstream/master into macos. Read mac/plans/RUNBOOK.md and mac/UPSTREAM-SYNC.md, then follow RUNBOOK.md section 5: run the upstream check, turn every FAIL and WARN into U-tasks in mac/plans/full-port.md, run them until the check passes, then continue with the normal loop. Never push, merge or switch branches."
```

**Unattended loop** (after task P0.8 exists and the allowlist of §7 is installed): `mac/tools/run-plan.sh` (one headless orchestrator run per task, until STOP; see §7).

The flags matter: `--model` and `--effort` on the command line apply to that session only. Do not use `/model` or `/effort` inside a session (they change your global defaults).

## 2. The orchestrator loop

**0. Preflight (once per session, and again after any STOP-like surprise)**
1. `git rev-parse --abbrev-ref HEAD` is `macos`; `test ! -e .git/MERGE_HEAD` (no merge in progress; if there is one: STOP, owner block "finish or abort the merge").
2. `git status --porcelain`: only `mac/` changes or nothing. Leftovers of a crashed task (a task with `Status: doing`): run `mac/tools/verify.sh` and the task's acceptance; if both pass, commit them as that task; else `git stash push -u -m "<ID> abandoned <YYYY-MM-DD>"` and set the task back to `todo` with a note in the run log.
3. Disk: `df -h ~`. Below 8 GB free: `find mac -type d -name bin -prune -exec rm -rf {} +`, `find mac -type d -name obj -prune -exec rm -rf {} +`, `rm -rf mac/artifacts/verify`, `rm -rf mac/artifacts/ui-drive` and `rm -rf mac/artifacts/upstream-sync` (one command each: the allowlist of §7 lists exactly these). Still below 3 GB: STOP (owner block "free disk space").
4. Read `mac/plans/OWNER-ACTIONS.md`. For every block with `Status: ANSWERED`: apply the answer (update §1.2 decisions, the affected tasks' Status from `blocked (Cn)` to `todo`, facts in §3), set the block to `DONE`, move it to "Done", commit `mac: apply owner answers OA-<n>`.
5. Set the `Run state:` line of `full-port.md` to `RUNNING (<date>, orchestrator)`.

**1. Pick** the first task in `full-port.md` §6 whose `Status:` is `todo` and whose `Depends` are all `done` or `done-with-caveat` (a checkpoint ID in Depends, like `C5`, counts as done only when its OWNER-ACTIONS block is DONE; a dependency written `P4.1 (GO)` needs the spike's Status to say `verdict GO`). U-tasks and RV-tasks come first. If none is left: go to §8 "STOP: checkpoint reached".

**2. Start**: set its Status to `doing (started <date>)` (no commit).

**3. Run** one fresh subagent with the Agent tool: `subagent_type: general-purpose`, `model: sonnet`, `effort:` the task's Effort, `run_in_background: false`, prompt = §3 with `{TASK_ID}` replaced. One subagent at a time; never two tasks in parallel (one branch, one working tree).

**4. Verify** (the orchestrator does this itself, it does not trust the report):
```bash
git log -1 --format='%h %s'                                   # a commit "mac: <ID> ..." exists (unless RESULT is blocked/failed)
git show --stat --format= HEAD                                # only allowed paths
git show HEAD --format= | grep -E '^\+' | grep -nEi 'claude-[a-z0-9.-]*[0-9]|(sonnet|opus|haiku)-[0-9]' && echo "MODEL ID FOUND"
mac/tools/verify.sh                                           # must end with RESULT: PASS
grep -n -A4 '^#### {TASK_ID} ' mac/plans/full-port.md | grep 'Status:'   # updated
```
Then run the task's own Accept commands (all of them; e2e commands too; `mac/tools/e2e.sh` exit 4 = screen locked, 5 = no case passed (fixtures or bundle missing), 6 = a launch was refused because another copy of the app runs: all three are SKIPPED, never PASS, and not FAIL).

**5. Decide**
- All pass: if the subagent did not commit, commit now (§2.1 of `full-port.md`). Status `done <date>` (or `done-with-caveat <date> (<what>)` when an acceptance part was SKIPPED or is an owner check; add the owner part to the checkpoint's list in `OWNER-ACTIONS.md` "Collected for C<n>").
- A check fails, first time: run the task again (§3 prompt plus the "Previous attempt" paragraph of §3, with the failure output, about 30 lines). The working tree keeps the first attempt's changes unless they are clearly wrong; then stash them first (`git stash push -u -m "<ID> attempt 1"`).
- Fails a second time: stash (`git stash push -u -m "<ID> attempt 2"`), set Status `blocked (two failures: <one line>)`, add the subagent's split proposal (or your own: two or three smaller tasks with the same template) to `full-port.md` §9 and, if the split is clear, as new tasks `<ID>.1`, `<ID>.2` in §7 and §6 replacing it (Status of the original: `split -> <ID>.1, <ID>.2`). Continue with the next task.
- Subagent reports `blocked` (STOP rule, owner input, needs an edit): check the reason is real; record it; write or extend the owner block; continue with the next independent task.

**6. Record**: run `mac/tools/plan-status.sh --write --backfill-log` (it rewrites the status table and the `next task:` part of the `Run state:` line of `full-port.md` from the Status lines, and appends a row to `mac/plans/run-log.md` for every `mac: <task id>` commit that has none), check the new row (fill in minutes and a note if useful; a spike or a caveat deserves one), run `mac/tools/plan-status.sh --check` (exit 0), and commit `mac: run log <ID>` together with any status changes (or amend nothing: a separate small commit is fine). Never edit the table by hand.

**7. Gates**: after every 5 completed tasks and at the end of each wave run the full gate and a review (§4).

**8. Loop** to step 1. In interactive mode keep going until §8 says STOP. In loop mode (§7) end the session after one task (the loop starts the next one).

## 3. Subagent prompt (verbatim; replace `{TASK_ID}`)

```
You are implementing exactly one task of the ComicRack CE macOS port: {TASK_ID}.
Working directory: /Users/vince/dev/ComicRackCE. Branch: macos. Do not create or switch branches, do not push, merge, rebase or reset.

Read first, completely:
1. mac/plans/full-port.md: section 2 (Conventions), section 3 (Facts), the task block "#### {TASK_ID}" in section 7, and the blocks of the tasks listed in its "Depends".
2. MACOS_NATIVE_PATH.md section 0 and section 9.2.
3. Every file the task block names. Older facts are in mac/plans/mvp-track.md section 3; search it when the task refers to an M-task.
4. If the task block has a "Parity:" line or changes anything a user sees: full-port.md section 2.11 (parity and chrome policy), mac/plans/chrome-architecture.md (where chrome lives), mac/plans/chrome-guidelines.md, and the rows of mac/plans/parity-ledger.md that name this task (grep -n "{TASK_ID}" mac/plans/parity-ledger.md) or that the Parity line lists. Behaviour, defaults, menus, labels (original TR keys), shortcuts and layout must match the WinForms original exactly; only the chrome-guidelines section 7 decisions are native.

Binding rules (full-port.md section 2 has the details):
- Change nothing outside mac/ (MACOS_NATIVE_PATH.md and .github/workflows/mac-*.yml only if the task says so). Original code is changed only through pass B replacements with an entry in mac/replacements.json. If an edit of an original file looks unavoidable: do not make it; write the proposal to mac/upstream-proposals/{TASK_ID}.md and report RESULT: blocked.
- Stay in the task's scope. New work goes to full-port.md section 9 as "- [{TASK_ID}] ...". Facts that later tasks need go to full-port.md section 3 under "Facts from {TASK_ID}".
- Disk: run `df -h ~` at the start and before large builds; STOP below 3 GB free. Keep mac/fixtures within 1.5 GB. At the end delete mac/**/bin and mac/**/obj if less than 8 GB is free.
- Never touch ~/Library/Application Support|Caches|Logs/ComicRack*, /Users/vince/dev/ComicRackAPI or the owner's folders; use temporary --data-dir folders and copies.
- Real-window checks only through mac/tools/ui-drive and mac/tools/e2e.sh (they refuse to type into other apps). Exit code 4 means the screen is locked: record the check as SKIPPED, do not fake it.
- Tests must be deterministic; new SkiaSharp-using test projects set RuntimeIdentifier osx-arm64 and SelfContained false.
- Chrome only in the chrome layer (R10): the look of anything lives in mac/ComicRack.Mac/Themes, Controls and Platform; views and view models use Cr* controls, cr-* style classes, tokens and icons by name only, never a colour, font, shape, inline style or template, or a picture path (ChromeArchitectureTests and NoLiteralColorsTests enforce it). A new chrome control implements IChromeElement and has its themes (mac/plans/chrome-architecture.md).
- Every new custom-drawn control or draw operation adds a 1x/2x test with the HiDPI harness (mac/tests/ComicRack.Mac.Tests/HiDpi, Category=HiDpi; full-port.md section 2.5).
- No model identifiers or session links in code, comments or docs.
- If the acceptance still fails after two serious attempts: stop, write a split proposal into full-port.md section 9, report RESULT: failed.

Do the task. Then:
1. Run every Accept command of the task, and mac/tools/verify.sh. All must pass, except checks that need the owner or an unlocked screen, which you report as SKIPPED with the reason.
2. Update the task's "Status:" line in full-port.md: "done YYYY-MM-DD", "done-with-caveat YYYY-MM-DD (what is unverified)", or "blocked (reason)". For a UI task also update its parity-ledger rows in the same commit (done YYYY-MM-DD, or deviation with "impossible: ..." / "native: C-...", or todo with the task that finishes it) and add any new chrome decision to chrome-guidelines.md section 7.
3. Commit once on macos: stage only paths you changed under mac/ (and the allowed files outside it); message "mac: {TASK_ID} <title> (<one-line result>)", ending with the Co-Authored-By trailer your environment specifies. If RESULT is failed, do not commit; leave the changes in the working tree.
4. Reply with exactly this report and nothing else:
TASK: {TASK_ID}
RESULT: done | done-with-caveat | blocked | failed
COMMIT: <short hash or none>
ACCEPT: <each Accept command, one per line, with PASS / FAIL / SKIPPED (reason)>
VERIFY: <the RESULT line of mac/tools/verify.sh>
OWNER: <what only the owner can check or decide, or none>
DISCOVERED: <items you added to section 9, or none>
FACTS: <one line per fact you added to section 3, or none>
LEDGER: <parity-ledger rows changed, as "ID -> status", or none>
CHROME: <contact sheet path and new chrome decisions (C-... rows), or none>
STOP: <STOP condition that triggered, or none>
```

On a retry, append to the prompt:
```
Previous attempt: it failed with the output below. The working tree contains that attempt's changes (or they were stashed as "{TASK_ID} attempt 1": inspect with `git stash show -p stash@{0}` if useful). Find the cause first, then finish the task. This is the last attempt; if it fails again, write the split proposal.
<about 30 lines of the failing output>
```

## 4. Full gate and reviews

Every 5 completed tasks and at each wave end (`full-port.md` §6):
```bash
mac/tools/verify.sh --full
mac/tools/e2e.sh                                          # after P0.6b; exit 4 (locked), 5 (no case passed) or 6 (launch refused) = skipped, retried at the next gate
dotnet run --project mac/tools/census -- --pass B          # 0 errors
dotnet run --project mac/tools/drawing-coverage            # PASS
git status --porcelain                                     # census rewrites mac/reports/*.md: commit only if totals changed,
git restore mac/reports                                    #   otherwise discard the timing-only changes (before the next line: its
                                                           #   precondition is a clean tree)
mac/tools/upstream-sync-check.sh --no-merge                # previews upstream/master, checks the current tree (about 5 minutes: it runs verify --full and both census passes itself)
git status --porcelain                                     # the check's own census runs rewrite mac/reports/*.md again:
git restore mac/reports                                    #   restore them once more unless the totals changed
```
A failure here that no single task explains becomes task `RV<n>` at the front of the order.

**Review**: one subagent with `subagent_type: general-purpose`, `model: opus`, `effort: high`, prompt:
```
Review the macOS port changes in /Users/vince/dev/ComicRackCE between commit {FROM} and HEAD (git diff {FROM}..HEAD -- mac MACOS_NATIVE_PATH.md .github/workflows). Read mac/plans/full-port.md section 2 first. Look for correctness bugs, threading violations (Core events not marshalled through UiDispatch, UI work off the UI thread, blocking calls on the UI thread), pixel ownership violations (drawing Core-owned bitmaps), resource leaks, tests that cannot fail or depend on timing, replacements without a manifest entry, changes outside mac/, and parity breaks (R9, section 2.11: UI that differs from the WinForms original or its view spec without a recorded ledger deviation, retyped strings instead of original TR keys, literal colours, invented menu items or shortcuts). Do not change any file. Report findings ranked by severity (high, medium, low), each with file:line, the failure scenario and a one-line fix. Report at most 15 findings; report none rather than speculative ones.
```
`{FROM}` is the commit of the previous review (in `run-log.md`). High findings become `RV<n>` tasks (template of §7, Effort high) at the front of the order; medium ones go to §9; low ones are dropped unless cheap to fold into a later task.

## 5. After an upstream merge (orchestrator side of `mac/UPSTREAM-SYNC.md`)

1. Preflight as in §2 step 0 (the merge must be finished and committed by the owner).
2. Run `mac/tools/upstream-sync-check.sh` (about 5 minutes; exit 0 clean, 1 action items, 2 build or tests broken, 3 precondition). Read `mac/artifacts/upstream-sync/report.md`: one line per step (preconditions, files, packages, ui-parity, drift, build, tests, census-A/B, new-api, drawing), the detail lines under each FAIL and WARN, `NEXT:` and `EXIT:`. The scan output is `scan.txt`, the drift output `drift.txt`, the build and test logs are in `mac/artifacts/verify/`.
3. For every FAIL and WARN create a task `U<yyyymmdd>.<n>` in `full-port.md` §7 "Upstream sync tasks" and at the front of §6, with the template. Typical tasks:
   - *Replacement drift, patched copy*: `mac/tools/replacement-sync.sh <original>`; check the merged copy; `--accept`; tests. Effort medium.
   - *Replacement drift, rewrite or stub*: port the upstream diff by hand into the replacement; `--accept`. Effort high.
   - *New platform API in linked code not covered by a shim*: add the member to `cYo.Compat.Drawing`/`cYo.Compat.Forms` (real behaviour if a v1 path reaches it, else a throwing member), or exclude/replace the file if it is Windows-only. Effort high.
   - *New DllImport in linked code*: replacement or exclusion; P1.2 tripwire test must stay green. Effort high.
   - *Build error from a new linked file*: exclusion plus stub, or replacement. Effort high.
   - *Package change*: mirror it in `mac/*/Sources.props` (same version) unless Windows-only. Effort medium.
   - *Payload change* (`ComicRack/Output/**`): nothing to do unless the build copies it wrongly. INFO only.
   - *UI parity* (`ui-parity` WARN: new or changed WinForms UI; `ledger-check` fails on new menu items): R9 requires parity, so add a row to `mac/plans/parity-ledger.md` (status `todo`) per new or changed feature and a U-task that implements it (or names the existing task that owns the area); a change to behaviour the Mac app already mirrors is fixed in the U-task. The U-task also adds one row per changed file to `mac/plans/upstream-ui-log.md` (file as the check prints it, the upstream commit hash it prints in parentheses, the U-task ID, the ledger row IDs); that record is what turns the `ui-parity` WARN into INFO on the next run. A changed original colour (token test) is a U-task too.
4. Run the U-tasks through the normal loop (§2 steps 2 to 6). Then re-run the check until it exits 0, then `mac/tools/upstream-sync-check.sh --record` (it updates and commits `mac/upstream-sync.json`).
5. Continue with the normal loop.

## 6. Writing to OWNER-ACTIONS.md

Rules: one block per question or action; consolidate (several questions for the same checkpoint go into one block with numbered parts); exact commands; say what the owner will see; always say what happens if he skips it; always end with the resume prompt. Put new blocks under "Inbox", newest last. Keep "Collected for C<n>" lists (owner-only checks gathered from tasks) until the checkpoint block is written.

Block template:
```markdown
### OA-<n>: <short title>
- **Status:** OPEN
- **Checkpoint / tasks:** C<n> / <task IDs> · **Asked:** <date> · **Blocks:** <task IDs, or "nothing; sessions use the default">
- **Why now:** <one or two sentences>
- **What to do:**
  1. <exact step, with a copy-paste command where possible>
  2. <...>
- **What you should see:** <the expected result of each step>
- **Time needed:** about <n> minutes
- **If you skip it:** <the default the sessions use, and what stays unverified>
- **What happens next:** <what the sessions do with your answer>
- **Your answer** (write below this line, then change Status to ANSWERED):
  >
- **Resume:** in the VS Code terminal, in /Users/vince/dev/ComicRackCE, paste the "Normal run" prompt of mac/plans/RUNBOOK.md §1.
```

## 7. Unattended loop mode and permissions

Loop model: `sonnet` (read by `mac/tools/run-plan.sh`; the environment variable `COMICRACK_PLAN_MODEL` or `--model` overrides it; the scripts hold no tier name).

Interactive mode (§1) asks for permission prompts unless the tools are allowed. For long unattended runs:
1. Install an allowlist once (owner action; `mac/tools/claude-allowlist.json` is the ready file, and the file, not this text, is the list): merge its `permissions.allow` list into `.claude/settings.local.json` in the repository (the user's global git ignore already excludes that file, so rule 1 is not affected). It allows `dotnet`, the read-only git commands, `git add` and `git restore` only for paths under `mac/` (plus `MACOS_NATIVE_PATH.md` and `.github/workflows/mac-*`), `git commit`, `git stash push -u -m`, `git stash list`, `git stash show`, `git fetch upstream`, `git rev-parse`, `git merge-file`, the scripts under `mac/tools`, `mac/native` and `mac/packaging`, `python3 mac/tools/...`, the packaging helpers (`screencapture`, `swiftc`, `codesign`, `hdiutil`, `plutil`, `sips`), read-only `df`, `du`, `test`, `grep`, `jq`, `wc`, `ls`, the exact cleanup commands of §2 step 0 (the `bin` and `obj` finds and `rm -rf` of the three `mac/artifacts` folders), `Read`, and `Edit` and `Write` scoped to `mac/**`, `MACOS_NATIVE_PATH.md` and `.github/workflows/mac-*.yml`. It does not allow `osascript` (the UI scripts call it themselves, behind their frontmost-process check), a bare `rm`, `git add` outside `mac/`, `git stash drop`, `git reset`, `git checkout`, `git clean`, `git merge`, `git rebase` or `git push`. A test (`ComicRack.Cli.Tests`, `AllowlistTests`) checks that every command the RUNBOOK and `run-plan.sh` use is covered and that none of these is allowed.
2. Start `mac/tools/run-plan.sh` from the VS Code terminal. It runs `claude -p "<Normal run prompt> Do one task only, then end the session." --model sonnet --effort high` in a loop, reads the `Run state:` line after each run and ends on STOPPED, after `--max-tasks`, or after two runs that committed nothing.
3. `--dangerously-skip-permissions` would also work but removes every guard; it is the owner's decision and is not used by the scripts.

## 8. STOP conditions (the orchestrator ends the run)

| Condition | What the orchestrator does |
|---|---|
| **Checkpoint reached**: no `todo` task is runnable because every remaining one depends on an unanswered checkpoint | For C2 and C6 first run `mac/tools/chrome-sheet.sh C2 --all` (or `C6`) and `python3 mac/tools/ledger-check/ledger-check.py --report`, and put the sheet path, the deviation list and the chrome decisions with a flip cost into the block (chrome-guidelines §9 step 5). Write (or complete) the checkpoint block in `OWNER-ACTIONS.md`, set `Run state: STOPPED (waiting for C<n>, <date>)`, commit, print the owner summary (below) |
| Disk below 3 GB after the cleanup of §2 step 0 | Owner block "free disk space" (what uses space: `du -sh mac/fixtures mac/artifacts ~/.nuget/packages`), STOPPED |
| `mac/tools/verify.sh` fails on HEAD without a task's changes (the tree was green before) and one `R` task did not fix it | Owner block with the failure, STOPPED |
| Three tasks in a row ended `blocked` or `failed` | Something systemic: owner block with the three reasons, STOPPED |
| Repository state unexpected: another branch, a merge in progress, commits on `macos` that are not from the plan and break the gate | Owner block, STOPPED |
| An action needs a secret or an owner-only resource that no checkpoint covers | Owner block, continue with independent tasks; STOPPED only when none is left |

**Owner summary** printed at every STOP (and at the end of an interactive session): tasks done this run (IDs, one line each), tasks blocked and why, the checkpoint or STOP reason, what is in the OWNER-ACTIONS inbox (titles, time needed), and the resume prompt.

## 9. Context and memory

- Everything that matters goes to files in the same commit as the work: Status lines, §3 facts, §9 discovered work, `run-log.md`, OWNER-ACTIONS blocks. A session may end at any point without losing anything but the task in progress, which preflight recovers.
- The orchestrator keeps only the reports in its context, not file dumps. If its context gets long (many tasks in one interactive session), it finishes the current task, sets `Run state: PAUSED (context, <date>)`, commits and asks the owner to paste the resume prompt again, or the loop mode starts a fresh one.
- `mac/plans/run-log.md` line format: `| <date time> | <ID> | <result> | <commit> | <minutes> | <note> |`.
