using System.Collections.Generic;
using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// The change terrain rules, which differ between the games (change_terrain).
///
/// UW1 (uw1/src/event/WORLDEV.C:210-265): a height is set only up to 0xD (:225-230), a wall texture below 0x30
/// (:258) and a floor texture below 0xB (:256). Only objects on the changing square itself move, everything but
/// traps and triggers (major class 6): when the floor rises, those below the new floor are lifted to it (:231-242);
/// when it sinks, those resting on the old floor go down with it (:243-255). Nothing is removed or damaged.
///
/// UW2 (uw2/src/event/WORLDEV.C:232-406): a height up to 0xE (:358-359), a wall texture below 0x3F (:370). Objects
/// on the square and those overlapping it from the squares around move, but never traps and triggers or major class
/// 5, doors, furniture and switches (filter, :308). A square that becomes solid removes the static objects it
/// lifts (raise_up, :254-255).
///
/// The scenario finds three open land squares in a row west to east at one floor height h from 2 to 10 with no
/// objects and changes the first (TileInfo.ChangeTile, the port's change_terrain), putting the square back after
/// each case: the height 0xE and 0xD, the wall texture 0x30 and 0x2F; a bench (0x150, major class 5) on it when it
/// rises by 2 and sinks back; a dagger on it the same way; an object of radius 1 or more at fine x 0 in the second
/// square when the first rises (its footprint reaches over the edge); and a dagger on it when it rises by 2 and
/// becomes solid.
/// </summary>
public sealed class ChangeTerrainRules : Scenario
{
    const int Dagger = 0x03;   // ITEM_DAGGER, uw1/src/include/items.h:150, uw2 items.h:135
    const int Bench = 0x150;   // ITEM_BENCH, uw1 items.h:506, uw2 items.h:496

    public override string Name => "change-terrain-rules";
    public override string Cites => "underworld-exhumed uw1/src/event/WORLDEV.C:210-265; uw2/src/event/WORLDEV.C:232-406";

    string _problem;
    int _x, _y, _h, _wide = -1;
    readonly List<(string what, long expected, long actual, string cite)> _checks = new();

    static int Class(TileInfo t) => UWClass._RES == UWClass.GAME_UW2 ? (t.terrain & 0xC0) >> 6 : t.terrain;

    public override Task Setup(ScenarioContext c)
    {
        var map = UWTileMap.current_tilemap;
        for (int y = 2; y < 62 && _h == 0; y++)
            for (int x = 2; x < 60 && _h == 0; x++)
            {
                int h = map.Tiles[x, y].floorHeight;
                if (h < 2 || h > 10) continue;
                bool ok = true;
                for (int i = 0; i < 3; i++)
                {
                    var t = map.Tiles[x + i, y];
                    if (t.tileType != UWTileMap.TILE_OPEN || t.floorHeight != h || t.indexObjectList != 0 || Class(t) != 0) ok = false;
                }
                if (ok) { _x = x; _y = y; _h = h; }
            }
        if (_h == 0) { _problem = "no three empty open land squares in a row at one floor height from 2 to 10"; return Task.CompletedTask; }
        // an object, not a creature, door, trap or animation, whose footprint reaches over a square's edge
        for (int id = 0; id < 0x140 && _wide < 0; id++)
            if ((id >> 6) != 1 && commonObjDat.radius(id) >= 1) _wide = id;
        if (_wide < 0) { _problem = "no object of radius 1 or more"; return Task.CompletedTask; }
        c.Note($"squares ({_x}, {_y}) to ({_x + 2}, {_y}) at floor height {_h}; wall texture {map.Tiles[_x, _y].wallTexture}; wide object 0x{_wide:X} radius {commonObjDat.radius(_wide)}");
        return Task.CompletedTask;
    }

