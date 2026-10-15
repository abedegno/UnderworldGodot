using System.Collections.Generic;
using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// The animation sequence a fleeing or defending critter is given (UW1, with UW2 kept as it is).
/// UW1 writes its sequence numbers out (uw1/src/include/critter.h:124-132: SEQ_COMBAT 0, the
/// attacks 1 to 3, SEQ_BACK_OFF 7, SEQ_WALK 0x2C); UW2 uses its own numbers through
/// change_or_inc_seq (walk and back off 1, combat stance 2).
///
/// crit_flee (uw1/src/critter/AI.C:613-678, uw2/src/critter/AI.C:582-646): near the target it
/// backs away, SEQ_BACK_OFF (uw1 :636; uw2 seq 1, :606); stuck and already cornered (goal 9)
/// within 3 tiles it faces the target in SEQ_COMBAT (uw1 :647; uw2 seq 2, :616); otherwise it
/// runs, SEQ_WALK (uw1 :677; uw2 seq 1, :645). crit_defense (uw1 AI.C:571-599, uw2 AI.C:541-569):
/// within 2 tiles but not in reach it faces the target in SEQ_COMBAT (uw1 :597; uw2 seq 2, :567).
///
/// The port's NPC_Goal6 and NPC_Goal9 passed UW2's numbers 1 and 2 in both games
/// (src/npc/npcai.cs, NPC_Goal6 and NPC_Goal9), which in UW1 are the first two attacks.
///
/// The scenario calls the port's NPC_Goal6 and NPC_Goal9 for a walking non-caster critter with the
/// AI's globals set for each path (as FleeingCritterSteers and CritterMeleeChoice do), and reads
/// the sequence left. The back-off path rolls against the critter's nerve first; seeds where the
/// roll turns it to fight instead (goal 9) are left out.
/// </summary>
public sealed class Uw1NpcSequences : Scenario
{
    public override string Name => "uw1-npc-sequences";
    public override string Cites => "underworld-exhumed uw1/src/critter/AI.C:571-678, uw1/src/include/critter.h:124-132; uw2/src/critter/AI.C:541-646";

    const uint Seeds = 8;

    string _problem;
    int _id, _backOffCases;
    readonly List<string> _diffs = new();
    readonly List<string> _seen = new();

    public override Task Setup(ScenarioContext c)
    {
        _id = BatchA.PickCritter(i => BatchA.Walker(i) && critterObjectDat.spell2C(i) == -1);
        if (_id < 0) _problem = "no walking critter without a spell";
        return Task.CompletedTask;
    }

    void Globals(uwObject critter, int tileDist, int xvec, int yvec, bool stuck)
    {
        var t = typeof(npc);
        var p = playerdat.playerObject;
        BatchA.Set(t, "IsNPCActive_dseg_67d6_2234", true);
        BatchA.Set(t, "currentGoalTarget", p);
        BatchA.Set(t, "currentGTargSquaredDistanceByTiles", tileDist);
        BatchA.Set(t, "currentGTargXVector", xvec);
        BatchA.Set(t, "currentGTargYVector", yvec);
        BatchA.Set(t, "RelatedToMotionCollision_dseg_67d6_224E", stuck);
        BatchA.Set(t, "HasCurrobjHeadingChanged_dseg_67d6_2242", false);
        BatchA.Set(t, "currObj_XHome", (int)critter.npc_xhome);
        BatchA.Set(t, "currObj_YHome", (int)critter.npc_yhome);
        BatchA.Set(t, "currentGTargXHome", p.tileX);
        BatchA.Set(t, "currentGTargYHome", p.tileY);
        BatchA.Set(t, "currObjXCoordinate", (critter.npc_xhome << 3) + critter.xpos);
        BatchA.Set(t, "currObjYCoordinate", (critter.npc_yhome << 3) + critter.ypos);
        BatchA.Set(t, "currentGTargXCoord", (p.tileX << 3) + p.xpos);
        BatchA.Set(t, "currentGTargYCoord", (p.tileY << 3) + p.ypos);
    }

