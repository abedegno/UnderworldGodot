using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// crit_offense_find_target, the attacking critter's move towards its target (uw1 AI.C:464-500,
/// uw2 AI.C:452-484), with target_found (uw1 AI.C:820-856, uw2 AI.C:783-819) and crit_offense's
/// give-up test (uw1 AI.C:377-383, uw2 AI.C:365-371). The port's are npc.AttackGoalSearchForTarget,
/// SearchForGoalTarget and the give-up test in NPC_Goal5_Attack.
///
/// When the destination differs from the target's square in x or in y, rand() % 8 == 0 checks the
/// target (target_found): 0 sets the destination there (set_loc); in UW1 1 drops the goal with
/// b19_1 cleared, and 2 drops it with b19_1 set when rand() % 2 == 0, else sets the destination; in
/// UW2 0, or 2 with rand() % 2 == 0, sets the destination, else the goal is dropped with b19_1 set
/// only for 1. target_found returns 0 when heard / 4 &gt; dist, 2 when heard * 4 &gt; dist, else 1
/// (not seen). Then it returns on the target's own square (x == myxpos, y == myypos, height within
/// 4); else it heads for the square (crit_head_for_loc) when how &gt; 1 and how * how &lt; tdistsqr, or
/// how * how * 64 &lt; tdisttsqr, or how &lt;= 1 and the height differs by 4 or more, and if that leaves
/// b18_6 (gave up) set it drops the goal and clears b19_1. crit_offense gives up and goes back to
/// guarding when range * range * 4 &lt; homedist (range is the high nibble of CRITTER byte 0x1C).
///
/// The scenario calls the port's functions directly on a critter beside the player, with the AI's
/// globals (squares, heights, distances) set for each case as NPCInitialProcess and
/// GetDistancesToGTarg would, and the player's noise and visibility set so that target_found's
/// heard is 4 and nothing is seen. The draws come from a scripted source that logs each one's
/// modulus, so the order and form of rand() % n is checked. It asserts the destination, the goal,
/// b19_0, b19_1 and the draws per game. Where crit_head_for_loc runs on a real square only the
/// destination and the first draw are checked.
/// </summary>
public sealed class AttackFindTarget : Scenario
{
    public override string Name => "attack-find-target";
    public override string Cites => "underworld-exhumed uw1/src/critter/AI.C:377-383, 464-500, 820-856, 1410-1420, uw2 AI.C:365-371, 452-484, 783-819, 1406-1416, uw1 PATHFIND.C:1090-1135";

    /// <summary>Raw values queued by the case, then 1; logs the modulus of each Next and 0 for a raw Rand.</summary>
    sealed class Scripted : IRandSource
    {
        readonly Queue<int> _q;
        public readonly List<int> Log = new();
        public Scripted(params int[] raw) { _q = new Queue<int>(raw); }
        int Take() => _q.Count > 0 ? _q.Dequeue() : 1;
        public int Rand() { Log.Add(0); return Take(); }
        public int Next(int max) { Log.Add(max); return max <= 0 ? 0 : Take() % max; }
    }

    sealed class Case
    {
        public string Name, Cite;
        public bool Real;                 // the critter's real square, so crit_head_for_loc may run
        public int Tx, Ty, Dz, DestX, DestY, How = 1, TDist, TDistT, Quiet = 15;
        public int B19_1, OldGoal, B18_6, DestZ = -1;
        public int[] Raw = Array.Empty<int>();
        public bool OnlyFirstDraw;
        // expected
        public int EDestX, EDestY, EGoal = 5, EB19_0 = 1, EB19_1;
        public int[] EDraws;
        public bool CheckFlags = true;
        // got
        public int GDestX, GDestY, GGoal, GB19_0, GB19_1;
        public int[] GDraws;
    }

    const int Cx = 10, Cy = 20, Floor = 2;

    string _problem;
    int _id = -1, _quiet = -1, _range = -1, _morale = -1;
    readonly List<Case> _cases = new();
    int _giveUpAt = -1, _giveUpPast = -1;

