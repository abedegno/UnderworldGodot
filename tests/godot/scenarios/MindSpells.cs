using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// What Cause Fear, Paralyze and Poison do to a critter that does not resist them.
///
/// UW1: sp_fear is hit_critter_goal(GOAL_FLEE, -1, ...), which gives goal 6 for gtarg 1 and, the
/// attitude being -1, leaves the attitude alone (uw1/src/combat/SPELLS.C:366-375, 399-403). sp_hold
/// is hit_critter_goal(GOAL_STAND_7, ATT_UPSET, ...): goal 7, gtarg 1 and attitude 1
/// (SPELLS.C:406-411, 370). sp_poison damages the critter with the caster as the source
/// (SPELLS.C:355-361, damage_item(target, Obj_IntTMem(src), ...)).
///
/// UW2: sp_fear sets attitude 1 before hit_critter_goal (uw2/src/combat/SPELLS.C:536-539), and
/// sp_poison also passes the caster (SPELLS.C:351-359).
///
/// UW1 casts through SpellCasting.CastSpell as the cast button does once its checks have passed; the
/// spell finds the critter put 4 squares ahead (nail_1area, uw1 SPELLS.C:553-559). UW2 casts the same
/// way and then clicks the critter. The critter has no magic resistance, so check_res never lets it
/// shrug a spell off, and it starts each cast with goal 0, gtarg 0 and attitude 2.
///
/// The port once set attitude 1 for Cause Fear in UW1, left UW1 Paralyze's gtarg alone and gave
/// Poison damage source 0 (src/magic/spellcasting_class_7.cs).
/// </summary>
public sealed class MindSpells : Scenario
{
    const int Fear = 1, Poison = 4, Paralyze = 5;   // class 7 minors
    const int StartAttitude = 2;

    public override string Name => "mind-spells";
    public override string Cites => "underworld-exhumed uw1/src/combat/SPELLS.C:355-375, 399-411; uw2/src/combat/SPELLS.C:351-359, 536-539";

    string _problem;
    uwObject _critter;
    int _heading;
    readonly Dictionary<int, (int goal, int gtarg, int attitude, int source)> _after = new();

    public override Task Setup(ScenarioContext c)
    {
        int critterId = BatchB.FindCritter(id => (commonObjDat.scaleresistances(id) & 3) == 0);
        if (critterId < 0) { _problem = "no critter type without magic resistance"; return Task.CompletedTask; }
        var p = playerdat.playerObject;
        var tiles = UWTileMap.current_tilemap.Tiles;
        if (c.Game == "uw2")
        {
            var (x, y) = BatchB.OpenSquareNearPlayer();
            _critter = BatchB.SpawnCritter(critterId, x, y);
        }
        else
        {
            for (int h = 0; h < 8 && _critter == null; h++)
            {
                int cx = p.npc_xhome, cy = p.npc_yhome;
                motion.GetCoordinateInDirection(h << 5, 4, ref cx, ref cy);
                if (!UWTileMap.ValidTile(cx, cy) || tiles[cx, cy].tileType != UWTileMap.TILE_OPEN || (cx == p.tileX && cy == p.tileY)) continue;
                bool occupied = false;
                for (int x = cx - 2; x <= cx + 2; x++)
                    for (int y = cy - 2; y <= cy + 2; y++)
                    {
                        if (!UWTileMap.ValidTile(x, y)) continue;
                        for (int i = tiles[x, y].indexObjectList; i != 0; i = UWTileMap.current_tilemap.LevelObjects[i].next)
                        {
                            var o = UWTileMap.current_tilemap.LevelObjects[i];
                            if (o.majorclass == 1 && o.index != 1) occupied = true;
                        }
                    }
                if (occupied) continue;
                _heading = h;
                _critter = BatchB.SpawnCritter(critterId, cx, cy);
            }
        }
        if (_critter == null) _problem = "no square for the critter";
        else c.Note($"critter 0x{critterId:X} at ({_critter.tileX},{_critter.tileY})");
        return Task.CompletedTask;
    }

    void Cast(int minor)
    {
        BatchB.ReadyCaster();
        _critter.npc_goal = 0;
        _critter.npc_gtarg = 0;
        _critter.npc_attitude = StartAttitude;
        _critter.npc_hp = 200;
        _critter.ProjectileSourceID = 0;
        var p = playerdat.playerObject;
        p.heading = (short)_heading;
        p.npc_heading = 0;
        RunicMagic.PendingSpellCost = 3;
        SpellCasting.CastSpell(7, minor, p, null, p.tileX, p.tileY, false);
        if (SpellCasting.currentSpell != null)
            SpellCasting.CastCurrentSpellOnRayCastTarget(_critter.index, UWTileMap.current_tilemap.LevelObjects, true);
        SpellCasting.currentSpell = null;
        RunicMagic.PendingSpellCost = 0;
        uimanager.instance.mousecursor.SetCursorToCursor();
        _after[minor] = (_critter.npc_goal, _critter.npc_gtarg, _critter.npc_attitude, _critter.ProjectileSourceID);
    }

    public override Task Act(ScenarioContext c)
    {
        if (_problem != null) return Task.CompletedTask;
        foreach (int minor in new[] { Fear, Paralyze, Poison })
        {
            Cast(minor);
            var a = _after[minor];
            c.Note($"class 7 minor {minor}: goal {a.goal}, gtarg {a.gtarg}, attitude {a.attitude}, damage source {a.source}");
        }
        return Task.CompletedTask;
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        var k = new Checks();
        int player = playerdat.playerObject.index;
        if (c.Game == "uw1")
        {
            if (_after[Fear].goal != 6) return $"precondition: Cause Fear did not reach the critter (goal {_after[Fear].goal})";
            k.Equal("UW1 Cause Fear: attitude (sp_fear passes -1, so unchanged)", StartAttitude, _after[Fear].attitude, "uw1 SPELLS.C:399-403, 371-372");
            k.Equal("UW1 Cause Fear: gtarg", 1, _after[Fear].gtarg, "uw1 SPELLS.C:370");
            k.Equal("UW1 Paralyze: goal", 7, _after[Paralyze].goal, "uw1 SPELLS.C:406-411");
            k.Equal("UW1 Paralyze: gtarg", 1, _after[Paralyze].gtarg, "uw1 SPELLS.C:370");
            k.Equal("UW1 Paralyze: attitude", 1, _after[Paralyze].attitude, "uw1 SPELLS.C:406-411, 371-372");
            k.Equal("UW1 Poison: damage source", player, _after[Poison].source, "uw1 SPELLS.C:359");
        }
        else
        {
            if (_after[Fear].goal != 6) return $"precondition: Cause Fear did not reach the critter (goal {_after[Fear].goal})";
            k.Equal("UW2 Cause Fear: attitude", 1, _after[Fear].attitude, "uw2 SPELLS.C:538");
            k.Equal("UW2 Poison: damage source", player, _after[Poison].source, "uw2 SPELLS.C:357, 767-772");
        }
        return k.Result;
    }
}
