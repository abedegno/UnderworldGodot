using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace Underworld.Scenarios;

/// <summary>
/// Helpers for the menu scenarios: keys pressed through Godot's input, as the keyboard does, and
/// saved games made of a DESC file alone, which is all the menu reads (get_save_descs, uw1
/// GAMEWRAP.C:151-170).
/// </summary>
static class MenuKeys
{
    /// <summary>Presses and lets go of key, with the modifiers given, and lets the game see it.</summary>
    public static async Task Tap(ScenarioContext c, Key key, bool ctrl = false, bool alt = false, bool shift = false)
    {
        foreach (bool pressed in new[] { true, false })
        {
            Input.ParseInputEvent(new InputEventKey
            {
                Keycode = key, PhysicalKeycode = key, Pressed = pressed,
                CtrlPressed = ctrl, AltPressed = alt, ShiftPressed = shift,
            });
            await c.Frames(2);
        }
    }

    static string DescPath(int slot) => Path.Combine(SlotTransaction.SlotDirectory(UWClass.BasePath, slot), "DESC");

    /// <summary>Makes slot hold a saved game called name, as far as the menu can tell.</summary>
    public static void MakeSave(int slot, string name)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DescPath(slot)));
        File.WriteAllBytes(DescPath(slot), System.Text.Encoding.ASCII.GetBytes(name));
    }

    /// <summary>
    /// Moves every slot's DESC aside, so the menu sees no saved game. A scenario that saved
    /// before this one may have left one in the temporary game folder.
    /// </summary>
    public static void SetSavesAside()
    {
        for (int slot = 1; slot <= 4; slot++)
        {
            if (File.Exists(DescPath(slot))) File.Move(DescPath(slot), DescPath(slot) + ".aside", true);
        }
    }

    /// <summary>Removes every DESC the scenario made and puts back the ones set aside.</summary>
    public static void RestoreSaves()
    {
        for (int slot = 1; slot <= 4; slot++)
        {
            if (File.Exists(DescPath(slot))) File.Delete(DescPath(slot));
            if (File.Exists(DescPath(slot) + ".aside")) File.Move(DescPath(slot) + ".aside", DescPath(slot));
        }
    }

    /// <summary>The cutscene playing, or the last one played (cutsplayer's own record).</summary>
    public static int Cutscene => cutsplayer.CurrentCutsceneNo;

    /// <summary>Stops a cutscene in progress and waits for the menu to come back.</summary>
    public static async Task StopCutsceneToMenu(ScenarioContext c)
    {
        ulong deadline = Time.GetTicksMsec() + 20000;
        while (Time.GetTicksMsec() < deadline && (cutsplayer.IsPlaying || uimanager.CurrentGameMode != uimanager.GameModes.MAIN))
        {
            if (cutsplayer.IsPlaying) cutsplayer.StopCutscene();
            await c.Frames(1);
        }
    }
}

/// <summary>
/// With no saved game the introduction plays by itself after the title: the original enters the
/// menu with real_start(1) after the title (uw1 UWEDIT.C:141, 230; uw2 UWEDIT.C:139, 233), and
/// its do_intro_scene runs cutscene 0 (UW2: 0 and then 1) when get_save_descs finds none (uw1
/// MAINMENU.C:94-107, uw2 MAINMENU.C:65-80). With a saved game it goes straight to the menu.
///
/// The start-up the runner stopped (ScenarioRunner.StartupCutscenes) must be the title and, when
/// the temporary game folder held no saved game, the introduction after it; it is empty at first,
/// but a scenario that saves leaves its slot for the later ones. UW2's introduction is cutscene 0
/// then 1, but stopping 0 returns to the menu without 1 (cutsplayer.cs, Escape on UW2's cutscene
/// 0), so 9 then 0 is expected in both. Then both branches are run through the title's callback
/// (uimanager.TitleFinished): with every slot's DESC set aside it plays cutscene 0, and with a
/// saved game it shows the menu and plays nothing.
/// </summary>
public sealed class StartupIntro : Scenario
{
    public override string Name => "startup-intro";
    public override string Cites => "underworld-exhumed uw1/src/ui/MAINMENU.C:94-107, uw1/src/game/UWEDIT.C:141; uw2/src/ui/MAINMENU.C:65-80, uw2/src/game/UWEDIT.C:139";

