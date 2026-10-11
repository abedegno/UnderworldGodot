using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// Using an unlit torch already in a light slot lights it unless it is used up, and what counts as
/// used up differs between the games.
///
/// The original's UseLight refuses a light whose quality is 0 in UW1, with message 0x7C (uw1
/// src/obj/USEITEMS.C:522-525), and one whose quality is 1 or less in UW2, with message 0x8A
/// "That light is already used up." (uw2 USEITEMS.C:620-624). Otherwise the light's inclass goes
/// up by 4, the lit item (uw1 USEITEMS.C:555-558, uw2 USEITEMS.C:659-664).
///
/// The quality check comes before the light is moved to a light slot (uw1 USEITEMS.C:526-551, uw2
/// USEITEMS.C:625-657) and before the toggle in either direction, so a used-up light that is lit is
/// not put out either.
///
/// The scenario puts an unlit torch of quality 0, 1 and 2 in turn in the right shoulder (slot 5, a
/// light slot, so no move happens) and uses it as a click on that slot does, then reads the torch's
/// item and the message scroll, and takes the torch away again. It then uses an unlit torch at the
/// game's refusing quality from the backpack (it must stay there, unlit, with slot 5 still empty),
/// and a lit torch in slot 5 at the refusing quality (it must stay lit) and at quality 2 (it goes
/// out).
/// </summary>
public sealed class LightOnQuality : Scenario
{
    const int Torch = 0x91, LitTorch = 0x95;   // ITEM_TORCH, ITEM_LIT_TORCH: uw1/src/include/items.h:293, 297; uw2 items.h:285, 289
    const int Slot = 5;
    static readonly int[] Qualities = { 0, 1, 2 };

    public override string Name => "light-on-quality";
    public override string Cites => "underworld-exhumed uw1/src/obj/USEITEMS.C:522-525, 555-558 (UseLight); uw2/src/obj/USEITEMS.C:620-624, 659-664";

    string _problem;
    readonly int[] _item = new int[3];
    readonly bool[] _refusedMessage = new bool[3];
    string _refusal;
    int _refuseQ, _bpSlot, _bpSlotAfter = -1, _bpItem, _slot5After;
    bool _bpMessage, _litMessage;
    int _litRefusedItem, _litOkItem;

    public override Task Setup(ScenarioContext c)
    {
        if (playerdat.GetInventorySlotListHead(Slot) != 0) _problem = $"slot {Slot} is not empty at the start";
        return Task.CompletedTask;
    }

    public override async Task Act(ScenarioContext c)
    {
        if (_problem != null) return;
        _refusal = GameStrings.GetString(1, c.Game == "uw1" ? 0x7C : 0x8A).Trim();
        for (int i = 0; i < Qualities.Length; i++)
        {
            int world = ObjectCreator.SpawnObjectInHand(Torch);
            if (world == 0) { _problem = "could not make a torch"; return; }
            UWTileMap.current_tilemap.LevelObjects[world].quality = (short)Qualities[i];
            int index = playerdat.AddObjectToPlayerInventory(world, false);
            playerdat.SetInventorySlotListHead(Slot, index);
            playerdat.ObjectInHand = -1;
            uimanager.instance.mousecursor.SetCursorToCursor();
            uimanager.UpdateInventoryDisplay();
            uimanager.instance.scroll.Clear();

            uimanager.CurrentSlot = Slot;   // as the click on the slot sets it
            use.Use(playerdat.InventoryObjects[index], playerdat.playerObject, playerdat.InventoryObjects, WorldObject: false);
            await c.Frames(3);
            _item[i] = playerdat.InventoryObjects[index]?.item_id ?? -1;
            var text = string.Join(" ", System.Array.ConvertAll(uimanager.instance.scroll.Lines, l => l.LineText));
            _refusedMessage[i] = text.Contains(_refusal);
            c.Note($"quality {Qualities[i]}: item now 0x{_item[i]:X}, refusal shown {_refusedMessage[i]}");

            playerdat.RemoveFromInventory(index);
            playerdat.PlayerStatusUpdate();
            await c.Frames(1);
        }

        _refuseQ = c.Game == "uw1" ? 0 : 1;

        // A used-up torch in the backpack: refused before it is moved.
        var (bpIndex, bpSlot) = Guards.PutInBackpack(Torch, o => o.quality = (short)_refuseQ);
        if (bpSlot == -1) { _problem = "could not put a torch in the backpack"; return; }
        _bpSlot = bpSlot;
        uimanager.instance.scroll.Clear();
        uimanager.CurrentSlot = bpSlot;
        use.Use(playerdat.InventoryObjects[bpIndex], playerdat.playerObject, playerdat.InventoryObjects, WorldObject: false);
        await c.Frames(3);
        var bp = playerdat.InventoryObjects[bpIndex];
        _bpItem = bp?.item_id ?? -1;
        _bpMessage = ScrollHas(_refusal);
        _slot5After = playerdat.GetInventorySlotListHead(Slot);
        _bpSlotAfter = playerdat.GetInventorySlotListHead(bpSlot) == bpIndex ? bpSlot : -1;
        c.Note($"backpack torch at quality {_refuseQ}: item 0x{_bpItem:X}, backpack slot {bpSlot} -> {_bpSlotAfter}, slot {Slot} head {_slot5After}, refusal shown {_bpMessage}");
        if (_slot5After != 0)
        {
            uimanager.CurrentSlot = Slot;
            playerdat.RemoveFromInventory(_slot5After);
        }
        else
        {
            uimanager.CurrentSlot = bpSlot;
            playerdat.RemoveFromInventory(bpIndex);
        }
        playerdat.ObjectInHand = -1;
        uimanager.instance.mousecursor.SetCursorToCursor();
        await c.Frames(1);

        // A lit torch in slot 5: at the refusing quality it stays lit, at 2 it goes out.
        (_litRefusedItem, _litMessage) = await UseInSlot(c, LitTorch, _refuseQ);
        (_litOkItem, _) = await UseInSlot(c, LitTorch, 2);
        c.Note($"lit torch at quality {_refuseQ}: item 0x{_litRefusedItem:X}, refusal shown {_litMessage}; at quality 2: item 0x{_litOkItem:X}");
    }

