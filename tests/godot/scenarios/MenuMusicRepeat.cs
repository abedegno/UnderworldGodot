using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace Underworld.Scenarios;

/// <summary>
/// The main menu keeps its music going. While the original's menu waits for input,
/// parse_start_input calls loop_music_maybe on every pass (uw1/src/ui/MAINMENU.C:320-323,
/// uw2/src/ui/MAINMENU.C:296-297), as do the save list (do_journey, through parse_start_input)
/// and character creation (uw1 and uw2 src/game/CHARGEN.C:466-468). loop_music_maybe starts the
/// current theme again with the start flag once it has ended (music_over), except that some
/// themes give way to another (uw1/src/sound/SOUND.C:928-938, uw2/src/sound/SOUND.C:1556-1568):
/// - UW1: theme 1 is followed by theme 4; any other theme repeats;
/// - UW2: themes 1, 6 and 7 are followed by theme 10; any other theme repeats.
///
/// Audio is not heard headlessly, so the scenario tests the music state the port keeps. For each
/// starting theme it puts the game at the main menu (uimanager.CurrentGameMode MAIN), starts the
/// theme with XMIMusic.LoadXMI, stops the music player (MusicStreamPlayer.Stop, so IsPlaying is
/// false, as when a tune has played to its end), lets 10 frames pass, and reads which theme is
/// current and whether the player is playing again. It puts the game back in GAME mode afterwards.
///
/// The port once ran its music refresh only in game (playerdatloop.cs PlayerTimedLoop), so at
/// the menu the theme played once and then there was silence. Issue hankmorgan/UnderworldGodot#219.
/// </summary>
public sealed class MenuMusicRepeat : Scenario
{
    const int WaitFrames = 10;

    public override string Name => "menu-music-repeat";
    public override string Cites => "underworld-exhumed uw1/src/sound/SOUND.C:928-938, uw1/src/ui/MAINMENU.C:320-323; uw2/src/sound/SOUND.C:1556-1568, uw2/src/ui/MAINMENU.C:296-297";

    string _problem;
    readonly List<(byte start, bool stopped, int after, bool playing)> _runs = new();

    static byte[] StartThemes(string game) => game == "uw2" ? new byte[] { 1, 10, 6, 7, 9 } : new byte[] { 1, 4, 3, 0xA };

    static int Expected(string game, byte start) => game == "uw2"
        ? (start == 1 || start == 6 || start == 7 ? 10 : start)   // uw2 SOUND.C:1562-1566
        : (start == 1 ? 4 : start);                              // uw1 SOUND.C:932-936

    public override Task Setup(ScenarioContext c)
    {
        if (MusicStreamPlayer.Instance == null) _problem = "no MusicStreamPlayer";
        else if (!playerdat.MusicEnabled) _problem = "music is off in the new game";
        return Task.CompletedTask;
    }

    public override async Task Act(ScenarioContext c)
    {
        if (_problem != null) return;
        var music = MusicStreamPlayer.Instance;
        try
        {
            uimanager.CurrentGameMode = uimanager.GameModes.MAIN;
            foreach (byte theme in StartThemes(c.Game))
            {
                XMIMusic.LoadXMI(theme, restartIfSame: true);
                await c.Frames(1);
                if (!music.IsPlaying || XMIMusic.CurrentlyPlayingThemeNo != theme)
                { _problem = $"theme {theme} did not start (current {XMIMusic.CurrentlyPlayingThemeNo}, playing {music.IsPlaying})"; return; }
                music.Stop();
                bool stopped = !music.IsPlaying;
                await c.Frames(WaitFrames);
                _runs.Add((theme, stopped, XMIMusic.CurrentlyPlayingThemeNo, music.IsPlaying));
                c.Note($"at the menu, theme {theme} ended: current theme {XMIMusic.CurrentlyPlayingThemeNo}, playing {music.IsPlaying}");
            }
        }
        finally
        {
            uimanager.CurrentGameMode = uimanager.GameModes.GAME;
        }
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        var k = new Checks();
        string cite = c.Game == "uw2" ? "uw2 SOUND.C:1556-1568, MAINMENU.C:296-297" : "uw1 SOUND.C:928-938, MAINMENU.C:320-323";
        foreach (var r in _runs)
        {
            if (!r.stopped) return $"precondition: stopping the music player left it playing (theme {r.start})";
            k.Equal($"theme at the menu after theme {r.start} ended", Expected(c.Game, r.start), r.after, cite);
            k.True(r.playing, $"music at the menu after theme {r.start} ended expected playing, got silent ({cite})");
        }
        return k.Result;
    }
}