    int[] _startup;
    bool _startupHadSaves;
    bool _noSavePlaying, _withSavePlaying;
    int _noSaveCutscene = -1;
    uimanager.GameModes _withSaveMode;

    public override Task Setup(ScenarioContext c)
    {
        _startup = ScenarioRunner.StartupCutscenes.ToArray();
        _startupHadSaves = ScenarioRunner.StartupHadSaves;
        c.Note($"cutscenes at start-up: {string.Join(", ", _startup)} (saved game there: {_startupHadSaves})");
        return Task.CompletedTask;
    }

    public override async Task Act(ScenarioContext c)
    {
        try
        {
            MenuKeys.SetSavesAside();
            uimanager.CurrentGameMode = uimanager.GameModes.CUTSCENE;
            uimanager.TitleFinished();
            await c.Frames(5);
            _noSavePlaying = cutsplayer.IsPlaying;
            _noSaveCutscene = MenuKeys.Cutscene;
            await MenuKeys.StopCutsceneToMenu(c);

            MenuKeys.MakeSave(1, "a saved game");
            uimanager.CurrentGameMode = uimanager.GameModes.CUTSCENE;
            uimanager.TitleFinished();
            await c.Frames(5);
            _withSavePlaying = cutsplayer.IsPlaying;
            _withSaveMode = uimanager.CurrentGameMode;
            await MenuKeys.StopCutsceneToMenu(c);
        }
        finally
        {
            MenuKeys.RestoreSaves();
        }
    }

    public override string Assert(ScenarioContext c)
    {
        string cite = c.Game == "uw1" ? "uw1 MAINMENU.C:102-106" : "uw2 MAINMENU.C:73-78";
        var k = new Checks();
        var expected = _startupHadSaves ? new[] { 9 } : new[] { 9, 0 };
        k.True(_startup.SequenceEqual(expected),
            $"cutscenes at start-up {(_startupHadSaves ? "with" : "with no")} saved game expected {string.Join(", ", expected)} (do_intro_scene, {cite}), got {string.Join(", ", _startup)}");
        k.True(_noSavePlaying && _noSaveCutscene == 0, $"after the title with no saved game expected the introduction (cutscene 0) playing, got playing {_noSavePlaying}, cutscene {_noSaveCutscene} ({cite})");
        k.True(!_withSavePlaying, $"after the title with a saved game a cutscene played (expected the menu at once, {cite})");
        k.True(_withSaveMode == uimanager.GameModes.MAIN, $"after the title with a saved game expected the main menu, got {_withSaveMode}");
        return k.Result;
    }
}

/// <summary>
/// The main menu and its list of saved games work from the keyboard, as parse_start_input takes
/// keys (uw1 MAINMENU.C:309-370, uw2 MAINMENU.C:285-344), for the menu real_start sets up (uw1
/// MAINMENU.C:167-168, uw2 MAINMENU.C:140-141): with no saved game, three buttons with 'create
/// character' lit; with one, four with 'journey onward' lit; keys stop at the ends.
///
/// The menu is reached the way the game returns to it after a death (uimanager.ReturnToMainMenu).
/// With no saved game: Enter on the lit button opens character creation (button 1); Escape
/// there is the port's own way back. Up then Enter plays the introduction (cutscene 0). Ctrl+N
/// twice, Down and End then Enter play the acknowledgements (cutscene 0xA, button 2: the last of
/// three). With saved games in slots 2 and 4: Enter opens the list (button 3 lit), which starts
/// on the first; Down lights the second (slot 4), Down again stays there; Alt+X goes back to the
/// menu (do_journey returns 0, uw1 MAINMENU.C:401, 420).
/// </summary>
public sealed class MainMenuKeys : Scenario
{
    public override string Name => "main-menu-keys";
    public override string Cites => "underworld-exhumed uw1/src/ui/MAINMENU.C:167-168, 309-370, 374-421; uw2/src/ui/MAINMENU.C:140-141, 285-344, 348-395";