    void Check(string path, uwObject critter, int expected, string cite)
    {
        _seen.Add($"{path} {critter.npc_animation}");
        if (critter.npc_animation != expected)
            _diffs.Add($"{path}: sequence {critter.npc_animation}, expected {expected} ({cite})");
    }

    public override Task Act(ScenarioContext c)
    {
        if (_problem != null) return Task.CompletedTask;
        bool uw1 = c.Game == "uw1";
        var critter = BatchA.SpawnCritter(_id, 4, 0);
        if (critter == null) critter = BatchA.SpawnCritter(_id, -4, 0);
        if (critter == null) { _problem = "no critter could be made"; return Task.CompletedTask; }
        var t = typeof(npc);
        short start = npc.ANIMATION_IDLE;
        var saved = Rng.Source;
        try
        {
            // crit_flee near the target, not stuck: backs away.
            for (uint seed = 1; seed <= Seeds; seed++)
            {
                critter.npc_goal = 6; critter.npc_gtarg = 1;
                critter.npc_animation = start; critter.AnimationFrame = 0;
                critter.zpos = playerdat.playerObject.zpos;
                Globals(critter, 2, 12, 0, false);
                Rng.Source = new BorlandRandSource(seed);
                BatchA.Call(t, "NPC_Goal6", critter);
                if (critter.npc_goal != 6) continue;   // the nerve roll turned it to fight
                _backOffCases++;
                Check($"flee, backing away (seed {seed})", critter, uw1 ? 7 : 1, uw1 ? "SEQ_BACK_OFF: uw1 AI.C:636" : "uw2 AI.C:606");
            }

            // crit_flee stuck within 3 tiles, already cornered (goal 9): combat stance.
            critter.npc_goal = 9; critter.npc_gtarg = 1;
            critter.npc_animation = start; critter.AnimationFrame = 0;
            Globals(critter, 5, 16, 8, true);
            Rng.Source = new BorlandRandSource(1);
            BatchA.Call(t, "NPC_Goal6", critter);
            Check("flee, stuck and cornered", critter, uw1 ? 0 : 2, uw1 ? "SEQ_COMBAT: uw1 AI.C:647" : "uw2 AI.C:616");

            // crit_flee far from the target, not stuck: runs.
            critter.npc_goal = 6; critter.npc_gtarg = 1;
            critter.npc_animation = start; critter.AnimationFrame = 0;
            Globals(critter, 20, 32, 8, false);
            Rng.Source = new BorlandRandSource(1);
            BatchA.Call(t, "NPC_Goal6", critter);
            Check("flee, running", critter, uw1 ? 0x2C : 1, uw1 ? "SEQ_WALK: uw1 AI.C:677" : "uw2 AI.C:645");

            // crit_defense within 2 tiles but out of reach: faces the target in the combat stance.
            critter.npc_goal = 9; critter.npc_gtarg = 1;
            critter.npc_animation = start; critter.AnimationFrame = 0;
            Globals(critter, 4, 13, 0, false);
            Rng.Source = new BorlandRandSource(1);
            BatchA.Call(t, "NPC_Goal9", critter);
            Check("defend, out of reach", critter, uw1 ? 0 : 2, uw1 ? "SEQ_COMBAT: uw1 AI.C:597" : "uw2 AI.C:567");
        }
        finally { Rng.Source = saved; }
        c.Note($"critter 0x{_id:X}; {_backOffCases} back-off seeds; {string.Join(", ", _seen)}");
        return Task.CompletedTask;
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        if (_backOffCases == 0) return $"precondition: every one of {Seeds} seeds turned the fleeing critter to fight";
        if (_diffs.Count == 0) return null;
        return $"critter sequence differs from the original on {_diffs.Count} paths: {string.Join("; ", _diffs)}";
    }
}
