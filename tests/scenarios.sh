#!/usr/bin/env bash
# Runs the gameplay scenarios (tests/godot/scenarios) in headless Godot 4.3, one process per game.
#
#   tests/scenarios.sh [--game uw1|uw2] [--uw1-dir DIR] [--uw2-dir DIR] [--reverse|--shuffle SEED] [name...]
#   tests/scenarios.sh --find-godot
#
# No names runs every scenario (--all); names run just those, including ones --all leaves out.
# --reverse runs them last first, --shuffle SEED in an order shuffled by the whole number SEED
# (the same seed gives the same order), to show no scenario depends on one run before it.
# SCENARIO_STATE_REPORT=1 also prints the runner's STATE lines: the statics each scenario left
# changed, and those that differ at a scenario's start from the first one's (see the README).
# Both games by default. Each game's folder (the one holding DATA) comes from --uwN-dir, else the
# UW1_DIR / UW2_DIR environment variable, else pathuw1 / pathuw2 in the game's own settings.json,
# read and never written. A game with no folder, or one with no DATA, is skipped.
#
# Exit 0 only when every game printed a verdict, every requested scenario reported, no scenario
# failed that KNOWN_FAILURES does not list (or failed for another reason than the listed one),
# and no listed scenario passed. Godot 4.3 mono's exit status is ignored: it can fault or hang on
# shutdown, so only the printed lines count, and no verdict within SCENARIO_TIMEOUT seconds
# (default 300) is a failure.
#
# Godot runs with a temporary HOME (and XDG, APPDATA and LOCALAPPDATA inside it), so the player's
# user://settings.json, logs and screenshots are never touched; the runner refuses to run unless
# Godot's user data folder is inside it. The game itself runs from a temporary game folder the
# runner builds there (the real folder's contents linked, empty SAVEn folders), so a save never
# lands in the real game folder.
#
# Environment: GODOT (the Godot 4.3 mono binary; set but not runnable is a failure, not a skip),
# UW1_DIR, UW2_DIR, SCENARIO_TIMEOUT (whole seconds per game), SCENARIO_IMPORT_TIMEOUT (whole
# seconds for the one-time import, default 180), DOTNET_ROOT (where the .NET runtime is, if dotnet
# on the PATH does not say), SCENARIO_NO_BUILD=1 to skip the C# build, SCENARIO_STATE_REPORT=1,
# SCENARIO_STATE_GUARD=0 (the runner does not restore the game's statics between scenarios).
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
find_mode=0
order=()
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
        --reverse)
            order=(--order reverse); shift ;;
        --shuffle)
            if ! printf '%s' "${2:-}" | grep -Eq '^[0-9]+$'; then echo "scenarios: --shuffle takes a whole-number seed" >&2; exit 2; fi
            order=(--order "shuffle:$2"); shift 2 ;;
        --find-godot)
            # Prints the Godot 4.3 this script would use and exits 0; or prints why not and
            # exits 1 (GODOT set but not runnable) or 3 (none found).
            find_mode=1; shift ;;
        -h|--help)
            sed -n '2,14p' "$0"; exit 0 ;;
        -*)
            echo "scenarios: unknown option $1" >&2; exit 2 ;;
        *)
            names+=("$1"); shift ;;
    esac
done

# Find Godot 4.3. One asked for by $GODOT that cannot run is a failure: skipping would hide a
# broken setup, in CI above all. Only a Godot nobody asked for and nobody has is a skip.
# tests/check.sh asks through --find-godot, so the two scripts cannot disagree.
# Sets $godot. Returns 0 found, 1 asked for but not runnable ($godot_why says why), 3 none.
find_godot() {
    godot="${GODOT:-}"
    godot_why=""
    if [ -n "$godot" ]; then
        if [ -x "$godot" ] && [ ! -d "$godot" ]; then return 0; fi
        godot_why="GODOT=$godot is not an executable file"
        return 1
    fi
    # On the PATH first, then a few usual install places. These are examples, not requirements:
    # anywhere else, set GODOT.
    local c
    for c in "$(command -v godot 2>/dev/null)" "$(command -v godot4 2>/dev/null)" \
             "$home_real/Applications/Godot43_mono.app/Contents/MacOS/Godot" \
             "/Applications/Godot43_mono.app/Contents/MacOS/Godot"; do
        if [ -n "$c" ] && [ -x "$c" ] && [ ! -d "$c" ]; then godot="$c"; return 0; fi
    done
    godot=""
    return 3
}
find_godot; found=$?
if [ "$find_mode" = 1 ]; then
    case $found in
        0) echo "$godot" ;;
        1) echo "$godot_why" ;;
        3) echo "no Godot 4.3" ;;
    esac
    exit $found
