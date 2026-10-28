using System.Linq;
using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// Casting a lasting spell with three already running (UW1 and UW2). The original's set_curmagic keeps at most
/// three active spells and refuses a fourth (uw1/src/game/PLAYTIME.C:215-225; uw2/src/game/PLAYTIME.C:250);
/// do_spell then returns 0 for classes 0 to 3 (uw1/src/combat/SPELLS.C:102-111; uw2/src/combat/SPELLS.C:102).
/// player_cast has already paid the mana for a spell that is no missile, keeps it paid, and prints "Casting was
/// not successful." with the fizzle sound (uw1/src/combat/RUNES.C:259-269, 221-226; uw2/src/combat/RUNES.C:248-251, 308-310).
///
/// Observed in DOS by Underworld Revisited (PQMarine, MIT): Assets/UWDataImport/UWData/UWSpellCasting.cs:528-532.
///
/// The scenario fills the three active spell slots, readies the player to cast anything (Casting 30, so the skill
/// check cannot fail on a circle 1 spell), selects a circle 1 spell of class 0 to 3 and casts it as the cast button
/// does (RunicMagic.CastRunicSpell). Three mana must be gone, the count must stay at three, and the scroll must say
/// "Casting was not successful.".
/// </summary>
public sealed class CastSlotsFull : Scenario
{
    public override string Name => "cast-slots-full";
    public override string Cites => "underworld-exhumed uw1/src/game/PLAYTIME.C:215-225, uw1/src/combat/SPELLS.C:102-111, uw1/src/combat/RUNES.C:259-269; uw2/src/game/PLAYTIME.C:250, uw2/src/combat/SPELLS.C:102, uw2/src/combat/RUNES.C:248-251, 308-310";

    string _problem, _text;
    int _manaBefore, _manaAfter, _countAfter, _idx = -1;

    public override Task Setup(ScenarioContext c)
    {
        var spell = RunicMagic.SpellList.Where(s => s != null && s.SpellLevel == 1 && s.SpellMajorClass <= 3).FirstOrDefault();
        if (spell == null) { _problem = "no circle 1 spell of class 0 to 3"; return Task.CompletedTask; }
        _idx = spell.SpellIndex;
        BatchB.ReadyCaster();
        for (int i = 0; i < 3; i++)
            playerdat.SetSpellEffect(index: i, effectid: (2 << 4) + 0, stability: 50);   // three light spells, class 0 minor 2
        playerdat.ActiveSpellEffectCount = 3;
        return Task.CompletedTask;
    }

    public override async Task Act(ScenarioContext c)
    {
        if (_problem != null) return;
        BatchB.SelectSpell(_idx);
        uimanager.instance.scroll.Clear();
        _manaBefore = playerdat.play_mana;
        BatchB.CastSelected();
        _manaAfter = playerdat.play_mana;
        _countAfter = playerdat.ActiveSpellEffectCount;
        _text = string.Join(" ", uimanager.instance.scroll.Lines.Select(l => l?.LineText ?? "")).Trim();
        c.Note($"spell {_idx} (class {RunicMagic.SpellList[_idx].SpellMajorClass}, minor {RunicMagic.SpellList[_idx].SpellMinorClass}): mana {_manaBefore} to {_manaAfter}, active spells {_countAfter}, scroll \"{_text}\"");
        await c.Frames(1);
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        bool uw1 = c.Game == "uw1";
        var k = new Checks();
        k.Equal("mana spent on a lasting spell with three running", 3, _manaBefore - _manaAfter, uw1 ? "paid before do_spell: uw1 RUNES.C:259-262" : "paid before do_spell: uw2 RUNES.C:248-251");
        k.Equal("active spells after the refused cast", 3, _countAfter, uw1 ? "set_curmagic: uw1 PLAYTIME.C:221-222" : "set_curmagic: uw2 PLAYTIME.C:250");
        string fail = GameStrings.GetString(1, GameStrings.str_casting_was_not_successful_).Trim();
        k.True(_text.Contains(fail), $"the refused cast: expected \"{fail}\", got \"{_text}\" ({(uw1 ? "fail_spell(3): uw1 RUNES.C:268-269" : "fail_spell(3): uw2 RUNES.C:309-310")})");
        return k.Result;
    }
}
