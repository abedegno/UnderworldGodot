using System.Collections.Generic;
using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// When an object dropped on an inventory slot joins the stack there, and what quality the stack gets (UW1
/// and UW2). The original's AddTogether merges two objects of the same item only when neither carries a link
/// in place of a count, the item is stackable (COMOBJ.DAT stack field not 1 or 3), keys open the same lock
/// (the same owner), the total stays below 999 and, except for missiles, both qualities lie in the same band
/// of sixteen with neither ruined unless both are (uw1/src/inv/INVPANEL.C:697-721; uw2/src/inv/INVPANEL.C:686-712).
/// AddToOccupiedSlot then counts an object without the quantity bit as one and gives the stack the plain average
/// of the two qualities, (q1 + q2) >> 1, whatever the counts (uw1 INVPANEL.C:744-766; uw2 INVPANEL.C:735-757).
/// Anything else is swapped with what is in the slot.
///
/// Observed in DOS by Underworld Revisited (PQMarine, MIT): Assets/UWDataImport/UWData/UWInventoryModel.cs:964-966,
/// 1001-1003, 1039-1044, 1051-1053.
///
/// The scenario puts an object in the right shoulder (slot 5) and drops another on it, as a click on the
/// occupied slot does (uimanager.UseObjectsTogether), in seven cases: two single torches of quality 40 and 35
/// (the observed pair: a stack of 2 at 37); a stack of 3 torches at 40 and one at 35 (a stack of 4 at 37); torches
/// at 40 and 50 (different bands: kept apart); torches at 0 and 10 (one ruined: kept apart); two keys 0x102 for
/// lock 1; keys 0x102 for locks 1 and 2 (kept apart); and two lockpicks without the quantity bit (a stack of 2).
/// Where the game data marks an item as not stackable, the original keeps its pieces apart and so must the port.
///
/// The port merged any two pieces of a stackable item, weighted the quality by the counts and added the link
/// fields as counts (src/ui/uimanager_inventory.cs:683-697).
/// </summary>
public sealed class StackMerge : Scenario
{
    const int Torch = 0x91, Key = 0x102, Lockpick = 0x101;   // ITEM_TORCH, ITEM_KEY_102, ITEM_LOCKPICK: uw1/src/include/items.h:293, 415-417; uw2 items.h:285, 407
    const int Slot = 5;

    public override string Name => "stack-merge";
    public override string Cites => "underworld-exhumed uw1/src/inv/INVPANEL.C:697-721, 744-766; uw2/src/inv/INVPANEL.C:686-712, 735-757";

    sealed record Piece(int Item, bool Quant, int Count, int Quality, int Owner);
    sealed record Case(string What, Piece InSlot, Piece Dropped, bool Bands, int Quality);

    static readonly Case[] Cases =
    {
        new("torches at 40 and 35", new(Torch, true, 1, 40, 0), new(Torch, true, 1, 35, 0), true, 37),
        new("3 torches at 40 and one at 35", new(Torch, true, 3, 40, 0), new(Torch, true, 1, 35, 0), true, 37),
        new("torches at 40 and 50", new(Torch, true, 1, 40, 0), new(Torch, true, 1, 50, 0), false, 0),
        new("torches at 0 and 10", new(Torch, true, 1, 0, 0), new(Torch, true, 1, 10, 0), false, 0),
        new("keys for lock 1 and lock 1", new(Key, true, 1, 40, 1), new(Key, true, 1, 40, 1), true, 40),
        new("keys for lock 1 and lock 2", new(Key, true, 1, 40, 1), new(Key, true, 1, 40, 2), false, 0),
        new("two lockpicks without the quantity bit", new(Lockpick, false, 0, 40, 0), new(Lockpick, false, 0, 40, 0), true, 40),
    };

    string _problem;
    readonly List<(bool merged, int count, int quality, int item)> _after = new();

    static int Count(uwObject o) => o.is_quant == 1 ? (o.link >= 0x200 ? -1 : o.link) : 1;

    static void Shape(uwObject o, Piece p)
    {
        o.quality = (short)p.Quality;
        o.owner = (short)p.Owner;
        o.is_quant = (short)(p.Quant ? 1 : 0);
        o.link = (short)(p.Quant ? p.Count : 0);
    }

