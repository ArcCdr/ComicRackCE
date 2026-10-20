# Merging upstream into `macos`

> **Status: final (P0.5c).** The harness `mac/tools/upstream-sync-check.sh` is the one command to run after the merge. It was proven on a rehearsal merge (`mac/plans/full-port.md` §3, "Facts from P0.5c").

`macos` is the one long-lived branch of the macOS port. Upstream (`maforget/ComicRackCE`, remote `upstream`) keeps adding Windows features; you bring them in with a normal merge. The sessions then check that the Mac build still works and fix what the merge broke.

## 1. Why the merge is conflict-free

No session edits a file outside `mac/` (the zero-edit rule, `mac/plans/full-port.md` §2.2; `mac/tools/verify.sh` checks it). Upstream never touches `mac/`. So the two sides change disjoint files and git merges them without conflicts. What a merge *can* do is change the original sources that the Mac build compiles (the shadow projects link `cYo.Common`, `ComicRack.Engine` and `ComicRack.Plugins`), or files the Mac build replaces with its own copies. The check in §3 finds those.

The only exception would be an approved edit listed in `mac/EDITS.md` (none today).

## 2. Procedure (about 5 minutes plus the check)

Run in the VS Code integrated terminal:

```bash
cd /Users/vince/dev/ComicRackCE
git switch macos
git status --short                     # must show nothing, or only untracked .DS_Store files
git fetch upstream
git log --oneline macos..upstream/master | head -40     # what comes in (and `| wc -l` for the count)
git merge --no-ff upstream/master -m "Merge upstream/master into macos"
```

**What you should see:** `Merge made by the 'ort' strategy.` and a list of changed files, none under `mac/`.

**If git reports a conflict** (not expected):
- In a file outside `mac/`: that file was changed on `macos`, which only an approved `mac/EDITS.md` entry allows. Take upstream's version and let a session re-apply the change the zero-edit way:
  ```bash
  git checkout --theirs -- <file> && git add <file>
  git commit --no-edit
  ```
  Write the file name into a new block in `mac/plans/OWNER-ACTIONS.md` (or just mention it in the resume prompt).
- In a file under `mac/` (only if upstream ever adds a `mac/` folder): stop with `git merge --abort` and paste the resume prompt with the sentence "upstream merge conflicts under mac/".

**To back out the merge** right after making it, before any session ran: `git reset --merge ORIG_HEAD`.

## 3. Check

```bash
mac/tools/upstream-sync-check.sh
```

About 4 to 5 minutes. It refuses to start (exit 3) unless you are on `macos` with a clean tree, no merge in progress, the upstream commits merged into `HEAD` and 3 GB free. The range it checks is `lastSyncedUpstream` of `mac/upstream-sync.json` up to `upstream/master` (or the second parent of the merge commit). Steps, one result line each (`PASS`, `WARN`, `FAIL`, `INFO`):

