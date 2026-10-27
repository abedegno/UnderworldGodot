using System.Collections.Generic;
using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// The castle's daily round includes whoami 0x8F (UW2). The original's move_folks_around runs
/// maybe_go_hang_out for whoami 0x81 to 0x8F inclusive, then Syria (uw2/src/event/WORLDEV.C:1160-1172),
/// and 0x8F has its own entry in where_shall_we_hang_out's xs and ys tables, the last, (0x19, 0x22)
/// (uw2/src/critter/CRITTIME.C:681-694). With XC_CASTLE 0 every castle NPC takes loc 0, its own spot
/// (CRITTIME.C:671).
///
/// The port's loop stopped before 0x8F (src/traps/a_hack_trap_castleschedule.cs:56). It skips 0x81,
/// for which DOS reads below its tables (CRITTIME.C:690-693), an original bug it does not copy.
///
/// The scenario gives an active mobile whoami 0x8F (or uses one that has it), sets quest 109 and
/// XC_CASTLE 0, clears the NPC's goal and target, runs the hack and checks the NPC was sent to (0x19, 0x22)
/// with goal 1 (goto).
/// </summary>
public sealed class CastleScheduleWhoami8F : Scenario
{
    public override string Name => "castle-schedule-whoami-8f";
    public override string[] Games => new[] { "uw2" };
    public override string Cites => "underworld-exhumed uw2/src/event/WORLDEV.C:1160-1172; uw2/src/critter/CRITTIME.C:646-694";

    string _problem;
    uwObject _npc;
    int _goal, _x, _y;

    public override Task Setup(ScenarioContext c)
    {
        var map = UWTileMap.current_tilemap;
        uwObject spare = null;
        for (int i = 0; i < map.NoOfActiveMobiles && _npc == null; i++)
        {
            int index = map.GetActiveMobileAtIndex(i);
            if (index == 0 || index >= 256) continue;
            var o = map.LevelObjects[index];
            if (o.majorclass != 1) continue;
            if (o.npc_whoami == 0x8F) _npc = o;
            else if (spare == null && o.npc_whoami == 0) spare = o;
        }
        _npc ??= spare;
        if (_npc == null) { _problem = "no active mobile to use"; return Task.CompletedTask; }
        _npc.npc_whoami = 0x8F;
        _npc.npc_goal = 0;
        _npc.quality = 0; _npc.owner = 0;
        playerdat.SetQuest(109, 1);
        playerdat.SetXClock(1, 0);
        playerdat.SetQuest(112, 0);
        return Task.CompletedTask;
    }

    public override Task Act(ScenarioContext c)
    {
        if (_problem != null) return Task.CompletedTask;
        a_hack_trap_castleschedule.Activate();
        _goal = _npc.npc_goal; _x = _npc.quality; _y = _npc.owner;
        c.Note($"whoami 0x8F (object {_npc.index}): goal {_goal}, destination ({_x}, {_y})");
        return Task.CompletedTask;
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        var k = new Checks();
        k.Equal("whoami 0x8F's goal after the castle hack", 1, _goal, "uw2 WORLDEV.C:1166, the loop includes 0x8F");
        k.Equal("whoami 0x8F's destination x", 0x19, _x, "uw2 CRITTIME.C:681-682, xs[13]");
        k.Equal("whoami 0x8F's destination y", 0x22, _y, "uw2 CRITTIME.C:683-684, ys[13]");
        return k.Result;
    }
}

/// <summary>
/// The create object trap puts its copy with put_at (UW1 and UW2). UseTrap, TRAP_CREATE_OBJECT, sets
/// stay_centered, calls put_at((x &lt;&lt; 3) + finex, (y &lt;&lt; 3) + finey, z, obj, 4, 0) with the template's
/// fine position and height, then clears it (uw1/src/event/TRIGGER.C:314-317, uw2/src/event/TRIGGER.C:569-572).
/// put_at calls drop_around_place, which with stay_centered tries the exact point first, then random
/// points within 4 eighths each way, x - 4 + rand() % 9, at the same z; it tests each with can_place and
/// adds the object at the END of the square's list (Obj_AddEnd), giving a mobile its home square
/// (uw1/src/motion/COLLIDE.C:645-706, uw2/src/motion/COLLIDE.C:631-692).
///
/// The port put the copy at the head of the square's list, and looked for a free spot only against
/// mobiles standing on the exact fine position, in the same square (src/traps/a_create_object_trap.cs:186-210).
///
/// The scenario puts a static object in an open square next to the player, so the list is not empty,
/// and a critter template at fine (2, 5) on the floor, links a quality 0 create object trap to it and
/// fires the trap twice. The first copy must be at (2, 5) and the floor height, last in the square's list,
/// with its home square set. The second, with the first standing on that point, must be elsewhere within
/// 4 eighths of it, at the same height and last in its square's list. stay_centered must be clear after.
/// </summary>
public sealed class CreateObjectTrapPutAt : Scenario
{
    const int TrapItem = 0x187, FineX = 2, FineY = 5, Range = 4;

