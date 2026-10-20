#!/usr/bin/env bash
# The one command to run after `git merge upstream/master` (mac/plans/full-port.md §5.2, mac/UPSTREAM-SYNC.md).
#
#   mac/tools/upstream-sync-check.sh                       check the merged upstream range (state file .. upstream/master)
#   mac/tools/upstream-sync-check.sh --range A..B          check another range (both ends must be merged into HEAD)
#   mac/tools/upstream-sync-check.sh --no-merge            do not require the merge: preview the range, check the current tree
#   mac/tools/upstream-sync-check.sh --record              after a clean run (exit 0) store the synced upstream commit in
#                                                          mac/upstream-sync.json and commit that file
#   option --skip-verify: leave out step 4 (verify.sh --full and the census); for debugging the harness, never recorded
#
# Steps (each prints PASS, WARN, FAIL, INFO or SKIP):
#   1 preconditions   on macos, no merge in progress, clean tree (mac/artifacts and .DS_Store ignored), 3 GB free, state file
#   2 upstream files  mac/tools/upstream-scan: linked, replaced, excluded, UI not linked, project files, payload, build/CI, other
#   3 drift           mac/tools/replacement-sync.sh --all: replaced, stubbed or excluded originals that changed
#   4 build and tests mac/tools/verify.sh --full (drift tests that fail only because an original changed count as drift)
#   5 census          mac/tools/census, pass A and pass B: 0 errors
#   6 platform API    mac/tools/api-inventory --compare mac/reports/api-baseline.json, then mac/tools/drawing-coverage
#   7 summary         stdout and mac/artifacts/upstream-sync/report.md, last lines `NEXT:` and `EXIT:`
#
# --no-merge is a preview when the end of the range is not in HEAD yet: steps 2 and 3 are then capped at INFO (they show what
# the merge would bring, "would be FAIL after the merge"); steps 4 to 6 always check the current tree.
#
# Exit codes: 0 clean, 1 action items that a session can fix, 2 the build or the tests are broken (or a tool could not run),
#             3 a precondition failed (nothing was checked), 4 reserved.   Bad arguments: 2.
# Needs git, jq, dotnet; the steps need perl and shasum through replacement-sync.sh.
set -uo pipefail

RANGE=""; NOMERGE=0; RECORD=0; SKIPVERIFY=0
while [ $# -gt 0 ]; do
	case "$1" in
		--range) shift; RANGE="${1:-}"; [ -n "$RANGE" ] || { echo "upstream-sync-check: --range needs A..B" >&2; exit 2; } ;;
		--no-merge) NOMERGE=1 ;;
		--record) RECORD=1 ;;
		--skip-verify) SKIPVERIFY=1 ;;
		-h|--help) sed -n '2,27p' "$0"; exit 0 ;;
		*) echo "upstream-sync-check: unknown option $1" >&2; exit 2 ;;
	esac
	shift
done
if [ -n "$RANGE" ]; then
	case "$RANGE" in
		*..*) ;;
		*) echo "upstream-sync-check: --range must look like A..B" >&2; exit 2 ;;
	esac
	case "$RANGE" in *...*) echo "upstream-sync-check: use A..B, not A...B" >&2; exit 2 ;; esac
fi
if [ "$RECORD" = 1 ] && [ "$NOMERGE" = 1 ]; then echo "upstream-sync-check: --record needs the merged tree; drop --no-merge" >&2; exit 2; fi
if [ "$RECORD" = 1 ] && [ "$SKIPVERIFY" = 1 ]; then echo "upstream-sync-check: --record cannot be combined with --skip-verify" >&2; exit 2; fi

ROOT=$(git -C "$(dirname "$0")" rev-parse --show-toplevel 2>/dev/null) || { echo "upstream-sync-check: not inside the git repository" >&2; exit 2; }
cd "$ROOT" || exit 2
OUT="$ROOT/mac/artifacts/upstream-sync"
STATE="$ROOT/mac/upstream-sync.json"
START=$(date +%s)

VERDICTS=()   # "name|VERDICT|text"
DETAILS=()    # "name|line"
verdict() { VERDICTS+=("$1|$2|$3"); }
detail() { DETAILS+=("$1|$2"); }
fail3() { echo "upstream-sync-check: PRECONDITION FAILED: $1" >&2; echo "NEXT: $2" >&2; echo "EXIT: 3" >&2; exit 3; }
progress() { echo "[$(( $(date +%s) - START )) s] $1" >&2; }

