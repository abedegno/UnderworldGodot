using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace Underworld.Scenarios;

/// <summary>
/// Guard: bare-handed swings at a critter in front of the player land, and each blow takes off
/// no more hit points than the original's damage roll allows.
///
/// The original's blow (player_attack, uw1/src/combat/COMBAT.C:650-758): while the swing key is
/// held the charge play_pow rises to at most 100; on the strike the power is the weapon's
/// min_charge plus (max_charge - min_charge) * play_pow / 100 (COMBAT.C:728-732). Bare-handed,
/// the damage is barehand skill * 2 / 5 + strength / 6 + 4 (DoPlayerWeapon, COMBAT.C:572-602, 586; the
/// fist is Weapons[0x0F]). A critical multiplies it by at most 2 (frp_check,
/// COMBAT.C:303-305); do_damage rolls it as (damage / 6)d6 + 1d(damage % 6), at least 2, scales
/// the roll by power / 128, adds hitangle (0 to 4, compute_hitangle, COMBAT.C:481-486) and takes
/// off the critter's armour at the hit location (COMBAT.C:333-375), and damage_critter takes the
/// rest off its hit points (uw1/src/critter/AI.C:1458-1500). So one blow takes at most
/// (2 * damage) * max_charge >> 7, plus 4, less the critter's smallest armour value. A miss
/// takes nothing. UW2 is the same here (uw2/src/combat/COMBAT.C).
///
/// The weapon and armour figures are read from the game's own OBJECTS.DAT (struct Weapon,
/// uw1/src/include/object.h:412-419; struct Creature, uw1/src/include/critter.h:21-24). The
/// critter is the first plain walking creature, made in the square in front of the player at
/// the edge nearest him, standing and mellow with 200 hit points; the player, with attack and
/// barehand skills of 30, draws his fists and swings with the bash key (P) held for a second, as a
/// player does, up to twelve times, and at least one blow must land.
/// </summary>
public sealed class GuardMelee : Scenario
{
    const int Fist = 0x0F;
    const int Swings = 12;
    static readonly int[] Dx = { 0, 1, 0, -1 }, Dy = { 1, 0, -1, 0 };
    static readonly short[] FineX = { 4, 1, 4, 6 }, FineY = { 1, 4, 6, 4 };

    public override string Name => "guard-melee";
    public override string Cites => "underworld-exhumed uw1/src/combat/COMBAT.C:282-380 (frp_check, do_damage), 481-486, 572-602 (DoPlayerWeapon), 650-758 (player_attack); uw1/src/critter/AI.C:1458-1500 (damage_critter); uw2/src/combat/COMBAT.C";

    string _problem;
    uwObject _critter;
    int _maxBlow, _damage, _maxCharge, _minArmour, _swings, _dir = -1;
    readonly List<int> _blows = new();
    readonly List<string> _diag = new();

