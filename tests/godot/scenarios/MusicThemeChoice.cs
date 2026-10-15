using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Godot;

namespace Underworld.Scenarios;

/// <summary>
/// Which music theme the game asks for: when a theme ends, when a conversation starts, and around
/// a night's sleep, with the weapon drawn and sheathed.
///
/// When a theme ends with nothing asked for, change_music_maybe chooses the next. UW1: a walking
/// theme (rand() % 3 + 2) when no theme was asked for, or in play after a walking theme or one that
/// does not repeat; then theme 8 if the weapon is drawn, whatever was playing
/// (uw1/src/sound/SOUND.C:975-982). UW2: in play, a walking theme for the world
/// (walking_music[(PlayerLevel - 1) / 8][rand() % 3]) unless the theme was a combat theme or 0x18;
/// then theme 5 if the weapon is drawn and the theme was not a combat theme, else a walking or
/// combat theme repeats; load_new_music(0) means the current theme (uw2 SOUND.C:1550-1553,
/// 1607-1619, 873-874). Neither game ever asks for theme 0 (UW00.XMI / UWA00.XMI).
///
/// strt_converse asks for the conversation theme and starts it at once with change_music_maybe:
/// UW1 theme 13 (uw1 CONVERSE.C:183-184), UW2 the world's first walking theme,
/// set_random_walking_music(0), drawn weapon or not (uw2 CONVERSE.C:172-173).
///
/// player_sleep does the same before the night (uw1 SKILLS.C:538-539, uw2 SKILLS.C:452-453) and
/// asks for a random walking theme at its end (uw1 SKILLS.C:617, uw2 SKILLS.C:555).
///
/// The scenario drives the port's RefreshMusic (change_music_maybe) directly with the music
/// stopped, so the theme has "ended", and reads the random pick from a recording rand() source.
/// The bedroll sleep is as in sleep-passes-time; the theme playing while asleep is read at the
/// first rand() % 5 of the night (the hours, uw1 SKILLS.C:545, uw2 SKILLS.C:463).
/// </summary>
public sealed class MusicThemeChoice : Scenario
{
    const int BedrollItem = 0x121;            // ITEM_BEDROLL, uw1/uw2 include/items.h
    const ulong TalkMs = 30000;
    const ulong SettleMs = 30000;

    // walking_music, uw2/src/sound/SOUND.C:175-179
    static readonly byte[,] WalkingUW2 =
    {
        { 0x0A, 0x0C, 0x0E }, { 0x09, 0x0A, 0x0F }, { 0x0B, 0x0D, 0x0A },
        { 0x0C, 0x0D, 0x09 }, { 0x08, 0x0B, 0x0E }, { 0x0D, 0x08, 0x0F },
        { 0x0E, 0x09, 0x08 }, { 0x0F, 0x0B, 0x0A }, { 0x08, 0x0C, 0x09 }
    };

    public override string Name => "music-theme-choice";
    public override string Cites => "underworld-exhumed uw1/src/sound/SOUND.C:917-982, uw1/src/conv/CONVERSE.C:183-184, uw1/src/game/SKILLS.C:538-539, 617; uw2/src/sound/SOUND.C:864-874, 1532-1619, uw2/src/conv/CONVERSE.C:172-173, uw2/src/game/SKILLS.C:452-453, 555";

    /// <summary>A rand() source that records every Next(max) and its value.</summary>
    sealed class Recorder : IRandSource
    {
        readonly BorlandRandSource _inner;
        public readonly List<(int Max, int Value)> Draws = new();
        public System.Action<int> OnNext;
        public Recorder(uint seed) { _inner = new BorlandRandSource(seed); }
        public int Rand() { int v = _inner.Rand(); Draws.Add((0, v)); return v; }
        public int Next(int max) { OnNext?.Invoke(max); int v = _inner.Next(max); Draws.Add((max, v)); return v; }
    }

