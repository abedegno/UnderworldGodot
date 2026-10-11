using System.Collections.Generic;
using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// A container that lands where it cannot stay and is culled frees its contents with it
/// (issue #207). The original's obj_deal (the port's PlacedObjectCollision_seg030_2BB7_10BC)
/// destroys an object that settles in a wall with Obj_Punt(&amp;tile->objects, obj, 0)
/// (uw1/src/motion/OBJPHYS.C:558-591, uw2/src/motion/OBJPHYS.C:587). Obj_Punt, once
/// Obj_Elem_Fate(10) allows it, frees the object and everything linked inside it
/// (Obj_FreeLinkChain / Obj_FreeChain, uw1/src/obj/OBJECTS.C and uw2/src/obj/OBJECTS.C, Obj_Punt).
///
/// The scenario makes a container holding three objects, all static and all of types the
/// culling test can remove, puts it in the object chain of a solid tile beside the player as
/// a drop does (src/interaction/pickup.cs), and calls PlacedObjectCollision for it until the
/// culling test lets it go (each refusal leaves it in the tile, so the call is repeated). The
/// static free list must then be back where it was before the four objects were made.
/// </summary>
public sealed class CulledContainerContents : Scenario
{
    public override string Name => "culled-container-contents";
    public override string Cites => "underworld-exhumed uw1/src/motion/OBJPHYS.C:558-591 (obj_deal), uw1/src/obj/OBJECTS.C and uw2/src/obj/OBJECTS.C (Obj_Punt, Obj_FreeChain)";

    string _problem;
    int _x, _y, _freeBefore, _freeAfter = -1, _attempts, _container;
    readonly List<int> _contents = new();
    bool _destroyed;
    readonly List<string> _stillAllocated = new();

    static int LowestCull(int from, int to, int skip = -1, int skip2 = -1)
    {
        int best = -1, bestP = 99;
        for (int i = from; i <= to; i++)
        {
            if (i == skip || i == skip2) continue;
            int p = commonObjDat.cullingpriority(i);
            if (p < bestP) { bestP = p; best = i; }
        }
        return best;
    }

    public override Task Setup(ScenarioContext c)
    {
        var map = UWTileMap.current_tilemap;
        var p = playerdat.playerObject;
        _x = -1;
        for (int d = 1; d <= 6 && _x < 0; d++)
            for (int dy = -d; dy <= d && _x < 0; dy++)
                for (int dx = -d; dx <= d && _x < 0; dx++)
                {
                    int tx = p.tileX + dx, ty = p.tileY + dy;
                    // not on the map edge: the collision scan looks one tile beyond
                    if (tx >= 1 && ty >= 1 && tx <= 62 && ty <= 62 && map.Tiles[tx, ty].tileType == UWTileMap.TILE_SOLID && map.Tiles[tx, ty].indexObjectList == 0)
                    { _x = tx; _y = ty; }
                }
        if (_x < 0) { _problem = "no solid tile near the start"; return Task.CompletedTask; }

        // A container (0x80..0x8F) and three small objects (0xC0..0xDF), each the type the
        // culling test most readily removes.
        int containerItem = LowestCull(0x80, 0x8E);
        int a = LowestCull(0xC0, 0xDF);
        int b = LowestCull(0xC0, 0xDF, a);
        int cc = LowestCull(0xC0, 0xDF, a, b);
        if (commonObjDat.cullingpriority(containerItem) > 8 || commonObjDat.cullingpriority(cc) > 8)
        { _problem = "no container and contents the culling test can remove"; return Task.CompletedTask; }

        _freeBefore = map.StaticFreeListPtr;
        _container = ObjectCreator.PrepareNewObject(containerItem);
        if (_container == 0) { _problem = "no container could be made"; return Task.CompletedTask; }
        var box = map.LevelObjects[_container];
        box.is_quant = 0;
        box.doordir = 0;
        int link = 0;
        foreach (var item in new[] { a, b, cc })
        {
            int s = ObjectCreator.PrepareNewObject(item);
            if (s == 0) { _problem = "no content object could be made"; return Task.CompletedTask; }
            var o = map.LevelObjects[s];
            o.is_quant = 1; o.link = 1; o.doordir = 0;   // a single item, no special link
            o.next = (short)link;
            link = s;
            _contents.Add(s);
        }
        box.link = (short)link;

        // Into the solid tile's chain, as a drop puts it (pickup.cs).
        box.xpos = 3; box.ypos = 3;
        box.zpos = (short)(map.Tiles[_x, _y].floorHeight << 3);
        box.tileX = _x; box.tileY = _y;
        box.next = map.Tiles[_x, _y].indexObjectList;
        map.Tiles[_x, _y].indexObjectList = box.index;
        if (map.StaticFreeListPtr != _freeBefore - 4) _problem = $"making four objects moved the static free list by {_freeBefore - map.StaticFreeListPtr}, not 4";
        return Task.CompletedTask;
    }

    public override async Task Act(ScenarioContext c)
    {
        if (_problem != null) return;
        var map = UWTileMap.current_tilemap;
        var box = map.LevelObjects[_container];
        for (_attempts = 1; _attempts <= 200; _attempts++)
        {
            var result = motion.PlacedObjectCollision_seg030_2BB7_10BC(box, _x, _y, 1);
            if (result == null) { _destroyed = true; break; }
            if (map.Tiles[_x, _y].indexObjectList != box.index) { _problem = "the container survived and left the tile; cannot retry"; return; }
        }
        await c.Frames(1);
        _freeAfter = map.StaticFreeListPtr;
        foreach (var s in _contents)
            if (map.LevelObjects[s].item_id != 0) _stillAllocated.Add($"{s} (item 0x{map.LevelObjects[s].item_id:X})");
        c.Note($"solid tile ({_x}, {_y}); destroyed {_destroyed} after {_attempts} calls; static free list {_freeBefore} before, {_freeAfter} after; contents still allocated: {(_stillAllocated.Count == 0 ? "none" : string.Join(", ", _stillAllocated))}");
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        if (!_destroyed) return "precondition: the container was never culled";
        var k = new Checks();
        k.Equal("static free list after the container is culled", _freeBefore, _freeAfter, "Obj_Punt frees the object and its contents: OBJECTS.C Obj_Punt / Obj_FreeLinkChain");
        k.Equal("contents still allocated", 0, _stillAllocated.Count, "Obj_FreeChain frees everything inside: OBJECTS.C");
        return k.Result;
    }
}
