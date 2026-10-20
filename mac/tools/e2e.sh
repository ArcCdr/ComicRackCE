#!/usr/bin/env bash
# End-to-end smoke suite of the real ComicRack Mac app (mac/plans/full-port.md section 5.3, task P0.6b).
# usage: mac/tools/e2e.sh [--bundle] [case ...]
#   --bundle   run against mac/artifacts/ComicRack CE (MVP).app (build it with mac/tools/make-app.sh) instead of the dev build
#   case       names of files in mac/tests/e2e/cases (without .sh); default: all, in name order
# Every case drives the app only through mac/tools/ui-drive/ui-drive.sh (it refuses to type into other apps) and uses a new
# temporary data folder. Environment: E2E_KEEP=1 keeps the data folders, E2E_ALLOW_APPEARANCE=1 lets a case switch light/dark.
# Output: one block per case ending in PASS, FAIL or SKIP; screenshots in mac/artifacts/e2e/<case>/.
# Case exit codes: 0 pass, 1 fail, 3 skipped (fixture or bundle missing), 4 skipped (screen locked or no GUI/Accessibility access),
# 6 skipped (ui-drive refused to launch: an owner-run copy of the app is running).
# Exit codes of this script: 0 at least one case passed and none failed, 1 a case failed, 2 bad arguments, 3 STOP: less than 3 GB
# free, 4 no case passed and every case was skipped because the screen is locked or there is no GUI or Accessibility access,
# 5 no case passed (every case was skipped for a missing fixture or bundle, or nothing ran), 6 no case passed and a launch was
# refused (another copy of the app is running; quit it and run again). Exit 4, 5 and 6 are never a pass: record the run as SKIPPED.
# E2E_CASES_DIR (tests of this script) replaces the folder of the cases.
set -uo pipefail

ROOT=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
CASES="${E2E_CASES_DIR:-$ROOT/mac/tests/e2e/cases}"
MODE=dev
names=()
while [ $# -gt 0 ]; do
	case "$1" in
		--bundle) MODE=bundle ;;
		--dev) MODE=dev ;;
		-h|--help) sed -n '2,16p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; exit 0 ;;
		-*) echo "e2e: unknown option $1" >&2; exit 2 ;;
		*) names+=("$1") ;;
	esac
	shift
done

if [ ${#names[@]} -eq 0 ]; then
	for f in "$CASES"/*.sh; do [ -e "$f" ] && names+=("$(basename "$f" .sh)"); done
fi
[ ${#names[@]} -gt 0 ] || { echo "e2e: no cases found in $CASES" >&2; exit 2; }
for n in "${names[@]}"; do
	[ -f "$CASES/$n.sh" ] || { echo "e2e: no such case: $n (have: $(ls "$CASES" | sed 's/\.sh$//' | tr '\n' ' '))" >&2; exit 2; }
done

FREE_KB=$(df -k "$ROOT" | awk 'NR==2 {print $4}')
if [ "$FREE_KB" -lt 3145728 ]; then echo "e2e: STOP, less than 3 GB free disk." >&2; exit 3; fi

pass=0; fail=0; skip=0; locked=0; refused=0
for n in "${names[@]}"; do
	echo "=== $n ($MODE)"
	E2E_MODE=$MODE E2E_CASE=$n bash "$CASES/$n.sh"
	rc=$?
	case $rc in
		0) pass=$((pass + 1)) ;;
		3) skip=$((skip + 1)) ;;
		4) skip=$((skip + 1)); locked=$((locked + 1)) ;;
		6) skip=$((skip + 1)); refused=$((refused + 1)) ;;
		*) fail=$((fail + 1)); echo "FAIL $n: the case ended with exit code $rc" ;;
	esac
done
echo "=== e2e summary ($MODE): $pass passed, $fail failed, $skip skipped (${locked} for a locked screen or missing GUI access, ${refused} for a refused launch)"
[ $fail -eq 0 ] || exit 1
if [ $pass -eq 0 ]; then
	if [ $locked -eq ${#names[@]} ]; then exit 4; fi
	if [ $refused -gt 0 ]; then exit 6; fi
	echo "e2e: no case passed" >&2
	exit 5
fi
exit 0
