using System.Linq;
using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// How long a lasting spell runs (UW1 and UW2). The original's set_curmagic gives the new active spell a duration, in
/// duration checks, from the stability bits of the spell's minor: 0x40 gives rollem(2, 8) + 6 and 0x80 gives
/// rollem(3, 20) + 24 (uw1/src/game/PLAYTIME.C:227-241; uw2/src/game/PLAYTIME.C:254-268). So the 0x40 spells last 8 to 22
/// checks and the 0x80 spells 27 to 84.
///
/// Observed by Underworld Revisited (PQMarine, MIT): Assets/UWDataImport/UWData/UWSpellCasting.cs:515-521.
///
/// The scenario finds a spell of class 0, 2 or 3 with each stability, casts it as the player 200 times with no other
/// active spell (SpellCasting.CastSpell, as a cast that passed its checks does) and reads the duration it got. Every
/// duration must lie in the original's range.
///
/// The port rolled 2d8 and 3d24 (src/magic/spellcasting_activeeffects.cs:20-22), so durations of 2 to 16 and 3 to 72.
/// </summary>
public sealed class SpellDurations : Scenario
{
    const int Casts = 200;

    public override string Name => "spell-durations";
    public override string Cites => "underworld-exhumed uw1/src/game/PLAYTIME.C:227-241; uw2/src/game/PLAYTIME.C:254-268";

    string _problem;
    readonly int[] _idx = { -1, -1 }, _min = { int.MaxValue, int.MaxValue }, _max = { int.MinValue, int.MinValue };
    static readonly int[] Stability = { 0x40, 0x80 };

    public override Task Setup(ScenarioContext c)
    {
        for (int s = 0; s < 2; s++)
        {
            var spell = RunicMagic.SpellList.FirstOrDefault(p => p != null && p.SpellMajorClass != 1 && p.SpellMajorClass <= 3 && (p.SpellMinorClass & 0xC0) == Stability[s]);
            if (spell == null) { _problem = $"no spell of class 0, 2 or 3 with stability 0x{Stability[s]:X}"; return Task.CompletedTask; }
            _idx[s] = spell.SpellIndex;
        }
        BatchB.ReadyCaster();
        return Task.CompletedTask;
    }

    public override async Task Act(ScenarioContext c)
    {
        if (_problem != null) return;
        for (int s = 0; s < 2; s++)
        {
            var spell = RunicMagic.SpellList[_idx[s]];
            for (int i = 0; i < Casts; i++)
            {
                playerdat.ActiveSpellEffectCount = 0;
                RunicMagic.PendingSpellCost = 0;
                SpellCasting.CastSpell(spell.SpellMajorClass, spell.SpellMinorClass, playerdat.playerObject, null,
                    playerdat.playerObject.tileX, playerdat.playerObject.tileY, false);
                if (playerdat.ActiveSpellEffectCount != 1) { _problem = $"spell {_idx[s]} did not start an active spell"; return; }
                int d = playerdat.GetEffectStability(0);
                _min[s] = System.Math.Min(_min[s], d);
                _max[s] = System.Math.Max(_max[s], d);
            }
            c.Note($"spell {_idx[s]} (class {spell.SpellMajorClass}, minor 0x{spell.SpellMinorClass:X}): durations {_min[s]} to {_max[s]} over {Casts} casts");
        }
        playerdat.ActiveSpellEffectCount = 0;
        playerdat.PlayerStatusUpdate();
        await c.Frames(1);
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        string cite = c.Game == "uw1" ? "set_curmagic: uw1 PLAYTIME.C:235-240" : "set_curmagic: uw2 PLAYTIME.C:262-267";
        var k = new Checks();
        k.Within("shortest 0x40 duration (2d8 + 6)", 8, 22, _min[0], cite);
        k.Within("longest 0x40 duration (2d8 + 6)", 8, 22, _max[0], cite);
        k.Within("shortest 0x80 duration (3d20 + 24)", 27, 84, _min[1], cite);
        k.Within("longest 0x80 duration (3d20 + 24)", 27, 84, _max[1], cite);
        return k.Result;
    }
}