    readonly List<(string What, int Expected, int Actual, string Cite)> _results = new();
    string _problem;
    uwObject _npc;
    bool _uw1;
    int _asleepTheme = -1, _sleepRequest = -1, _sleepPick = -1, _sleepLastMax = -1, _settledTheme = -1;
    bool _bedroll, _usedOk, _settled;

    byte Walk(int pick) => _uw1 ? (byte)(2 + pick) : WalkingUW2[(playerdat.dungeon_level - 1) / 8, pick];
    byte Maps => 13;   // MUSIC_MAPS, uw1 include/sound.h:60
    byte Armed => (byte)(_uw1 ? 8 : 5);   // MUSIC_ARMED, uw1 sound.h:56, uw2 sound.h:49

    /// <summary>Theme cur playing, nothing asked for, the weapon drawn or not, no recent combat.</summary>
    void SetState(byte cur, int drawn)
    {
        playerdat.MusicEnabled = true;
        XMIMusic.MusicOk = true;
        if (!_uw1)
        {
            // so the world of the last walking pick is this one, as after any earlier pick here
            XMIMusic.PickLevelThemeMusic(-2);
        }
        XMIMusic.CurrentlyPlayingThemeNo = cur;
        XMIMusic.NewThemeToPlay = 0;
        XMIMusic.CombatMusicTimer = main.GlobalPITTimer;
        playerdat.play_drawn = drawn;
    }

    void Record(string what, int expected, int actual, string cite) => _results.Add((what, expected, actual, cite));

    public override Task Setup(ScenarioContext c)
    {
        _uw1 = c.Game == "uw1";
        if (MusicStreamPlayer.Instance == null) { _problem = "no music player"; return Task.CompletedTask; }
        if (!uimanager.InGame) { _problem = "not in play"; return Task.CompletedTask; }

        var map = UWTileMap.current_tilemap;
        var p = playerdat.playerObject;
        int bestD = int.MaxValue;
        for (int i = 2; i < 256; i++)
        {
            var o = map.LevelObjects[i];
            if (o == null || o.majorclass != 1 || !UWTileMap.ValidTile(o.tileX, o.tileY)) continue;
            if (!Guards.InTileChain(o.tileX, o.tileY, o.index)) continue;
            if (o.npc_whoami < 1 || o.npc_whoami > 0xFE || o.npc_attitude == 0 || o.npc_hp == 0) continue;
            if ((o.npc_goal == 5 || o.npc_goal == 6 || o.npc_goal == 9) && o.npc_gtarg == 1) continue;
            int dd = (o.tileX - p.tileX) * (o.tileX - p.tileX) + (o.tileY - p.tileY) * (o.tileY - p.tileY);
            if (dd < bestD) { bestD = dd; _npc = o; }
        }
        if (_npc == null) _problem = "no friendly NPC with a whoami of its own on this level";
        return Task.CompletedTask;
    }

    public override async Task Act(ScenarioContext c)
    {
        if (_problem != null) return;
        var saved = Rng.Source;
        try
        {
            ThemeEnds(c);
            if (_problem != null) return;
            Weapon(c);
            Options(c);
            Automap(c);
            if (_problem != null) return;
            if (!_uw1) CutsceneMusic(c);
            await ConversationStarts(c, drawn: 0);
            if (_problem != null) return;
            await ConversationStarts(c, drawn: 1);
            if (_problem != null) return;
            playerdat.play_drawn = 0;
            await Sleep(c);
        }
        finally
        {
            Rng.Source = saved;
        }
    }

