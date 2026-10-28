# Gameplay scenarios

These tests run inside the real game. The runner boots `res://scenes/Underworld.tscn` headless, starts a new game on level 1, sets up a state, lets real physics frames pass, and checks the game state afterwards. They need Godot 4.3 mono and the game data, so they are separate from the `dotnet test` suites.

## Running them

```
tests/scenarios.sh                       # every scenario, both games
tests/scenarios.sh clock-runs            # just these, by name
tests/scenarios.sh --game uw2 clock-runs # one game
tests/scenarios.sh --uw1-dir /path/to/UW1 --uw2-dir /path/to/UW2
```

The script uses the Godot 4.3 mono binary named by `$GODOT`. Without it, it tries `godot` and `godot4` on the PATH and then a few usual install places (for example `~/Applications/Godot43_mono.app` on macOS); anywhere else, set `GODOT`. With none it prints `scenarios: no Godot 4.3, skipped` and exits 0. A `$GODOT` that is set but cannot be run fails the run instead, so a broken setup is never mistaken for a skip.

The mono build hangs if it cannot find the .NET runtime. The script uses `DOTNET_ROOT` if it is set, and otherwise derives it from where `dotnet` on the PATH really lives; for a user install outside the standard places, set it (for example `DOTNET_ROOT=$HOME/.dotnet`). It builds `Underworld.csproj` first (set `SCENARIO_NO_BUILD=1` to skip that; without `dotnet` on the PATH it warns that the C# may be stale) and imports the project once if `.godot/imported` is missing or empty. The import has its own limit, `SCENARIO_IMPORT_TIMEOUT` (default 180 seconds); an import that finished but left Godot hanging on exit is accepted, and the run fails with a clear message if the import did not happen.

Each game's folder, the one that holds `DATA`, comes from `--uw1-dir` / `--uw2-dir`, else the `UW1_DIR` / `UW2_DIR` environment variables, else `pathuw1` / `pathuw2` in the game's own `settings.json` (the launcher's choice; read, never written). A game with no folder, or whose folder has no `DATA`, is skipped, not failed. The data is only read.

Each game runs in its own Godot process. The script prints one line per scenario, `SCENARIO <game>/<name> PASS`, `FAIL <reason>` or `SKIP <reason>`, with any notes the scenario wrote above it. It exits 0 only when every game printed its `GODOT-TEST-VERDICT` line, every requested scenario printed a line, no scenario failed that `KNOWN_FAILURES` does not list, and no listed scenario passed. A requested scenario with no line fails as "never reported": for named runs the script checks the names it asked for, and for a full run it checks the `SCENARIO-PLAN` line the runner prints first. A failure in the runner itself, outside any scenario, prints `SCENARIO <game>/(runner) FAIL <reason>`, which `KNOWN_FAILURES` cannot list.

Godot 4.3 mono can crash or hang on shutdown, so its exit status is ignored. Only the printed lines count. A game that prints no verdict within `SCENARIO_TIMEOUT` seconds (a whole number, default 300) fails the run with "no verdict: crashed or timed out".

`tests/check.sh` runs them after the test projects and prints `ok    scenarios  N pass, M known failures, K skipped` (the counts from the `scenarios: ok|FAIL (...)` summary line of `scenarios.sh`) or `FAIL  scenarios  ...` with each problem beneath. It asks `tests/scenarios.sh --find-godot` whether there is a Godot, so the two scripts agree: no Godot at all prints `skip  scenarios  (no Godot 4.3)`, a `$GODOT` that cannot run fails, and `CHECK_SKIP_SCENARIOS=1` skips them on purpose.

## Your settings are never touched

Godot runs with a temporary `HOME`, removed afterwards, and with `XDG_DATA_HOME`, `XDG_CONFIG_HOME` and `XDG_CACHE_HOME` (Linux) and `APPDATA` and `LOCALAPPDATA` (Windows) pointed inside it, so Godot's user data folder, with `user://settings.json`, its logs and screenshots, is there and the player's own is never written. The runner sets the game paths in memory only and never saves them. The runner is told the temporary home (`--temp-home`) and refuses to run (every scenario fails with "not isolated") unless Godot's user data folder is inside it. So do not run `scenario_runner.tscn` by hand with your own `HOME`: Godot itself would write its log into your real user data folder before the runner could refuse.

Saves are not in the user data folder: the game writes them to `SAVE1` to `SAVE4` in its own game folder (`BasePath`, see `src/savegame/SaveGame.cs` and `SlotTransaction.cs`). So the runner never points the game at the real game folder. It builds a temporary game folder inside the temporary home, a real directory holding a symbolic link to each entry of the real folder except the save slots, which are new empty directories, and sets `BasePath` to it. A scenario that saves therefore writes only into the temporary home, and the player's saves are neither read nor overwritten. Symbolic links need no privilege on macOS and Linux; on Windows they need Developer Mode, and without it every scenario fails ("could not build the temporary game folder") rather than run against the real folder.

## How a new game is started

For each scenario the runner points the game at the data the way `LaunchMenu.OnLaunchInput` does, installs `Rng.Source = new BorlandRandSource(scenario.Seed)`, adds `Underworld.tscn` to the tree (its `uimanager._Ready` calls `main.StartGame`, which queues the title cutscene), stops that cutscene and the introduction that follows it when there is no saved game (as there never is in the temporary game folder), and waits for the main menu, then makes the calls `main.LaunchUWDemo` makes: `playerdat.InitEmptyPlayer()`, the demo's stand-in stats (30 hit points, 20 in each attribute) and the recompute of the maxima, mana and carrying limit from them, and `uimanager.instance.JourneyOnwards("DATA")`. It checks the game is then in game, on level 1, with the Avatar alive. Without the stand-in stats the Avatar has 0 hit points and dies on the first frame. The character creation screens are skipped, so anything they would roll is not there. The runner then installs a fresh `BorlandRandSource(scenario.Seed)` again, so `Setup` starts from exactly the scenario's seed (the first one makes the new game's own draws repeatable).

After the scenario the runner restores `Rng.Source`, lets the audio server release the game's music and sound (as the game's own quit path does, or Godot hangs on exit, issue #78), and frees the scene. The next scenario starts a fresh new game. Static game state that a new game does not reset can still carry over between scenarios in one process, so a scenario must not rely on anything it did not set up.

## Writing a scenario

Add a class that derives from `Scenario` in this folder and add it to `Registry` in `ScenarioRunner.cs`.

- `Name`: the name used on the command line, such as `clock-runs`.
- `Games`: the games it applies to (both by default); the other prints SKIP.
- `Cites`: where the expected behaviour is in the original, as `file:line` in [underworld-exhumed](https://github.com/abedegno/underworld-exhumed)'s `uwN/src` (the byte-matched decompilation of the original games), which is what "underworld-exhumed uw1/src/..." in the code means. Every expected value comes from the original's code, never from what the port does.
- `Seed`: the random seed (1 by default).
- `Setup(c)`: put the game in the state you need and record what you will compare against.
- `Act(c)`: do the thing. `await c.Frames(n)` lets n physics frames pass, which is n/60 seconds of game time under `--fixed-fps 60`.
- `Assert(c)`: return `null` for a pass or the reason for a failure. `Checks` collects several expected-against-actual comparisons, each with its citation, and reports every mismatch.

Assert the precondition as well as the effect. A scenario must fail if Setup did not get the game where it meant to, or if the action never happened, rather than pass by accident. `clock-runs` shows the shape: it records whether the game was in game when it read the clock, and fails if not. `c.Note(text)` prints a line above the verdict to help explain a failure.

An exception from Setup, Act or Assert fails the scenario with its first line. An exception inside the game's own frame callbacks does not reach the runner; Godot logs it, and the scenario's assertions have to notice its effect.

`harness-fails` always fails. It tests the harness itself and runs only when named; `tests/scenarios.sh harness-fails` must exit 1.

## KNOWN_FAILURES

`KNOWN_FAILURES` lists scenarios the port does not pass yet, one per line: `<game>/<name>  ~<part of the FAIL reason>  # the bug`, with the port's file and line and the original's. A listed scenario still runs and prints its failure but does not fail the run, as long as it fails for the listed reason: the reason must contain the `~` text, and a reason that starts `precondition:` or names an exception never counts, because then the scenario no longer reaches the bug it tests. If it starts passing, the run fails until its line is removed, so the list cannot go stale. A malformed line fails the run.