    uimanager.GameModes _afterEnter, _afterIntroEnter, _afterAckEnter, _afterSavesEnter, _afterAltX;
    int _introCutscene = -1, _ackCutscene = -1;
    bool _introPlaying, _ackPlaying;
    Color _slot2Colour, _slot4Colour, _slot4ColourAfterSecondDown;
    bool _inMenu;

    public override async Task Setup(ScenarioContext c)
    {
        MenuKeys.SetSavesAside();
        uimanager.ReturnToMainMenu();
        await c.Frames(3);
        _inMenu = uimanager.CurrentGameMode == uimanager.GameModes.MAIN;
    }

    public override async Task Act(ScenarioContext c)
    {
        if (!_inMenu) return;
        try
        {
            await MenuKeys.Tap(c, Key.Enter);
            _afterEnter = uimanager.CurrentGameMode;
            await MenuKeys.Tap(c, Key.Escape);
            await c.Frames(2);

            await MenuKeys.Tap(c, Key.Up);
            await MenuKeys.Tap(c, Key.Enter);
            _afterIntroEnter = uimanager.CurrentGameMode;
            _introPlaying = cutsplayer.IsPlaying;
            _introCutscene = MenuKeys.Cutscene;
            await MenuKeys.StopCutsceneToMenu(c);

            await MenuKeys.Tap(c, Key.N, ctrl: true);
            await MenuKeys.Tap(c, Key.N, ctrl: true);
            await MenuKeys.Tap(c, Key.Down);
            await MenuKeys.Tap(c, Key.End);
            await MenuKeys.Tap(c, Key.Enter);
            _afterAckEnter = uimanager.CurrentGameMode;
            _ackPlaying = cutsplayer.IsPlaying;
            _ackCutscene = MenuKeys.Cutscene;
            await MenuKeys.StopCutsceneToMenu(c);

            MenuKeys.MakeSave(2, "slot two");
            MenuKeys.MakeSave(4, "slot four");
            uimanager.ReturnToMainMenu();
            await c.Frames(3);
            await MenuKeys.Tap(c, Key.Enter);
            _afterSavesEnter = uimanager.CurrentGameMode;
            await MenuKeys.Tap(c, Key.Down);
            _slot2Colour = uimanager.instance.SaveGamesNames[1].Get("theme_override_colors/font_color").AsColor();
            _slot4Colour = uimanager.instance.SaveGamesNames[3].Get("theme_override_colors/font_color").AsColor();
            await MenuKeys.Tap(c, Key.Down);
            _slot4ColourAfterSecondDown = uimanager.instance.SaveGamesNames[3].Get("theme_override_colors/font_color").AsColor();
            await MenuKeys.Tap(c, Key.X, alt: true);
            _afterAltX = uimanager.CurrentGameMode;
        }
        finally
        {
            MenuKeys.RestoreSaves();
            await MenuKeys.StopCutsceneToMenu(c);
        }
    }