# ---------------------------------------------------------------- 1. preconditions
for tool in git jq; do
	command -v "$tool" >/dev/null 2>&1 || fail3 "$tool is required" "install $tool (brew install $tool)"
done
free_kb=$(df -Pk "$HOME" | awk 'NR==2 {print $4}')
free_gb=$(awk -v k="$free_kb" 'BEGIN { printf "%.1f", k / 1048576 }')
[ "$free_kb" -ge $((3 * 1048576)) ] || fail3 "only $free_gb GB free on the home volume (limit 3 GB)" "delete mac/**/bin, mac/**/obj and old mac/artifacts folders, then run the check again"
branch=$(git rev-parse --abbrev-ref HEAD 2>/dev/null)
if [ "$branch" != macos ] && [ "${UPSTREAM_SYNC_ANY_BRANCH:-0}" != 1 ]; then
	fail3 "the current branch is '$branch', not macos" "git switch macos (CI sets UPSTREAM_SYNC_ANY_BRANCH=1)"
fi
[ ! -e "$(git rev-parse --git-path MERGE_HEAD)" ] || fail3 "a merge is in progress" "finish it (git commit) or abort it (git merge --abort), then run the check again"
dirty=$(git status --porcelain --untracked-files=all | grep -vE '(^| |/)\.DS_Store$' | grep -vE ' mac/artifacts/' || true)
[ -z "$dirty" ] || fail3 "uncommitted changes:
$(printf '%s\n' "$dirty" | head -10)" "commit or stash them, then run the check again"
[ -f "$STATE" ] || fail3 "mac/upstream-sync.json is missing" "restore it: git checkout -- mac/upstream-sync.json"
LAST=$(jq -r '.lastSyncedUpstream // empty' "$STATE" 2>/dev/null)
[ -n "$LAST" ] && git cat-file -e "$LAST^{commit}" 2>/dev/null || fail3 "lastSyncedUpstream in mac/upstream-sync.json is not a commit of this clone" "git fetch upstream, or fix the file"

# Range: FROM..TO.
if [ -n "$RANGE" ]; then
	FROM_REF="${RANGE%%..*}"; TO_REF="${RANGE#*..}"
else
	FROM_REF="$LAST"
	if git rev-parse --verify -q upstream/master >/dev/null; then
		if git merge-base --is-ancestor upstream/master HEAD 2>/dev/null; then TO_REF=upstream/master
		elif git rev-parse --verify -q HEAD^2 >/dev/null && [ "$NOMERGE" = 0 ]; then TO_REF=HEAD^2
		else TO_REF=upstream/master; fi
	elif git rev-parse --verify -q HEAD^2 >/dev/null; then TO_REF=HEAD^2
	else fail3 "upstream/master does not exist in this clone" "git fetch upstream"; fi
fi
FROM=$(git rev-parse --verify -q "$FROM_REF^{commit}") || fail3 "cannot resolve the start of the range ($FROM_REF)" "git fetch upstream, or pass --range with commits that exist"
TO=$(git rev-parse --verify -q "$TO_REF^{commit}") || fail3 "cannot resolve the end of the range ($TO_REF)" "git fetch upstream, or pass --range with commits that exist"
if git merge-base --is-ancestor "$TO" HEAD 2>/dev/null; then MERGED=1; else MERGED=0; fi
if [ "$MERGED" = 0 ] && [ "$NOMERGE" = 0 ]; then
	fail3 "the end of the range (${TO:0:7}) is not merged into HEAD" "git merge --no-ff upstream/master first (mac/UPSTREAM-SYNC.md §2), or use --no-merge for a preview"
fi
PREVIEW=$((1 - MERGED))
COMMITS=$(git rev-list --count "$FROM..$TO")

