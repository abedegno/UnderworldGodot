using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace Underworld.Scenarios;

/// <summary>
/// Helpers shared by the door scenarios.
/// </summary>
static class DoorTools
{
    public const int FirstDoor = 0x140, MovingDoor = 0x1CF;

    /// <summary>The live overlay of object index (a duration of 0 is the port's free slot).</summary>
    public static AnimationOverlay Overlay(int index)
    {
        foreach (var ovl in UWTileMap.current_tilemap.Overlays)
            if (ovl != null && ovl.link == index && ovl.Duration != 0) return ovl;
        return null;
    }

    /// <summary>Lets frames pass until the door stops moving. True if it did.</summary>
    public static async Task<bool> Settle(ScenarioContext c, uwObject d, int frames = 600)
    {
        await c.Frames(2);
        for (int f = 0; f < frames && d.item_id == MovingDoor; f++) await c.Frames(1);
        return d.item_id != MovingDoor;
    }

    /// <summary>The nearest closed plain door (0x140..0x145) with nothing linked, on a square of the level, that has a model.</summary>
    public static uwObject NearestPlainDoor(int px, int py)
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
                    if (o.item_id >= FirstDoor && o.item_id <= FirstDoor + 5 && o.link == 0 && o.instance != null)
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

/// <summary>
/// Using a door while it moves turns it round (UW1 and UW2). The original's use of the moving door,
/// item 0x1CF, calls CloseDoor while it opens (owner state below 8) and OpenDoor while it closes
/// (uw1/src/obj/USEITEMS.C:145-151, uw2/src/obj/OBJUSE.C:125-131). Either one flips the owner's open
/// bit and calls changeDoor, which flips bit 3 of the flags (the direction, set while closing) and makes
/// the run left 5 less the run left (uw1 USEITEMS.C:1114-1128, 1163-1174; uw2 USEITEMS.C:716-732, 772-784).
/// The door then swings back and, at the end of the run, becomes the closed door again, lowered 0x18
/// (toast_animobj, uw1 EFFECT.C:113-143, uw2 EFFECT.C:101-136).
///
/// The port's OpenDoor, CloseDoor and ToggleDoor returned at once on a moving door, and its use of
/// item 0x1CF did nothing (src/objects/door.cs, src/interaction/use.cs UseMajorClass7).
///
/// The scenario opens the nearest plain door with nothing linked (use.Use, as a click calls it), waits
/// until it has turned two steps, and uses it again.
/// </summary>
public sealed class DoorTurnsRound : Scenario
{
    public override string Name => "door-turns-round";
    public override string Cites => "underworld-exhumed uw1/src/obj/USEITEMS.C:145-151, 1114-1128, 1163-1174, uw1/src/obj/EFFECT.C:113-143, 234-241; uw2/src/obj/OBJUSE.C:125-131, uw2/src/obj/USEITEMS.C:716-732, 772-784, uw2/src/obj/EFFECT.C:101-136, 225-232";

    string _problem;
    uwObject _door;
    int _item0, _z0, _angleAtUse, _runAtUse, _runAfter, _dirAfter, _itemAfterUse, _itemEnd, _zEnd, _flagsEnd;
    bool _reachedTwo, _settled;

    public override Task Setup(ScenarioContext c)
    {
        var p = playerdat.playerObject;
        _door = DoorTools.NearestPlainDoor(p.tileX, p.tileY);
        if (_door == null) { _problem = "no closed plain door with nothing linked on this level"; return Task.CompletedTask; }
        _item0 = _door.item_id; _z0 = _door.zpos;
        c.Note($"door {_door.index} (0x{_door.item_id:X}) at ({_door.tileX}, {_door.tileY}) z {_z0} flags {_door.flags_full}");
        if (_door.flags_full != 0) _problem = $"the closed door's flags are {_door.flags_full}, not 0";
        return Task.CompletedTask;
    }