    static bool Uw2 => UWClass._RES == UWClass.GAME_UW2;

    public override Task Setup(ScenarioContext c)
    {
        _id = BatchA.PickCritter(i => BatchA.Walker(i)
            && critterObjectDat.NPCPower_0x2DBits1To7(i) == 0
            && critterObjectDat.weaponloot(i, 0) >> 4 != 1
            && Enumerable.Range(1, 15).Any(q => critterObjectDat.noisedetectionrange(i) * q / 16 == 2)
            && critterObjectDat.maybemorale(i) != 2 * critterObjectDat.TravelRange(i));
        if (_id < 0) { _problem = "no walking critter that casts nothing, shoots nothing and has hearing 3 or more"; return Task.CompletedTask; }
        _quiet = Enumerable.Range(1, 15).First(q => critterObjectDat.noisedetectionrange(_id) * q / 16 == 2);
        _range = critterObjectDat.TravelRange(_id);
        _morale = critterObjectDat.maybemorale(_id);
        playerdat.play_hp = 200; playerdat.playerObject.npc_hp = 200;
        return Task.CompletedTask;
    }

    void BuildCases(int rx, int ry, int px, int py)
    {
        // target_found's heard is (hearing * quiet / 16)^2 = 4: 0 under dist 1, 2 under dist 16, else 1
        var u1 = !Uw2;
        _cases.Add(new Case { Name = "destination already the target's square: no draw", Cite = "uw1 AI.C:468, uw2 AI.C:456",
            Tx = Cx + 2, Ty = Cy, DestX = Cx + 2, DestY = Cy, B19_1 = 1,
            EDestX = Cx + 2, EDestY = Cy, EB19_1 = 1, EDraws = new int[0] });
        _cases.Add(new Case { Name = "destination differs only in y: rand() % 8 drawn", Cite = "uw1 AI.C:468, uw2 AI.C:456",
            Tx = Cx + 2, Ty = Cy, DestX = Cx + 2, DestY = Cy + 1, B19_1 = 1, Raw = new[] { 1 },
            EDestX = Cx + 2, EDestY = Cy + 1, EB19_1 = 1, EDraws = new[] { 8 } });
        _cases.Add(new Case { Name = "target_found 0 (heard): destination set, on the target's square", Cite = "uw1 AI.C:471-473, 838, uw2 AI.C:458-459, 801",
            Tx = Cx, Ty = Cy, DestX = Cx + 1, DestY = Cy + 1, B19_1 = 1, Raw = new[] { 0 },
            EDestX = Cx, EDestY = Cy, EB19_1 = 1, EDraws = new[] { 8 } });
        _cases.Add(new Case { Name = "target_found 1 (lost): goal dropped", Cite = "uw1 AI.C:474-478, uw2 AI.C:460-464",
            Tx = Cx + 2, Ty = Cy, DestX = Cx + 3, DestY = Cy + 1, Quiet = 0, B19_1 = u1 ? 1 : 0, Raw = new[] { 0 },
            EDestX = Cx + 3, EDestY = Cy + 1, EGoal = 2, EB19_0 = 0, EB19_1 = u1 ? 0 : 1, EDraws = new[] { 8 } });
        _cases.Add(new Case { Name = "target_found 2 (heard faintly), rand() % 2 == 0", Cite = "uw1 AI.C:479-484, uw2 AI.C:458-459",
            Tx = Cx + 2, Ty = Cy, DestX = Cx + 3, DestY = Cy + 1, B19_1 = u1 ? 0 : 1, Raw = new[] { 0, 0 },
            EDestX = u1 ? Cx + 3 : Cx + 2, EDestY = u1 ? Cy + 1 : Cy, EGoal = u1 ? 2 : 5, EB19_0 = u1 ? 0 : 1, EB19_1 = u1 ? 1 : 1, EDraws = new[] { 8, 2 } });
        _cases.Add(new Case { Name = "target_found 2 (heard faintly), rand() % 2 == 1", Cite = "uw1 AI.C:485-486, uw2 AI.C:460-464",
            Tx = Cx + 2, Ty = Cy, DestX = Cx + 3, DestY = Cy + 1, B19_1 = 1, Raw = new[] { 0, 1 },
            EDestX = u1 ? Cx + 2 : Cx + 3, EDestY = u1 ? Cy : Cy + 1, EGoal = u1 ? 5 : 2, EB19_0 = u1 ? 1 : 0, EB19_1 = u1 ? 1 : 0, EDraws = new[] { 8, 2 } });
        _cases.Add(new Case { Name = "target_found at dist == heard / 4 is 2, not 0", Cite = "uw1 AI.C:838, 853, uw2 AI.C:801, 816",
            Tx = Cx + 1, Ty = Cy, DestX = Cx + 3, DestY = Cy + 1, B19_1 = u1 ? 0 : 1, Raw = new[] { 0, 0 },
            EDestX = u1 ? Cx + 3 : Cx + 1, EDestY = u1 ? Cy + 1 : Cy, EGoal = u1 ? 2 : 5, EB19_0 = u1 ? 0 : 1, EB19_1 = 1, EDraws = new[] { 8, 2 } });
        _cases.Add(new Case { Name = "target_found at dist == heard * 4 is 1, not 2", Cite = "uw1 AI.C:853-855, uw2 AI.C:816-818",
            Tx = Cx + 4, Ty = Cy, DestX = Cx + 3, DestY = Cy + 1, B19_1 = u1 ? 1 : 0, Raw = new[] { 0 },
            EDestX = Cx + 3, EDestY = Cy + 1, EGoal = 2, EB19_0 = 0, EB19_1 = u1 ? 0 : 1, EDraws = new[] { 8 } });
        _cases.Add(new Case { Name = "on the target's square (x != y) 9 fine units away: returns", Cite = "uw1 AI.C:490-491, uw2 AI.C:466-467",
            Tx = Cx, Ty = Cy, DestX = Cx + 1, DestY = Cy + 1, TDistT = 98, B19_1 = 1, Raw = new[] { 1 },
            EDestX = Cx + 1, EDestY = Cy + 1, EB19_1 = 1, EDraws = new[] { 8 } });
        _cases.Add(new Case { Name = "crit_head_for_loc leaves b18_6 set: goal dropped", Cite = "uw1 AI.C:494-497, uw2 AI.C:470-473, uw1 PATHFIND.C:1120-1135",
            Tx = Cx, Ty = Cy, Dz = 4, DestX = Cx, DestY = Cy, DestZ = Floor + 4, B18_6 = 1, OldGoal = 4, B19_1 = 1,
            EDestX = Cx, EDestY = Cy, EGoal = 4, EB19_0 = 1, EB19_1 = 0, EDraws = new int[0] });
        // the head-in test, on the real squares: does the destination become the player's square?
        void Head(string name, int how, int tdist, int tdistt, int dz, bool heads) =>
            _cases.Add(new Case { Name = name, Cite = "uw1 AI.C:492-494, uw2 AI.C:468-470", Real = true,
                Tx = px, Ty = py, Dz = dz, DestX = rx, DestY = ry, How = how, TDist = tdist, TDistT = tdistt, Raw = new[] { 1 },
                OnlyFirstDraw = true, CheckFlags = false,
                EDestX = heads ? px : rx, EDestY = heads ? py : ry, EDraws = new[] { 8 } });
        Head("how 4, tdistsqr 17: heads in", 4, 17, 0, 0, true);
        Head("how 4, tdistsqr 16, tdisttsqr 1024: stays", 4, 16, 1024, 0, false);
        Head("how 4, tdistsqr 16, tdisttsqr 1025: heads in", 4, 16, 1025, 0, true);
        Head("how 4, tdistsqr 16, tdisttsqr 1024, height 4 apart: stays", 4, 16, 1024, 4, false);
        Head("how 1, tdisttsqr 64: stays", 1, 1, 64, 0, false);
        Head("how 1, tdisttsqr 65: heads in", 1, 1, 65, 0, true);
        Head("how 1, tdisttsqr 64, height 4 apart: heads in", 1, 1, 64, 4, true);
    }

