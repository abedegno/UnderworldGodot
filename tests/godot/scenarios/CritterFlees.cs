using System.Collections.Generic;
using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// A fleeing critter, crit_flee (uw1/src/critter/AI.C:610-679, uw2/src/critter/AI.C:579-647).
/// Near its target (squared tile distance 3 or less) and within 0x10 of its height, abs(dz) with
/// dz = OBJ_Z(mytarget) - OBJ_Z(meptr), it backs away at half speed; otherwise it runs. A critter
/// that is stuck (failed and not aligned) further away turns 90 degrees left or right,
/// newh = (((heading &gt;&gt; 5) + (rand() % 2 * 2 - 1) * 2 + 8) % 8 &lt;&lt; 5) + rand() % 0x20, the
/// rand() % 2 drawn first (uw2 seg007_17A2_137D); within 3 tiles it holds in its combat stance.
/// The sequences differ between the games: UW1 sets SEQ_BACK_OFF 7, SEQ_WALK 0x2C and SEQ_COMBAT 0
/// and always counts the frame on, SET_FRAME((frame + 1) % 4), even from another sequence
/// (uw1/src/include/critter.h:124-133); UW2 uses change_or_inc_seq, which starts a new sequence at
/// frame 0 (uw2 AI.C:125-132), with 1 to back off and run and 2 to hold.
///
/// The scenario calls the port's NPC_Goal6 for a walking critter with the AI's globals set as
/// crit_flee has them, and compares what it leaves with the original's: a critter 0x20 above its
/// target runs (it does not back off), the stuck turn for several seeds and headings with
/// Rng.Source a fresh BorlandRandSource of the seed (the original's draws in its order), and the
/// sequence and frame after backing off, running and holding, from another sequence at frame 2.
///
/// Both games. The port backs off whenever dz &lt; 0x10, with no abs (src/npc/npcai.cs NPC_Goal6),
/// turns 45 degrees with % 5, draws rand() % 0x20 first and shifts it with the heading, and in UW1
/// sets UW2's sequences 1 and 2 (UW1's first two attacks) and restarts the frame at 0.
/// </summary>
public sealed class CritterFlees : Scenario
{
    public override string Name => "critter-flees";
    public override string Cites => "underworld-exhumed uw1/src/critter/AI.C:610-679, uw1/src/include/critter.h:124-133; uw2/src/critter/AI.C:125-132, 579-647";

    static readonly int[] Headings = { 0x00, 0x13, 0x47, 0x80, 0xA5, 0xE0, 0xFF };
    const int Seeds = 6;

    string _problem;
    int _id, _cases;
    readonly List<string> _diffs = new();

    public override Task Setup(ScenarioContext c)
    {
        // Never casts (power 0), never turns to fight when near (b1C_0 >> 3 == 0), and runs at a
        // speed other than its backing-off speed, so running and backing off can be told apart.
        _id = BatchA.PickCritter(i => BatchA.Walker(i)
            && critterObjectDat.NPCPower_0x2DBits1To7(i) == 0
            && critterObjectDat.maybemorale(i) < 8
            && critterObjectDat.run(i) != (critterObjectDat.walkspeed(i) + 1) / 2);
        if (_id < 0) _problem = "no walking critter that never casts, has nerve under 8 and runs at another speed than it backs off";
        return Task.CompletedTask;
    }

    /// <summary>The AI's globals as crit_flee sees them: tdx/tdy, tdistsqr, failed, aligned.</summary>
    static void Globals(uwObject critter, int distTiles, bool failed, bool aligned)
    {
        var t = typeof(npc);
        var p = playerdat.playerObject;
        BatchA.Set(t, "IsNPCActive_dseg_67d6_2234", true);
        BatchA.Set(t, "currentGoalTarget", p);
        BatchA.Set(t, "currentGTargXVector", 3);
        BatchA.Set(t, "currentGTargYVector", 2);
        BatchA.Set(t, "currentGTargSquaredDistanceByTiles", distTiles);
        BatchA.Set(t, "RelatedToMotionCollision_dseg_67d6_224E", failed);
        BatchA.Set(t, "HasCurrobjHeadingChanged_dseg_67d6_2242", aligned);
        // Far from the player, so crit_avoid_player leaves the heading alone.
        int px = (p.npc_xhome << 3) + p.xpos, py = (p.npc_yhome << 3) + p.ypos;
        BatchA.Set(t, "currObjXCoordinate", px + 100);
        BatchA.Set(t, "currObjYCoordinate", py + 100);
        BatchA.Set(t, "currObj_XHome", (int)critter.npc_xhome);
        BatchA.Set(t, "currObj_YHome", (int)critter.npc_yhome);
        npc.MaxAnimFrame = 4;
    }