    public override string Name => "create-object-trap-put-at";
    public override string Cites => "underworld-exhumed uw1/src/event/TRIGGER.C:297-317, uw1/src/motion/COLLIDE.C:645-706; uw2/src/event/TRIGGER.C:523-572, uw2/src/motion/COLLIDE.C:631-692";

    string _problem;
    int _x, _y, _z;
    uwObject _trap, _template;
    readonly List<(uwObject o, bool last)> _copies = new();
    bool _stayCenteredAfter;

    public override Task Setup(ScenarioContext c)
    {
        (_x, _y) = BatchB.OpenSquareNearPlayer();
        _z = UWTileMap.current_tilemap.Tiles[_x, _y].floorHeight << 3;
        if (BatchB.SpawnStatic(0xB3, _x, _y) == null) { _problem = "no static object could be made"; return Task.CompletedTask; }
        int id = BatchB.FindCritter(_ => true);
        int ci = id < 0 ? 0 : ObjectCreator.PrepareNewObject(id, ObjectFreeLists.ObjectListType.MobileList);
        int tr = ObjectCreator.PrepareNewObject(TrapItem);
        if (ci == 0 || tr == 0) { _problem = "the template or the trap could not be made"; return Task.CompletedTask; }
        _template = UWTileMap.current_tilemap.LevelObjects[ci];
        _template.xpos = FineX; _template.ypos = FineY; _template.zpos = (short)_z;
        _trap = UWTileMap.current_tilemap.LevelObjects[tr];
        _trap.is_quant = 0; _trap.link = (short)ci; _trap.quality = 0;
        return Task.CompletedTask;
    }

    static List<int> Mobiles()
    {
        var map = UWTileMap.current_tilemap;
        var all = new List<int>();
        for (int i = 0; i < map.NoOfActiveMobiles; i++) all.Add(map.GetActiveMobileAtIndex(i));
        return all;
    }

    /// <summary>The objects in square x, y, in list order.</summary>
    static List<int> ListOf(int x, int y)
    {
        var map = UWTileMap.current_tilemap;
        var list = new List<int>();
        for (int i = map.Tiles[x, y].indexObjectList, guard = 0; i != 0 && guard < 1024; guard++)
        {
            list.Add(i);
            i = map.LevelObjects[i].next;
        }
        return list;
    }

    uwObject FindNewCopy(HashSet<int> before)
    {
        for (int x = _x - 1; x <= _x + 1; x++)
        for (int y = _y - 1; y <= _y + 1; y++)
        {
            if (!UWTileMap.ValidTile(x, y)) continue;
            foreach (int i in ListOf(x, y))
            {
                var o = UWTileMap.current_tilemap.LevelObjects[i];
                if (!o.IsStatic && o.item_id == _template.item_id && !before.Contains(i)) return o;
            }
        }
        return null;
    }

    HashSet<int> Existing()
    {
        var set = new HashSet<int>();
        for (int x = _x - 1; x <= _x + 1; x++)
        for (int y = _y - 1; y <= _y + 1; y++)
            if (UWTileMap.ValidTile(x, y)) foreach (int i in ListOf(x, y)) set.Add(i);
        return set;
    }

    public override Task Act(ScenarioContext c)
    {
        if (_problem != null) return Task.CompletedTask;
        for (uint t = 1; t <= 2; t++)
        {
            var before = Existing();
            BatchB.Seed(t * 7919u);
            trap.ActivateTrap(0, _trap, null, _x, _y, UWTileMap.current_tilemap.LevelObjects);
            var o = FindNewCopy(before);
            if (o == null) { _problem = $"firing {t} made no copy within a square of ({_x}, {_y})"; break; }
            var list = ListOf(o.tileX, o.tileY);
            _copies.Add((o, list.Count > 0 && list[^1] == o.index));
            c.Note($"copy {t}: object {o.index} at ({o.xpos},{o.ypos},{o.zpos}) in ({o.tileX},{o.tileY}), home ({o.npc_xhome},{o.npc_yhome}), list [{string.Join(",", list)}]");
        }
        _stayCenteredAfter = motion.StayCentered;
        return Task.CompletedTask;
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        var k = new Checks();
        var (first, firstLast) = _copies[0];
        k.True(first.tileX == _x && first.tileY == _y && first.xpos == FineX && first.ypos == FineY && first.zpos == _z,
            $"first copy expected at ({FineX},{FineY},{_z}) in ({_x},{_y}), got ({first.xpos},{first.ypos},{first.zpos}) in ({first.tileX},{first.tileY}) (stay_centered tries the exact point first: COLLIDE.C drop_around_place)");
        k.True(firstLast, "first copy expected at the end of its square's list (Obj_AddEnd: uw1 COLLIDE.C:696, uw2 COLLIDE.C:682)");
        k.True(first.npc_xhome == first.tileX && first.npc_yhome == first.tileY, $"first copy's home expected ({first.tileX},{first.tileY}), got ({first.npc_xhome},{first.npc_yhome}) (SET_HOMEX/Y in drop_around_place)");
        var (second, secondLast) = _copies[1];
        int cx = (_x << 3) + FineX, cy = (_y << 3) + FineY;
        int sx = (second.tileX << 3) + second.xpos, sy = (second.tileY << 3) + second.ypos;
        k.True(!(sx == cx && sy == cy), $"second copy expected off the occupied point, got ({second.xpos},{second.ypos}) in ({second.tileX},{second.tileY}) (can_place refuses it)");
        k.True(System.Math.Abs(sx - cx) <= Range && System.Math.Abs(sy - cy) <= Range && second.zpos == _z,
            $"second copy expected within {Range} eighths of the point at z {_z}, got offset ({sx - cx},{sy - cy}) z {second.zpos} (x - range + rand() % (range * 2 + 1), z kept)");
        k.True(secondLast, "second copy expected at the end of its square's list (Obj_AddEnd)");
        k.True(!_stayCenteredAfter, "stay_centered expected clear after the trap (uw1 TRIGGER.C:317, uw2 TRIGGER.C:572)");
        return k.Result;
    }
}

