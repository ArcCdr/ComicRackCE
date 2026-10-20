#!/usr/bin/env bash
# Unattended loop for the macOS port (mac/plans/full-port.md §5.4, mac/plans/RUNBOOK.md §7).
#
#   mac/tools/run-plan.sh [--max-tasks N] [--dry-run] [--model M] [--effort E]
#
# Starts one headless orchestrator run per task:
#   claude -p "<normal run prompt> Do one task only, then end the session." --model sonnet --effort high
# After each run it reads the "Run state:" line of full-port.md and stops on STOPPED, after --max-tasks runs,
# when no task is runnable, or after two runs in a row that committed nothing.
# --dry-run prints the commands and the task each would pick (simulating that every picked task ends done);
# it starts no headless run and changes nothing.
# Install the permission allowlist first (mac/tools/claude-allowlist.json, RUNBOOK.md §7).
# Exit codes: 0 finished normally (limit, STOPPED, nothing runnable, dry run), 1 two runs without a commit,
# 2 bad arguments or unexpected repository state, 3 claude CLI missing.
set -uo pipefail

MAX=0; DRY=0; MODEL=sonnet; EFFORT=high
while [ $# -gt 0 ]; do
	case "$1" in
		--max-tasks) shift; MAX="${1:-}"; case "$MAX" in ''|*[!0-9]*) echo "run-plan: --max-tasks needs a number" >&2; exit 2 ;; esac ;;
		--dry-run) DRY=1 ;;
		--model) shift; MODEL="${1:-}"; [ -n "$MODEL" ] || { echo "run-plan: --model needs a value" >&2; exit 2; } ;;
		--effort) shift; EFFORT="${1:-}"; [ -n "$EFFORT" ] || { echo "run-plan: --effort needs a value" >&2; exit 2; } ;;
		-h|--help) sed -n '2,14p' "$0"; exit 0 ;;
		*) echo "run-plan: unknown option $1" >&2; exit 2 ;;
	esac
	shift
done

ROOT=$(git -C "$(dirname "$0")" rev-parse --show-toplevel 2>/dev/null) || { echo "run-plan: not inside the git repository" >&2; exit 2; }
cd "$ROOT" || exit 2
PLAN=mac/plans/full-port.md
LOG=mac/plans/run-log.md

PROMPT="You are the orchestrator of the ComicRack CE macOS port in $ROOT, branch macos. Read mac/plans/RUNBOOK.md completely and follow it exactly: apply the owner's answers in mac/plans/OWNER-ACTIONS.md, then run the tasks of mac/plans/full-port.md one at a time, each in a fresh subagent, verify and commit each, until a checkpoint or a STOP condition. Never push, merge or switch branches. Do one task only, then end the session."

# Prints the IDs of the next N runnable tasks, one per line, assuming each picked task ends done.
# Output "NONE" when nothing is runnable. Reads §6 (order) and the Status/Depends lines of §7 itself.
pick() {
	python3 -I - "$PLAN" mac/plans/OWNER-ACTIONS.md "$1" <<'PY'
import re, sys
plan, oa, count = sys.argv[1], sys.argv[2], int(sys.argv[3])
text = open(plan, encoding="utf-8").read()
# Section 6 order: wave bullets, in order.
m6 = re.search(r"^## 6\. .*?(?=^## 7\.)", text, re.S | re.M)
order = []
for line in m6.group(0).splitlines():
    if line.startswith("- **Wave"):
        body = line.split(":**", 1)[1] if ":**" in line else line
        body = re.split(r"→|->", body)[0] if False else body
        for tid in re.findall(r"\b(P\d+\.(?:T)?\d+[a-z]?|P\d+\.[RT]\d+|P5\.R\d+|P5\.T\d+)\b", body):
            if tid not in order:
                order.append(tid)
# Task blocks in section 7.
m7 = re.search(r"^## 7\. Tasks.*?(?=^## 8\.)", text, re.S | re.M)
blocks = re.split(r"^#### ", m7.group(0), flags=re.M)[1:]
tasks = {}
seq = []
for b in blocks:
    tid = b.split(None, 1)[0]
    dep = re.search(r"^- Depends:\s*(.*?)\s*·", b, re.M)
    st = re.search(r"Status:\*{0,2}\s*(.*?)\*{0,2}\s*$", b.split("\n", 3)[1] if False else "\n".join(b.splitlines()[:6]), re.M)
    if not dep or not st:
        continue
    st_line = [l for l in b.splitlines()[:6] if "Status:" in l]
    s = re.search(r"Status:\*{0,2}\s*(.*)$", st_line[0]).group(1).strip().strip("*").strip() if st_line else ""
    deps = [] if dep.group(1).strip().lower() == "none" else [d.strip() for d in re.split(r",", re.sub(r"\(.*?\)", lambda mm: mm.group(0), dep.group(1))) if d.strip()]
    tasks[tid] = {"status": s, "deps": deps}
    seq.append(tid)
# Front of the list: R and U tasks, then the wave order, then anything else in file order.
front = [t for t in seq if re.match(r"^(R\d+|U\d{8}\.\d+)$", t)]
full = front + [t for t in order if t in tasks and t not in front] + [t for t in seq if t not in front and t not in order]
# Checkpoints answered: an OWNER-ACTIONS block "### ... Cn ..." with Status DONE (or ANSWERED applied).
done_cp = set()
try:
    oat = open(oa, encoding="utf-8").read()
    for blk in re.split(r"^### ", oat, flags=re.M)[1:]:
        head = blk.splitlines()[0]
        if re.search(r"Status:\*{0,2}\s*DONE", blk):
            for c in re.findall(r"\b(C\d+)\b", head):
                done_cp.add(c)
except OSError:
    pass
def is_done(s):
    return s.startswith("done") or s.startswith("skipped") or s.startswith("split")
state = {t: tasks[t]["status"] for t in tasks}
def dep_ok(d):
    mm = re.match(r"^(\S+?)\s*(\(GO\))?$", d)
    did, go = mm.group(1), mm.group(2)
    if re.match(r"^C\d+$", did):
        return did in done_cp
    if did not in state:
        return False
    s = state[did]
    if not (s.startswith("done")):
        return False
    return ("verdict GO" in s) if go else True
picked = []
for _ in range(count):
    nxt = None
    for t in full:
        if state[t].startswith("todo") and all(dep_ok(d) for d in tasks[t]["deps"]):
            nxt = t
            break
    if nxt is None:
        break
    picked.append(nxt)
    state[nxt] = "done (simulated)"
    # a simulated spike done does not satisfy "(GO)" deps; that is intended
print("\n".join(picked) if picked else "NONE")
PY
}

