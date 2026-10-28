using System.Collections.Generic;
using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// Items of protection and toughness. The original's player_affected_by, spell class 12, takes
/// the item's paper doll slot: slots 0 to 4 give one hit location (defence_slot_index: helm 3,
/// body 0, gloves 1, leggings and boots 2), slots above 4 (the rings) give locations 0 and 1.
/// Without the 8 bit in the minor (protection) it adds (minor &amp; 7) + 1 to the to-hit protection
/// cmbModTH of each location; with it (toughness) it adds (minor &amp; 7) + 1 to armour, always to
/// the first location's, once per location, so a ring of toughness adds it twice to location 0
/// (uw2/src/game/PLAYDATA.C:249-264; uw1/src/game/PLAYDATA.C:271-286).
///
/// Each row clears the port's LocationalProtectionValues and LocationalArmourValues, calls the
/// port's SpellCasting.CastEnchantedItemSpell with major class 12 for one slot and minor (the call
/// PlayerStatusUpdate makes for a worn enchanted item, src/player/playerdatloop.cs), and compares
/// both arrays with the original's arithmetic.
///
/// The port once handled only slots 0 and 1, left out the +1, and added toughness to the to-hit
/// protection instead of armour (src/magic/spellcasting_objects.cs:61-77).
/// </summary>
public sealed class ItemProtection : Scenario
{
    static readonly int[] DefenceSlot = { 3, 0, 1, 2, 2 };   // defence_slot_index, PLAYDATA.C:59

    // (paper doll slot, minor)
    static readonly (int Slot, int Minor)[] Rows =
    {
        (0, 1),        // helm of protection: head +2
        (1, 0x8 | 2),  // body armour of toughness: body armour +3
        (2, 3),        // gloves of protection: arms +4
        (3, 0x8 | 1),  // leggings of toughness: legs armour +2
        (4, 0),        // boots of protection: legs +1
        (9, 2),        // ring of protection: body and arms +3
        (10, 0x8 | 3), // ring of toughness: body armour +4, twice
    };

    public override string Name => "item-protection";
    public override string Cites => "underworld-exhumed uw2/src/game/PLAYDATA.C:249-264 (player_affected_by, class 12); uw1/src/game/PLAYDATA.C:271-286";

    readonly List<(int slot, int minor, int[] th, int[] armour)> _results = new();

    static (int[] th, int[] armour) Expected(int slot, int minor)
    {
        var th = new int[4];
        var armour = new int[4];
        int[] slots = slot > 4 ? new[] { 0, 1 } : new[] { DefenceSlot[slot] };
        foreach (int s in slots)
        {
            int toughness = 0;
            if ((minor & 8) != 0) toughness = (minor & 7) + 1;
            else th[s] += (minor & 7) + 1;
            armour[slots[0]] += toughness;
        }
        return (th, armour);
    }

    public override Task Setup(ScenarioContext c) => Task.CompletedTask;

    public override Task Act(ScenarioContext c)
    {
        foreach (var r in Rows)
        {
            for (int i = 0; i < 4; i++) { playerdat.LocationalProtectionValues[i] = 0; playerdat.LocationalArmourValues[i] = 0; }
            int dr = 0, sb = 0;
            SpellCasting.CastEnchantedItemSpell(majorclass: 12, minorclass: r.Minor, TriggeredByInventoryEvent: false,
                DamageResistance: ref dr, StealthBonus: ref sb, PaperDollSlot: r.Slot);
            _results.Add((r.Slot, r.Minor, (int[])playerdat.LocationalProtectionValues.Clone(), (int[])playerdat.LocationalArmourValues.Clone()));
        }
        for (int i = 0; i < 4; i++) { playerdat.LocationalProtectionValues[i] = 0; playerdat.LocationalArmourValues[i] = 0; }
        playerdat.PlayerStatusUpdate();
        return Task.CompletedTask;
    }

    public override string Assert(ScenarioContext c)
    {
        string cite = c.Game == "uw2" ? "uw2 PLAYDATA.C:251-263" : "uw1 PLAYDATA.C:273-285";
        var k = new Checks();
        foreach (var r in _results)
        {
            var (th, armour) = Expected(r.slot, r.minor);
            string kind = (r.minor & 8) != 0 ? "toughness" : "protection";
            k.True(Same(th, r.th), $"slot {r.slot} {kind} minor 0x{r.minor:X}: to-hit protection by location expected [{string.Join(",", th)}], got [{string.Join(",", r.th)}] ({cite})");
            k.True(Same(armour, r.armour), $"slot {r.slot} {kind} minor 0x{r.minor:X}: armour by location expected [{string.Join(",", armour)}], got [{string.Join(",", r.armour)}] ({cite})");
        }
        return k.Result;
    }

    static bool Same(int[] a, int[] b)
    {
        for (int i = 0; i < 4; i++) if (a[i] != b[i]) return false;
        return true;
    }
}
