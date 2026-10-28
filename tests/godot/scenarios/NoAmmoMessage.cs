using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace Underworld.Scenarios;

/// <summary>
/// What a bow says with no arrows (UW1 and UW2). The original's check_ammo looks for the launcher's ammunition
/// anywhere in the inventory, bags included (FindObj how 4), and without any prints "Sorry, you have no ", the
/// ammunition's plural name and a full stop. UW1 has the first and last parts as literals in the code (uw1/src/combat/COMBAT.C:519-529);
/// UW2 takes them from strings 0xCF and 0x60 (uw2/src/combat/COMBAT.C:555-563).
///
/// Observed in DOS by Underworld Revisited (PQMarine, MIT): Assets/UWDataImport/UWData/UWItemMessages.cs:115-128
/// ("Sorry, you have no arrows." with the bow, "Sorry, you have no sling stones." with the sling).
///
/// After the message GetPlayerWeapon waits for the button to be let go (mouse_release(1): uw1 COMBAT.C:551,
/// uw2 COMBAT.C:587), so a held press prints it once.
///
/// The scenario puts a bow in the weapon hand with no arrows carried, draws it, holds the attack key for ten frames and
/// reads the scroll. The message must be there once.
///
/// The port printed string 207 of block 1 and the singular name with an "s" (src/interaction/combat/combat_input.cs:189);
/// 207 is UW2's 0xCF, but in UW1 it is another line. It printed it again on every frame the key was held.
/// </summary>
public sealed class NoAmmoMessage : Scenario
{
    const int Bow = 0x19;   // ITEM_BOW: uw1/src/include/items.h:174, uw2 items.h:159

    public override string Name => "no-ammo-message";
    public override string Cites => "underworld-exhumed uw1/src/combat/COMBAT.C:519-529; uw2/src/combat/COMBAT.C:555-563";

    string _problem, _text, _want;

    public override async Task Setup(ScenarioContext c)
    {
        await Guards.CameraOnPlayer(c);
        foreach (int slot in new[] { 7, 8 }) BatchB.Unequip(slot);
        foreach (int slot in new[] { 7, 8 })
        {
            var o = BatchB.Equip(Bow, slot, 40);
            if (o == null) continue;
            if (playerdat.PrimaryHandObject == o) break;
            BatchB.Unequip(slot);
        }
        if (playerdat.PrimaryHandObject?.item_id != Bow) { _problem = "the bow did not reach the weapon hand"; return; }
        int ammoType = rangedObjectDat.RangedWeaponType(Bow);
        if (objectsearch.FindMatchInFullObjectList(0, 1, ammoType, playerdat.InventoryObjects) != null) { _problem = "the player carries arrows"; return; }
        // get_name(buf, fake, 0, 1): the ammunition's plural name (uw1 COMBAT.C:525, uw2 COMBAT.C:560)
        _want = "Sorry, you have no " + GameStrings.GetObjectNounUW(16 + ammoType, 2) + ".";
    }

    public override async Task Act(ScenarioContext c)
    {
        if (_problem != null) return;
        uimanager.ToggleWeaponAnimationState(true, false);
        for (int f = 0; f < 120 && combat.stage != combat.CombatStages.Ready; f++) await c.Frames(1);
        if (combat.stage != combat.CombatStages.Ready) { _problem = $"the bow never became ready ({combat.stage})"; return; }
        uimanager.instance.scroll.Clear();
        Guards.Key(Key.P, true);
        await c.Frames(10);
        Guards.Key(Key.P, false);
        await c.Frames(10);
        _text = string.Join(" ", uimanager.instance.scroll.Lines.Select(l => l?.LineText ?? "")).Trim();
        c.Note($"scroll: \"{_text}\"; expected \"{_want}\"");
        uimanager.ToggleWeaponAnimationState(false, false);
        await c.Frames(30);
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        if (_text.Length == 0) return "precondition: the attack key printed nothing";
        var k = new Checks();
        k.True(_text.Contains(_want), $"a bow with no arrows: expected \"{_want}\", got \"{_text}\" ({(c.Game == "uw1" ? "check_ammo: uw1 COMBAT.C:524-528" : "check_ammo: uw2 COMBAT.C:559-562")})");
        int times = (_text.Length - _text.Replace(_want, "").Length) / _want.Length;
        k.Equal("times the message is printed while the key is held", 1, times, c.Game == "uw1" ? "GetPlayerWeapon waits for the release: uw1 COMBAT.C:551" : "GetPlayerWeapon waits for the release: uw2 COMBAT.C:587");
        return k.Result;
    }
}
