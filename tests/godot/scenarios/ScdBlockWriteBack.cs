using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// Which changes to a schedule block are kept (UW2). Sched_SetAllClocks loads each of the 16 SCD.ARK
/// blocks, sets the current level's clock from the block's X clock and writes the block back with
/// Sched_Save (uw2/src/event/SCHEDULE.C:204-222). Sched_Load clears the modified flag
/// (SCHEDULE.C:122-133) and Sched_Save writes only when it is set (SCHEDULE.C:137-158); only a row
/// that ran (result 0) or deleted itself (result 4) sets it (SCHEDULE.C:66-69). So a block whose
/// clock moved but ran no row keeps its old clock in SCD.ARK, a block written back earlier keeps that
/// write, and an error stops before the save. A row that deletes itself leaves the index where it
/// is (case 4 does i-- and falls into case 0, then i++), so the row after it runs next and the one
/// before it is not run again (SCHEDULE.C:66-69).
///
/// The port keeps written-back blocks in scd.scd_data (null: the block in the file). It cleared the
/// modified flag once per pass, not per block, dropped a block from scd_data when no row ran even if
/// an earlier pass had changed it, and did si-- without the i++ on a deleted row
/// (src/scd/scd.cs:18, 61-68, 186-189).
///
/// The scenario fills all 16 blocks in memory, sets each X clock to its block's clock, and makes:
/// block 1 run a row (written back), block 2 move its clock only, block 3 delete a once row, and
/// block 4 run the rows round a once row in the middle. A second pass then moves block 3's clock only.
/// </summary>
public sealed class ScdBlockWriteBack : Scenario
{
    const int VarA = 100, VarB = 101, VarC = 102;

    public override string Name => "scd-block-write-back";
    public override string[] Games => new[] { "uw2" };
    public override string Cites => "underworld-exhumed uw2/src/event/SCHEDULE.C:56-80, 122-158, 169-184, 204-222, uw2/src/event/SCDEVENT.C:488-505";

    UWBlock[] _origData;
    readonly int[] _origXClock = new int[16];
    readonly int[] _origVars = new int[3];
    int _level, _varABefore;
    int _b1Clock, _b1Next, _b2Clock, _b2Kept, _b3Rows, _b3Clock, _b4Rows, _b4Next, _varA, _varB, _varC;
    int _b1KeptAfter2, _b1ClockAfter2, _b3KeptAfter2, _b3RowsAfter2, _b3ClockAfter2;

    int ClockAt => 4 + _level * 4;
    int NextAt => 6 + _level * 4;

    static UWBlock Block(int blockno, int clock, int level, params byte[][] rows)
    {
        var data = new byte[324 + 16 * 4];
        data[0] = (byte)rows.Length;
        data[2] = (byte)blockno;
        data[4 + level * 4] = (byte)clock;
        for (int i = 0; i < rows.Length; i++)
            System.Array.Copy(rows[i], 0, data, 324 + i * 16, 16);
        return new UWBlock { Data = data, DataLen = data.Length };
    }

    /// <summary>A row due at time 1 for any level: event 9 adds 1 to var, or event 0 does nothing.</summary>
    static byte[] Row(int once, int evnt, int var = 0)
    {
        var r = new byte[16];
        r[0] = 1;              // time
        r[2] = 0xFF;           // any level
        r[3] = (byte)once;
        r[4] = (byte)evnt;
        r[5] = (byte)var;      // 9: var, op 0 (add), value 1 (event.h:46-50, TRIGGER.C:154)
        r[8] = 1;
        return r;
    }

    public override Task Setup(ScenarioContext c)
    {
        _level = playerdat.dungeon_level;
        _origData = scd.scd_data;
        for (int b = 0; b < 16; b++) _origXClock[b] = playerdat.GetXClock(b);
        _origVars[0] = playerdat.GetGameVariable(VarA);
        _origVars[1] = playerdat.GetGameVariable(VarB);
        _origVars[2] = playerdat.GetGameVariable(VarC);
        return Task.CompletedTask;
    }

