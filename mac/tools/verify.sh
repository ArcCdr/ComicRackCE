#!/usr/bin/env bash
# Fast gate for the macOS port (mac/plans/full-port.md §2, mac/plans/RUNBOOK.md).
#
#   mac/tools/verify.sh            disk guard + rule-1 check + build mac/ComicRack.Mac.slnx + fast tests
#   mac/tools/verify.sh --full     same, and also the tests marked [Trait("Category", "Slow")]
#   options: --no-build (reuse the last build), --no-test, --config Release, --keep-logs
#
# Test categories: tests without a Category trait are "fast". "Slow" runs only with --full. "E2E" never runs here
# (mac/tools/e2e.sh drives the real app; see full-port.md P0.6b).
#
# Exit codes: 0 all steps passed, 1 a step failed, 2 bad arguments, 3 STOP: less than 3 GB free on the home volume.
# Logs: mac/artifacts/verify/ (git-ignored): build.log, test.log and one .trx per test assembly.
set -uo pipefail

FULL=0; BUILD=1; TEST=1; CONFIG=Debug
while [ $# -gt 0 ]; do
	case "$1" in
		--full) FULL=1 ;;
		--no-build) BUILD=0 ;;
		--no-test) TEST=0 ;;
		--config) shift; CONFIG="${1:-}"; [ -n "$CONFIG" ] || { echo "verify: --config needs a value" >&2; exit 2; } ;;
		--keep-logs) ;; # logs are always kept; accepted for symmetry with other tools
		-h|--help) sed -n '2,13p' "$0"; exit 0 ;;
		*) echo "verify: unknown option $1" >&2; exit 2 ;;
	esac
	shift
done

ROOT=$(git -C "$(dirname "$0")" rev-parse --show-toplevel 2>/dev/null) || { echo "verify: not inside the git repository" >&2; exit 2; }
cd "$ROOT" || exit 2
LOGS="$ROOT/mac/artifacts/verify"
mkdir -p "$LOGS"
rm -f "$LOGS"/*.trx "$LOGS"/build.log "$LOGS"/test.log

SUMMARY=()
FAILED=0
add() { SUMMARY+=("$(printf '%-8s %-6s %s' "$1" "$2" "$3")"); [ "$2" = FAIL ] && FAILED=1; return 0; }
now() { date +%s; }

# 1. Disk guard (STOP rule: less than 3 GB free).
free_kb=$(df -Pk "$HOME" | awk 'NR==2 {print $4}')
free_gb=$(awk -v k="$free_kb" 'BEGIN { printf "%.1f", k / 1048576 }')
if [ "$free_kb" -lt $((3 * 1048576)) ]; then
	echo "verify: STOP: only ${free_gb} GB free on the home volume (limit 3 GB). Delete mac/**/bin, mac/**/obj and old artifacts first." >&2
	exit 3
fi
add disk PASS "${free_gb} GB free"

# 2. Rule 1: no file outside mac/ (plus MACOS_NATIVE_PATH.md and .github/workflows/mac-*) differs from the upstream base,
#    except files approved in mac/EDITS.md. Base: upstream/master if that ref exists (after `git fetch upstream`), else master.
if git rev-parse --verify -q upstream/master >/dev/null; then BASE=upstream/master; else BASE=master; fi
ALLOWED='^(mac/|MACOS_NATIVE_PATH\.md$|\.github/workflows/mac-)'
approved=$(grep -E '^\| *`[^`]+`' mac/EDITS.md 2>/dev/null | sed -E 's/^\| *`([^`]+)`.*/\1/' || true)
offenders=$( { git diff --name-only "$BASE"...HEAD; git status --porcelain --untracked-files=all | sed -E 's/^...//; s/.* -> //'; } \
	| sort -u | grep -vE "$ALLOWED" | grep -vE '(^|/)\.DS_Store$' || true)