mkdir -p "$OUT"
rm -f "$OUT"/*.txt "$OUT"/*.json "$OUT"/*.log "$OUT"/*.md
mode=merged; [ "$PREVIEW" = 1 ] && mode="preview, not merged"
verdict preconditions PASS "on $branch, clean tree, $free_gb GB free"
verdict upstream INFO "${FROM:0:7}..${TO:0:7}: $COMMITS upstream commit(s) ($mode)"

# A verdict of a range step; a preview caps FAIL and WARN at INFO.
range_verdict() { # name verdict text
	if [ "$PREVIEW" = 1 ] && { [ "$2" = FAIL ] || [ "$2" = WARN ]; }; then
		verdict "$1" INFO "$3 (preview: would be $2 after the merge)"
	else
		verdict "$1" "$2" "$3"
	fi
}

# ---------------------------------------------------------------- 2. upstream files
progress "step 2: classifying the files of $FROM..$TO"
SCAN_JSON="$OUT/scan.json"
if [ "$COMMITS" = 0 ]; then
	verdict files PASS "no upstream change in the range"
else
	if dotnet run --project mac/tools/upstream-scan -- --range "$FROM..$TO" --json "$SCAN_JSON" >"$OUT/scan.txt" 2>&1; then
		counts=$(jq -r '.Counts | to_entries | map(select(.value > 0) | "\(.key) \(.value)") | join(", ")' "$SCAN_JSON")
		verdict files INFO "${counts:-no file changed}"
		actions=$(jq -r '.Files[] | select(.Action != null and .Category != "replaced" and .Category != "excluded" and .Category != "project files") | "\(.Status) \(.Path) [\(.Category)]: \(.Action)"' "$SCAN_JSON")
		if [ -n "$actions" ]; then
			n=$(printf '%s\n' "$actions" | grep -c .)
			range_verdict action-files WARN "$n changed file(s) need a follow-up (new file that does not compile, deleted file named by a Sources.props, build settings)"
			while IFS= read -r l; do detail action-files "$l"; done <<<"$actions"
		fi
		newlinked=$(jq -r '.Files[] | select(.Category == "linked" and .Status == "A") | .Path' "$SCAN_JSON")
		if [ -n "$newlinked" ]; then
			n=$(printf '%s\n' "$newlinked" | grep -c .)
			verdict new-files INFO "$n new file(s) that the shadow projects compile (the build and the census below confirm they compile)"
			while IFS= read -r l; do detail new-files "$l"; done < <(printf '%s\n' "$newlinked" | head -15)
		fi
		pk=$(jq -r '.Packages[] | select(.Mac == "absent" or .Mac == "different" or .Mac == "still-referenced") | "\(.Kind) \(.Id) \(.Change) (\(.OldVersion // "-") -> \(.NewVersion // "-")) in \(.Project): mac \(.Mac)\(if .MacVersion then " " + .MacVersion else "" end); mirror it in \(.MacFile // "the mac project file")"' "$SCAN_JSON")
		if [ -n "$pk" ]; then
			n=$(printf '%s\n' "$pk" | grep -c .)
			range_verdict packages WARN "$n package or assembly reference change(s) that the mac project files do not mirror"
			while IFS= read -r l; do detail packages "$l"; done <<<"$pk"
		else
			pn=$(jq -r '[.Packages[]] | length' "$SCAN_JSON")
			[ "$pn" = 0 ] || verdict packages INFO "$pn reference change(s), all mirrored, replaced or dropped on the Mac (details in mac/artifacts/upstream-sync/scan.txt)"
		fi
		ui=$(jq -r '.Files[] | select(.Category == "UI not linked") | "\(.Status) \(.Path)"' "$SCAN_JSON")
		if [ -n "$ui" ]; then
			n=$(printf '%s\n' "$ui" | grep -c .)
			verdict ui-parity INFO "$n changed Windows UI file(s): parity candidates for the Mac app (mac/plans/parity-ledger.md; a U-task adds a ledger row per new or changed feature)"
			while IFS= read -r l; do detail ui-parity "$l"; done < <(printf '%s\n' "$ui" | head -25)
			[ "$n" -le 25 ] || detail ui-parity "... and $((n - 25)) more (mac/artifacts/upstream-sync/scan.txt)"
		fi
		pl=$(jq -r '[.Files[] | select(.Category == "payload")] | length' "$SCAN_JSON")
		[ "$pl" = 0 ] || verdict payload INFO "$pl changed file(s) under ComicRack/Output (Languages, Scripts, Help, Resources): nothing to do unless the bundle copies them wrongly"
		ours=$(jq -r '[.Files[] | select(.Category == "macOS (ours)")] | length' "$SCAN_JSON")
		[ "$ours" = 0 ] || verdict ours INFO "$ours file(s) under mac/ in the range: upstream never touches mac/, check the range"
	else
		verdict files FAIL "upstream-scan failed (mac/artifacts/upstream-sync/scan.txt)"
		detail files "$(tail -3 "$OUT/scan.txt" | tr '\n' ' ')"
		TOOLFAIL=1
	fi
fi

# ---------------------------------------------------------------- 3. replacement drift
progress "step 3: replacement drift"
if [ "$PREVIEW" = 1 ]; then
	DRIFT_ARGS=(--all --against "$TO")
else
	DRIFT_ARGS=(--all)
fi
mac/tools/replacement-sync.sh ${DRIFT_ARGS[@]+"${DRIFT_ARGS[@]}"} >"$OUT/drift.txt" 2>&1
drc=$?
changed=$(grep ': CHANGED since base' "$OUT/drift.txt" || true)
if [ "$drc" -ge 2 ]; then
	verdict drift FAIL "replacement-sync.sh could not run (mac/artifacts/upstream-sync/drift.txt)"
	detail drift "$(tail -3 "$OUT/drift.txt" | tr '\n' ' ')"
	TOOLFAIL=1
elif [ -n "$changed" ]; then
	n=$(printf '%s\n' "$changed" | grep -c .)
	range_verdict drift FAIL "$n replaced, stubbed or excluded original(s) changed"
	while IFS= read -r line; do
		key="${line%%: CHANGED since base*}"
		kind=$(printf '%s' "$line" | sed -E 's/.*\(([a-z-]+)\); recorded.*/\1/')
		if [ "$PREVIEW" = 1 ]; then
			if [ "$kind" = patched-copy ]; then
				detail drift "$key ($kind): after the merge: mac/tools/replacement-sync.sh $key (merges the change into the copy), then --accept"
			else
				detail drift "$key ($kind): after the merge: mac/tools/replacement-sync.sh $key (diff to port by hand), then --accept"
			fi
		else
			res=$(mac/tools/replacement-sync.sh "$key" 2>&1)
			result=$(printf '%s\n' "$res" | grep '^RESULT:' | head -1)
			next=$(printf '%s\n' "$res" | grep '^NEXT:' | head -1)
			detail drift "$key ($kind): ${result#RESULT: }"
			[ -z "$next" ] || detail drift "    ${next}"
		fi
	done <<<"$changed"