    public override string Assert(ScenarioContext c)
    {
        if (!_inMenu) return "precondition: ReturnToMainMenu did not reach the main menu";
        bool uw1 = c.Game == "uw1";
        string keys = uw1 ? "uw1 MAINMENU.C:324-367" : "uw2 MAINMENU.C:298-341";
        var palette = PaletteLoader.Palettes[2];
        var selected = palette.ColorAtIndex((byte)(uw1 ? 0xA2 : 7), false, false);
        var unselected = palette.ColorAtIndex((byte)(uw1 ? 0xAA : 0xCD), false, false);
        var k = new Checks();
        k.True(_afterEnter == uimanager.GameModes.CHARGEN, $"Enter on a fresh menu with no saved game expected create character (CHARGEN), got {_afterEnter} ({(uw1 ? "uw1 MAINMENU.C:168" : "uw2 MAINMENU.C:141")}, {keys})");
        k.True(_afterIntroEnter == uimanager.GameModes.CUTSCENE && _introPlaying && _introCutscene == 0,
            $"Up then Enter expected the introduction (cutscene 0 playing), got mode {_afterIntroEnter}, playing {_introPlaying}, cutscene {_introCutscene} ({keys})");
        k.True(_afterAckEnter == uimanager.GameModes.CUTSCENE && _ackPlaying && _ackCutscene == 0xA,
            $"Ctrl+N, Ctrl+N, Down, End then Enter expected the acknowledgements (cutscene 10 playing), got mode {_afterAckEnter}, playing {_ackPlaying}, cutscene {_ackCutscene} ({keys})");
        k.True(_afterSavesEnter == uimanager.GameModes.JOURNEY, $"Enter with saved games expected the list of saved games (journey onward lit), got {_afterSavesEnter} ({(uw1 ? "uw1 MAINMENU.C:168" : "uw2 MAINMENU.C:141")})");
        k.True(_slot2Colour == unselected && _slot4Colour == selected,
            $"Down on the list expected the second saved game (slot 4) lit, got slot 2 {_slot2Colour}, slot 4 {_slot4Colour} ({(uw1 ? "uw1 MAINMENU.C:125-141" : "uw2 MAINMENU.C:98-113")})");
        k.True(_slot4ColourAfterSecondDown == selected, $"Down past the last saved game expected it to stay lit ({keys})");
        k.True(_afterAltX == uimanager.GameModes.MAIN, $"Alt+X on the list expected the menu back, got {_afterAltX} ({(uw1 ? "uw1 MAINMENU.C:352-354, 401" : "uw2 MAINMENU.C:326-328, 375")})");
        return k.Result;
    }
}

/// <summary>
/// The options panel works from the keyboard. UW1 (run_options_panel, uw1 OPTIONS.C:114-128;
/// options_handle_key, :421-438): Down and Space move the highlight down onto the next button, Up
/// up, never past the ends; Enter presses the lit button; Escape closes. UW2
/// (busywaiting_new_options, uw2 WRAPPER.C:111-147; move_hilite, :160-182): the same keys plus
/// Left and Right, and the highlight goes round the seven buttons; F6 presses 'return to game'.
/// The top page opens with 'save' lit (uw1 OPTIONS.C:175, uw2 WRAPPER.C:322).
///
/// Opened with F1 (the port's key for the options icon). Down, Space, Enter: the music page
/// (third button). Escape: back in play. F1, Up, Enter: UW1 stays on 'save' and opens the save
/// page; UW2 goes round to the last button, which the port shows as 'quit', and opens the quit
/// page. Escape. UW2 then: F1, F6: back in play.
/// </summary>
public sealed class OptionsKeys : Scenario
{
    public override string Name => "options-keys";
    public override string Cites => "underworld-exhumed uw1/src/ui/OPTIONS.C:114-128, 175, 421-438; uw2/src/ui/WRAPPER.C:111-147, 160-182, 322";

    bool _opened;
    uimanager.OptionMenus _afterDownSpaceEnter, _afterUpEnter;
    bool _inGameAfterEscape, _inGameAfterF6;

    public override Task Setup(ScenarioContext c) => Task.CompletedTask;

    public override async Task Act(ScenarioContext c)
    {
        await MenuKeys.Tap(c, Key.F1);
        _opened = uimanager.CurrentGameMode == uimanager.GameModes.OPTIONS
            && uimanager.CurrentGameOptionMenu == uimanager.OptionMenus.MainOptionMenu;
        if (!_opened) return;
        await MenuKeys.Tap(c, Key.Down);
        await MenuKeys.Tap(c, Key.Space);
        await MenuKeys.Tap(c, Key.Enter);
        _afterDownSpaceEnter = uimanager.CurrentGameOptionMenu;
        await MenuKeys.Tap(c, Key.Escape);
        _inGameAfterEscape = uimanager.InGame;

        await MenuKeys.Tap(c, Key.F1);
        await MenuKeys.Tap(c, Key.Up);
        await MenuKeys.Tap(c, Key.Enter);
        _afterUpEnter = uimanager.CurrentGameOptionMenu;
        await MenuKeys.Tap(c, Key.Escape);

        if (c.Game == "uw2")
        {
            await MenuKeys.Tap(c, Key.F1);
            await MenuKeys.Tap(c, Key.F6);
            _inGameAfterF6 = uimanager.InGame;
        }
        if (!uimanager.InGame) uimanager.ReturnToGameFromOptions();
    }

