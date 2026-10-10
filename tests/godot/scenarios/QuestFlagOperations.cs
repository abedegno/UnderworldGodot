using System.Collections.Generic;
using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// Quest flags set by traps, schedules and conversations. The original's set_numbered_variable
/// treats variables 0x100 to 0x17F as one bit each, bit (n &amp; 3) of quests[n / 4]: operation 5
/// toggles it, operation 1 leaves it as it was, and every other operation sets it to right &gt; 0
/// (uw2/src/event/TRIGGER.C:169-195, the default case at 188-191). So operation 2 (set) with a
/// right value of 0 clears the flag, as do and, or, shift and count-up with 0.
///
/// The scenario sets a quest flag to a known value, applies one operation through the port's
/// a_set_variable_trap.VariableOperationUW2, which set variable traps, schedule rows
/// (src/scd/functions/scu_variableoperation.cs) and conversations
/// (src/conversation/conversation_functions/x_traps.cs) all use, and reads the flag back with
/// GetQuest. Operation 1 is left out: DOS leaves the flag alone there, most likely a slip in the
/// original that the port already differs from (the audit's "original bugs" list).
///
/// The port once set the flag to 1 for every operation but 1 and 5, whatever the right value
/// (src/traps/a_set_variable_trap.cs:171).
/// </summary>
public sealed class QuestFlagOperations : Scenario
{
    const int Quest = 0x41;   // a quest flag well inside 0..0x7F, variable 0x141

    // (operation, right, flag before)
    static readonly (int Op, int Right, int Before)[] Rows =
    {
        (2, 0, 1),   // set to 0: clears
        (2, 1, 0),   // set to 1: sets
        (2, 5, 0),   // set to 5: sets (right > 0)
        (0, 0, 1),   // add 0: right > 0 is false, clears
        (3, 0, 1),   // and 0: clears
        (4, 0, 1),   // or 0: clears
        (6, 0, 1),   // shift by 0: clears
        (7, 0, 1),   // count up against 0: clears
        (5, 0, 1),   // xor: toggles whatever the right value
        (5, 0, 0),
    };

    public override string Name => "quest-flag-set-zero";
    public override string[] Games => new[] { "uw2" };
    public override string Cites => "underworld-exhumed uw2/src/event/TRIGGER.C:169-195 (set_numbered_variable)";

    readonly List<(int op, int right, int before, int setBack, int after)> _results = new();

    static int Expected(int op, int right, int before) => op switch
    {
        5 => before ^ 1,         // TRIGGER.C:178-182
        1 => before,             // TRIGGER.C:183-187
        _ => right > 0 ? 1 : 0,  // TRIGGER.C:188-191
    };

    public override Task Setup(ScenarioContext c) => Task.CompletedTask;

    public override Task Act(ScenarioContext c)
    {
        foreach (var r in Rows)
        {
            playerdat.SetQuest(Quest, r.Before);
            int setBack = playerdat.GetQuest(Quest);
            a_set_variable_trap.VariableOperationUW2(0x100 + Quest, r.Op, r.Right);
            _results.Add((r.Op, r.Right, r.Before, setBack, playerdat.GetQuest(Quest)));
        }
        playerdat.SetQuest(Quest, 0);
        return Task.CompletedTask;
    }

    public override string Assert(ScenarioContext c)
    {
        foreach (var r in _results)
            if (r.setBack != r.before) return $"precondition: quest {Quest} read back {r.setBack} after setting {r.before}";
        var k = new Checks();
        foreach (var r in _results)
            k.Equal($"quest flag (variable 0x{0x100 + Quest:X}) after operation {r.op} with right {r.right} from {r.before}",
                Expected(r.op, r.right, r.before), r.after,
                r.op == 5 ? "uw2 TRIGGER.C:178-182" : "uw2 TRIGGER.C:188-191, right > 0");
        return k.Result;
    }
}
