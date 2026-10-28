#!/bin/bash
# Copy the rule vector tables from an Underworld Exhumed checkout and record which commit they
# came from. usage: sync-vectors.sh UNDERWORLD_EXHUMED_DIR
set -euo pipefail
src=${1:?usage: sync-vectors.sh UNDERWORLD_EXHUMED_DIR}
here=$(cd "$(dirname "$0")" && pwd)
for g in uw1 uw2; do
  mkdir -p "$here/vectors/$g"; rm -f "$here/vectors/$g"/*.csv
  cp "$src/$g/vectors/"*.csv "$here/vectors/$g/"
done
{ echo "Underworld Exhumed $(git -C "$src" rev-parse --short HEAD) (https://github.com/abedegno/underworld-exhumed, uw1/vectors and uw2/vectors)"
  echo "Outputs of UW.EXE's and UW2.EXE's own routines on synthetic inputs: numbers only, no game data. MIT License."
} > "$here/vectors/SOURCE"
echo "synced $(ls "$here"/vectors/uw1/*.csv "$here"/vectors/uw2/*.csv | wc -l | tr -d ' ') tables"
