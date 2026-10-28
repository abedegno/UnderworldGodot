using System.Collections.Generic;
using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// Where a trap chain goes on and where it stops (UW1 and UW2). After a trap, the original's
/// UseTrap runs the object its link points at, a trap with UseTrap / SetOffTrap or a trigger with
/// UseTrigger(..., type -1), unless the trap ended the chain (continue_chain = 0)
/// (uw1/src/event/TRIGGER.C:426-436, uw2/src/event/TRIGGER.C:702-712). A trigger reached with
/// type -1 skips every check on who set it off (uw1 TRIGGER.C:116, uw2 TRIGGER.C:98). A ward trap
/// always ends the chain (uw1 TRIGGER.C:403-416, also the tell trap; uw2 TRIGGER.C:681-693), and in
/// UW1 so does a door trap (uw1 TRIGGER.C:339-340). UW2's door trap ends it when the square has a door
/// and the trap's owner is not 0, the case where its link is a lock to put on the door (uw2 TRIGGER.C:633-636). A hack trap goes on (uw1 TRIGGER.C:288-290,
/// uw2 TRIGGER.C:514-516); UW2's hack 37 brings the schedules up to date (TRIGGER.C:872-873) and
/// UW1's hack 0x32 makes a waiting 0xD8 critter talk (TRIGGER.C:526-527), neither ending it.
///
/// The port: a chained trigger still needs the player flag (src/triggers/trigger.cs:51-55); the
/// ward trap passes the chain on (src/traps/a_ward_trap.cs:29); UW1's door trap passes it on, and
/// UW2's always does (src/traps/trap.cs, case 8); a hack trap it does not handle stops the chain
/// (src/traps/hack_trap.cs:456, trap.cs's "implemented" test).
///
/// Each case fires a chain through trap.ActivateTrap with the player (index 1) as the one who set
/// it off, ending in a set-variable trap that adds 7 to variable 10 (UW2: variable xpos &lt;&lt; 7 + z,
/// value quality &lt;&lt; 6 + owner, operation heading 0; UW1: variable z, value
/// ((quality &lt;&lt; 5 | owner) &lt;&lt; 3) | y, operation heading 0). The precondition fires that trap alone
/// and sees the 7 added.
/// </summary>
public sealed class TrapChainFlow : Scenario
{
    const int Var = 10, Add = 7;
    const int HackTrap = 0x183, DoorTrap = 0x188, WardTrap = 0x189, SetVariable = 0x18D, UseTrigger = 0x1A2;   // include/items.h

    public override string Name => "trap-chain-flow";
    public override string Cites => "underworld-exhumed uw1/src/event/TRIGGER.C:104-136, 339-340, 403-436; uw2/src/event/TRIGGER.C:86-128, 633-636, 681-712";

    record Case(string Name, bool Continues, string Cite) { public int Got; }

    readonly List<Case> _cases = new();
    string _problem;
    int _alone;
    int _px, _py;

    uwObject Make(int item)
    {
        int slot = ObjectCreator.PrepareNewObject(item);
        if (slot == 0) { _problem ??= $"no object 0x{item:X} could be made"; return null; }
        var o = UWTileMap.current_tilemap.LevelObjects[slot];
        o.link = 0; o.next = 0; o.is_quant = 0;
        o.tileX = _px; o.tileY = _py;
        return o;
    }

    uwObject AddVariable(bool uw2)
    {
        var t = Make(SetVariable);
        if (t == null) return null;
        t.heading = 0;   // add
        if (uw2) { t.xpos = 0; t.zpos = Var; t.quality = 0; t.owner = Add; t.ypos = 0; }
        else { t.zpos = Var; t.quality = 0; t.owner = 0; t.ypos = Add; t.xpos = 0; }
        return t;
    }

    int Fire(uwObject first) => Fire(first, _px, _py);

    int Fire(uwObject first, int x, int y)
    {
        playerdat.SetGameVariable(Var, 0);
        trap.ActivateTrap(character: 1, trapObj: first, ObjectUsed: null, triggerX: x, triggerY: y, objList: UWTileMap.current_tilemap.LevelObjects);
        return playerdat.GetGameVariable(Var);
    }

    public override Task Setup(ScenarioContext c)
    {
        _px = playerdat.playerObject.tileX; _py = playerdat.playerObject.tileY;
        return Task.CompletedTask;
    }

