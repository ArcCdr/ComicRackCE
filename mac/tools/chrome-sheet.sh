#!/usr/bin/env bash
# Contact sheet of views for the visual parity check (mac/plans/chrome-guidelines.md section 9, task P5.T3).
#
#   mac/tools/chrome-sheet.sh <label> --all            every view of the app (the names `ComicRack.Mac --list-views` prints)
#   mac/tools/chrome-sheet.sh <label> <View>...        the named views (MainWindow, ChromeGallery, later every registered view)
#
# For each view one row of three cells: the reference, the Mac window in the light theme, the Mac window in the dark theme.
#   reference = mac/fixtures/owner/reference/<View>.png (owner screenshot), else mac/fixtures/owner/reference/ci/<View>.png (Windows CI,
#               P5.T5), else a reference card drawn from mac/specs/views/<View>.md (layout outline, token swatches, icons; without a spec
#               from the original Designer file). The README banner, downloaded once to mac/artifacts/chrome/reference/readme-hero.png
#               (https://github.com/maforget/ComicRackCE/assets/11904426/4748925c-662f-4ccd-bfb7-62ec46ae881e), is only shown at the top of
#               sheet.html: it is a brand banner, not a screenshot.
#   CHROME_SHEET_ARGS adds app options to every launch (RV5: CHROME_SHEET_ARGS='--chrome StatusBar=Native' shoots a Native variant).
#   Mac shots = the real window through mac/tools/ui-drive: `launch --dev --data-dir <temp> -- --theme light|dark --show <View>
#               --fixture-library <folder>` (the folder is mac/fixtures/small, or CHROME_SHEET_FIXTURES, or 24 generated books).
# Output (git-ignored): mac/artifacts/chrome/<label>/sheet.html and sheet.png, plus the single shots and cards.
#
# Exit codes: 0 both files written with the Mac shots; 1 a step failed; 2 bad arguments; 4 the screen is locked or there is no GUI
# access: both files are still written, with reference cards only and a note in place of the Mac shots (record the check as SKIPPED);
# 6 ui-drive refused to launch (an owner-run copy of the app is running), same output as for 4.
set -uo pipefail

ROOT=$(git -C "$(dirname "$0")" rev-parse --show-toplevel 2>/dev/null) || { echo "chrome-sheet: not inside the git repository" >&2; exit 2; }
UI="$ROOT/mac/tools/ui-drive/ui-drive.sh"
HELPER="$ROOT/mac/tools/chrome-sheet/chrome_sheet.py"
CRCLI="$ROOT/mac/ComicRack.Cli/bin/Debug/net10.0/osx-arm64/crcli"
APP_PROJECT="$ROOT/mac/ComicRack.Mac/ComicRack.Mac.csproj"
APP="$ROOT/mac/ComicRack.Mac/bin/Debug/net10.0/osx-arm64/ComicRack.Mac"
HERO_URL="https://github.com/maforget/ComicRackCE/assets/11904426/4748925c-662f-4ccd-bfb7-62ec46ae881e"
REFS="$ROOT/mac/artifacts/chrome/reference"
OWNER_REFS="$ROOT/mac/fixtures/owner/reference"

usage() { sed -n '2,22p' "$0" | sed 's/^# \{0,1\}//'; }

case "${1:-}" in -h|--help) usage; exit 0 ;; esac
[ $# -ge 2 ] || { usage >&2; exit 2; }
LABEL=$1; shift
case "$LABEL" in ''|*[!A-Za-z0-9._-]*) echo "chrome-sheet: the label may only hold letters, digits, dot, underscore and dash" >&2; exit 2 ;; esac

LOCKED=0; REFUSED=0; FAILED=0
CLEANUP_DATA=()
cleanup() {
	"$UI" kill >/dev/null 2>&1 || true
	local d; for d in ${CLEANUP_DATA[@]+"${CLEANUP_DATA[@]}"}; do rm -rf "$d"; done
}
trap cleanup EXIT