    /// <summary>change_music_maybe once with the theme ended and nothing asked for.</summary>
    void ThemeEnds(ScenarioContext c)
    {
        string cite = _uw1 ? "uw1 SOUND.C:975-982" : "uw2 SOUND.C:1607-1619, 873-874";
        (string what, byte cur, int drawn, System.Func<int, int> expect)[] cases = _uw1
            ? new (string, byte, int, System.Func<int, int>)[]
            {
                ("after theme 13 ended, sheathed", 13, 0, v => Walk(v)),
                ("after combat theme 6 ended, drawn", 6, 1, v => 8),
                ("after walking theme 3 ended, drawn", 3, 1, v => 8),
                ("after walking theme 3 ended, sheathed", 3, 0, v => Walk(v)),
            }
            : new (string, byte, int, System.Func<int, int>)[]
            {
                ("after theme 6 ended, sheathed", 6, 0, v => Walk(v)),
                ("after combat theme 3 ended, drawn", 3, 1, v => 3),
                ("after walking theme 0xC ended, drawn", 0xC, 1, v => 5),
                ("after walking theme 0xC ended, sheathed", 0xC, 0, v => Walk(v)),
            };
        foreach (var k in cases)
        {
            SetState(k.cur, k.drawn);
            MusicStreamPlayer.Instance.Stop();
            if (MusicStreamPlayer.Instance.IsPlaying) { _problem = "the music did not stop"; return; }
            Rng.Source = new BorlandRandSource(0x1234);
            XMIMusic.RefreshMusic();
            // the walking pick is change_music_maybe's first rand(), so it is the seed's first value % 3
            int v = new BorlandRandSource(0x1234).Rand() % 3;
            Record("theme " + k.what, k.expect(v), XMIMusic.CurrentlyPlayingThemeNo, cite);
        }
        playerdat.play_drawn = 0;
    }

    // the walking pick of a request made with the seed 0x1234 just installed: its first rand() % 3
    int SeededPick => new BorlandRandSource(0x1234).Rand() % 3;

    /// <summary>
    /// Drawing the weapon asks for the armed theme only when no combat theme plays; putting it away
    /// asks for a random walking theme only when the armed theme plays (uw1 INTERACT.C:800-801,
    /// 807-808; uw2 INTERACT.C:682-683, 688-689). The request is read before any music step.
    /// </summary>
    void Weapon(ScenarioContext c)
    {
        byte combat = (byte)(_uw1 ? 6 : 3);
        string cite = _uw1 ? "uw1 INTERACT.C:800-801, 807-808" : "uw2 INTERACT.C:682-683, 688-689";
        (string what, byte cur, bool draw, int expected)[] cases =
        {
            ("drawing the weapon during combat theme " + combat, combat, true, 0),
            ("drawing the weapon during walking theme " + Walk(1), Walk(1), true, Armed),
            ("sheathing the weapon during the armed theme", Armed, false, Walk(SeededPick)),
            ("sheathing the weapon during combat theme " + combat, combat, false, 0),
        };
        foreach (var k in cases)
        {
            SetState(k.cur, k.draw ? 0 : 1);
            Rng.Source = new BorlandRandSource(0x1234);
            uimanager.ToggleWeaponAnimationState(k.draw);
            Record("request on " + k.what, k.expected, XMIMusic.NewThemeToPlay, cite);
        }
        uimanager.ToggleWeaponAnimationState(false, updateThemes: false);
        playerdat.play_drawn = 0;
    }

    /// <summary>
    /// Presses an options button. Where the panel has PressOptionButton (UW2 buttons act on the mouse's release,
    /// as better_mouse_release), that is called directly; otherwise the click goes through the panel's handler.
    /// </summary>
    static void PressOption(MethodInfo handler, InputEventMouseButton click, long button)
    {
        var press = typeof(uimanager).GetMethod("PressOptionButton", BindingFlags.NonPublic | BindingFlags.Instance);
        if (press != null) press.Invoke(uimanager.instance, new object[] { button });
        else handler.Invoke(uimanager.instance, new object[] { click, button });
    }

