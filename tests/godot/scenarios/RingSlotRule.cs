using System.IO;
using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// What may go in the armour and ring slots of the paper doll. The original's ItemFitsSlot takes
/// into slots 0 to 4 and the ring slots 9 and 10 only armour (hack major, minor 2 or 3) whose
/// OBJECTS.DAT category matches the slot: helm 8, body armour 1, gloves 4, leggings 3, boots 5,
/// rings 9 (uw1/src/inv/INVPANEL.C:554-582, uw2/src/inv/INVPANEL.C:540-568). The category is
/// byte 3 of the item's Armor record, the 32 records of 4 bytes that hack_init reads after the
/// 2-byte header, 16 weapons of 8 bytes and 16 missiles of 3 (uw1 and uw2 OBJCLASS.C, HACK.C:24-26),
/// so at offset 0xB2 + (item - 0x20) * 4 + 3.
///
/// The scenario reads that table from the game's OBJECTS.DAT itself, then sets one object to
/// every item id in turn and asks the port's uimanager.ValidObjectForSlot about each of those
/// seven slots, comparing every answer with the original's rule.
///
/// The port once used fixed item ranges for the ring slots (src/ui/uimanager_inventory.cs:971-978):
/// it refused the gold ring in both games and UW2's strange artifact, and took the tower shield (0x3B).
/// </summary>
public sealed class RingSlotRule : Scenario
{
    static readonly int[] Slots = { 0, 1, 2, 3, 4, 9, 10 };
    static readonly int[] Category = { 8, 1, 4, 3, 5, 0, 0, 0, 0, 9, 9 };   // by slot; ARMOUR_* in uw1/src/include/object.h:432-438

    public override string Name => "ring-slot-rule";
    public override string Cites => "underworld-exhumed uw1/src/inv/INVPANEL.C:554-582, uw2/src/inv/INVPANEL.C:540-568 (ItemFitsSlot)";

    string _problem;
    byte[] _dat;
    uwObject _obj;
    int _restoreId;
    readonly bool[,] _got = new bool[0x200, 11];

    public override Task Setup(ScenarioContext c)
    {
        _dat = File.ReadAllBytes(Path.Combine(UWClass.BasePath, "DATA", "OBJECTS.DAT"));
        int slot = ObjectCreator.PrepareNewObject(0x20, ObjectFreeLists.ObjectListType.StaticList);
        if (slot == 0) { _problem = "no object could be made"; return Task.CompletedTask; }
        _obj = UWTileMap.current_tilemap.LevelObjects[slot];
        _restoreId = _obj.item_id;
        return Task.CompletedTask;
    }

    public override Task Act(ScenarioContext c)
    {
        if (_problem != null) return Task.CompletedTask;
        for (int id = 0; id < 0x200; id++)
        {
            _obj.item_id = id;
            foreach (int s in Slots) _got[id, s] = uimanager.ValidObjectForSlot(s, _obj);
        }
        _obj.item_id = _restoreId;
        ObjectFreeLists.ReleaseFreeObject(_obj);
        return Task.CompletedTask;
    }

    bool Fits(int id, int slot)
    {
        if ((id >> 6) != 0 || ((id >> 4) & 3) < 2) return false;   // INVPANEL.C: major != MAJOR_HACK || minor < 2
        return _dat[0xB2 + (id - 0x20) * 4 + 3] == Category[slot];
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        bool uw2 = c.Game == "uw2";
        string cite = uw2 ? "uw2 INVPANEL.C:540-568" : "uw1 INVPANEL.C:554-582";
        int rings = 0;
        for (int id = 0; id < 0x200; id++)
            if (Fits(id, 9)) rings++;
        if (rings < 4) return $"precondition: only {rings} items of ring category 9 in OBJECTS.DAT";
        var k = new Checks();
        for (int id = 0; id < 0x200; id++)
            foreach (int s in Slots)
                k.True(_got[id, s] == Fits(id, s), $"item 0x{id:X} in slot {s} expected {(Fits(id, s) ? "allowed" : "refused")}, port {(_got[id, s] ? "allowed" : "refused")} ({cite})");
        return k.Result;
    }
}
