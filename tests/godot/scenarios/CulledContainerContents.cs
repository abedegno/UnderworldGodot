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
///
/// Two more cases on the same freeing:
///
/// A temporary critter (spawned, attitude_word bit 8) that up_crit removes, here
/// npc.ReturnNPCToHomeTile, goes with everything it carries: Obj_FreeLinkChain
/// (uw1/src/critter/CRITTIME.C:149-150, uw2/src/critter/CRITTIME.C:147-148). A mobile critter
/// carrying two static objects is put in the player's square and removed; both free lists must
/// be back where they were.
///
/// An object that comes to rest and is culled (ObjectHitsFloorTile_seg030_2BB7_DDF, obj_deal's
/// come-to-rest path, uw1 OBJPHYS.C:477, uw2 OBJPHYS.C:468) but cannot be found in its square's
/// chain must be left as it is, as the culling path in PlacedObjectCollision already does: its
/// contents are not freed out from under it. A static object holding two objects, in no chain,
/// is passed in with every draw 0 so that the culling test takes it; afterwards it must still
/// hold both and the static free list must not have moved. Only missiles come to rest culled in
/// either game's data, so the object is a missile made a non-quantity with a link: a case built
/// for the test, which play reaches rarely if at all.
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

    // The temporary critter case.
    int _critFreeMobileBefore, _critFreeStaticBefore, _critFreeMobileAfter = -1, _critFreeStaticAfter = -1;
    readonly List<string> _critStillAllocated = new();
    bool _critRemoved;

    // The container-not-in-its-chain case.
    int _strayFreeBefore, _strayFreeAfter = -1, _strayContainer, _strayLinkBefore, _strayLinkAfter = -1, _strayItemBefore, _strayItemAfter = -1;
    readonly List<int> _strayContents = new();
    readonly List<string> _strayFreed = new();

    /// <summary>Every draw 0: the culling draws in ObjectHitsFloorTile all pass.</summary>
    sealed class ZeroRand : IRandSource
    {
        public int Rand() => 0;
        public int Next(int max) => 0;
    }

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

        TemporaryCritter(c);
        if (_problem != null) return;
        StrayContainer(c);
    }

    /// <summary>Makes count static single items of the lowest culling types in 0xC0..0xDF, chained by next; returns the head.</summary>
    int MakeItems(int count, List<int> made)
    {
        var map = UWTileMap.current_tilemap;
        int link = 0, skip = -1;
        for (int n = 0; n < count; n++)
        {
            int item = LowestCull(0xC0, 0xDF, skip);
            skip = item;
            int s = ObjectCreator.PrepareNewObject(item);
            if (s == 0) return -1;
            var o = map.LevelObjects[s];
            o.is_quant = 1; o.link = 1; o.doordir = 0;
            o.next = (short)link;
            link = s;
            made.Add(s);
        }
        return link;
    }

    void TemporaryCritter(ScenarioContext c)
    {
        var map = UWTileMap.current_tilemap;
        int px = playerdat.playerObject.tileX, py = playerdat.playerObject.tileY;
        _critFreeMobileBefore = map.MobileFreeListPtr;
        _critFreeStaticBefore = map.StaticFreeListPtr;
        var crit = ObjectCreator.spawnObjectInTile(0x40, px, py, 1, 1, (short)(map.Tiles[px, py].floorHeight << 3), ObjectFreeLists.ObjectListType.MobileList, RenderImmediately: false);
        if (crit == null) { _problem = "no critter could be made"; return; }
        var carried = new List<int>();
        int head = MakeItems(2, carried);
        if (head <= 0) { _problem = "no objects could be made for the critter to carry"; return; }
        crit.is_quant = 0;
        crit.link = (short)head;
        crit.SpawnedCritter_0XD_Bit8 = 1;
        int critIndex = crit.index;
        var counts = new int[32];
        npc.ReturnNPCToHomeTile(crit, ref counts);
        _critRemoved = !Guards.InTileChain(px, py, critIndex);
        _critFreeMobileAfter = map.MobileFreeListPtr;
        _critFreeStaticAfter = map.StaticFreeListPtr;
        foreach (var s in carried)
            if (map.LevelObjects[s].item_id != 0) _critStillAllocated.Add($"{s} (item 0x{map.LevelObjects[s].item_id:X})");
        c.Note($"temporary critter {critIndex} removed {_critRemoved}; mobile free list {_critFreeMobileBefore} -> {_critFreeMobileAfter}, static {_critFreeStaticBefore} -> {_critFreeStaticAfter}; carried still allocated: {(_critStillAllocated.Count == 0 ? "none" : string.Join(", ", _critStillAllocated))}");
    }

    void StrayContainer(ScenarioContext c)
    {
        var map = UWTileMap.current_tilemap;
        int item = -1, bestP = 99;
        // Only missiles (0x10..0x1F) both come to rest culled and pass the culling test in either
        // game's data, so the stray "container" is a missile made a non-quantity holding two
        // objects: the freeing walks the link of anything that is not a quantity.
        for (int i = 0x10; i <= 0x1F; i++)
        {
            int pc = commonObjDat.projectilecullingpriority(i);
            if (pc < 1 || pc > 8) continue;
            int p = commonObjDat.cullingpriority(i);
            if (p < bestP) { bestP = p; item = i; }
        }
        if (item < 0 || bestP > 10) { _problem = "no object that comes to rest culled"; return; }
        _strayFreeBefore = map.StaticFreeListPtr;
        _strayContainer = ObjectCreator.PrepareNewObject(item);
        if (_strayContainer == 0) { _problem = "no stray container could be made"; return; }
        var box = map.LevelObjects[_strayContainer];
        int head = MakeItems(2, _strayContents);
        if (head <= 0) { _problem = "no objects could be made for the stray container"; return; }
        box.is_quant = 0; box.doordir = 0;
        box.link = (short)head;
        box.next = 0;
        box.xpos = 3; box.ypos = 3;
        box.tileX = playerdat.playerObject.tileX; box.tileY = playerdat.playerObject.tileY;
        box.zpos = (short)(map.Tiles[box.tileX, box.tileY].floorHeight << 3);
        if (Guards.InTileChain(box.tileX, box.tileY, box.index)) { _problem = "the stray container is in its square's chain"; return; }
        _strayFreeBefore = map.StaticFreeListPtr;
        _strayLinkBefore = box.link;
        _strayItemBefore = box.item_id;
        var saved = Rng.Source;
        uwObject result;
        try
        {
            Rng.Source = new ZeroRand();
            result = motion.ObjectHitsFloorTile_seg030_2BB7_DDF(box);
        }
        finally { Rng.Source = saved; }
        if (result != null) { _problem = "the stray container came to rest instead of being culled"; return; }
        _strayFreeAfter = map.StaticFreeListPtr;
        _strayLinkAfter = map.LevelObjects[_strayContainer].link;
        _strayItemAfter = map.LevelObjects[_strayContainer].item_id;
        foreach (var s in _strayContents)
            if (map.LevelObjects[s].item_id == 0) _strayFreed.Add(s.ToString());
        c.Note($"stray container {_strayContainer} (item 0x{item:X}): link {_strayLinkBefore} -> {_strayLinkAfter}, item 0x{_strayItemBefore:X} -> 0x{_strayItemAfter:X}; static free list {_strayFreeBefore} -> {_strayFreeAfter}; contents freed: {(_strayFreed.Count == 0 ? "none" : string.Join(", ", _strayFreed))}");
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        if (!_destroyed) return "precondition: the container was never culled";
        var k = new Checks();
        k.Equal("static free list after the container is culled", _freeBefore, _freeAfter, "Obj_Punt frees the object and its contents: OBJECTS.C Obj_Punt / Obj_FreeLinkChain");
        k.Equal("contents still allocated", 0, _stillAllocated.Count, "Obj_FreeChain frees everything inside: OBJECTS.C");

        const string temp = "up_crit frees a temporary critter with Obj_FreeLinkChain: uw1 CRITTIME.C:149-150, uw2 CRITTIME.C:147-148";
        k.True(_critRemoved, "temporary critter still in its square (" + temp + ")");
        k.Equal("mobile free list after the temporary critter is removed", _critFreeMobileBefore, _critFreeMobileAfter, temp);
        k.Equal("static free list after the temporary critter is removed", _critFreeStaticBefore, _critFreeStaticAfter, temp);
        k.Equal("objects the temporary critter carried still allocated", 0, _critStillAllocated.Count, temp);

        const string stray = "an object not in its square's chain is left alone, as the culling path in PlacedObjectCollision does";
        k.Equal("stray container's link after it is culled", _strayLinkBefore, _strayLinkAfter, stray);
        k.Equal("stray container's item after it is culled", _strayItemBefore, _strayItemAfter, stray);
        k.Equal("static free list after the stray container is culled", _strayFreeBefore, _strayFreeAfter, stray);
        k.Equal("stray container's contents freed", 0, _strayFreed.Count, stray);
        return k.Result;
    }
}
