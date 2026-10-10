using System;
using System.Threading.Tasks;
using Godot;

namespace Underworld.Scenarios;

/// <summary>
/// A repair takes durability * 3 - skill - quality / 2 minutes, at least 15. The original's
/// do_repair computes that and raises it to 15 when it is less (uw1/src/event/WORLDEV.C:627-630,
/// uw2/src/event/WORLDEV.C:1008-1011); the player's repair then adds the minutes to the clock:
/// UW1 adds time * 60 seconds (in 1/256 s, uw1 WORLDEV.C:705), UW2 calls pass_time(time * 60)
/// (uw2 WORLDEV.C:1085, which adds seconds << 8, WORLDEV.C:163-169).
///
/// The port's RepairLogic takes Math.Min(15, ...) instead, so 15 minutes is the most a repair
/// takes rather than the least, and then Math.Abs of the seconds (src/interaction/repair.cs:83-84).
///
/// The scenario repairs a weapon from the backpack on an anvil as the player does (anvil.UseOn,
/// which runs repair.RepairLogic; answers the "Make an attempt?" question with Yes) twice: once
/// with skill and quality chosen so the formula gives 5 minutes (DOS: 15) and once so it gives
/// 25 (DOS: 25). The skill and quality are found at run time from the weapon's durability in the
/// game data, so the formula hits those values exactly.
/// </summary>
public sealed class RepairTime : Scenario
{
    const int Minute = 60 << 8;          // the clock is in 1/256 s
    const ulong RepairMs = 20000;         // UW1 plays a cutscene first

    public override string Name => "repair-time-floor";
    public override string Cites => "underworld-exhumed uw1/src/event/WORLDEV.C:627-630, 705; uw2/src/event/WORLDEV.C:1008-1011, 1085, 163-169";

    record Case(int Target, int Skill, int Quality, int Expected) { public long Rose; public int Frames; public bool Done; }

    string _problem;
    int _itemId, _durability;
    Case[] _cases;

    public override Task Setup(ScenarioContext c)
    {
        // A melee weapon (item ids 0..15) whose durability lets both targets be hit exactly.
        for (int id = 0; id < 16 && _cases == null; id++)
        {
            int slot = ObjectCreator.PrepareNewObject(id);
            if (slot == 0) continue;
            var probe = UWTileMap.current_tilemap.LevelObjects[slot];
            int d = probe.Durability;
            ObjectFreeLists.ReleaseFreeObject(probe);
            if (d < 0) continue;
            var a = Solve(d, 5); var b = Solve(d, 25);
            if (a == null || b == null) continue;
            _itemId = id; _durability = d;
            _cases = new[] {
                new Case(5, a.Value.s, a.Value.q, 15),   // raised to 15: WORLDEV.C if (repair_time < 15) repair_time = 15
                new Case(25, b.Value.s, b.Value.q, 25),
            };
        }
        if (_cases == null) _problem = "no melee weapon with a durability that lets the formula give 5 and 25 minutes";
        else c.Note($"weapon item {_itemId}, durability {_durability}");
        return Task.CompletedTask;
    }

    /// <summary>A skill 0..30 and quality 0..63 for which d * 3 - skill - quality / 2 == target.</summary>
    static (int s, int q)? Solve(int d, int target)
    {
        for (int q = 0; q < 64; q++)
            for (int s = 0; s <= 30; s++)
                if (d * 3 - s - q / 2 == target) return (s, q);
        return null;
    }

    public override async Task Act(ScenarioContext c)
    {
        if (_problem != null) return;
        foreach (var k in _cases)
        {
            int slot = ObjectCreator.SpawnObjectInHand(_itemId);
            if (slot == 0) { _problem = "no weapon could be made"; return; }
            int freeSlot = uimanager.FreePaperDollSlot;
            if (freeSlot == -1) { _problem = "no free backpack slot"; return; }
            int index = playerdat.AddObjectToPlayerInventory(slot, false);
            playerdat.SetInventorySlotListHead(freeSlot, index);
            playerdat.ObjectInHand = -1;
            uimanager.instance.mousecursor.SetCursorToCursor();
            var item = playerdat.InventoryObjects[index];
            item.quality = (short)k.Quality;
            playerdat.Repair = k.Skill;

            int before = playerdat.ClockValue;
            int anvilSlot = ObjectCreator.PrepareNewObject(0xD7);   // ITEM_ANVIL (include/items.h); anvil.UseOn does not read it
            anvil.UseOn(UWTileMap.current_tilemap.LevelObjects[anvilSlot], item, WorldObject: false);
            ulong deadline = Time.GetTicksMsec() + RepairMs;
            bool answered = false;
            while (Time.GetTicksMsec() < deadline)
            {
                await c.Frames(1);
                k.Frames++;
                if (!answered && MessageDisplay.WaitingForYesOrNo)
                {
                    uimanager.instance.TypedInput.Text = "Yes";
                    MessageDisplay.WaitingForYesOrNo = false;
                    answered = true;
                }
                if (answered && (long)playerdat.ClockValue - before >= Minute) { k.Done = true; break; }
            }
            k.Rose = (long)playerdat.ClockValue - before;
            c.Note($"skill {k.Skill}, quality {k.Quality}: formula {k.Target} min; asked {answered}; clock rose {k.Rose} ({k.Rose / (double)Minute:0.0} min) over {k.Frames} frames");
            await c.Frames(30);
        }
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        var chk = new Checks();
        string cite = c.Game == "uw1" ? "uw1 WORLDEV.C:629-630, 705" : "uw2 WORLDEV.C:1010-1011, 1085";
        foreach (var k in _cases)
        {
            if (!k.Done) return $"precondition: the repair with formula {k.Target} min did not advance the clock by a minute within {RepairMs / 1000} s";
            long play = 0x40 + (k.Frames / 6 + 1) * 0x19;
            chk.Within($"clock advance for a repair whose formula gives {k.Target} min", (long)k.Expected * Minute, (long)k.Expected * Minute + play, k.Rose,
                $"{k.Expected} min: {cite}");
        }
        return chk.Result;
    }
}
