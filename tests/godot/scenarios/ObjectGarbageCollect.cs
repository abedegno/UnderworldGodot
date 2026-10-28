using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// The original frees distant objects when it runs out of object slots, and when the player
/// sleeps. Obj_GarbageCollect(range, count) walks the map's tiles in row order, and for each tile
/// more than 10 - range tiles (taxicab) from the player's home tile walks its list, freeing the
/// object and its contents (Obj_FreeLinkChain) when Obj_Fate(range) lets it go, until count objects
/// are freed (uw1/src/obj/OBJECTS.C:188-223, uw2/src/obj/OBJECTS.C:181-216). Obj_Alloc calls it with
/// (3, 10) when the static free list is empty and (3, 5) when the mobile one is, then tries again
/// (uw1 OBJECTS.C:228-246, uw2 OBJECTS.C:221-239); player_sleep calls it with (1, 20)
/// (uw1/src/game/SKILLS.C:544, uw2/src/game/SKILLS.C:462). Obj_Fate draws rand() * 3 / 0x8000 for
/// the range, then, unless the object or its contents are tenacious, rand() * 10 / 0x8000 against it
/// (uw1 OBJECTS.C:154-170, uw2 OBJECTS.C:148-164).
///
/// The scenario puts the least tenacious ordinary item (tenacity 2 at most) in many tiles more than 12 tiles from the player and in
/// tiles within 6 tiles of the player, taking the slots straight off the free lists.
/// (b) It sleeps (sleep.Sleep(0)): exactly 20 objects must be freed, none within 9 tiles.
/// (a) It takes every static slot off the free list, then allocates one through the port's allocator:
/// the allocation must succeed, and the objects freed must be exactly those a model of
/// Obj_GarbageCollect(3, 10), run on the tiles as they were with the same seeded rand(), frees; none
/// within 7 tiles. The same for a mobile, with mobile items placed first in the scan order and (3, 5).
/// The player and the inventory are never in a far tile's list, so they are never looked at.
///
/// Both games. The port had no garbage collection: an empty free list gave slot 0, and sleep freed nothing.
/// </summary>
public sealed class ObjectGarbageCollect : Scenario
{
    const int SleepFar = 120, StaticFar = 30, MobileFar = 20, NearCount = 10;

    public override string Name => "object-garbage-collect";
    public override string Cites => "underworld-exhumed uw1/src/obj/OBJECTS.C:154-170, 188-246, uw1/src/game/SKILLS.C:544; uw2/src/obj/OBJECTS.C:148-164, 181-239, uw2/src/game/SKILLS.C:462";

    string _problem;
    int _itemId = -1, _px, _py;
    readonly List<int> _near = new();
    readonly List<(int x, int y)> _farTiles = new();
    int _farNext;

    // results
    bool _slept;
    int _sleepFreed, _sleepFreedClose, _sleepNearFreed;
    int _staticSlot, _mobileSlot;
    bool _staticWasEmpty, _mobileWasEmpty;
    HashSet<int> _staticModel, _staticActual, _mobileModel, _mobileActual;
    HashSet<int> _staticModelAll, _mobileModelAll;
    int _staticClose, _mobileClose, _nearFreedAlloc;

    static uwObject[] Objs => UWTileMap.current_tilemap.LevelObjects;
    int Dist(int x, int y) => System.Math.Abs(_px - x) + System.Math.Abs(_py - y);

    static int PopStatic()
    {
        var m = UWTileMap.current_tilemap;
        if (m.StaticFreeListPtr < 0) return 0;
        int s = m.StaticFreeListObject;
        m.StaticFreeListPtr--;
        return s;
    }

    static int PopMobile()
    {
        var m = UWTileMap.current_tilemap;
        if (m.MobileFreeListPtr < 0) return 0;
        int s = m.MobileFreeListObject;
        m.MobileFreeListPtr--;
        return s;   // not put on the active list, so no frame moves it
    }