| Line | What it checks |
|---|---|
| `files`, `new-files`, `ui-parity`, `payload` | Every changed file of the range, sorted by `mac/tools/upstream-scan`: linked into the Mac build, replaced, excluded, Windows UI that the Mac build does not compile (candidates for the Mac app), project files, payload, build settings. `WARN` for a file that needs a follow-up. |
| `packages` | `PackageReference` and `Reference` changes of the original `.csproj` files against the Mac mirrors (`mac/*/Sources.props`). `WARN` when the Mac side lacks or differs. |
| `drift` | Every replaced, stubbed or excluded original against its recorded hash (`mac/replacements.json`). `FAIL` names the originals that changed and the exact `replacement-sync.sh` command (a patched copy says whether upstream's change merges cleanly). |
| `rule-1`, `build`, `tests` | `mac/tools/verify.sh --full`: nothing outside `mac/` changed, the solution builds, all tests pass. If only the drift guards fail because an original changed, `tests` says so and the exit code stays 1. |
| `census-A`, `census-B` | `mac/tools/census`, pass A and pass B: 0 errors. |
| `new-api`, `drawing` | `mac/tools/api-inventory --compare mac/reports/api-baseline.json`: new WinForms, System.Drawing and P/Invoke API in linked code, covered by the shims or not (`FAIL` when not covered or a linked DllImport; `WARN` when covered: review, then `dotnet run --project mac/tools/api-inventory -- --write-baseline`); `mac/tools/drawing-coverage` must pass. |

The last lines are `NEXT:` (what to do) and `EXIT:`. The report is also in `mac/artifacts/upstream-sync/report.md`; the scan, drift, census, API and verify logs are next to it. Nothing under `mac/` is modified by a check.

**Other forms**
```bash
mac/tools/upstream-sync-check.sh --no-merge                       # before merging: preview + check of the current tree
mac/tools/upstream-sync-check.sh --no-merge --range 7fd9fd3..upstream/master   # preview of a given range
mac/tools/upstream-sync-check.sh --record                         # after a clean run: store the synced commit and commit mac/upstream-sync.json
```
In a preview (the end of the range is not merged) the `files`, `packages` and `drift` lines are shown as `INFO ... (preview: would be FAIL after the merge)`; the other lines check the current tree.

**Sample, clean run** (exit 0):
```
upstream-sync-check 990a164 on macos: 7fd9fd3..d0fedcf, 1 upstream commit(s), merged, 253 s
PASS  preconditions  on macos, clean tree, 15.9 GB free
INFO  upstream       7fd9fd3..d0fedcf: 1 upstream commit(s) (merged)
INFO  files          linked 1, replaced 1, UI not linked 1, project files 1
INFO  new-files      1 new file(s) that the shadow projects compile (the build and the census below confirm they compile)
INFO  ui-parity      1 changed Windows UI file(s): parity candidates for the Mac app (...)
PASS  drift          0 of 39 entries need action, no original changed since its recorded hash
PASS  rule-1         nothing outside mac/ changed against upstream/master
PASS  build          Debug, 0 errors, 26 warnings, 4 s
PASS  tests          fast+slow, 861 passed, 218 s
PASS  census-A       pass A: 0 errors
PASS  census-B       pass B: 0 errors
PASS  new-api        no new platform API
PASS  drawing        PASS: 100 % of the inventory is covered
NEXT: clean; record it with mac/tools/upstream-sync-check.sh --record
EXIT: 0
```

**Sample, findings** (exit 1; the same merge before the fix-ups):
```
INFO  new-files      1 new file(s) that the shadow projects compile (...)
WARN  packages       1 package or assembly reference change(s) that the mac project files do not mirror
                       PackageReference Newtonsoft.Json added (- -> 13.0.3) in ComicRack.Engine/ComicRack.Engine.csproj: mac absent; mirror it in mac/ComicRack.Engine.Core/Sources.props
FAIL  drift          1 replaced, stubbed or excluded original(s) changed
                       ComicRack.Engine/CacheManager.cs (patched-copy): merges cleanly (merged text: mac/artifacts/replacement-sync/CacheManager.cs.merged)
                           NEXT: mac/tools/replacement-sync.sh ComicRack.Engine/CacheManager.cs --accept
FAIL  tests          only the drift guards fail (4 test(s)): they pass again once step 3 is resolved
WARN  new-api        new platform API in linked code: 0 type(s), 1 member(s), 0 DllImport(s); 0 not covered by the shims, 0 linked DllImport(s) (all covered: ...)
                       static System.Windows.Forms.Application.StartupPath : string {get} [System.Windows.Forms] covered by the shim: yes  in ComicRack.Engine/IO/NewThing.cs
NEXT: paste the 'After you merged upstream' prompt of mac/plans/RUNBOOK.md §1; a session turns each FAIL and WARN into a U-task, fixes it and runs this check again until it exits 0
EXIT: 1
```

## 4. What the result means and what to do

| Result | Meaning | What you do |
|---|---|---|
| Exit 0, no FAIL or WARN line | The Mac build takes the upstream changes as they are | Done. With the harness: `mac/tools/upstream-sync-check.sh --record` (stores the synced upstream commit and commits `mac/upstream-sync.json`). Push if you want: `git push origin macos`. |
| Exit 1, FAIL or WARN lines (INFO lines never change the exit code) | Upstream changed something the Mac build mirrors: a replaced file changed, new Windows-only API in linked code, a new package, a new file that does not compile on macOS. Also INFO lines listing new Windows UI features (candidates for the Mac app) | Paste the **"After you merged upstream"** prompt of `mac/plans/RUNBOOK.md` §1. A session turns each item into a task, fixes it, re-runs the check until it passes, records the sync, and continues the plan. You do nothing else. |
| Exit 2 | The Mac build or its tests are broken by the merge (for example a new linked file that does not compile with the shims), or a tool of the check could not run | Same prompt. Do not build a release until it passes. |
| Exit 3 | Precondition: uncommitted changes, a merge in progress, or less than 3 GB free | The message says which; fix it (commit or stash, finish the merge, free space) and run the check again. |

## 5. How the session fixes what the merge broke

(`mac/plans/RUNBOOK.md` §5 has the exact steps.) Each finding becomes a task `U<date>.<n>` at the front of the plan:
- **A replaced original changed** (`mac/replacements.json` drift): for a patched copy, `mac/tools/replacement-sync.sh` merges upstream's change into the Mac copy with `git merge-file` and records the new hash; for a rewrite, the session ports the upstream diff by hand.
- **New Windows-only API in linked code** (WinForms, System.Drawing, P/Invoke): the shim gets the member, or the file gets a Mac replacement or is excluded.
- **A new linked file does not compile**: exclusion plus stub, or a replacement.
- **A package changed** in an original `.csproj`: mirrored in `mac/*/Sources.props`.
- **New Windows UI features**: listed in `mac/plans/full-port.md` §9 as candidates; not implemented automatically.

## 6. How often

Every two to four weeks, or before you build a Mac release. After task P3.10 a weekly GitHub workflow runs a dry-run merge and this check in the cloud (after you push `macos`), so breaking upstream changes show up before you merge.
