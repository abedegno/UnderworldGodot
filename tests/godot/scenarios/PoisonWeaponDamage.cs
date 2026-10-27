using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// Poison Weapon. In UW2's frp_check, with PoisonWeap set, a sharp weapon in the weapon hand
/// (AskInventory(8 - lefty), is_sharp) against a creature that bleeds adds
/// damage * (skills[9] + 30) / 40 to the blow (uw2/src/combat/COMBAT.C:322-326); skills[9] is
/// Casting (uw2/src/include/player.h:112-115). With Casting 0 that is three quarters of the blow
/// again, so the spell roughly doubles the damage.
///
/// The scenario puts a dagger (item 0, sharp: COMBAT.C:275-290) in the weapon hand, sets the
/// spell's flag, and calls the port's frp_check, combat.CalcAttackResults, for the player against a
/// bleeding creature, with the attack score held two below the creature's defence so the skill
/// check (score 0..28) can never be a critical, whose doubling would hide the bonus
/// (COMBAT.C:328-330). It tries several damages and Casting values and compares AttackDamage after
/// the call with the original's formula.
///
/// UW2 only (UW1 has no Poison Weapon, uw1/src/ui/INTERACT.C:22). The port adds
/// (Casting + 30) / 40, without the damage (src/interaction/combat/combat.cs:422).
/// </summary>
public sealed class PoisonWeaponDamage : Scenario
{
    public override string Name => "poison-weapon-damage";
    public override string[] Games => new[] { "uw2" };
    public override string Cites => "underworld-exhumed uw2/src/combat/COMBAT.C:322-326 (frp_check), 275-290 (is_sharp); uw2/src/include/player.h:112-115";

    static readonly (int damage, int casting)[] Vectors = { (10, 0), (10, 30), (7, 10), (1, 0), (20, 15) };

    string _problem;
    uwObject _target, _dagger;
    readonly List<(int damage, int casting, int expected, int actual, bool crit)> _rows = new();

    public override Task Setup(ScenarioContext c)
    {
        int id = BatchA.PickCritter(i => BatchA.Walker(i) && critterObjectDat.bleed(i) != 0);
        if (id < 0) { _problem = "no creature that bleeds"; return Task.CompletedTask; }
        _target = BatchA.SpawnCritter(id, 0, 0);
        if (_target == null) { _problem = "no critter could be made"; return Task.CompletedTask; }

        int slot = ObjectCreator.SpawnObjectInHand(0);   // a dagger
        if (slot == 0) { _problem = "no dagger could be made"; return Task.CompletedTask; }
        int hand = 8 - playerdat.handednessvalue;          // AskInventory(8 - player->lefty), COMBAT.C:321
        int inv = playerdat.AddObjectToPlayerInventory(slot, false);
        playerdat.SetInventorySlotListHead(hand, inv);
        playerdat.ObjectInHand = -1;
        uimanager.instance.mousecursor.SetCursorToCursor();
        _dagger = playerdat.InventoryObjects[inv];
        if (_dagger == null || _dagger.item_id != 0 || playerdat.GetInventorySlotListHead(hand) != inv)
        { _problem = "the dagger is not in the weapon hand"; return Task.CompletedTask; }
        combat.currentweapon = _dagger;
        playerdat.PoisonedWeapon = true;
        return Task.CompletedTask;
    }

    public override Task Act(ScenarioContext c)
    {
        if (_problem != null) return Task.CompletedTask;
        var t = typeof(combat);
        int defence = critterObjectDat.defence(_target.item_id);
        foreach (var (damage, casting) in Vectors)
        {
            playerdat.Casting = casting;
            BatchA.Set(t, "AttackingCharacter", playerdat.playerObject);
            BatchA.Set(t, "DefendingCharacter", _target);
            combat.AttackScore = defence - 2;           // score 0..28: never a critical (COMBAT.C:328)
            combat.AttackScoreFlankingBonus = 0;
            combat.BodyPartHit = 0;
            combat.AttackDamage = damage;
            BatchA.Call(t, "CalcAttackResults");
            int expected = damage + damage * (playerdat.Casting + 30) / 40;   // COMBAT.C:325
            _rows.Add((damage, casting, expected, combat.AttackDamage, combat.AttackWasACrit));
        }
        c.Note("damage/Casting -> AttackDamage: " + string.Join(", ", _rows.Select(r => $"{r.damage}/{r.casting} -> {r.actual} (DOS {r.expected})")));
        return Task.CompletedTask;
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        if (_rows.Any(r => r.crit)) return "precondition: a blow was a critical, whose doubling hides the bonus";
        var k = new Checks();
        foreach (var r in _rows)
            k.Equal($"poisoned blow of {r.damage} with Casting {r.casting}", r.expected, r.actual, "uw2 COMBAT.C:325");
        return k.Result;
    }
}
