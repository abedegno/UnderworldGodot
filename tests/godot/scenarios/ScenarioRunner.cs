using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace Underworld.Scenarios;

/// <summary>
/// Runs gameplay scenarios inside the real game, one game per process:
///
///   Godot --headless --fixed-fps 60 --path . res://tests/godot/scenarios/scenario_runner.tscn \
///     -- --game uw1|uw2 --scenarios a,b|--all --data-home REAL_HOME [--uw1-dir D] [--uw2-dir D]
///
/// Run it through tests/scenarios.sh, which gives Godot a temporary HOME. The runner refuses
/// to run when Godot's user data folder lies under --data-home, so it can never write the
/// player's own settings.json or saves.
///
/// Per scenario: point the game at the data the way LaunchMenu.OnLaunchInput does (without
/// its Save), install a seeded BorlandRandSource, instantiate Underworld.tscn, stop the intro
/// cutscene that main.StartGame queues, start a new game the way main.LaunchUWDemo does
/// (playerdat.InitEmptyPlayer, the demo's stand-in stats, uimanager.JourneyOnwards("DATA")),
/// then Setup, Act, Assert.
/// Prints "SCENARIO game/name PASS|FAIL reason|SKIP reason" per scenario and a final
/// "GODOT-TEST-VERDICT PASS|FAIL|SKIP". Godot's exit status is not to be trusted; the lines are
/// the result.
/// </summary>
public partial class ScenarioRunner : Node
{
    /// <summary>Every scenario. InAll false: run only when named (the harness's negative control).</summary>
    static readonly (Func<Scenario> Make, bool InAll)[] Registry =
    {
        (() => new ClockRuns(), true),
        (() => new HarnessFails(), false),
    };

    const string GameScene = "res://scenes/Underworld.tscn";

    /// <summary>Wall-clock limit for the intro cutscene to stop. Its splash waits are real time (Task.Delay).</summary>
    const ulong CutsceneStopMs = 20000;

    int _pass, _fail, _skip;

    public override void _Ready() => Callable.From(Run).CallDeferred();

    void Line(string game, string name, string verdict)
    {
        if (verdict.StartsWith("PASS")) _pass++;
        else if (verdict.StartsWith("SKIP")) _skip++;
        else _fail++;
        GD.Print($"SCENARIO {game}/{name} {verdict}");
    }

    async void Run()
    {
        try
        {
            await RunAll();
        }
        catch (Exception ex)
        {
            _fail++;
            GD.PrintErr("runner failed: " + ex);
        }
        string overall = _fail > 0 ? "FAIL" : _pass > 0 ? "PASS" : "SKIP";
        GD.Print($"GODOT-TEST-VERDICT {overall}");
        GetTree().Quit(_fail > 0 ? 1 : 0);
    }

