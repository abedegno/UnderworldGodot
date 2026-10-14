using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Godot;

namespace Underworld.Scenarios;

/// <summary>
/// The music keeps going on the screens that wait for input outside the game loop. The original
/// calls loop_music_maybe while they wait: the main menu and the save list through
/// parse_start_input (uw1/src/ui/MAINMENU.C:320-323, uw2/src/ui/MAINMENU.C:296-297), character
/// creation (CHARGEN.C:466-468 in both), conversations (uw1/src/conv/CONVERSE.C:348,
/// uw2/src/conv/CONVERSE.C:335), the automap (uw1/src/ui/AUTOMAP.C:672, uw2/src/ui/AUTOMAP.C:818),
/// the options (uw1/src/ui/OPTIONS.C:103, uw2/src/ui/WRAPPER.C:109) and, in UW1, every cutscene
/// frame (uw1/src/gfx/CUTS.C:474-476). loop_music_maybe starts the current theme again with the
/// start flag once it has ended, except that some themes give way to another
/// (uw1/src/sound/SOUND.C:928-938, uw2/src/sound/SOUND.C:1556-1568):
/// - UW1: theme 1 is followed by theme 4; any other theme repeats;
/// - UW2: themes 1, 6 and 7 are followed by theme 10; any other theme repeats.
///
/// Audio is not heard headlessly, so the scenario tests the music state the port keeps. A theme
/// "ends" when the scenario stops the music player (MusicStreamPlayer.Stop, so IsPlaying is false,
/// as at the end of a tune); it then lets 10 frames pass and reads the current theme and whether
/// the player is playing again. It does this:
/// 1. at the main menu (CurrentGameMode MAIN) for several starting themes;
/// 2. in the conversation, automap and options modes (the mode is set directly; the screens
///    themselves are not opened), with a walking theme, which must repeat;
/// 3. UW1 only: on a cutscene frame, by calling the port's per-frame cutscene music step
///    (cutsplayer.RefreshCutsceneMusic) with theme 1 ended, which must give theme 4;
/// 4. on coming back to the main menu, through uimanager.StartMainMenuMusic (the music part of
///    ReturnToMainMenu, which also tears the level down): in UW2 the menu asks for theme 1 on every
///    pass and starts it again if it has ended (uw2 MAINMENU.C:147-148, SOUND.C:1581-1620), so an
///    ended theme 1 starts again and theme 10 gives way to theme 1; in UW1 the pass only calls
///    loop_music_maybe (uw1 MAINMENU.C:174), so an ended theme 1 gives way to theme 4;
/// 5. a theme that cannot be loaded (a theme number with no XMI file) turns the music off for the
///    session: the current theme stays as it was, and nothing is loaded afterwards, by the menu
///    loop or by LoadXMI (load_new_music's fail path, uw1 SOUND.C:351-354, uw2 SOUND.C:903-906).
/// 6. XMIMusic.RefreshMusic, which the sleep, conversation and automap code call directly, does
///    nothing when there is no music player (MusicStreamPlayer.Instance null) instead of throwing;
/// 7. a theme that is read but never plays (the player's XmiPlayer is taken away, as when no synth
///    could be made, so IsPlaying stays false) turns the music off after one load, at the menu and
///    in play, rather than being read again on every frame (XMIMusic.LoadObserver counts the loads);
/// 8. main.StartGame puts the music back on after it went off: music_ok is 1 when the program
///    starts and nothing sets it again (uw1 SOUND.C:139, uw2 SOUND.C:170), so a new game start in
///    the same process must not inherit the last one's failure.
/// The game is put back in GAME mode, and the music back on, afterwards.
///
/// The port once ran its music refresh only in game (playerdatloop.cs PlayerTimedLoop), so at
/// the menu the theme played once and then there was silence. Issue hankmorgan/UnderworldGodot#219.
/// </summary>
public sealed class MenuMusicRepeat : Scenario
{
    const int WaitFrames = 10;
    const byte MissingTheme = 0x3F;

