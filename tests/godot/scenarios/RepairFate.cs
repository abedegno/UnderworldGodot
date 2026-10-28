using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// A repair that destroys the item can still leave it. The original's repair_item, after
/// do_repair returns -2, removes the item only when Obj_Elem_Fate(10, obj) lets it go, and
/// otherwise turns the result into 0, "You cannot repair that." (uw1/src/event/WORLDEV.C:706-712,
/// uw2/src/event/WORLDEV.C:1086-1092). Obj_Elem_Fate keeps any object that is tenacious, its id
/// bit 13 (the door-direction bit) set or its tenacity above the range of 10 to 12, and with range
/// 10 lets every other object go (uw1/src/obj/OBJECTS.C:136-170, uw2/src/obj/OBJECTS.C:130-164).
///
/// The scenario puts a melee weapon of tenacity below 10 in the backpack and hands the port's
/// repair.DoRepair (the step after the anvil) a result of -2, once with the item's bit 13 set
/// and once clear. With the bit set the item must still be in its slot; clear, it must be gone.
///
/// A third case gives the item contents: is_quant clear and its link the inventory index of a
/// second, tenacious object (bit 13 set) in the backpack. Obj_Elem_Fate walks the contents with
/// Obj_Check (uw1 OBJECTS.C:116-127, uw2 OBJECTS.C:104-122) in the list the item lives in, so the
/// tenacious content keeps the item. The scenario picks an inventory index whose level object, and
/// everything a walk from it through the level's list would reach, is not tenacious, so a walk
/// through the level's list would let the item go.
///
/// A fourth case gives the item plain contents (a weapon of tenacity below 10, bit 13 clear) and
/// a tenacious object after it in its list (its next). Obj_Check starts at the contents, so the
/// next object is not looked at and the item goes.
///
/// Both games. The port once removed the item on every -2.
/// </summary>
public sealed class RepairFate : Scenario
{
    public override string Name => "repair-fate";
    public override string Cites => "underworld-exhumed uw1/src/event/WORLDEV.C:706-712, uw1/src/obj/OBJECTS.C:136-170; uw2/src/event/WORLDEV.C:1086-1092, uw2/src/obj/OBJECTS.C:130-164";

    string _problem;
    int _itemId = -1;
    bool _keptTenacious, _keptPlain, _keptByContent, _keptBySibling;

    public override Task Setup(ScenarioContext c)
    {
        for (int id = 0; id < 16 && _itemId < 0; id++)
            if (commonObjDat.cullingpriority(id) < 10) _itemId = id;
        if (_itemId < 0) _problem = "no melee weapon of tenacity below 10";
        else c.Note($"weapon item {_itemId}, tenacity {commonObjDat.cullingpriority(_itemId)}");
        return Task.CompletedTask;
    }

    /// <summary>chkTenacious at the highest range Obj_Elem_Fate(10) can draw, 12.</summary>
    static bool TenaciousAt12(uwObject o)
    {
        if (o.doordir == 1) return true;
        int extra = (o.is_quant == 1 && (o.link & 0x200) == 0) ? o.link - 1 : 0;
        return commonObjDat.cullingpriority(o.item_id) + extra / 2 > 12;
    }

    int NewInventoryObject()
    {
        int s = ObjectCreator.SpawnObjectInHand(_itemId);
        if (s == 0) return 0;
        int i = playerdat.AddObjectToPlayerInventory(s, false);
        playerdat.ObjectInHand = -1;
        uimanager.instance.mousecursor.SetCursorToCursor();
        return i;
    }

