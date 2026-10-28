using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// Picking a lock (UW1 and UW2). The original's UseLockpickOn calls checkLock with minus the Pick Lock skill in UW1
/// and minus (Pick Lock + 1) in UW2 (uw1/src/obj/USEITEMS.C:222-249, uw2/src/obj/USEITEMS.C:525-557). checkLock
/// (uw1 USEITEMS.C:1004-1045, uw2 OBJUSE.C:301-344):
/// - a lock that is not locked answers "That is not locked."; UW2 reports "Your lockpicking attempt failed." instead
///   when the player has no Pick Lock skill;
/// - a lock at z 0xF cannot be picked, and one at z 0xE needs a skill of 0x1F in UW1 (0x20, Pick Lock + 1, in UW2);
/// - otherwise skill_check(skill, z * 3) decides; a pick sets off the unlock traps and deletes the lock, or only
///   clears its locked bit when its flag 1 (id bit 10) is set. The door stays shut;
/// - only UW2 can break the pick: on a fumble, unless skill_check(dexterity, 20) passes, "You broke your pick." and
///   the pick is gone. In UW1 a fumble is a failure.
///
/// The scenario takes a door with a lock on the level and a lockpick in the backpack, sets the lock's state, its z,
/// the Pick Lock skill, dexterity and rand() (fixed: skill_check adds rand() % 31), and uses the pick on the door
/// (lockpick.UseOn) once for each case.
///
/// The port picked with Pick Lock + 1 in both games, broke the pick in UW1 too, had no z 0xE or 0xF rule, opened
/// the door and never deleted the lock (src/objects/lockpick.cs:44-74).
/// </summary>
public sealed class LockpickRules : Scenario
{
    public override string Name => "lockpick-rules";
    public override string Cites => "underworld-exhumed uw1/src/obj/USEITEMS.C:222-249, 1004-1045; uw2/src/obj/USEITEMS.C:525-557, uw2/src/obj/OBJUSE.C:301-344";

    sealed class FixedRand : IRandSource
    {
        readonly int _v;
        public FixedRand(int v) { _v = v; }
        public int Rand() => _v;
        public int Next(int max) => max <= 0 ? 0 : _v % max;
    }

    enum Says { Failed, Picked, NotLocked, Broke }

    /// <summary>One use of the pick: the lock's state and the roll, and what the original does in each game.</summary>
    sealed record Case(string What, bool Locked, bool Relock, int Z, int Skill, int Dex, int Rand, Says Uw1, Says Uw2);

    static readonly Case[] Cases =
    {
        new("a lock that is not locked", false, true, 1, 5, 20, 30, Says.NotLocked, Says.NotLocked),
        new("a lock that is not locked, no Pick Lock skill", false, true, 1, 0, 20, 30, Says.NotLocked, Says.Failed),
        new("a lock at z 0xF, skill 30, best roll", true, true, 0xF, 30, 20, 30, Says.Failed, Says.Failed),
        new("a lock at z 0xE, skill 0x1E, best roll", true, true, 0xE, 0x1E, 20, 30, Says.Failed, Says.Failed),
        new("a lock at z 0xE, skill 0x1F, best roll", true, true, 0xE, 0x1F, 20, 30, Says.Picked, Says.Picked),
        new("a lock at z 4, skill 10, roll 17", true, true, 4, 10, 20, 17, Says.Failed, Says.Picked),
        new("a fumble (z 4, skill 10, roll 0, dexterity 10)", true, true, 4, 10, 10, 0, Says.Failed, Says.Broke),
        new("a lock without flag 1 at z 1, skill 30, best roll", true, false, 1, 30, 20, 30, Says.Picked, Says.Picked),
    };

    string _problem;
    uwObject _door, _lock;
    readonly string[] _fails = new string[Cases.Length];

    static int Lockpick(ScenarioContext c) => c.Game == "uw1" ? 0x100 : 0x101;

