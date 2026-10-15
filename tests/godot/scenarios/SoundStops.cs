using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using Underworld.Sfx;

namespace Underworld.Scenarios;

/// <summary>
/// Records what the game asks the sound-effect backend to do, in place of the OPL backend.
/// SoundEffects (src/audio/sfx/godot/SoundEffects.cs) keeps its backend in a private field; the
/// scenario swaps this in for the scenario's length and puts the real one back. What reaches a
/// backend is the port's whole decision, and reading it here does not depend on the OPL producer
/// thread, which runs only as the headless audio driver takes its output, nor on how long each
/// effect's own patch sounds.
/// </summary>
sealed class RecordingSfx : ISfxBackend
{
    public readonly System.Collections.Generic.List<int> Played = new();
    public readonly System.Collections.Generic.List<int> Stopped = new();
    public void Play(SoundEntry entry, byte pan, byte velocityOffset, int handle) => Played.Add(handle);
    public void Stop(int handle) => Stopped.Add(handle);
    public void Dispose() { }

    static readonly FieldInfo Backend = typeof(SoundEffects).GetField("_backend", BindingFlags.NonPublic | BindingFlags.Static);
    object _real;

    /// <summary>Installs the recorder; null, or why it could not.</summary>
    public string Install()
    {
        if (Backend == null) return "SoundEffects._backend not found";
        if (SoundEffects.SoundDat.Length == 0) return "SOUNDS.DAT did not load";
        if (!playerdat.SoundEffectsEnabled) return "sound effects are off in the new game";
        _real = Backend.GetValue(null);
        Backend.SetValue(null, this);
        return null;
    }

    public void Remove() => Backend?.SetValue(null, _real);

    public bool StoppedAll => Stopped.Contains(SfxCommand.AllHandles);
}

/// <summary>
/// The water sound stops when the player leaves the water. The original's make_noise plays
/// effect 0 while the player swims, keeps its handle in water_eff, and once out of the water
/// calls kill_effect(water_eff) (uw1 PLAYMOVE.C:436-452, uw2 PLAYMOVE.C:430-455), which silences
/// that effect at once (uw1 SOUND.C:627-637, uw2 SOUND.C:1204-1220). UW2 has no SP00.VOC, so its
/// water also plays on the MIDI-effect backend.
///
/// The scenario sets the swimming bit of the player's tile state, lets the game loop's FootSteps
/// (main.cs) start the water sound, clears the bit and lets a few frames pass.
/// </summary>
public sealed class WaterSoundStops : Scenario
{
    public override string Name => "water-sound-stops";
    public override string Cites => "underworld-exhumed uw1/src/motion/PLAYMOVE.C:436-452, uw2/src/motion/PLAYMOVE.C:430-455";

    readonly RecordingSfx _sfx = new();
    string _problem;
    int _water = -1;
    bool _stoppedInWater, _stoppedAfter;

    public override Task Setup(ScenarioContext c)
    {
        _problem = _sfx.Install();
        return Task.CompletedTask;
    }

    public override async Task Act(ScenarioContext c)
    {
        if (_problem != null) return;
        playerdat.TileState |= 1;
        await c.Frames(5);
        if (_sfx.Played.Count > 0) _water = _sfx.Played[0];
        _stoppedInWater = _sfx.Stopped.Contains(_water);
        playerdat.TileState &= ~1;
        await c.Frames(5);
        _stoppedAfter = _sfx.Stopped.Contains(_water);
        _sfx.Remove();
        c.Note($"effects started {_sfx.Played.Count}, stops [{string.Join(", ", _sfx.Stopped)}], water handle {_water}");
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        if (_water < 0) return "precondition: no water sound started in the water";
        if (_stoppedInWater) return "precondition: the water sound was stopped while still in the water";
        var k = new Checks();
        k.True(_stoppedAfter, "water sound not stopped after leaving the water (kill_effect(water_eff): uw1 PLAYMOVE.C:451, uw2 PLAYMOVE.C:453)");
        return k.Result;
    }
}

/// <summary>
/// Turning the sound effects off in the options silences the effects already playing: the
/// options page calls turn_fx(0) (uw1 OPTIONS.C:265, uw2 WRAPPER.C:310), which kills every
/// effect (uw1 SOUND.C:484-493 through seg014_1DC5_C7C; uw2 SOUND.C:1035-1045 through
/// kill_all_effects). The scenario starts an effect, then sends the options page's own input
/// handler a click on "off" on the sound page.
/// </summary>
public sealed class EffectsStopSoundOff : Scenario
{
    public override string Name => "effects-stop-sound-off";
    public override string Cites => "underworld-exhumed uw1/src/sound/SOUND.C:484-493, uw2/src/sound/SOUND.C:1035-1045";

    readonly RecordingSfx _sfx = new();
    string _problem;
    bool _stoppedBefore, _stoppedAfter, _offAfter;