    void Run(uwObject critter, Case k)
    {
        var t = typeof(npc);
        int cx = k.Real ? critter.npc_xhome : Cx, cy = k.Real ? critter.npc_yhome : Cy;
        int floor = k.Real ? critter.zpos >> 3 : Floor;
        critter.npc_goal = 5;
        critter.npc_gtarg = 1;
        critter.npc_level = (byte)k.OldGoal;
        critter.npc_attitude = 0;
        critter.TargetTileX = (short)k.DestX;
        critter.TargetTileY = (short)k.DestY;
        critter.TargetZHeight = (short)(k.DestZ >= 0 ? k.DestZ : floor + k.Dz);
        critter.UnkBit_0x18_5 = 0;
        critter.UnkBit_0x18_6 = (short)k.B18_6;
        critter.UnkBit_0X15_Bit7 = 0;
        critter.UnkBit_0x19_0_likelyincombat = 1;
        critter.UnkBit_0x19_1 = (short)k.B19_1;
        BatchA.Set(t, "IsNPCActive_dseg_67d6_2234", true);
        npc.SpecialMotionHandler = MotionHandler.LandNPCMotionHandler;  // a walker's, as NPCInitialProcess sets it
        BatchA.Set(t, "currObj_XHome", cx);
        BatchA.Set(t, "currObj_YHome", cy);
        BatchA.Set(t, "currObj_FloorHeight", floor);
        BatchA.Set(t, "currObjXCoordinate", (cx << 3) + critter.xpos);
        BatchA.Set(t, "currObjYCoordinate", (cy << 3) + critter.ypos);
        BatchA.Set(t, "currentGoalTarget", playerdat.playerObject);
        BatchA.Set(t, "currentGTargXHome", k.Tx);
        BatchA.Set(t, "currentGTargYHome", k.Ty);
        BatchA.Set(t, "currentGTargFloorHeight", floor + k.Dz);
        BatchA.Set(t, "currentGTargSquaredDistanceByTiles", k.TDist);
        BatchA.Set(t, "currentGTargSquaredDistanceByCoordinates", k.TDistT);
        playerdat.PlayerQuietness = k.Quiet == 15 ? _quiet : k.Quiet;
        playerdat.PlayerVisibility = 0;
        var src = new Scripted(k.Raw);
        var saved = Rng.Source;
        Rng.Source = src;
        try
        {
            BatchA.Call(t, "AttackGoalSearchForTarget", critter, k.Tx, k.Ty, k.How);
        }
        finally { Rng.Source = saved; }
        k.GDestX = critter.TargetTileX; k.GDestY = critter.TargetTileY;
        k.GGoal = critter.npc_goal; k.GB19_0 = critter.UnkBit_0x19_0_likelyincombat; k.GB19_1 = critter.UnkBit_0x19_1;
        k.GDraws = src.Log.ToArray();
    }