    void Place(int slot, int x, int y)
    {
        var tile = UWTileMap.current_tilemap.Tiles[x, y];
        var o = Objs[slot];
        o.item_id = _itemId;
        o.doordir = 0;
        o.is_quant = 1;
        o.link = 1;   // a quantity of 1
        o.next = tile.indexObjectList;
        o.tileX = x; o.tileY = y;
        tile.indexObjectList = (short)slot;
    }

    bool PlaceFar(int slot)
    {
        if (slot == 0 || _farNext >= _farTiles.Count) return false;
        var (x, y) = _farTiles[_farNext++];
        Place(slot, x, y);
        return true;
    }

    /// <summary>Every object in each tile's list, by tile.</summary>
    static Dictionary<int, (int x, int y)> TileObjects()
    {
        var d = new Dictionary<int, (int, int)>();
        var m = UWTileMap.current_tilemap;
        for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
            {
                int link = m.Tiles[x, y].indexObjectList, guard = 0;
                while (link > 0 && guard++ < 1024)
                {
                    d[link] = (x, y);
                    link = Objs[link].next;
                }
            }
        return d;
    }

    // chkTenacious (uw1 OBJECTS.C:136-147, uw2 OBJECTS.C:130-141)
    static bool Tenacious(uwObject o, int cullRange)
    {
        if (o.doordir == 1) return true;
        int extra = (o.is_quant == 1 && (o.link & 0x200) == 0) ? o.link - 1 : 0;
        return commonObjDat.cullingpriority(o.item_id) + extra / 2 > cullRange;
    }

    // Obj_Check (uw2 OBJECTS.C:104-122)
    static bool Check(uwObject o, int cullRange, int depth = 0)
    {
        int guard = 0;
        while (o != null && guard++ < 1024 && depth < 64)
        {
            if (Tenacious(o, cullRange)) return true;
            if (o.is_quant == 0 && o.link > 0 && Check(Objs[o.link], cullRange, depth + 1)) return true;
            if (o.next == 0) return false;
            o = Objs[o.next];
        }
        return false;
    }

    static int Scale(BorlandRandSource r, int n) => (int)((long)r.Rand() * n / 0x8000);

    // Obj_Elem_Fate (uw2 OBJECTS.C:148-164)
    static bool Fate(int range, uwObject o, BorlandRandSource r)
    {
        if (range != 0) range += Scale(r, 3);
        if (Tenacious(o, range)) return false;
        if (o.is_quant == 0 && o.link > 0 && Check(Objs[o.link], range)) return false;
        return Scale(r, 10) < range;
    }

    static void AddContents(uwObject o, HashSet<int> into, int depth = 0)
    {
        if (o.is_quant != 0 || o.link <= 0 || depth > 64) return;
        int link = o.link, guard = 0;
        while (link > 0 && guard++ < 1024)
        {
            into.Add(link);
            AddContents(Objs[link], into, depth + 1);
            link = Objs[link].next;
        }
    }

    /// <summary>Obj_GarbageCollect on the tiles as they are now, with a copy of the current rand() state:
    /// the top-level objects it frees, and (in all) those with their contents.</summary>
    HashSet<int> Model(int range, int count, out HashSet<int> all)
    {
        all = new HashSet<int>();
        var freed = new HashSet<int>();
        var r = new BorlandRandSource(((BorlandRandSource)Rng.Source).Seed);
        var m = UWTileMap.current_tilemap;
        for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
            {
                if (Dist(x, y) <= 10 - range) continue;
                int link = m.Tiles[x, y].indexObjectList, guard = 0;
                while (link > 0 && guard++ < 1024)
                {
                    var o = Objs[link];
                    if (Fate(range, o, r))
                    {
                        freed.Add(link);
                        all.Add(link);
                        AddContents(o, all);
                        if (freed.Count >= count) return freed;
                    }
                    link = o.next;
                }
            }
        return freed;
    }

