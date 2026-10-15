using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// Hack trap 37 brings the schedules up to date (UW2). The original's hack trap runs
/// Sched_SetAllClocks(1) for quality 37 (uw2/src/event/TRIGGER.C:872-873): every one of the 16
/// SCD.ARK blocks has the current level's clock set from player->xclock[block] (block 0 wrapping
/// at 72) and, with mode 1, the rows that fall due are run (uw2/src/event/SCHEDULE.C:202-223,
/// Sched_SetTime :165-180). A block's header holds, per level, the clock (byte 4 + level * 4) and
/// the next row to run (byte 6 + level * 4); rows start at 0x144, 16 bytes each, time in byte 0,
/// level in byte 2 (0xFF any level), event in byte 4 (uw2/src/include/event.h).
///
/// The port's hack_trap had no case 37, so the trap did nothing (src/traps/hack_trap.cs).
///
/// The scenario puts a block of its own in place of block 1 in the port's scd.scd_data (which
/// ProcessSCDArk uses instead of loading the block from SCD.ARK): one row, time 5, any level,
/// event 0 (does nothing), with the current level's clock at 4 and its next row 0. It sets X
/// clock 1 to 5 and fires a hack trap of quality 37 through trap.ActivateTrap. The block's clock
/// for the level must then be 5 and its next row 1: the row came due and ran. X clock 1 and the
/// scd blocks are restored afterwards.
/// </summary>
public sealed class HackSetAllClocks : Scenario
{
    const int Block = 1, Due = 5;

    public override string Name => "uw2-hack-set-all-clocks";
    public override string[] Games => new[] { "uw2" };
    public override string Cites => "underworld-exhumed uw2/src/event/TRIGGER.C:872-873, uw2/src/event/SCHEDULE.C:165-223";

    string _problem;
    int _level, _clockAfter, _nextAfter;
    bool _kept;

    public override Task Setup(ScenarioContext c) => Task.CompletedTask;

    public override Task Act(ScenarioContext c)
    {
        _level = playerdat.dungeon_level;
        int slot = ObjectCreator.PrepareNewObject(0x183);   // ITEM_HACK_TRAP
        if (slot == 0) { _problem = "no hack trap could be made"; return Task.CompletedTask; }
        var hack = UWTileMap.current_tilemap.LevelObjects[slot];
        hack.quality = 37;
        hack.link = 0;

        var data = new byte[0x144 + 16];
        data[0] = 1;                            // one row
        data[2] = Block;
        data[4 + _level * 4] = Due - 1;         // this level's clock
        data[6 + _level * 4] = 0;               // its next row
        data[0x144 + 0] = Due;                  // row time
        data[0x144 + 2] = 0xFF;                 // any level
        data[0x144 + 4] = 0;                    // event 0, does nothing
        var block = new UWBlock { Data = data, DataLen = data.Length };

        var savedBlocks = scd.scd_data == null ? null : (UWBlock[])scd.scd_data.Clone();
        int xclockWas = playerdat.GetXClock(Block);
        try
        {
            scd.scd_data ??= new UWBlock[0x10];
            scd.scd_data[Block] = block;
            playerdat.SetXClock(Block, Due);
            trap.ActivateTrap(1, hack, null, playerdat.playerObject.tileX, playerdat.playerObject.tileY, UWTileMap.current_tilemap.LevelObjects);
            _kept = scd.scd_data[Block] == block;
            //read the block the game holds now: Sched_Load works on a copy and Sched_Save stores it back
            var after = scd.scd_data[Block]?.Data ?? data;
            _clockAfter = after[4 + _level * 4];
            _nextAfter = after[6 + _level * 4];
            c.Note($"level {_level}: block {Block} clock {Due - 1} -> {_clockAfter}, next row 0 -> {_nextAfter}, block kept in memory {_kept}");
        }
        finally
        {
            playerdat.SetXClock(Block, xclockWas);
            scd.scd_data = savedBlocks;
        }
        return Task.CompletedTask;
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        var k = new Checks();
        k.Equal($"block {Block}'s clock for level {_level} after hack trap 37", Due, _clockAfter, "Sched_SetTime: uw2 SCHEDULE.C:171, :212-214");
        k.Equal($"block {Block}'s next row for level {_level} after hack trap 37 (the due row ran)", 1, _nextAfter, "uw2 SCHEDULE.C:180, TRIGGER.C:872-873");
        return k.Result;
    }
}
