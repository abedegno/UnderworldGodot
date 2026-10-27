using System.Collections.Generic;
using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// Critters and locked doors. checkLock (uw1/src/obj/USEITEMS.C:1004-1043,
/// uw2/src/obj/OBJUSE.C:301-345) looks for a lock (0x10F) in a door's contents when the door is
/// not a quantity and its link is above 0. With key 0 it answers 0 for a lock whose bit 9 is set
/// and 4 for one that is clear. With a negative key, minus a lock picking skill, a lock of z 0xF
/// cannot be picked (0), and a pick that passes skill_check unlocks it (3): the lock is deleted
/// unless its bit 10 is set, when only bit 9 is cleared.
///
/// Its critter callers: UW2's hyp_move (uw2/src/critter/PATHFIND.C:583-636) fails a move through a
/// closed door whose checkLock 0 is 0 when the door's frame lies across it; straight through the
/// door square that is north to south for heading &amp; 3 of 0 (dir 7, 10: fails unless 2) and east
/// to west for 2 (dir 1, 4: fails unless 0). UW1's (uw1/src/critter/PATHFIND.C:443-505) has no lock
/// test: the frame of every closed door counts. try_to_open_door (uw1 PATHFIND.C:1248-1261,
/// uw2 PATHFIND.C:1392-1406) picks the lock with checkLock(-locks).
///
/// The scenario takes the nearest closed locked door on level 1 with heading 0 or 2 and a critter of
/// the level, and asks the port's checkLock (door.CharacterDoorLockAndKeyInteraction) and hyp_move
/// (Pathfind.TraverseMultipleTiles, flags 0 so only walls and doors count) what the original's
/// would answer: open, the move passes (so no wall is in the way); locked, it fails; with the lock
/// clear it passes in UW2 and fails in UW1; z 0xF, a pick fails; a pick at skill 100 (above any
/// roll's need: z 0xE wants 0x1F in UW1 and 0x20 in UW2, and skill_check at most 0xE * 3 = 42
/// against it, so it always returns 2) unlocks it.
///
/// Both games. The port tested link &lt;= 0, so it never found the lock (src/objects/door.cs:398),
/// and answered 1 to key 0 on a locked lock, so no closed door ever stopped a critter's path.
/// </summary>
public sealed class CritterLockedDoor : Scenario
{
    const int FirstDoor = 0x140, Lock = 0x10F;
    const int PickSkill = 100;

    public override string Name => "critter-locked-door";
    public override string Cites => "underworld-exhumed uw1/src/obj/USEITEMS.C:1004-1043 (checkLock), uw1/src/critter/PATHFIND.C:443-480 (hyp_move doors), 1247-1261 (try_to_open_door); uw2/src/obj/OBJUSE.C:301-345, uw2/src/critter/PATHFIND.C:583-636, 1391-1406";

    string _problem;
    uwObject _door, _lock, _critter;
    int _x1, _y1, _x3, _y3;
    int _lockedAnswer, _unlockedAnswer, _hardPick, _pick;
    bool _moveOpen, _moveLocked, _moveUnlocked, _moveAfterPick, _stillLockedAfterHard, _keepsLock, _lockGone, _lockClearAfterPick;

    public override Task Setup(ScenarioContext c)
    {
        var p = playerdat.playerObject;
        var objects = UWTileMap.current_tilemap.LevelObjects;
        for (int i = 2; i < 256 && _critter == null; i++)
        {
            var o = objects[i];
            if (o != null && o.majorclass == 1 && o.item_id >= 0x40 && o.item_id < 0x80) _critter = o;
        }
        _door = Nearest(p.tileX, p.tileY, d =>
        {
            if (d.heading != 0 && d.heading != 2) return false;
            var lk = Chain(d).Find(o => o.item_id == Lock);
            return lk != null && (lk.flags_full & 1) == 1 && lk.zpos < 0xF;
        });
        if (_critter == null) { _problem = "no critter on the level"; return Task.CompletedTask; }
        if (_door == null) { _problem = "no closed locked door with heading 0 or 2 on the level"; return Task.CompletedTask; }
        _lock = Chain(_door).Find(o => o.item_id == Lock);
        int x = _door.tileX, y = _door.tileY;
        if (_door.heading == 0) { _x1 = x; _y1 = y - 1; _x3 = x; _y3 = y + 1; }   // dir 7: x1 == x2 == x3, y1 < y2
        else { _x1 = x - 1; _y1 = y; _x3 = x + 1; _y3 = y; }                     // dir 1: x1 < x2, y2 == y3
        c.Note($"door {_door.index} (0x{_door.item_id:X}, heading {_door.heading}) at ({x}, {y}), lock {_lock.index} z {_lock.zpos} bit 10 {(_lock.flags_full >> 1) & 1}; critter {_critter.index} (0x{_critter.item_id:X})");
        return Task.CompletedTask;
    }