fi
if [ $found -eq 1 ]; then
    echo "scenarios: FAIL, $godot_why"
    exit 1
fi
if [ $found -eq 3 ]; then
    echo "scenarios: no Godot 4.3, skipped"
    exit 0
fi

# The mono build cannot find the runtime without DOTNET_ROOT when .NET is not installed in a
# standard place, and then hangs. Taken from DOTNET_ROOT if set, else from where dotnet on the
# PATH really lives (following its links); e.g. DOTNET_ROOT=$HOME/.dotnet for a user install.
if [ -z "${DOTNET_ROOT:-}" ] && command -v dotnet >/dev/null 2>&1; then
    d="$(command -v dotnet)"
    for _ in 1 2 3 4 5 6 7 8; do
        l="$(readlink "$d" 2>/dev/null)" || break
        case "$l" in /*) d="$l" ;; *) d="$(dirname "$d")/$l" ;; esac
    done
    export DOTNET_ROOT="$(cd "$(dirname "$d")" && pwd -P)"
fi
if [ -n "${DOTNET_ROOT:-}" ]; then export PATH="$DOTNET_ROOT:$PATH"; fi

# Each game's folder: --uwN-dir, else UWN_DIR, else the game's own settings (read only).
# Godot keeps them in user://settings.json, which is under the real home on each platform.
settings_file() {
    local f
    for f in "$home_real/Library/Application Support/Godot/app_userdata/Underworld/settings.json" \
             "${XDG_DATA_HOME:-$home_real/.local/share}/godot/app_userdata/Underworld/settings.json" \
             "${APPDATA:-$home_real/AppData/Roaming}/Godot/app_userdata/Underworld/settings.json"; do
        [ -f "$f" ] && { printf '%s' "$f"; return 0; }
    done
    return 1
}
setting() {   # setting KEY: the string value of "KEY" in settings.json, JSON escapes for \ and /
    local f; f="$(settings_file)" || return 0
    grep -io "\"$1\" *: *\"[^\"]*\"" "$f" | head -1 | sed -E 's/^[^:]*: *"(.*)"$/\1/; s/\\\\/\\/g; s/\\\//\//g'
}
uw1_dir=""; uw2_dir=""
i=0
while [ $i -lt ${#dirs[@]} ]; do
    case "${dirs[$i]}" in --uw1-dir) uw1_dir="${dirs[$((i + 1))]}" ;; --uw2-dir) uw2_dir="${dirs[$((i + 1))]}" ;; esac
    i=$((i + 2))
done
[ -z "$uw1_dir" ] && uw1_dir="${UW1_DIR:-$(setting pathuw1)}"
[ -z "$uw2_dir" ] && uw2_dir="${UW2_DIR:-$(setting pathuw2)}"

tmp="$(mktemp -d "${TMPDIR:-/tmp}/uw-scenarios.XXXXXX")"
trap 'rm -rf "$tmp"' EXIT
mkdir -p "$tmp/home"
# The physical path, so the runner's "is Godot's user folder inside it" test compares like with like.
home_tmp="$(cd "$tmp/home" && pwd -P)"
mkdir -p "$home_tmp/.local/share" "$home_tmp/.config" "$home_tmp/.cache" "$home_tmp/AppData/Roaming" "$home_tmp/AppData/Local"

# Runs a command in the background with output to $1, killing it after $2 seconds. With a third
# argument, it also stops waiting a few seconds after that text appears in the output, because
# Godot 4.3 can hang on shutdown after it has said everything it is going to.
# Returns 0 if the process ended by itself, 1 if it was killed.
watch() {
    local out="$1" limit="$2" marker="$3"; shift 3
    # The XDG variables too: on Linux, Godot puts user data and editor settings under them
    # when they are set, whatever HOME says. APPDATA and LOCALAPPDATA are Windows' equivalents.
    HOME="$home_tmp" XDG_DATA_HOME="$home_tmp/.local/share" XDG_CONFIG_HOME="$home_tmp/.config" \
        XDG_CACHE_HOME="$home_tmp/.cache" APPDATA="$home_tmp/AppData/Roaming" \
        LOCALAPPDATA="$home_tmp/AppData/Local" "$@" >"$out" 2>&1 &
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
if [ "${SCENARIO_NO_BUILD:-0}" != 1 ]; then
    if command -v dotnet >/dev/null 2>&1; then
        if ! (cd "$repo" && dotnet build Underworld.csproj -nologo -v q >"$tmp/build.log" 2>&1); then
            tail -20 "$tmp/build.log"
            echo "scenarios: FAIL, the C# build failed"
            exit 1
        fi
    else
        echo "scenarios: warning: no dotnet on the PATH, so the C# was not built and may be stale"
    fi
fi
# Imported means .godot/imported holds files: the folder alone can be left by an import that died.
imported() { [ -d "$repo/.godot/imported" ] && [ -n "$(ls -A "$repo/.godot/imported" 2>/dev/null)" ]; }
if ! imported; then
    echo "scenarios: importing the project (once; up to ${import_timeout_s}s)"
    if ! watch "$tmp/import.log" "$import_timeout_s" "" "$godot" --headless --path "$repo" --import; then
        # Godot 4.3 can hang on exit after the import has finished; only a missing import fails.
        if imported; then
            echo "scenarios: the import finished but Godot did not exit; carrying on"
        else
            tail -15 "$tmp/import.log" | sed 's/^/    /'
            echo "scenarios: FAIL, the import did not finish within ${import_timeout_s}s (SCENARIO_IMPORT_TIMEOUT)"
            exit 1
        fi
    fi
    if ! imported; then
        tail -15 "$tmp/import.log" | sed 's/^/    /'
        echo "scenarios: FAIL, the import ended but .godot/imported is empty"
        exit 1
    fi
fi

# KNOWN_FAILURES: "<game>/<name>  [~reason substring]  # why". A malformed line fails the run
# rather than being ignored. With a substring, only a FAIL whose reason contains it is the known
# failure; any FAIL whose reason starts "precondition:" or names an exception is not, since the
# scenario no longer reached the bug it tests.
listed=" "
reasons=""   # lines of "<game>/<name><TAB><substring>"
bad=0
problems=()
npass=0; nknown=0; nskip=0
if [ -f "$known" ]; then
    while IFS= read -r line || [ -n "$line" ]; do
        body="${line%%#*}"
        entry="$(printf '%s' "${body%%~*}" | tr -d '[:space:]')"
        [ -z "$entry" ] && [ -z "$(printf '%s' "$body" | tr -d '[:space:]')" ] && continue
        substr=""
        case "$body" in *"~"*)
            substr="$(printf '%s' "${body#*~}" | sed -E 's/^[[:space:]]+//; s/[[:space:]]+$//')"
            [ -z "$substr" ] && entry="" ;;   # an empty ~ is malformed
        esac
        if ! printf '%s' "$entry" | grep -Eq '^uw[12]/[a-z0-9-]+$'; then
            echo "KNOWN_FAILURES: not <game>/<name>: $line"
            problems+=("KNOWN_FAILURES: not <game>/<name>: $line")
            bad=1
            continue
        fi
        listed="$listed$entry "
        [ -n "$substr" ] && reasons="$reasons$entry	$substr
"
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
        -- --game "$g" "${sel[@]}" ${order[@]+"${order[@]}"} --temp-home "$home_tmp" --uw1-dir "$uw1_dir" --uw2-dir "$uw2_dir"

    if [ ${#order[@]} -gt 0 ]; then grep -E '^SCENARIO-ORDER ' "$out" | sed "s/^SCENARIO-ORDER /scenarios: $g order /"; fi
    if [ "${SCENARIO_STATE_REPORT:-0}" = 1 ]; then grep -E '^STATE' "$out"; fi
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
            problems+=("SCENARIO $g FAIL the runner printed no SCENARIO-PLAN")
            bad=1
        fi
    fi
    if grep -q '^GODOT-TEST-VERDICT ' "$out"; then
        for n in ${expected[@]+"${expected[@]}"}; do
            if ! grep -Eq "^SCENARIO $g/$n (PASS|FAIL|SKIP)" "$out"; then
                echo "SCENARIO $g/$n FAIL never reported"
                problems+=("SCENARIO $g/$n FAIL never reported")
                bad=1
            fi
        done
    fi

    fails=0
    while read -r sline; do
        read -r _ id verdict _ <<<"$sline"
        case "$verdict" in
            FAIL)
                fails=$((fails + 1))
                # A failure outside any scenario, "(runner)", can never be listed.
                case "$id" in */"(runner)") problems+=("$sline"); bad=1; continue ;; esac
                case "$listed" in
                    *" $id "*)
                        reason="${sline#* FAIL }"
                        want="$(printf '%s' "$reasons" | awk -F'\t' -v id="$id" '$1 == id { print $2; exit }')"
                        other=""
                        case "$reason" in precondition:*) other="its precondition failed" ;; esac
                        if [ -z "$other" ] && printf '%s' "$reason" | grep -Eq '[A-Za-z]+Exception:'; then other="it threw"; fi
                        if [ -z "$other" ] && [ -n "$want" ]; then
                            case "$reason" in *"$want"*) ;; *) other="its reason lacks '$want'" ;; esac
                        fi
                        if [ -z "$other" ]; then
                            echo "  ($id is a known failure: KNOWN_FAILURES)"; nknown=$((nknown + 1))
                        else
                            echo "  $id is listed, but $other: it no longer tests its known bug"
                            problems+=("$sline (listed in KNOWN_FAILURES, but $other)"); bad=1
                        fi ;;
                    *) problems+=("$sline"); bad=1 ;;
                esac ;;
            PASS)
                npass=$((npass + 1))
                case "$listed" in
                    *" $id "*) echo "  $id now passes: remove it from KNOWN_FAILURES"
                               problems+=("$id now passes: remove it from KNOWN_FAILURES"); bad=1 ;;
                esac ;;
            SKIP)
                nskip=$((nskip + 1)) ;;
        esac
    done < <(grep -E '^SCENARIO ' "$out")

    verdict="$(grep -E '^GODOT-TEST-VERDICT ' "$out" | tail -1 | awk '{print $2}')"
    if [ -z "$verdict" ]; then
        echo "SCENARIO $g FAIL no verdict: crashed or timed out (limit ${timeout_s}s); last lines:"
        tail -15 "$out" | sed 's/^/    /'
        problems+=("SCENARIO $g FAIL no verdict: crashed or timed out (limit ${timeout_s}s)")
        bad=1
    elif [ "$verdict" = FAIL ] && [ "$fails" -eq 0 ]; then
        echo "SCENARIO $g FAIL the runner failed outside any scenario; last lines:"
        grep -v '^\s*at: ' "$out" | tail -15 | sed 's/^/    /'
        problems+=("SCENARIO $g FAIL the runner failed outside any scenario")
        bad=1
    fi
done

# The last lines are the summary tests/check.sh reads: the counts, then each problem.
counts="$npass pass, $nknown known failures, $nskip skipped"
if [ "$bad" -ne 0 ]; then
    echo "scenarios: FAIL ($counts)"
    for p in ${problems[@]+"${problems[@]}"}; do echo "  problem: $p"; done
    exit 1
fi
echo "scenarios: ok ($counts)"
exit 0
