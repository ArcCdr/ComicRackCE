#!/usr/bin/env bash
# P4.6b: fetch the public plugin corpus that `crcli script-scan` measures (mac/scripting/corpus.md lists every source).
#
# Every plugin goes into its own folder below the corpus root (default mac/fixtures/plugins, git ignored), pinned to a commit
# or a release asset with a SHA-256. Nothing that is downloaded is ever run, built, imported or unpacked here: git is used
# only to fetch one commit (hooks off, no submodules, no LFS), release files are stored as they are. The folders stay
# untrusted; the only consumer is the scanner, which reads them without running anything.
#
#   mac/scripting/fetch-corpus.sh [--root DIR] [--only NAME] [--list]
#
# Environment: COMICRACK_CORPUS_DIR (same as --root). Exit 0 ok, 1 a fetch or a check failed, 2 bad arguments, 3 disk or budget STOP.
set -u

HERE="$(cd "$(dirname "$0")" && pwd)"
REPO="$(cd "$HERE/../.." && pwd)"
ROOT="${COMICRACK_CORPUS_DIR:-$REPO/mac/fixtures/plugins}"
ONLY=""
LIST=0
BUDGET_KB=$((1500 * 1024))   # mac/fixtures budget of the plan (1.5 GB)

while [ $# -gt 0 ]; do
  case "$1" in
    --root) [ $# -ge 2 ] || { echo "--root needs a folder" >&2; exit 2; }; ROOT="$2"; shift 2 ;;
    --only) [ $# -ge 2 ] || { echo "--only needs a name" >&2; exit 2; }; ONLY="$2"; shift 2 ;;
    --list) LIST=1; shift ;;
    -h|--help) sed -n '2,12p' "$0"; exit 0 ;;
    *) echo "unknown argument: $1" >&2; exit 2 ;;
  esac
done

# name | kind | source | pin | licence
#   kind git:     source = repository URL, pin = full commit hash
#   kind release: source = file URL,       pin = SHA-256 of the file
ENTRIES='
bedetheque-scraper-2-v6.08|release|https://github.com/maforget/Bedetheque-Scrapper-2/releases/download/v6.08/Bedetheque.Scraper.2_v6.08.crplugin|666016de2439ba179c27fb267b64d9896faa3bae71200f9b372969991954e5c5|none stated
data-manager-v2.07.06.830|release|https://github.com/maforget/CRDataManager/releases/download/2.07.06.830/Data.Manager_v2.07.06.830.crplugin|d520fe4d9a3e6a5ef47a94b738068e9ef55c4dbbf508c61decd0ec55bf49f1bd|none stated
amazon-scrapper-v1.1|release|https://github.com/maforget/ComicRack_AmazonScrapper/releases/download/v1.1/AmazonScrapper.crplugin|48c3dc3c9143f5cc0b144390628230f1ab7905f87111f72cee4debbe853d4659|GPL-2.0
find-image-resolution-v1.0|release|https://github.com/maforget/ComicRack_FindImageResolution/releases/download/v1.0/FindImageResolutionNET.crplugin|d47837afabbbe05b4098ea2a9f3a3abec9df8d2a608c9d6dea1781d3ff71e8e3|GPL-2.0
library-organizer-2.1.13|release|https://github.com/Stonepaw/comicrack-library-organizer/releases/download/2.1.13/Library.Organizer.2.1.13.crplugin|9ef9617d349fc212396409d84a3c3b2c927105257bb18f5fc63cabd284312320|none stated
from-ducks-3.0.1|release|https://github.com/ducksmanager/ComicRack_FromDucks/releases/download/v3.0.1/FromDucks-3.0.1.crplugin|3249f424c5ac726611ca13f91a9ed656f683739d4848ea19596c03f7b87f1b3e|GPL-3.0
grand-comics-db-scraper-v1.0.0|release|https://github.com/strumples/ComicRack-Grand-Comics-Scraper/releases/download/v1.0.0/Local.Grand.Comics.DB.Scraper.crplugin|3a791d3648924b8e109ce5ef4813c4d6291e5e15fc485aee66a5f3ddeae5aac2|MIT
cvinfowrite-v1.4|release|https://github.com/killo3967/CVInfoWrite/releases/download/v1.4/CVInfoWrite.crplugin|06ff040247dff74e9546f758be137393d018033df1235918ba478d9da2d1421e|none stated
mifs-v1.0.0|release|https://github.com/faqy003/ComicRack-Plugin-MIFS/releases/download/v1.0.0/ComicRack_Plugin_MIFS.crplugin|d341a94550248170e7efe0b4a8fa361a8fba626e24c278f555c9d79867212c38|Unlicense
comic-vine-scraper|git|https://github.com/cbanack/comic-vine-scraper.git|a4c100bcaa031881f009a28acd398b6a19d35c3d|none stated
manga-update-scraper|git|https://github.com/maforget/ComicRack_MangaUpdateScraper.git|14600310f52939af8ef2b36323c769f714a339fc|none stated
pescuma-plugins|git|https://github.com/pescuma/comicrack-plugins.git|4d62d7263b3219dde75b5d5c4eb1a2924b5a1f48|none stated
duplicates-manager|git|https://github.com/pescuma/comicrack-duplicates-manager.git|162d6e10225b9e9eb3b4e618c3343eec08c92887|none stated
copy-move-field|git|https://github.com/Stonepaw/comicrack-copy-move-field.git|ce576868cefa4612c76e25c4e21e007d143a3c4b|MIT
scan-information-from-filename|git|https://github.com/Stonepaw/comicrack-scan-information-from-filename.git|e40a507fb8c7d0fa9e8610a43f8e9cae1759e881|MIT
weekly-comic-releases|git|https://github.com/Stonepaw/comicrack-weekly-comic-releases.git|ef968cea2d85152b168c3eef91b97a2bb698dc45|other
webcomic-helper|git|https://github.com/Stonepaw/comicrack-webcomic-helper.git|c3788c98a1c31aad23a32d1c7b494112b0ccc665|GPL-3.0
new-entry-via-barcode|git|https://github.com/Stonepaw/comicrack-new-entry-via-barcode.git|73c285bc568d25fd46ec4d636baed3bd1075a449|other
story-arc-from-titles|git|https://github.com/SenorSmartyPants/ComicRack-StoryArcFromTitles.git|cd00338ffa0a12cabcfa4e41e34788b55e4b79fe|GPL-3.0
mcl-updater-py3|git|https://github.com/azuravian/ComicRack_mcl_Updater_Py3.git|7f4e47398e557a3adf1e9371e926fb822f42ee8e|CC0-1.0
auto-folders|git|https://github.com/scinos/ComicRackAutoFolders.git|d15dc04325be8c5460f776f8c67dbca16e64c46d|none stated
guided-rename-mod|git|https://github.com/Northrup69/GuidedRename-Mod.git|8da26db6b85b26e18a994976b7bd29e965949763|none stated
get-comicrack-info|git|https://github.com/killo3967/get_comicrack_info.git|8d219f6fdd9a0bca4dbd41f2ddd3f9cec0a4f682|GPL-3.0
'