    public override Task Act(ScenarioContext c)
    {
        try
        {
            playerdat.SetGameVariable(VarA, 0);
            playerdat.SetGameVariable(VarB, 0);
            playerdat.SetGameVariable(VarC, 0);
            _varABefore = playerdat.GetGameVariable(VarA);

            scd.scd_data = new UWBlock[16];
            for (int b = 0; b < 16; b++)
            {
                scd.scd_data[b] = Block(b, 0, _level);
                playerdat.SetXClock(b, 0);
            }
            scd.scd_data[1] = Block(1, 0, _level, Row(0, 9, VarA));
            scd.scd_data[3] = Block(3, 0, _level, Row(1, 0));
            scd.scd_data[4] = Block(4, 0, _level, Row(0, 9, VarB), Row(1, 0), Row(0, 9, VarC));
            playerdat.SetXClock(1, 1);
            playerdat.SetXClock(2, 5);
            playerdat.SetXClock(3, 1);
            playerdat.SetXClock(4, 1);

            scd.ProcessSCDArk(1);

            var d = scd.scd_data;
            _b1Clock = d[1] == null ? -1 : d[1].Data[ClockAt];
            _b1Next = d[1] == null ? -1 : d[1].Data[NextAt];
            _b2Kept = d[2] == null ? 0 : 1;
            _b2Clock = d[2] == null ? -1 : d[2].Data[ClockAt];
            _b3Rows = d[3] == null ? -1 : d[3].Data[0];
            _b3Clock = d[3] == null ? -1 : d[3].Data[ClockAt];
            _b4Rows = d[4] == null ? -1 : d[4].Data[0];
            _b4Next = d[4] == null ? -1 : d[4].Data[NextAt];
            _varA = playerdat.GetGameVariable(VarA);
            _varB = playerdat.GetGameVariable(VarB);
            _varC = playerdat.GetGameVariable(VarC);

            // Second pass: only block 3's clock moves.
            playerdat.SetXClock(3, 2);
            scd.ProcessSCDArk(1);
            d = scd.scd_data;
            _b1KeptAfter2 = d[1] == null ? 0 : 1;
            _b1ClockAfter2 = d[1] == null ? -1 : d[1].Data[ClockAt];
            _b3KeptAfter2 = d[3] == null ? 0 : 1;
            _b3RowsAfter2 = d[3] == null ? -1 : d[3].Data[0];
            _b3ClockAfter2 = d[3] == null ? -1 : d[3].Data[ClockAt];
            c.Note($"pass 1: block 1 clock {_b1Clock} next {_b1Next}; block 2 kept {_b2Kept} clock {_b2Clock}; block 3 rows {_b3Rows} clock {_b3Clock}; block 4 rows {_b4Rows} next {_b4Next}; vars {_varA} {_varB} {_varC}");
            c.Note($"pass 2: block 1 kept {_b1KeptAfter2} clock {_b1ClockAfter2}; block 3 kept {_b3KeptAfter2} rows {_b3RowsAfter2} clock {_b3ClockAfter2}");
        }
        finally
        {
            scd.scd_data = _origData;
            for (int b = 0; b < 16; b++) playerdat.SetXClock(b, _origXClock[b]);
            playerdat.SetGameVariable(VarA, _origVars[0]);
            playerdat.SetGameVariable(VarB, _origVars[1]);
            playerdat.SetGameVariable(VarC, _origVars[2]);
        }
        return Task.CompletedTask;
    }

    public override string Assert(ScenarioContext c)
    {
        if (_varABefore != 0) return $"precondition: var {VarA} read back {_varABefore} after clearing it";
        if (_varA != 1) return $"precondition: block 1's row (add 1 to var {VarA}) ran {_varA} times, expected once";
        var k = new Checks();
        k.Equal("block 1 (a row ran) clock written back", 1, _b1Clock, "uw2 SCHEDULE.C:69, 155-158");
        k.Equal("block 1 next row written back", 1, _b1Next, "uw2 SCHEDULE.C:76");
        k.Equal("block 2 (clock moved, no row ran) clock in SCD.ARK", 0, _b2Clock, "uw2 SCHEDULE.C:129, 155-158, 169-184");
        k.Equal("block 3 (once row deleted) rows written back", 0, _b3Rows, "uw2 SCDEVENT.C:500-502, SCHEDULE.C:268-277");
        k.Equal("block 3 clock written back", 1, _b3Clock, "uw2 SCHEDULE.C:155-158");
        k.Equal($"block 4: row before the once row run (var {VarB})", 1, _varB, "uw2 SCHEDULE.C:66-69, 75");
        k.Equal($"block 4: row after the once row run (var {VarC})", 1, _varC, "uw2 SCHEDULE.C:66-69, 75");
        k.Equal("block 4 rows written back", 2, _b4Rows, "uw2 SCDEVENT.C:500-502, SCHEDULE.C:268-277");
        k.Equal("block 4 next row written back", 2, _b4Next, "uw2 SCHEDULE.C:77");
        k.Equal("block 1 still holds pass 1's write after a pass that left it alone", 1, _b1KeptAfter2, "uw2 SCHEDULE.C:137-158");
        k.Equal("block 1 clock after pass 2", 1, _b1ClockAfter2, "uw2 SCHEDULE.C:137-158");
        k.Equal("block 3 still holds pass 1's write after a clock-only pass", 1, _b3KeptAfter2, "uw2 SCHEDULE.C:137-158");
        k.Equal("block 3 rows after pass 2", 0, _b3RowsAfter2, "uw2 SCHEDULE.C:137-158");
        k.Equal("block 3 clock after its clock-only pass 2", 1, _b3ClockAfter2, "uw2 SCHEDULE.C:129, 155-158, 169-184");
        return k.Result;
    }
}
