using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// Which creature Summon Monster calls up. The original's creat_spell, case 4, takes lvl as the
/// caster's Casting (a critter caster's dungeon level * 4), at least 2, and picks item
/// lvl + rand() % lvl + 0x40, rerolling until the creature has hit points and is not a swimmer,
/// a bA_1 creature or item 0x7B or 0x7C (uw1/src/combat/SPELLS.C:605-612; uw2/src/combat/SPELLS2.C:511-518).
/// So a player with Casting 10 summons a creature from 0x4A to 0x53, never one below.
///
/// The scenario turns the player to face open floor (the summon lands 12 fine units ahead in UW1, 9
/// in UW2, give or take a little), sets Casting 10, and for a few seeds casts Summon Monster through
/// the port's SpellCasting.CastClass8_Summoning(4, player), and checks the creature that appeared.
///
/// The port once picked 0 to lvl - 1 (src/magic/spellcasting_class_8.cs:84): rats and bats where the
/// original matches the creature to the caster's skill.
/// </summary>
public sealed class SummonMonsterLevel : Scenario
{
    const int Casting = 10;
    static readonly uint[] Seeds = { 3, 31, 314 };

    public override string Name => "summon-monster";
    public override string Cites => "underworld-exhumed uw1/src/combat/SPELLS.C:570-640 (creat_spell, case 4 at 605-612); uw2/src/combat/SPELLS2.C:511-518";

    string _problem;
    readonly List<int> _summoned = new();
    int _casts, _heading;
    bool _room;

    public override Task Setup(ScenarioContext c)
    {
        var p = playerdat.playerObject;
        int dist = c.Game == "uw1" ? 0xC : 9;
        int x0 = (p.npc_xhome << 3) + p.xpos, y0 = (p.npc_yhome << 3) + p.ypos;
        for (int h = 0; h < 8; h++)
        {
            bool open = true;
            foreach (int turn in new[] { -14, 0, 14 })
            {
                int x = x0, y = y0;
                motion.GetCoordinateInDirection(((h << 5) + turn) & 0xFF, dist, ref x, ref y);
                int tx = x >> 3, ty = y >> 3;
                if (!UWTileMap.ValidTile(tx, ty) || UWTileMap.current_tilemap.Tiles[tx, ty].tileType != UWTileMap.TILE_OPEN) open = false;
            }
            if (open)
            {
                // Room for a creature the original could summon, straight ahead (can_place, SPELLS.C:610).
                int fx = x0, fy = y0;
                motion.GetCoordinateInDirection(h << 5, dist, ref fx, ref fy);
                int floor = UWTileMap.current_tilemap.Tiles[fx >> 3, fy >> 3].floorHeight << 3;
                _room = Enumerable.Range(0x40 + Casting, Casting).Any(id => critterObjectDat.avghit(id) > 0
                    && motion.TestIfObjectFitsInTile(id, 0, fx, fy, floor, 1, 8));
                if (!_room) continue;
                _heading = h;
                playerdat.Casting = Casting;
                return Task.CompletedTask;
            }
        }
        _problem = "no heading from the start position faces open floor with room for a creature 0x4A..0x53";
        return Task.CompletedTask;
    }

    /// <summary>The critters linked into some square's object chain, the live ones.</summary>
    static HashSet<int> Critters()
    {
        var set = new HashSet<int>();
        var map = UWTileMap.current_tilemap;
        for (int x = 0; x < 64; x++)
            for (int y = 0; y < 64; y++)
                for (int i = map.Tiles[x, y].indexObjectList, guard = 0; i != 0 && guard < 1024; guard++)
                {
                    var o = map.LevelObjects[i];
                    if (o == null) break;
                    if (o.majorclass == 1) set.Add(i);
                    i = o.next;
                }
        return set;
    }

    public override Task Act(ScenarioContext c)
    {
        if (_problem != null) return Task.CompletedTask;
        foreach (uint seed in Seeds)
        {
            var before = Critters();
            playerdat.playerObject.heading = (short)_heading;   // the camera can turn the player between frames
            playerdat.playerObject.npc_heading = 0;
            BatchB.Seed(seed);
            SpellCasting.CastClass8_Summoning(4, playerdat.playerObject);
            _casts++;
            foreach (int i in Critters().Except(before)) _summoned.Add(UWTileMap.current_tilemap.LevelObjects[i].item_id);
        }
        var valid = Enumerable.Range(0x40, 2 * Casting).Where(id => critterObjectDat.avghit(id) != 0 && !critterObjectDat.isSwimmer(id)
            && !critterObjectDat.unkPassivenessProperty(id) && id != 0x7B && id != 0x7C).Select(id => $"0x{id:X}");
        c.Note($"heading {_heading}, room ahead {_room}; creatures 0x40..0x{0x3F + 2 * Casting:X} that pass the reroll test: {string.Join(" ", valid)}");
        c.Note($"Casting {Casting}: {_casts} casts summoned {string.Join(", ", _summoned.Select(i => $"0x{i:X}"))}");
        return Task.CompletedTask;
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        if (!_room) return "precondition: no room straight ahead for any creature 0x4A..0x53";
        string cite = c.Game == "uw1" ? "uw1 SPELLS.C:605-612, lvl + rand() % lvl + 0x40" : "uw2 SPELLS2.C:511-518, lvl + rand() % lvl + 0x40";
        var k = new Checks();
        k.True(_summoned.Count > 0, $"{_casts} casts of Summon Monster at Casting {Casting} expected creatures from 0x{0x40 + Casting:X} to 0x{0x3F + 2 * Casting:X} (rerolled until valid), none appeared ({cite})");
        foreach (int id in _summoned)
            k.Within($"creature summoned at Casting {Casting}", 0x40 + Casting, 0x40 + 2 * Casting - 1, id, cite);
        return k.Result;
    }
}
