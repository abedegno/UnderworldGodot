using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// UW1's main menu animates its flames by rotating palette colours 0x40..0x7F one place whenever 14
/// ticks of the game clock have passed since the last step (ovr138_94, uw1/src/ui/MAINMENU.C:84-91,
/// called while the menu and the saved-game list wait, MAINMENU.C:212, 265, 284, 322). The clock is
/// an AIL timer at 256 Hz (uw1/src/sound/SOUND.C:176-179), so the menu steps 256 / 14 times a
/// second, about 18. UW2's menu has no colour cycling (uw2/src/ui/MAINMENU.C has no call to
/// rotate_bank or cycle_colors).
///
/// The scenario puts the port in its main menu mode and counts the steps of the menu's palette
/// cycle (PaletteLoader.NextPaletteCycle_UI, which the menu background's shader reads) over two
/// seconds: 36 or 37 expected. UW1 only.
///
/// The port stepped it every 0.2 s (main.cs _PhysicsProcess), 10 in two seconds.
/// </summary>
public sealed class MenuPaletteCycle : Scenario
{
    public override string Name => "menu-palette-cycle";
    public override string[] Games => new[] { "uw1" };
    public override string Cites => "underworld-exhumed uw1/src/ui/MAINMENU.C:84-91 (ovr138_94); uw1/src/sound/SOUND.C:179";

    bool _atMenu;
    int _steps;

    public override async Task Setup(ScenarioContext c)
    {
        uimanager.CurrentGameMode = uimanager.GameModes.MAIN;
        await c.Frames(5);
        _atMenu = uimanager.AtMainMenu;
    }

    public override async Task Act(ScenarioContext c)
    {
        int last = PaletteLoader.NextPaletteCycle_UI;
        for (int i = 0; i < 120; i++)
        {
            await c.Frames(1);
            int now = PaletteLoader.NextPaletteCycle_UI;
            if (now != last) _steps++;
            last = now;
        }
        c.Note($"{_steps} palette steps in 120 frames at the main menu");
        uimanager.CurrentGameMode = uimanager.GameModes.GAME;
        await c.Frames(1);
    }

    public override string Assert(ScenarioContext c)
    {
        if (!_atMenu) return "precondition: the port was not in its main menu mode";
        var k = new Checks();
        k.Within("menu palette steps in two seconds (2 * 256 / 14)", 36, 37, _steps, "uw1 MAINMENU.C:86, SOUND.C:179");
        return k.Result;
    }
}
