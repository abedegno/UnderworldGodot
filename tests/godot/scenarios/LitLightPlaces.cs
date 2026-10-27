using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// Where a burning light may be (UW1 and UW2). The original's ItemFitsSlot tests a lit light (misc minor 1,
/// class 4 to 7) as its unlit form and keeps it lit only in a shoulder or hand slot; put anywhere else it
/// goes out, its class lowered by 4 (uw1/src/inv/INVPANEL.C:586-594, ValidLightSlots at INVPANEL.C:72;
/// uw2/src/inv/INVPANEL.C:572-582, INVPANEL.C:48). A light used where it lies in the world is not lit:
/// UseLight answers "Lights may only be used if equipped." (uw1/src/obj/USEITEMS.C:518-521, string 0x7B;
/// uw2/src/obj/USEITEMS.C:615-619, string 0x89). The carried light is the brightest lit light in the four light
/// slots or on the cursor (uw1/src/game/PLAYDATA.C:426-440; uw2/src/game/PLAYDATA.C:389-399), so one held on the
/// cursor lights the way.
///
/// Observed in DOS by Underworld Revisited (PQMarine, MIT): Assets/UWDataImport/UWData/UWInventoryModel.cs:530-534, 552-562,
/// Assets/UWDataImport/UWData/UWWorldUseRules.cs:61.
///
/// The scenario drops a lit torch from the hand into the first empty backpack slot and another into the left
/// shoulder (slot 6), as a click on an empty slot does (uimanager.PickupToEmptySlot), and uses an unlit torch
/// lying on the player's square as a click in use mode does (use.Use from the world). Then, with the light slots
/// empty, it holds a lit torch on the cursor and asks the port for the carried light (playerdat.BrightestNonMagicalLight).
/// </summary>
public sealed class LitLightPlaces : Scenario
{
    const int Torch = 0x91, LitTorch = 0x95;   // ITEM_TORCH, ITEM_LIT_TORCH: uw1/src/include/items.h:293, 297; uw2 items.h:285, 289
    const int Shoulder = 6;

    public override string Name => "lit-light-places";
    public override string Cites => "underworld-exhumed uw1/src/inv/INVPANEL.C:586-594, uw1/src/obj/USEITEMS.C:518-521; uw2/src/inv/INVPANEL.C:572-582, uw2/src/obj/USEITEMS.C:615-619";

    string _problem, _worldText;
    int _pack = -1, _packItem, _shoulderItem, _cursorLight = -1, _torchLight, _bareLight;

    static int DropLitTorch(int slot)
    {
        int hand = ObjectCreator.SpawnObjectInHand(LitTorch, changeInteractionmode: false);
        if (hand == 0) return 0;
        var o = UWTileMap.current_tilemap.LevelObjects[hand];
        o.quality = 40; o.is_quant = 1; o.link = 1;
        uimanager.CurrentSlot = slot;
        uimanager.PickupToEmptySlot(hand);
        int head = playerdat.GetInventorySlotListHead(slot);
        return head == 0 ? -1 : playerdat.InventoryObjects[head].item_id;
    }

    static void Empty(int slot)
    {
        int head = playerdat.GetInventorySlotListHead(slot);
        if (head != 0) { uimanager.CurrentSlot = slot; playerdat.RemoveFromInventory(head); }
        if (playerdat.ObjectInHand > 0)
        {
            ObjectFreeLists.ReleaseFreeObject(UWTileMap.current_tilemap.LevelObjects[playerdat.ObjectInHand]);
            playerdat.ObjectInHand = -1;
            uimanager.instance.mousecursor.SetCursorToCursor();
        }
    }

    public override Task Setup(ScenarioContext c)
    {
        for (int s = 11; s <= 18 && _pack < 0; s++)
            if (playerdat.GetInventorySlotListHead(s) == 0) _pack = s;
        if (_pack < 0) _problem = "no empty backpack slot";
        else if (playerdat.GetInventorySlotListHead(Shoulder) != 0) _problem = $"slot {Shoulder} is not empty at the start";
        else if (uimanager.OpenedContainerIndex >= 0) _problem = "a container is open, so the backpack slots show its contents";
        return Task.CompletedTask;
    }

