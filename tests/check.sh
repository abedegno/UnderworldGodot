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

# Gameplay scenarios in headless Godot 4.3 (tests/scenarios.sh). scenarios.sh decides whether
# there is a Godot, so the two scripts cannot disagree: a GODOT that is set but cannot run fails
# here too, and only no Godot at all (or CHECK_SKIP_SCENARIOS=1) skips. Without game data the
# scenarios themselves skip.
if [ "${CHECK_SKIP_SCENARIOS:-0}" = 1 ]; then
  echo "skip  scenarios  (CHECK_SKIP_SCENARIOS)"
else
  why=$(tests/scenarios.sh --find-godot 2>&1); rc=$?
  case $rc in
    0)
      # The build above is current, so scenarios.sh need not build again.
      out=$(SCENARIO_NO_BUILD=1 tests/scenarios.sh 2>&1); rc=$?
      summary=$(echo "$out" | grep -E '^scenarios: (ok|FAIL)' | tail -1)
      counts=$(echo "$summary" | sed -nE 's/^scenarios: (ok|FAIL) \((.*)\)$/\2/p')
      if [ $rc -eq 0 ] && [ -n "$counts" ]; then echo "ok    scenarios  $counts"
      else
        status=1; echo "FAIL  scenarios  ${counts:-${summary#scenarios: }}"
        problems=$(echo "$out" | grep -E '^  problem: ' | sed 's/^  problem: //')
        if [ -n "$problems" ]; then echo "$problems" | head -20 | sed 's/^/      /'
        else echo "$out" | tail -10 | sed 's/^/      /'; fi
      fi ;;
    3) echo "skip  scenarios  (no Godot 4.3)" ;;
    *) status=1; echo "FAIL  scenarios  $why" ;;
  esac
fi
exit $status