    public override async Task Act(ScenarioContext c)
    {
        if (_problem != null) return;
        var objects = UWTileMap.current_tilemap.LevelObjects;
        use.Use(_door, playerdat.playerObject, objects, WorldObject: true);
        for (int f = 0; f < 600 && _door.item_id == DoorTools.MovingDoor; f++)
        {
            if ((_door.flags_full & 7) >= 2) { _reachedTwo = true; break; }
            await c.Frames(1);
        }
        if (!_reachedTwo) return;
        _angleAtUse = _door.flags_full & 7;
        _runAtUse = DoorTools.Overlay(_door.index)?.Duration ?? -1;
        use.Use(_door, playerdat.playerObject, objects, WorldObject: true);
        _itemAfterUse = _door.item_id;
        _dirAfter = (_door.flags_full >> 3) & 1;
        _runAfter = DoorTools.Overlay(_door.index)?.Duration ?? -1;
        _settled = await DoorTools.Settle(c, _door);
        _itemEnd = _door.item_id; _zEnd = _door.zpos; _flagsEnd = _door.flags_full;
        c.Note($"used at angle {_angleAtUse} with {_runAtUse} steps left: then 0x{_itemAfterUse:X} direction {_dirAfter} steps left {_runAfter}; at rest 0x{_itemEnd:X} z {_zEnd} flags {_flagsEnd}");
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        if (!_reachedTwo) return "precondition: the door never reached its second step after the first use";
        var k = new Checks();
        k.Equal("door item just after the second use", DoorTools.MovingDoor, _itemAfterUse, "still moving: uw1 USEITEMS.C:1168-1174, uw2 USEITEMS.C:778-784");
        k.Equal("direction bit just after the second use (1 closing)", 1, _dirAfter, "changeDoor flips flag bit 3: uw1 USEITEMS.C:1122-1125, uw2 USEITEMS.C:724-727");
        k.Equal("steps left just after the second use", 5 - _runAtUse, _runAfter, "set_animlen(len - cur): uw1 USEITEMS.C:1126-1127, uw2 USEITEMS.C:729-731");
        k.True(_settled, "the door was still moving 600 frames after the second use");
        k.Equal("door item at rest", _item0, _itemEnd, "the closed door again: uw1 EFFECT.C:118-143, uw2 EFFECT.C:106-136");
        k.Equal("door height at rest", _z0, _zEnd, "lowered 0x18 when it shuts: uw1 EFFECT.C:121-122, uw2 EFFECT.C:109-110");
        k.Equal("door flags at rest", 0, _flagsEnd, "angle 0, direction bit cleared when it shuts: uw1 EFFECT.C:140-143, uw2 EFFECT.C:133-136");
        return k.Result;
    }
}

/// <summary>
/// A closing door that meets something in its doorway swings back open (UW1 and UW2). Each step of a
/// closing door runs check_door, which asks can_place whether the door, at the height it has when shut,
/// still fits its square; if not it clears the direction bit, steps back and sets the run to
/// limit - run + 1 (uw1/src/obj/EFFECT.C:234-241, 441-474; uw2/src/obj/EFFECT.C:225-232, 445-479, which also
/// fires the open triggers). toast_animobj makes the same test at the end of a close (uw1 EFFECT.C:118-130,
/// uw2 EFFECT.C:106-118). The door so ends open.
///
/// The port's door step only turned the door (src/objects/door.cs MoveDoor) and so closed it through
/// whatever stood there.
///
/// The scenario opens the nearest plain door with nothing linked, puts a critter (item 0x40) at the door's
/// own position in its square, and uses the door again.
/// </summary>
public sealed class DoorObstructed : Scenario
{
    const int Critter = 0x40;

    public override string Name => "door-obstructed";
    public override string Cites => "underworld-exhumed uw1/src/obj/EFFECT.C:113-143, 234-241, 441-474; uw2/src/obj/EFFECT.C:101-136, 225-232, 445-479";

    string _problem;
    uwObject _door;
    int _item0, _itemOpen, _zOpen, _angleOpen, _itemEnd, _zEnd, _angleEnd;
    bool _opened, _everClosedItem, _settled, _moved;