    string Text(Says s, ScenarioContext c) => s switch
    {
        Says.Failed => GameStrings.GetString(1, GameStrings.str_your_lockpicking_attempt_failed_),
        Says.Picked => GameStrings.GetString(1, GameStrings.str_you_succeed_in_picking_the_lock_),
        Says.NotLocked => GameStrings.GetString(1, GameStrings.str_that_is_not_locked_),
        _ => GameStrings.GetString(1, 134),
    };

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
        var keepSource = Rng.Source;
        int keepSkill = playerdat.PickLock, keepDex = playerdat.DEX;
        c.Note($"door {_door.index} (item 0x{_door.item_id:X}) at ({_door.tileX}, {_door.tileY}), lock {_lock.index}");
        var (pickIndex, slot) = Guards.PutInBackpack(Lockpick(c));
        if (slot == -1) { _problem = "could not put a lockpick in the backpack"; return; }
        try
        {
            for (int n = 0; n < Cases.Length; n++)
            {
                var k = Cases[n];
                var pick = playerdat.InventoryObjects[pickIndex];
                if (pick == null)
                {// the last case broke it: another
                    (pickIndex, slot) = Guards.PutInBackpack(Lockpick(c));
                    if (slot == -1) { _problem = "could not put another lockpick in the backpack"; return; }
                    pick = playerdat.InventoryObjects[pickIndex];
                }
                _lock.flags0 = (short)(k.Locked ? 1 : 0);
                _lock.flags1 = (short)(k.Relock ? 1 : 0);
                _lock.zpos = (short)k.Z;
                playerdat.PickLock = k.Skill;
                playerdat.DEX = k.Dex;
                Rng.Source = new FixedRand(k.Rand);
                uimanager.instance.scroll.Clear();
                lockpick.UseOn(pick, _door);
                Rng.Source = keepSource;
                await c.Frames(1);

                string text = string.Join(" ", System.Linq.Enumerable.Select(uimanager.instance.scroll.Lines, l => l?.LineText ?? "")).Trim();
                var says = c.Game == "uw1" ? k.Uw1 : k.Uw2;
                bool lockThere = a_lock.LockObject(_door) != null;
                bool pickThere = playerdat.InventoryObjects[pickIndex] != null;
                var fails = new System.Collections.Generic.List<string>();
                if (!text.Contains(Text(says, c).Trim())) fails.Add($"expected \"{Text(says, c).Trim()}\", was \"{text}\"");
                if (door.isOpen(_door) || _door.item_id == DoorTools.MovingDoor) fails.Add("the door opened");
                if (says == Says.Picked && !k.Relock) { if (lockThere) fails.Add("the lock was kept"); }
                else if (!lockThere) fails.Add("the lock was deleted");
                else if (says == Says.Picked && _lock.flags0 != 0) fails.Add("the lock is still locked");
                else if (says != Says.Picked && (_lock.flags0 != 0) != k.Locked) fails.Add("the lock's state changed");
                if (says == Says.Broke ? pickThere : !pickThere) fails.Add(pickThere ? "the pick was kept" : "the pick was lost");
                _fails[n] = fails.Count == 0 ? null : string.Join(", ", fails);
                c.Note($"{k.What}: \"{text}\"{(_fails[n] == null ? "" : " -- " + _fails[n])}");
            }
        }
        finally
        {
            Rng.Source = keepSource;
            playerdat.PickLock = keepSkill;
            playerdat.DEX = keepDex;
        }
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        string cite = c.Game == "uw1" ? "UseLockpickOn, checkLock: uw1 USEITEMS.C:222-249, 1026-1044" : "UseLockpickOn, checkLock: uw2 USEITEMS.C:535-557, OBJUSE.C:327-343";
        var k = new Checks();
        for (int n = 0; n < Cases.Length; n++)
            k.True(_fails[n] == null, $"{Cases[n].What}: {_fails[n]} ({cite})");
        return k.Result;
    }
}