if [ -n "$approved" ] && [ -n "$offenders" ]; then
	offenders=$(printf '%s\n' "$offenders" | grep -vxF -f <(printf '%s\n' "$approved") || true)
fi
if [ -n "$offenders" ]; then
	add rule-1 FAIL "files outside mac/ changed against $BASE:"
	while IFS= read -r f; do SUMMARY+=("                $f"); done <<< "$offenders"
else
	add rule-1 PASS "nothing outside mac/ changed against $BASE"
fi

# 3. Build. The Compat.*.Tests drift guards need the Microsoft.WindowsDesktop.App.Ref pack, which a pass A build downloads.
if [ "$BUILD" = 1 ]; then
	t0=$(now)
	pack="${NUGET_PACKAGES:-$HOME/.nuget/packages}/microsoft.windowsdesktop.app.ref"
	if [ ! -d "$pack" ]; then
		echo "verify: reference pack missing, running a pass A build once to download it"
		dotnet build mac/ComicRack.Plugins.Core/ComicRack.Plugins.Core.csproj -p:CensusPass=A -nologo -v q >"$LOGS/build-passA.log" 2>&1 \
			|| { add build FAIL "pass A build for the reference pack failed (mac/artifacts/verify/build-passA.log)"; }
	fi
	if dotnet build mac/ComicRack.Mac.slnx -c "$CONFIG" -nologo -v q >"$LOGS/build.log" 2>&1; then
		warnings=$(grep -Eo '[0-9]+ Warning\(s\)' "$LOGS/build.log" | tail -1 | awk '{print $1}')
		add build PASS "$CONFIG, 0 errors, ${warnings:-?} warnings, $(( $(now) - t0 )) s"
	else
		errors=$(grep -E ': error ' "$LOGS/build.log" | sort -u)
		add build FAIL "$(printf '%s\n' "$errors" | grep -c . | tr -d ' ') errors (mac/artifacts/verify/build.log); first ones:"
		while IFS= read -r e; do SUMMARY+=("                ${e#"$ROOT"/}"); done < <(printf '%s\n' "$errors" | head -5)
		TEST=0
	fi
fi

# 4. Tests (no rebuild). Fast = no Category trait; --full adds Slow. E2E is never run here.
if [ "$TEST" = 1 ]; then
	t0=$(now)
	if [ "$FULL" = 1 ]; then FILTER='Category!=E2E'; KIND='fast+slow'; else FILTER='Category!=Slow&Category!=E2E'; KIND='fast'; fi
	dotnet test mac/ComicRack.Mac.slnx -c "$CONFIG" --no-build --filter "$FILTER" --logger trx --results-directory "$LOGS" \
		--blame-hang-timeout 15m >"$LOGS/test.log" 2>&1
	rc=$?
	lines=$(grep -E '^(Passed|Failed)!' "$LOGS/test.log" | sed -E 's/ - Failed:/ failed:/; s/, Duration:/ in/')
	if [ $rc -eq 0 ] && [ -n "$lines" ]; then
		total=$(grep -Eo 'Passed: +[0-9]+' "$LOGS/test.log" | awk '{s += $2} END {print s + 0}')
		add tests PASS "$KIND, $total passed, $(( $(now) - t0 )) s"
	else
		add tests FAIL "$KIND, exit $rc (mac/artifacts/verify/test.log); failing tests:"
		while IFS= read -r e; do SUMMARY+=("                $e"); done < <(grep -E '^\s+Failed [A-Za-z]' "$LOGS/test.log" | sed -E 's/^\s+//' | sort -u | head -10)
	fi
	while IFS= read -r l; do SUMMARY+=("                $l"); done <<< "$lines"
fi

echo
echo "verify.sh summary ($(git rev-parse --short HEAD), $(git rev-parse --abbrev-ref HEAD))"
printf '%s\n' "${SUMMARY[@]}"
if [ "$FAILED" = 1 ]; then echo "RESULT: FAIL"; exit 1; fi
echo "RESULT: PASS"
exit 0