    public override Task Act(ScenarioContext c)
    {
        if (_problem != null) return Task.CompletedTask;

        _door.item_id += 8;   // open: inclass 8 and up is not tested
        _moveOpen = Move();
        _door.item_id -= 8;

        _lockedAnswer = door.CharacterDoorLockAndKeyInteraction(_critter, _door, 0);
        _moveLocked = Move();

        _lock.flags0 = 0;
        _unlockedAnswer = door.CharacterDoorLockAndKeyInteraction(_critter, _door, 0);
        _moveUnlocked = Move();
        _lock.flags0 = 1;

        var z = _lock.zpos;
        _lock.zpos = 0xF;
        _hardPick = door.CharacterDoorLockAndKeyInteraction(_critter, _door, -PickSkill);
        _stillLockedAfterHard = Chain(_door).Contains(_lock) && _lock.flags0 == 1;
        _lock.zpos = z;

        _keepsLock = _lock.flags1 == 1;
        _pick = door.CharacterDoorLockAndKeyInteraction(_critter, _door, -PickSkill);
        _lockGone = !Chain(_door).Contains(_lock);
        _lockClearAfterPick = _lock.flags0 == 0;
        _moveAfterPick = Move();
        c.Note($"key 0 locked {_lockedAnswer}, unlocked {_unlockedAnswer}; move open {_moveOpen}, locked {_moveLocked}, unlocked {_moveUnlocked}; pick z 0xF {_hardPick}, pick {_pick}, lock gone {_lockGone}; move after {_moveAfterPick}");
        return Task.CompletedTask;
    }

    bool Move()
    {
        int height = 0, dist = 0;
        return Pathfind.TraverseMultipleTiles(_critter, _x1, _y1, _door.tileX, _door.tileY, _x3, _y3, 0, 0, 0, ref height, ref dist);
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        if (!_moveOpen) return "precondition: the move through the door square failed with the door open, so a wall is in the way";
        var k = new Checks();
        bool uw1 = c.Game == "uw1";
        string hypMove = uw1 ? "uw1 PATHFIND.C:443-476, no lock test" : "uw2 PATHFIND.C:584-616";
        k.Equal("checkLock 0 on the locked door", 0, _lockedAnswer, uw1 ? "uw1 USEITEMS.C:1009-1014, 1033-1034" : "uw2 OBJUSE.C:307-312, 333-334");
        k.Equal("checkLock 0 with the lock clear", 4, _unlockedAnswer, uw1 ? "uw1 USEITEMS.C:1014, 1025" : "uw2 OBJUSE.C:312, 323");
        k.True(!_moveLocked, $"move through the locked door expected to fail ({hypMove}), it passed");
        if (uw1) k.True(!_moveUnlocked, $"move through the closed door with its lock clear expected to fail ({hypMove}), it passed");
        else k.True(_moveUnlocked, $"move through the closed door with its lock clear expected to pass ({hypMove}), it failed");
        k.Equal("pick of a z 0xF lock", 0, _hardPick, uw1 ? "uw1 USEITEMS.C:1026-1029" : "uw2 OBJUSE.C:324-327");
        k.True(_stillLockedAfterHard, "the z 0xF lock was opened");
        k.Equal($"pick at skill {PickSkill}", 3, _pick, uw1 ? "uw1 USEITEMS.C:1026-1044" : "uw2 OBJUSE.C:324-343");
        if (_keepsLock) k.True(!_lockGone && _lockClearAfterPick, "a lock with bit 10 set expected kept with bit 9 clear");
        else k.True(_lockGone, "a lock with bit 10 clear expected deleted from the door");
        if (uw1) k.True(!_moveAfterPick, $"move through the closed door after the pick expected to fail ({hypMove})");
        else k.True(_moveAfterPick, $"move through the door after the pick expected to pass ({hypMove})");
        return k.Result;
    }

    /// <summary>The objects linked to d (its link chain), when d is not a quantity.</summary>
    static List<uwObject> Chain(uwObject d)
    {
        var list = new List<uwObject>();
        if (d.is_quant == 1) return list;
        var objects = UWTileMap.current_tilemap.LevelObjects;
        int i = d.link;
        for (int guard = 0; i > 0 && i < objects.Length && guard < 64; guard++)
        {
            var o = objects[i];
            if (o == null) break;
            list.Add(o);
            i = o.next;
        }
        return list;
    }

    /// <summary>The nearest closed door 0x140..0x145 on a square's object chain that suits.</summary>
    static uwObject Nearest(int px, int py, System.Predicate<uwObject> suits)
    {
        var map = UWTileMap.current_tilemap;
        uwObject best = null;
        int bestD = int.MaxValue;
        for (int y = 1; y < 63; y++)
            for (int x = 1; x < 63; x++)
            {
                int i = map.Tiles[x, y].indexObjectList;
                for (int guard = 0; i != 0 && guard < 1024; guard++)
                {
                    var o = map.LevelObjects[i];
                    if (o == null) break;
                    if (o.item_id >= FirstDoor && o.item_id <= FirstDoor + 5 && suits(o))
                    {
                        int dd = (x - px) * (x - px) + (y - py) * (y - py);
                        if (dd < bestD) { bestD = dd; best = o; }
                    }
                    i = o.next;
                }
            }
        return best;
    }
}
