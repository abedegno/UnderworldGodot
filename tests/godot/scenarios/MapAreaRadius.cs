using System.Collections.Generic;
using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// How much Map Area maps. The original's special_spells case 7 takes r = Casting, adds
/// Casting - 13 when Casting is above 15, then r = r / 5 + 2, and maps every square (x, y) with
/// (x - px)^2 + (y - py)^2 &lt;= r^2 through automap_area and clip_circle
/// (uw2/src/combat/SPELLS.C:991-1004, 924-934; uw2/src/ui/AUTOMAP.C:130-170), recording each
/// square's own tile type, whether the player could reach it or not.
///
/// For each Casting value the scenario marks the automap squares within r + 3 of the player
/// unmapped (solid), casts Map Area through the port's SpellCasting.CastSpell (class 13, minor 7),
/// and counts the non-solid squares inside the original's circle that are now mapped with their
/// tile type, and the squares outside it that were mapped at all.
///
/// The port once used Max(Casting - 13, 0) / 5 + 2, a square, flood filled from the player
/// (src/magic/spellcasting_class_13.cs:71, src/World/automap.cs:115): radius 3 for 7 at Casting 20.
/// </summary>
public sealed class MapAreaRadius : Scenario
{
    static readonly int[] CastingValues = { 10, 20, 30 };

    public override string Name => "map-area-radius";
    public override string[] Games => new[] { "uw2" };
    public override string Cites => "underworld-exhumed uw2/src/combat/SPELLS.C:991-1004, 924-934; uw2/src/ui/AUTOMAP.C:130-170";

    readonly List<(int casting, int r, int inside, int mappedInside, int mappedOutside)> _results = new();
    int _px, _py, _level;

    static int Radius(int casting) { int r = casting; r += r > 15 ? r - 13 : 0; return r / 5 + 2; }

    public override Task Setup(ScenarioContext c)
    {
        _px = playerdat.playerObject.tileX;
        _py = playerdat.playerObject.tileY;
        _level = playerdat.dungeon_level;
        return Task.CompletedTask;
    }

    public override Task Act(ScenarioContext c)
    {
        var map = automap.automaps[_level - 1];
        var tiles = UWTileMap.current_tilemap.Tiles;
        foreach (int casting in CastingValues)
        {
            int r = Radius(casting), box = r + 3;
            for (int x = _px - box; x <= _px + box; x++)
                for (int y = _py - box; y <= _py + box; y++)
                    if (UWTileMap.ValidTile(x, y)) automap.MarkTileVisited(_level - 1, x, y, UWTileMap.TILE_SOLID);
            playerdat.Casting = casting;
            SpellCasting.CastSpell(majorclass: 13, minorclass: 7, caster: playerdat.playerObject, target: null,
                tileX: _px, tileY: _py, CastOnEquip: false);
            int inside = 0, mappedInside = 0, mappedOutside = 0;
            for (int x = _px - box; x <= _px + box; x++)
                for (int y = _py - box; y <= _py + box; y++)
                {
                    if (x < 1 || x >= 0x3F || y < 1 || y >= 0x3F) continue;   // AUTOMAP.C:144-151
                    int dx = x - _px, dy = y - _py;
                    bool inCircle = dx * dx + dy * dy <= r * r;
                    int type = map.tiles[x, y].tileType;
                    if (inCircle && tiles[x, y].tileType != UWTileMap.TILE_SOLID)
                    {
                        inside++;
                        if (type == tiles[x, y].tileType) mappedInside++;
                    }
                    if (!inCircle && type != UWTileMap.TILE_SOLID) mappedOutside++;
                }
            _results.Add((casting, r, inside, mappedInside, mappedOutside));
            c.Note($"Casting {casting}: radius {r}, {mappedInside} of {inside} open squares in the circle mapped, {mappedOutside} squares outside it mapped");
        }
        return Task.CompletedTask;
    }

    public override string Assert(ScenarioContext c)
    {
        if (_level < 1 || _level > 0x40) return $"precondition: dungeon level {_level}";
        var k = new Checks();
        foreach (var r in _results)
        {
            if (r.inside == 0) return $"precondition: no open square within radius {r.r} of the player";
            k.Equal($"open squares mapped within the radius {r.r} circle at Casting {r.casting}", r.inside, r.mappedInside, "uw2 SPELLS.C:996-1002, radius (Casting + (Casting > 15 ? Casting - 13 : 0)) / 5 + 2");
            k.Equal($"squares mapped outside the radius {r.r} circle at Casting {r.casting}", 0, r.mappedOutside, "uw2 SPELLS.C:924-934, clip_circle");
        }
        return k.Result;
    }
}
