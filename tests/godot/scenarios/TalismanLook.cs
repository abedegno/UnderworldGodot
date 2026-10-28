using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// An identified talisman is described by its own text. In the original, LookAt at lore 3 first asks
/// talisman_desc (uw1/src/obj/LOOK.C:74), which for an object whose COMOBJ fate is 10 prints game
/// string 0x104, "You see ", and 0x105 + which, the talisman's own name ("the Cup of Wonder.", "the
/// Book of Honesty.", ...), and LookAt then prints nothing else (uw1 LOOK.C:443-464). Lore 3 comes from
/// an inventory look whose stored lore result is 3 (inv_look, uw1/src/ui/INTERACT.C:640-659: heading
/// bit 2 set means looked at before, bits 0..1 the result) or from Name Enchantment, which looks with
/// lore 3 (uw1/src/combat/SPELLS.C:742).
///
/// The scenario puts the Cup of Wonder (0xAE, which 6) and the Book of Honesty (0x136, which 0; a
/// book, which the port otherwise reads aloud) in the backpack, marks them identified at lore 3
/// (heading 4 | 3), and looks at each as the inventory does (look.LookAt with WorldObject false); then
/// casts Name Enchantment's effect on a fresh, unidentified cup (SpellCasting.NameEnchantment). Each
/// must print exactly one line, "You see " and the talisman's text. The COMOBJ fate of both items is
/// checked to be 10 first.
///
/// UW1 only: UW2 has no talisman_desc. The port once gave the plain item description
/// (src/interaction/look.cs, PrintLookDescription) and read the book.
/// </summary>
public sealed class TalismanLookScenario : Scenario
{
    const int Cup = 0xAE, Book = 0x136;   // ITEM_SHINY_CUP, ITEM_BOOK_136, uw1/src/include/items.h:324, 475

    public override string Name => "talisman-look";
    public override string[] Games => new[] { "uw1" };
    public override string Cites => "underworld-exhumed uw1/src/obj/LOOK.C:74, 443-464 (talisman_desc); uw1/src/ui/INTERACT.C:640-659 (inv_look); uw1/src/combat/SPELLS.C:742";

    string _problem;
    readonly List<(string what, string expected, string[] got)> _cases = new();

    public override Task Setup(ScenarioContext c)
    {
        // fate is bits 1-4 of COMOBJ byte 7 (uw1/src/include/object.h:49), the port's projectilecullingpriority
        foreach (var item in new[] { Cup, Book })
            if (commonObjDat.projectilecullingpriority(item) != 10)
                _problem = $"item 0x{item:X} has COMOBJ fate {commonObjDat.projectilecullingpriority(item)}, not 10";
        return Task.CompletedTask;
    }

    public override async Task Act(ScenarioContext c)
    {
        string youSee = GameStrings.GetString(1, 0x104);
        await Look(c, "the identified cup (0xAE)", Cup, $"{youSee}{GameStrings.GetString(1, 0x105 + 6)}".Trim(), nameEnchantment: false);
        await Look(c, "the identified book (0x136)", Book, $"{youSee}{GameStrings.GetString(1, 0x105 + 0)}".Trim(), nameEnchantment: false);
        await Look(c, "a cup under Name Enchantment", Cup, $"{youSee}{GameStrings.GetString(1, 0x105 + 6)}".Trim(), nameEnchantment: true);
    }

    async Task Look(ScenarioContext c, string what, int item, string expected, bool nameEnchantment)
    {
        if (_problem != null) return;
        var obj = InventoryAndScroll.MakeInBackpack(item, out string why);
        if (obj == null) { _problem = why; return; }
        if (!nameEnchantment)
        {
            obj.heading = 4 | 3;
            if (look.LoreCheck(obj) != 3) { _problem = $"{what}: the lore result did not read back 3"; return; }
        }
        InventoryAndScroll.ClearScroll();
        if (nameEnchantment)
            SpellCasting.NameEnchantment(obj.index, playerdat.InventoryObjects);
        else
            look.LookAt(obj.index, playerdat.InventoryObjects, false);
        var got = await InventoryAndScroll.ReadScroll(c);
        c.Note($"{what}: {string.Join(" | ", got)}");
        _cases.Add((what, expected, got));
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        if (_cases.Count != 3) return "precondition: not every look happened";
        var k = new Checks();
        foreach (var (what, expected, got) in _cases)
        {
            if (got.Length == 0) return $"precondition: looking at {what} printed nothing";
            k.True(got.Length == 1 && got[0] == expected, $"looking at {what} expected only '{expected}', got '{string.Join(" | ", got)}' (talisman_desc, LOOK.C:459-460)");
        }
        return k.Result;
    }
}
