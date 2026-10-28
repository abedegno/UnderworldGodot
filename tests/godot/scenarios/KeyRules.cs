using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// Using a key on a lock (UW1 and UW2). The original's UseKeyOn calls checkLock with the key's owner field, the
/// number of the lock it fits, and prints string result + 2 (uw1/src/obj/USEITEMS.C:251-263,
/// uw2/src/obj/USEITEMS.C:561-575). checkLock (uw1 USEITEMS.C:1004-1045, uw2 OBJUSE.C:301-344):
/// - a lock that is not locked: on an open door (inclass 8 or more) or an open container (class 8, odd inclass below
///   0xC) it answers 4, "That is already open."; otherwise the right key sets its locked bit, 2, "The key locks the
///   lock."; any other key 0, "The key does not fit.";
/// - a locked lock: the right key sets off the unlock traps and deletes the lock, or only clears its locked bit when
///   its flag 1 (id bit 10) is set, 3, "The key unlocks the lock."; the door stays shut. Any other key 0.
///
/// The scenario takes a door with a lock on the level and a key in the backpack, sets the lock's state and flag 1,
/// the key's owner and, for the open door and the containers, the door's item id for the moment of the use, and uses
/// the key on it (doorkey.UseOn) once for each case.
///
/// The port opened the door on unlocking and never deleted the lock, said "That is already open." for any open door
/// without asking checkLock, and on a container toggled the lock with any key (src/objects/doorkey.cs:31-113).
/// </summary>
public sealed class KeyRules : Scenario
{
    public override string Name => "key-rules";
    public override string Cites => "underworld-exhumed uw1/src/obj/USEITEMS.C:251-263, 1004-1045; uw2/src/obj/USEITEMS.C:561-575, uw2/src/obj/OBJUSE.C:301-344";

    /// <summary>checkLock's answer; the message is string answer + 2.</summary>
    enum Says { DoesNotFit = 0, Locks = 2, Unlocks = 3, AlreadyOpen = 4 }

    /// <summary>What the target is for the moment of the use: the door as it is, the door open, or a container.</summary>
    enum As { ShutDoor, OpenDoor, ShutContainer, OpenContainer }

    /// <summary>One use of the key: the lock's state, whether the key fits, and what checkLock answers (both games).</summary>
    sealed record Case(string What, As Target, bool Locked, bool Relock, bool Fits, Says Says);

    // The cases that unlock come last, and the one that deletes the lock last of all.
    static readonly Case[] Cases =
    {
        new("an unlocked shut door, the wrong key", As.ShutDoor, false, true, false, Says.DoesNotFit),
        new("an unlocked shut door, the right key", As.ShutDoor, false, true, true, Says.Locks),
        new("a locked shut door, the wrong key", As.ShutDoor, true, true, false, Says.DoesNotFit),
        new("an unlocked open door, the right key", As.OpenDoor, false, true, true, Says.AlreadyOpen),
        new("an unlocked shut container (0x80), the wrong key", As.ShutContainer, false, true, false, Says.DoesNotFit),
        new("an unlocked shut container (0x80), the right key", As.ShutContainer, false, true, true, Says.Locks),
        new("an unlocked open container (0x81), the right key", As.OpenContainer, false, true, true, Says.AlreadyOpen),
        new("a locked open container (0x81), the right key", As.OpenContainer, true, true, true, Says.Unlocks),
        new("a locked shut door with flag 1, the right key", As.ShutDoor, true, true, true, Says.Unlocks),
        new("a locked shut door without flag 1, the right key", As.ShutDoor, true, false, true, Says.Unlocks),
    };

    const int Key = 0x102;

    string _problem;
    uwObject _door, _lock;
    readonly string[] _fails = new string[Cases.Length];