    /// <summary>
    /// The options panel's music on and off, through the panel's own handler: turn_music restarts
    /// the current theme, or with none loaded picks a random walking theme (uw1 SOUND.C:458-473,
    /// uw2 SOUND.C:1010-1026).
    /// </summary>
    void Options(ScenarioContext c)
    {
        string cite = _uw1 ? "turn_music: uw1 SOUND.C:458-473" : "turn_music: uw2 SOUND.C:1010-1026";
        var handler = typeof(uimanager).GetMethod("_on_game_options_input", BindingFlags.NonPublic | BindingFlags.Instance);
        if (handler == null) { _problem = "no options handler"; return; }
        var click = new InputEventMouseButton { Pressed = true, ButtonIndex = MouseButton.Left };
        var savedMenu = uimanager.CurrentGameOptionMenu;
        try
        {
            uimanager.CurrentGameOptionMenu = uimanager.OptionMenus.MusicMenu;
            SetState(Walk(1), 0);
            PressOption(handler, click, 3L);   // off
            Record("music on after turning it off (1 = on)", 0, playerdat.MusicEnabled ? 1 : 0, cite);
            Rng.Source = new BorlandRandSource(0x1234);
            PressOption(handler, click, 2L);   // on
            Record("theme after turning the music back on", Walk(1), XMIMusic.CurrentlyPlayingThemeNo, cite);
            Record("request after turning the music back on", 0, XMIMusic.NewThemeToPlay, cite);

            SetState(0, 0);
            playerdat.MusicEnabled = false;
            Rng.Source = new BorlandRandSource(0x1234);
            PressOption(handler, click, 2L);   // on, nothing loaded
            Record("theme after turning the music on with none loaded", Walk(SeededPick), XMIMusic.CurrentlyPlayingThemeNo, cite);
        }
        finally
        {
            uimanager.CurrentGameOptionMenu = savedMenu;
            playerdat.MusicEnabled = true;
        }
    }

    /// <summary>
    /// The map: opening it starts UW1's theme 13 (uw1 AUTOMAP.C:113-114) and, in UW2, the world's
    /// first walking theme unless a walking theme plays (uw2 AUTOMAP.C:101-104). Closing it asks
    /// for a walking theme in UW1, drawn weapon or not (uw1 AUTOMAP.C:173), and in UW2 starts the
    /// armed theme at once when the weapon is drawn (uw2 AUTOMAP.C:232-235). The map is opened with
    /// the map scroll's use and closed with the map's own click handler.
    /// </summary>
    void Automap(ScenarioContext c)
    {
        var close = typeof(uimanager).GetMethod("CloseAutomap", BindingFlags.NonPublic | BindingFlags.Instance);
        if (close == null) { _problem = "no automap close handler"; return; }
        var click = new InputEventMouseButton { Pressed = true, ButtonIndex = MouseButton.Left };
        (byte cur, int expected, string cite)[] opens = _uw1
            ? new[] { (Walk(1), (int)Maps, "uw1 AUTOMAP.C:113-114") }
            : new[] { (Armed, (int)Walk(0), "uw2 AUTOMAP.C:101-104"), (Walk(1), (int)Walk(1), "uw2 AUTOMAP.C:101-104") };
        foreach (var o in opens)
        {
            SetState(o.cur, o.cur == Armed ? 1 : 0);
            map.Use(playerdat.playerObject, WorldObject: false);
            if (uimanager.CurrentGameMode != uimanager.GameModes.AUTOMAP) { _problem = $"the map did not open ({uimanager.CurrentGameMode})"; return; }
            Record($"theme on opening the map during theme {o.cur}", o.expected, XMIMusic.CurrentlyPlayingThemeNo, o.cite);

            SetState(XMIMusic.CurrentlyPlayingThemeNo, 1);
            Rng.Source = new BorlandRandSource(0x1234);
            close.Invoke(uimanager.instance, new object[] { click });
            if (uimanager.CurrentGameMode != uimanager.GameModes.GAME) { _problem = $"the map did not close ({uimanager.CurrentGameMode})"; return; }
            if (_uw1)
                Record("request on closing the map, drawn", Walk(SeededPick), XMIMusic.NewThemeToPlay, "uw1 AUTOMAP.C:173");
            else
                Record("theme on closing the map, drawn", Armed, XMIMusic.CurrentlyPlayingThemeNo, "uw2 AUTOMAP.C:232-235");
        }
        playerdat.play_drawn = 0;
    }

