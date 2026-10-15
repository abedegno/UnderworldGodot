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
/// A shield counts too. FixPlayerEquips applies the enchantment of every item ObjWorn says is
/// worn in slots 0 to 10 (uw2 PLAYDATA.C:405-411, uw1 PLAYDATA.C:445-454), and in the hands that
/// is a shield (MAJOR_HACK, minor 2 or 3, class 0xB..0xF) in the shield hand, lefty + 7, alone
/// (uw2 INVDATA.C:384-393, uw1 INVDATA.C:403-412). The shield's slot is above 4, so it gives
/// locations 0 and 1, body and arms. For a right- and a left-handed player the scenario puts a
/// shield of protection and one of toughness in the shield hand (the port's 7 + handednessvalue)
/// and in the weapon hand (8 - handednessvalue), runs the port's PlayerStatusUpdate, and compares
/// both arrays with the same shield unenchanted in the same slot: the shield hand gets the
/// class 12 bonus, the weapon hand nothing.
///
/// The loop runs to slot 10 (slot &lt;= 10 in both games), so a ring in either ring slot, 9 or 10,
/// applies; the scenario checks a ring of protection and one of toughness in each the same way,
/// against the same ring unenchanted.
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
    readonly List<(string what, int slot, int minor, bool shieldHand, int[] th, int[] armour)> _shield = new();
    string _problem;

    static (int[] th, int[] armour) Snapshot() => ((int[])playerdat.LocationalProtectionValues.Clone(), (int[])playerdat.LocationalArmourValues.Clone());
    static int[] Minus(int[] a, int[] b) { var d = new int[4]; for (int i = 0; i < 4; i++) d[i] = a[i] - b[i]; return d; }

    void RingRows(ScenarioContext c)
    {
        const int ringId = 0x39;   // MAJOR_HACK, minor 3, class 9: a ring the port accepts in a ring slot in both games (uimanager.ValidObjectForSlot)
        int s = ObjectCreator.SpawnObjectInHand(ringId);
        int inv = s == 0 ? 0 : playerdat.AddObjectToPlayerInventory(s, false);
        playerdat.ObjectInHand = -1;
        uimanager.instance.mousecursor.SetCursorToCursor();
        var ring = inv == 0 ? null : playerdat.InventoryObjects[inv];
        if (ring == null) { _problem = "no ring could be made"; return; }
        ring.is_quant = 1;
        ring.flags_full = (short)(ring.flags_full & ~4);   // a passive enchantment
        int s9 = playerdat.GetInventorySlotListHead(9), s10 = playerdat.GetInventorySlotListHead(10);
        foreach (int slot in new[] { 9, 10 })
        {
            playerdat.SetInventorySlotListHead(9, 0);
            playerdat.SetInventorySlotListHead(10, 0);
            playerdat.SetInventorySlotListHead(slot, inv);
            foreach (int minor in new[] { 2, 0x8 | 1 })
            {
                ring.enchantment = 0;
                ring.link = 0;
                playerdat.PlayerStatusUpdate();
                var plain = Snapshot();
                ring.enchantment = 1;
                ring.link = (short)(0x2C0 | minor);   // LINK_SPECIAL | major 0xC << 4 | minor
                playerdat.PlayerStatusUpdate();
                var ench = Snapshot();
                _shield.Add(($"ring 0x{ringId:X} in slot {slot}", slot, minor, true, Minus(ench.th, plain.th), Minus(ench.armour, plain.armour)));
            }
        }
        ring.enchantment = 0;
        ring.link = 0;
        playerdat.SetInventorySlotListHead(9, s9);
        playerdat.SetInventorySlotListHead(10, s10);
        playerdat.PlayerStatusUpdate();
        foreach (var r in _shield) if (r.what.StartsWith("ring")) c.Note($"{r.what} minor 0x{r.minor:X}: to-hit [{string.Join(",", r.th)}], armour [{string.Join(",", r.armour)}]");
    }

    void ShieldRows(ScenarioContext c)
    {
        const int shieldId = 0x3B;   // the first shield: MAJOR_HACK, minor 3, class 0xB
        int s = ObjectCreator.SpawnObjectInHand(shieldId);
        int inv = s == 0 ? 0 : playerdat.AddObjectToPlayerInventory(s, false);
        playerdat.ObjectInHand = -1;
        uimanager.instance.mousecursor.SetCursorToCursor();
        var shield = inv == 0 ? null : playerdat.InventoryObjects[inv];
        if (shield == null) { _problem = "no shield could be made"; return; }
        shield.is_quant = 1;
        shield.flags_full = (short)(shield.flags_full & ~4);   // a passive enchantment

        bool wasLefty = playerdat.isLefty;
        int s7 = playerdat.GetInventorySlotListHead(7), s8 = playerdat.GetInventorySlotListHead(8);
        foreach (bool lefty in new[] { false, true })
        {
            playerdat.isLefty = lefty;
            string who = lefty ? "left-handed" : "right-handed";
            int shieldHand = 7 + playerdat.handednessvalue, weaponHand = 8 - playerdat.handednessvalue;
            foreach (int slot in new[] { shieldHand, weaponHand })
            {
                playerdat.SetInventorySlotListHead(7, 0);
                playerdat.SetInventorySlotListHead(8, 0);
                playerdat.SetInventorySlotListHead(slot, inv);
                foreach (int minor in new[] { 2, 0x8 | 1 })
                {
                    shield.enchantment = 0;
                    shield.link = 0;
                    playerdat.PlayerStatusUpdate();
                    var plain = Snapshot();
                    shield.enchantment = 1;
                    shield.link = (short)(0x2C0 | minor);   // LINK_SPECIAL | major 0xC << 4 | minor
                    var e = MagicEnchantment.GetSpellEnchantment(shield, playerdat.InventoryObjects);
                    if (e == null || e.SpellMajorClass != 0xC || e.SpellMinorClass != minor) { _problem = $"the shield does not read back as major 0xC minor {minor}"; break; }
                    playerdat.PlayerStatusUpdate();
                    var ench = Snapshot();
                    string what = $"{who} player, shield 0x{shieldId:X} in the {(slot == shieldHand ? "shield" : "weapon")} hand (slot {slot})";
                    _shield.Add((what, slot, minor, slot == shieldHand, Minus(ench.th, plain.th), Minus(ench.armour, plain.armour)));
                }
            }
        }
        shield.enchantment = 0;
        shield.link = 0;
        playerdat.isLefty = wasLefty;
        playerdat.SetInventorySlotListHead(7, s7);
        playerdat.SetInventorySlotListHead(8, s8);
        playerdat.PlayerStatusUpdate();
        foreach (var r in _shield) c.Note($"{r.what} minor 0x{r.minor:X}: to-hit [{string.Join(",", r.th)}], armour [{string.Join(",", r.armour)}]");
    }

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
        ShieldRows(c);
        RingRows(c);
        return Task.CompletedTask;
    }

    public override string Assert(ScenarioContext c)
    {
        string cite = c.Game == "uw2" ? "uw2 PLAYDATA.C:251-263" : "uw1 PLAYDATA.C:273-285";
        var k = new Checks();
        if (_problem != null) k.True(false, "precondition: " + _problem);
        string shieldCite = c.Game == "uw2" ? "uw2 PLAYDATA.C:405-411, INVDATA.C:384-393" : "uw1 PLAYDATA.C:445-454, INVDATA.C:403-412";
        foreach (var r in _shield)
        {
            var (th, armour) = r.shieldHand ? Expected(r.slot, r.minor) : (new int[4], new int[4]);
            string kind = (r.minor & 8) != 0 ? "toughness" : "protection";
            k.True(Same(th, r.th), $"{r.what} of {kind} minor 0x{r.minor:X}: to-hit protection gained expected [{string.Join(",", th)}], got [{string.Join(",", r.th)}] ({shieldCite})");
            k.True(Same(armour, r.armour), $"{r.what} of {kind} minor 0x{r.minor:X}: armour gained expected [{string.Join(",", armour)}], got [{string.Join(",", r.armour)}] ({shieldCite})");
        }
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