    public override async Task Setup(ScenarioContext c)
    {
        var map = UWTileMap.current_tilemap;
        var p = playerdat.playerObject;
        // The player's square, or the nearest one, with an open, empty square of its floor
        // height beside it to put the critter in.
        int best = int.MaxValue, sx = -1, sy = -1;
        for (int y = 1; y < 63; y++)
            for (int x = 1; x < 63; x++)
            {
                int dd = (x - p.tileX) * (x - p.tileX) + (y - p.tileY) * (y - p.tileY);
                if (dd >= best || map.Tiles[x, y].tileType != UWTileMap.TILE_OPEN) continue;
                if (map.Tiles[x, y].indexObjectList != 0 && !(x == p.tileX && y == p.tileY)) continue;
                for (int d = 0; d < 4; d++)
                {
                    int nx = x + Dx[d], ny = y + Dy[d];
                    if (UWTileMap.ValidTile(nx, ny) && map.Tiles[nx, ny].tileType == UWTileMap.TILE_OPEN
                        && map.Tiles[nx, ny].floorHeight == map.Tiles[x, y].floorHeight && map.Tiles[nx, ny].indexObjectList == 0)
                    { best = dd; sx = x; sy = y; _dir = d; break; }
                }
            }
        if (_dir < 0) { _problem = "no open square with an open, empty square of the same floor height beside it"; return; }
        if ((sx != p.tileX || sy != p.tileY) && !await Guards.TeleportHere(c, sx, sy)) { _problem = $"the move to ({sx}, {sy}) did not happen"; return; }

        int id = -1;
        for (int i = 0x40; i < 0x7F && id < 0; i++)
            if (critterObjectDat.avghit(i) > 0 && !critterObjectDat.isFlier(i) && !critterObjectDat.isSwimmer(i) && !critterObjectDat.unkPassivenessProperty(i)) id = i;
        if (id < 0) { _problem = "no plain walking creature"; return; }

        int tx = p.tileX + Dx[_dir], ty = p.tileY + Dy[_dir];
        _critter = ObjectCreator.spawnObjectInTile(itemid: id, tileX: tx, tileY: ty, xpos: FineX[_dir], ypos: FineY[_dir], zpos: p.zpos,
            WhichList: ObjectFreeLists.ObjectListType.MobileList, RenderImmediately: false);
        if (_critter == null) { _problem = "no critter could be made"; return; }
        ObjectCreator.InitialiseCritter(_critter);
        _critter.npc_xhome = (short)tx; _critter.npc_yhome = (short)ty;
        _critter.npc_goal = 0; _critter.npc_gtarg = 0; _critter.npc_attitude = 2; _critter.IsAlly = 0;
        _critter.npc_whoami = 0;
        _critter.npc_hp = 200;
        _critter.heading = (short)((_dir * 2 + 4) & 7);   // facing the player
        _critter.npc_heading = 0;

        p.heading = (short)(_dir * 2);
        p.npc_heading = 0;
        playerdat.PlayerCameraYaw_dseg_8294 = (short)(_dir * 16384);
        playerdat.play_hp = 200; p.npc_hp = 200;
        // Skilled fists, so blows land often enough to see some in a few swings (the to-hit roll
        // is skill_check(askill + hitangle, defence), COMBAT.C:301; askill is half the attack
        // skill plus the barehand skill plus dexterity / 7, COMBAT.C:581-582).
        playerdat.Attack = 30;
        playerdat.Unarmed = 30;

        // The original's figures, from OBJECTS.DAT.
        byte[] od = File.ReadAllBytes(Path.Combine(UWClass.BasePath, "DATA", "OBJECTS.DAT"));
        _maxCharge = od[2 + Fist * 8 + 5];                         // Weapons[0x0F].max_charge
        int cr = 2 + 16 * 8 + 16 * 3 + 32 * 4 + (id - 0x40) * 0x30;  // Creature[id & 0x3F]
        _minArmour = Enumerable.Range(0, 4).Select(i => od[cr + i]).Where(a => a != 0xFF).DefaultIfEmpty(od[cr]).Min();
        _damage = playerdat.Unarmed * 2 / 5 + playerdat.STR / 6 + 4;
        _maxBlow = System.Math.Max(0, ((2 * _damage) * _maxCharge >> 7) + 4 - _minArmour);
        c.Note($"critter 0x{id:X} at ({tx}, {ty}) fine ({FineX[_dir]}, {FineY[_dir]}) in front ({"NESW"[_dir]}); bare-hand damage {_damage} (Unarmed {playerdat.Unarmed}, STR {playerdat.STR}), fist max charge {_maxCharge}, least armour {_minArmour}: at most {_maxBlow} a blow");

        playerdat.play_drawn = 1;
        for (int f = 0; f < 120 && combat.stage != combat.CombatStages.Ready; f++) await c.Frames(1);
        if (combat.stage != combat.CombatStages.Ready) _problem = $"the fists were not ready to swing ({combat.stage})";
    }

    public override async Task Act(ScenarioContext c)
    {
        if (_problem != null) return;
        for (int s = 0; s < Swings && _blows.Count(b => b > 0) == 0; s++)
        {
            _swings++;
            int before = _critter.npc_hp;
            int lowest = before;
            Guards.Key(Key.P, true);
            for (int f = 0; f < 60; f++) { await c.Frames(1); lowest = System.Math.Min(lowest, _critter.npc_hp); }
            Guards.Key(Key.P, false);
            for (int f = 0; f < 120; f++)
            {
                await c.Frames(1);
                lowest = System.Math.Min(lowest, _critter.npc_hp);
                if (combat.stage == combat.CombatStages.Ready && f > 10) break;
            }
            _blows.Add(before - lowest);
            var pl = playerdat.playerObject;
            _diag.Add($"[{_critter.tileX},{_critter.tileY} f{_critter.xpos},{_critter.ypos} hp{_critter.npc_hp} goal{_critter.npc_goal}; player h{pl.heading}/{pl.npc_heading} at {pl.tileX},{pl.tileY} f{pl.xpos},{pl.ypos}; dmg {combat.AttackDamage} score {combat.AttackScore}]");
            _critter.npc_hp = 200;   // keep it alive and the next blow measurable
        }
        c.Note($"{_swings} swings, hit points taken: {string.Join(", ", _blows)}");
        if (!_blows.Any(b => b > 0)) c.Note(string.Join(" ", _diag));
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        var k = new Checks();
        k.True(_blows.Any(b => b > 0), $"no blow of {_swings} landed on the critter in front (do_attack: uw1 COMBAT.C:491-510)");
        for (int i = 0; i < _blows.Count; i++)
            k.Within($"hit points taken by blow {i + 1}", 0, _maxBlow, _blows[i],
                "at most (2 * damage) * max_charge >> 7, + 4 hitangle, - armour: uw1 COMBAT.C:303-305, 346-373, 728-732");
        return k.Result;
    }
}