    public override string Name => "menu-music-repeat";
    public override string Cites => "underworld-exhumed uw1/src/sound/SOUND.C:139, 351-354, 928-938, uw1/src/ui/MAINMENU.C:174, 320-323; uw2/src/sound/SOUND.C:170, 903-906, 1556-1620, uw2/src/ui/MAINMENU.C:147-148, 296-297";

    static readonly MethodInfo CutsceneMusicStep = typeof(cutsplayer).GetMethod("RefreshCutsceneMusic", BindingFlags.NonPublic | BindingFlags.Static);

    string _problem;
    readonly List<(string where, int expected, int after, bool playing, string cite)> _runs = new();
    int _failedBefore, _failedAfter, _failedAfterLoad;
    bool _failedMusicOk, _failedPlaying;
    readonly List<string> _noPlayerThrows = new();
    readonly List<(string mode, int loads, bool musicOk)> _silentRuns = new();
    bool _startMusicOk, _startPlaying;
    int _startTheme;

    static byte[] MenuThemes(string game) => game == "uw2" ? new byte[] { 1, 10, 6, 7, 9 } : new byte[] { 1, 4, 3, 0xA };

    static int LoopNext(string game, int start) => game == "uw2"
        ? (start == 1 || start == 6 || start == 7 ? 10 : start)   // uw2 SOUND.C:1562-1566
        : (start == 1 ? 4 : start);                              // uw1 SOUND.C:932-936

    static string LoopCite(string game) => game == "uw2" ? "uw2 SOUND.C:1556-1568" : "uw1 SOUND.C:928-938";

    public override Task Setup(ScenarioContext c)
    {
        if (MusicStreamPlayer.Instance == null) _problem = "no MusicStreamPlayer";
        else if (!playerdat.MusicEnabled) _problem = "music is off in the new game";
        else if (!XMIMusic.MusicOk) _problem = "the music was already off (XMIMusic.MusicOk false)";
        else if (CutsceneMusicStep == null) _problem = "cutsplayer.RefreshCutsceneMusic not found";
        return Task.CompletedTask;
    }

    /// <summary>Starts theme, then stops the player as if the tune had played to its end.</summary>
    async Task<bool> StartAndEnd(ScenarioContext c, byte theme)
    {
        var music = MusicStreamPlayer.Instance;
        XMIMusic.LoadXMI(theme, restartIfSame: true);
        await c.Frames(1);
        if (!music.IsPlaying || XMIMusic.CurrentlyPlayingThemeNo != theme)
        { _problem = $"theme {theme} did not start (current {XMIMusic.CurrentlyPlayingThemeNo}, playing {music.IsPlaying})"; return false; }
        music.Stop();
        if (music.IsPlaying) { _problem = $"stopping the music player left theme {theme} playing"; return false; }
        return true;
    }

    void Record(ScenarioContext c, string where, int expected, string cite)
    {
        var music = MusicStreamPlayer.Instance;
        _runs.Add((where, expected, XMIMusic.CurrentlyPlayingThemeNo, music.IsPlaying, cite));
        c.Note($"{where}: current theme {XMIMusic.CurrentlyPlayingThemeNo}, playing {music.IsPlaying}");
    }