    bool Repair(bool tenacious, bool tenaciousContent = false, bool tenaciousSibling = false)
    {
        int slot = ObjectCreator.SpawnObjectInHand(_itemId);
        if (slot == 0) { _problem = "no weapon could be made"; return false; }
        int freeSlot = uimanager.FreePaperDollSlot;
        if (freeSlot == -1) { _problem = "no free backpack slot"; return false; }
        int index = playerdat.AddObjectToPlayerInventory(slot, false);
        playerdat.SetInventorySlotListHead(freeSlot, index);
        playerdat.ObjectInHand = -1;
        uimanager.instance.mousecursor.SetCursorToCursor();
        var item = playerdat.InventoryObjects[index];
        item.doordir = (short)(tenacious ? 1 : 0);
        if (tenaciousContent)
        {
            // the content: a second weapon in the backpack's object list, at an index whose level chain
            // (the object at that index in LevelObjects, its contents and the rest of its list) holds
            // nothing tenacious even at range 12, so a walk through LevelObjects would let the item go
            int contentIndex = 0;
            for (int tries = 0; tries < 16 && contentIndex == 0; tries++)
            {
                int s2 = ObjectCreator.SpawnObjectInHand(_itemId);
                if (s2 == 0) break;
                int ci = playerdat.AddObjectToPlayerInventory(s2, false);
                playerdat.ObjectInHand = -1;
                uimanager.instance.mousecursor.SetCursorToCursor();
                var level = UWTileMap.current_tilemap.LevelObjects[ci];
                if (level == null || !CallBacks.RunCodeOnObjectsInChain(TenaciousAt12, level, UWTileMap.current_tilemap.LevelObjects)) contentIndex = ci;
            }
            if (contentIndex == 0) { _problem = "no inventory index found whose level chain holds nothing tenacious"; return false; }
            var content = playerdat.InventoryObjects[contentIndex];
            content.doordir = 1;
            content.next = 0;
            item.is_quant = 0;
            item.link = (short)contentIndex;
        }
        if (tenaciousSibling)
        {
            int contentIndex = NewInventoryObject(), siblingIndex = NewInventoryObject();
            if (contentIndex == 0 || siblingIndex == 0) { _problem = "no content or sibling object could be made"; return false; }
            var content = playerdat.InventoryObjects[contentIndex];
            content.doordir = 0;
            content.next = 0;
            var sibling = playerdat.InventoryObjects[siblingIndex];
            sibling.doordir = 1;
            sibling.next = 0;
            item.is_quant = 0;
            item.link = (short)contentIndex;
            item.next = (short)siblingIndex;
        }

        var t = typeof(repair);
        BatchA.Set(t, "CallBackItemToRepair", item);
        BatchA.Set(t, "CallBackRepairResult", -2);
        BatchA.Set(t, "CallBackRepairTime", 0);
        BatchA.Set(t, "CallBackWorldObject", false);
        BatchA.Call(t, "DoRepair");
        bool kept = playerdat.GetInventorySlotListHead(freeSlot) == index;
        if (kept) { item.doordir = 0; }
        return kept;
    }

    public override async Task Act(ScenarioContext c)
    {
        if (_problem != null) return;
        _keptTenacious = Repair(true);
        if (_problem != null) return;
        await c.Frames(2);
        _keptPlain = Repair(false);
        if (_problem != null) return;
        await c.Frames(2);
        _keptByContent = Repair(false, true);
        if (_problem != null) return;
        await c.Frames(2);
        _keptBySibling = Repair(false, false, true);
        c.Note($"destroyed by the repair: bit 13 set, kept {_keptTenacious}; clear, kept {_keptPlain}; clear with tenacious inventory contents, kept {_keptByContent}; clear with plain contents and a tenacious next, kept {_keptBySibling}");
        await c.Frames(2);
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        var k = new Checks();
        string cite = c.Game == "uw1" ? "uw1 WORLDEV.C:706-712, OBJECTS.C:136-170" : "uw2 WORLDEV.C:1086-1092, OBJECTS.C:130-164";
        k.True(_keptTenacious, $"a tenacious item (bit 13 set) destroyed by a repair expected kept by its fate, got removed ({cite})");
        k.True(!_keptPlain, $"an item of tenacity below 10 destroyed by a repair expected removed, got kept ({cite})");
        k.True(_keptByContent, $"an item holding a tenacious inventory object (its link an inventory index) destroyed by a repair expected kept by its fate, got removed ({cite}, Obj_Check {(c.Game == "uw1" ? "uw1 OBJECTS.C:116-127" : "uw2 OBJECTS.C:104-122")})");
        k.True(!_keptBySibling, $"an item with plain contents and a tenacious object after it in its list, destroyed by a repair, expected removed (Obj_Check starts at the contents), got kept ({cite}, {(c.Game == "uw1" ? "uw1 OBJECTS.C:116-127, 163-166" : "uw2 OBJECTS.C:104-122, 157-160")})");
        return k.Result;
    }
}
