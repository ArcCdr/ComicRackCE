#!/usr/bin/env bash
# P2.4a: fetches the official 7-Zip macOS console binary (7zz) into mac/artifacts/native/. Usage: mac/native/fetch-7zz.sh [--force]
#
# Provenance: https://www.7-zip.org/download.html (section "Download 7-Zip 26.05 (2026-10-21)", row "macOS (arm64 / x86-64)", "7-Zip for
# MacOS: console version") links to the release of the official project repository https://github.com/ip7z/7zip (Igor Pavlov), asset
# 7z2605-mac.tar.xz. The SHA-256 below was computed on the downloaded file and equals the digest that the release page reports for
# the asset. 7-zip.org itself serves only older macOS builds under /a/. Homebrew and other repackagers are never used.
# Licence: GNU LGPL (main), GNU LGPL with the unRAR licence restriction (some code, so 7zz must never be used to create RAR archives), BSD 3-clause
# and BSD 2-clause (some code); the full text is License.txt of the archive, copied next to the binary as 7zz-License.txt. A binary
# distribution must reproduce it (the bundle step copies it; P6.5 lists it in the third party notices).
# To move to a newer version: change VERSION, ARCHIVE_SHA256, BINARY_SHA256 together after reading the release notes and checking the digest.
set -euo pipefail

VERSION="26.05"
ARCHIVE="7z2605-mac.tar.xz"
URL="https://github.com/ip7z/7zip/releases/download/$VERSION/$ARCHIVE"
ARCHIVE_SHA256="12ef6830317743cfd3f0ab1724bde43fcc270d8b6d8564f8c12c2a2b7f993570"
BINARY_SHA256="8e495531e1b2b8cb169ca583493922d4ab386007756418c15b95f07f642c35fb"
LICENSE_SHA256="1790374e5352329cedb46ee3808930a88e9ca2f08b82b10fcf5cf605d2c301b1"

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
OUT="$ROOT/mac/artifacts/native"
TARGET="$OUT/7zz"

sha256() { shasum -a 256 "$1" | awk '{print $1}'; }

FORCE=0
[ "${1:-}" = "--force" ] && FORCE=1

if [ "$FORCE" = 0 ] && [ -x "$TARGET" ] && [ "$(sha256 "$TARGET")" = "$BINARY_SHA256" ] && [ -f "$OUT/7zz-License.txt" ]; then
	echo "7zz $VERSION is already in $OUT (checksum verified)."
	exit 0
fi

FREE_KB=$(df -k "$ROOT" | awk 'NR==2 {print $4}')
if [ "$FREE_KB" -lt 3145728 ]; then
	echo "STOP: less than 3 GB free disk." >&2
	exit 1
fi

mkdir -p "$OUT"
WORK="$(mktemp -d "$OUT/.fetch.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

echo "Downloading $URL"
curl --fail --silent --show-error --location --proto '=https' --max-time 300 -o "$WORK/$ARCHIVE" "$URL"
GOT="$(sha256 "$WORK/$ARCHIVE")"
if [ "$GOT" != "$ARCHIVE_SHA256" ]; then
	echo "FAIL: SHA-256 of $ARCHIVE is $GOT, expected $ARCHIVE_SHA256. Nothing was installed." >&2
	exit 1
fi

mkdir "$WORK/x"
tar -xJf "$WORK/$ARCHIVE" -C "$WORK/x" 7zz License.txt
if [ "$(sha256 "$WORK/x/7zz")" != "$BINARY_SHA256" ] || [ "$(sha256 "$WORK/x/License.txt")" != "$LICENSE_SHA256" ]; then
	echo "FAIL: the extracted files do not match the pinned checksums. Nothing was installed." >&2
	exit 1
fi
if ! lipo -archs "$WORK/x/7zz" | grep -qw arm64; then
	echo "FAIL: 7zz has no arm64 slice." >&2
	exit 1
fi

chmod 755 "$WORK/x/7zz"
xattr -d com.apple.quarantine "$WORK/x/7zz" 2>/dev/null || true
mv -f "$WORK/x/7zz" "$TARGET"
mv -f "$WORK/x/License.txt" "$OUT/7zz-License.txt"
printf '%s\n' "7-Zip $VERSION ($ARCHIVE) from $URL" "sha256 $ARCHIVE_SHA256" > "$OUT/7zz-source.txt"
echo "Installed $TARGET ($(du -h "$TARGET" | cut -f1))"