    public override string Assert(ScenarioContext c)
    {
        if (!_opened) return "precondition: F1 did not open the options panel on its top page";
        bool uw1 = c.Game == "uw1";
        var k = new Checks();
        k.True(_afterDownSpaceEnter == uimanager.OptionMenus.MusicMenu,
            $"Down, Space, Enter on the top page expected the music page, got {_afterDownSpaceEnter} ({(uw1 ? "uw1 OPTIONS.C:114-121, 425-438" : "uw2 WRAPPER.C:124-133")})");
        k.True(_inGameAfterEscape, $"Escape in the options expected play to resume ({(uw1 ? "uw1 OPTIONS.C:126-127" : "uw2 WRAPPER.C:134-137")})");
        var expected = uw1 ? uimanager.OptionMenus.SaveMenu : uimanager.OptionMenus.ConfirmQuit;
        k.True(_afterUpEnter == expected,
            $"Up, Enter on the top page expected {expected}, got {_afterUpEnter} ({(uw1 ? "uw1 OPTIONS.C:428-429, the highlight stops at the top" : "uw2 WRAPPER.C:165-168, the highlight goes round")})");
        if (!uw1) k.True(_inGameAfterF6, "F6 on the top page expected play to resume (uw2 WRAPPER.C:143-146)");
        return k.Result;
    }
}

/// <summary>
/// Ctrl+S, R, M, F, D and Q in play open the options at that page (uw1 PLAYER.C:241-246,
/// OPTIONS.C:440-478; uw2 PLAYER.C:235-240, ICONS.C:155-196), and are refused with "You cannot
/// select options partway through an action." while one is in progress (uw1 OPTIONS.C:469-470,
/// string 0xA0; uw2 ICONS.C:158-163, 0xAE).
///
/// Ctrl+M opens the music page with the present state lit, and Enter presses it: UW1 opened it
/// with options_quick set, so the choice closes the panel (choose_music, uw1 OPTIONS.C:235-256);
/// UW2 stays on the page (do_musicsound_opt has no sub-group, uw2 ICONS.C:62-66). Ctrl+D the
/// detail page, Ctrl+Q the quit page, Ctrl+S the save page, Ctrl+R the restore page, Ctrl+F the
/// sound page, each closed with Escape. Then, with an object being used on another
/// (useon.CurrentItemBeingUsed, GameInputMode 2), Ctrl+S is refused.
/// </summary>
public sealed class OptionShortcuts : Scenario
{
    public override string Name => "option-shortcuts";
    public override string Cites => "underworld-exhumed uw1/src/ui/OPTIONS.C:235-256, 440-478, uw1/src/game/PLAYER.C:241-246; uw2/src/ui/ICONS.C:155-196, uw2/src/game/PLAYER.C:235-240";

    uimanager.OptionMenus _ctrlM, _ctrlD, _ctrlQ, _ctrlS, _ctrlR, _ctrlF;
    bool _openM, _openD, _openQ, _openS, _openR, _openF;
    bool _inGameAfterMusicEnter;
    uimanager.OptionMenus _menuAfterMusicEnter;
    bool _music;
    bool _refusedInGame;
    string _refusalText;
    bool _inGame;

    public override Task Setup(ScenarioContext c)
    {
        _inGame = uimanager.InGame;
        _music = playerdat.MusicEnabled;
        return Task.CompletedTask;
    }

    async Task<(bool open, uimanager.OptionMenus page)> Shortcut(ScenarioContext c, Key key)
    {
        await MenuKeys.Tap(c, key, ctrl: true);
        var result = (uimanager.CurrentGameMode == uimanager.GameModes.OPTIONS, uimanager.CurrentGameOptionMenu);
        return result;
    }

    async Task Close(ScenarioContext c)
    {
        await MenuKeys.Tap(c, Key.Escape);
        if (!uimanager.InGame) uimanager.ReturnToGameFromOptions();
        await c.Frames(2);
    }

