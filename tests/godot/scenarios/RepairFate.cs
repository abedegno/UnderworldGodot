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
/// Both games. The port once removed the item on every -2.
/// </summary>
public sealed class RepairFate : Scenario
{
    public override string Name => "repair-fate";
    public override string Cites => "underworld-exhumed uw1/src/event/WORLDEV.C:706-712, uw1/src/obj/OBJECTS.C:136-170; uw2/src/event/WORLDEV.C:1086-1092, uw2/src/obj/OBJECTS.C:130-164";

    string _problem;
    int _itemId = -1;
    bool _keptTenacious, _keptPlain;

    public override Task Setup(ScenarioContext c)
    {
        for (int id = 0; id < 16 && _itemId < 0; id++)
            if (commonObjDat.cullingpriority(id) < 10) _itemId = id;
        if (_itemId < 0) _problem = "no melee weapon of tenacity below 10";
        else c.Note($"weapon item {_itemId}, tenacity {commonObjDat.cullingpriority(_itemId)}");
        return Task.CompletedTask;
    }

    bool Repair(bool tenacious)
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
        c.Note($"destroyed by the repair: bit 13 set, kept {_keptTenacious}; clear, kept {_keptPlain}");
        await c.Frames(2);
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        var k = new Checks();
        string cite = c.Game == "uw1" ? "uw1 WORLDEV.C:706-712, OBJECTS.C:136-170" : "uw2 WORLDEV.C:1086-1092, OBJECTS.C:130-164";
        k.True(_keptTenacious, $"a tenacious item (bit 13 set) destroyed by a repair expected kept by its fate, got removed ({cite})");
        k.True(!_keptPlain, $"an item of tenacity below 10 destroyed by a repair expected removed, got kept ({cite})");
        return k.Result;
    }
}
