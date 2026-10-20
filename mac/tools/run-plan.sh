#!/usr/bin/env bash
# Unattended loop for the macOS port (mac/plans/full-port.md §5.4, mac/plans/RUNBOOK.md §7).
#
#   mac/tools/run-plan.sh [--max-tasks N] [--dry-run] [--model M] [--effort E]
#
# Starts one headless orchestrator run per task:
#   claude -p "<normal run prompt> Do one task only, then end the session." --model <model> --effort <effort>
# Model: --model, else the environment variable COMICRACK_PLAN_MODEL, else the "Loop model:" line of mac/plans/RUNBOOK.md
# section 7 (the plan files, not this script, name the tier). Effort: --effort, else high.
# After each run it reads the "Run state:" line of full-port.md and stops on STOPPED, after --max-tasks runs,
# when no task is runnable, or after two runs in a row that committed nothing.
# --dry-run prints the commands and the task each would pick (simulating that every picked task ends done);
# it starts no headless run and changes nothing.
# Install the permission allowlist first (mac/tools/claude-allowlist.json, RUNBOOK.md §7).
# Exit codes: 0 finished normally (limit, STOPPED, nothing runnable, dry run), 1 two runs without a commit,
# 2 bad arguments or unexpected repository state, 3 claude CLI missing.
set -uo pipefail

MAX=0; DRY=0; MODEL="${COMICRACK_PLAN_MODEL:-}"; EFFORT=high
while [ $# -gt 0 ]; do
	case "$1" in
		--max-tasks) shift; MAX="${1:-}"; case "$MAX" in ''|*[!0-9]*) echo "run-plan: --max-tasks needs a number" >&2; exit 2 ;; esac ;;
		--dry-run) DRY=1 ;;
		--model) shift; MODEL="${1:-}"; [ -n "$MODEL" ] || { echo "run-plan: --model needs a value" >&2; exit 2; } ;;
		--effort) shift; EFFORT="${1:-}"; [ -n "$EFFORT" ] || { echo "run-plan: --effort needs a value" >&2; exit 2; } ;;
		-h|--help) sed -n '2,16p' "$0"; exit 0 ;;
		*) echo "run-plan: unknown option $1" >&2; exit 2 ;;
	esac
	shift
done

ROOT=$(git -C "$(dirname "$0")" rev-parse --show-toplevel 2>/dev/null) || { echo "run-plan: not inside the git repository" >&2; exit 2; }
cd "$ROOT" || exit 2
PLAN=mac/plans/full-port.md
LOG=mac/plans/run-log.md
RUNBOOK=mac/plans/RUNBOOK.md

if [ -z "$MODEL" ]; then
	# "Loop model: `<name>`" in RUNBOOK.md section 7.
	MODEL=$(sed -n 's/^\**Loop model:\**[[:space:]]*`\{0,1\}\([A-Za-z0-9._-]\{1,\}\)`\{0,1\}.*/\1/p' "$RUNBOOK" 2>/dev/null | head -1)
fi
[ -n "$MODEL" ] || { echo "run-plan: no model: pass --model, set COMICRACK_PLAN_MODEL or add a 'Loop model:' line to $RUNBOOK section 7" >&2; exit 2; }

PROMPT="You are the orchestrator of the ComicRack CE macOS port in $ROOT, branch macos. Read mac/plans/RUNBOOK.md completely and follow it exactly: apply the owner's answers in mac/plans/OWNER-ACTIONS.md, then run the tasks of mac/plans/full-port.md one at a time, each in a fresh subagent, verify and commit each, until a checkpoint or a STOP condition. Never push, merge or switch branches. Do one task only, then end the session."

# Prints the IDs of the next N runnable tasks, one per line, assuming each picked task ends done ("NONE" when nothing is runnable).
# The logic (section 6 order, Depends, checkpoints answered in OWNER-ACTIONS.md) lives in mac/tools/plan-status.sh.
pick() { mac/tools/plan-status.sh --pick "$1"; }

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
