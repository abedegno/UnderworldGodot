using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace Underworld.Scenarios;

/// <summary>
/// The player's blow uses the weapon in his hand when the swing starts, whatever he held when he
/// drew it.
///
/// player_attack calls GetPlayerWeapon at the start of every swing (uw1/src/combat/COMBAT.C:745,
/// uw2/src/combat/COMBAT.C:845). It reads the weapon hand, AskInventory(8 - lefty) (uw1 :543,
/// uw2 :578), and takes the weapon's data record and reach (wsize, ComObjData radius) from what is
/// there, or the bare fist's, Weapons[15], when the hand is empty or holds no weapon (uw1 :538-563,
/// uw2 :573-607). DoPlayerWeapon then sets the blow's damage from that record when the blow is
/// struck: the damage byte for the swing's kind plus strength / 9, or bare-handed 2/5 of the
/// barehand skill plus strength / 6 plus 4 (uw1 :572-600, uw2 :616-650; called at uw1 :733, uw2 :789).
///
/// The scenario draws the weapon (uimanager.ToggleWeaponAnimationState, as entering attack mode
/// does) with one weapon in the hand, then empties the hand, as a weapon broken in the fight
/// leaves it, and swings at an empty square with the bash key held; then it draws with that weapon
/// again, puts a different weapon in the hand and swings again. After each swing it compares the
/// blow's reach (combat.CurrentWeaponRadius) and damage (combat.AttackDamage) with the hand's.
/// The swing kind is the port's choice for the key; only the weapon is under test.
///
/// The port took the weapon from combat.currentweapon, which only drawing or sheathing sets
/// (src/ui/uimanager_interaction.cs:380-388), so it kept swinging the weapon it drew.
/// </summary>
public sealed class WeaponEachSwing : Scenario
{
    static readonly int[] Dx = { 0, 1, 0, -1 }, Dy = { 1, 0, -1, 0 };

    public override string Name => "weapon-each-swing";
    public override string Cites => "underworld-exhumed uw1/src/combat/COMBAT.C:538-600 (GetPlayerWeapon, DoPlayerWeapon), 733, 745; uw2/src/combat/COMBAT.C:573-650, 789, 845";

    string _problem;
    int _dir = -1;
    int _first = -1, _second = -1;
    readonly List<(string what, int item, int swing, int radius, int damage)> _swings = new();

    static bool Melee(int id) { int s = weaponObjectDat.skill(id); return s >= 3 && s <= 5; }

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
                    int ox = x + Dx[d], oy = y + Dy[d], fx = x + 2 * Dx[d], fy = y + 2 * Dy[d];
                    if (Empty(ox, oy) && Empty(fx, fy) && map.Tiles[ox, oy].floorHeight == map.Tiles[x, y].floorHeight)
                    { best = dd; sx = x; sy = y; _dir = d; break; }
                }
            }
        if (_dir < 0) { _problem = "no open square with two open, empty squares ahead of it"; return; }
        if ((sx != p.tileX || sy != p.tileY) && !await Guards.TeleportHere(c, sx, sy)) { _problem = $"the move to ({sx}, {sy}) did not happen"; return; }

        // Two melee weapons whose blows differ in reach or in every damage byte.
        for (int a = 0; a < 0x10 && _second < 0; a++)
        {
            if (!Melee(a)) continue;
            for (int b = 0; b < 0x10; b++)
            {
                if (b == a || !Melee(b)) continue;
                if (commonObjDat.radius(a) != commonObjDat.radius(b)
                    || (weaponObjectDat.stab(a) != weaponObjectDat.stab(b) && weaponObjectDat.slash(a) != weaponObjectDat.slash(b) && weaponObjectDat.bash(a) != weaponObjectDat.bash(b)))
                { _first = a; _second = b; break; }
            }
        }
        if (_second < 0) { _problem = "no two melee weapons with different blows"; return; }
        playerdat.play_hp = 200; p.npc_hp = 200;
        c.Note($"player at ({sx}, {sy}) facing {"NESW"[_dir]}; weapons {_first} and {_second}");
    }

    void Face()
    {
        var p = playerdat.playerObject;
        p.heading = (short)(_dir * 2);
        p.npc_heading = 0;
        playerdat.PlayerCameraYaw_dseg_8294 = (short)(_dir * 16384);
    }

    static uwObject Hold(int item)
    {
        foreach (int slot in new[] { 7, 8 }) BatchB.Unequip(slot);
        if (item < 0) return null;
        foreach (int slot in new[] { 7, 8 })
        {
            var o = BatchB.Equip(item, slot, 40);
            if (o == null) return null;
            if (playerdat.PrimaryHandObject == o) return o;
            BatchB.Unequip(slot);
        }
        return null;
    }

    /// <summary>Sheathes, draws with item drawn in the hand, then puts item swung there (-1 empties it) and swings once.</summary>
    async Task<string> Swing(ScenarioContext c, string what, int drawn, int swung)
    {
        uimanager.ToggleWeaponAnimationState(false, false);
        if (Hold(drawn) == null) return $"item {drawn} did not reach the weapon hand";
        uimanager.ToggleWeaponAnimationState(true, false);
        for (int f = 0; f < 120 && combat.stage != combat.CombatStages.Ready; f++) { Face(); await c.Frames(1); }
        if (combat.stage != combat.CombatStages.Ready) return $"not ready to swing ({combat.stage})";
        if (swung >= 0 ? Hold(swung) == null : Hold(-1) != null || playerdat.PrimaryHandObject != null)
            return $"the weapon hand could not be set for {what}";
        combat.CurrentWeaponRadius = -1;
        combat.AttackDamage = -1000;
        Guards.Key(Key.P, true);
        for (int f = 0; f < 60; f++) { Face(); await c.Frames(1); }
        Guards.Key(Key.P, false);
        for (int f = 0; f < 120; f++)
        {
            Face();
            await c.Frames(1);
            if (combat.stage == combat.CombatStages.Ready && f > 10) break;
        }
        if (combat.CurrentWeaponRadius < 0) return $"the swing with {what} was never struck";
        _swings.Add((what, swung, combat.WeaponSwingTypePlayer, combat.CurrentWeaponRadius, combat.AttackDamage));
        c.Note($"{what}: reach {combat.CurrentWeaponRadius}, damage {combat.AttackDamage}, swing kind {combat.WeaponSwingTypePlayer}");
        return null;
    }

    public override async Task Act(ScenarioContext c)
    {
        if (_problem != null) return;
        _problem = await Swing(c, $"drawn with item {_first}, then the hand emptied", _first, -1)
            ?? await Swing(c, $"drawn with item {_first}, then item {_second} in the hand", _first, _second);
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        var k = new Checks();
        foreach (var s in _swings)
        {
            int id = s.item < 0 ? 15 : s.item;   // Weapons[15], the fist, for an empty hand
            int expected;
            if (s.item < 0)
                expected = 4 + playerdat.STR / 6 + playerdat.Unarmed * 2 / 5;
            else
                expected = playerdat.STR / 9 + (s.swing == 0 ? weaponObjectDat.stab(id) : s.swing == 1 ? weaponObjectDat.slash(id) : weaponObjectDat.bash(id));
            k.Equal($"reach of the blow {s.what}", commonObjDat.radius(id), s.radius, "COMBAT.C GetPlayerWeapon: wsize = ComObjData[item].radius (uw1 :556, 561, uw2 :593, 602)");
            k.Equal($"damage of the blow {s.what}", expected, s.damage, "COMBAT.C DoPlayerWeapon (uw1 :585-588, uw2 :632-635)");
        }
        return k.Result;
    }
}
