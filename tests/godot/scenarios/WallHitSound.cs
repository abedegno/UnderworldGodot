using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace Underworld.Scenarios;

/// <summary>
/// The sounds of the player's swing that meets a wall, and of the swing at nothing after it.
///
/// A swing whose probe finds no object but meets terrain makes the spark (find_wall_coll) and then
/// plays the whiff of a swing at nothing (do_attack: resolve_attack returns 0, then do_miss(0);
/// uw1/src/combat/COMBAT.C:495-496, uw2/src/combat/COMBAT.C:530-531). For the player, find_wall_coll
/// plays the wall sound: UW1 always effect 7 (uw1 :213-214); UW2 effect 7 when player_weapon is 1
/// or 2 and 8 otherwise, and it sets hit_wall (uw2 :205-210). UW1's do_miss(0) always plays the
/// whiff, effect 10 (uw1 :438-439). UW2's plays it only when hit_wall is clear, and otherwise clears
/// hit_wall instead (uw2 :476-480), so a wall hit makes one sound in UW2 and two in UW1, and the next
/// swing at nothing whiffs again.
///
/// player_weapon is set at the start of every swing by GetPlayerWeapon (uw2 :573-606, called at
/// :845) from the object in the weapon hand: 3 for a launcher (a missile-class object whose
/// Missile ammo is 0..0x0F), 2 for an edged weapon and 1 for a blunt one (is_sharp, uw2 :275-289:
/// items 7..9 are blunt), and 0 for bare hands or anything else. UW1 has no player_weapon.
///
/// The scenario puts the player in an open square with a wall on one side and an open, empty
/// square on the other, faces him into the wall and swings with the bash key (P) held, as a player
/// does: bare-handed, with item 7 (blunt) and with item 4 (edged) in the weapon hand. It records the
/// effect numbers the game asks for through UWsoundeffects.PositionalRequestObserver (7, 8 and 10
/// only) and, in UW2, combat.PlayerWeaponSound after the swing. Then he turns to the open square
/// and swings once more at nothing.
///
/// The port played the whiff after a wall hit in UW2 as well (src/interaction/combat/combat.cs:297,
/// 302-315), and combat_input.cs pinned PlayerWeaponSound to 1 for every swing.
/// </summary>
public sealed class WallHitSound : Scenario
{
    static readonly int[] Dx = { 0, 1, 0, -1 }, Dy = { 1, 0, -1, 0 };
    static readonly byte[] Watched = { 7, 8, 10 };

    public override string Name => "wall-hit-sound";
    public override string Cites => "underworld-exhumed uw1/src/combat/COMBAT.C:189-225 (find_wall_coll), 429-439 (do_miss), 495-496; uw2/src/combat/COMBAT.C:181-222 (find_wall_coll), 466-480, 530-531, 573-606 (GetPlayerWeapon), 275-289 (is_sharp)";

    string _problem;
    int _wall = -1;
    readonly List<(string what, int weapon, List<int> sounds, int playerWeapon)> _swings = new();
    List<int> _air;

    public override async Task Setup(ScenarioContext c)
    {
        var map = UWTileMap.current_tilemap;
        var p = playerdat.playerObject;
        bool Empty(int x, int y) => UWTileMap.ValidTile(x, y) && map.Tiles[x, y].tileType == UWTileMap.TILE_OPEN && map.Tiles[x, y].indexObjectList == 0;
        int best = int.MaxValue, sx = -1, sy = -1;
        for (int y = 1; y < 63; y++)
            for (int x = 1; x < 63; x++)
            {
                int dd = (x - p.tileX) * (x - p.tileX) + (y - p.tileY) * (y - p.tileY);
                if (dd >= best || map.Tiles[x, y].tileType != UWTileMap.TILE_OPEN) continue;
                if (map.Tiles[x, y].indexObjectList != 0 && !(x == p.tileX && y == p.tileY)) continue;
                for (int d = 0; d < 4; d++)
                {
                    int wx = x + Dx[d], wy = y + Dy[d], ox = x - Dx[d], oy = y - Dy[d];
                    if (UWTileMap.ValidTile(wx, wy) && map.Tiles[wx, wy].tileType == UWTileMap.TILE_SOLID
                        && Empty(ox, oy) && map.Tiles[ox, oy].floorHeight == map.Tiles[x, y].floorHeight)
                    { best = dd; sx = x; sy = y; _wall = d; break; }
                }
            }
        if (_wall < 0) { _problem = "no open square with a wall on one side and an open, empty square on the other"; return; }
        if ((sx != p.tileX || sy != p.tileY) && !await Guards.TeleportHere(c, sx, sy)) { _problem = $"the move to ({sx}, {sy}) did not happen"; return; }
        playerdat.play_hp = 200; p.npc_hp = 200;
        c.Note($"player at ({sx}, {sy}), wall to the {"NESW"[_wall]}");
    }