    async Task RunAll()
    {
        var args = ParseArgs(OS.GetCmdlineUserArgs());
        string game = args.GetValueOrDefault("--game", "");
        if (game != "uw1" && game != "uw2")
        {
            _fail++;
            GD.PrintErr("usage: -- --game uw1|uw2 --scenarios a,b|--all --data-home DIR [--uw1-dir DIR] [--uw2-dir DIR]");
            return;
        }

        // Which scenarios. An unknown name is a failure, not silence.
        var chosen = new List<Scenario>();
        if (args.ContainsKey("--all"))
        {
            chosen.AddRange(Registry.Where(r => r.InAll).Select(r => r.Make()));
        }
        foreach (string n in args.GetValueOrDefault("--scenarios", "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var found = Registry.Select(r => r.Make()).FirstOrDefault(s => s.Name == n);
            if (found == null) Line(game, n, "FAIL no such scenario");
            else if (!chosen.Any(s => s.Name == n)) chosen.Add(found);
        }

        var applicable = new List<Scenario>();
        foreach (var s in chosen)
        {
            if (s.Games.Contains(game)) applicable.Add(s);
            else Line(game, s.Name, $"SKIP not a {game} scenario ({string.Join(", ", s.Games)} only)");
        }
        if (applicable.Count == 0) return;

        // Never run against the real user data folder: settings.json and saves live there.
        string dataHome = args.GetValueOrDefault("--data-home", OS.GetEnvironment("HOME"));
        string userDir = OS.GetUserDataDir();
        if (!string.IsNullOrEmpty(dataHome) && IsUnder(userDir, dataHome))
        {
            foreach (var s in applicable)
                Line(game, s.Name, "FAIL not isolated: Godot's user data folder is under the real home; run through tests/scenarios.sh");
            return;
        }

        string gameDir = args.GetValueOrDefault($"--{game}-dir",
            Path.Combine(dataHome ?? "", "UWGOG", game.ToUpperInvariant()));
        if (!Directory.Exists(Path.Combine(gameDir, "DATA")))
        {
            foreach (var s in applicable)
                Line(game, s.Name, $"SKIP no game data: {gameDir} has no DATA folder");
            return;
        }

        // As LaunchMenu.OnLaunchInput does, minus Save(): the settings stay in memory. Touch
        // uwsettings first so its static constructor (LoadSettings, which sets _RES and
        // BasePath from the file) has run and cannot overwrite these afterwards.
        _ = uwsettings.instance;
        if (game == "uw1")
        {
            uwsettings.instance.pathuw1 = gameDir;
            uwsettings.instance.gametoload = "UW1";
            UWClass._RES = UWClass.GAME_UW1;
        }
        else
        {
            uwsettings.instance.pathuw2 = gameDir;
            uwsettings.instance.gametoload = "UW2";
            UWClass._RES = UWClass.GAME_UW2;
        }
        UWClass.BasePath = gameDir;

        var packed = GD.Load<PackedScene>(GameScene);
        if (packed == null)
        {
            foreach (var s in applicable) Line(game, s.Name, $"FAIL {GameScene} did not load");
            return;
        }

        foreach (var s in applicable)
        {
            Line(game, s.Name, await RunOne(s, game, packed));
        }
    }

    async Task<string> RunOne(Scenario s, string game, PackedScene packed)
    {
        GD.Print($"  {game}/{s.Name}: {s.Cites}");
        var previousSource = Rng.Source;
        Node root = null;
        try
        {
            Rng.Source = new BorlandRandSource(s.Seed);

            // The value a fresh process starts with. A previous scenario left it at GAME, and
            // the intro cutscene restores whatever mode it found when it ends.
            uimanager.CurrentGameMode = uimanager.GameModes.CUTSCENE;

            root = packed.Instantiate();
            root.Name = "Underworld";   // the game looks itself up at /root/Underworld
            GetTree().Root.AddChild(root);   // uimanager._Ready runs main.StartGame here

            if (root is not UnderworldRoot uwRoot)
                return "FAIL Underworld.tscn's root is not an UnderworldRoot";
            if (!uwRoot.FontsReady)
                return $"FAIL the game did not start: fonts not loaded ({uwRoot.FontError ?? "no reason"})";

            string notStarted = await StartNewGame();
            if (notStarted != null) return "FAIL " + notStarted;

            var c = new ScenarioContext(this, game, root);
            await s.Setup(c);
            await s.Act(c);
            string reason = s.Assert(c);
            return reason == null ? "PASS" : "FAIL " + reason;
        }
        catch (Exception ex)
        {
            string first = (ex.GetType().Name + ": " + ex.Message).Split('\n')[0];
            GD.PrintErr($"{game}/{s.Name} threw: {ex}");
            return "FAIL " + first;
        }
        finally
        {
            Rng.Source = previousSource;
            if (root != null && IsInstanceValid(root))
            {
                await DrainAudio();
                root.QueueFree();
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
        }
    }

    /// <summary>
    /// From the scene just added to "in game on level 1": stop the intro cutscene that
    /// main.StartGame queued, wait for its callback (uimanager.ReturnToMainMenu) to reach the
    /// main menu, then make the calls main.LaunchUWDemo makes. Returns null or why it failed.
    /// </summary>
    async Task<string> StartNewGame()
    {
        cutsplayer.StopCutscene();
        ulong deadline = Time.GetTicksMsec() + CutsceneStopMs;
        while (cutsplayer.IsPlaying || uimanager.CurrentGameMode != uimanager.GameModes.MAIN)
        {
            if (Time.GetTicksMsec() > deadline)
                return $"the intro cutscene did not stop (mode {uimanager.CurrentGameMode}, playing {cutsplayer.IsPlaying})";
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        uimanager.EnableDisable(uimanager.instance.PanelMainMenu, false);
        playerdat.InitEmptyPlayer();
        // InitEmptyPlayer leaves max_hp and play_hp at 0 and the attributes unset, and with
        // those alone the Avatar dies on the first PlayerTimedLoop. These are the values
        // main.LaunchUWDemo itself gives a demo with no DPLAYER file (main.cs:212-217), the
        // game's own stand-in for chargen. The chargen screens would roll real ones.
        playerdat.CharName = "DEMO";
        playerdat.max_hp = 30;
        playerdat.play_hp = 30;
        playerdat.STR = 20;
        playerdat.INT = 20;
        playerdat.DEX = 20;
        uimanager.instance.JourneyOnwards("DATA");
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

        if (!uimanager.InGame) return $"the new game is not in game (mode {uimanager.CurrentGameMode})";
        if (playerdat.dungeon_level != 1) return $"the new game is on level {playerdat.dungeon_level}, not 1";
        if (playerdat.play_hp <= 0) return $"the new game's Avatar has {playerdat.play_hp} hit points";
        return null;
    }

    /// <summary>
    /// Lets the AudioServer let go of the game's music and sound playbacks before the scene is
    /// freed, as UnderworldRoot.DrainAudioThenQuit does on the game's own quit path. Freeing the
    /// scene without this leaves a playback the server still owns, and Godot 4.3 then hangs on
    /// shutdown (issue #78): measured here, the process never exited after its verdict.
    /// The wait is real time (Task.Delay), not a scene timer, because under --fixed-fps a
    /// timer counts game time and frames run faster than real time.
    /// </summary>
    async Task DrainAudio()
    {
        var music = MusicStreamPlayer.Instance;
        var sfx = Sfx.SfxStreamPlayer.Instance;
        bool stopped = false;
        for (int attempt = 0; !stopped && attempt < 4; attempt++)
        {
            if (attempt > 0) await Task.Delay(100);
            int join = attempt == 0 ? 500 : 50;
            bool m = music == null || !IsInstanceValid(music) || music.BeginGodotAudioShutdown(join);
            bool f = sfx == null || !IsInstanceValid(sfx) || sfx.BeginGodotAudioShutdown(join);
            stopped = m && f;
        }
        await Task.Delay(250);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (!stopped)
        {
            GD.PushError("Audio producers did not stop; leaving their objects alone.");
            return;
        }
        if (music != null && IsInstanceValid(music)) music.ReleaseGodotAudioBindings();
        if (sfx != null && IsInstanceValid(sfx)) sfx.ReleaseGodotAudioBindings();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    static bool IsUnder(string path, string dir)
    {
        string p = Path.GetFullPath(path).TrimEnd('/') + "/";
        string d = Path.GetFullPath(dir).TrimEnd('/') + "/";
        return p.StartsWith(d, StringComparison.Ordinal);
    }

    /// <summary>"--key value" pairs; a flag with no value (e.g. --all) maps to "".</summary>
    static Dictionary<string, string> ParseArgs(string[] argv)
    {
        var d = new Dictionary<string, string>();
        for (int i = 0; i < argv.Length; i++)
        {
            if (!argv[i].StartsWith("--")) continue;
            if (i + 1 < argv.Length && !argv[i + 1].StartsWith("--")) d[argv[i]] = argv[++i];
            else d[argv[i]] = "";
        }
        return d;
    }
}

/// <summary>
/// The harness's own negative control: it always fails, so a run that names it must fail
/// unless KNOWN_FAILURES lists it. Run only by name; --all leaves it out. UW2 only, so a
/// run of it boots one game.
/// </summary>
public sealed class HarnessFails : Scenario
{
    public override string Name => "harness-fails";
    public override string[] Games => new[] { "uw2" };
    public override string Cites => "none: a deliberate failure that tests the harness";
    public override Task Setup(ScenarioContext c) => Task.CompletedTask;
    public override Task Act(ScenarioContext c) => c.Frames(1);
    public override string Assert(ScenarioContext c) => "deliberate failure";
}
