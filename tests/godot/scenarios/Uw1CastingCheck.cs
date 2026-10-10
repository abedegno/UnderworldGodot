using System.Collections.Generic;
using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// The UW1 casting roll. The original's player_cast checks skill_check(Casting + 5, circle * 2)
/// (uw1/src/combat/RUNES.C:247-249): a result of 0 fails the spell, which costs nothing; 1 or 2
/// casts it and -1 backfires, and both of those pay the mana, 3 * circle, at once for a spell that
/// is not a missile (RUNES.C:259-263). UW2's rule, skill_check(Casting, circle * 3), is different
/// (uw2/src/combat/RUNES.C:238).
///
/// For each row the scenario sets Casting, selects the runes of the circle 1 spell 0 (class 0, a
/// light spell, paid at once), seeds the game's generator and casts through the port's
/// RunicMagic.CastRunicSpell, the cast button. The mana spent must be 3 when the original's
/// skill_check over the same seed gives anything but 0, and 0 when it gives 0. The seeds are found
/// by the scenario: for each Casting, one whose first rand() % 31 makes the two rules disagree, and
/// one where they agree.
///
/// UW1 only. The port once used UW2's rule in both games (src/magic/runicmagic.cs:339), making
/// every UW1 spell 5 + circle points harder.
/// </summary>
public sealed class Uw1CastingCheck : Scenario
{
    const int SpellIdx = 0, Circle = 1, Cost = 3;
    static readonly int[] Castings = { 13, 8, 20 };

    public override string Name => "uw1-casting-check";
    public override string[] Games => new[] { "uw1" };
    public override string Cites => "underworld-exhumed uw1/src/combat/RUNES.C:236-268 (player_cast); uw1/src/game/SKILLCHK.C:48 (skill_check)";

    string _problem;
    readonly List<(int casting, uint seed, int dosResult, int spent)> _results = new();

    public override Task Setup(ScenarioContext c)
    {
        if (RunicMagic.SpellList[SpellIdx].SpellLevel != Circle || RunicMagic.SpellList[SpellIdx].SpellMajorClass > 3)
            _problem = $"spell {SpellIdx} is not a circle {Circle} spell of class 0..3";
        return Task.CompletedTask;
    }

    static int Dos(uint seed, int casting) => new BatchB.DosRand(seed).SkillCheck(casting + 5, Circle * 2);
    static int Uw2Rule(uint seed, int casting) => new BatchB.DosRand(seed).SkillCheck(casting, Circle * 3);

    public override Task Act(ScenarioContext c)
    {
        if (_problem != null) return Task.CompletedTask;
        foreach (int casting in Castings)
        {
            var seeds = new List<uint>();
            for (uint s = 1; s < 5000 && seeds.Count < 1; s++)
                if ((Dos(s, casting) == 0) != (Uw2Rule(s, casting) == 0)) seeds.Add(s);
            for (uint s = 1; s < 5000 && seeds.Count < 2; s++)
                if ((Dos(s, casting) == 0) == (Uw2Rule(s, casting) == 0)) { seeds.Add(s); break; }
            foreach (uint seed in seeds)
            {
                BatchB.ReadyCaster(casting);
                BatchB.SelectSpell(SpellIdx);
                int before = playerdat.play_mana;
                playerdat.ClockValue += 0x10000;
                BatchB.Seed(seed);
                RunicMagic.CastRunicSpell();
                _results.Add((casting, seed, Dos(seed, casting), before - playerdat.play_mana));
            }
        }
        SpellCasting.currentSpell = null;
        foreach (var r in _results) c.Note($"Casting {r.casting}, seed {r.seed}: original's skill_check {r.dosResult}, mana spent {r.spent}");
        return Task.CompletedTask;
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        if (_results.Count < Castings.Length) return $"precondition: only {_results.Count} rows ran";
        var k = new Checks();
        foreach (var r in _results)
            k.Equal($"mana spent casting a circle 1 spell at Casting {r.casting}, seed {r.seed} (original's check {r.dosResult})",
                r.dosResult == 0 ? 0 : Cost, r.spent, "uw1 RUNES.C:249-263, skill_check(Casting + 5, circle * 2)");
        return k.Result;
    }
}
