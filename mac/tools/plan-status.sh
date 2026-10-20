#!/usr/bin/env bash
# Plan bookkeeping: the status table and "Run state:" next-task part of mac/plans/full-port.md from the Status lines of its task
# blocks, the next runnable task, and the run log backfilled from `git log` (RUNBOOK.md section 2 step 6).
#
#   mac/tools/plan-status.sh                 print the table and the next task
#   mac/tools/plan-status.sh --check         exit 0 when the plan's table and next task equal the Status lines, else 1
#   mac/tools/plan-status.sh --write         rewrite them
#   mac/tools/plan-status.sh --pick N        the next N runnable task IDs (run-plan.sh uses this)
#   mac/tools/plan-status.sh --backfill-log  append run-log rows for `mac: <task id> ...` commits that have none
#   mac/tools/plan-status.sh --check-log     exit 1 if such commits are missing from the run log
# Other options: --root DIR, --plan FILE, --log FILE (tests). Exit codes: 0 ok, 1 differences, 2 bad arguments.
exec python3 -I "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/plan-status/plan_status.py" "$@"
