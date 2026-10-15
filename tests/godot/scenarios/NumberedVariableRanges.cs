using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// The numbered variables' upper ranges (UW2). set_numbered_variable and get_numbered_variable
/// (uw2/src/event/TRIGGER.C:165-219) map 0x180..0x18F to the 16 quest bytes (quest 128 on) and
/// 0x190..0x19F to the 16 X clocks; anything above 0x19F is ignored on a set and reads 0.
///
/// The port's a_check_variable_trap.GetVarQuestOrClockValue took 0x180..0x190 inclusive as quest
/// bytes, so 0x190 read quest 144 instead of X clock 0, and both it and
/// a_set_variable_trap.VariableOperationUW2 took 0x190..0x1FF as X clocks, so 0x1A0 on reached
/// past the 16 clocks.
///
/// The scenario sets 0x18F, 0x190, 0x19F and 0x1A0 through a set-variable trap (left variable
/// zpos + (xpos &lt;&lt; 7), value (quality &lt;&lt; 6) + owner, operation heading 2, TRIGGER.C:338-344) and
/// reads each back through a check-variable trap in sum mode (variable zpos | (xpos &amp; 3) &lt;&lt; 7,
/// xpos &gt;&gt; 2 set, heading 0, compared with ((quality &lt;&lt; 5) | owner) &lt;&lt; 3 | ypos, TRIGGER.C:345-361).
/// The check trap's own index is its link, so a match returns it and a mismatch returns 0.
/// </summary>
public sealed class NumberedVariableRanges : Scenario
{
    public override string Name => "numbered-variable-ranges";
    public override string[] Games => new[] { "uw2" };
    public override string Cites => "underworld-exhumed uw2/src/event/TRIGGER.C:165-219, 338-361";

    // variable, value set, value expected back
    static readonly (int var, int set, int expect)[] Cases =
    {
        (0x18F, 5, 5),   // quest byte 15 (quest 143)
        (0x190, 6, 6),   // X clock 0
        (0x19F, 3, 3),   // X clock 15
        (0x1A0, 7, 0),   // past the X clocks: ignored, reads 0
    };

    string _problem;
    readonly bool[] _matched = new bool[Cases.Length];
    readonly int[] _direct = new int[Cases.Length];

    public override Task Setup(ScenarioContext c) => Task.CompletedTask;

    public override Task Act(ScenarioContext c)
    {
        var objs = UWTileMap.current_tilemap.LevelObjects;
        int setSlot = ObjectCreator.PrepareNewObject(0x18D);     // a_set_variable_trap
        int checkSlot = ObjectCreator.PrepareNewObject(0x18E);   // a_check_variable_trap
        if (setSlot == 0 || checkSlot == 0) { _problem = "no trap could be made"; return Task.CompletedTask; }
        var set = objs[setSlot];
        var check = objs[checkSlot];

        var pdat = (byte[])playerdat.pdat.Clone();
        for (int k = 0; k < Cases.Length; k++)
        {
            var (v, value, _) = Cases[k];
            set.xpos = (short)(v >> 7); set.zpos = (short)(v & 0x7F); set.ypos = 0;
            set.heading = 2; set.quality = (short)(value >> 6); set.owner = (short)(value & 0x3F);
            a_set_variable_trap.Activate(set);

            int expect = Cases[k].expect;
            check.xpos = (short)(4 | ((v >> 7) & 3)); check.zpos = (short)(v & 0x7F); check.heading = 0;
            check.quality = (short)(expect >> 8); check.owner = (short)((expect >> 3) & 0x1F); check.ypos = (short)(expect & 7);
            check.link = (short)checkSlot;
            check.next = 0;
            _matched[k] = a_check_variable_trap.Activate(check, objs) == checkSlot;
            _direct[k] = a_check_variable_trap.GetVarQuestOrClockValue(v);
            c.Note($"variable 0x{v:X}: set {value}, reads {_direct[k]}, check trap for {expect} {(_matched[k] ? "matched" : "did not match")}");
        }
        playerdat.pdat = pdat;
        return Task.CompletedTask;
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        var k = new Checks();
        for (int i = 0; i < Cases.Length; i++)
        {
            var (v, value, expect) = Cases[i];
            string what = v switch { < 0x190 => "quest byte", < 0x1A0 => "X clock", _ => "past the X clocks" };
            k.Equal($"variable 0x{v:X} ({what}) read back after setting {value}", expect, _direct[i], "get/set_numbered_variable: uw2 TRIGGER.C:165-219");
            k.True(_matched[i], $"check-variable trap on 0x{v:X} for {expect} did not match (TRIGGER.C:345-361)");
        }
        return k.Result;
    }
}