    async Task<(int item, bool refused)> UseInSlot(ScenarioContext c, int item, int quality)
    {
        int world = ObjectCreator.SpawnObjectInHand(item);
        if (world == 0) { _problem = "could not make a torch"; return (-1, false); }
        UWTileMap.current_tilemap.LevelObjects[world].quality = (short)quality;
        int index = playerdat.AddObjectToPlayerInventory(world, false);
        playerdat.SetInventorySlotListHead(Slot, index);
        playerdat.ObjectInHand = -1;
        uimanager.instance.mousecursor.SetCursorToCursor();
        uimanager.UpdateInventoryDisplay();
        uimanager.instance.scroll.Clear();
        uimanager.CurrentSlot = Slot;
        use.Use(playerdat.InventoryObjects[index], playerdat.playerObject, playerdat.InventoryObjects, WorldObject: false);
        await c.Frames(3);
        int after = playerdat.InventoryObjects[index]?.item_id ?? -1;
        bool msg = ScrollHas(_refusal);
        playerdat.RemoveFromInventory(index);
        playerdat.PlayerStatusUpdate();
        await c.Frames(1);
        return (after, msg);
    }

    static bool ScrollHas(string s) =>
        string.Join(" ", System.Array.ConvertAll(uimanager.instance.scroll.Lines, l => l.LineText)).Contains(s);

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        bool uw1 = c.Game == "uw1";
        var k = new Checks();
        for (int i = 0; i < Qualities.Length; i++)
        {
            int q = Qualities[i];
            bool refused = uw1 ? q == 0 : q <= 1;
            string why = uw1 ? "refused only at quality 0: uw1 USEITEMS.C:522-525" : "refused at quality 1 or less: uw2 USEITEMS.C:620-624";
            k.Equal($"torch item after use at quality {q}", refused ? Torch : LitTorch, _item[i], why);
            k.True(refused == _refusedMessage[i], $"used-up message at quality {q} expected {(refused ? "shown" : "not shown")}, got {(_refusedMessage[i] ? "shown" : "not shown")} ({(uw1 ? "message 0x7C: uw1 USEITEMS.C:523" : "message 0x8A: uw2 USEITEMS.C:622")})");
        }
        string refuseCite = uw1 ? "uw1 USEITEMS.C:522-525, before the move at 526-551" : "uw2 USEITEMS.C:620-624, before the move at 625-657";
        k.Equal($"slot {Slot} after using a quality {_refuseQ} torch from the backpack", 0, _slot5After, $"refused before it is moved: {refuseCite}");
        k.Equal($"backpack slot of the quality {_refuseQ} torch after use", _bpSlot, _bpSlotAfter, $"refused before it is moved: {refuseCite}");
        k.Equal($"backpack torch's item at quality {_refuseQ}", Torch, _bpItem, refuseCite);
        k.True(_bpMessage, $"used-up message expected for the quality {_refuseQ} torch used from the backpack ({refuseCite})");
        string offCite = uw1 ? "the check comes before the toggle either way: uw1 USEITEMS.C:522-525, 555-558" : "the check comes before the toggle either way: uw2 USEITEMS.C:620-624, 659-664";
        k.Equal($"lit torch's item after use at quality {_refuseQ}", LitTorch, _litRefusedItem, offCite);
        k.True(_litMessage, $"used-up message expected for the lit quality {_refuseQ} torch ({offCite})");
        k.Equal("lit torch's item after use at quality 2", Torch, _litOkItem, "inclass - 4: uw1 USEITEMS.C:555-556, uw2 USEITEMS.C:659-660");
        return k.Result;
    }
}
