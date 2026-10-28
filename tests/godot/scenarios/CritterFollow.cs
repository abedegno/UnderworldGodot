using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// Goal 3, follow the target (UW1 only). critter_mv sends goal 3 to seg007_1798_3E4
/// (uw1/src/critter/AI.C:1277-1281, 211-247) once set_up_target has loaded the target's square and
/// distance (AI.C:1330-1349). Within about 1.4 squares (tdistsqr at most 2) the critter faces the
/// target (deltatotheta of tdx, tdy), plays its first attack sequence one frame on (SEQ_ATTACK1,
/// frame + 1 mod 4) and stands, at rate 4. Further than 8 squares (tdistsqr above 0x40) it jumps to
/// the square a quarter of the way back along the line from the target to itself, four squares
/// from the target (txpos + (myxpos - txpos) * 4 / cSqRt(tdistsqr)), onto the middle of that square
/// (fine x and y 4) at floor height. In between it heads for the target's square
/// (crit_head_for_loc, which sets that square as its destination, PATHFIND.C:1092-1100). critter_ai
/// then moves its time bin on by its rate (AI.C:1181).
///
/// The scenario finds a straight run of 13 open, empty squares of plain floor at one height on level 1, puts a
/// standing target critter at one end and three goal-3 critters aimed at it 1, 5 and 12 squares
/// away, and runs each one's AI step once, npc.NPCInitialProcess, as main.ProcessMobileObjects does.
///
/// UW2's goal 3 does nothing (uw2/src/critter/AI.C:1307-1308), as the port's does, so this is UW1
/// only. The port's goal 3 was an empty case (src/npc/npcai.cs:770).
/// </summary>
public sealed class CritterFollow : Scenario
{
    public override string Name => "critter-follow";
    public override string[] Games => new[] { "uw1" };
    public override string Cites => "underworld-exhumed uw1/src/critter/AI.C:211-247 (seg007_1798_3E4), 1181, 1277-1281, 1330-1349";

    string _problem;
    uwObject _target, _near, _mid, _far;
    int _x0, _y0, _dx, _dy, _floor;
    int _nearHeading, _nearFrame, _nearBin;
    int _nearAnim, _nearFrameAfter, _nearHeadingAfter, _nearSpeed, _nearRate, _nearBinAfter;
    int _midTX, _midTY, _midTZ, _midTile;
    int _farTile, _farTileY, _farX, _farY, _farZ, _farHomeX, _farInList;

    /// <summary>deltatotheta (uw1/src/critter/PATHFIND.C:1076-1086): the octant from a vector, as chars.</summary>
    static int DeltaToTheta(int x, int y)
    {
        sbyte cx = (sbyte)x, cy = (sbyte)y;
        sbyte dx = (sbyte)(cx << 1), dy = (sbyte)(cy << 1);
        if (cy > dx)
        {
            if (cx > -dy) return cy > -dx ? 0 : 7;
            return cx > dy ? 5 : 6;
        }
        if (cx > -dy) return cx > dy ? 2 : 1;
        return cy > -dx ? 3 : 4;
    }

    /// <summary>A run of 13 open squares of plain floor (no water or lava) at one height with no critter in
    /// them, along x or y, from (x0, y0) in steps of (dx, dy).</summary>
    static bool FindRun(out int x0, out int y0, out int dx, out int dy)
    {
        var map = UWTileMap.current_tilemap;
        foreach (var (ddx, ddy) in new[] { (1, 0), (0, 1) })
            for (y0 = 1; y0 + 12 * ddy < 63; y0++)
                for (x0 = 1; x0 + 12 * ddx < 63; x0++)
                {
                    int h = map.Tiles[x0, y0].floorHeight;
                    bool ok = true;
                    for (int i = 0; i <= 12 && ok; i++)
                    {
                        var t = map.Tiles[x0 + i * ddx, y0 + i * ddy];
                        ok = t.tileType == UWTileMap.TILE_OPEN && t.floorHeight == h
                            && TerrainDatLoader.GetTerrainTypeNo(t) == 0;
                        for (int o = t.indexObjectList, n = 0; ok && o != 0 && n < 1024; o = map.LevelObjects[o].next, n++)
                            ok = map.LevelObjects[o].majorclass != 1;
                    }
                    if (ok) { dx = ddx; dy = ddy; return true; }
                }
        x0 = y0 = dx = dy = -1;
        return false;
    }

    int SqX(int i) => _x0 + i * _dx;
    int SqY(int i) => _y0 + i * _dy;

    uwObject Spawn(int item, int i, int goal, int gtarg)
    {
        int tx = SqX(i), ty = SqY(i);
        var o = ObjectCreator.spawnObjectInTile(
            itemid: item, tileX: tx, tileY: ty, xpos: 3, ypos: 3, zpos: (short)(_floor << 3),
            WhichList: ObjectFreeLists.ObjectListType.MobileList, RenderImmediately: false);
        if (o == null) return null;
        ObjectCreator.InitialiseCritter(o);
        o.npc_xhome = (short)tx;
        o.npc_yhome = (short)ty;
        o.quality = (short)tx;
        o.owner = (short)ty;
        o.zpos = (short)(_floor << 3);
        o.npc_goal = (byte)goal;
        o.npc_gtarg = (byte)gtarg;
        o.npc_level = 0;
        o.npc_attitude = 2;
        o.IsAlly = 0;
        o.npc_hp = 0xC8;
        o.npc_whoami = 0;
        o.UnkBit_0XA_Bit7 = 0;
        o.UnkBit_0X13_Bit0to6 = 0;
        o.TargetTileX = (short)tx;
        o.TargetTileY = (short)ty;
        o.npc_animation = npc.ANIMATION_IDLE;
        o.AnimationFrame = 0;
        return o;
    }

