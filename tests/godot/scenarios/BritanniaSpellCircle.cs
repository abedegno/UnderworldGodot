using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// Which spells work in Britannia. The original's player_cast fails a spell outright on the
/// first world's levels (Britannia, levels 1 to 8) when its circle, idx / 8 + 1, is above 3
/// (uw2/src/combat/RUNES.C:219-232), with "Casting was not successful" (fail_spell(3), RUNES.C:202-207).
/// A circle 3 spell goes on to the other checks.
///
/// On level 1, with a caster who passes every other check (level 16, mana 100, Casting 30, so
/// skill_check(30, 9) scores at least 21 and always succeeds, RUNES.C:238), the scenario selects
/// the runes of a circle 4 spell and then of a circle 3 spell (both of class 0 to 3, which pay
/// their mana at once, RUNES.C:247-251) and casts each through the port's RunicMagic.CastRunicSpell,
/// the cast button. The circle 4 spell must cost nothing (refused); the circle 3 spell must cost
/// its 9 mana (cast).
///
/// The port once refused circle 3 too (SpellLevel &gt;= 3, src/magic/runicmagic.cs:476), with the
/// string a failed skill check prints.
/// </summary>
public sealed class BritanniaSpellCircle : Scenario
{
    public override string Name => "britannia-spell-circle";
    public override string[] Games => new[] { "uw2" };
    public override string Cites => "underworld-exhumed uw2/src/combat/RUNES.C:219-253 (player_cast), 202-207 (fail_spell)";

    string _problem;
    int _idx3 = -1, _idx4 = -1, _cost3, _cost4, _level;

    static int Pick(int circle)
    {
        for (int i = (circle - 1) * 8; i < circle * 8; i++)
            if (RunicMagic.SpellList[i] != null && RunicMagic.SpellList[i].SpellMajorClass <= 3) return i;
        return -1;
    }

    public override Task Setup(ScenarioContext c)
    {
        _level = playerdat.dungeon_level;
        _idx3 = Pick(3);
        _idx4 = Pick(4);
        if (_idx3 < 0 || _idx4 < 0) _problem = "no circle 3 or circle 4 spell of class 0..3 in the spell list";
        BatchB.ReadyCaster();
        return Task.CompletedTask;
    }

    public override Task Act(ScenarioContext c)
    {
        if (_problem != null) return Task.CompletedTask;
        BatchB.SelectSpell(_idx4);
        int before = playerdat.play_mana;
        BatchB.CastSelected();
        _cost4 = before - playerdat.play_mana;

        BatchB.ReadyCaster();
        BatchB.SelectSpell(_idx3);
        before = playerdat.play_mana;
        BatchB.CastSelected();
        _cost3 = before - playerdat.play_mana;
        SpellCasting.currentSpell = null;
        c.Note($"on level {_level}: circle 4 spell {_idx4} cost {_cost4} mana, circle 3 spell {_idx3} cost {_cost3}");
        return Task.CompletedTask;
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        if (_level < 1 || _level > 8) return $"precondition: the player is on level {_level}, not in Britannia";
        var k = new Checks();
        k.Equal("mana spent on a circle 4 spell in Britannia (refused)", 0, _cost4, "uw2 RUNES.C:232, level > 3 fails");
        k.Equal("mana spent on a circle 3 spell in Britannia (cast)", 9, _cost3, "uw2 RUNES.C:232 lets circle 3 through; cost 3 * circle, RUNES.C:247-251");
        return k.Result;
    }
}