    public override async Task Act(ScenarioContext c)
    {
        if (_problem != null) return;
        uimanager.UpdateInventoryDisplay();
        _packItem = DropLitTorch(_pack);
        Empty(_pack);
        _shoulderItem = DropLitTorch(Shoulder);
        Empty(Shoulder);
        playerdat.PlayerStatusUpdate();

        var p = playerdat.playerObject;
        var t = ObjectCreator.spawnObjectInTile(Torch, p.tileX, p.tileY, 3, 3, p.zpos, RenderImmediately: false);
        if (t == null) { _problem = "no torch could be made in the world"; return; }
        t.quality = 40;
        uimanager.instance.scroll.Clear();
        use.Use(t, playerdat.playerObject, UWTileMap.current_tilemap.LevelObjects, WorldObject: true);
        _worldText = string.Join(" ", System.Linq.Enumerable.Select(uimanager.instance.scroll.Lines, l => l?.LineText ?? "")).Trim();
        var tile = UWTileMap.current_tilemap.Tiles[p.tileX, p.tileY];
        if (tile.indexObjectList == t.index) tile.indexObjectList = t.next;
        ObjectFreeLists.ReleaseFreeObject(t);

        bool slotsEmpty = true;
        for (int s = 5; s <= 8; s++) if (playerdat.GetInventorySlotListHead(s) != 0) slotsEmpty = false;
        _bareLight = playerdat.BrightestNonMagicalLight();   // UW2 adds the level's own light (set_light takes the larger)
        int held = ObjectCreator.SpawnObjectInHand(LitTorch, changeInteractionmode: false);
        if (held != 0 && slotsEmpty)
        {
            UWTileMap.current_tilemap.LevelObjects[held].quality = 40;
            _cursorLight = playerdat.BrightestNonMagicalLight();
        }
        _torchLight = lightsourceObjectDat.brightness(LitTorch);
        Empty(_pack);
        c.Note($"backpack slot {_pack}: 0x{_packItem:X}; shoulder: 0x{_shoulderItem:X}; used in the world: {_worldText}; carried light with a lit torch on the cursor {_cursorLight}, without it {_bareLight} (the torch gives {_torchLight})");
        await c.Frames(1);
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        if (_packItem <= 0) return $"precondition: the lit torch did not go into backpack slot {_pack}";
        if (_shoulderItem <= 0) return "precondition: the lit torch did not go into the shoulder";
        bool uw1 = c.Game == "uw1";
        var k = new Checks();
        k.Equal("a lit torch put in a backpack slot", Torch, _packItem, uw1 ? "put out outside the light slots: uw1 INVPANEL.C:586-594" : "put out outside the light slots: uw2 INVPANEL.C:572-582");
        k.Equal("a lit torch put in a shoulder slot", LitTorch, _shoulderItem, uw1 ? "kept lit in a light slot: uw1 INVPANEL.C:587-589" : "kept lit in a light slot: uw2 INVPANEL.C:579-581");
        string equipped = GameStrings.GetString(1, GameStrings.str_lights_may_only_be_used_if_equipped_).Trim();
        k.True(_worldText.Contains(equipped), $"a torch used in the world: expected \"{equipped}\", got \"{_worldText}\" ({(uw1 ? "uw1 USEITEMS.C:518-521" : "uw2 USEITEMS.C:615-619")})");
        if (_cursorLight < 0) k.True(false, "precondition: the light slots were not empty, or no torch could be held, for the cursor check");
        else k.Equal("the carried light with a lit torch on the cursor", System.Math.Max(_torchLight, _bareLight), _cursorLight, uw1 ? "the cursor counts: uw1 PLAYDATA.C:429-431" : "the cursor counts: uw2 PLAYDATA.C:389-391");
        return k.Result;
    }
}
