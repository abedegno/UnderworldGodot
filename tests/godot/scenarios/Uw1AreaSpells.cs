using System.Collections.Generic;
using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// How far UW1's area spells reach. The original's nail_area acts 3d4 times through gronk_area
/// with radius 2 around the square 4 ahead of the caster (uw1/src/combat/SPELLS.C:543-551,
/// 487-512), and process_area visits x0..x0 + w and y0..y0 + h with w = h = 2 * 2 + 1, so the squares
/// from 2 before to 3 after the centre each way (SPELLS.C:416-485). Mass Confusion (class 6 minor
/// 3, target mode 0: every critter but the caster) calls sp_confusion on each critter there, which
/// unless resisted gives it goal 2 and attitude 1 (hit_critter_goal, SPELLS.C:366-375, 392-396); a
/// critter whose resist byte has no magic bits never resists (check_res draws nothing then).
///
/// The scenario turns the player to a heading whose centre square has three open squares inside the
/// original's area but outside the port's (the centre's own column and the two after it), puts a
/// critter that cannot resist magic, made friendly, in each, and casts Mass Confusion through the
/// port's SpellCasting.CastClass6_SpellsAroundPlayer(3). With at least 3 actions and 3 critters, all
/// three must be upset (attitude 1). A fourth critter at the centre, inside both areas, is the
/// control: it must be upset, or the spell did not act at all.
///
/// UW1 only. The port once gave UW1's area spells a count of 2 and left the radius 0
/// (src/magic/spellcasting_class_6.cs:23, 64-66, 85), so the area was the 2 by 3 patch at the centre.
/// </summary>
public sealed class Uw1AreaSpells : Scenario
{
    public override string Name => "uw1-area-spells";
    public override string[] Games => new[] { "uw1" };
    public override string Cites => "underworld-exhumed uw1/src/combat/SPELLS.C:543-551 (nail_area), 487-512 (gronk_area), 416-485 (process_area), 366-375, 392-396";

    string _problem;
    int _heading, _cx, _cy, _critterId;
    readonly List<uwObject> _critters = new();
    int _upset;
    uwObject _control;
    uint _seed;

    public override Task Setup(ScenarioContext c)
    {
        _critterId = BatchB.FindCritter(id => (commonObjDat.scaleresistances(id) & 3) == 0);
        if (_critterId < 0) { _problem = "no critter type without magic resistance"; return Task.CompletedTask; }
        var p = playerdat.playerObject;
        var tiles = UWTileMap.current_tilemap.Tiles;
        for (int h = 0; h < 8; h++)
        {
            int cx = p.npc_xhome, cy = p.npc_yhome;   // OBJ_HOMEX, OBJ_HOMEY, as gronk_area starts (SPELLS.C:498-500)
            motion.GetCoordinateInDirection(h << 5, 4, ref cx, ref cy);
            var squares = new List<(int, int)>();
            for (int x = cx - 2; x <= cx + 3; x++)
                for (int y = cy - 2; y <= cy + 3; y++)
                {
                    bool portBox = x >= cx && x <= cx + 1 && y >= cy && y <= cy + 2;
                    if (portBox || (x == p.tileX && y == p.tileY) || !UWTileMap.ValidTile(x, y)) continue;
                    if (tiles[x, y].tileType == UWTileMap.TILE_OPEN && squares.Count < 3) squares.Add((x, y));
                }
            if (squares.Count < 3) continue;
            if (!UWTileMap.ValidTile(cx, cy) || tiles[cx, cy].tileType != UWTileMap.TILE_OPEN || (cx == p.tileX && cy == p.tileY)) continue;
            _heading = h; _cx = cx; _cy = cy;
            _control = BatchB.SpawnCritter(_critterId, cx, cy);
            if (_control == null) { _problem = "the control critter could not be made"; return Task.CompletedTask; }
            _control.npc_attitude = 3;
            _control.IsAlly = 0;
            foreach (var (x, y) in squares)
            {
                var critter = BatchB.SpawnCritter(_critterId, x, y);
                if (critter == null) { _problem = "a critter could not be made"; return Task.CompletedTask; }
                critter.npc_attitude = 3;
                critter.IsAlly = 0;
                _critters.Add(critter);
            }
            return Task.CompletedTask;
        }
        _problem = "no heading has three open squares in the original's area outside the port's";
        return Task.CompletedTask;
    }

    public override Task Act(ScenarioContext c)
    {
        if (_problem != null) return Task.CompletedTask;
        playerdat.playerObject.heading = (short)_heading;
        playerdat.playerObject.npc_heading = 0;
        // A seed whose 3d4 (the original's first draws, SPELLS.C:549) is at least 4, so the original
        // reaches all four critters whatever their order.
        for (_seed = 1; new BatchB.DosRand(_seed).Rollem(3, 4) < 4; _seed++) { }
        BatchB.Seed(_seed);
        SpellCasting.CastClass6_SpellsAroundPlayer(3);
        foreach (var cr in _critters) if (cr.npc_attitude == 1) _upset++;
        var where = new List<string>();
        foreach (var cr in _critters) where.Add($"({cr.tileX},{cr.tileY}) attitude {cr.npc_attitude}");
        c.Note($"critter 0x{_critterId:X}, heading {_heading}, seed {_seed}, centre ({_cx},{_cy}) control attitude {_control.npc_attitude}; outside the patch: {string.Join(", ", where)}");
        return Task.CompletedTask;
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        if (_control.npc_attitude != 1) return $"precondition: the control critter at the centre ({_cx},{_cy}) was not upset (attitude {_control.npc_attitude}), so the spell itself did not act";
        var k = new Checks();
        k.Equal("critters upset by Mass Confusion within 2 squares of its centre (outside the 2 by 3 patch)", _critters.Count, _upset,
            "uw1 SPELLS.C:549-550, 3d4 actions over radius 2; SPELLS.C:509, 465-482");
        return k.Result;
    }
}
