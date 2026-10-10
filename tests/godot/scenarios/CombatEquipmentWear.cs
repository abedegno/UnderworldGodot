using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// Weapons and armour wear in combat. frp_check (uw1/src/combat/COMBAT.C:282,
/// uw2/src/combat/COMBAT.C:300) calls DamageInventory three times:
///  - the player strikes a door: when rand() * 12 / 0x8000 &lt; (id &amp; 7) &lt;&lt; 1 the weapon hand
///    (8 - lefty) takes rollem(2, 4) (uw1 COMBAT.C:290-295, uw2 COMBAT.C:310-315);
///  - a critical against the player: the item at slot (hitloc + 1) &amp; 3, which for hitloc 3 is
///    slot 0, the head, takes rollem(2, 4) if worn there (uw1 COMBAT.C:303-313, uw2 COMBAT.C:328-338);
///  - a bad miss (result -1) by the player against a critter not marked passive: the weapon hand
///    takes rollem(2, 3) (uw1 COMBAT.C:316-320, uw2 COMBAT.C:341-345).
/// DamageInventory (uw1/src/inv/INVDATA.C:419, uw2/src/inv/INVDATA.C:400) hands the damage to
/// damage_item: check_res, then damage_object shifts it right by the item's toughness
/// (ComObjData qualclass) and takes it off the quality (uw2/src/combat/DAMAGE.C:257-297; UW1's is
/// the same). rollem(n, d) is n plus n draws of 0..d-1, so the loss lies between
/// (2 &gt;&gt; qualclass) and (8 &gt;&gt; qualclass), or (6 &gt;&gt; qualclass) for the miss.
///
/// The scenario puts a weapon of toughness 0 or 1 that does not resist physical damage (type 4)
/// in the weapon hand and such an item on the head, then calls the port's frp_check,
/// combat.CalcAttackResults, for each case: the player against a door whose id ends in 6 (12, so
/// the draw, 0..11, always passes), a critter against the player with an attack score so high the
/// check is always a critical and hitloc 3, and the player against a critter that is not passive
/// with an attack score so low it is always a bad miss.
///
/// Both games. The port only prints a debug line in the three places
/// (src/interaction/combat/combat.cs:452, 467, 488).
/// </summary>
public sealed class CombatEquipmentWear : Scenario
{
    public override string Name => "combat-equipment-wear";
    public override string Cites => "underworld-exhumed uw1/src/combat/COMBAT.C:282-322, uw1/src/inv/INVDATA.C:419-450; uw2/src/combat/COMBAT.C:300-347, uw2/src/inv/INVDATA.C:400-445, uw2/src/combat/DAMAGE.C:238-297";

    string _problem;
    uwObject _weapon, _helm, _door, _critter;
    int _wq;   // the weapon's toughness
    int _hq;   // the head item's toughness
    int _doorBefore, _doorAfter, _critBefore, _critAfter, _missBefore, _missAfter;
    bool _wasCrit, _missResult;

    static bool Tough(int id) => commonObjDat.qualityclass(id) <= 1 && (commonObjDat.scaleresistances(id) & 4) == 0;

    uwObject PutInSlot(int item, int slot)
    {
        int s = ObjectCreator.SpawnObjectInHand(item);
        if (s == 0) return null;
        int inv = playerdat.AddObjectToPlayerInventory(s, false);
        playerdat.SetInventorySlotListHead(slot, inv);
        playerdat.ObjectInHand = -1;
        uimanager.instance.mousecursor.SetCursorToCursor();
        var o = playerdat.InventoryObjects[inv];
        if (o == null || playerdat.GetInventorySlotListHead(slot) != inv) return null;
        o.quality = 0x28;
        return o;
    }

