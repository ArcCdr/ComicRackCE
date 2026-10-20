#!/usr/bin/env bash
# Re-sync a replaced, stubbed or excluded original after an upstream change (mac/plans/full-port.md §5.1).
#
#   mac/tools/replacement-sync.sh <original-or-pattern>             dry run: compare with the recorded hash, show what to do
#   mac/tools/replacement-sync.sh <original-or-pattern> --accept    apply it (patched-copy: auto-merge; others: record the new hash after a port by hand)
#   mac/tools/replacement-sync.sh <original-or-pattern> --record    only record the current hash and base (after resolving by hand)
#   mac/tools/replacement-sync.sh --all                             dry run for every manifest entry; exit 1 if any original changed
#   mac/tools/replacement-sync.sh --list                            print the keys of the manifest
#   option: --against <ref>   compare with the original as it is in <ref> (for example upstream/master) instead of the working tree
#
# <original-or-pattern> is the "original" (or "pattern") value of an entry of mac/replacements.json, for example
# ComicRack.Engine/CacheManager.cs. Hash = SHA-256 of the file without BOM and with LF line endings; a glob pattern hashes the
# sorted "path sha256" lines of the matched files (obj/ and bin/ ignored).
#
# patched-copy: three-way merge with `git merge-file` (ours = the replacement, base = the original at the recorded base commit,
#   theirs = the original now). Clean merge: --accept writes it and records the new hash. Conflicts: the merged text with
#   markers goes to mac/artifacts/replacement-sync/, nothing is changed; resolve in the replacement, then run --record.
# rewrite, stub, excluded: prints `git diff <base> -- <original>`; port the change by hand, then --accept (= --record).
#
# Exit codes: 0 unchanged, or merged / recorded; 1 the original changed and needs action (conflict, or a port by hand);
#             2 bad arguments or manifest problem. Needs git, jq, perl, shasum.
set -uo pipefail

ROOT=$(git -C "$(dirname "$0")" rev-parse --show-toplevel 2>/dev/null) || { echo "replacement-sync: not inside the git repository" >&2; exit 2; }
cd "$ROOT" || exit 2
MANIFEST="${COMICRACK_REPLACEMENT_MANIFEST:-$ROOT/mac/replacements.json}"
OUT="$ROOT/mac/artifacts/replacement-sync"

for tool in jq perl shasum; do
	command -v "$tool" >/dev/null 2>&1 || { echo "replacement-sync: $tool is required" >&2; exit 2; }
done
[ -f "$MANIFEST" ] || { echo "replacement-sync: $MANIFEST not found" >&2; exit 2; }

KEY=""; ACTION=check; AGAINST=""; ALL=0; LIST=0
while [ $# -gt 0 ]; do
	case "$1" in
		--accept) ACTION=accept ;;
		--record) ACTION=record ;;
		--all) ALL=1 ;;
		--list) LIST=1 ;;
		--against) shift; AGAINST="${1:-}"; [ -n "$AGAINST" ] || { echo "replacement-sync: --against needs a ref" >&2; exit 2; } ;;
		-h|--help) sed -n '2,23p' "$0"; exit 0 ;;
		-*) echo "replacement-sync: unknown option $1" >&2; exit 2 ;;
		*) [ -z "$KEY" ] || { echo "replacement-sync: only one original at a time (or --all)" >&2; exit 2; }; KEY="$1" ;;
	esac
	shift
done

if [ "$LIST" = 1 ]; then
	jq -r '.entries[] | select(.original != null or .pattern != null) | (.original // .pattern)' "$MANIFEST"
	exit 0
fi
if [ "$ALL" = 0 ] && [ -z "$KEY" ]; then sed -n '2,23p' "$0" >&2; exit 2; fi
[ "$ALL" = 0 ] || [ "$ACTION" = check ] || { echo "replacement-sync: --all is a dry run only" >&2; exit 2; }
if [ -n "$AGAINST" ] && [ "$ACTION" != check ]; then
	echo "replacement-sync: --against is for dry runs; --accept and --record read the working tree" >&2; exit 2
fi

normalize() { perl -0777 -pe 's/\A\xEF\xBB\xBF//; s/\r\n/\n/g'; }
sha_of() { shasum -a 256 | cut -d' ' -f1; }