    void Face(int dir)
    {
        var p = playerdat.playerObject;
        p.heading = (short)(dir * 2);
        p.npc_heading = 0;
        playerdat.PlayerCameraYaw_dseg_8294 = (short)(dir * 16384);
    }

    /// <summary>Puts a new object of item id in the weapon hand (8 - lefty in the original), or empties it for -1.</summary>
    static string Wield(int item)
    {
        foreach (int slot in new[] { 7, 8 }) BatchB.Unequip(slot);
        combat.currentweapon = null;
        if (item < 0) return playerdat.PrimaryHandObject == null ? null : "the weapon hand is not empty";
        foreach (int slot in new[] { 7, 8 })
        {
            var o = BatchB.Equip(item, slot, 40);
            if (o == null) return $"item {item} could not be made";
            if (playerdat.PrimaryHandObject == o) { combat.currentweapon = o; return null; }
            BatchB.Unequip(slot);
        }
        return $"item {item} did not reach the weapon hand";
    }

    /// <summary>One swing with the bash key held for a second; returns the watched effects asked for.</summary>
    static async Task<List<int>> Swing(ScenarioContext c, int dir, System.Action<int> face)
    {
        var heard = new List<int>();
        System.Action<byte> listen = e => { if (Watched.Contains(e)) heard.Add(e); };
        playerdat.play_drawn = 1;
        for (int f = 0; f < 120 && combat.stage != combat.CombatStages.Ready; f++) { face(dir); await c.Frames(1); }
        if (combat.stage != combat.CombatStages.Ready) return null;
        UWsoundeffects.PositionalRequestObserver += listen;
        try
        {
            Guards.Key(Key.P, true);
            for (int f = 0; f < 60; f++) { face(dir); await c.Frames(1); }
            Guards.Key(Key.P, false);
            for (int f = 0; f < 120; f++)
            {
                face(dir);
                await c.Frames(1);
                if (combat.stage == combat.CombatStages.Ready && f > 10) break;
            }
        }
        finally { UWsoundeffects.PositionalRequestObserver -= listen; }
        return heard;
    }

    public override async Task Act(ScenarioContext c)
    {
        if (_problem != null) return;
        foreach (var (what, item) in new[] { ("bare hands", -1), ("item 7 (blunt)", 7), ("item 4 (edged)", 4) })
        {
            string w = Wield(item);
            if (w != null) { _problem = w; return; }
            var heard = await Swing(c, _wall, Face);
            if (heard == null) { _problem = $"not ready to swing with {what} ({combat.stage})"; return; }
            _swings.Add((what, item, heard, combat.PlayerWeaponSound));
            c.Note($"{what} at the wall: effects [{string.Join(", ", heard)}], PlayerWeaponSound {combat.PlayerWeaponSound}");
        }
        _air = await Swing(c, (_wall + 2) & 3, Face);
        if (_air == null) { _problem = $"not ready to swing at nothing ({combat.stage})"; return; }
        c.Note($"then at nothing: effects [{string.Join(", ", _air)}]");
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        bool uw2 = c.Game == "uw2";
        var k = new Checks();
        foreach (var s in _swings)
        {
            if (!s.sounds.Contains(7) && !s.sounds.Contains(8)) return $"precondition: the swing with {s.what} never met the wall (no effect 7 or 8)";
            // player_weapon from GetPlayerWeapon: 0 bare-handed, 1 blunt (items 7..9), 2 edged.
            int playerWeapon = s.weapon < 0 ? 0 : (s.weapon >= 7 && s.weapon < 10) ? 1 : 2;
            string expected;
            if (uw2)
                expected = (playerWeapon == 1 || playerWeapon == 2) ? "7" : "8";
            else
                expected = "7, 10";
            string got = string.Join(", ", s.sounds);
            k.True(got == expected, $"effects for a swing at the wall with {s.what} expected [{expected}], got [{got}] ({(uw2 ? "uw2 COMBAT.C:205-210, 476-480" : "uw1 COMBAT.C:213-214, 438-439")})");
            if (uw2)
                k.Equal($"player_weapon after a swing with {s.what}", playerWeapon, s.playerWeapon, "uw2 COMBAT.C:590-604");
        }
        string air = string.Join(", ", _air);
        k.True(air == "10", $"effects for the next swing at nothing expected [10], got [{air}] ({(uw2 ? "uw2 COMBAT.C:476-478" : "uw1 COMBAT.C:438-439")})");
        return k.Result;
    }
}