    public override Task Setup(ScenarioContext c)
    {
        int weapon = -1, helm = -1;
        for (int id = 0; id < 0x10 && weapon < 0; id++) if (Tough(id)) weapon = id;
        for (int id = 0x20; id < 0x40; id++)
        {
            if (!Tough(id)) continue;
            string n = GameStrings.GetSimpleObjectNameUW(id).ToLowerInvariant();
            if (helm < 0 || n.Contains("helm") || n.Contains("cap")) { helm = id; if (n.Contains("helm") || n.Contains("cap")) break; }
        }
        int crit = BatchA.PickCritter(i => BatchA.Walker(i) && !critterObjectDat.damagesWeaponOnCritMiss(i));   // passive bit clear, COMBAT.C passive
        if (weapon < 0 || helm < 0 || crit < 0) { _problem = $"no items to use (weapon {weapon}, head {helm}, critter {crit})"; return Task.CompletedTask; }

        _weapon = PutInSlot(weapon, 8 - playerdat.handednessvalue);
        _helm = PutInSlot(helm, 0);
        if (_weapon == null || _helm == null) { _problem = "the weapon or the head item did not go into its slot"; return Task.CompletedTask; }
        _wq = commonObjDat.qualityclass(weapon);
        _hq = commonObjDat.qualityclass(helm);
        combat.currentweapon = _weapon;

        var p = playerdat.playerObject;
        _door = ObjectCreator.spawnObjectInTile(itemid: 0x146, tileX: p.tileX, tileY: p.tileY, xpos: 0, ypos: 0, zpos: p.zpos, RenderImmediately: false);
        _critter = BatchA.SpawnCritter(crit, 0, 0);
        if (_door == null || _critter == null) { _problem = "no door or critter could be made"; return Task.CompletedTask; }
        c.Note($"weapon 0x{weapon:X} (toughness {_wq}) in slot {8 - playerdat.handednessvalue}, head item 0x{helm:X} (toughness {_hq}), critter 0x{crit:X}");
        return Task.CompletedTask;
    }

    static int Attack(uwObject attacker, uwObject defender, int score, int bodyPart)
    {
        var t = typeof(combat);
        BatchA.Set(t, "AttackingCharacter", attacker);
        BatchA.Set(t, "DefendingCharacter", defender);
        combat.AttackScore = score;
        combat.AttackScoreFlankingBonus = 0;
        combat.BodyPartHit = bodyPart;
        combat.AttackDamage = 5;
        return (int)BatchA.Call(t, "CalcAttackResults");
    }

    public override Task Act(ScenarioContext c)
    {
        if (_problem != null) return Task.CompletedTask;
        var p = playerdat.playerObject;

        _doorBefore = _weapon.quality;
        Attack(p, _door, 0, 0);
        _doorAfter = _weapon.quality;

        _critBefore = _helm.quality;
        for (int i = 0; i < 4; i++) playerdat.LocationalProtectionValues[i] = 0;
        Attack(_critter, p, 200, 3);
        _wasCrit = combat.AttackWasACrit;
        _critAfter = _helm.quality;

        _missBefore = _weapon.quality;
        _missResult = Attack(p, _critter, critterObjectDat.defence(_critter.item_id) - 40, 0) == 2;   // 1 - result, result -1
        _missAfter = _weapon.quality;

        c.Note($"door strike: weapon {_doorBefore} -> {_doorAfter}; critical on the player (crit {_wasCrit}): head {_critBefore} -> {_critAfter}; bad miss: weapon {_missBefore} -> {_missAfter}");
        return Task.CompletedTask;
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        if (!_wasCrit) return "precondition: the blow on the player was not a critical";
        if (!_missResult) return "precondition: the low attack was not a bad miss (frp_check did not return 2)";
        var k = new Checks();
        string g = c.Game;
        k.Within("weapon wear from a door strike", 2 >> _wq, 8 >> _wq, _doorBefore - _doorAfter,
            g == "uw1" ? "uw1 COMBAT.C:290-295, INVDATA.C:419" : "uw2 COMBAT.C:310-315, INVDATA.C:400");
        k.Within("head armour wear from a critical on the player", 2 >> _hq, 8 >> _hq, _critBefore - _critAfter,
            g == "uw1" ? "uw1 COMBAT.C:303-313" : "uw2 COMBAT.C:328-338");
        k.Within("weapon wear from a bad miss", 2 >> _wq, 6 >> _wq, _missBefore - _missAfter,
            g == "uw1" ? "uw1 COMBAT.C:316-320" : "uw2 COMBAT.C:341-345");
        return k.Result;
    }
}