/// <summary>
/// UW1's set-variable trap with z 0 works on quest bit d itself: op 5 toggles, op 1 clears, anything
/// else sets player-&gt;quests bit 1L &lt;&lt; d, where d = (quality &lt;&lt; 5 | owner) &lt;&lt; 3 | finey
/// (uw1/src/event/TRIGGER.C:229-243). The long shift helper shifts by CL, the low byte of d, and a
/// count of 32 to 47 gives a mask of 0 on every CPU (F_LXLSH@, seg005:36E4; called with
/// mov cl, byte ptr [var_1A] in ovr153).
///
/// The port changed quest 1 &lt;&lt; d (src/traps/a_set_variable_trap.cs:51), so d 5 set quest 32, a quest byte.
///
/// No known trap in the game does this, so the scenario calls a_set_variable_trap.Activate directly
/// with d 5 (set, clear, toggle twice), d 0x105 (CL 5) and d 40 (no change), and checks quest 5 and
/// that quests 32 to 35 are untouched.
/// </summary>
public sealed class Uw1SetVariableQuestBit : Scenario
{
    const int SetVariable = 0x18D;

    public override string Name => "uw1-set-variable-quest-bit";
    public override string[] Games => new[] { "uw1" };
    public override string Cites => "underworld-exhumed uw1/src/event/TRIGGER.C:229-243";

    string _problem;
    uwObject _t;
    readonly List<(string what, int expected, int got)> _steps = new();
    readonly int[] _bytesBefore = new int[4];
    readonly int[] _bytesAfter = new int[4];

    public override Task Setup(ScenarioContext c)
    {
        int slot = ObjectCreator.PrepareNewObject(SetVariable);
        if (slot == 0) { _problem = "no set-variable trap could be made"; return Task.CompletedTask; }
        _t = UWTileMap.current_tilemap.LevelObjects[slot];
        _t.zpos = 0;
        for (int i = 0; i < 4; i++) _bytesBefore[i] = playerdat.GetQuest(32 + i);
        return Task.CompletedTask;
    }

    void Fire(ScenarioContext c, string what, int d, int op, int expected)
    {
        _t.quality = (short)(d >> 8); _t.owner = (short)((d >> 3) & 0x1F); _t.ypos = (short)(d & 7);
        _t.heading = (short)op;
        a_set_variable_trap.Activate(_t);
        int got = playerdat.GetQuest(5);
        _steps.Add((what, expected, got));
        c.Note($"{what}: quest 5 = {got}");
    }

    public override Task Act(ScenarioContext c)
    {
        if (_problem != null) return Task.CompletedTask;
        playerdat.SetQuest(5, 0);
        Fire(c, "d 5, op 0 (set)", 5, 0, 1);
        Fire(c, "d 5, op 1 (clear)", 5, 1, 0);
        Fire(c, "d 5, op 5 (toggle)", 5, 5, 1);
        Fire(c, "d 5, op 5 (toggle again)", 5, 5, 0);
        Fire(c, "d 0x105, op 2 (set, CL 5)", 0x105, 2, 1);
        Fire(c, "d 40, op 5 (mask 0, no change)", 40, 5, 1);
        for (int i = 0; i < 4; i++) _bytesAfter[i] = playerdat.GetQuest(32 + i);
        return Task.CompletedTask;
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        var k = new Checks();
        foreach (var (what, expected, got) in _steps)
            k.Equal($"quest 5 after {what}", expected, got, "uw1 TRIGGER.C:233-243");
        for (int i = 0; i < 4; i++)
            k.Equal($"quest {32 + i} (a quest byte, not a bit of player->quests)", _bytesBefore[i], _bytesAfter[i], "uw1 TRIGGER.C:233-243 touches only the 32 quest bits");
        return k.Result;
    }
}