else
	verdict drift PASS "$(tail -1 "$OUT/drift.txt" | sed 's/^replacement-sync: //'), no original changed since its recorded hash"
fi

# ---------------------------------------------------------------- 4. build and tests, 5. census
BUILD_BROKEN=0
if [ "$SKIPVERIFY" = 1 ]; then
	verdict build SKIP "--skip-verify"
	verdict tests SKIP "--skip-verify"
	verdict census SKIP "--skip-verify"
else
	progress "step 4: mac/tools/verify.sh --full (a few minutes)"
	mac/tools/verify.sh --full >"$OUT/verify.log" 2>&1
	vrc=$?
	# the summary line of one check and the indented lines that follow it
	vlines() { awk -v n="$1" 'f && /^ / {print; next} {f = ($1 == n)} f {print}' "$OUT/verify.log"; }
	if [ "$vrc" = 3 ]; then
		fail3 "verify.sh stopped: less than 3 GB free" "free disk space, then run the check again"
	fi
	for name in rule-1 build tests; do
		first=$(vlines "$name" | head -1)
		[ -n "$first" ] || continue
		vv=$(printf '%s' "$first" | awk '{print $2}')
		txt=$(printf '%s' "$first" | sed -E 's/^[^ ]+ +[A-Z]+ +//')
		if [ "$vv" = PASS ]; then
			verdict "$name" PASS "$txt"
		elif [ "$name" = rule-1 ]; then
			verdict rule-1 FAIL "$txt"
			while IFS= read -r l; do detail rule-1 "$(printf '%s' "$l" | sed -E 's/^ +//')"; done < <(vlines rule-1 | tail -n +2 | head -10)
		elif [ "$name" = build ]; then
			verdict build FAIL "$txt"
			BUILD_BROKEN=1
			while IFS= read -r l; do detail build "$(printf '%s' "$l" | sed -E 's/^ +//')"; done < <(vlines build | tail -n +2 | head -8)
		else
			failing=$(grep -E '^\s+Failed [A-Za-z]' "$ROOT/mac/artifacts/verify/test.log" 2>/dev/null | sed -E 's/^\s+Failed //; s/ \[.*$//' | sort -u)
			others=$(printf '%s\n' "$failing" | grep -vE 'Every_original_still_has_the_recorded_hash|The_original_CacheManager_is_unchanged|The_replacement_differs_from_the_original_only|Patched_copies_stay_within_their_changed_line_budget' | grep . || true)
			if [ -n "$failing" ] && [ -z "$others" ] && [ -n "$changed" ]; then
				verdict tests FAIL "only the drift guards fail ($(printf '%s\n' "$failing" | grep -c .) test(s)): they pass again once step 3 is resolved"
				while IFS= read -r l; do detail tests "$l"; done <<<"$failing"
			else
				verdict tests FAIL "$txt"
				BUILD_BROKEN=1
				while IFS= read -r l; do detail tests "$(printf '%s' "$l" | sed -E 's/^ +//')"; done < <(vlines tests | tail -n +2 | head -10)
			fi
		fi
	done
	if [ "$BUILD_BROKEN" = 1 ] && ! printf '%s\n' "${VERDICTS[@]}" | grep -q '^tests|'; then
		verdict tests SKIP "the build failed, no test ran"
	fi
	if [ "$vrc" != 0 ] && ! printf '%s\n' "${VERDICTS[@]}" | grep -q '^\(build\|tests\)|FAIL'; then
		verdict build FAIL "verify.sh exited $vrc without a failing build or test line (mac/artifacts/upstream-sync/verify.log)"
		BUILD_BROKEN=1
	fi

	progress "step 5: census"
	for pass in A B; do
		dotnet run --project mac/tools/census -- --pass "$pass" --out "$OUT/census-$pass.md" >"$OUT/census-$pass.log" 2>&1
		crc=$?
		total=$(grep -E "TOTAL unique errors" "$OUT/census-$pass.log" | tail -1)
		errs=$(printf '%s' "$total" | sed -nE 's/.*errors \(full Roslyn diagnostics\): ([0-9]+); other.*: ([0-9]+).*/\1 \2/p')
		if [ -z "$errs" ]; then
			verdict "census-$pass" FAIL "census pass $pass did not finish (exit $crc, mac/artifacts/upstream-sync/census-$pass.log)"
			TOOLFAIL=1
		elif [ "$errs" = "0 0" ]; then
			verdict "census-$pass" PASS "pass $pass: 0 errors"
		else
			verdict "census-$pass" FAIL "pass $pass: ${errs% *} compiler error(s), ${errs#* } other error(s) (mac/artifacts/upstream-sync/census-$pass.md)"
			while IFS= read -r l; do detail "census-$pass" "$l"; done < <(grep -E 'error [A-Z]+[0-9]+' "$OUT/census-$pass.log" 2>/dev/null | head -5)
		fi
	done
