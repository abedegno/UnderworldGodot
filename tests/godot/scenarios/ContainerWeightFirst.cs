using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// Which refusal a container gives first (UW1 and UW2). The original's ItemFitsSlot weighs the object against
/// the container's capacity, and every open bag around it, before it looks at what kind of object the
/// container takes: too heavy is "The &lt;container&gt; is too full." whatever the object is, and only an object
/// that fits by weight can get "That item does not fit." (uw1/src/inv/INVPANEL.C:598-628; uw2/src/inv/INVPANEL.C:584-617).
///
/// Observed in DOS by Underworld Revisited (PQMarine, MIT): Assets/UWDataImport/UWData/UWContainerCapacity.cs:55-56,
/// Assets/UWDataImport/UWData/UWItemMessages.cs:35.
///
/// The scenario puts a map case (which takes only scrolls and maps) in the right shoulder and drops on it, as a
/// click on the occupied slot does (uimanager.UseObjectsTogether), the first weapon or armour heavier than the map
/// case holds.
///
/// The port tested the kind first (src/objects/container.cs:307-420), so it said "That item does not fit.".
/// </summary>
public sealed class ContainerWeightFirst : Scenario
{
    const int MapCase = 0x88;   // ITEM_MAP_CASE: uw1/src/include/items.h:281, uw2 items.h:274
    const int Slot = 5;

    public override string Name => "container-weight-first";
    public override string Cites => "underworld-exhumed uw1/src/inv/INVPANEL.C:598-628; uw2/src/inv/INVPANEL.C:584-617";

    string _problem, _text;
    int _heavy = -1, _cap;
    bool _stillHeld;

    public override Task Setup(ScenarioContext c)
    {
        if (playerdat.GetInventorySlotListHead(Slot) != 0) { _problem = $"slot {Slot} is not empty at the start"; return Task.CompletedTask; }
        _cap = containerObjectDat.capacity(MapCase);
        if (_cap <= 0) { _problem = $"the map case has capacity {_cap} in this game's data"; return Task.CompletedTask; }
        for (int id = 0; id < 0x80 && _heavy < 0; id++)
            if (commonObjDat.mass(id) > _cap) _heavy = id;
        if (_heavy < 0) _problem = $"no weapon or armour heavier than the map case's {_cap}";
        return Task.CompletedTask;
    }

    public override async Task Act(ScenarioContext c)
    {
        if (_problem != null) return;
        int world = ObjectCreator.SpawnObjectInHand(MapCase, changeInteractionmode: false);
        if (world == 0) { _problem = "no map case could be made"; return; }
        var mc = UWTileMap.current_tilemap.LevelObjects[world];
        mc.link = 0; mc.is_quant = 0;
        int index = playerdat.AddObjectToPlayerInventory(world, false);
        playerdat.SetInventorySlotListHead(Slot, index);
        playerdat.ObjectInHand = -1;

        int hand = ObjectCreator.SpawnObjectInHand(_heavy, changeInteractionmode: false);
        if (hand == 0) { _problem = "no heavy object could be made"; return; }
        var h = UWTileMap.current_tilemap.LevelObjects[hand];
        h.is_quant = 1; h.link = 1; h.quality = 40;
        uimanager.UpdateInventoryDisplay();
        uimanager.instance.scroll.Clear();
        uimanager.CurrentSlot = Slot;
        uimanager.UseObjectsTogether(hand, index);
        _text = string.Join(" ", System.Linq.Enumerable.Select(uimanager.instance.scroll.Lines, l => l?.LineText ?? "")).Trim();
        _stillHeld = playerdat.ObjectInHand == hand;
        c.Note($"map case capacity {_cap}, dropped 0x{_heavy:X} of mass {commonObjDat.mass(_heavy)}: \"{_text}\", still in hand {_stillHeld}");

        if (playerdat.ObjectInHand > 0)
        {
            ObjectFreeLists.ReleaseFreeObject(UWTileMap.current_tilemap.LevelObjects[playerdat.ObjectInHand]);
            playerdat.ObjectInHand = -1;
            uimanager.instance.mousecursor.SetCursorToCursor();
        }
        uimanager.CurrentSlot = Slot;
        playerdat.RemoveFromInventory(playerdat.GetInventorySlotListHead(Slot));
        playerdat.PlayerStatusUpdate();
        await c.Frames(1);
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        string cite = c.Game == "uw1" ? "capacity before mask: uw1 INVPANEL.C:598-622" : "capacity before mask: uw2 INVPANEL.C:584-608";
        var k = new Checks();
        k.True(_stillHeld, "the heavy object did not stay in the hand");
        k.True(_text.Contains("is too full") && !_text.Contains("does not fit"), $"a map case given an object too heavy for it: expected \"The map case is too full.\", got \"{_text}\" ({cite})");
        return k.Result;
    }
}