    public override async Task Act(ScenarioContext c)
    {
        if (!_inGame) return;
        (_openM, _ctrlM) = await Shortcut(c, Key.M);
        if (_openM)
        {
            await MenuKeys.Tap(c, Key.Enter);
            _inGameAfterMusicEnter = uimanager.InGame;
            _menuAfterMusicEnter = uimanager.CurrentGameOptionMenu;
            if (!uimanager.InGame) await Close(c);
        }
        (_openD, _ctrlD) = await Shortcut(c, Key.D); if (_openD) await Close(c);
        (_openQ, _ctrlQ) = await Shortcut(c, Key.Q); if (_openQ) await Close(c);
        (_openS, _ctrlS) = await Shortcut(c, Key.S); if (_openS) await Close(c);
        (_openR, _ctrlR) = await Shortcut(c, Key.R); if (_openR) await Close(c);
        (_openF, _ctrlF) = await Shortcut(c, Key.F); if (_openF) await Close(c);

        useon.CurrentItemBeingUsed = new useon(playerdat.playerObject, false);
        try
        {
            uimanager.instance.scroll.Clear();
            await MenuKeys.Tap(c, Key.S, ctrl: true);
            await c.Frames(10);
            _refusedInGame = uimanager.InGame;
            // The scroll wraps the line and colours it: compare the words alone.
            _refusalText = Words(string.Join(" ", uimanager.instance.scroll.Lines.Select(l => l.LineText)));
        }
        finally
        {
            useon.CurrentItemBeingUsed = null;
            if (!uimanager.InGame) uimanager.ReturnToGameFromOptions();
        }
    }

    static string Words(string text) =>
        System.Text.RegularExpressions.Regex.Replace(
            System.Text.RegularExpressions.Regex.Replace(text, @"\[/?color[^\]]*\]", ""), @"\s+", " ").Trim();

    public override string Assert(ScenarioContext c)
    {
        if (!_inGame) return "precondition: not in game";
        bool uw1 = c.Game == "uw1";
        string cite = uw1 ? "uw1 OPTIONS.C:440-478" : "uw2 ICONS.C:164-194";
        var k = new Checks();
        void Page(string key, bool open, uimanager.OptionMenus got, uimanager.OptionMenus want) =>
            k.True(open && got == want, $"Ctrl+{key} in play expected the options at {want}, got {(open ? got.ToString() : "no options")} ({cite})");
        Page("M", _openM, _ctrlM, uimanager.OptionMenus.MusicMenu);
        if (_openM)
        {
            if (uw1) k.True(_inGameAfterMusicEnter, $"Enter on the music page opened by Ctrl+M expected the panel closed (options_quick, uw1 OPTIONS.C:249-255), got {_menuAfterMusicEnter}");
            else k.True(!_inGameAfterMusicEnter && _menuAfterMusicEnter == uimanager.OptionMenus.MusicMenu, $"Enter on the music page expected to stay on it (uw2 WRAPPER.C:378-391, ICONS.C:62-66), got in game {_inGameAfterMusicEnter}, {_menuAfterMusicEnter}");
        }
        k.True(playerdat.MusicEnabled == _music, $"Enter on the lit music button expected music left {(_music ? "on" : "off")}");
        Page("D", _openD, _ctrlD, uimanager.OptionMenus.DetailMenu);
        Page("Q", _openQ, _ctrlQ, uimanager.OptionMenus.ConfirmQuit);
        Page("S", _openS, _ctrlS, uimanager.OptionMenus.SaveMenu);
        Page("R", _openR, _ctrlR, uimanager.OptionMenus.RestoreMenu);
        Page("F", _openF, _ctrlF, uimanager.OptionMenus.SoundMenu);
        var refusal = Words(GameStringFormat.StripDisplayCodes(GameStrings.GetString(1, GameStrings.str_you_cannot_select_options_partway_through_an_action_)));
        k.True(_refusedInGame, $"Ctrl+S while an object is being used expected the options refused ({(uw1 ? "uw1 OPTIONS.C:469-470" : "uw2 ICONS.C:158-163")})");
        k.True(_refusalText != null && refusal.Length > 0 && _refusalText.Contains(refusal),
            $"Ctrl+S while an object is being used expected \"{refusal}\" (string 0x{(uw1 ? 0xA0 : 0xAE):X}), the scroll shows \"{_refusalText}\"");
        return k.Result;
    }
}