    /// <summary>
    /// UW2's cutscene music opcode asks for its theme with set_new_music, started by the next
    /// music step (change_music_maybe on each frame); 0 stops the music (uw2 CUTS.C:564-570, 891-893).
    /// </summary>
    void CutsceneMusic(ScenarioContext c)
    {
        var step = typeof(cutsplayer).GetMethod("RefreshCutsceneMusic", BindingFlags.NonPublic | BindingFlags.Static);
        var savedMode = uimanager.CurrentGameMode;
        var cancel = typeof(cutsplayer).GetField("cancelRequested", BindingFlags.NonPublic | BindingFlags.Static);
        var savedCancel = cancel?.GetValue(null);
        try
        {
            cancel?.SetValue(null, false);   // the runner stopped the intro, which leaves it set
            uimanager.CurrentGameMode = uimanager.GameModes.CUTSCENE;
            SetState(0x18, 0);
            var cmd = new CutSceneCommand { functionNo = 25, functionParams = new List<int> { 0x0C, 0, 0, 0 } };
            cutsplayer.ExecuteCommand(cmd, null, 0).MoveNext();
            Record("request after cutscene music opcode 0xC", 0x0C, XMIMusic.NewThemeToPlay, "set_new_music: uw2 CUTS.C:568");
            Record("theme after cutscene music opcode 0xC, before the music step", 0x18, XMIMusic.CurrentlyPlayingThemeNo, "set_new_music: uw2 CUTS.C:568");
            step?.Invoke(null, null);
            Record("theme after the cutscene's music step", 0x0C, XMIMusic.CurrentlyPlayingThemeNo, "change_music_maybe: uw2 CUTS.C:891-893");

            cmd = new CutSceneCommand { functionNo = 25, functionParams = new List<int> { 0, 0, 0, 0 } };
            cutsplayer.ExecuteCommand(cmd, null, 0).MoveNext();
            Record("theme after cutscene music opcode 0", 0x0C, XMIMusic.CurrentlyPlayingThemeNo, "stop_music: uw2 CUTS.C:567");
        }
        finally
        {
            uimanager.CurrentGameMode = savedMode;
            if (savedCancel != null) cancel.SetValue(null, savedCancel);
        }
    }

    /// <summary>Talks to the NPC and reads the theme at once, then talks the conversation to its end.</summary>
    async Task ConversationStarts(ScenarioContext c, int drawn)
    {
        SetState(drawn == 1 ? Armed : Walk(1), drawn);
        Rng.Source = new BorlandRandSource(0x1234);
        talk.Talk(_npc, WorldObject: true);
        bool started = uimanager.CurrentGameMode == uimanager.GameModes.CONVERSATION;
        if (!started) { _problem = $"talking to NPC {_npc.index} did not start a conversation ({uimanager.CurrentGameMode})"; return; }
        Record($"theme on starting a conversation, {(drawn == 1 ? "drawn" : "sheathed")}",
            _uw1 ? Maps : Walk(0), XMIMusic.CurrentlyPlayingThemeNo,
            _uw1 ? "set_new_music(13); change_music_maybe(): uw1 CONVERSE.C:183-184"
                 : "set_random_walking_music(0); change_music_maybe(): uw2 CONVERSE.C:172-173");

        ulong deadline = Time.GetTicksMsec() + TalkMs;
        int menus = 0;
        while (Time.GetTicksMsec() < deadline && uimanager.CurrentGameMode == uimanager.GameModes.CONVERSATION && menus <= 60)
        {
            if (MessageDisplay.WaitingForMore) MessageDisplay.WaitingForMore = false;
            if (MessageDisplay.WaitingForTypedInput) MessageDisplay.WaitingForTypedInput = false;
            if (ConversationVM.WaitingForInput)
            {
                ConversationVM.PlayerNumericAnswer = System.Math.Max(1, ConversationVM.MaxAnswer);
                ConversationVM.WaitingForInput = false;
                menus++;
            }
            await c.Frames(1);
        }
        for (int f = 0; f < 600 && uimanager.CurrentGameMode != uimanager.GameModes.GAME; f++)
        {
            if (MessageDisplay.WaitingForMore) MessageDisplay.WaitingForMore = false;
            await c.Frames(1);
        }
        if (uimanager.CurrentGameMode != uimanager.GameModes.GAME) _problem = $"the conversation did not end ({uimanager.CurrentGameMode})";
        await c.Frames(2);
    }

