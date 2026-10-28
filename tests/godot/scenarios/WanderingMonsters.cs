using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace Underworld.Scenarios;

/// <summary>
/// Wandering monsters appear out of the player's sight. DoWanderingMonsters
/// (uw1/src/event/TRIGGER.C:656-673, uw2/src/event/TRIGGER.C:1033-1050) runs every create-object
/// trap on the level (trap class 7, flags 0) whose template is a mobile object, but, while the
/// player is awake (is_player set, PLAYTIME.C's slow tick), only where check_alert
/// (uw1 TRIGGER.C:643-652, uw2 TRIGGER.C:1020-1029) says the player is not within 8 squares:
/// abs(px - x) &lt; 8 &amp;&amp; abs(py - y) &lt; 8 is "would be seen" and the trap is skipped.
///
/// The scenario finds the level's create-object traps with mobile templates (as the port's
/// FindAndRunCreateObjectTraps finds them), sets each trap's quality to 0 so that the trap's own
/// chance (rand() * 0x3F / 0x8000 &lt; quality, uw1 TRIGGER.C:299, uw2 TRIGGER.C:526) never
/// cancels it, takes the player to one trap's square through Teleportation.Teleport, and calls
/// a_create_object_trap.FindAndRunCreateObjectTraps, the port's DoWanderingMonsters for the awake
/// player (src/player/playerdatloop.cs:230), once per trap with the others' flags set so that only
/// that one is looked at. A trap that ran draws from the generator (its chance roll, TRIGGER.C:299 /
/// :526), while one skipped by the range test draws nothing; a copy may be moved up to 4 eighths or
/// culled by put_at (uw1 COLLIDE.C:645-706), so the draw, not the copy, is the test.
/// No trap within 8 squares may run; at least one further away must.
///
/// Both games. The port runs the trap only when the player is within 8 squares, the opposite
/// (src/traps/a_create_object_trap.cs:241). The asleep case (DoWanderingMonsters(0) from sleep,
/// which runs every trap) is not tested: the port has one function for both.
/// </summary>
public sealed class WanderingMonsters : Scenario
{
    public override string Name => "wandering-monsters-out-of-sight";
    public override string Cites => "underworld-exhumed uw1/src/event/TRIGGER.C:643-673, 297-310; uw2/src/event/TRIGGER.C:1020-1050, 524-537";

    const ulong TeleportMs = 20000;
    string _problem;
    readonly List<(int x, int y, uwObject trap)> _traps = new();
    readonly HashSet<int> _before = new();
    int _px, _py, _nearFired, _nearCount, _farFired, _farCount, _level;

    /// <summary>Every object in a square's list on the level.</summary>
    static HashSet<int> OnMap()
    {
        var set = new HashSet<int>();
        var objs = UWTileMap.current_tilemap.LevelObjects;
        for (int x = 0; x < 64; x++)
        for (int y = 0; y < 64; y++)
        {
            int n = 0;
            for (int i = UWTileMap.current_tilemap.Tiles[x, y].indexObjectList; i != 0 && n < 1024; i = objs[i].next, n++) set.Add(i);
        }
        return set;
    }

    void FindTraps()
    {
        _traps.Clear();
        var objs = UWTileMap.current_tilemap.LevelObjects;
        for (int x = 0; x < 64; x++)
        for (int y = 0; y < 64; y++)
        {
            var t = objectsearch.FindMatchInTile(tileX: x, tileY: y, majorclass: 6, minorclass: 0, classindex: 7);
            if (t == null || t.flags_full != 0 || t.link <= 0 || t.is_quant != 0) continue;
            var template = objs[t.link];
            if (template == null || template.IsStatic) continue;
            _traps.Add((x, y, t));
        }
    }

    static bool Near(int x, int y, int px, int py) => System.Math.Abs(px - x) < 8 && System.Math.Abs(py - y) < 8;