    public override Task Setup(ScenarioContext c)
    {
        var map = UWTileMap.current_tilemap;
        for (int y = 0; y < 64 && _door == null; y++)
            for (int x = 0; x < 64 && _door == null; x++)
            {
                int i = map.Tiles[x, y].indexObjectList;
                for (int guard = 0; i != 0 && guard < 1024; guard++)
                {
                    var o = map.LevelObjects[i];
                    if (o == null) break;
                    if (o.majorclass == 5 && o.minorclass == 0 && o.classindex < 6 && o.instance != null && a_lock.LockObject(o) != null)
                    {
                        _door = o; _lock = a_lock.LockObject(o);
                        break;
                    }
                    i = o.next;
                }
            }
        if (_door == null) _problem = "no shut door with a lock on the level";
        return Task.CompletedTask;
    }

    public override async Task Act(ScenarioContext c)
    {
        if (_problem != null) return;
        int lockNumber = _lock.link & 0x1FF;
        if (lockNumber == 0) { _problem = "the door's lock has no key number"; return; }
        c.Note($"door {_door.index} (item 0x{_door.item_id:X}) at ({_door.tileX}, {_door.tileY}), lock {_lock.index} for key {lockNumber}");
        var (keyIndex, slot) = Guards.PutInBackpack(Key);
        if (slot == -1) { _problem = "could not put a key in the backpack"; return; }
        var key = playerdat.InventoryObjects[keyIndex];
        int doorItem = _door.item_id;
        for (int n = 0; n < Cases.Length; n++)
        {
            var k = Cases[n];
            _lock.flags0 = (short)(k.Locked ? 1 : 0);
            _lock.flags1 = (short)(k.Relock ? 1 : 0);
            key.owner = (short)(k.Fits ? lockNumber : (lockNumber % 0x3F) + 1);
            _door.item_id = k.Target switch
            {
                As.OpenDoor => doorItem + 8,
                As.ShutContainer => 0x80,
                As.OpenContainer => 0x81,
                _ => doorItem,
            };
            uimanager.instance.scroll.Clear();
            try
            {
                doorkey.UseOn(key, _door);
            }
            finally
            {
                if (_door.item_id != DoorTools.MovingDoor) _door.item_id = doorItem;
            }
            await c.Frames(1);

            string text = string.Join(" ", System.Linq.Enumerable.Select(uimanager.instance.scroll.Lines, l => l?.LineText ?? "")).Trim();
            string expected = GameStrings.GetString(1, (int)k.Says + 2).Trim();
            bool lockThere = a_lock.LockObject(_door) != null;
            var fails = new System.Collections.Generic.List<string>();
            if (!text.Contains(expected)) fails.Add($"expected \"{expected}\", was \"{text}\"");
            if (door.isOpen(_door) || _door.item_id == DoorTools.MovingDoor) fails.Add("the door opened");
            if (k.Says == Says.Unlocks && !k.Relock) { if (lockThere) fails.Add("the lock was kept"); }
            else if (!lockThere) fails.Add("the lock was deleted");
            else if (k.Says == Says.Unlocks && _lock.flags0 != 0) fails.Add("the lock is still locked");
            else if (k.Says == Says.Locks && _lock.flags0 == 0) fails.Add("the lock is not locked");
            else if (k.Says != Says.Unlocks && k.Says != Says.Locks && (_lock.flags0 != 0) != k.Locked) fails.Add("the lock's state changed");
            _fails[n] = fails.Count == 0 ? null : string.Join(", ", fails);
            c.Note($"{k.What}: \"{text}\"{(_fails[n] == null ? "" : " -- " + _fails[n])}");
            if (_door.item_id == DoorTools.MovingDoor) { _problem = "the door turned into a moving door; the later cases cannot run"; break; }
        }
    }

    public override string Assert(ScenarioContext c)
    {
        string cite = c.Game == "uw1" ? "UseKeyOn, checkLock: uw1 USEITEMS.C:251-263, 1004-1045" : "UseKeyOn, checkLock: uw2 USEITEMS.C:561-575, OBJUSE.C:301-344";
        var k = new Checks();
        for (int n = 0; n < Cases.Length; n++)
            if (_fails[n] != null) k.True(false, $"{Cases[n].What}: {_fails[n]} ({cite})");
        if (k.Result != null) return k.Result;
        if (_problem != null) return "precondition: " + _problem;
        return null;
    }
}