build_tools() {
	if [ ! -x "$CRCLI" ] || [ -n "$(find "$ROOT/mac/ComicRack.Cli" -name '*.cs' -newer "$CRCLI" -not -path '*/obj/*' -not -path '*/bin/*' 2>/dev/null | head -1)" ]; then
		echo "chrome-sheet: building crcli ..." >&2
		dotnet build "$ROOT/mac/ComicRack.Cli/ComicRack.Cli.csproj" -nologo -v q >/dev/null 2>&1 || { echo "chrome-sheet: crcli does not build" >&2; exit 1; }
	fi
	echo "chrome-sheet: building the dev app (incremental) ..." >&2
	dotnet build "$APP_PROJECT" -nologo -v q >/dev/null 2>&1 || { echo "chrome-sheet: the dev app does not build" >&2; exit 1; }
	[ -x "$APP" ] || { echo "chrome-sheet: no dev build at $APP" >&2; exit 1; }
}

# Views: --all asks the app, otherwise the names given (checked against the same list).
build_tools
KNOWN=$("$APP" --list-views 2>/dev/null) || { echo "chrome-sheet: the app did not list its views" >&2; exit 1; }
if [ "$1" = "--all" ]; then
	[ $# -eq 1 ] || { echo "chrome-sheet: --all takes no view names" >&2; exit 2; }
	VIEWS=(); while IFS= read -r v; do [ -n "$v" ] && VIEWS+=("$v"); done <<< "$KNOWN"
else
	VIEWS=()
	for v in "$@"; do
		printf '%s\n' "$KNOWN" | grep -qxF "$v" || { echo "chrome-sheet: unknown view $v. Known: $(printf '%s' "$KNOWN" | tr '\n' ' ')" >&2; exit 2; }
		VIEWS+=("$v")
	done
fi

OUT="$ROOT/mac/artifacts/chrome/$LABEL"
rm -rf "$OUT"; mkdir -p "$OUT" "$REFS"

# The README banner, once.
if [ ! -s "$REFS/readme-hero.png" ]; then
	if curl -fsSL --max-time 60 -o "$REFS/readme-hero.png.part" "$HERO_URL" 2>/dev/null && [ -s "$REFS/readme-hero.png.part" ]; then
		mv "$REFS/readme-hero.png.part" "$REFS/readme-hero.png"
	else
		rm -f "$REFS/readme-hero.png.part"; echo "chrome-sheet: note: the README banner could not be downloaded (offline?)" >&2
	fi
fi

# Fixture library for the Mac shots.
FIXTURES=${CHROME_SHEET_FIXTURES:-$ROOT/mac/fixtures/small}
if [ ! -d "$FIXTURES" ]; then
	FIXTURES=$(mktemp -d "${TMPDIR:-/tmp}/chrome-sheet-fixtures.XXXXXX"); CLEANUP_DATA+=("$FIXTURES")
	"$CRCLI" fixtures make "$FIXTURES" --books 24 --pages 3 --seed 5 >/dev/null || { echo "chrome-sheet: crcli could not make fixtures" >&2; exit 1; }
fi

# Is a real window possible at all?
"$UI" check >/dev/null 2>&1; rc=$?
if [ $rc -eq 4 ]; then LOCKED=1; echo "chrome-sheet: the screen is locked or there is no GUI access (ui-drive exit 4): no Mac shots will be taken" >&2; fi

# shoot <view> <theme> <png>: starts the app with the view in the theme, takes a screenshot of its window, quits. 0 ok, 1 failed, 4 locked, 6 refused.
shoot() {
	local view=$1 theme=$2 png=$3 data out rc title regex id
	data=$(mktemp -d "${TMPDIR:-/tmp}/chrome-sheet-data.XXXXXX"); CLEANUP_DATA+=("$data")
	out=$(UI_DRIVE_NO_BUILD=1 "$UI" launch --dev --data-dir "$data" -- --theme "$theme" --show "$view" --fixture-library "$FIXTURES" ${CHROME_SHEET_ARGS:-} 2>&1); rc=$?
	[ $rc -eq 0 ] || { echo "chrome-sheet: launch for $view ($theme) failed (exit $rc): $out" >&2; return $rc; }
	if [ "$view" = MainWindow ]; then
		regex='^ComicRack$'
		"$UI" expect-log 'Fixture library: .* scanned' 120 >/dev/null 2>&1 || echo "chrome-sheet: note: the fixture library did not finish scanning in time" >&2
		sleep 3 # covers decode after the scan
	else
		out=$("$UI" expect-log "Showing the view $view" 30 2>&1) || { echo "chrome-sheet: the view $view did not open: $out" >&2; "$UI" kill >/dev/null 2>&1; return 1; }
		title=$(printf '%s\n' "$out" | sed -n 's/.*Showing the view [A-Za-z0-9_]* ("\(.*\)")\..*/\1/p' | head -1)
		[ -n "$title" ] || { echo "chrome-sheet: could not read the window title of $view from the log: $out" >&2; "$UI" kill >/dev/null 2>&1; return 1; }
		# every character that is not a letter, digit or space matches any character (awk -v would process backslashes)
		regex="^$(printf '%s' "$title" | sed 's/[^A-Za-z0-9 ]/./g')\$"
		if [ "$view" = BookGrid ] || [ "$view" = Reader ]; then
			# a view of the library's books (the grid, the first book in a reader): wait for the fixture library and the covers as for the library window
			"$UI" expect-log 'Fixture library: .* scanned' 120 >/dev/null 2>&1 || echo "chrome-sheet: note: the fixture library did not finish scanning in time" >&2
			sleep 3
		fi
		sleep 1
	fi
	out=$("$UI" wait-window "$regex" 60 2>&1) || { rc=$?; echo "chrome-sheet: no window for $view: $out" >&2; "$UI" kill >/dev/null 2>&1; return $rc; }
	id=$(printf '%s\n' "$out" | awk '{print $1; exit}')
	"$UI" front >/dev/null 2>&1 || true
	out=$("$UI" shot "chrome-$LABEL-$view-$theme" --window "$id" 2>&1); rc=$?
	if [ $rc -ne 0 ] || [ ! -s "$out" ]; then
		echo "chrome-sheet: shot of $view ($theme) failed (exit $rc): $out" >&2; "$UI" kill >/dev/null 2>&1; return $((rc == 0 ? 1 : rc))
	fi
	cp "$out" "$png"
	"$UI" quit >/dev/null 2>&1 || "$UI" kill >/dev/null 2>&1
	if "$UI" stats "$png" 2>/dev/null | grep -q 'blank: yes'; then echo "chrome-sheet: the shot of $view ($theme) is blank" >&2; return 1; fi
	return 0
}

placeholder() { # <file> <title> <text>
	python3 - "$1" "$2" "$3" <<'PY'
import json, sys
json.dump({"title": sys.argv[2], "width": 560, "lines": [sys.argv[3]], "background": "#F5F5F5"}, open(sys.argv[1], "w"))
PY
}

ROWS_JSON="$OUT/rows.json"
CELLS=()
echo "[" >"$ROWS_JSON"
first=1
for v in "${VIEWS[@]}"; do
	echo "chrome-sheet: $v"
	card="$OUT/$v-card.json"
	python3 -I "$HELPER" card "$v" --out "$card" || { FAILED=1; continue; }
	decisions=$(python3 -I "$HELPER" decisions "$v")

	ref_file=""; ref_kind="reference card"; notes=()
	if [ -s "$OWNER_REFS/$v.png" ]; then
		cp "$OWNER_REFS/$v.png" "$OUT/$v-reference.png"; ref_file="$v-reference.png"; ref_kind="owner reference"
		[ -s "$OWNER_REFS/$v-dark.png" ] && { cp "$OWNER_REFS/$v-dark.png" "$OUT/$v-reference-dark.png"; notes+=("Dark owner reference: $v-reference-dark.png"); }
	elif [ -s "$OWNER_REFS/ci/$v.png" ]; then
		cp "$OWNER_REFS/ci/$v.png" "$OUT/$v-reference.png"; ref_file="$v-reference.png"; ref_kind="Windows CI reference"
	fi

	light=""; dark=""
	if [ $LOCKED -eq 0 ] && [ $REFUSED -eq 0 ]; then
		for theme in light dark; do
			png="$OUT/$v-$theme.png"
			shoot "$v" "$theme" "$png"; rc=$?
			case $rc in
				0) [ $theme = light ] && light="$v-light.png" || dark="$v-dark.png" ;;
				4) LOCKED=1; break ;;
				6) REFUSED=1; break ;;
				*) FAILED=1 ;;
			esac
		done
	fi
	if [ -n "$ref_file" ]; then
		cell_ref="$OUT/$ref_file"
	else
		cell_ref="$card"
		# the card as a picture for sheet.html
		"$CRCLI" image sheet --out "$OUT/$v-card.png" --cols 1 --cell-width 560 "$card::$v: reference card" >/dev/null && ref_file="$v-card.png" || FAILED=1
	fi
	why="screen locked"; [ $REFUSED -eq 1 ] && why="launch refused: an owner-run copy of the app is running"
	if [ -n "$light" ]; then cell_light="$OUT/$light"; else placeholder "$OUT/$v-light-missing.json" "$v (light)" "Mac shot not taken: $why."; cell_light="$OUT/$v-light-missing.json"; fi
	if [ -n "$dark" ]; then cell_dark="$OUT/$dark"; else placeholder "$OUT/$v-dark-missing.json" "$v (dark)" "Mac shot not taken: $why."; cell_dark="$OUT/$v-dark-missing.json"; fi
	CELLS+=("$cell_ref::$v: $ref_kind" "$cell_light::$v: Mac light" "$cell_dark::$v: Mac dark")

	[ $first -eq 1 ] || echo "," >>"$ROWS_JSON"; first=0
	python3 - "$v" "$ref_file" "$ref_kind" "$light" "$dark" "$decisions" "$(printf '%s\n' ${notes[@]+"${notes[@]}"})" >>"$ROWS_JSON" <<'PY'
