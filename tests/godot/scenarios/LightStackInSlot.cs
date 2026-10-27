using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// Using a stack of unlit lights that sits in a light slot.
///
/// The original's UseLight only lights a light where it is when it is in one of the four light slots
/// AND is a single item: the loop over ValidLightSlots breaks only on `qty == 1` (uw1
/// src/obj/USEITEMS.C:526-530, uw2 USEITEMS.C:625-631). A stack in a light slot therefore takes the
/// move path: the first free light slot from 5 to 8 is chosen (the stack's own slot is not free, and
/// the `inslot == obj && qty == 1` case does not apply), InvRemoveOneObject splits one light off the
/// stack, leaving the rest in the slot (uw1 INVDATA.C:243-246 and takeFromSlot), and that one light is
/// put in the free slot and lit (uw1 USEITEMS.C:532-558, uw2 USEITEMS.C:633-664). With no free light
/// slot it is refused (message 0xF6 in UW1, 0x105 "Your hands are full." in UW2: uw1
/// USEITEMS.C:544-547, uw2 USEITEMS.C:648-651) and nothing changes.
///
/// The scenario puts a stack of 3 unlit torches in the right shoulder (slot 5) and uses it as a click
/// on that slot does: slot 5 must keep 2 unlit torches and slot 6 must get 1 lit torch. It then fills
/// slots 6 to 8 with single unlit torches and uses the stack of 3 in slot 5 again: all four slots must
/// be unchanged, the stack still 3 unlit torches. (The hands-full message is not checked here.)
///
/// The port lit the whole stack in place (src/objects/light.cs:9-17).
/// </summary>
public sealed class LightStackInSlot : Scenario
{
    const int Torch = 0x91, LitTorch = 0x95;   // ITEM_TORCH, ITEM_LIT_TORCH: uw1/src/include/items.h:293, 297; uw2 items.h:285, 289
    const int Slot = 5;

    public override string Name => "light-stack-in-slot";
    public override string Cites => "underworld-exhumed uw1/src/obj/USEITEMS.C:526-558 (UseLight); uw2/src/obj/USEITEMS.C:625-664";

    string _problem;
    readonly (int item, int qty)[] _free = new (int, int)[4];
    readonly (int item, int qty)[] _full = new (int, int)[4];

    public override Task Setup(ScenarioContext c)
    {
        for (int s = 5; s <= 8; s++)
            if (playerdat.GetInventorySlotListHead(s) != 0) { _problem = $"light slot {s} is not empty at the start"; break; }
        return Task.CompletedTask;
    }

    static int PutInSlot(int slot, int quantity)
    {
        int world = ObjectCreator.SpawnObjectInHand(Torch);
        if (world == 0) return 0;
        var o = UWTileMap.current_tilemap.LevelObjects[world];
        o.quality = 10;
        if (quantity > 1) { o.is_quant = 1; o.link = (short)quantity; }
        int index = playerdat.AddObjectToPlayerInventory(world, false);
        playerdat.SetInventorySlotListHead(slot, index);
        playerdat.ObjectInHand = -1;
        uimanager.instance.mousecursor.SetCursorToCursor();
        return index;
    }

    static (int item, int qty) InSlot(int slot)
    {
        int head = playerdat.GetInventorySlotListHead(slot);
        var o = head == 0 ? null : playerdat.InventoryObjects[head];
        return o == null ? (0, 0) : (o.item_id, o.ObjectQuantity);
    }

    static async Task UseStack(ScenarioContext c, int index)
    {
        uimanager.UpdateInventoryDisplay();
        uimanager.instance.scroll.Clear();
        uimanager.CurrentSlot = Slot;   // as the click on the slot sets it
        use.Use(playerdat.InventoryObjects[index], playerdat.playerObject, playerdat.InventoryObjects, WorldObject: false);
        await c.Frames(3);
    }

    static async Task ClearSlots(ScenarioContext c)
    {
        for (int s = 5; s <= 8; s++)
        {
            int head = playerdat.GetInventorySlotListHead(s);
            if (head == 0) continue;
            uimanager.CurrentSlot = s;
            playerdat.RemoveFromInventory(head);
        }
        if (playerdat.ObjectInHand > 0) { playerdat.ObjectInHand = -1; uimanager.instance.mousecursor.SetCursorToCursor(); }
        playerdat.PlayerStatusUpdate();
        await c.Frames(1);
    }

    public override async Task Act(ScenarioContext c)
    {
        if (_problem != null) return;

        // A free light slot: one torch is split off and lit there.
        int stack = PutInSlot(Slot, 3);
        if (stack == 0) { _problem = "could not make a stack of torches"; return; }
        if (InSlot(Slot) != (Torch, 3)) { _problem = $"slot {Slot} does not hold 3 unlit torches before the use"; return; }
        await UseStack(c, stack);
        for (int s = 5; s <= 8; s++) _free[s - 5] = InSlot(s);
        c.Note($"free slots: slots 5..8 = {string.Join(", ", System.Array.ConvertAll(_free, x => $"0x{x.item:X} x{x.qty}"))}");
        await ClearSlots(c);

        // No free light slot: refused, nothing changes.
        stack = PutInSlot(Slot, 3);
        if (stack == 0) { _problem = "could not make a stack of torches"; return; }
        for (int s = 6; s <= 8; s++)
            if (PutInSlot(s, 1) == 0) { _problem = $"could not make a torch for slot {s}"; return; }
        await UseStack(c, stack);
        for (int s = 5; s <= 8; s++) _full[s - 5] = InSlot(s);
        c.Note($"hands full: slots 5..8 = {string.Join(", ", System.Array.ConvertAll(_full, x => $"0x{x.item:X} x{x.qty}"))}");
        await ClearSlots(c);
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        bool uw1 = c.Game == "uw1";
        var k = new Checks();
        string move = uw1 ? "a stack is not lit in place, one is split off to a free light slot and lit: uw1 USEITEMS.C:526-558"
                          : "a stack is not lit in place, one is split off to a free light slot and lit: uw2 USEITEMS.C:625-664";
        k.Equal($"slot {Slot} item after using the stack", Torch, _free[0].item, move);
        k.Equal($"slot {Slot} quantity after using the stack", 2, _free[0].qty, move);
        k.Equal("slot 6 item after using the stack", LitTorch, _free[1].item, move);
        k.Equal("slot 6 quantity after using the stack", 1, _free[1].qty, move);
        k.Equal("slot 7 item after using the stack", 0, _free[2].item, move);
        k.Equal("slot 8 item after using the stack", 0, _free[3].item, move);
        string full = uw1 ? "refused with no free light slot: uw1 USEITEMS.C:544-547" : "refused with no free light slot: uw2 USEITEMS.C:648-651";
        k.Equal($"slot {Slot} item after using the stack with the hands full", Torch, _full[0].item, full);
        k.Equal($"slot {Slot} quantity after using the stack with the hands full", 3, _full[0].qty, full);
        for (int s = 6; s <= 8; s++)
        {
            k.Equal($"slot {s} item with the hands full", Torch, _full[s - 5].item, full);
            k.Equal($"slot {s} quantity with the hands full", 1, _full[s - 5].qty, full);
        }
        return k.Result;
    }
}
