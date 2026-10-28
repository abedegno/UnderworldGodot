using System.Collections.Generic;
using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// A critter's melee manoeuvre, crit_attack (uw1/src/critter/AI.C:395-458,
/// uw2/src/critter/AI.C:383-445). By the squared fine distance to its target: under 0x31 it backs
/// off three times in four, else sidesteps; over 0x51 it walks in; between, it dodges (chance
/// dexterity in 64) or stands. Within reach, dist up to 0x64, one time in four it starts an attack,
/// chosen by `for (i = 0; attacks[i].prob &lt;= r &amp;&amp; i &lt; 2; i++) r -= attacks[i].prob` with
/// r = rand() % 100 (uw1 AI.C:442-447, uw2 AI.C:432-437); otherwise its attack charge (ATKFRAME)
/// counts up to 0xF. The sequences differ between the games: UW1 backs off with 7, walks with 0x2C,
/// stands and dodges in the combat stance 0 and attacks with 1..3 (uw1/src/include/critter.h:124-132);
/// UW2 walks and backs off with 1, stands and dodges with 2 and attacks with 3..5 (uw2 AI.C:395-411, 438).
///
/// The scenario is a vector table: for several distances and seeds it calls the port's
/// ChooseMeleeAttackToMake for a walking critter with Rng.Source a fresh BorlandRandSource of the
/// seed, and compares the sequence and charge left with the original's, worked out here with the
/// same seed's draws in the original's order.
///
/// Both games. The port reaches to 0x100 (src/npc/npcai.cs:2983), chooses the attack with `&gt;=`
/// (npcai.cs:2997), and in UW1 sets UW2's sequence numbers 1 and 2 (npcai.cs:2912-2942), which are
/// UW1's first two attacks.
/// </summary>
public sealed class CritterMeleeChoice : Scenario
{
    public override string Name => "critter-melee-choice";
    public override string Cites => "underworld-exhumed uw1/src/critter/AI.C:395-458, uw1/src/include/critter.h:124-132; uw2/src/critter/AI.C:383-445";

    static readonly int[] Dists = { 0x20, 0x40, 0x60, 0x64, 0x65, 0x90, 0x100 };
    const int Seeds = 8;
    const int StartCharge = 3;

    string _problem;
    int _id, _cases;
    readonly List<string> _diffs = new();

    public override Task Setup(ScenarioContext c)
    {
        _id = BatchA.PickCritter(i => BatchA.Walker(i)
            && critterObjectDat.attackprobability(i, 0) > 0 && critterObjectDat.attackprobability(i, 0) < 100);
        if (_id < 0) _problem = "no walking critter with a first attack probability between 0 and 100";
        return Task.CompletedTask;
    }

    int Combat(ScenarioContext c) => c.Game == "uw1" ? 0 : 2;

    /// <summary>crit_attack's sequence and charge, by the original, drawing from r.</summary>
    (int seq, int charge) Original(ScenarioContext c, int dist, int startSeq, BorlandRandSource r)
    {
        bool uw1 = c.Game == "uw1";
        int seq;
        int charge = StartCharge;
        if (dist < 0x31)
        {
            if (r.Rand() % 4 == 0) { r.Rand(); seq = uw1 ? 0 : 2; }       // sidestep
            else seq = uw1 ? 7 : 1;                                          // back off
        }
        else if (dist > 0x51) seq = uw1 ? 0x2C : 1;                          // walk in
        else if (r.Rand() % 0x40 < critterObjectDat.dexterity(_id)) { r.Rand(); seq = uw1 ? 0 : 2; }
        else seq = uw1 ? 0 : 2;
        if (dist <= 0x64)
        {
            if (r.Rand() % 4 == 0)
            {
                int rr = r.Rand() % 100;
                int i;
                for (i = 0; critterObjectDat.attackprobability(_id, i) <= rr && i < 2; i++)
                    rr -= critterObjectDat.attackprobability(_id, i);
                seq = i + (uw1 ? 1 : 3);
            }
            else if (charge < 0xF) charge++;
        }
        return (seq, charge);
    }

    public override Task Act(ScenarioContext c)
    {
        if (_problem != null) return Task.CompletedTask;
        var critter = BatchA.SpawnCritter(_id, 0, 0);
        if (critter == null) { _problem = "no critter could be made"; return Task.CompletedTask; }
        var t = typeof(npc);
        var saved = Rng.Source;
        try
        {
            foreach (int dist in Dists)
            for (uint seed = 1; seed <= Seeds; seed++)
            {
                critter.npc_animation = (short)Combat(c);
                critter.AnimationFrame = 0;
                critter.SwingChargeIndex = StartCharge;
                BatchA.Set(t, "currentGTargXVector", 3);
                BatchA.Set(t, "currentGTargYVector", 2);
                BatchA.Set(t, "currentGoalTarget", playerdat.playerObject);
                npc.MaxAnimFrame = 4;
                var (eseq, echarge) = Original(c, dist, Combat(c), new BorlandRandSource(seed));
                Rng.Source = new BorlandRandSource(seed);
                BatchA.Call(t, "ChooseMeleeAttackToMake", critter, dist);
                _cases++;
                if (critter.npc_animation != eseq || critter.SwingChargeIndex != echarge)
                    _diffs.Add($"dist 0x{dist:X} seed {seed}: sequence {critter.npc_animation} charge {critter.SwingChargeIndex}, DOS {eseq} and {echarge}");
            }
        }
        finally { Rng.Source = saved; }
        c.Note($"critter 0x{_id:X}, dexterity {critterObjectDat.dexterity(_id)}, attack chances {critterObjectDat.attackprobability(_id, 0)}/{critterObjectDat.attackprobability(_id, 1)}/{critterObjectDat.attackprobability(_id, 2)}: {_diffs.Count} of {_cases} differ");
        return Task.CompletedTask;
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        if (_diffs.Count == 0) return null;
        return $"melee manoeuvre differs from crit_attack on {_diffs.Count} of {_cases}: {string.Join("; ", _diffs.GetRange(0, System.Math.Min(4, _diffs.Count)))} ({(c.Game == "uw1" ? "uw1 AI.C:395-458" : "uw2 AI.C:383-445")})";
    }
}