    public override Task Setup(ScenarioContext c)
    {
        var p = playerdat.playerObject;
        _door = DoorTools.NearestPlainDoor(p.tileX, p.tileY);
        if (_door == null) { _problem = "no closed plain door with nothing linked on this level"; return Task.CompletedTask; }
        _item0 = _door.item_id;
        c.Note($"door {_door.index} (0x{_door.item_id:X}) at ({_door.tileX}, {_door.tileY}) fine ({_door.xpos}, {_door.ypos}) z {_door.zpos}");
        return Task.CompletedTask;
    }

    public override async Task Act(ScenarioContext c)
    {
        if (_problem != null) return;
        var objects = UWTileMap.current_tilemap.LevelObjects;
        use.Use(_door, playerdat.playerObject, objects, WorldObject: true);
        _opened = await DoorTools.Settle(c, _door);
        _itemOpen = _door.item_id; _zOpen = _door.zpos; _angleOpen = _door.flags_full & 7;
        if (!_opened || _itemOpen != _item0 + 8) { _problem = $"the door did not open (0x{_itemOpen:X})"; return; }

        var tile = UWTileMap.current_tilemap.Tiles[_door.tileX, _door.tileY];
        var crit = ObjectCreator.spawnObjectInTile(Critter, _door.tileX, _door.tileY, _door.xpos, _door.ypos, (short)(tile.floorHeight << 3), ObjectFreeLists.ObjectListType.MobileList);
        if (crit == null) { _problem = "no critter could be made"; return; }
        crit.npc_goal = 7;   // stand
        crit.npc_attitude = 3;
        await c.Frames(2);
        c.Note($"door open 0x{_itemOpen:X} z {_zOpen} angle {_angleOpen}; critter 0x{crit.item_id:X} at ({crit.tileX}, {crit.tileY}) fine ({crit.xpos}, {crit.ypos}) z {crit.zpos}");

        use.Use(_door, playerdat.playerObject, objects, WorldObject: true);
        _moved = _door.item_id == DoorTools.MovingDoor;
        for (int f = 0; f < 600 && _door.item_id == DoorTools.MovingDoor; f++)
        {
            await c.Frames(1);
            if (_door.item_id == _item0) _everClosedItem = true;
        }
        _settled = _door.item_id != DoorTools.MovingDoor;
        if (_door.item_id == _item0) _everClosedItem = true;
        _itemEnd = _door.item_id; _zEnd = _door.zpos; _angleEnd = _door.flags_full & 7;
        c.Note($"after closing on the critter: 0x{_itemEnd:X} z {_zEnd} angle {_angleEnd}; critter at ({crit.tileX}, {crit.tileY})");
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        if (!_moved) return "precondition: the second use did not start the door closing";
        var k = new Checks();
        k.True(_settled, "the door was still moving 600 frames after the second use");
        k.True(!_everClosedItem, "a door closing on a critter in its doorway expected to swing back open, but it shut (check_door: uw1 EFFECT.C:441-474, uw2 EFFECT.C:445-479)");
        k.Equal("door item at rest", _itemOpen, _itemEnd, "back open: uw1 EFFECT.C:133-143, uw2 EFFECT.C:126-136");
        k.Equal("door height at rest", _zOpen, _zEnd, "not lowered, it never shut: uw1 EFFECT.C:121-129, uw2 EFFECT.C:109-117");
        k.Equal("door angle at rest", _angleOpen, _angleEnd, "fully open again");
        return k.Result;
    }
}

/// <summary>
/// Arriving in level 1 of UW2 opens the door on the square asked for. The original's do_level_hacks runs
/// gruesome_door_hack(NewPlayerX, NewPlayerY) on arrival in level 1 (uw2/src/game/GAMEWRAP.C:474-477), which
/// finds the door (major 5, minor 0) on that square, tries its lock, opens it (OpenDoor, raised 0x18) and ends
/// its motion at once (toast_animobj with the frames left, the door class having the finish flag), so it is
/// the open door, inclass | 8, at full angle with the direction bit set (uw2 GAMEWRAP.C:422-438,
/// uw2/src/obj/EFFECT.C:97-136).
///
/// The port only printed a debug line (src/utility/teleportation.cs LevelChangeEventsUW2).
///
/// The scenario takes the nearest closed plain door with nothing linked on level 1, teleports the player to
/// level 2 and then back to level 1 onto the door's square, as a teleport trap does.
/// </summary>
public sealed class GruesomeDoorHack : Scenario
{
    const ulong TeleportMs = 20000;

