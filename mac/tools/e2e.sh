#!/usr/bin/env bash
# End-to-end smoke suite of the real ComicRack Mac app (mac/plans/full-port.md section 5.3, task P0.6b).
# usage: mac/tools/e2e.sh [--bundle] [case ...]
#   --bundle   run against mac/artifacts/ComicRack CE (MVP).app (build it with mac/tools/make-app.sh) instead of the dev build
#   case       names of files in mac/tests/e2e/cases (without .sh); default: all, in name order
# Every case drives the app only through mac/tools/ui-drive/ui-drive.sh (it refuses to type into other apps) and uses a new
# temporary data folder. Environment: E2E_KEEP=1 keeps the data folders, E2E_ALLOW_APPEARANCE=1 lets a case switch light/dark.
# Output: one block per case ending in PASS, FAIL or SKIP; screenshots in mac/artifacts/e2e/<case>/.
# Exit codes: 0 no case failed, 1 a case failed, 2 bad arguments, 4 every case was skipped because the screen is locked or
# there is no GUI or Accessibility access (record the run as SKIPPED, never as passed).
set -uo pipefail

ROOT=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
CASES="$ROOT/mac/tests/e2e/cases"
MODE=dev
names=()
while [ $# -gt 0 ]; do
	case "$1" in
		--bundle) MODE=bundle ;;
		--dev) MODE=dev ;;
		-h|--help) sed -n '2,10p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; exit 0 ;;
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

pass=0; fail=0; skip=0; locked=0
for n in "${names[@]}"; do
	echo "=== $n ($MODE)"
	E2E_MODE=$MODE E2E_CASE=$n bash "$CASES/$n.sh"
	rc=$?
	case $rc in
		0) pass=$((pass + 1)) ;;
		3) skip=$((skip + 1)) ;;
		4) skip=$((skip + 1)); locked=$((locked + 1)) ;;
		*) fail=$((fail + 1)); echo "FAIL $n: the case ended with exit code $rc" ;;
	esac
done
echo "=== e2e summary ($MODE): $pass passed, $fail failed, $skip skipped (${locked} for a locked screen or missing GUI access)"
[ $fail -eq 0 ] || exit 1
[ $locked -eq ${#names[@]} ] && exit 4
exit 0