    public override async Task Act(ScenarioContext c)
    {
        if (_problem != null) return;
        var music = MusicStreamPlayer.Instance;
        string g = c.Game;
        try
        {
            // 1. the main menu
            uimanager.CurrentGameMode = uimanager.GameModes.MAIN;
            foreach (byte theme in MenuThemes(g))
            {
                if (!await StartAndEnd(c, theme)) return;
                await c.Frames(WaitFrames);
                Record(c, $"at the menu, theme {theme} ended", LoopNext(g, theme), LoopCite(g) + (g == "uw2" ? ", MAINMENU.C:296-297" : ", MAINMENU.C:320-323"));
            }

            // 2. conversation, automap, options
            byte walking = g == "uw2" ? (byte)8 : (byte)2;
            foreach (var (mode, cite) in new[]
            {
                (uimanager.GameModes.CONVERSATION, g == "uw2" ? "uw2 CONVERSE.C:335" : "uw1 CONVERSE.C:348"),
                (uimanager.GameModes.AUTOMAP, g == "uw2" ? "uw2 AUTOMAP.C:818" : "uw1 AUTOMAP.C:672"),
                (uimanager.GameModes.OPTIONS, g == "uw2" ? "uw2 WRAPPER.C:109" : "uw1 OPTIONS.C:103"),
            })
            {
                uimanager.CurrentGameMode = mode;
                if (!await StartAndEnd(c, walking)) return;
                await c.Frames(WaitFrames);
                Record(c, $"in mode {mode}, theme {walking} ended", walking, LoopCite(g) + ", " + cite);
            }

            // 3. UW1 cutscene frame
            if (g == "uw1")
            {
                uimanager.CurrentGameMode = uimanager.GameModes.GAME;
                if (!await StartAndEnd(c, 1)) return;
                CutsceneMusicStep.Invoke(null, null);
                Record(c, "on a UW1 cutscene frame, theme 1 ended", 4, "uw1 CUTS.C:474-476, SOUND.C:928-938");
            }

            // 4. coming back to the main menu
            uimanager.CurrentGameMode = uimanager.GameModes.MAIN;
            if (!await StartAndEnd(c, 1)) return;
            uimanager.StartMainMenuMusic();
            await c.Frames(WaitFrames);
            Record(c, "back at the menu with theme 1 ended", g == "uw2" ? 1 : 4,
                g == "uw2" ? "uw2 MAINMENU.C:147-148, SOUND.C:1581-1620" : "uw1 MAINMENU.C:174, SOUND.C:928-938");
            if (g == "uw2")
            {
                XMIMusic.LoadXMI(10, restartIfSame: true);
                await c.Frames(1);
                uimanager.StartMainMenuMusic();
                await c.Frames(WaitFrames);
                Record(c, "back at the menu with theme 10 playing", 1, "uw2 MAINMENU.C:147-148, SOUND.C:1581-1620");
            }

            // 5. a theme that cannot be loaded
            string missing = Path.Combine(UWClass.BasePath, "SOUND", g == "uw2" ? "UWA77.XMI" : "UW77.XMI");
            if (File.Exists(missing)) { _problem = $"{missing} exists, so theme 0x3F is not a missing theme"; return; }
            if (!await StartAndEnd(c, 3)) return;
            _failedBefore = XMIMusic.CurrentlyPlayingThemeNo;
            XMIMusic.LoadXMI(MissingTheme);
            _failedMusicOk = XMIMusic.MusicOk;
            await c.Frames(WaitFrames);
            _failedAfter = XMIMusic.CurrentlyPlayingThemeNo;
            XMIMusic.LoadXMI(1);
            _failedAfterLoad = XMIMusic.CurrentlyPlayingThemeNo;
            _failedPlaying = music.IsPlaying;
            c.Note($"after theme 0x3F failed to load: music on {_failedMusicOk}, current theme {_failedBefore} -> {_failedAfter} after {WaitFrames} menu frames, {_failedAfterLoad} after LoadXMI(1), playing {_failedPlaying}");

            // 6. the music refresh with no music player
            XMIMusic.MusicOk = true;
            uimanager.CurrentGameMode = uimanager.GameModes.GAME;
            var instanceProp = typeof(MusicStreamPlayer).GetProperty(nameof(MusicStreamPlayer.Instance));
            byte walkingTheme = g == "uw2" ? (byte)8 : (byte)2;
            instanceProp.SetValue(null, null);
            try
            {
                foreach (var (what, current) in new[] { ("the fanfare", XMIMusic.Fanfare), ("an ended walking theme", walkingTheme) })
                {
                    XMIMusic.CurrentlyPlayingThemeNo = current;
                    XMIMusic.NewThemeToPlay = 0;
                    try { XMIMusic.RefreshMusic(); }
                    catch (System.Exception ex) { _noPlayerThrows.Add($"{what}: {ex.GetType().Name}"); }
                }
            }
            finally { instanceProp.SetValue(null, music); }
            c.Note($"RefreshMusic with no music player: {(_noPlayerThrows.Count == 0 ? "no exception" : string.Join(", ", _noPlayerThrows))}");

            // 7. a theme that loads but never plays (the player has no synth, as when none could
            //    be made, so IsPlaying stays false): the music goes off after one load, at the
            //    menu and in play, instead of the file being read again on every frame.
            var xmiField = typeof(MusicStreamPlayer).GetField("_xmiPlayer", BindingFlags.NonPublic | BindingFlags.Instance);
            var savedXmi = xmiField.GetValue(music);
            int loads = 0;
            music.Stop();
            xmiField.SetValue(music, null);
            XMIMusic.LoadObserver = _ => loads++;
            try
            {
                foreach (var (mode, theme) in new[] { (uimanager.GameModes.MAIN, (byte)3), (uimanager.GameModes.GAME, walkingTheme) })
                {
                    XMIMusic.MusicOk = true;
                    uimanager.CurrentGameMode = mode;
                    loads = 0;
                    XMIMusic.LoadXMI(theme, restartIfSame: true);
                    await c.Frames(WaitFrames);
                    _silentRuns.Add((mode.ToString(), loads, XMIMusic.MusicOk));
                    c.Note($"in mode {mode}, theme {theme} never playing: {loads} loads over {WaitFrames} frames, music on {XMIMusic.MusicOk}");
                }
            }
            finally
            {
                XMIMusic.LoadObserver = null;
                xmiField.SetValue(music, savedXmi);
            }

            // 8. starting the game again after the music went off: music_ok is set once, when the
            //    program starts (uw1 SOUND.C:139, uw2 SOUND.C:170), so the port's game start puts
            //    it back and the intro theme plays.
            XMIMusic.MusicOk = false;
            uimanager.CurrentGameMode = uimanager.GameModes.CUTSCENE;
            main.StartGame();
            _startMusicOk = XMIMusic.MusicOk;
            await c.Frames(1);
            _startTheme = XMIMusic.CurrentlyPlayingThemeNo;
            _startPlaying = music.IsPlaying;
            c.Note($"after main.StartGame with the music off: music on {_startMusicOk}, current theme {_startTheme}, playing {_startPlaying}");
            cutsplayer.StopCutscene();
            ulong deadline = Time.GetTicksMsec() + 10000;
            while ((cutsplayer.IsPlaying || uimanager.CurrentGameMode != uimanager.GameModes.MAIN) && Time.GetTicksMsec() < deadline)
                await c.Frames(1);
        }
        finally
        {
            XMIMusic.MusicOk = true;
            uimanager.CurrentGameMode = uimanager.GameModes.GAME;
        }
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        var k = new Checks();
        foreach (var r in _runs)
        {
            k.Equal($"theme {r.where}", r.expected, r.after, r.cite);
            k.True(r.playing, $"music {r.where} expected playing, got silent ({r.cite})");
        }
        string fail = c.Game == "uw2" ? "uw2 SOUND.C:903-906" : "uw1 SOUND.C:351-354";
        k.True(!_failedMusicOk, $"music after a theme failed to load expected off, got on ({fail})");
        k.Equal("current theme after a theme failed to load and 10 menu frames", _failedBefore, _failedAfter, fail);
        k.Equal("current theme after LoadXMI(1) with the music off", _failedBefore, _failedAfterLoad, fail);
        k.True(!_failedPlaying, $"music after a theme failed to load expected silent, got playing ({fail})");
        k.True(_noPlayerThrows.Count == 0, $"RefreshMusic with no music player threw ({string.Join(", ", _noPlayerThrows)})");
        k.Equal("silent-theme runs", 2, _silentRuns.Count, fail);
        foreach (var r in _silentRuns)
        {
            k.Equal($"loads in mode {r.mode} of a theme that never plays, over {WaitFrames} frames", 1, r.loads, fail);
            k.True(!r.musicOk, $"music in mode {r.mode} after a theme that never plays expected off, got on ({fail})");
        }
        string start = c.Game == "uw2" ? "uw2 SOUND.C:170" : "uw1 SOUND.C:139";
        k.True(_startMusicOk, $"music after the game starts again expected on, got off (music_ok starts at 1, {start})");
        k.Equal("current theme after the game starts again", XMIMusic.IntroTheme, _startTheme, start);
        k.True(_startPlaying, $"music after the game starts again expected playing, got silent ({start})");
        return k.Result;
    }
}
