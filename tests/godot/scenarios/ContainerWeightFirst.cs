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
/// The weight is the object's and the container's contents' (ItemWeight and BagWeight, uw1 INVPANEL.C:602, 613; uw2 INVPANEL.C:588, 599;
/// BagWeight starts at the contents, uw1 inv/BAGS.C:514-529, uw2 inv/BAGS.C:511), not the container's own mass, and a capacity of 0 has
/// no limit (uw1 INVPANEL.C:610, 615; uw2 INVPANEL.C:596, 601). The quiver and the rune bag have capacity 0.
///
/// The scenario puts a container in the right shoulder and drops an object on it, as a click on the occupied slot
/// does (uimanager.UseObjectsTogether), three times: on a map case (which takes only scrolls and maps), the first
/// weapon heavier than the map case holds; on a quiver, the same weapon, which is refused for its kind and not its
/// weight; on a quiver, five arrows, which go in (the quiver takes sling stones, bolts and arrows, and in UW2 also
/// misc minor class 1 from class 8: uw1 INVPANEL.C:641-642, uw2 INVPANEL.C:630-631); and on an empty sack, a stack
/// that weighs exactly what the sack holds, which goes in.
///
/// The port tested the kind first (src/objects/container.cs:307-420), so it said "That item does not fit." to the
/// map case; it counted the container's own mass and took a capacity of 0 as no room at all.
/// </summary>
public sealed class ContainerWeightFirst : Scenario
{
    const int MapCase = 0x88;   // ITEM_MAP_CASE: uw1/src/include/items.h:281, uw2 items.h:274
    const int Quiver = 0x8D, Sack = 0x80, Arrow = 0x12;   // ITEM_ARROW_12: uw1 items.h:167, uw2 items.h:152   // ITEM_QUIVER, ITEM_SACK: uw1/src/include/items.h:286, 273; uw2 items.h:279, 266
    const int Slot = 5;

    public override string Name => "container-weight-first";
    public override string Cites => "underworld-exhumed uw1/src/inv/INVPANEL.C:598-628; uw2/src/inv/INVPANEL.C:584-617";

    string _problem, _text, _quiverText, _sackText;
    int _heavy = -1, _cap, _fits = -1, _fitsQty;
    string _arrowText;
    bool _stillHeld, _quiverHeld, _sackTook, _arrowsTaken;

    public override Task Setup(ScenarioContext c)
    {
        if (playerdat.GetInventorySlotListHead(Slot) != 0) { _problem = $"slot {Slot} is not empty at the start"; return Task.CompletedTask; }
        _cap = containerObjectDat.capacity(MapCase);
        if (_cap <= 0) { _problem = $"the map case has capacity {_cap} in this game's data"; return Task.CompletedTask; }
        for (int id = 0; id < 0x10 && _heavy < 0; id++)
            if (commonObjDat.mass(id) > _cap) _heavy = id;
        if (_heavy < 0) _problem = $"no weapon heavier than the map case's {_cap}";
        int sackCap = containerObjectDat.capacity(Sack), sackMass = commonObjDat.mass(Sack);
        if (containerObjectDat.capacity(Quiver) != 0) _problem = $"the quiver has capacity {containerObjectDat.capacity(Quiver)}, not 0";
        if (sackMass <= 0 || containerObjectDat.objectmask(Sack) != -1) _problem = $"the sack has mass {sackMass} and mask {containerObjectDat.objectmask(Sack)}";
        for (int id = 0; id < 0x80 && _fits < 0; id++)
        {
            int m = commonObjDat.mass(id), st = commonObjDat.stackable(id);
            if (m > 0 && sackCap % m == 0 && sackCap / m < 0x200 && st != 1 && st != 3) { _fits = id; _fitsQty = sackCap / m; }
        }
        if (_fits < 0) _problem = $"no stackable weapon or armour whose mass divides the sack's capacity {sackCap}";
        return Task.CompletedTask;
    }