    public override Task Setup(ScenarioContext c)
    {
        _problem = _sfx.Install();
        return Task.CompletedTask;
    }

    public override async Task Act(ScenarioContext c)
    {
        if (_problem != null) return;
        Sfx.SoundEffects.Play(0);
        await c.Frames(2);
        _stoppedBefore = _sfx.StoppedAll;
        var handler = typeof(uimanager).GetMethod("_on_game_options_input", BindingFlags.NonPublic | BindingFlags.Instance);
        if (handler == null) { _problem = "uimanager._on_game_options_input not found"; _sfx.Remove(); return; }
        uimanager.CurrentGameOptionMenu = uimanager.OptionMenus.SoundMenu;
        var click = new InputEventMouseButton { Pressed = true, ButtonIndex = MouseButton.Left };
        // UW2 buttons act on the mouse's release (better_mouse_release); press the button directly where the
        // panel has PressOptionButton, else click through the handler.
        var press = typeof(uimanager).GetMethod("PressOptionButton", BindingFlags.NonPublic | BindingFlags.Instance);
        if (press != null) press.Invoke(uimanager.instance, new object[] { 3L }); // 3: turn off
        else handler.Invoke(uimanager.instance, new object[] { click, 3L }); // 3: turn off
        _offAfter = !playerdat.SoundEffectsEnabled;
        await c.Frames(2);
        _stoppedAfter = _sfx.StoppedAll;
        _sfx.Remove();
        c.Note($"stops [{string.Join(", ", _sfx.Stopped)}] (-1 is every effect); sound off {_offAfter}");
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        if (_stoppedBefore) return "precondition: every effect was stopped before the click";
        if (!_offAfter) return "precondition: the options click did not turn the sound off";
        var k = new Checks();
        k.True(_stoppedAfter, "effects not stopped when the sound was turned off (turn_fx(0): uw1 SOUND.C:492-493, uw2 SOUND.C:1044-1045)");
        return k.Result;
    }
}

/// <summary>
/// Opening the map screen silences the effects in UW2 (kill_all_effects, uw2 AUTOMAP.C:123) but
/// not in UW1, whose map screen leaves them alone. The scenario opens the map of the current
/// level as the map scroll does (src/objects/map.cs).
/// </summary>
public sealed class MapStopsEffects : Scenario
{
    public override string Name => "map-stops-effects";
    public override string Cites => "underworld-exhumed uw2/src/ui/AUTOMAP.C:123";

    readonly RecordingSfx _sfx = new();
    string _problem;
    bool _stoppedBefore, _stoppedAfter, _mapOpen;

    public override Task Setup(ScenarioContext c)
    {
        _problem = _sfx.Install();
        return Task.CompletedTask;
    }

    public override async Task Act(ScenarioContext c)
    {
        if (_problem != null) return;
        Sfx.SoundEffects.Play(0);
        await c.Frames(2);
        _stoppedBefore = _sfx.StoppedAll;
        int world = UWClass._RES == UWClass.GAME_UW2 ? worlds.GetWorldNo(playerdat.dungeon_level) : 0;
        uimanager.DrawAutoMap(playerdat.dungeon_level - 1, world);
        _mapOpen = uimanager.CurrentGameMode == uimanager.GameModes.AUTOMAP;
        await c.Frames(2);
        _stoppedAfter = _sfx.StoppedAll;
        _sfx.Remove();
        c.Note($"stops [{string.Join(", ", _sfx.Stopped)}] (-1 is every effect); map open {_mapOpen}");
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        if (_stoppedBefore) return "precondition: every effect was stopped before the map opened";
        if (!_mapOpen) return "precondition: the map did not open";
        var k = new Checks();
        if (c.Game == "uw2")
            k.True(_stoppedAfter, "UW2 effects not stopped when the map opened (kill_all_effects: uw2 AUTOMAP.C:123)");
        else
            k.True(!_stoppedAfter, "UW1 effects stopped when the map opened, which its map screen does not do");
        return k.Result;
    }
}

/// <summary>
/// Escape in a cutscene stops its speech. UW2 cuts the speech off on Escape
/// (cutoff_cutscene_speech, uw2 CUTS.C:960-963); UW1 leaves the cutscene and stops the speech on
/// the way out (uw1 CUTS.C:648-651, 762, stop_speech). The scenario plays a cutscene that speaks on
/// its first frames (UW1 cutscene 0, the intro, speech 26.VOC on frame 1; UW2 cutscene 0x18, a
/// dream, BSP00.VOC on frame 0), waits for the speech, then calls cutsplayer.StopCutscene, which
/// is what the port's Escape key handler calls (src/ui/uimanager_mainmenu.cs).
/// </summary>
public sealed class CutsceneSpeechStops : Scenario
{
    public override string Name => "cutscene-speech-stops";
    public override string Cites => "underworld-exhumed uw1/src/gfx/CUTS.C:648-651, 762, uw2/src/gfx/CUTS.C:960-963";