fi

# ---------------------------------------------------------------- 6. new platform API
progress "step 6: platform API against mac/reports/api-baseline.json"
dotnet run --project mac/tools/api-inventory -- --compare mac/reports/api-baseline.json --json "$OUT/api-delta.json" >"$OUT/api.txt" 2>&1
arc=$?
if [ "$arc" = 0 ]; then
	verdict new-api PASS "no new platform API"
elif [ "$arc" = 1 ] && [ -f "$OUT/api-delta.json" ]; then
	unc=$(jq -r '.uncovered' "$OUT/api-delta.json")
	nt=$(jq -r '.newTypes | length' "$OUT/api-delta.json"); nm=$(jq -r '.newMembers | length' "$OUT/api-delta.json"); nd=$(jq -r '.newDllImports | length' "$OUT/api-delta.json")
	bad=$(jq -r '[.newDllImports[] | select(.linked != "no")] | length' "$OUT/api-delta.json")
	txt="new platform API in linked code: $nt type(s), $nm member(s), $nd DllImport(s); $unc not covered by the shims, $bad linked DllImport(s)"
	if [ "$unc" != 0 ] || [ "$bad" != 0 ]; then verdict new-api FAIL "$txt"; else verdict new-api WARN "$txt (all covered: review, then record it with api-inventory --write-baseline)"; fi
	while IFS= read -r l; do detail new-api "$l"; done < <(grep -E '^  [A-Za-z]' "$OUT/api.txt" | sed -E 's/^ +//' | head -20)
