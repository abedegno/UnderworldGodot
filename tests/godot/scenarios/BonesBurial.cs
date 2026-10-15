using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// Bones used on a gravestone are buried. In both games a skull or bones 0xC2..0xC6 used from the
/// inventory asks for a target (UseUtil, uw1/src/obj/USEITEMS.C:396-399, uw2/src/obj/USEITEMS.C:1057-1060;
/// how is 1 for a use in the inventory). UseBonesOn then, for a gravestone (0x165), prints "You
/// thoughtfully give the bones a final resting place." (game string 0x86 in UW1, 0x94 in UW2) and uses
/// the bones up (using_punt(ObjectActing, how, 1)); on anything else it prints "It seems to have no
/// effect." (0x84, UW2 0x92) and keeps them (uw1 USEITEMS.C:327-365, where only Garamon's bones on his
/// own grave do more; uw2 USEITEMS.C:42-58).
///
/// The scenario makes a gravestone and a skull on the player's square, puts two skulls with owner 3 in
/// the backpack, and uses each as a player does: use.Use with WorldObject false (the inventory's use
/// click), which must ask for a target, then the click on the target, pickup.PickUp, which hands an
/// item waiting for a target to useon.UseOn (src/interaction/pickup.cs:233-235). The first goes on the
/// gravestone, the second on the skull.
///
/// The port once did this in UW1 only: use.cs and useon.cs gated bones on _RES != GAME_UW2, so in UW2
/// using bones asked for no target and the burial string was never printed.
/// </summary>
public sealed class BonesBurial : Scenario
{
    const int Gravestone = 0x165, Skull = 0xC2;   // items.h: uw1 526, 348; uw2 517, 340

    public override string Name => "bones-burial";
    public override string Cites => "underworld-exhumed uw2/src/obj/USEITEMS.C:42-58 (UseBonesOn), 1057-1060 (UseUtil); uw1/src/obj/USEITEMS.C:327-365, 396-399";

    string _problem;
    readonly List<(string what, bool asked, string expected, string[] got, bool shouldGo, bool gone)> _cases = new();

    public override Task Setup(ScenarioContext c) => Task.CompletedTask;

    public override async Task Act(ScenarioContext c)
    {
        bool uw1 = c.Game == "uw1";
        string bury = GameStrings.GetString(1, uw1 ? 0x86 : 0x94).Trim();     // uw1 USEITEMS.C:359, uw2 USEITEMS.C:53
        string noEffect = GameStrings.GetString(1, uw1 ? 0x84 : 0x92).Trim(); // uw1 USEITEMS.C:363, uw2 USEITEMS.C:56
        if (!bury.StartsWith("You thoughtfully") || !noEffect.StartsWith("It seems to have no effect"))
        { _problem = $"the strings are not the ones expected: '{bury}', '{noEffect}'"; return; }
        var p = playerdat.playerObject;
        var grave = ObjectCreator.spawnObjectInTile(Gravestone, p.tileX, p.tileY, 3, 3, p.zpos, ObjectFreeLists.ObjectListType.StaticList, false);
        var skull = ObjectCreator.spawnObjectInTile(Skull, p.tileX, p.tileY, 4, 4, p.zpos, ObjectFreeLists.ObjectListType.StaticList, false);
        if (grave == null || skull == null) { _problem = "no gravestone or skull could be made beside the player"; return; }
        await UseOn(c, "a skull on a gravestone", grave, bury, true);
        await UseOn(c, "a skull on another skull", skull, noEffect, false);
    }

    async Task UseOn(ScenarioContext c, string what, uwObject target, string expected, bool shouldGo)
    {
        if (_problem != null) return;
        var bones = InventoryAndScroll.MakeInBackpack(Skull, out string why);
        if (bones == null) { _problem = why; return; }
        bones.owner = 3;
        int index = bones.index;
        useon.CurrentItemBeingUsed = null;
        use.Use(bones, playerdat.playerObject, playerdat.InventoryObjects, WorldObject: false);
        bool asked = useon.CurrentItemBeingUsed != null && useon.CurrentItemBeingUsed.index == index;
        if (!asked)
        {
            c.Note($"{what}: using the skull asked for no target; setting useon.CurrentItemBeingUsed as a port item with a target does");
            useon.CurrentItemBeingUsed = new useon(bones, false);
        }
        InventoryAndScroll.ClearScroll();
        pickup.PickUp(target.index, UWTileMap.current_tilemap.LevelObjects, WorldObject: true);
        if (useon.CurrentItemBeingUsed != null) { _problem = $"{what}: the click on the target did not reach the use-on"; return; }
        var got = await InventoryAndScroll.ReadScroll(c);
        bool gone = playerdat.InventoryObjects[index] == null;
        c.Note($"{what}: asked for a target {asked}, printed '{string.Join(" | ", got)}', skull gone {gone}");
        _cases.Add((what, asked, expected, got, shouldGo, gone));
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        if (_cases.Count != 2) return "precondition: not every use happened";
        var k = new Checks();
        foreach (var (what, asked, expected, got, shouldGo, gone) in _cases)
        {
            k.True(asked, $"{what}: using the skull expected to ask for a target, asked for none (UseUtil)");
            k.True(string.Join(" ", got).Contains(expected), $"{what}: expected '{expected}', got '{string.Join(" | ", got)}' (UseBonesOn)");
            k.True(gone == shouldGo, $"{what}: the skull expected {(shouldGo ? "used up" : "kept")}, was {(gone ? "used up" : "kept")} (using_punt only after a burial)");
        }
        return k.Result;
    }
}