    public override Task Act(ScenarioContext c)
    {
        bool uw2 = c.Game == "uw2";
        var alone = AddVariable(uw2);
        if (alone == null) return Task.CompletedTask;
        _alone = Fire(alone);
        c.Note($"the set-variable trap alone: variable {Var} = {_alone}");

        // A ward of any class (quality 0x3F) set off by the player, then the variable trap.
        var ward = Make(WardTrap); var v1 = AddVariable(uw2);
        if (ward == null || v1 == null) return Task.CompletedTask;
        ward.quality = 0x3F; ward.link = (short)v1.index;
        Run(c, new Case("ward trap -> set variable", false, uw2 ? "continue_chain = 0: uw2 TRIGGER.C:693" : "continue_chain = 0: uw1 TRIGGER.C:416"), ward);

        // A trap, then a trigger the player could not set off himself (no ID_FLAG11), then the variable trap.
        var head = AddVariable(uw2); var trig = Make(UseTrigger); var v2 = AddVariable(uw2);
        if (head == null || trig == null || v2 == null) return Task.CompletedTask;
        head.zpos = Var + 1;   // the head adds to the next variable (z is the variable in both games)
        trig.flags_full = 2;            // ID_FLAG10 set (the chain is not deleted), ID_FLAG11 clear
        trig.enchantment = 0;
        trig.quality = (short)_px; trig.owner = (short)_py;
        trig.link = (short)v2.index; head.link = (short)trig.index;
        Run(c, new Case("set variable -> trigger without the player flag -> set variable", true,
            uw2 ? "UseTrigger(..., -1) skips the checks: uw2 TRIGGER.C:98, 702-707" : "UseTrigger(..., -1) skips the checks: uw1 TRIGGER.C:116, 426-433"), head);

        // A hack trap the original goes on from.
        var hack = Make(HackTrap); var v3 = AddVariable(uw2);
        if (hack == null || v3 == null) return Task.CompletedTask;
        hack.quality = (short)(uw2 ? 37 : 0x32); hack.owner = 0; hack.link = (short)v3.index;
        Run(c, new Case(uw2 ? "hack trap 37 -> set variable" : "hack trap 0x32 -> set variable", true,
            uw2 ? "hack 37, Sched_SetAllClocks, then the chain: uw2 TRIGGER.C:514-516, 872-873, 702-711" : "hack 0x32, then the chain: uw1 TRIGGER.C:288-290, 526-527, 426-430"), hack);

        if (!uw2)
        {
            var door = Make(DoorTrap); var v4 = AddVariable(uw2);
            if (door == null || v4 == null) return Task.CompletedTask;
            door.quality = 1; door.link = (short)v4.index;
            Run(c, new Case("door trap -> set variable", false, "continue_chain = 0: uw1 TRIGGER.C:339-340"), door);
        }
        else
        {
            // UW2's door trap in a square with a door: owner 0 goes on, owner != 0 swaps the lock and ends the chain.
            var (dx, dy) = FindDoorSquare();
            if (dx < 0) { _problem ??= "no square with a door on the level"; return Task.CompletedTask; }
            foreach (int owner in new[] { 0, 1 })
            {
                var door = Make(DoorTrap); var v4 = AddVariable(uw2);
                if (door == null || v4 == null) return Task.CompletedTask;
                door.quality = 0;   // no open, close or toggle
                door.owner = (short)owner; door.link = (short)v4.index;
                Run(c, new Case($"door trap with owner {owner} at a door -> set variable", owner == 0,
                    owner == 0 ? "the chain goes on: uw2 TRIGGER.C:633, 702-711" : "continue_chain = 0 with a door and owner != 0: uw2 TRIGGER.C:633-636"), door, dx, dy);
            }
        }
        return Task.CompletedTask;
    }

    /// <summary>The first square holding a door (major class 5, minor class 0), or (-1, -1).</summary>
    static (int x, int y) FindDoorSquare()
    {
        var map = UWTileMap.current_tilemap;
        for (int x = 0; x < 64; x++)
        for (int y = 0; y < 64; y++)
            for (int i = map.Tiles[x, y].indexObjectList, guard = 0; i != 0 && guard < 1024; guard++)
            {
                var o = map.LevelObjects[i];
                if (o.majorclass == 5 && o.minorclass == 0) return (x, y);
                i = o.next;
            }
        return (-1, -1);
    }

    void Run(ScenarioContext c, Case k, uwObject first) => Run(c, k, first, _px, _py);

    void Run(ScenarioContext c, Case k, uwObject first, int x, int y)
    {
        k.Got = Fire(first, x, y);
        _cases.Add(k);
        c.Note($"{k.Name}: variable {Var} = {k.Got}");
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        if (_alone != Add) return $"precondition: the set-variable trap alone left variable {Var} at {_alone}, not {Add}";
        var chk = new Checks();
        foreach (var k in _cases)
            chk.Equal($"{k.Name}: variable {Var} ({(k.Continues ? "the chain goes on" : "the chain ends")})", k.Continues ? Add : 0, k.Got, k.Cite);
        return chk.Result;
    }
}