    async Task Sleep(ScenarioContext c)
    {
        int slot = ObjectCreator.PrepareNewObject(BedrollItem);
        var bedroll = slot != 0 ? UWTileMap.current_tilemap.LevelObjects[slot] : null;
        _bedroll = bedroll != null && bedroll.item_id == BedrollItem;
        if (!_bedroll) return;
        playerdat.play_fatigue = 0x30;
        SetState(Walk(1), 0);
        var rec = new Recorder(0x1234);
        rec.OnNext = max => { if (max == 5 && _asleepTheme < 0) _asleepTheme = XMIMusic.CurrentlyPlayingThemeNo; };
        Rng.Source = rec;
        _usedOk = use.Use(bedroll, playerdat.playerObject, UWTileMap.current_tilemap.LevelObjects, WorldObject: true);
        _sleepRequest = XMIMusic.NewThemeToPlay;
        if (rec.Draws.Count > 0)
        {
            var last = rec.Draws[rec.Draws.Count - 1];
            _sleepLastMax = last.Max;
            _sleepPick = last.Value;
        }
        ulong deadline = Time.GetTicksMsec() + SettleMs;
        int stable = 0;
        while (Time.GetTicksMsec() < deadline)
        {
            await c.Frames(1);
            stable = (uimanager.InGame && !cutsplayer.IsPlaying && !uimanager.blockinput) ? stable + 1 : 0;
            if (stable >= 30) { _settled = true; break; }
        }
        _settledTheme = XMIMusic.CurrentlyPlayingThemeNo;
        c.Note($"sleep: theme asleep {_asleepTheme}, request at the end {_sleepRequest} (last draw rand()%{_sleepLastMax} = {_sleepPick}), theme once back in play {_settledTheme}");
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        if (!_bedroll) return "precondition: no bedroll could be made";
        if (!_usedOk) return "precondition: using the bedroll did nothing";
        if (!_settled) return "precondition: the game did not come back to play after the sleep";
        var k = new Checks();
        foreach (var r in _results)
            k.Equal(r.What, r.Expected, r.Actual, r.Cite);
        k.Equal("theme while asleep", _uw1 ? Maps : Walk(0), _asleepTheme,
            _uw1 ? "set_new_music(13); change_music_maybe(): uw1 SKILLS.C:538-539"
                 : "set_random_walking_music(0); change_music_maybe(): uw2 SKILLS.C:452-453");
        k.Equal("theme asked for at the end of the sleep", _sleepLastMax == 3 ? Walk(_sleepPick) : -1, _sleepRequest,
            _uw1 ? "a random walking theme: uw1 SKILLS.C:617, SOUND.C:923-926"
                 : "set_random_walking_music(-1): uw2 SKILLS.C:555, SOUND.C:1541-1553");
        k.True(_settledTheme != 0, "theme 0 playing once back in play after the sleep; the original never asks for it (uw1 SOUND.C:977-978, uw2 SOUND.C:873-874)");
        return k.Result;
    }
}