# File contents: working tree, or <ref>:<path> with --against.
file_text() { if [ -n "$AGAINST" ]; then git show "$AGAINST:$1" 2>/dev/null; else cat "$ROOT/$1" 2>/dev/null; fi; }
file_hash() { file_text "$1" | normalize | sha_of; }

is_glob() { case "$1" in *\**|*\?*) return 0 ;; *) return 1 ;; esac; }

# Files matched by a glob (** crosses folders, * and ? stay inside one), one repo-relative path per line, sorted, obj/ and bin/ ignored.
glob_files() {
	local pattern="$1" prefix regex
	prefix=$(printf '%s' "${pattern%%[*?]*}"); prefix="${prefix%/*}"
	regex=$(printf '%s' "$pattern" | perl -pe 's/([.+^$(){}|\[\]\\])/\\$1/g; s/\*\*/\x01/g; s/\*/[^\/]*/g; s/\?/[^\/]/g; s/\x01/.*/g; $_="^$_\$"')
	{
		if [ -n "$AGAINST" ]; then git ls-tree -r --name-only "$AGAINST" -- "$prefix" 2>/dev/null
		else find "$ROOT/$prefix" -type f 2>/dev/null | sed "s|^$ROOT/||"; fi
	} | grep -E "$regex" | grep -Ev '(^|/)(obj|bin)/' | LC_ALL=C sort
}

entry_hash() { # $1 = original or pattern
	if is_glob "$1"; then
		local f lines=""
		while IFS= read -r f; do
			[ -n "$f" ] || continue
			lines="${lines:+$lines
}$f $(file_hash "$f")"
		done < <(glob_files "$1")
		printf '%s' "$lines" | sha_of
	else
		file_hash "$1"
	fi
}

# A ref whose tree equals the working tree for <path>, to record as the new base (upstream/master if it matches, else HEAD).
choose_base() {
	local path="$1" prefix ref
	prefix="$path"; is_glob "$path" && { prefix=$(printf '%s' "${path%%[*?]*}"); prefix="${prefix%/*}"; }
	for ref in upstream/master HEAD; do
		git rev-parse --verify -q "$ref^{commit}" >/dev/null || continue
		if git diff --quiet "$ref" -- "$prefix" 2>/dev/null; then git rev-parse "$ref"; return 0; fi
	done
	return 1
}

record() { # $1 key, $2 base, $3 hash
	local tmp; tmp=$(mktemp)
	jq --arg k "$1" --arg b "$2" --arg h "$3" \
		'(.entries[] | select((.original // .pattern) == $k)) |= (.base = $b | .sha256 = $h)' "$MANIFEST" > "$tmp" \
		&& mv "$tmp" "$MANIFEST" || { rm -f "$tmp"; echo "replacement-sync: could not update $MANIFEST" >&2; return 2; }
}