if [ "$LIST" = 1 ]; then
  printf '%s\n' "$ENTRIES" | grep -v '^$' | awk -F'|' '{ printf "%-34s %-8s %s\n", $1, $2, $3 }'
  exit 0
fi

case "$ROOT" in /*) ;; *) ROOT="$PWD/$ROOT" ;; esac
case "$ROOT" in
  *"/../"*|*"/..") echo "refusing a folder with '..': $ROOT" >&2; exit 2 ;;
esac
case "$ROOT" in
  "$REPO"/mac/fixtures/*|"$REPO"/mac/artifacts/*|/tmp/*|/private/tmp/*|/var/folders/*|/private/var/folders/*) ;;
  *) echo "refusing to fetch outside mac/fixtures, mac/artifacts or a temp folder: $ROOT" >&2; exit 2 ;;
esac
mkdir -p "$ROOT" || exit 1
ROOT="$(cd "$ROOT" && pwd)"

free_kb=$(df -k "$HOME" | awk 'NR==2 { print $4 }')
if [ "${free_kb:-0}" -lt $((3 * 1024 * 1024)) ]; then
  echo "STOP: less than 3 GB free" >&2
  exit 3
fi

fail=0
fetched=0
kept=0
while IFS='|' read -r name kind source pin licence; do
  [ -n "$name" ] || continue
  if [ -n "$ONLY" ] && [ "$ONLY" != "$name" ]; then continue; fi
  dir="$ROOT/$name"
  if [ -f "$dir/SOURCE.txt" ]; then
    kept=$((kept + 1))
    continue
  fi
  if [ -e "$dir" ]; then
    echo "SKIP $name: $dir exists without SOURCE.txt (not touched)" >&2
    fail=1
    continue
  fi
  if [ -d "$REPO/mac/fixtures" ] && [ "$(du -sk "$REPO/mac/fixtures" | awk '{ print $1 }')" -gt "$BUDGET_KB" ]; then
    echo "STOP: mac/fixtures is over the 1.5 GB budget" >&2
    exit 3
  fi
  case "$pin" in @*) echo "SKIP $name: the pin is not recorded yet" >&2; fail=1; continue ;; esac
  echo "fetch $name ($kind)"
  tmp="$ROOT/.fetch-$name.$$"
  rm -rf "$tmp"
  mkdir -p "$tmp"
  ok=0
  if [ "$kind" = git ]; then
    (
      cd "$tmp" &&
      export GIT_TERMINAL_PROMPT=0 GIT_LFS_SKIP_SMUDGE=1 &&
      git init -q &&
      git -c core.hooksPath=/dev/null remote add origin "$source" &&
      git -c core.hooksPath=/dev/null -c protocol.version=2 fetch -q --depth 1 --no-recurse-submodules origin "$pin" &&
      git -c core.hooksPath=/dev/null -c advice.detachedHead=false checkout -q --detach FETCH_HEAD &&
      [ "$(git rev-parse HEAD)" = "$pin" ]
    ) && ok=1
  else
    file="${source##*/}"
    if curl -fsSL --proto '=https' --max-time 300 -o "$tmp/$file" "$source"; then
      sum="$(shasum -a 256 "$tmp/$file" | awk '{ print $1 }')"
      if [ "$sum" = "$pin" ]; then ok=1; else echo "  SHA-256 mismatch: $sum" >&2; fi
    fi
  fi
  if [ "$ok" = 1 ]; then
    {
      echo "name: $name"
      echo "url: $source"
      echo "pin: $pin"
      echo "licence: $licence"
      echo "note: untrusted download; scanned only, never run (mac/scripting/corpus.md)"
    } > "$tmp/SOURCE.txt"
    mv "$tmp" "$dir"
    fetched=$((fetched + 1))
  else
    echo "  FAILED: $name" >&2
    rm -rf "$tmp"
    fail=1
  fi
done <<< "$ENTRIES"

echo "corpus: $fetched fetched, $kept already there, root $ROOT"
du -sh "$ROOT" | awk '{ print "size: " $1 }'
exit "$fail"
