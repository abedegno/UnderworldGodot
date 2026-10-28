using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// Looking at bones says whose they are. In the original, LookAt ends with SpecialLook
/// (uw1/src/obj/LOOK.C:132, uw2's the same), which for a skull or bones 0xC2..0xC6 calls BonesLook
/// (uw1 LOOK.C:425-427, uw2 LOOK.C:445-447). With an owner other than 0, 0x28 and 0x3C..0x3E it prints
/// game string 0x16 (UW2 0x1A), "It looks to be that of ", or the next one, "They look to be those of ",
/// for a pile of bones (0xC6) or a link above 1, then "an adventurer." for owner 0x3F or the name, with
/// its article, of the creature whose item is the owner + 0x40 (FIRST_CREATURE) and a full stop
/// (uw1 LOOK.C:323-345, uw2 LOOK.C:338-361).
///
/// The scenario puts three objects in the backpack and looks at each as the inventory does
/// (look.LookAt with WorldObject false, uimanager_inventory.cs): a skull (0xC2) with owner 3, a pile of
/// bones (0xC6) with owner 0x3F, and a skull with owner 0, which must add nothing. The expected words
/// are the shipped strings, read at run time.
///
/// The port once printed only the description: nothing called the bones strings
/// (src/interaction/look.cs, LookAt).
/// </summary>
public sealed class BonesLookScenario : Scenario
{
    public override string Name => "bones-look";
    public override string Cites => "underworld-exhumed uw1/src/obj/LOOK.C:323-345 (BonesLook), 413-427 (SpecialLook); uw2/src/obj/LOOK.C:338-361, 433-447";

    string _problem;
    readonly List<(string what, string expected, string[] got)> _cases = new();

    public override Task Setup(ScenarioContext c) => Task.CompletedTask;

    public override async Task Act(ScenarioContext c)
    {
        int first = c.Game == "uw1" ? 0x16 : 0x1A;   // uw1 LOOK.C:331, uw2 LOOK.C:346
        string that = GameStrings.GetString(1, first), those = GameStrings.GetString(1, first + 1);
        string creature = InventoryAndScroll.NameWithArticle(3 + 0x40);
        if (!that.StartsWith("It looks") || !those.StartsWith("They look") || creature.Length == 0)
        { _problem = $"the strings are not the ones expected: '{that}', '{those}', creature 0x43 '{creature}'"; return; }

        await Look(c, "a skull (0xC2) with owner 3", 0xC2, 3, $"{that}{creature}.".Trim());
        await Look(c, "a pile of bones (0xC6) with owner 0x3F", 0xC6, 0x3F, $"{those}an adventurer.".Trim());
        await Look(c, "a skull (0xC2) with owner 0", 0xC2, 0, null);
    }

    async Task Look(ScenarioContext c, string what, int item, int owner, string expected)
    {
        if (_problem != null) return;
        var obj = InventoryAndScroll.MakeInBackpack(item, out string why);
        if (obj == null) { _problem = why; return; }
        obj.owner = (short)owner;
        if (obj.owner != owner) { _problem = $"{what}: the owner did not read back"; return; }
        InventoryAndScroll.ClearScroll();
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
            if (expected != null)
                k.True(string.Join(" ", got).Contains(expected), $"looking at {what} expected the line '{expected}', got '{string.Join(" | ", got)}' (BonesLook)");
            else
                k.True(!got.Any(l => l.Contains(" to be th")), $"looking at {what} expected no bones line, got '{string.Join(" | ", got)}' (owner 0 says nothing)");
        }
        return k.Result;
    }
}