    public override string Name => "gruesome-door-hack";
    public override string[] Games => new[] { "uw2" };
    public override string Cites => "underworld-exhumed uw2/src/game/GAMEWRAP.C:422-438, 474-477; uw2/src/obj/USEITEMS.C:738-768; uw2/src/obj/EFFECT.C:84-136";

    string _problem;
    int _x, _y, _item0, _z0, _level2, _itemEnd = -1, _zEnd, _flagsEnd, _levelEnd;
    bool _arrived;

    public override Task Setup(ScenarioContext c)
    {
        if (playerdat.dungeon_level != 1) { _problem = $"not on level 1 ({playerdat.dungeon_level})"; return Task.CompletedTask; }
        var p = playerdat.playerObject;
        var d = DoorTools.NearestPlainDoor(p.tileX, p.tileY);
        if (d == null) { _problem = "no closed plain door with nothing linked on level 1"; return Task.CompletedTask; }
        _x = d.tileX; _y = d.tileY; _item0 = d.item_id; _z0 = d.zpos;
        c.Note($"door 0x{_item0:X} at ({_x}, {_y}) z {_z0}");
        return Task.CompletedTask;
    }

    public override async Task Act(ScenarioContext c)
    {
        if (_problem != null) return;
        var p = playerdat.playerObject;
        Teleportation.Teleport(character: 0, tileX: 32, tileY: 32, newLevel: 2, HeadingHeightFlag: 0);
        await WaitLevel(c, 2);
        _level2 = playerdat.dungeon_level;
        if (_level2 != 2) return;
        Teleportation.Teleport(character: 0, tileX: _x, tileY: _y, newLevel: 1, HeadingHeightFlag: 0);
        _arrived = await WaitLevel(c, 1);
        _levelEnd = playerdat.dungeon_level;
        var d = objectsearch.FindMatchInTile(tileX: _x, tileY: _y, majorclass: 5, minorclass: 0, classindex: -1);
        var moving = objectsearch.FindMatchInTile(tileX: _x, tileY: _y, majorclass: 7, minorclass: 0, classindex: 0xF);
        if (d == null && moving != null) d = moving;
        if (d != null) { _itemEnd = d.item_id; _zEnd = d.zpos; _flagsEnd = d.flags_full; }
        c.Note($"after arriving at ({playerdat.playerObject.tileX}, {playerdat.playerObject.tileY}) on level {_levelEnd}: door 0x{_itemEnd:X} z {_zEnd} flags {_flagsEnd}");
    }

    static async Task<bool> WaitLevel(ScenarioContext c, int level)
    {
        ulong deadline = Time.GetTicksMsec() + TeleportMs;
        int stable = 0;
        while (Time.GetTicksMsec() < deadline && stable < 3)
        {
            await c.Frames(1);
            stable = (playerdat.dungeon_level == level && !uimanager.WaitingForFade && uimanager.InGame) ? stable + 1 : 0;
        }
        return stable >= 3;
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        if (_level2 != 2) return $"precondition: the teleport to level 2 did not happen (level {_level2})";
        if (!_arrived || _levelEnd != 1) return $"precondition: the teleport back to level 1 did not happen (level {_levelEnd})";
        var k = new Checks();
        k.Equal("door item on the arrival square", _item0 + 8, _itemEnd, "gruesome_door_hack opens it and ends its motion: uw2 GAMEWRAP.C:426-438, EFFECT.C:126-136");
        k.Equal("door height on the arrival square", _z0 + 0x18, _zEnd, "OpenDoor raises a door 0x18: uw2 USEITEMS.C:755-756");
        k.Equal("door flags on the arrival square", 5 + 8, _flagsEnd, "the five steps left taken at once (finish flag 0x80), then the direction bit: uw2 EFFECT.C:97-99, 133-136");
        return k.Result;
    }
}