    /// <summary>Teleports to (x, y) of level, or with x 0x3F to anywhere on it. new_player_pos lands the player on the
    /// nearest square with room, within the search box round (x, y), and kills him if there is none
    /// (uw1/src/game/UWEDIT.C:466-471, uw2/src/game/UWEDIT.C:480-486). A stand-in square (0x3F) with a callback
    /// queued parks him in the middle of the map with no search, so a level can be visited without knowing its map.</summary>
    async Task<bool> TeleportTo(ScenarioContext c, int x, int y, int level)
    {
        if (x == 0x3F) Teleportation.CodeToRunOnTeleport = () => { };
        Teleportation.Teleport(character: 0, tileX: x, tileY: y, newLevel: level == playerdat.dungeon_level ? 0 : level, HeadingHeightFlag: 0);
        ulong deadline = Time.GetTicksMsec() + TeleportMs;
        int stable = 0;
        while (Time.GetTicksMsec() < deadline && stable < 10)
        {
            await c.Frames(1);
            bool there = playerdat.dungeon_level == level
                && (x == 0x3F || (System.Math.Abs(playerdat.playerObject.tileX - x) <= 4 && System.Math.Abs(playerdat.playerObject.tileY - y) <= 4));
            stable = (there && !uimanager.WaitingForFade && uimanager.InGame) ? stable + 1 : 0;
        }
        return stable >= 10;
    }

    public override async Task Setup(ScenarioContext c)
    {
        // The first level (from the start one on) with traps both near one of them and far from it.
        int start = playerdat.dungeon_level;
        for (int level = start; level < start + 5; level++)
        {
            if (level != playerdat.dungeon_level && !await TeleportTo(c, 0x3F, 0x3F, level))
                continue;
            FindTraps();
            foreach (var t in _traps)
            {
                if (_traps.Any(o => !Near(o.x, o.y, t.x, t.y)))
                {
                    _level = level; _px = t.x; _py = t.y;
                    break;
                }
            }
            if (_level != 0) break;
        }
        if (_level == 0) { _problem = $"no level from {start} to {start + 4} has a create-object trap with others both near it and far from it"; return; }
        if (!await TeleportTo(c, _px, _py, _level)) { _problem = $"the teleport to ({_px}, {_py}) on level {_level} did not happen"; return; }
        FindTraps();   // the same level; re-read after the move
        foreach (var t in _traps) t.trap.quality = 0;
        _before.UnionWith(OnMap());
    }

    public override Task Act(ScenarioContext c)
    {
        if (_problem != null) return Task.CompletedTask;
        int px = playerdat.playerObject.tileX, py = playerdat.playerObject.tileY;
        if (Rng.Source is not BorlandRandSource) { _problem = "Rng.Source is not a BorlandRandSource"; return Task.CompletedTask; }
        var flags = _traps.Select(t => t.trap.flags_full).ToList();
        foreach (var t in _traps)
        {
            foreach (var o in _traps) o.trap.flags_full = (short)(o.trap == t.trap ? 0 : 1);
            uint seed = ((BorlandRandSource)Rng.Source).Seed;
            a_create_object_trap.FindAndRunCreateObjectTraps();
            bool fired = ((BorlandRandSource)Rng.Source).Seed != seed;
            if (Near(t.x, t.y, px, py)) { _nearCount++; if (fired) _nearFired++; }
            else { _farCount++; if (fired) _farFired++; }
        }
        for (int i = 0; i < _traps.Count; i++) _traps[i].trap.flags_full = flags[i];
        int made = OnMap().Count(i => !_before.Contains(i));
        c.Note($"level {_level}, player at ({px}, {py}): {_nearCount} traps within 8 squares ran {_nearFired}; {_farCount} further away ran {_farFired}; {made} new objects on the map");
        return Task.CompletedTask;
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        if (_nearCount == 0 || _farCount == 0) return $"precondition: {_nearCount} traps near the player and {_farCount} far";
        var k = new Checks();
        string cite = c.Game == "uw1" ? "uw1 TRIGGER.C:643-652, 667-668" : "uw2 TRIGGER.C:1020-1029, 1044-1045";
        k.Equal("create-object traps within 8 squares of the awake player that ran", 0, _nearFired, cite);
        k.True(_farFired > 0, $"create-object traps 8 or more squares away expected to run, none of {_farCount} did ({cite})");
        return k.Result;
    }
}