    public override Task Setup(ScenarioContext c)
    {
        if (!FindRun(out _x0, out _y0, out _dx, out _dy)) { _problem = "no run of 13 open plain squares at one height without critters on level 1"; return Task.CompletedTask; }
        _floor = UWTileMap.current_tilemap.Tiles[_x0, _y0].floorHeight;
        int id = BatchA.PickCritter(BatchA.Walker);
        if (id < 0) { _problem = "no walking creature type"; return Task.CompletedTask; }
        _target = Spawn(id, 0, 0, 0);
        if (_target == null) { _problem = "the target critter could not be made"; return Task.CompletedTask; }
        _near = Spawn(id, 1, 3, _target.index);
        _mid = Spawn(id, 5, 3, _target.index);
        _far = Spawn(id, 12, 3, _target.index);
        if (_near == null || _mid == null || _far == null) { _problem = "the following critters could not be made"; return Task.CompletedTask; }
        // Face one octant short of the target, so constrain_movement's one-octant turn limit
        // (AI.C:1183, PATHFIND.C) still lets the new heading through.
        _nearHeading = DeltaToTheta(-8 * _dx, -8 * _dy);
        _near.heading = (short)((_nearHeading + 7) % 8);
        _near.npc_heading = 0;
        _nearFrame = _near.AnimationFrame;
        _nearBin = _near.NextFrame_0XA_Bit0123;
        c.Note($"creature 0x{id:X}; run from ({_x0}, {_y0}) in steps of ({_dx}, {_dy}) at floor {_floor}; target index {_target.index}, followers {_near.index}, {_mid.index}, {_far.index}");
        return Task.CompletedTask;
    }

    public override Task Act(ScenarioContext c)
    {
        if (_problem != null) return Task.CompletedTask;
        npc.NPCInitialProcess(_near);
        _nearAnim = _near.npc_animation;
        _nearFrameAfter = _near.AnimationFrame;
        _nearHeadingAfter = _near.heading;
        _nearSpeed = _near.UnkBit_0X13_Bit0to6;
        _nearRate = _near.Projectile_Speed;
        _nearBinAfter = _near.NextFrame_0XA_Bit0123;

        npc.NPCInitialProcess(_mid);
        _midTX = _mid.TargetTileX; _midTY = _mid.TargetTileY; _midTZ = _mid.TargetZHeight;
        _midTile = _mid.tileX + _mid.tileY * 64;

        npc.NPCInitialProcess(_far);
        _farTile = _far.tileX; _farTileY = _far.tileY;
        _farX = _far.xpos; _farY = _far.ypos; _farZ = _far.zpos;
        _farHomeX = _far.npc_xhome;
        _farInList = 0;
        var tile = UWTileMap.current_tilemap.Tiles[_farTile, _farTileY];
        for (int i = tile.indexObjectList, n = 0; i != 0 && n < 1024; i = UWTileMap.current_tilemap.LevelObjects[i].next, n++)
            if (i == _far.index) _farInList = 1;
        c.Note($"near: seq {_nearAnim} frame {_nearFrameAfter} heading {_nearHeadingAfter} speed {_nearSpeed} rate {_nearRate} bin {_nearBin}->{_nearBinAfter}; middle: destination ({_midTX}, {_midTY}, {_midTZ}); far: square ({_farTile}, {_farTileY}) fine ({_farX}, {_farY}) z {_farZ}");
        return Task.CompletedTask;
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        var k = new Checks();
        // tdistsqr 1: face, attack sequence, stand (AI.C:219-226).
        k.Equal("near follower's sequence (SEQ_ATTACK1)", 1, _nearAnim, "uw1 AI.C:222");
        k.Equal("near follower's frame (frame + 1 mod 4)", (_nearFrame + 1) % 4, _nearFrameAfter, "uw1 AI.C:223");
        k.Equal("near follower's heading (deltatotheta to the target)", _nearHeading, _nearHeadingAfter, "uw1 AI.C:225");
        k.Equal("near follower's speed", 0, _nearSpeed, "uw1 AI.C:224");
        k.Equal("near follower's rate", 4, _nearRate, "uw1 AI.C:226");
        k.Equal("near follower's time bin (moved on by its rate)", (_nearBin + 4) % 16, _nearBinAfter, "uw1 AI.C:1181");
        // tdistsqr 25: head for the target's square (AI.C:241-245).
        k.Equal("middle follower's destination x (the target's square)", SqX(0), _midTX, "uw1 AI.C:245, PATHFIND.C:1092-1100");
        k.Equal("middle follower's destination y", SqY(0), _midTY, "uw1 AI.C:245");
        k.Equal("middle follower's destination height (the target's)", _floor, _midTZ, "uw1 AI.C:245");
        k.Equal("middle follower's square (no jump)", SqX(5) + SqY(5) * 64, _midTile, "uw1 AI.C:227");
        // tdistsqr 144: cSqRt 12, a jump to the target's square + 12 * 4 / 12 = 4 squares on (AI.C:227-240).
        k.Equal("far follower's square x after the jump", SqX(4), _farTile, "uw1 AI.C:229, 235");
        k.Equal("far follower's square y after the jump", SqY(4), _farTileY, "uw1 AI.C:230, 236");
        k.Equal("far follower's home x (its square)", SqX(4), _farHomeX, "uw1 AI.C:235");
        k.Equal("far follower's fine x", 4, _farX, "uw1 AI.C:237");
        k.Equal("far follower's fine y", 4, _farY, "uw1 AI.C:238");
        k.Equal("far follower's z (floor height << 3)", _floor << 3, _farZ, "uw1 AI.C:239");
        k.Equal("far follower in its new square's object list", 1, _farInList, "uw1 AI.C:233-234");
        return k.Result;
    }
}
