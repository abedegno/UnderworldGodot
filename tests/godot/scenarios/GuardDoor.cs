using System.Collections.Generic;
using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// Guard: using a closed door opens it and using it again closes it; a locked door refuses.
///
/// The original's UseRect (minor class 0, doors) opens a closed door (inclass below 8) unless
/// checkLock finds a lock that is set (its flag bit 9; with no key it returns 0, uw1/src/obj/USEITEMS.C:1004-1034),
/// and closes an open one (USEITEMS.C:958-978). OpenDoor raises the door 0x18 (not a portcullis)
/// and turns it into a moving door, 0x1CF, its own item kept in the owner field (USEITEMS.C:1100-1111,
/// 1132-1161); when the animation ends, toast_animobj makes it the open door, inclass | 8
/// (uw1/src/obj/EFFECT.C:97-140). Closing runs the other way and lowers it 0x18 again
/// (EFFECT.C:117-138). An open door is the passage: inclass 8 and up is what the original tests
/// for an open door (USEITEMS.C:965, 1175, 1190). The UW2 code is the same
/// (uw2/src/obj/USEITEMS.C:738 OpenDoor, 876 UseRect; uw2/src/obj/OBJUSE.C:301 checkLock; uw2/src/obj/EFFECT.C).
///
/// The doors come from the level's live objects: the nearest closed plain door (0x140..0x145, not
/// a portcullis or secret door) with nothing linked to it, and the nearest closed one whose lock
/// is set. Both are used as the player uses them (use.Use, as the click in the view calls it).
/// </summary>
public sealed class GuardDoor : Scenario
{
    const int FirstDoor = 0x140, MovingDoor = 0x1CF, Lock = 0x10F;
    const int SettleFrames = 600;

    public override string Name => "guard-door";
    public override string Cites => "underworld-exhumed uw1/src/obj/USEITEMS.C:958-978 (UseRect doors), 1004-1034 (checkLock), 1100-1194 (moveDoor, OpenDoor, CloseDoor, ToggleDoor), uw1/src/obj/EFFECT.C:97-140 (toast_animobj); uw2 the same functions";

    string _problem;
    uwObject _door, _locked;
    int _item0, _z0, _itemOpen, _zOpen, _itemClosed, _zClosed, _lockedItem0, _lockedItemAfter, _lockedZ0, _lockedZAfter;
    bool _opened, _closed;

    public override Task Setup(ScenarioContext c)
    {
        var p = playerdat.playerObject;
        _door = Nearest(p.tileX, p.tileY, d => Chain(d).Count == 0);
        _locked = Nearest(p.tileX, p.tileY, d =>
        {
            var lk = Chain(d).Find(o => o.item_id == Lock);
            return lk != null && (lk.flags & 1) == 1;
        });
        if (_door == null) { _problem = "no closed plain door with nothing linked on this level"; return Task.CompletedTask; }
        c.Note($"door {_door.index} (0x{_door.item_id:X}) at ({_door.tileX}, {_door.tileY}); locked door {(_locked == null ? "none" : $"{_locked.index} (0x{_locked.item_id:X}) at ({_locked.tileX}, {_locked.tileY})")}; player at ({p.tileX}, {p.tileY})");
        if (_door.instance == null) _problem = "the door has no instance in the view";
        return Task.CompletedTask;
    }

    public override async Task Act(ScenarioContext c)
    {
        if (_problem != null) return;
        var objects = UWTileMap.current_tilemap.LevelObjects;
        _item0 = _door.item_id; _z0 = _door.zpos;
        use.Use(_door, playerdat.playerObject, objects, WorldObject: true);
        _opened = await Settle(c, _door);
        _itemOpen = _door.item_id; _zOpen = _door.zpos;
        use.Use(_door, playerdat.playerObject, objects, WorldObject: true);
        _closed = await Settle(c, _door);
        _itemClosed = _door.item_id; _zClosed = _door.zpos;

        if (_locked != null)
        {
            _lockedItem0 = _locked.item_id; _lockedZ0 = _locked.zpos;
            use.Use(_locked, playerdat.playerObject, objects, WorldObject: true);
            await c.Frames(30);
            _lockedItemAfter = _locked.item_id; _lockedZAfter = _locked.zpos;
        }
        c.Note($"door 0x{_item0:X} z {_z0} -> open 0x{_itemOpen:X} z {_zOpen} -> closed 0x{_itemClosed:X} z {_zClosed}; locked door 0x{_lockedItem0:X} z {_lockedZ0} -> 0x{_lockedItemAfter:X} z {_lockedZAfter}");
    }

    /// <summary>Lets frames pass until the door stops moving. True if it did.</summary>
    static async Task<bool> Settle(ScenarioContext c, uwObject d)
    {
        await c.Frames(2);
        for (int f = 0; f < SettleFrames && d.item_id == MovingDoor; f++) await c.Frames(1);
        return d.item_id != MovingDoor;
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        var k = new Checks();
        k.True(_opened, $"the door was still moving {SettleFrames} frames after the use");
        k.Equal("door item after opening", _item0 + 8, _itemOpen, "open door is inclass | 8: uw1 EFFECT.C:133-138");
        k.Equal("door height after opening", _z0 + 0x18, _zOpen, "OpenDoor raises a door 0x18: uw1 USEITEMS.C:1147-1148");
        k.True(_closed, $"the door was still moving {SettleFrames} frames after the second use");
        k.Equal("door item after closing", _item0, _itemClosed, "CloseDoor, then inclass & 7: uw1 USEITEMS.C:1163-1185, EFFECT.C:117-138");
        k.Equal("door height after closing", _z0, _zClosed, "lowered 0x18 when it shuts: uw1 EFFECT.C:120-122");
        if (_locked == null) c.Note("no locked door on this level: the refusal is not tested");
        else
        {
            k.Equal("locked door item after a use without a key", _lockedItem0, _lockedItemAfter, "checkLock 0, 'is locked': uw1 USEITEMS.C:965-973, 1033-1034");
            k.Equal("locked door height after a use without a key", _lockedZ0, _lockedZAfter, "not opened: uw1 USEITEMS.C:965-973");
        }
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
        for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
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
