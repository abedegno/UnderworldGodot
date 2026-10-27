using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// Once the last talisman is destroyed the player cannot die. In the original the game loop calls
/// player_is_dead when the player's hit points reach 0 (uw1/src/ui/INTERACT.C:240-241), and
/// player_is_dead first checks player->talismans, the talismans left (player byte 0x6D,
/// uw1/src/include/player.h:79): at 0 it sets the hit points to 4 and returns, before any of the
/// death (uw1/src/game/SKILLS.C:825-829). The count reaches 0 when the last talisman is destroyed
/// (uw1/src/motion/OBJPHYS.C:443) and becomes 0xFF once the Slasher of Veils is gone
/// (uw1/src/game/SKILLS.C:714).
///
/// The port keeps the count in quest 36 (pdat 0x6E, its offsets being DOS plus one,
/// src/player/playerdatquest.cs GetQuest). The scenario sets it to 0, drops the hit points to 0 and lets
/// the game loop run: the player must be alive again with 4 hit points and still in the game.
///
/// UW1 only. The port once had no such check (src/player/playerdatdeath.cs, PlayerDeath), so the
/// player died and the death cutscene played.
/// </summary>
public sealed class LastTalismanKeepsHp : Scenario
{
    const int TalismansLeft = 36;

    public override string Name => "last-talisman-keeps-hp";
    public override string[] Games => new[] { "uw1" };
    public override string Cites => "underworld-exhumed uw1/src/game/SKILLS.C:825-829 (player_is_dead), uw1/src/ui/INTERACT.C:240-241, uw1/src/include/player.h:79";

    string _problem;
    int _hpAfter;
    bool _inGameAfter;

    public override Task Setup(ScenarioContext c)
    {
        playerdat.SetQuest(TalismansLeft, 0);
        if (playerdat.GetQuest(TalismansLeft) != 0) _problem = "quest 36 did not read back 0";
        else if (!uimanager.InGame || playerdat.play_hp <= 0) _problem = "not in game with the player alive";
        return Task.CompletedTask;
    }

    public override async Task Act(ScenarioContext c)
    {
        if (_problem != null) return;
        playerdat.play_hp = 0;
        if (playerdat.play_hp != 0) { _problem = "the hit points did not read back 0"; return; }
        await c.Frames(10);
        _hpAfter = playerdat.play_hp;
        _inGameAfter = uimanager.InGame;
        c.Note($"talismans left 0, hit points set to 0; after 10 frames hit points {_hpAfter}, in game {_inGameAfter}");
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        var k = new Checks();
        k.Equal("hit points after dying with no talismans left", 4, _hpAfter, "SKILLS.C:827");
        k.True(_inGameAfter, "expected still in game, the death did not happen (SKILLS.C:828 returns), but the game left it");
        return k.Result;
    }
}
