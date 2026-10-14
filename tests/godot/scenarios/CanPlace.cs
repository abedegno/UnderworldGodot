using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// can_place, the original's "does this object fit here" test (uw1/src/motion/COLLIDE.C:578-640,
/// uw2/src/motion/COLLIDE.C:564-626, the same code in both games), against the port's
/// motion.TestIfObjectFitsInTile (src/physics/motion_projectile.cs), called directly with crafted
/// inputs on empty open squares of level 1. Teleports, summons, drops and critter moves use it.
///
/// 1. Ceiling: the object does not fit when height + z > 0x7F (height 0x80 excepted)
///    (COLLIDE.C uw1:593, uw2:579). The port tested the height alone.
/// 2. Terrain result: on the floor nvokTerr is 1 << (the terrain class, bits 0-1 of hits0)
///    (uw1:607, uw2:593). The port shifted by the whole of hits0.
/// 3. Footprint in the air: ObjectCheck gets the point footprint for a flat static object only
///    when nvokTerr is not 0x10 (FOOT_AIR), so in the air the object's radius counts
///    (uw1:609-610, uw2:595-596). The port compared with 0x16, so a flat static object in the air
///    used a point and missed a neighbour in its radius.
/// 4. Resting object: nvokHgt and its object ("best") change only when a collision underfoot
///    is higher than nvokHgt so far, and only that object must be solid (uw1:615-631,
///    uw2:601-617). The port took the last object underfoot whatever its height, so a
///    non-solid object lying no higher than the floor refused the placement.
/// </summary>
public sealed class CanPlace : Scenario
{
    public override string Name => "can-place";
    public override string Cites => "underworld-exhumed uw1/src/motion/COLLIDE.C:578-640; uw2/src/motion/COLLIDE.C:564-626";

    string _problem;
    readonly List<(int x, int y)> _tiles = new();
    readonly List<string> _notes = new();

    // results
    bool _ceilOver, _ceilUnder, _floorFits, _airFits, _restFits;
    int _floorTerr, _expectTerr;
    int _ceilItem, _ceilZ, _airItem, _airRadius, _blocker, _rest;

    static bool Creature(int id) => id >= 0x40 && id < 0x80;

    /// <summary>An open square at least minZ high with no objects in it or its eight neighbours (what ObjectCheck scans), away from earlier picks. A footprint of radius 3 or less at the centre stays inside the square, so the neighbours' shape does not matter.</summary>
    (int x, int y) FindTile(int minZ)
    {
        var map = UWTileMap.current_tilemap;
        for (int y = 2; y < 62; y++)
            for (int x = 2; x < 62; x++)
            {
                if (map.Tiles[x, y].tileType != UWTileMap.TILE_OPEN || (map.Tiles[x, y].floorHeight << 3) < minZ) continue;
                bool ok = true;
                foreach (var t in _tiles)
                    if (Math.Max(Math.Abs(t.x - x), Math.Abs(t.y - y)) < 5) ok = false;
                for (int i = -1; i <= 1 && ok; i++)
                    for (int j = -1; j <= 1 && ok; j++)
                    {
                        if (map.Tiles[x + i, y + j].indexObjectList != 0) ok = false;
                    }
                if (ok) { _tiles.Add((x, y)); return (x, y); }
            }
        return (-1, -1);
    }

    public override Task Setup(ScenarioContext c)
    {
        // Items, from COMOBJ.DAT: any object with a height for the ceiling test; a flat object with
        // a radius for the footprint test, and a touchable one tall enough to stand in its way; a
        // touchable non-solid object with a height for the resting test.
        _ceilItem = _airItem = _blocker = _rest = -1;
        for (int id = 0; id < 0x180; id++)
        {
            if (Creature(id)) continue;
            int h = commonObjDat.height(id), r = commonObjDat.radius(id);
            bool touch = commonObjDat.Unk6_0_maybecancollide(id), solid = commonObjDat.UnknownFlag3_1(id);
            if (_ceilItem < 0 && h > 0 && h < 0x40) _ceilItem = id;
            if (_airItem < 0 && h == 0 && r > 0 && r < 4) _airItem = id;
            if (_rest < 0 && touch && !solid && h > 0 && h <= 16) _rest = id;
        }
        if (_ceilItem < 0 || _airItem < 0 || _rest < 0) { _problem = $"no suitable items (ceiling 0x{_ceilItem:X}, flat with radius 0x{_airItem:X}, non-solid touchable 0x{_rest:X})"; return Task.CompletedTask; }
        _airRadius = commonObjDat.radius(_airItem);
        // The blocker sits just inside the flat object's radius, in the same square (fine x 4 + both radii <= 7).
        for (int id = 0; id < 0x180 && _blocker < 0; id++)
        {
            int r = commonObjDat.radius(id);
            if (!Creature(id) && commonObjDat.Unk6_0_maybecancollide(id) && r > 0 && 4 + _airRadius + r <= 7 && commonObjDat.height(id) >= _airRadius + 2) _blocker = id;
        }
        if (_blocker < 0) { _problem = $"no touchable object taller than {_airRadius + 1} with radius 1..{3 - _airRadius}"; return Task.CompletedTask; }
        c.Note($"items: ceiling 0x{_ceilItem:X} (height {commonObjDat.height(_ceilItem)}), flat 0x{_airItem:X} (radius {_airRadius}), blocker 0x{_blocker:X} (radius {commonObjDat.radius(_blocker)}, height {commonObjDat.height(_blocker)}), non-solid 0x{_rest:X} (height {commonObjDat.height(_rest)})");

        var a = FindTile(0);
        var b = FindTile(0);
        var d = FindTile(commonObjDat.height(_rest));
        if (a.x < 0 || b.x < 0 || d.x < 0) { _problem = $"not enough empty open squares with empty neighbours (found {_tiles.Count} of 3)"; return Task.CompletedTask; }
        c.Note($"squares: ceiling and terrain ({a.x}, {a.y}), footprint ({b.x}, {b.y}), resting ({d.x}, {d.y})");
        return Task.CompletedTask;
    }

