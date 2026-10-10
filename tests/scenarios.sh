#!/usr/bin/env bash
# Runs the gameplay scenarios (tests/godot/scenarios) in headless Godot 4.3, one process per game.
#
#   tests/scenarios.sh [--game uw1|uw2] [--uw1-dir DIR] [--uw2-dir DIR] [name...]
#
# No names runs every scenario (--all); names run just those, including ones --all leaves out.
# Both games by default. Game data defaults to ~/UWGOG/UW1 and ~/UWGOG/UW2; a game whose folder
# has no DATA is skipped.
#
# Exit 0 only when every game printed a verdict, no scenario failed that KNOWN_FAILURES does not
# list, and no listed scenario passed. Godot 4.3 mono's exit status is ignored: it can fault or
# hang on shutdown, so only the printed lines count, and no verdict within SCENARIO_TIMEOUT
# seconds (default 300) is a failure.
#
# Godot runs with a temporary HOME, so the player's own user://settings.json and saves are never
# read or written. The real home is passed to the runner only to find the game data.
#
# Environment: GODOT (the Godot 4.3 mono binary; set but not runnable is a failure, not a skip),
# SCENARIO_TIMEOUT (whole seconds per game), SCENARIO_IMPORT_TIMEOUT (whole seconds for the
# one-time import, default 180), DOTNET_ROOT, SCENARIO_NO_BUILD=1 to skip the C# build.
set -u

repo="$(cd "$(dirname "$0")/.." && pwd)"
known="$repo/tests/godot/scenarios/KNOWN_FAILURES"
timeout_s="${SCENARIO_TIMEOUT:-300}"
import_timeout_s="${SCENARIO_IMPORT_TIMEOUT:-180}"
for v in "SCENARIO_TIMEOUT=$timeout_s" "SCENARIO_IMPORT_TIMEOUT=$import_timeout_s"; do
    if ! printf '%s' "${v#*=}" | grep -Eq '^[1-9][0-9]*$'; then
        echo "scenarios: ${v%%=*} must be a whole number of seconds, not '${v#*=}'" >&2
        exit 2
    fi
done
home_real="$HOME"

games=(uw1 uw2)
names=()
dirs=()
while [ $# -gt 0 ]; do
    case "$1" in
        --game)
            case "${2:-}" in uw1|uw2) games=("$2") ;; *) echo "scenarios: --game takes uw1 or uw2" >&2; exit 2 ;; esac
            shift 2 ;;
        --uw1-dir|--uw2-dir)
            [ $# -ge 2 ] || { echo "scenarios: $1 needs a folder" >&2; exit 2; }
            dirs+=("$1" "$2"); shift 2 ;;
        -h|--help)
            sed -n '2,8p' "$0"; exit 0 ;;
        -*)
            echo "scenarios: unknown option $1" >&2; exit 2 ;;
        *)
            names+=("$1"); shift ;;
    esac
done

# Find Godot 4.3. One asked for by $GODOT that cannot run is a failure: skipping would hide a
# broken setup, in CI above all. Only a Godot nobody asked for and nobody has is a skip.
godot="${GODOT:-}"
if [ -n "$godot" ] && [ ! -x "$godot" ]; then
    echo "scenarios: FAIL, GODOT=$godot is not an executable file"
    exit 1
fi
if [ -z "$godot" ] && [ -x "$home_real/Applications/Godot43_mono.app/Contents/MacOS/Godot" ]; then
    godot="$home_real/Applications/Godot43_mono.app/Contents/MacOS/Godot"
fi
if [ -z "$godot" ]; then godot="$(command -v godot || true)"; fi
if [ -z "$godot" ] || [ ! -x "$godot" ]; then
    echo "scenarios: no Godot 4.3, skipped"
    exit 0
fi

# The mono build cannot find the runtime without DOTNET_ROOT, and then hangs.
if [ -z "${DOTNET_ROOT:-}" ] && [ -d "$home_real/.dotnet10" ]; then
    export DOTNET_ROOT="$home_real/.dotnet10"
fi
if [ -n "${DOTNET_ROOT:-}" ]; then export PATH="$DOTNET_ROOT:$PATH"; fi

tmp="$(mktemp -d "${TMPDIR:-/tmp}/uw-scenarios.XXXXXX")"
trap 'rm -rf "$tmp"' EXIT
mkdir -p "$tmp/home" "$tmp/home/.local/share" "$tmp/home/.config" "$tmp/home/.cache"

# Runs a command in the background with output to $1, killing it after $2 seconds. With a third
# argument, it also stops waiting a few seconds after that text appears in the output, because
# Godot 4.3 can hang on shutdown after it has said everything it is going to.
# Returns 0 if the process ended by itself, 1 if it was killed.
watch() {
    local out="$1" limit="$2" marker="$3"; shift 3
    # The XDG variables too: on Linux, Godot puts user data and editor settings under them
    # when they are set, whatever HOME says.
    HOME="$tmp/home" XDG_DATA_HOME="$tmp/home/.local/share" XDG_CONFIG_HOME="$tmp/home/.config" \
        XDG_CACHE_HOME="$tmp/home/.cache" "$@" >"$out" 2>&1 &
    local pid=$! waited=0 seen=-1
    while kill -0 "$pid" 2>/dev/null; do
        if [ "$waited" -ge $((limit * 2)) ]; then
            kill -9 "$pid" 2>/dev/null; wait "$pid" 2>/dev/null; return 1
        fi
        if [ -n "$marker" ] && [ "$seen" -lt 0 ] && grep -q "$marker" "$out" 2>/dev/null; then
            seen=$waited
        fi
        if [ "$seen" -ge 0 ] && [ $((waited - seen)) -ge 20 ]; then
            kill -9 "$pid" 2>/dev/null; wait "$pid" 2>/dev/null; return 1
        fi
        sleep 0.5
        waited=$((waited + 1))
    done
    wait "$pid" 2>/dev/null
    return 0
}