    /// <summary>crit_offense out of reach with nothing to shoot, at homedist; the goal it leaves.</summary>
    int GiveUp(uwObject critter, int homedist)
    {
        var t = typeof(npc);
        critter.npc_goal = 5; critter.npc_gtarg = 1; critter.npc_level = 4; critter.UnkBit_0x19_5 = 0;
        critter.TargetTileX = Cx + 5; critter.TargetTileY = Cy;
        critter.UnkBit_0x18_6 = 0;
        int dx = 2 * _range, dy = homedist - dx * dx;
        BatchA.Set(t, "IsNPCActive_dseg_67d6_2234", true);
        BatchA.Set(t, "currObj_XHome", Cx);
        BatchA.Set(t, "currObj_YHome", Cy);
        BatchA.Set(t, "currObj_FloorHeight", Floor);
        BatchA.Set(t, "currObjQualityX", Cx + dx);
        BatchA.Set(t, "currObjOwnerY", Cy + dy);
        BatchA.Set(t, "currentGoalTarget", playerdat.playerObject);
        BatchA.Set(t, "currentGTargXHome", Cx + 5);
        BatchA.Set(t, "currentGTargYHome", Cy);
        BatchA.Set(t, "currentGTargFloorHeight", Floor);
        BatchA.Set(t, "currentGTargXVector", 0x11);
        BatchA.Set(t, "currentGTargYVector", 0);
        BatchA.Set(t, "currentGTargSquaredDistanceByTiles", 0);
        BatchA.Set(t, "currentGTargSquaredDistanceByCoordinates", 0);
        var saved = Rng.Source;
        Rng.Source = new Scripted();
        try { BatchA.Call(t, "NPC_Goal5_Attack", critter); }
        finally { Rng.Source = saved; }
        return critter.npc_goal;
    }