sync_one() { # $1 key; returns 0 unchanged/applied, 1 action needed, 2 error
	local key="$1" entry kind base want got replacement short
	entry=$(jq -c --arg k "$key" '[.entries[] | select((.original // .pattern) == $k)] | .[0] // empty' "$MANIFEST")
	[ -n "$entry" ] || { echo "replacement-sync: $key is not in $(basename "$MANIFEST") (run with --list)" >&2; return 2; }
	kind=$(jq -r '.kind' <<<"$entry"); base=$(jq -r '.base // empty' <<<"$entry"); want=$(jq -r '.sha256 // empty' <<<"$entry")
	replacement=$(jq -r '.replacement // empty' <<<"$entry")
	[ -n "$want" ] || { echo "replacement-sync: $key has no recorded hash" >&2; return 2; }
	got=$(entry_hash "$key")
	short="${base:0:7}"

	if [ "$ACTION" = record ]; then
		local newbase; newbase=$(choose_base "$key") || { echo "replacement-sync: no commit matches the working tree for $key; commit the merge first (or run git fetch upstream)" >&2; return 2; }
		record "$key" "$newbase" "$got" || return 2
		echo "$key: recorded hash ${got:0:12} at base ${newbase:0:7}"
		return 0
	fi

	if [ "$got" = "$want" ]; then
		echo "$key: unchanged since base $short ($kind)"
		return 0
	fi

	echo "$key: CHANGED since base $short ($kind); recorded ${want:0:12}, now ${got:0:12}"
	if [ "$kind" = patched-copy ] && ! is_glob "$key" && [ -n "$replacement" ] && [ -z "$AGAINST" ]; then
		local dir; dir="$OUT"; mkdir -p "$dir"
		local b="$dir/base.txt" t="$dir/theirs.txt" o="$dir/ours.txt" m="$dir/$(basename "$key").merged"
		git show "$base:$key" 2>/dev/null | normalize > "$b" || true
		[ -s "$b" ] || { echo "replacement-sync: the base commit $short does not contain $key" >&2; return 2; }
		normalize < "$ROOT/$key" > "$t"
		normalize < "$ROOT/$replacement" > "$o"
		git merge-file -p -L "replacement" -L "base" -L "original" "$o" "$b" "$t" > "$m"
		local rc=$?
		echo "--- upstream change to the original:"
		git diff --no-index --stat -- "$b" "$t" | sed 's/^/    /'
		if [ "$rc" -ge 128 ] || [ "$rc" -lt 0 ]; then echo "replacement-sync: git merge-file failed" >&2; return 2; fi
		if [ "$rc" -gt 0 ]; then
			echo "RESULT: $rc conflict(s). Merged text with markers: ${m#$ROOT/}"
			echo "NEXT: port the change into $replacement by hand, then: mac/tools/replacement-sync.sh $key --record"
			return 1
		fi
		if [ "$ACTION" = accept ]; then
			if grep -q $'\r' "$ROOT/$replacement"; then perl -pe 's/\n/\r\n/' "$m" > "$ROOT/$replacement"; else cp "$m" "$ROOT/$replacement"; fi
			local newbase; newbase=$(choose_base "$key") || { echo "replacement-sync: replacement written, but no commit matches the working tree for $key; commit the merge, then run --record" >&2; return 1; }
			record "$key" "$newbase" "$got" || return 2
			echo "RESULT: merged cleanly into $replacement; recorded hash ${got:0:12} at base ${newbase:0:7}"
			echo "NEXT: build and run mac/tools/verify.sh, then commit"
			return 0
		fi
		echo "RESULT: merges cleanly (merged text: ${m#$ROOT/})"
		echo "NEXT: mac/tools/replacement-sync.sh $key --accept"
		return 1
	fi

	if [ "$ACTION" = accept ]; then
		local newbase; newbase=$(choose_base "$key") || { echo "replacement-sync: no commit matches the working tree for $key; commit the merge first (or run git fetch upstream)" >&2; return 2; }
		record "$key" "$newbase" "$got" || return 2
		echo "RESULT: recorded hash ${got:0:12} at base ${newbase:0:7} (the port by hand is assumed to be done)"
		return 0
	fi

	local diffbase="$base"
	git cat-file -e "$diffbase^{commit}" 2>/dev/null || { echo "replacement-sync: the base commit $short is not in this clone" >&2; return 2; }
	echo "--- git diff $short ${AGAINST:-<working tree>} -- $key:"
	local prefix="$key"; is_glob "$key" && { prefix=$(printf '%s' "${key%%[*?]*}"); prefix="${prefix%/*}"; }
	if is_glob "$key"; then
		if [ -n "$AGAINST" ]; then git diff --name-status "$diffbase" "$AGAINST" -- "$prefix" | sed 's/^/    /'
		else git diff --name-status "$diffbase" -- "$prefix" | sed 's/^/    /'; fi
	else
		if [ -n "$AGAINST" ]; then git diff "$diffbase" "$AGAINST" -- "$key"; else git diff "$diffbase" -- "$key"; fi
	fi
	echo "RESULT: port the change by hand into ${replacement:-the Sources.props exclusion (check whether the new code still needs it)}"
	echo "NEXT: afterwards mac/tools/replacement-sync.sh $key --accept"
	return 1
}

if [ "$ALL" = 1 ]; then
	rc=0; changed=0; total=0
	while IFS= read -r k; do
		total=$((total + 1))
		out=$(sync_one "$k" 2>&1); r=$?
		first=$(printf '%s\n' "$out" | head -1)
		echo "$first"
		if [ "$r" -ne 0 ]; then changed=$((changed + 1)); [ "$r" -ge 2 ] && rc=2 || { [ "$rc" -eq 2 ] || rc=1; }; fi
	done < <(jq -r '.entries[] | select(.sha256 != null) | (.original // .pattern)' "$MANIFEST")
	echo "replacement-sync: $changed of $total entries need action"
	exit "$rc"
fi

sync_one "$KEY"
exit $?
