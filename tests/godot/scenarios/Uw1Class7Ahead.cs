using System.Collections.Generic;
using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// UW1's class 7 spells pick their own target. The original's do_spell sends class 7 to
/// nail_1area (uw1/src/combat/SPELLS.C:131-133), which for the player calls gronk_area with count 1,
/// distance 4 and radius 2 (SPELLS.C:553-559): the handler from area1_spells (Cause Fear, Smite
/// Undead, Ally, Poison, Paralyze, SPELLS.C:539-541) runs on the critters other than the caster
/// within 2 squares of the square 4 ahead, in process_area's order (x, then y, then the square's
/// chain, SPELLS.C:416-483), until one call returns true. sp_fear and sp_hold return
/// hit_critter_goal's 1 whether or not the critter resists (SPELLS.C:366-375, 399-411), so they
/// act on the first critter only. Nothing waits for a click; UW1 has no target cursor for them
/// (header, SPELLS.C:21-26). player_cast has already taken the mana for every class but missiles
/// (uw1/src/combat/RUNES.C:259-263), and do_spell returns 1 for class 7 whether a critter was
/// found or not (SPELLS.C:131-133, 163), so a cast with no critter ahead still costs its mana and
/// prints nothing.
///
/// The scenario turns the player to a heading whose square 4 ahead is open with no critter in the
/// area, casts Cause Fear the way the cast button does once its checks have passed (cost in
/// RunicMagic.PendingSpellCost, then SpellCasting.CastSpell), then puts two critters that cannot
/// resist magic in the area and casts Cause Fear and then Paralyze. Each cast must leave nothing
/// waiting for a target and cost its mana, and each spell must land on the first critter in
/// process_area's order and only on it.
///
/// The port once gave UW1 the UW2 targeting cursor for these spells (src/magic/spellcasting.cs).
/// </summary>
public sealed class Uw1Class7Ahead : Scenario
{
    const int Fear = 1, Paralyze = 5;   // class 7 minors, area1_spells[minor - 1] (SPELLS.C:539-541)
    const int FearGoal = 6, ParalyzeGoal = 7;   // GOAL_FLEE, GOAL_STAND_7 (uw1/src/include/critter.h:103-104)

    public override string Name => "uw1-class7-ahead";
    public override string[] Games => new[] { "uw1" };
    public override string Cites => "underworld-exhumed uw1/src/combat/SPELLS.C:553-559 (nail_1area), 487-512 (gronk_area), 416-483 (process_area), 366-375, 399-411; RUNES.C:259-263";

    string _problem;
    int _critterId, _heading, _cx, _cy;
    readonly Dictionary<int, int> _cost = new();
    uwObject _first, _second;
    readonly List<(string what, bool waiting, int spent, int firstGoal, int secondGoal)> _casts = new();

    public override Task Setup(ScenarioContext c)
    {
        foreach (var s in RunicMagic.SpellList)
            if (s != null && s.SpellMajorClass == 7) _cost[s.SpellMinorClass] = s.ManaCost;
        if (!_cost.ContainsKey(Fear) || !_cost.ContainsKey(Paralyze)) { _problem = "no Cause Fear or Paralyze in the spell list"; return Task.CompletedTask; }
        _critterId = BatchB.FindCritter(id => (commonObjDat.scaleresistances(id) & 3) == 0);
        if (_critterId < 0) { _problem = "no critter type without magic resistance"; return Task.CompletedTask; }
        var p = playerdat.playerObject;
        var tiles = UWTileMap.current_tilemap.Tiles;
        for (int h = 0; h < 8; h++)
        {
            int cx = p.npc_xhome, cy = p.npc_yhome;   // OBJ_HOMEX, OBJ_HOMEY, as gronk_area starts (SPELLS.C:498-500)
            motion.GetCoordinateInDirection(h << 5, 4, ref cx, ref cy);
            if (!UWTileMap.ValidTile(cx, cy) || tiles[cx, cy].tileType != UWTileMap.TILE_OPEN || (cx == p.tileX && cy == p.tileY)) continue;
            // a second open square in the area, before the centre in process_area's order, and no critter there yet
            (int x, int y)? second = null;
            bool occupied = false;
            for (int x = cx - 2; x <= cx + 4; x++)
                for (int y = cy - 2; y <= cy + 4; y++)
                {
                    if (!UWTileMap.ValidTile(x, y)) continue;
                    for (int i = tiles[x, y].indexObjectList; i != 0; i = UWTileMap.current_tilemap.LevelObjects[i].next)
                    {
                        var o = UWTileMap.current_tilemap.LevelObjects[i];
                        if (o.majorclass == 1 && o.index != 1) occupied = true;
                    }
                    if (second == null && x < cx && x >= cx - 2 && y >= cy - 2 && y <= cy + 2
                        && tiles[x, y].tileType == UWTileMap.TILE_OPEN && !(x == p.tileX && y == p.tileY))
                        second = (x, y);
                }
            if (occupied || second == null) continue;
            _heading = h; _cx = cx; _cy = cy;
            // _first is the one process_area reaches first: the lower x
            _first = BatchB.SpawnCritter(_critterId, second.Value.x, second.Value.y);
            _second = BatchB.SpawnCritter(_critterId, cx, cy);
            if (_first == null || _second == null) { _problem = "a critter could not be made"; return Task.CompletedTask; }
            return Task.CompletedTask;
        }
        _problem = "no heading has an open square 4 ahead with no critter about it and a second open square";
        return Task.CompletedTask;
    }

