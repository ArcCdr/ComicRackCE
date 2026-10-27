#!/usr/bin/env bash
# P4.17: write the plugin gap report (mac/reports/plugin-gap-bedetheque.md) for the two acceptance plugins.
#
#   mac/scripting/plugin-gap.sh [--check]
#
# Reads the packages in mac/fixtures/plugins (fetch them with mac/scripting/fetch-corpus.sh --only <name>), installs each into a temporary
# data folder through the plugin install path, loads its scripts in the plugin host, scans it (nothing else of it runs) and classifies everything it reaches
# against the facades, the Core and the runtime. Planning data: mac/scripting/gap-rules.txt; hand-written sections: mac/scripting/gap-notes/<package>.md.
# Writes mac/reports/plugin-gap-bedetheque.md (committed) and mac/artifacts/plugin-gap/gap.json (git ignored, every row).
# --check writes to mac/artifacts/plugin-gap instead and exits 1 when a row that needs work names no task.
# Exit 0 ok, 1 a row has no task, 2 a package is missing.
set -eu
cd "$(cd "$(dirname "$0")/../.." && pwd)"

packages=(
  mac/fixtures/plugins/bedetheque-scraper-2-v6.08/Bedetheque.Scraper.2_v6.08.crplugin
  mac/fixtures/plugins/library-organizer-2.1.13/Library.Organizer.2.1.13.crplugin
)
for p in "${packages[@]}"; do
  if [ ! -f "$p" ]; then
    echo "missing $p: run mac/scripting/fetch-corpus.sh --only $(basename "$(dirname "$p")")" >&2
    exit 2
  fi
done

out=mac/reports/plugin-gap-bedetheque.md
if [ "${1:-}" = "--check" ]; then
  out=mac/artifacts/plugin-gap/gap.md
fi
mkdir -p mac/artifacts/plugin-gap
dotnet build mac/ComicRack.Cli --nologo -v q -clp:ErrorsOnly > /dev/null
dotnet run --no-build --project mac/ComicRack.Cli -- plugin-gap "${packages[@]}" \
  --rules mac/scripting/gap-rules.txt --notes-dir mac/scripting/gap-notes \
  --markdown "$out" --json mac/artifacts/plugin-gap/gap.json --require-tasks
echo "report: $out"