/// <summary>
/// UW2's options buttons light under the held pointer and act on the one the pointer is let go
/// over (better_mouse_release, uw2 WRAPPER.C:74-89; busywaiting_new_options, :111-117). UW1's act
/// as the button goes down (run_options_panel, uw1 OPTIONS.C:105-112), so this is UW2's alone.
///
/// On the top page: pressing 'music', sliding to 'sound' and letting go opens the sound page; the
/// press alone opens nothing. Back on the top page, pressing 'save', sliding off the panel and
/// letting go opens nothing.
/// </summary>
public sealed class OptionsMouseRelease : Scenario
{
    public override string Name => "options-mouse-release";
    public override string[] Games => new[] { "uw2" };
    public override string Cites => "underworld-exhumed uw2/src/ui/WRAPPER.C:74-89, 111-117";

    bool _opened;
    uimanager.OptionMenus _afterPress, _afterRelease, _afterOffPanel;

    public override Task Setup(ScenarioContext c) => Task.CompletedTask;

    /// <summary>
    /// The middle of option button b in window coordinates, which is what an input event
    /// carries: the headless window is far smaller than the game's viewport, and Godot scales
    /// events by the viewport's final transform.
    /// </summary>
    static Vector2 Centre(int b)
    {
        var button = uimanager.GameOptionButtons[b];
        return button.GetViewport().GetFinalTransform() * (button.GetGlobalTransformWithCanvas() * (button.Size / 2));
    }

    static async Task Mouse(ScenarioContext c, InputEventMouse e)
    {
        Input.ParseInputEvent(e);
        await c.Frames(2);
    }

    static InputEventMouseButton Button(Vector2 at, bool pressed) =>
        new() { ButtonIndex = MouseButton.Left, Pressed = pressed, Position = at, GlobalPosition = at, ButtonMask = pressed ? MouseButtonMask.Left : 0 };

    static InputEventMouseMotion Motion(Vector2 at) =>
        new() { Position = at, GlobalPosition = at, ButtonMask = MouseButtonMask.Left };

    public override async Task Act(ScenarioContext c)
    {
        await MenuKeys.Tap(c, Key.F1);
        _opened = uimanager.CurrentGameMode == uimanager.GameModes.OPTIONS
            && uimanager.CurrentGameOptionMenu == uimanager.OptionMenus.MainOptionMenu;
        if (!_opened) return;

        await Mouse(c, Motion(Centre(2)));
        await Mouse(c, Button(Centre(2), true));
        _afterPress = uimanager.CurrentGameOptionMenu;
        await Mouse(c, Motion(Centre(3)));
        await Mouse(c, Button(Centre(3), false));
        _afterRelease = uimanager.CurrentGameOptionMenu;

        await MenuKeys.Tap(c, Key.Escape);
        await MenuKeys.Tap(c, Key.F1);
        await Mouse(c, Motion(Centre(0)));
        await Mouse(c, Button(Centre(0), true));
        var away = uimanager.GameOptionButtons[0].GetViewport().GetFinalTransform() * new Vector2(10, 10);
        await Mouse(c, Motion(away));
        await Mouse(c, Button(away, false));
        _afterOffPanel = uimanager.CurrentGameOptionMenu;
        if (!uimanager.InGame) uimanager.ReturnToGameFromOptions();
    }

    public override string Assert(ScenarioContext c)
    {
        if (!_opened) return "precondition: F1 did not open the options panel on its top page";
        var k = new Checks();
        k.True(_afterPress == uimanager.OptionMenus.MainOptionMenu, $"pressing 'music' expected nothing yet (the button acts on release, uw2 WRAPPER.C:80-88, 115-116), got {_afterPress}");
        k.True(_afterRelease == uimanager.OptionMenus.SoundMenu, $"press on 'music', release on 'sound' expected the sound page (uw2 WRAPPER.C:82-88, 115-116), got {_afterRelease}");
        k.True(_afterOffPanel == uimanager.OptionMenus.MainOptionMenu, $"press on 'save', release off the panel expected nothing (uw2 WRAPPER.C:82-88, 114-116), got {_afterOffPanel}");
        return k.Result;
    }
}
