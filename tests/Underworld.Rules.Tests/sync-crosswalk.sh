#!/bin/bash
# Copy Underworld Exhumed's crosswalks (each routine's listing name and address in hankmorgan's
# disassembly, its original name and the file:line where the matched source defines it) for the
# rule index, and record which commit they came from. usage: sync-crosswalk.sh UNDERWORLD_EXHUMED_DIR
set -euo pipefail
src=${1:?usage: sync-crosswalk.sh UNDERWORLD_EXHUMED_DIR}
here=$(cd "$(dirname "$0")" && pwd)
mkdir -p "$here/ruleindex"
for g in uw1 uw2; do
  { echo "# listing name	listing address	original name	defined at (file:line)"
    grep -v '^#' "$src/$g/map/crosswalk.tsv" | cut -f1,2,4,5
  } > "$here/ruleindex/crosswalk-$g.tsv"
done
{ echo "Underworld Exhumed $(git -C "$src" rev-parse --short HEAD) (https://github.com/abedegno/underworld-exhumed, uw1/map/crosswalk.tsv and uw2/map/crosswalk.tsv)"
  echo "Routine names, disassembly addresses and source line numbers only: no code, no game data. MIT License."
} > "$here/ruleindex/SOURCE"
echo "synced $(cat "$here"/ruleindex/crosswalk-uw?.tsv | grep -vc '^#' | tr -d ' ') routines"