# Build the C# (the runner and scenarios compile into the game's Debug assembly) and import the
# project once, or Godot runs stale code or cannot load the scene at all.
if [ "${SCENARIO_NO_BUILD:-0}" != 1 ] && command -v dotnet >/dev/null 2>&1; then
    if ! (cd "$repo" && dotnet build Underworld.csproj -nologo -v q >"$tmp/build.log" 2>&1); then
        tail -20 "$tmp/build.log"
        echo "scenarios: FAIL, the C# build failed"
        exit 1
    fi
fi
# Imported means .godot/imported holds files: the folder alone can be left by an import that died.
imported() { [ -d "$repo/.godot/imported" ] && [ -n "$(ls -A "$repo/.godot/imported" 2>/dev/null)" ]; }
if ! imported; then
    echo "scenarios: importing the project (once; up to ${import_timeout_s}s)"
    if ! watch "$tmp/import.log" "$import_timeout_s" "" "$godot" --headless --path "$repo" --import; then
        tail -15 "$tmp/import.log" | sed 's/^/    /'
        echo "scenarios: FAIL, the import did not finish within ${import_timeout_s}s (SCENARIO_IMPORT_TIMEOUT)"
        exit 1
    fi
    if ! imported; then
        tail -15 "$tmp/import.log" | sed 's/^/    /'
        echo "scenarios: FAIL, the import ended but .godot/imported is empty"
        exit 1
    fi
fi

# KNOWN_FAILURES: "<game>/<name>  # why". A malformed line fails the run rather than being ignored.
listed=" "
bad=0
if [ -f "$known" ]; then
    while IFS= read -r line || [ -n "$line" ]; do
        entry="$(printf '%s' "${line%%#*}" | tr -d '[:space:]')"
        [ -z "$entry" ] && continue
        if ! printf '%s' "$entry" | grep -Eq '^uw[12]/[a-z0-9-]+$'; then
            echo "KNOWN_FAILURES: not <game>/<name>: $line"
            bad=1
            continue
        fi
        listed="$listed$entry "
    done <"$known"
fi

if [ ${#names[@]} -eq 0 ]; then
    sel=(--all)
else
    sel=(--scenarios "$(IFS=,; echo "${names[*]}")")
fi

for g in "${games[@]}"; do
    out="$tmp/$g.log"
    watch "$out" "$timeout_s" "GODOT-TEST-VERDICT" \
        "$godot" --headless --fixed-fps 60 --path "$repo" res://tests/godot/scenarios/scenario_runner.tscn \
        -- --game "$g" "${sel[@]}" --data-home "$home_real" ${dirs[@]+"${dirs[@]}"}

    grep -E '^SCENARIO |^    note: ' "$out"

    # Every requested scenario must report. Named runs are checked against the names asked
    # for; --all against the runner's own SCENARIO-PLAN line, whose absence is itself a failure
    # once the runner has printed a verdict (no verdict is reported below).
    if [ ${#names[@]} -gt 0 ]; then
        expected=("${names[@]}")
    else
        plan="$(grep -E '^SCENARIO-PLAN' "$out" | tail -1)"
        expected=()
        if [ -n "$plan" ]; then
            plan="${plan#SCENARIO-PLAN}"
            plan="${plan# }"
            [ -n "$plan" ] && IFS=, read -r -a expected <<<"$plan"
        elif grep -q '^GODOT-TEST-VERDICT ' "$out"; then
            echo "SCENARIO $g FAIL the runner printed no SCENARIO-PLAN"
            bad=1
        fi
    fi
    if grep -q '^GODOT-TEST-VERDICT ' "$out"; then
        for n in ${expected[@]+"${expected[@]}"}; do
            if ! grep -Eq "^SCENARIO $g/$n (PASS|FAIL|SKIP)" "$out"; then
                echo "SCENARIO $g/$n FAIL never reported"
                bad=1
            fi
        done
    fi

    fails=0
    while read -r _ id verdict _; do
        case "$verdict" in
            FAIL)
                fails=$((fails + 1))
                # A failure outside any scenario, "(runner)", can never be listed.
                case "$id" in */"(runner)") bad=1; continue ;; esac
                case "$listed" in
                    *" $id "*) echo "  ($id is a known failure: KNOWN_FAILURES)" ;;
                    *) bad=1 ;;
                esac ;;
            PASS)
                case "$listed" in
                    *" $id "*) echo "  $id now passes: remove it from KNOWN_FAILURES"; bad=1 ;;
                esac ;;
        esac
    done < <(grep -E '^SCENARIO ' "$out")

    verdict="$(grep -E '^GODOT-TEST-VERDICT ' "$out" | tail -1 | awk '{print $2}')"
    if [ -z "$verdict" ]; then
        echo "SCENARIO $g FAIL no verdict: crashed or timed out (limit ${timeout_s}s); last lines:"
        tail -15 "$out" | sed 's/^/    /'
        bad=1
    elif [ "$verdict" = FAIL ] && [ "$fails" -eq 0 ]; then
        echo "SCENARIO $g FAIL the runner failed outside any scenario; last lines:"
        grep -v '^\s*at: ' "$out" | tail -15 | sed 's/^/    /'
        bad=1
    fi
done

if [ "$bad" -ne 0 ]; then
    echo "scenarios: FAIL"
    exit 1
fi
echo "scenarios: ok"
exit 0