import json, sys
v, ref, kind, light, dark, dec, notes = sys.argv[1:8]
print(json.dumps({"view": v, "reference": ref or None, "referenceKind": kind, "light": light or None, "dark": dark or None,
                  "decisions": dec.split(), "notes": [n for n in notes.split("\n") if n]}), end="")
PY
done
echo "]" >>"$ROWS_JSON"

# The card of a view that has no reference screenshot is not a file of the output folder yet: copy it next to the page.
python3 - "$ROWS_JSON" "$OUT" "$LOCKED" "$REFUSED" "$LABEL" <<'PY'
import json, os, sys
path, out, locked, refused, label = sys.argv[1:6]
rows = json.load(open(path))
warn = None
if locked == "1":
    warn = "The screen was locked or there was no GUI access: the Mac shots are missing. Run this again on an unlocked screen."
elif refused == "1":
    warn = "ui-drive refused to launch because an owner-run copy of the app is running: the Mac shots are missing."
if warn and rows:
    rows[0]["warning"] = warn
hero = os.path.join(os.path.dirname(out), "reference", "readme-hero.png")
if os.path.exists(hero) and rows:
    rows[0].setdefault("notes", []).append("README banner (brand reference, not a screenshot): ../reference/readme-hero.png")
json.dump(rows, open(path, "w"), indent=1)
PY
python3 -I "$HELPER" html --out "$OUT/sheet.html" --label "$LABEL" --data "$ROWS_JSON" || FAILED=1

"$CRCLI" image sheet --out "$OUT/sheet.png" --cols 3 --cell-width 520 "${CELLS[@]}" || FAILED=1
echo "chrome-sheet: $OUT/sheet.html"
echo "chrome-sheet: $OUT/sheet.png"
[ -s "$OUT/sheet.html" ] && [ -s "$OUT/sheet.png" ] || FAILED=1
if [ $FAILED -ne 0 ]; then echo "chrome-sheet: FAILED"; exit 1; fi
if [ $REFUSED -eq 1 ]; then echo "chrome-sheet: SKIPPED (launch refused)"; exit 6; fi
if [ $LOCKED -eq 1 ]; then echo "chrome-sheet: SKIPPED (screen locked): reference cards written, no Mac shots"; exit 4; fi
echo "chrome-sheet: done"
exit 0