    public override async Task Act(ScenarioContext c)
    {
        if (_problem != null) return;
        c.Note(await BatchA.CameraOnPlayer(c));
        var p = playerdat.playerObject;
        int dx = p.tileX <= 6 ? 1 : -1;
        var critter = BatchA.SpawnCritter(_id, dx, 0);
        if (critter == null) { _problem = "no critter could be made"; return; }
        int quietSaved = playerdat.PlayerQuietness, visSaved = playerdat.PlayerVisibility;
        try
        {
            BuildCases(critter.npc_xhome, critter.npc_yhome, p.npc_xhome, p.npc_yhome);
            foreach (var k in _cases) Run(critter, k);
            _giveUpAt = GiveUp(critter, 4 * _range * _range);
            _giveUpPast = GiveUp(critter, 4 * _range * _range + 1);
        }
        finally { playerdat.PlayerQuietness = quietSaved; playerdat.PlayerVisibility = visSaved; }
        c.Note($"critter 0x{_id:X}, hearing {critterObjectDat.noisedetectionrange(_id)}, player noise {_quiet}; range {_range}, low nibble {_morale}");
        foreach (var k in _cases)
            c.Note($"{k.Name}: dest ({k.GDestX}, {k.GDestY}), goal {k.GGoal}, b19 {k.GB19_0}/{k.GB19_1}, draws [{string.Join(",", k.GDraws)}]");
        c.Note($"give-up at homedist {4 * _range * _range}: goal {_giveUpAt}; at {4 * _range * _range + 1}: goal {_giveUpPast}");
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        if (_cases.Count == 0) return "precondition: no case ran";
        var k = new Checks();
        string g = Uw2 ? "uw2" : "uw1";
        foreach (var x in _cases)
        {
            string cite = x.Cite;
            k.Equal(x.Name + ": destination x", x.EDestX, x.GDestX, cite);
            k.Equal(x.Name + ": destination y", x.EDestY, x.GDestY, cite);
            if (x.CheckFlags)
            {
                k.Equal(x.Name + ": goal", x.EGoal, x.GGoal, cite);
                k.Equal(x.Name + ": b19_0", x.EB19_0, x.GB19_0, cite);
                k.Equal(x.Name + ": b19_1", x.EB19_1, x.GB19_1, cite);
            }
            var draws = x.OnlyFirstDraw ? x.GDraws.Take(1).ToArray() : x.GDraws;
            string want = string.Join(",", x.EDraws), got = string.Join(",", draws);
            k.True(want == got, $"{x.Name}: draws (moduli) expected [{want}], got [{got}] ({cite})");
        }
        k.Equal("crit_offense at homedist range*range*4: keeps attacking", 5, _giveUpAt, g + (Uw2 ? " AI.C:365-371" : " AI.C:377-383"));
        k.Equal("crit_offense at homedist range*range*4 + 1: back to guarding", 4, _giveUpPast, g + (Uw2 ? " AI.C:365-371" : " AI.C:377-383"));
        return k.Result;
    }
}