    public override Task Act(ScenarioContext c)
    {
        if (_problem != null) return Task.CompletedTask;
        var map = UWTileMap.current_tilemap;

        // 1, 2: the ceiling and the terrain result, at the centre of the first square.
        var (ax, ay) = _tiles[0];
        int fa = map.Tiles[ax, ay].floorHeight << 3;
        int cx = (ax << 3) + 4, cy = (ay << 3) + 4;
        int h = commonObjDat.height(_ceilItem);
        _ceilZ = 0x80 - h;
        _ceilOver = motion.TestIfObjectFitsInTile(_ceilItem, 0, cx, cy, _ceilZ, 1, 8);
        _ceilUnder = motion.TestIfObjectFitsInTile(_ceilItem, 0, cx, cy, _ceilZ - 1, 1, 8);
        _floorFits = motion.TestIfObjectFitsInTile(_ceilItem, 0, cx, cy, fa, 1, 8);
        _floorTerr = UWMotionParamArray.dseg_67d6_2586;
        _expectTerr = 1 << (TerrainDatLoader.GetTerrainTypeNo(map.Tiles[ax, ay]) & 3);

        // 3: a flat static object in the air, a touchable object just inside its radius but not at its centre.
        var (bx, by) = _tiles[1];
        int fb = map.Tiles[bx, by].floorHeight << 3;
        var flat = ObjectCreator.spawnObjectInTile(_airItem, bx, by, 4, 4, (short)fb);
        int rb = commonObjDat.radius(_blocker);
        int X = (bx << 3) + 4 + _airRadius + rb;
        var blocker = ObjectCreator.spawnObjectInTile(_blocker, bx, by, (short)(X & 7), 4, (short)fb);
        if (flat == null || blocker == null) { _problem = "could not make the objects for the footprint test"; return Task.CompletedTask; }
        _airFits = motion.TestIfObjectFitsInTile(_airItem, flat.index, (bx << 3) + 4, (by << 3) + 4, fb + _airRadius + 1, 1, 0);
        c.Note($"footprint: flat object index 0x{flat.index:X} at z {fb + _airRadius + 1}, blocker at fine x {X} (centre {(bx << 3) + 4}), z {fb}");

        // 4: a non-solid object lying with its top at the floor, under an object placed on the floor.
        var (dx, dy) = _tiles[2];
        int fd = map.Tiles[dx, dy].floorHeight << 3;
        var rest = ObjectCreator.spawnObjectInTile(_rest, dx, dy, 4, 4, (short)(fd - commonObjDat.height(_rest)));
        if (rest == null) { _problem = "could not make the object for the resting test"; return Task.CompletedTask; }
        _restFits = motion.TestIfObjectFitsInTile(_ceilItem, 0, (dx << 3) + 4, (dy << 3) + 4, fd, 1, 8);
        c.Note($"ceiling: fits at z {_ceilZ} {_ceilOver}, at z {_ceilZ - 1} {_ceilUnder}; floor z {fa}: fits {_floorFits}, nvokTerr 0x{_floorTerr:X}; footprint fits {_airFits}; resting fits {_restFits}");
        return Task.CompletedTask;
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        string g = c.Game;
        string L(int l1, int l2) => g == "uw1" ? $"uw1 COLLIDE.C:{l1}" : $"uw2 COLLIDE.C:{l2}";
        var k = new Checks();
        k.True(_ceilUnder, $"precondition: item 0x{_ceilItem:X} at z {_ceilZ - 1} (height + z = 0x7F) expected to fit");
        k.True(_floorFits, $"precondition: item 0x{_ceilItem:X} on the floor of an empty square expected to fit");
        if (k.Result != null) return k.Result;
        k.True(!_ceilOver, $"item 0x{_ceilItem:X} at z {_ceilZ}: height + z = 0x80 > 0x7F expected not to fit, it fitted ({L(593, 579)})");
        k.Equal("nvokTerr on the floor (1 << terrain class)", _expectTerr, _floorTerr, L(607, 593));
        k.True(!_airFits, $"flat static object in the air with an object inside its radius expected not to fit (the air footprint is its radius, nvokTerr 0x10), it fitted ({L(609, 595)}-{(g == "uw1" ? 610 : 596)})");
        k.True(_restFits, $"object on the floor above a non-solid object whose top is the floor expected to fit (best only for a higher top), it did not ({L(615, 601)}-{(g == "uw1" ? 631 : 617)})");
        return k.Result;
    }
}