    void Cast(ScenarioContext c, string what, int minor)
    {
        BatchB.ReadyCaster();
        foreach (var cr in new[] { _first, _second }) { cr.npc_goal = 0; cr.npc_attitude = 3; cr.IsAlly = 0; }
        var p = playerdat.playerObject;
        p.heading = (short)_heading;
        p.npc_heading = 0;
        int before = playerdat.play_mana;
        RunicMagic.PendingSpellCost = _cost[minor];
        SpellCasting.CastSpell(7, minor, p, null, p.tileX, p.tileY, false);
        bool waiting = SpellCasting.currentSpell != null;
        int spent = before - playerdat.play_mana;
        _casts.Add((what, waiting, spent, _first.npc_goal, _second.npc_goal));
        c.Note($"{what}: waiting for a target {waiting}, mana spent {spent} of {_cost[minor]}, goals first ({_first.tileX},{_first.tileY}) {_first.npc_goal}, second ({_second.tileX},{_second.tileY}) {_second.npc_goal}");
        SpellCasting.currentSpell = null;
        RunicMagic.PendingSpellCost = 0;
        uimanager.instance.mousecursor.SetCursorToCursor();
    }

    public override Task Act(ScenarioContext c)
    {
        if (_problem != null) return Task.CompletedTask;
        c.Note($"critter 0x{_critterId:X}, heading {_heading}, centre ({_cx},{_cy})");
        UnlinkCritters();
        Cast(c, "Cause Fear with no critter ahead", Fear);
        RelinkCritters();
        Cast(c, "Cause Fear on two critters", Fear);
        Cast(c, "Paralyze on two critters", Paralyze);
        return Task.CompletedTask;
    }

    // The empty case needs the area clear, so the two critters leave their squares' chains for it.
    void UnlinkCritters()
    {
        foreach (var cr in new[] { _first, _second })
        {
            var tile = UWTileMap.current_tilemap.Tiles[cr.tileX, cr.tileY];
            if (tile.indexObjectList == cr.index) tile.indexObjectList = cr.next;
            else
                for (int i = tile.indexObjectList; i != 0; i = UWTileMap.current_tilemap.LevelObjects[i].next)
                    if (UWTileMap.current_tilemap.LevelObjects[i].next == cr.index) { UWTileMap.current_tilemap.LevelObjects[i].next = cr.next; break; }
            cr.next = 0;
        }
    }

    void RelinkCritters()
    {
        foreach (var cr in new[] { _first, _second })
        {
            var tile = UWTileMap.current_tilemap.Tiles[cr.tileX, cr.tileY];
            cr.next = tile.indexObjectList;
            tile.indexObjectList = cr.index;
        }
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        if (_casts.Count != 3) return "precondition: the three casts did not all happen";
        var k = new Checks();
        var cite = "uw1 SPELLS.C:131-133, 553-559; header 21-26";
        foreach (var t in _casts)
        {
            k.True(!t.waiting, $"{t.what}: the spell is waiting for a target after the cast; UW1 class 7 acts at once ({cite})");
            k.Equal($"{t.what}: mana spent", _cost[t.what.StartsWith("Paralyze") ? Paralyze : Fear], t.spent, "uw1 RUNES.C:259-263, SPELLS.C:163");
        }
        k.Equal("Cause Fear with no critter ahead: goal of the first critter (not in the area then)", 0, _casts[0].firstGoal, cite);
        k.Equal("Cause Fear with no critter ahead: goal of the second critter (not in the area then)", 0, _casts[0].secondGoal, cite);
        k.Equal("Cause Fear on two critters: goal of the first in process_area's order", FearGoal, _casts[1].firstGoal, "uw1 SPELLS.C:399-403, 366-375, 476");
        k.Equal("Cause Fear on two critters: goal of the second (count 1 is used up)", 0, _casts[1].secondGoal, "uw1 SPELLS.C:558, 476");
        k.Equal("Paralyze on two critters: goal of the first in process_area's order", ParalyzeGoal, _casts[2].firstGoal, "uw1 SPELLS.C:406-411, 366-375, 476");
        k.Equal("Paralyze on two critters: goal of the second (count 1 is used up)", 0, _casts[2].secondGoal, "uw1 SPELLS.C:558, 476");
        return k.Result;
    }
}