    static async Task Clear(ScenarioContext c)
    {
        int head = playerdat.GetInventorySlotListHead(Slot);
        if (head != 0) { uimanager.CurrentSlot = Slot; playerdat.RemoveFromInventory(head); }
        if (playerdat.ObjectInHand > 0)
        {
            ObjectFreeLists.ReleaseFreeObject(UWTileMap.current_tilemap.LevelObjects[playerdat.ObjectInHand]);
            playerdat.ObjectInHand = -1;
            uimanager.instance.mousecursor.SetCursorToCursor();
        }
        playerdat.PlayerStatusUpdate();
        await c.Frames(1);
    }

    public override Task Setup(ScenarioContext c)
    {
        if (playerdat.GetInventorySlotListHead(Slot) != 0) _problem = $"slot {Slot} is not empty at the start";
        if (playerdat.ObjectInHand > 0) _problem = "the player holds something at the start";
        return Task.CompletedTask;
    }

    public override async Task Act(ScenarioContext c)
    {
        if (_problem != null) return;
        foreach (var k in Cases)
        {
            int world = ObjectCreator.SpawnObjectInHand(k.InSlot.Item, changeInteractionmode: false);
            if (world == 0) { _problem = "no object could be made"; return; }
            Shape(UWTileMap.current_tilemap.LevelObjects[world], k.InSlot);
            int index = playerdat.AddObjectToPlayerInventory(world, false);
            playerdat.SetInventorySlotListHead(Slot, index);
            playerdat.ObjectInHand = -1;

            int hand = ObjectCreator.SpawnObjectInHand(k.Dropped.Item, changeInteractionmode: false);
            if (hand == 0) { _problem = "no object could be made for the hand"; return; }
            Shape(UWTileMap.current_tilemap.LevelObjects[hand], k.Dropped);
            uimanager.UpdateInventoryDisplay();
            uimanager.CurrentSlot = Slot;
            uimanager.UseObjectsTogether(hand, index);
            await c.Frames(1);

            int head = playerdat.GetInventorySlotListHead(Slot);
            var o = head == 0 ? null : playerdat.InventoryObjects[head];
            bool handEmpty = playerdat.ObjectInHand <= 0;
            _after.Add((handEmpty, o == null ? 0 : Count(o), o?.quality ?? -1, o?.item_id ?? 0));
            await Clear(c);
        }
        var notes = new List<string>();
        for (int i = 0; i < _after.Count; i++)
            notes.Add($"{Cases[i].What}: {(_after[i].merged ? "merged" : "kept apart")}, slot 0x{_after[i].item:X} x{_after[i].count} q{_after[i].quality}");
        notes.Add($"stack field torch {commonObjDat.stackable(Torch)}, key {commonObjDat.stackable(Key)}, lockpick {commonObjDat.stackable(Lockpick)}");
        c.Note(string.Join("; ", notes));
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        if (_after.Count != Cases.Length) return $"precondition: only {_after.Count} of {Cases.Length} drops ran";
        string together = c.Game == "uw1" ? "AddTogether: uw1 INVPANEL.C:697-721" : "AddTogether: uw2 INVPANEL.C:686-712";
        string average = c.Game == "uw1" ? "AddToOccupiedSlot: uw1 INVPANEL.C:750-764" : "AddToOccupiedSlot: uw2 INVPANEL.C:741-755";
        var k = new Checks();
        for (int i = 0; i < Cases.Length; i++)
        {
            var w = Cases[i];
            int stack = commonObjDat.stackable(w.InSlot.Item);
            bool merge = w.Bands && stack != 1 && stack != 3;
            var a = _after[i];
            k.True(a.merged == merge, $"{w.What}: expected {(merge ? "merged" : "kept apart")}, was {(a.merged ? "merged" : "kept apart")} ({together})");
            if (merge && a.merged)
            {
                int n = (w.InSlot.Quant ? w.InSlot.Count : 1) + (w.Dropped.Quant ? w.Dropped.Count : 1);
                k.Equal($"{w.What}: count of the stack", n, a.count, average);
                k.Equal($"{w.What}: quality of the stack", w.Quality, a.quality, average);
            }
        }
        return k.Result;
    }
}
