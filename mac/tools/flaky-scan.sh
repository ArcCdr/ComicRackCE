#!/usr/bin/env bash
# Runs the fast gate (mac/tools/verify.sh) N times in a row, one after the other, keeps the logs of every run and summarises the failures.
#
#   mac/tools/flaky-scan.sh N [verify.sh options]     N >= 1; extra options go to verify.sh (for example --full, --no-build)
#
# Output: mac/artifacts/verify/flaky-<timestamp>/run-<i>/ (verify.log, build.log, test.log, *.trx of that run) and, at the end, a table
# of the runs (result, duration) and the failing tests with the number of runs they failed in. A test that fails in some runs but not in
# all of them is flaky. Never start a second copy while one runs: the runs share the build output and the logs of verify.sh.
# Exit codes: 0 every run passed, 1 at least one run failed, 2 bad arguments, 3 a run stopped on the disk guard.
set -uo pipefail

if [ $# -lt 1 ] || ! [[ "$1" =~ ^[0-9]+$ ]] || [ "$1" -lt 1 ]; then
	echo "usage: flaky-scan.sh N [verify.sh options]   (N >= 1)" >&2
	exit 2
fi
N=$1; shift

ROOT=$(git -C "$(dirname "$0")" rev-parse --show-toplevel 2>/dev/null) || { echo "flaky-scan: not inside the git repository" >&2; exit 2; }
cd "$ROOT" || exit 2
LOGS="$ROOT/mac/artifacts/verify"
OUT="$LOGS/flaky-$(date +%Y%m%d-%H%M%S)"
mkdir -p "$OUT"

RESULTS=()
STOPPED=0
FAIL_LIST="$OUT/failures.txt"
: > "$FAIL_LIST"

for i in $(seq 1 "$N"); do
	run="$OUT/run-$i"
	mkdir -p "$run"
	t0=$(date +%s)
	echo "flaky-scan: run $i of $N ..."
	mac/tools/verify.sh "$@" >"$run/verify.log" 2>&1
	rc=$?
	secs=$(( $(date +%s) - t0 ))
	cp "$LOGS"/build.log "$LOGS"/test.log "$LOGS"/*.trx "$run"/ 2>/dev/null || true
	if [ $rc -eq 0 ]; then
		word=PASS
	elif [ $rc -eq 3 ]; then
		word=STOP; STOPPED=1
	else
		word=FAIL
		# failing tests of this run: the "Failed <name> [time]" lines of the test log (same pattern as verify.sh), one entry per test and run
		names=$(grep -E '^[[:space:]]+Failed [A-Za-z]' "$run/test.log" 2>/dev/null | sed -E 's/^[[:space:]]+Failed //; s/ \[[0-9.,]+ [a-zA-Z]+\]$//' | sort -u)
		if [ -n "$names" ]; then
			printf '%s\n' "$names" | sed "s/^/run-$i	/" >> "$FAIL_LIST"
		else
			# a build failure, a rule failure or a crashed test host has no failing test line: keep the verify summary line
			grep -E '^(disk|rule-1|ledger|build|tests) +FAIL' "$run/verify.log" | sed -E 's/ +/ /g' | sed "s/^/run-$i	(gate step) /" >> "$FAIL_LIST"
		fi
	fi
	RESULTS+=("$(printf 'run-%-3s %-5s exit %s  %4s s  %s' "$i" "$word" "$rc" "$secs" "$(grep -E '^tests ' "$run/verify.log" | head -1 | sed -E 's/^tests +[A-Z]+ +//')")")
	[ $rc -eq 3 ] && break
done

echo
echo "flaky-scan summary ($(git rev-parse --short HEAD), $N run(s) requested, logs in ${OUT#"$ROOT"/})"
printf '%s\n' "${RESULTS[@]}"
passed=$(printf '%s\n' "${RESULTS[@]}" | grep -c ' PASS ')
echo "passed: $passed of ${#RESULTS[@]}"
if [ -s "$FAIL_LIST" ]; then
	echo
	echo "failures (number of runs, test):"
	cut -f2- "$FAIL_LIST" | sort | uniq -c | sort -rn | sed -E 's/^ +//'
fi
if [ "$STOPPED" = 1 ]; then echo "RESULT: STOP (disk guard)"; exit 3; fi
if [ "$passed" -eq "${#RESULTS[@]}" ]; then echo "RESULT: PASS"; exit 0; fi
echo "RESULT: FAIL"
exit 1
