using System.Collections.Generic;
using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// The first-person weapon sways sideways with the player's speed. Each time the original draws the
/// weapon (do_fbuf_bms) it calls jiggle_weapon with 0 when the player is still and otherwise
/// speed * 2 / 0x31F + 1 (uw1/src/ui/PANELS.C:1270-1271, uw2/src/ui/PANELS.C:891-892), and draws the
/// picture wxo pixels to the side (uw1 PANELS.C:1283, uw2 PANELS.C:898). jiggle_weapon sets wxo to 0
/// for 0, to rand() % 5 for 1 and to rand() % 10 for 2, negated unless wxo is already above 0
/// (uw1 PANELS.C:776-794, uw2 PANELS.C:587-602); wxo starts at 0, so the sway is 0 to 4 pixels left
/// at a walk and 0 to 9 at a faster pace. A swing's frames are drawn with wxo 0 (uw1 PANELS.C:1278,
/// uw2 PANELS.C:871).
///
/// The scenario shows the weapon ready (play_drawn 1 and combat.EndCombatLoop), then
/// holds the player's speed (motion.playerMotionParams.momentum_14, the original's PN.speed) at 0x100,
/// 0x2F0 and 0 for a second each, then 0x2F0 again with a swing frame shown, and reads the weapon
/// picture's x in each frame. Screen units are 4 to one of the original's 320 pixels.
///
/// Both games. The port drew the weapon at a fixed x (src/ui/uimanager_weapons.cs DrawWeaponAnimation).
/// </summary>
public sealed class WeaponSway : Scenario
{
    public override string Name => "weapon-sway";
    public override string Cites => "underworld-exhumed uw1/src/ui/PANELS.C:776-794, 1268-1283 (jiggle_weapon, do_fbuf_bms); uw2/src/ui/PANELS.C:587-602, 862-900";

    float _baseX;
    int _dir, _run;
    short _x0, _y0;
    bool _drawn;
    readonly Dictionary<string, (int min, int max, int nonzero, int late)> _seen = new();

    Godot.TextureRect Weapon => _game == "uw2" ? uimanager.instance.weaponanimuw2 : uimanager.instance.weaponanimuw1;
    string _game;

    public override async Task Setup(ScenarioContext c)
    {
        _game = c.Game;
        _baseX = Weapon.Position.X;
        // Face the longest stretch of open floor, so a wall does not stop the held speed at once.
        var p = playerdat.playerObject;
        var map = UWTileMap.current_tilemap;
        int[] dx = { 0, 1, 0, -1 }, dy = { 1, 0, -1, 0 };
        for (int d = 0; d < 4; d++)
        {
            int k = 1;
            while (k < 12 && UWTileMap.ValidTile(p.tileX + k * dx[d], p.tileY + k * dy[d])
                && map.Tiles[p.tileX + k * dx[d], p.tileY + k * dy[d]].tileType == UWTileMap.TILE_OPEN)
                k++;
            if (k - 1 > _run) { _run = k - 1; _dir = d; }
        }
        playerdat.PlayerCameraYaw_dseg_8294 = motion.PlayerCardinalHeadingLookupTable[_dir];
        playerdat.play_drawn = 1;
        combat.EndCombatLoop();   // with play_drawn 1 this shows the ready picture, whatever an earlier scenario left
        await c.Frames(10);
        _drawn = Weapon.Texture != null && uimanager.CurrentWeaponAnim % 7 == 6;
        _x0 = motion.playerMotionParams.x_0;
        _y0 = motion.playerMotionParams.y_2;
        c.Note($"weapon texture {(Weapon.Texture != null ? "set" : "null")}, animation {uimanager.CurrentWeaponAnim}");
        c.Note($"facing {"NESW"[_dir]} from ({p.tileX}, {p.tileY}), {_run} open squares ahead");
    }

    /// <summary>Holds the speed for a second and records the sway (in the original's pixels) seen.</summary>
    async Task Hold(ScenarioContext c, string what, short speed)
    {
        int min = int.MaxValue, max = int.MinValue, nonzero = 0, late = 0;
        for (int i = 0; i < 60; i++)
        {
            motion.playerMotionParams.momentum_14 = speed;
            motion.playerMotionParams.x_0 = _x0;   // back to the start each frame, so nothing in the way stops the player
            motion.playerMotionParams.y_2 = _y0;
            await c.Frames(1);
            int x = (int)System.Math.Round((Weapon.Position.X - _baseX) / 4);
            min = System.Math.Min(min, x);
            max = System.Math.Max(max, x);
            if (x != 0) nonzero++;
            if (x != 0 && i >= 12) late++;   // the sway changes once a game loop (about 6 frames), so the first frames keep the last phase's
        }
        _seen[what] = (min, max, nonzero, late);
        c.Note($"{what}: sway {min}..{max}, {nonzero} of 60 frames off centre");
    }

    public override async Task Act(ScenarioContext c)
    {
        if (!_drawn) return;
        await Hold(c, "walk (speed 0x100, jiggle 1)", 0x100);
        await Hold(c, "run (speed 0x2F0, jiggle 2)", 0x2F0);
        await Hold(c, "still (speed 0)", 0);
        uimanager.DrawWeaponAnimation(uimanager.CurrentWeaponAnim - 6, 0);   // the group's first swing frame
        await Hold(c, "swing (speed 0x2F0)", 0x2F0);
        motion.playerMotionParams.momentum_14 = 0;
        playerdat.play_drawn = 0;
        combat.EndCombatLoop();   // puts the weapon away again
        await c.Frames(2);
    }

    public override string Assert(ScenarioContext c)
    {
        if (!_drawn) return "precondition: the weapon was not drawn out to its ready picture";
        if (_seen.Count != 4) return "precondition: not every phase ran";
        var walk = _seen["walk (speed 0x100, jiggle 1)"];
        var run = _seen["run (speed 0x2F0, jiggle 2)"];
        var still = _seen["still (speed 0)"];
        var swing = _seen["swing (speed 0x2F0)"];
        string j = c.Game == "uw1" ? "uw1 PANELS.C:776-794" : "uw2 PANELS.C:587-602";
        var k = new Checks();
        k.True(walk.nonzero > 0, $"weapon never swayed at a walk ({j})");
        k.Within("sway at a walk, lowest", -4, 0, walk.min, j);
        k.Within("sway at a walk, highest", -4, 0, walk.max, j);
        k.True(run.min < -4, $"sway at a run never passed 4 pixels, lowest {run.min} ({j})");
        k.Within("sway at a run, lowest", -9, 0, run.min, j);
        k.Within("sway at a run, highest", -9, 0, run.max, j);
        k.Equal("sway frames when still, after the first two game loops", 0, still.late, j);
        k.Equal("sway frames during a swing, after the first two game loops", 0, swing.late, c.Game == "uw1" ? "uw1 PANELS.C:1278" : "uw2 PANELS.C:871");
        return k.Result;
    }
}