    /// <summary>Drops item on an empty container of kind cont in the slot; gives the scroll text and whether the item stayed in the hand.</summary>
    async Task<(string text, bool held)> Drop(ScenarioContext c, int cont, int item, int qty = 1)
    {
        int world = ObjectCreator.SpawnObjectInHand(cont, changeInteractionmode: false);
        if (world == 0) { _problem = $"no container 0x{cont:X} could be made"; return ("", false); }
        var mc = UWTileMap.current_tilemap.LevelObjects[world];
        mc.link = 0; mc.is_quant = 0;
        int index = playerdat.AddObjectToPlayerInventory(world, false);
        playerdat.SetInventorySlotListHead(Slot, index);
        playerdat.ObjectInHand = -1;

        int hand = ObjectCreator.SpawnObjectInHand(item, changeInteractionmode: false);
        if (hand == 0) { _problem = $"no object 0x{item:X} could be made"; return ("", false); }
        var h = UWTileMap.current_tilemap.LevelObjects[hand];
        h.is_quant = 1; h.link = (short)qty; h.quality = 40;
        uimanager.UpdateInventoryDisplay();
        uimanager.instance.scroll.Clear();
        uimanager.CurrentSlot = Slot;
        uimanager.UseObjectsTogether(hand, index);
        var text = string.Join(" ", System.Linq.Enumerable.Select(uimanager.instance.scroll.Lines, l => l?.LineText ?? "")).Trim();
        bool held = playerdat.ObjectInHand == hand;

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
        return (text, held);
    }

    public override async Task Act(ScenarioContext c)
    {
        if (_problem != null) return;
        (_text, _stillHeld) = await Drop(c, MapCase, _heavy);
        if (_problem != null) return;
        (_quiverText, _quiverHeld) = await Drop(c, Quiver, _heavy);
        if (_problem != null) return;
        bool arrowsHeld;
        (_arrowText, arrowsHeld) = await Drop(c, Quiver, Arrow, 5);
        _arrowsTaken = !arrowsHeld;
        if (_problem != null) return;
        bool sackHeld;
        (_sackText, sackHeld) = await Drop(c, Sack, _fits, _fitsQty);
        _sackTook = !sackHeld;
        c.Note($"map case capacity {_cap}, dropped 0x{_heavy:X} of mass {commonObjDat.mass(_heavy)}: \"{_text}\", still in hand {_stillHeld}; "
            + $"on a quiver: \"{_quiverText}\", still in hand {_quiverHeld}; 5 arrows on a quiver: \"{_arrowText}\", taken {_arrowsTaken}; {_fitsQty} of 0x{_fits:X} of mass {commonObjDat.mass(_fits)} on a sack of mass {commonObjDat.mass(Sack)} "
            + $"and capacity {containerObjectDat.capacity(Sack)}: \"{_sackText}\", taken {_sackTook}");
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        string cite = c.Game == "uw1" ? "capacity before mask: uw1 INVPANEL.C:598-622" : "capacity before mask: uw2 INVPANEL.C:584-608";
        var k = new Checks();
        k.True(_stillHeld, "the heavy object did not stay in the hand");
        k.True(_text.Contains("is too full.") && !_text.Contains("does not fit"), $"a map case given an object too heavy for it: expected \"The map case is too full.\", got \"{_text}\" ({cite})");
        string noLimit = c.Game == "uw1" ? "a capacity of 0 has no limit: uw1 INVPANEL.C:610, 615" : "a capacity of 0 has no limit: uw2 INVPANEL.C:596, 601";
        string notFit = GameStrings.GetString(1, GameStrings.str_that_item_does_not_fit_).Trim();
        k.True(_quiverHeld, "the weapon dropped on the quiver did not stay in the hand");
        k.True(_quiverText.Contains(notFit) && !_quiverText.Contains("is too full"), $"a quiver given a weapon: expected \"{notFit}\", got \"{_quiverText}\" ({noLimit})");
        k.True(_arrowsTaken, $"a quiver given arrows: expected taken, got \"{_arrowText}\" ({(c.Game == "uw1" ? "uw1 INVPANEL.C:641-642" : "uw2 INVPANEL.C:630-631")})");
        k.True(_sackTook, $"a stack that weighs what the sack holds: expected taken, got \"{_sackText}\" (the contents' weight only: {(c.Game == "uw1" ? "uw1 INVPANEL.C:613, BAGS.C:514-529" : "uw2 INVPANEL.C:599, BAGS.C:511")})");
        return k.Result;
    }
}