run_state() { grep -m1 '^Run state:' "$PLAN" | sed 's/^Run state: *//'; }

if [ "$DRY" = 1 ]; then
	N=$MAX; [ "$N" -gt 0 ] || N=1
	IDS=$(pick "$N") || { echo "run-plan: could not read the plan" >&2; exit 2; }
	if [ "$IDS" = NONE ]; then echo "run-plan (dry run): no runnable task (checkpoint or all done); Run state: $(run_state)"; exit 0; fi
	i=0
	while IFS= read -r id; do
		i=$((i + 1))
		echo "[$i] would pick task: $id"
		echo "    claude -p \"<normal run prompt: orchestrator, one task only>\" --model $MODEL --effort $EFFORT"
	done <<<"$IDS"
	echo "run-plan (dry run): prompt text:"
	echo "    $PROMPT"
	exit 0
fi

command -v claude >/dev/null 2>&1 || { echo "run-plan: the claude CLI is not on PATH" >&2; exit 3; }
[ "$(git rev-parse --abbrev-ref HEAD)" = macos ] || { echo "run-plan: not on branch macos" >&2; exit 2; }
[ ! -e .git/MERGE_HEAD ] || { echo "run-plan: a merge is in progress" >&2; exit 2; }

COUNT=0; IDLE=0
while :; do
	if [ "$MAX" -gt 0 ] && [ "$COUNT" -ge "$MAX" ]; then echo "run-plan: --max-tasks reached"; exit 0; fi
	STATE=$(run_state)
	case "$STATE" in STOPPED*) echo "run-plan: $STATE"; exit 0 ;; esac
	NEXT=$(pick 1) || { echo "run-plan: could not read the plan" >&2; exit 2; }
	if [ "$NEXT" = NONE ]; then echo "run-plan: no runnable task (checkpoint or all done)"; exit 0; fi
	BEFORE=$(git rev-parse HEAD)
	echo "run-plan: run $((COUNT + 1)), next task $NEXT, $(date '+%F %T')"
	claude -p "$PROMPT" --model "$MODEL" --effort "$EFFORT" || echo "run-plan: claude exited with $?"
	COUNT=$((COUNT + 1))
	AFTER=$(git rev-parse HEAD)
	if [ "$BEFORE" = "$AFTER" ]; then IDLE=$((IDLE + 1)); else IDLE=0; fi
	STATE=$(run_state)
	echo "run-plan: after run: Run state: $STATE; commits this run: $(git rev-list --count "$BEFORE..$AFTER")"
	case "$STATE" in STOPPED*) echo "run-plan: stopped by the orchestrator"; exit 0 ;; esac
	if [ "$IDLE" -ge 2 ]; then echo "run-plan: two runs in a row committed nothing; ending" >&2; exit 1; fi
done
