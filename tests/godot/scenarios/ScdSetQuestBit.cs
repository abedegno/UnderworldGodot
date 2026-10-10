using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// A scheduled quest change happens (UW2). SCD.ARK event 4, ev_set_qbit, sets bit quest &amp; 3 of
/// quests[quest &gt;&gt; 2] to the row's value: quests[q &gt;&gt; 2] = (quests[q &gt;&gt; 2] &amp; ~(1 &lt;&lt; (q &amp; 3)))
/// + (value &lt;&lt; (q &amp; 3)) (uw2/src/event/SCDEVENT.C:220-230), the quest flag q in GET_QUEST's
/// packing (uw2/src/event/WORLDEV.C:50). The row is 16 bytes: time, level, once, event (byte 4),
/// then quest (byte 5) and value (byte 6) (uw2/src/include/event.h:36-38, 56-62).
///
/// The port's ChangeQuest only prints the row and returns (src/scd/functions/scd_changequest.cs:7).
///
/// The scenario runs rows through scd.RunSCDFunction, the port's Sched_DoEvent dispatch, for
/// quest flag 37 (quests[9] bit 1): value 1 on a clear flag, then value 0 on a set flag.
/// </summary>
public sealed class ScdSetQuestBit : Scenario
{
    const int Quest = 37;

    public override string Name => "scd-set-quest-bit";
    public override string[] Games => new[] { "uw2" };
    public override string Cites => "underworld-exhumed uw2/src/event/SCDEVENT.C:220-230, uw2/src/include/event.h:36-38, 56-62";

    int _clearBefore, _afterSet, _setBefore, _afterClear;

    static byte[] Row(int quest, int value)
    {
        var r = new byte[16];
        r[2] = 0xFF;          // any level
        r[4] = 4;             // event 4, ev_set_qbit
        r[5] = (byte)quest;
        r[6] = (byte)value;
        return r;
    }

    public override Task Setup(ScenarioContext c) => Task.CompletedTask;

    public override Task Act(ScenarioContext c)
    {
        playerdat.SetQuest(Quest, 0);
        _clearBefore = playerdat.GetQuest(Quest);
        scd.RunSCDFunction(Row(Quest, 1), 0);
        _afterSet = playerdat.GetQuest(Quest);

        playerdat.SetQuest(Quest, 1);
        _setBefore = playerdat.GetQuest(Quest);
        scd.RunSCDFunction(Row(Quest, 0), 0);
        _afterClear = playerdat.GetQuest(Quest);
        c.Note($"quest {Quest}: {_clearBefore} -> row value 1 -> {_afterSet}; {_setBefore} -> row value 0 -> {_afterClear}");
        return Task.CompletedTask;
    }

    public override string Assert(ScenarioContext c)
    {
        if (_clearBefore != 0) return $"precondition: quest {Quest} read back {_clearBefore} after clearing it";
        if (_setBefore != 1) return $"precondition: quest {Quest} read back {_setBefore} after setting it";
        var k = new Checks();
        k.Equal($"quest {Quest} after an SCD row setting it to 1", 1, _afterSet, "uw2 SCDEVENT.C:225-229");
        k.Equal($"quest {Quest} after an SCD row setting it to 0", 0, _afterClear, "uw2 SCDEVENT.C:225-229");
        return k.Result;
    }
}