    void Diff(string what, int actual, int expected)
    {
        _cases++;
        if (actual != expected) _diffs.Add($"{what}: port 0x{actual:X}, DOS 0x{expected:X}");
    }

    public override Task Act(ScenarioContext c)
    {
        if (_problem != null) return Task.CompletedTask;
        var critter = BatchA.SpawnCritter(_id, 2, 0);
        if (critter == null) { _problem = "no critter could be made"; return Task.CompletedTask; }
        bool uw1 = c.Game == "uw1";
        var t = typeof(npc);
        var p = playerdat.playerObject;
        var saved = Rng.Source;
        short savedZ = p.zpos;
        try
        {
            // 1. Near, 0x20 above its target: abs(dz) is not under 0x10, so it runs (uw1 AI.C:627, uw2 AI.C:597).
            p.zpos = 0x10;
            critter.zpos = 0x30;
            critter.npc_goal = 6;
            critter.npc_animation = 0;
            Globals(critter, 2, false, false);
            Rng.Source = new BorlandRandSource(1);
            BatchA.Call(t, "NPC_Goal6", critter);
            Diff("speed 0x20 above its target (runs)", critter.UnkBit_0X13_Bit0to6, critterObjectDat.run(_id));
            p.zpos = savedZ;
            critter.zpos = savedZ;

            // 2. The stuck turn, further than 3 tiles (uw1 AI.C:655, uw2 AI.C:623).
            foreach (int heading in Headings)
            for (uint seed = 1; seed <= Seeds; seed++)
            {
                critter.npc_goal = 6;
                critter.ProjectileHeading = (ushort)heading;
                Globals(critter, 16, true, false);
                var r = new BorlandRandSource(seed);
                int turn = r.Rand() % 2 * 2 - 1;
                int expected = (byte)((((heading >> 5) + turn * 2 + 8) % 8 << 5) + r.Rand() % 0x20);
                Rng.Source = new BorlandRandSource(seed);
                BatchA.Call(t, "NPC_Goal6", critter);
                Diff($"stuck turn from 0x{heading:X2} seed {seed}", critter.ProjectileHeading, expected);
            }

            // 3. Sequence and frame from another sequence (0x20 in UW1, 0 in UW2) at frame 2.
            int other = uw1 ? 0x20 : 0;
            (string what, int dist, bool failed, int goal, int seq)[] seqs =
            {
                ("backing off", 2, false, 6, uw1 ? 7 : 1),     // uw1 AI.C:636-637, uw2 AI.C:606
                ("running", 16, false, 6, uw1 ? 0x2C : 1),     // uw1 AI.C:677-678, uw2 AI.C:645
                ("holding", 4, true, 9, uw1 ? 0 : 2),          // uw1 AI.C:647-648, uw2 AI.C:616
            };
            foreach (var s in seqs)
            {
                critter.npc_goal = (byte)s.goal;
                critter.npc_animation = (short)other;
                critter.AnimationFrame = 2;
                Globals(critter, s.dist, s.failed, false);
                Rng.Source = new BorlandRandSource(1);
                BatchA.Call(t, "NPC_Goal6", critter);
                Diff($"{s.what} sequence", critter.npc_animation, s.seq);
                Diff($"{s.what} frame", critter.AnimationFrame, uw1 ? 3 : 0);
            }
        }
        finally
        {
            Rng.Source = saved;
            p.zpos = savedZ;
        }
        c.Note($"critter 0x{_id:X}, speed {critterObjectDat.walkspeed(_id)}, run {critterObjectDat.run(_id)}: {_diffs.Count} of {_cases} differ");
        return Task.CompletedTask;
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        if (_diffs.Count == 0) return null;
        return $"fleeing differs from crit_flee on {_diffs.Count} of {_cases}: {string.Join("; ", _diffs.GetRange(0, System.Math.Min(5, _diffs.Count)))} ({(c.Game == "uw1" ? "uw1 AI.C:610-679" : "uw2 AI.C:579-647")})";
    }
}
