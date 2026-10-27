using System.Collections.Generic;
using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// Tremor's boulders do not hurt (UW1). The original's Tremor walks the area around the square five ahead of
/// the caster with radius 3 (gronk_area(caster, rollem(8, 3), tremor_area, AREA_RANDOM, 5, 3), uw1/src/combat/SPELLS.C:798),
/// and tremor_area only drops a boulder, 0x154 + rand() % 3, at fine 3/3 and z 0x6E, with no damage of its own
/// (SPELLS.C:646-662). A falling boulder that meets a creature goes through do_objhit, which uses one object on the
/// other only when one of them is usable, else bounces (uw1/src/motion/OBJPHYS.C:136-205); a boulder is no missile,
/// so nothing strikes the creature, and nothing makes it hostile.
///
/// Observed in DOS by Underworld Revisited (PQMarine, MIT): Assets/UWDataImport/UWData/UWMiscSpellRules.cs:398-402
/// (the Tremor scroll on level 1's goblins: no damage, and they did not turn hostile).
///
/// The scenario turns the player north, puts a friendly walking creature on every open square from three before
/// to four after the centre five squares ahead (at most 16), casts Tremor through the port's class 11 dispatch
/// (SpellCasting.CastClassB_Spells, minor 9), and lets five seconds pass. At least one boulder must have come
/// down on a creature's square; no creature may have lost hit points or turned hostile.
/// </summary>
public sealed class TremorHarmless : Scenario
{
    public override string Name => "tremor-harmless";
    public override string[] Games => new[] { "uw1" };
    public override string Cites => "underworld-exhumed uw1/src/combat/SPELLS.C:646-662, 798; uw1/src/motion/OBJPHYS.C:136-205";

    string _problem;
    readonly List<(uwObject o, int hp, int x, int y)> _critters = new();
    int _boulders, _onCritters, _before, _id;
    readonly List<string> _changed = new();

    static bool IsBoulder(int id) => id >= 0x154 && id <= 0x156;

    int OnCritterSquares(UWTileMap map)
    {
        int n = 0;
        var seen = new HashSet<(int, int)>();
        foreach (var (o, hp, x, y) in _critters)
            if (seen.Add((x, y)))
                for (int i = map.Tiles[x, y].indexObjectList, g = 0; i != 0 && g < 256; g++, i = map.LevelObjects[i].next)
                    if (IsBoulder(map.LevelObjects[i].item_id)) n++;
        return n;
    }

    public override async Task Setup(ScenarioContext c)
    {
        await Guards.CameraOnPlayer(c);
        _id = BatchB.FindCritter(id => BatchA.Walker(id) && critterObjectDat.avghit(id) >= 10);
        if (_id < 0) { _problem = "no walking creature type"; return; }
        var p = playerdat.playerObject;
        p.heading = 0; p.npc_heading = 0;
        int cx = p.npc_xhome, cy = p.npc_yhome;
        motion.GetCoordinateInDirection(0, 5, ref cx, ref cy);
        var tiles = UWTileMap.current_tilemap.Tiles;
        for (int x = cx - 3; x <= cx + 4 && _critters.Count < 16; x++)
            for (int y = cy - 3; y <= cy + 4 && _critters.Count < 16; y++)
            {
                if (!UWTileMap.ValidTile(x, y) || (x == p.tileX && y == p.tileY)) continue;
                if (tiles[x, y].tileType != UWTileMap.TILE_OPEN) continue;
                var o = BatchB.SpawnCritter(_id, x, y);
                if (o == null) break;
                o.npc_attitude = 3; o.IsAlly = 0; o.npc_goal = 0; o.npc_gtarg = 0; o.npc_hp = 0xC8;
                _critters.Add((o, o.npc_hp, x, y));
            }
        if (_critters.Count == 0) _problem = $"no open square around ({cx}, {cy})";
        else c.Note($"creature 0x{_id:X}, {_critters.Count} of them around the centre ({cx}, {cy})");
    }

    public override async Task Act(ScenarioContext c)
    {
        if (_problem != null) return;
        var map = UWTileMap.current_tilemap;
        _before = OnCritterSquares(map);
        SpellCasting.CastClassB_Spells(playerdat.playerObject, 9, 0);
        await c.Frames(300);
        _onCritters = OnCritterSquares(map) - _before;
        foreach (var (o, hp, x, y) in _critters)
        {
            if (o.npc_hp != hp || o.npc_attitude == 0)
                _changed.Add($"({o.tileX}, {o.tileY}) hit points {hp} to {o.npc_hp}, attitude {o.npc_attitude}");
        }
        for (int x = 0; x < 64; x++)
            for (int y = 0; y < 64; y++)
                for (int i = map.Tiles[x, y].indexObjectList, g = 0; i != 0 && g < 256; g++, i = map.LevelObjects[i].next)
                    if (IsBoulder(map.LevelObjects[i].item_id)) _boulders++;
        c.Note($"{_boulders} boulders in the level, {_onCritters} new on creatures' squares; changed: {(_changed.Count == 0 ? "none" : string.Join(", ", _changed))}");
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        if (_onCritters == 0) return $"precondition: no boulder came down on a creature's square ({_boulders} in the level)";
        var k = new Checks();
        k.True(_changed.Count == 0, $"creatures under Tremor's boulders were hurt or angered: {string.Join(", ", _changed)} (tremor_area deals no damage: uw1 SPELLS.C:646-662; do_objhit: OBJPHYS.C:186-205)");
        return k.Result;
    }
}
