# Merging upstream into `macos`

> **Status: draft. The harness `mac/tools/upstream-sync-check.sh` is pending task P0.5c** (`mac/plans/full-port.md`). Until it exists, use the interim check in §3. P0.5c replaces this note and the interim section with the final commands and sample output.

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

**With the harness (after P0.5c):**
```bash
mac/tools/upstream-sync-check.sh
```
It builds, runs all tests (fast and slow), both census passes, the replacement drift guards, the new-platform-API scan and the new-file classification, and prints one line per check, ending with `NEXT:` and the exit code. The full report is `mac/artifacts/upstream-sync/report.md`.

**Interim, until P0.5c is done:**
```bash
mac/tools/verify.sh --full
dotnet run --project mac/tools/census -- --pass A          # total errors must be 0
dotnet run --project mac/tools/census -- --pass B          # total errors must be 0
dotnet run --project mac/tools/drawing-coverage            # must end with PASS
dotnet test mac/tests/ComicRack.Mac.Host.Tests --filter FullyQualifiedName~Drift
git status --short mac/reports                             # the census rewrites its reports; only timings should differ
```

## 4. What the result means and what to do

| Result | Meaning | What you do |
|---|---|---|
| Exit 0, every line PASS (interim: every command passed) | The Mac build takes the upstream changes as they are | Done. With the harness: `mac/tools/upstream-sync-check.sh --record` (stores the synced upstream commit and commits `mac/upstream-sync.json`). Push if you want: `git push origin macos`. |
| Exit 1, FAIL or WARN lines | Upstream changed something the Mac build mirrors: a replaced file changed, new Windows-only API in linked code, a new package, a new file that does not compile on macOS. Also INFO lines listing new Windows UI features (candidates for the Mac app) | Paste the **"After you merged upstream"** prompt of `mac/plans/RUNBOOK.md` §1. A session turns each item into a task, fixes it, re-runs the check until it passes, records the sync, and continues the plan. You do nothing else. |
| Exit 2 | The Mac build or its tests are broken by the merge | Same prompt. Do not build a release until it passes. |
| Exit 3 | Precondition: uncommitted changes, a merge in progress, or less than 3 GB free | The message says which; fix it (commit or stash, finish the merge, free space) and run the check again. |

The same applies to the interim commands: any failure, paste the "After you merged upstream" prompt.

## 5. How the session fixes what the merge broke

(`mac/plans/RUNBOOK.md` §5 has the exact steps.) Each finding becomes a task `U<date>.<n>` at the front of the plan:
- **A replaced original changed** (`mac/replacements.json` drift): for a patched copy, `mac/tools/replacement-sync.sh` merges upstream's change into the Mac copy with `git merge-file` and records the new hash; for a rewrite, the session ports the upstream diff by hand.
- **New Windows-only API in linked code** (WinForms, System.Drawing, P/Invoke): the shim gets the member, or the file gets a Mac replacement or is excluded.
- **A new linked file does not compile**: exclusion plus stub, or a replacement.
- **A package changed** in an original `.csproj`: mirrored in `mac/*/Sources.props`.
- **New Windows UI features**: listed in `mac/plans/full-port.md` §9 as candidates; not implemented automatically.

## 6. How often

Every two to four weeks, or before you build a Mac release. After task P3.10 a weekly GitHub workflow runs a dry-run merge and this check in the cloud (after you push `macos`), so breaking upstream changes show up before you merge.