    public override async Task Act(ScenarioContext c)
    {
        if (_problem != null) return;
        bool uw2 = c.Game == "uw2";
        var map = UWTileMap.current_tilemap;
        var tile = map.Tiles[_x, _y];
        short keepWall = tile.wallTexture, keepFloor = tile.floorTexture, keepType = tile.tileType;
        void Restore()
        {
            tile.floorHeight = (short)_h; tile.wallTexture = keepWall; tile.floorTexture = keepFloor; tile.tileType = keepType;
        }
        string g = uw2 ? "uw2 WORLDEV.C" : "uw1 WORLDEV.C";

        // heights
        TileInfo.ChangeTile(_x, _y, newHeight: 0xE);
        _checks.Add(("height after a change to 0xE", uw2 ? 0xE : _h, tile.floorHeight, uw2 ? $"{g}:358-359" : $"at most 0xD: {g}:225-230"));
        Restore();
        TileInfo.ChangeTile(_x, _y, newHeight: 0xD);
        _checks.Add(("height after a change to 0xD", 0xD, tile.floorHeight, uw2 ? $"{g}:358-359" : $"{g}:225-230"));
        Restore();

        // walls
        if (keepWall != 0x30)
        {
            TileInfo.ChangeTile(_x, _y, newWallTexture: 0x30);
            _checks.Add(("wall texture after a change to 0x30", uw2 ? 0x30 : keepWall, tile.wallTexture, uw2 ? $"below 0x3F: {g}:370-371" : $"below 0x30: {g}:258-259"));
            Restore();
        }
        if (keepWall != 0x2F)
        {
            TileInfo.ChangeTile(_x, _y, newWallTexture: 0x2F);
            _checks.Add(("wall texture after a change to 0x2F", 0x2F, tile.wallTexture, uw2 ? $"{g}:370-371" : $"{g}:258-259"));
            Restore();
        }

        // a bench (major class 5) on the square
        var bench = Spawn(Bench, _x, _y, 3, 3);
        if (bench == null) { _problem = "no bench could be made"; return; }
        TileInfo.ChangeTile(_x, _y, newHeight: _h + 2);
        _checks.Add(("bench's height when its square rises by 2", uw2 ? _h << 3 : (_h + 2) << 3, bench.zpos, uw2 ? $"class 5 never moves: filter, {g}:308" : $"all but traps move: {g}:231-242"));
        TileInfo.ChangeTile(_x, _y, newHeight: _h);
        _checks.Add(("bench's height when its square sinks back", _h << 3, bench.zpos, uw2 ? $"{g}:308" : $"{g}:243-255"));
        Remove(bench);
        Restore();

        // a dagger on the square
        var dagger = Spawn(Dagger, _x, _y, 3, 3);
        if (dagger == null) { _problem = "no dagger could be made"; return; }
        TileInfo.ChangeTile(_x, _y, newHeight: _h + 2);
        _checks.Add(("dagger's height when its square rises by 2", (_h + 2) << 3, dagger.zpos, uw2 ? $"raise_up: {g}:248-249" : $"{g}:235-236"));
        TileInfo.ChangeTile(_x, _y, newHeight: _h);
        _checks.Add(("dagger's height when its square sinks back", _h << 3, dagger.zpos, uw2 ? $"SET_Z(obj, height << 3): put_down, {g}:275-276" : $"SET_Z(obj, height << 3): {g}:247-248"));
        Remove(dagger);
        Restore();

        // an object overlapping from the next square east
        var wide = Spawn(_wide, _x + 1, _y, 0, 3);
        if (wide == null) { _problem = $"no object 0x{_wide:X} could be made"; return; }
        TileInfo.ChangeTile(_x, _y, newHeight: _h + 2);
        _checks.Add(($"overlapping object's (0x{_wide:X}) height when the square west of it rises by 2", uw2 ? (_h + 2) << 3 : _h << 3, wide.zpos, uw2 ? $"filter: {g}:323-326, 379-382" : $"only the changing square: {g}:231-232"));
        Remove(wide);
        Restore();

        // a dagger on a square that rises and becomes solid
        dagger = Spawn(Dagger, _x, _y, 3, 3);
        if (dagger == null) { _problem = "no dagger could be made"; return; }
        TileInfo.ChangeTile(_x, _y, newHeight: _h + 2, newType: UWTileMap.TILE_SOLID);
        bool kept = InChain(map.Tiles[_x, _y], dagger.index);
        _checks.Add(("dagger kept on a square that rises and becomes solid (1 kept)", uw2 ? 0 : 1, kept ? 1 : 0, uw2 ? $"Obj_Punt: raise_up, {g}:254-255" : $"nothing removed: {g}:231-242"));
        if (kept) Remove(dagger);
        Restore();
        TileInfo.ChangeTile(_x, _y, newHeight: _h, newType: keepType, newWallTexture: keepWall);
        Restore();

        await c.Frames(1);
    }

    static uwObject Spawn(int item, int x, int y, short fx, short fy)
    {
        var map = UWTileMap.current_tilemap;
        short z = (short)(map.Tiles[x, y].floorHeight << 3);
        var o = ObjectCreator.spawnObjectInTile(item, x, y, fx, fy, z, RenderImmediately: false);
        if (o != null) o.zpos = z;
        return o;
    }

    static bool InChain(TileInfo t, int index)
    {
        var map = UWTileMap.current_tilemap;
        for (int n = t.indexObjectList, guard = 0; n != 0 && guard < 1024; n = map.LevelObjects[n].next, guard++)
            if (n == index) return true;
        return false;
    }

    static void Remove(uwObject o)
    {
        var map = UWTileMap.current_tilemap;
        var t = map.Tiles[o.tileX, o.tileY];
        if (t.indexObjectList == o.index) t.indexObjectList = o.next;
        else
            for (int n = t.indexObjectList, guard = 0; n != 0 && guard < 1024; n = map.LevelObjects[n].next, guard++)
                if (map.LevelObjects[n].next == o.index) { map.LevelObjects[n].next = o.next; break; }
        ObjectFreeLists.ReleaseFreeObject(o);
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        var k = new Checks();
        foreach (var x in _checks) k.Equal(x.what, x.expected, x.actual, x.cite);
        return k.Result;
    }
}