    string _problem;
    bool _spoke, _ended, _stillSpeaking;

    public override Task Setup(ScenarioContext c) => Task.CompletedTask;

    public override async Task Act(ScenarioContext c)
    {
        var player = main.instance?.DigitalAudioPlayer;
        if (player == null) { _problem = "no DigitalAudioPlayer"; return; }
        cutsplayer.PlayCutscene(c.Game == "uw2" ? 0x18 : 0, null);
        for (int i = 0; i < 600 && !_spoke; i++)
        {
            await c.Frames(1);
            _spoke = player.Playing;
        }
        if (!_spoke) { _problem = "the cutscene's speech never started"; cutsplayer.StopCutscene(); return; }
        await c.Frames(30);
        cutsplayer.StopCutscene();
        for (int i = 0; i < 600 && cutsplayer.IsPlaying; i++)
            await c.Frames(1);
        _ended = !cutsplayer.IsPlaying;
        _stillSpeaking = player.Playing;
        c.Note($"cutscene ended {_ended}, speech still playing {_stillSpeaking}");
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        if (!_ended) return "precondition: the cutscene did not end after Escape";
        var k = new Checks();
        k.True(!_stillSpeaking, "speech still playing after Escape ended the cutscene (uw2 CUTS.C:963 cutoff_cutscene_speech, uw1 CUTS.C:762 stop_speech)");
        return k.Result;
    }
}

/// <summary>
/// The cutscene loop opcode. loop(n) on frame F sends the file back to its first frame each time
/// frame F is reached, n times, reading no script records meanwhile, then plays on (cutsop_loop
/// and the frame loop: uw2 CUTS.C:698-706, 1031, 1317-1322; uw1 CUTS.C:398-405, 667, 718-722).
/// Both games' death cutscene, 0x103 (CS403), has loop(10) on frame 5 of its second file, the
/// skull's repeat.
///
/// The scenario plays cutscene 0x103 and counts how often the second file's frame number goes
/// back while it plays. The count expected is the loop's own argument, read from the script.
/// </summary>
public sealed class CutsceneLoop : Scenario
{
    public override string Name => "cutscene-loop";
    public override string Cites => "underworld-exhumed uw2/src/gfx/CUTS.C:698-706, 1317-1322, uw1/src/gfx/CUTS.C:398-405, 718-722";

    string _problem;
    int _loopArg = -1, _restarts;
    bool _reachedSecondFile, _ended;

    public override Task Setup(ScenarioContext c) => Task.CompletedTask;

    public override async Task Act(ScenarioContext c)
    {
        var frameNo = typeof(cutsplayer).GetField("FrameNo", BindingFlags.NonPublic | BindingFlags.Static);
        var fileExt = typeof(cutsplayer).GetField("currentFileExt", BindingFlags.NonPublic | BindingFlags.Static);
        if (frameNo == null || fileExt == null) { _problem = "cutsplayer.FrameNo or currentFileExt not found"; return; }
        cutsplayer.PlayCutscene(0x103, null, true);
        for (int i = 0; i < 60 && !cutsplayer.IsPlaying; i++)
            await c.Frames(1);
        var loop = cutsplayer.commands?.FirstOrDefault(cmd => cmd.functionNo == 7);
        if (loop == null) { _problem = "CS403.N00 has no loop record"; cutsplayer.StopCutscene(); return; }
        _loopArg = loop.functionParams[0];
        // The cutscene paces its frames by the real clock (cutsplayer.WaitForNextFrame), so the
        // limit is real time: the cutscene takes about 15 seconds with the loop.
        var clock = System.Diagnostics.Stopwatch.StartNew();
        int lastExt = -1;
        while (clock.Elapsed.TotalSeconds < 90 && cutsplayer.IsPlaying)
        {
            await c.Frames(1);
            lastExt = (int)fileExt.GetValue(null);
            if (lastExt != 2) continue;
            _reachedSecondFile = true;
        }
        // Counted by the player itself: sampling the frame once per Godot frame can miss a restart
        // when frames are slow (software rendering on CI), the loop being only a few frames long.
        _restarts = cutsplayer.LoopRestarts;
        _ended = !cutsplayer.IsPlaying;
        if (!_ended) cutsplayer.StopCutscene();
        c.Note($"loop({_loopArg}) on frame {loop.frame}; the second file went back to its start {_restarts} times; {clock.Elapsed.TotalSeconds:F1} s, last file {lastExt}");
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        if (!_reachedSecondFile) return "precondition: the cutscene never reached its second file";
        if (!_ended) return "precondition: the cutscene did not finish";
        var k = new Checks();
        k.Equal("times the death skull file went back to its start", _loopArg, _restarts, "cutsop_loop: uw2 CUTS.C:1317-1322, uw1 CUTS.C:718-722");
        return k.Result;
    }
}