else
	verdict new-api FAIL "api-inventory could not run (exit $arc, mac/artifacts/upstream-sync/api.txt)"
	detail new-api "$(tail -3 "$OUT/api.txt" | tr '\n' ' ')"
	TOOLFAIL=1
fi
dotnet run --project mac/tools/drawing-coverage >"$OUT/drawing.txt" 2>&1
if [ $? = 0 ] && grep -q '^PASS' "$OUT/drawing.txt"; then
	verdict drawing PASS "$(grep '^PASS' "$OUT/drawing.txt" | head -1)"
else
	verdict drawing FAIL "drawing-coverage does not pass (mac/artifacts/upstream-sync/drawing.txt)"
	while IFS= read -r l; do detail drawing "$l"; done < <(tail -6 "$OUT/drawing.txt")
fi

# ---------------------------------------------------------------- 7. summary
EXIT=0
for v in ${VERDICTS[@]+"${VERDICTS[@]}"}; do
	case "$v" in *'|FAIL|'*|*'|WARN|'*) EXIT=1 ;; esac
done
if [ "$BUILD_BROKEN" = 1 ] || [ "${TOOLFAIL:-0}" = 1 ]; then EXIT=2; fi

case "$EXIT" in
	0) if [ "$PREVIEW" = 1 ]; then NEXT="nothing to fix on the current tree; this was a preview, run the check without --no-merge after git merge --no-ff upstream/master"
	   elif [ "$SKIPVERIFY" = 1 ]; then NEXT="clean, but steps 4 and 5 were skipped: run the check again without --skip-verify"
	   else NEXT="clean; record it with mac/tools/upstream-sync-check.sh --record"; fi ;;
	1) NEXT="paste the 'After you merged upstream' prompt of mac/plans/RUNBOOK.md §1; a session turns each FAIL and WARN into a U-task, fixes it and runs this check again until it exits 0" ;;
	2) NEXT="the Mac build, its tests or a tool is broken: paste the 'After you merged upstream' prompt of mac/plans/RUNBOOK.md §1; do not build a release until the check passes" ;;
esac
[ "$PREVIEW" = 1 ] && [ "$EXIT" != 0 ] && NEXT="$NEXT (the current tree is checked; the preview of the merge is in steps 2 and 3)"

render() {
	echo "upstream-sync-check $(git rev-parse --short HEAD) on $branch: ${FROM:0:7}..${TO:0:7}, $COMMITS upstream commit(s), $mode, $(( $(date +%s) - START )) s"
	local v name vv text d
	for v in ${VERDICTS[@]+"${VERDICTS[@]}"}; do
		name="${v%%|*}"; v="${v#*|}"; vv="${v%%|*}"; text="${v#*|}"
		printf '%-5s %-14s %s\n' "$vv" "$name" "$text"
		for d in ${DETAILS[@]+"${DETAILS[@]}"}; do
			[ "${d%%|*}" = "$name" ] && printf '                       %s\n' "${d#*|}"
		done
	done
	echo "NEXT: $NEXT"
	echo "EXIT: $EXIT"
}
render | tee "$OUT/report.md"
DIGEST=$(render | grep -vE '^upstream-sync-check ' | shasum -a 256 | cut -d' ' -f1)

# ---------------------------------------------------------------- --record
if [ "$RECORD" = 1 ]; then
	if [ "$EXIT" != 0 ]; then echo "upstream-sync-check: not recorded: the check did not pass" >&2
	else
		tmp=$(mktemp)
		jq --arg c "$TO" --arg d "$(date -u +%Y-%m-%d)" --arg g "$DIGEST" '.lastSyncedUpstream = $c | .date = $d | .reportDigest = $g' "$STATE" >"$tmp" && mv "$tmp" "$STATE"
		git add -- mac/upstream-sync.json
		if git diff --cached --quiet -- mac/upstream-sync.json; then echo "upstream-sync-check: mac/upstream-sync.json is already up to date"
		else
			git commit -q -m "mac: record upstream sync ${TO:0:7} (upstream-sync-check passed)" -- mac/upstream-sync.json \
				&& echo "upstream-sync-check: recorded ${TO:0:7} in mac/upstream-sync.json and committed it"
		fi
	fi
fi
exit "$EXIT"
