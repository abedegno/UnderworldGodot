using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// The sound of a blocked blow, 7 or 8. The original's do_miss (uw1/src/combat/COMBAT.C:429-476,
/// uw2/src/combat/COMBAT.C:466-511), for a nonzero hit:
///
/// UW1 puts the attacker's item id into fromwho itself before testing it (uw1 :445), so the weapon
/// kind is 1 for item id 1, the attacker's Creature weapon_kind (by item id &amp; 0x3F) for any item id
/// below 0x100, else 1 (uw1 :446-451). The player (item 0x7F) therefore gets Creature[0x3F]'s weapon
/// kind, never the weapon he swings. UW2 keeps the index: the player's weapon sound class
/// (player_weapon) for index 1, the attacker's weapon_kind for any other index below 0x100, else 1
/// (uw2 :482-488). Against anything but the player, the armour kind is the ATTACKER's Creature
/// armour_kind in both games (uw1 :463-466, uw2 :500-501; the code's note says probably meant to be
/// the defender's), and 0 for a defender index of 0x100 or more. UW1 plays 7 only for weapon 1 and
/// armour 1 (uw1 :469-472); UW2 for weapon 1, or weapon 2 and armour 1 (uw2 :504-507).
///
/// The port's CombatMissImpactSound (src/interaction/combat/combat.cs:302) used UW2's attacker test
/// in both games and the defender's armour_kind. The scenario calls its chooser, the private
/// combat.MissImpactEffect, by reflection for every critter type as attacker against every critter
/// type as defender, and for the player attacking every critter type with each weapon sound class
/// 0 to 3, and compares with the original's choice. A player defender (the armour-slot path,
/// fixed by PR #115) is not covered here.
/// </summary>
public sealed class MissImpactSound : Scenario
{
    public override string Name => "miss-impact-sound";
    public override string Cites => "underworld-exhumed uw1/src/combat/COMBAT.C:429-476; uw2/src/combat/COMBAT.C:466-511";

    string _problem;
    int _cases;
    readonly List<string> _wrong = new();

    public override Task Setup(ScenarioContext c) => Task.CompletedTask;

    static int Original(bool uw2, int attIndex, int attItem, int playerWeapon, int defIndex)
    {
        int weapon;
        if (uw2)
            weapon = attIndex == 1 ? playerWeapon : attIndex < 0x100 ? critterObjectDat.combatimpactsound_7_3(attItem) : 1;
        else
            weapon = attItem == 1 ? 1 : attItem < 0x100 ? critterObjectDat.combatimpactsound_7_3(attItem & 0x3F) : 1;
        int victim = defIndex < 0x100 ? critterObjectDat.combatimpactsound_4_3(attItem & 0x3F) : 0;
        if (uw2) return (weapon == 1 || (weapon == 2 && victim == 1)) ? 7 : 8;
        return (weapon == 1 && victim == 1) ? 7 : 8;
    }

    public override Task Act(ScenarioContext c)
    {
        var t = typeof(combat);
        var chooser = t.GetMethod("MissImpactEffect", BindingFlags.NonPublic | BindingFlags.Static);
        var att = t.GetField("AttackingCharacter", BindingFlags.NonPublic | BindingFlags.Static);
        var def = t.GetField("DefendingCharacter", BindingFlags.NonPublic | BindingFlags.Static);
        if (chooser == null || att == null || def == null) { _problem = "combat.MissImpactEffect, AttackingCharacter or DefendingCharacter not found"; return Task.CompletedTask; }

        int a = ObjectCreator.PrepareNewObject(0x40, ObjectFreeLists.ObjectListType.MobileList);
        int d = ObjectCreator.PrepareNewObject(0x40, ObjectFreeLists.ObjectListType.MobileList);
        if (a == 0 || d == 0) { _problem = "no critters could be made"; return Task.CompletedTask; }
        var objs = UWTileMap.current_tilemap.LevelObjects;
        var attacker = objs[a];
        var defender = objs[d];
        var player = playerdat.playerObject;
        bool uw2 = c.Game == "uw2";
        object attWas = att.GetValue(null), defWas = def.GetValue(null);
        int weaponWas = combat.PlayerWeaponSound;
        try
        {
            for (int di = 0x40; di < 0x7F; di++)
            {
                defender.item_id = di;
                def.SetValue(null, defender);
                for (int ai = 0x40; ai < 0x7F; ai++)
                {
                    attacker.item_id = ai;
                    att.SetValue(null, attacker);
                    Check(chooser, uw2, attacker, defender, weaponWas, $"critter 0x{ai:X} against critter 0x{di:X}");
                }
                att.SetValue(null, player);
                for (int pw = 0; pw < 4; pw++)
                {
                    combat.PlayerWeaponSound = pw;
                    Check(chooser, uw2, player, defender, pw, $"player (weapon sound {pw}) against critter 0x{di:X}");
                }
            }
        }
        finally
        {
            att.SetValue(null, attWas); def.SetValue(null, defWas);
            combat.PlayerWeaponSound = weaponWas;
        }
        c.Note($"{_cases} cases, {_wrong.Count} wrong");
        return Task.CompletedTask;
    }

    void Check(MethodInfo chooser, bool uw2, uwObject attacker, uwObject defender, int playerWeapon, string what)
    {
        int expected = Original(uw2, attacker.index, attacker.item_id, playerWeapon, defender.index);
        int got = (int)chooser.Invoke(null, null);
        _cases++;
        if (got != expected) _wrong.Add($"{what}: expected {expected}, got {got}");
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        if (_cases == 0) return "precondition: no cases ran";
        if (_wrong.Count == 0) return null;
        string cite = c.Game == "uw1" ? "uw1 COMBAT.C:445-472" : "uw2 COMBAT.C:482-507";
        return $"blocked-blow sound wrong in {_wrong.Count} of {_cases} cases ({cite}), first: " + string.Join("; ", _wrong.GetRange(0, System.Math.Min(4, _wrong.Count)));
    }
}
