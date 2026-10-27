#!/usr/bin/env bash
# P4.6b: scan the plugin corpus and the bundled samples and write the usage report.
#
#   mac/scripting/scan-corpus.sh [--fetch]
#
# Reads mac/fixtures/plugins/<plugin>/ (one untrusted folder per plugin, see mac/scripting/corpus.md; --fetch runs
# fetch-corpus.sh first) and ComicRack/Output/Scripts, leaves out the paths of mac/scripting/corpus-exclude.txt, and writes
#   mac/scripting/compat-usage.md            the ranked report (committed)
#   mac/artifacts/script-corpus/scan.json    the full scan (git ignored; every member of every unit)
#   mac/artifacts/script-corpus/scan.txt     the text report (git ignored)
# The scanner parses and reads metadata only; nothing from the corpus is run, loaded or unpacked.
set -eu
cd "$(cd "$(dirname "$0")/../.." && pwd)"

if [ "${1:-}" = "--fetch" ]; then
  mac/scripting/fetch-corpus.sh
fi
if [ ! -d mac/fixtures/plugins ] || [ -z "$(ls -A mac/fixtures/plugins 2>/dev/null)" ]; then
  echo "no corpus in mac/fixtures/plugins: run mac/scripting/fetch-corpus.sh first" >&2
  exit 1
fi

mkdir -p mac/artifacts/script-corpus
dotnet build mac/ComicRack.Cli --nologo -v q -clp:ErrorsOnly > /dev/null
dotnet run --no-build --project mac/ComicRack.Cli -- script-scan mac/fixtures/plugins ComicRack/Output/Scripts \
  --exclude-file mac/scripting/corpus-exclude.txt \
  --json mac/artifacts/script-corpus/scan.json \
  --markdown mac/scripting/compat-usage.md \
  --top 200 > mac/artifacts/script-corpus/scan.txt
head -4 mac/artifacts/script-corpus/scan.txt
echo "report: mac/scripting/compat-usage.md"
