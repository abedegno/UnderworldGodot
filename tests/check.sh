#!/bin/bash
# Build the game and run every test project under tests/, one summary line each.
# Exit 0 only when every project builds and no test fails (skips are fine and are counted).
# For agents: run after every edit. Uses dotnet on PATH (or $DOTNET).
set -uo pipefail
cd "$(dirname "$0")/.."
dotnet=${DOTNET:-dotnet}
status=0
if ! out=$("$dotnet" build Underworld.csproj -nologo -v q 2>&1); then
  echo "$out" | grep -E 'error' | head -20; echo "FAIL  build"; exit 1
fi
echo "ok    build"
for proj in tests/*/*.csproj; do
  name=$(basename "$proj" .csproj)
  out=$("$dotnet" test "$proj" -nologo -v q 2>&1); rc=$?
  line=$(echo "$out" | grep -E '(Passed|Failed)!' | tail -1 | sed -E 's/^.*(Passed|Failed)! *- *//')
  if [ $rc -eq 0 ]; then echo "ok    $name  ${line:-no summary}"
  else
    status=1; echo "FAIL  $name  ${line:-did not run}"
    echo "$out" | grep -E '^\s+Failed |Error Message|error' | head -20 | sed 's/^/      /'
  fi
done
exit $status