    public override Task Setup(ScenarioContext c)
    {
        if (Rng.Source is not BorlandRandSource) { _problem = "the random source is not seeded"; return Task.CompletedTask; }
        // the least tenacious item among the weapons, armour and the other ordinary items (not critters 0x40-0x7F, doors 0x140 up)
        for (int id = 0; id < 0x140; id++)
            if ((id < 0x40 || id >= 0x80) && (_itemId < 0 || commonObjDat.cullingpriority(id) < commonObjDat.cullingpriority(_itemId))) _itemId = id;
        if (commonObjDat.cullingpriority(_itemId) > 2) { _problem = $"no item of tenacity 2 or less (least {commonObjDat.cullingpriority(_itemId)})"; return Task.CompletedTask; }
        _px = playerdat.playerObject.npc_xhome;
        _py = playerdat.playerObject.npc_yhome;
        for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
                if (Dist(x, y) > 12) _farTiles.Add((x, y));
        if (c.Game == "uw2") { playerdat.SetQuest(50, 0); playerdat.SetQuest(54, 0); }
        c.Note($"item {_itemId} (tenacity {commonObjDat.cullingpriority(_itemId)}), player home tile ({_px},{_py}), {_farTiles.Count} far tiles");
        return Task.CompletedTask;
    }

    public override Task Act(ScenarioContext c)
    {
        if (_problem != null) return Task.CompletedTask;
        var m = UWTileMap.current_tilemap;

        // The mobile items go in the first far tiles of the scan, so they come before the statics.
        var mobileTiles = _farTiles.Take(MobileFar).ToList();
        _farNext = MobileFar;

        // near objects, within 6 tiles, in valid open tiles
        for (int d = 1; d <= 6 && _near.Count < NearCount; d++)
            for (int dx = -d; dx <= d && _near.Count < NearCount; dx++)
            {
                int x = _px + dx, y = _py + (d - System.Math.Abs(dx));
                if (!UWTileMap.ValidTile(x, y) || m.Tiles[x, y].tileType == UWTileMap.TILE_SOLID) continue;
                int s = PopStatic();
                if (s == 0) { _problem = "no static slot for a near object"; return Task.CompletedTask; }
                Place(s, x, y);
                _near.Add(s);
            }
        if (_near.Count < NearCount) { _problem = "not enough open tiles near the player"; return Task.CompletedTask; }

        // (b) sleep
        for (int i = 0; i < SleepFar; i++)
            if (!PlaceFar(PopStatic())) { _problem = "could not place the far objects for the sleep"; return Task.CompletedTask; }
        var before = TileObjects();
        int clock = playerdat.ClockValue;
        sleep.Sleep(0);
        _slept = playerdat.ClockValue != clock;
        var after = TileObjects();
        foreach (var kv in before)
        {
            if (after.ContainsKey(kv.Key) && after[kv.Key] == kv.Value) continue;
            _sleepFreed++;
            if (Dist(kv.Value.x, kv.Value.y) <= 9) _sleepFreedClose++;
        }
        _sleepNearFreed = _near.Count(n => !after.ContainsKey(n));
        c.Note($"sleep: {_sleepFreed} tile objects freed, {_sleepFreedClose} within 9 tiles");

        // (a) static: exhaust, place far statics, allocate one
        var spare = new List<int>();
        int s2;
        while ((s2 = PopStatic()) != 0) spare.Add(s2);
        int placed = 0;
        foreach (int s in spare) { if (placed >= StaticFar) break; if (PlaceFar(s)) placed++; }
        if (placed < StaticFar) { _problem = $"only {placed} far statics placed"; return Task.CompletedTask; }
        _staticWasEmpty = m.StaticFreeListPtr < 0;
        before = TileObjects();
        _staticModel = Model(3, 10, out _staticModelAll);
        _staticSlot = ObjectFreeLists.GetAvailableObjectSlot(ObjectFreeLists.ObjectListType.StaticList);
        after = TileObjects();
        _staticActual = new HashSet<int>(before.Keys.Where(k => !after.ContainsKey(k) || after[k] != before[k]));
        _staticClose = _staticActual.Count(k => Dist(before[k].x, before[k].y) <= 7);
        c.Note($"static: slot {_staticSlot}; model frees [{string.Join(",", _staticModel.OrderBy(i => i))}], port freed [{string.Join(",", _staticActual.OrderBy(i => i))}]");

        // (a) mobile: exhaust, place far mobiles first in scan order, allocate one
        var mobiles = new List<int>();
        while ((s2 = PopMobile()) != 0) mobiles.Add(s2);
        if (mobiles.Count < MobileFar) { _problem = $"only {mobiles.Count} free mobile slots"; return Task.CompletedTask; }
        for (int i = 0; i < MobileFar; i++) Place(mobiles[i], mobileTiles[i].x, mobileTiles[i].y);
        _mobileWasEmpty = m.MobileFreeListPtr < 0;
        before = TileObjects();
        _mobileModel = Model(3, 5, out _mobileModelAll);
        _mobileSlot = ObjectFreeLists.GetAvailableObjectSlot(ObjectFreeLists.ObjectListType.MobileList);
        after = TileObjects();
        _mobileActual = new HashSet<int>(before.Keys.Where(k => !after.ContainsKey(k) || after[k] != before[k]));
        _mobileClose = _mobileActual.Count(k => Dist(before[k].x, before[k].y) <= 7);
        _nearFreedAlloc = _near.Count(n => !after.ContainsKey(n));
        c.Note($"mobile: slot {_mobileSlot}; model frees [{string.Join(",", _mobileModel.OrderBy(i => i))}], port freed [{string.Join(",", _mobileActual.OrderBy(i => i))}]");
        return Task.CompletedTask;
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        if (!_slept) return "precondition: the sleep did not pass any time";
        if (!_staticWasEmpty || !_mobileWasEmpty) return "precondition: a free list was not empty before the allocation";
        if (_staticModel.Count != 10) return $"precondition: the model frees {_staticModel.Count} objects for the static allocation, not 10";
        if (!_staticModelAll.Any(i => i >= 256)) return "precondition: the model frees no static for the static allocation";
        if (_mobileModel.Count != 5) return $"precondition: the model frees {_mobileModel.Count} objects for the mobile allocation, not 5";
        if (!_mobileModelAll.Any(i => i < 256)) return "precondition: the model frees no mobile for the mobile allocation";
        bool uw2 = c.Game == "uw2";
        string gc = uw2 ? "uw2 OBJECTS.C:181-216" : "uw1 OBJECTS.C:188-223";
        string alloc = uw2 ? "uw2 OBJECTS.C:221-239" : "uw1 OBJECTS.C:228-246";
        var k = new Checks();
        k.Equal("tile objects freed by sleeping, Obj_GarbageCollect(1, 20)", 20, _sleepFreed, (uw2 ? "uw2 SKILLS.C:462, " : "uw1 SKILLS.C:544, ") + gc);
        k.Equal("objects within 9 tiles freed by sleeping", 0, _sleepFreedClose + _sleepNearFreed, gc);
        k.True(_staticSlot >= 256 && _staticModelAll.Contains(_staticSlot), $"a static allocation with the free list empty expected a slot freed by Obj_GarbageCollect(3, 10), got {_staticSlot} ({alloc})");
        k.True(_staticActual.SetEquals(_staticModel), $"objects freed for the static allocation expected [{string.Join(",", _staticModel.OrderBy(i => i))}], got [{string.Join(",", _staticActual.OrderBy(i => i))}] ({gc})");
        k.True(_mobileSlot > 0 && _mobileSlot < 256 && _mobileModelAll.Contains(_mobileSlot), $"a mobile allocation with the free list empty expected a slot freed by Obj_GarbageCollect(3, 5), got {_mobileSlot} ({alloc})");
        k.True(_mobileActual.SetEquals(_mobileModel), $"objects freed for the mobile allocation expected [{string.Join(",", _mobileModel.OrderBy(i => i))}], got [{string.Join(",", _mobileActual.OrderBy(i => i))}] ({gc})");
        k.Equal("objects within 7 tiles freed by the allocations", 0, _staticClose + _mobileClose + _nearFreedAlloc, gc);
        return k.Result;
    }
}
