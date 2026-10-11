using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// The conversation import x_traps(variable, value) sets a game variable only when the value is in
/// range, and returns the variable. UW1 sets game_vars[var] when the value is 0..0x3F
/// (uw1/src/conv/BABLHACK.C:116-128); UW2 calls set_numbered_variable(var, 2, val) when the value is
/// 0..0x3FF (uw2/src/conv/BABLHACK.C:435-446). Both read their arguments with getmem(args[n]): the
/// stack holds the address of each argument, which the port reads the same way
/// (GetConvoStackValueAtPtr, a double lookup, as x_skills' at(at(...))).
///
/// The port's x_traps (src/conversation/conversation_functions/x_traps.cs:14) tested the variable
/// number (&lt; 0x3FF) instead of the value, and used the UW2 range and set_numbered_variable in UW1 too.
///
/// The scenario builds a call the way a script does, the arguments' addresses pushed last argument
/// first and then the count (2), and calls x_traps for game variable 10 with a value one past the
/// range (unchanged), the top of the range, and 5.
/// </summary>
public sealed class XTrapsValueRange : Scenario
{
    const int Var = 10;
    const int VarAddr = 100, ValueAddr = 101;   // script memory slots holding the arguments

    public override string Name => "x-traps-value-range";
    public override string[] Games => new[] { "uw1", "uw2" };
    public override string Cites => "underworld-exhumed uw1/src/conv/BABLHACK.C:116-128, uw2/src/conv/BABLHACK.C:435-446";

    int _max;
    int _overVar, _overResult, _topVar, _topExpected, _fiveVar, _fiveResult;

    public override Task Setup(ScenarioContext c) => Task.CompletedTask;

    static int Call(int value)
    {
        ConversationVM.StackValues = new short[4096];
        ConversationVM.stack = 0;
        ConversationVM.stackptr = 0;
        ConversationVM.result_register = -1;
        ConversationVM.StackValues[VarAddr] = Var;
        ConversationVM.StackValues[ValueAddr] = (short)value;
        ConversationVM.Push(VarAddr);     // arg2, the variable
        ConversationVM.Push(ValueAddr);   // arg1, the value
        ConversationVM.Push(2);           // the argument count
        ConversationVM.x_traps();
        return ConversationVM.result_register;
    }

    public override Task Act(ScenarioContext c)
    {
        bool uw2 = UWClass._RES == UWClass.GAME_UW2;
        _max = uw2 ? 0x3FF : 0x3F;
        var saved = (ConversationVM.StackValues, ConversationVM.stack, ConversationVM.stackptr, ConversationVM.result_register);

        int varWas = playerdat.GetGameVariable(Var);
        playerdat.SetGameVariable(Var, 3);
        _overResult = Call(_max + 1);
        _overVar = playerdat.GetGameVariable(Var);

        Call(_max);
        _topVar = playerdat.GetGameVariable(Var);
        _topExpected = uw2 ? (_max & 0xFF) : _max;   // UW2 stores a byte: (unsigned char)... (uw2 TRIGGER.C:173)

        _fiveResult = Call(5);
        _fiveVar = playerdat.GetGameVariable(Var);

        (ConversationVM.StackValues, ConversationVM.stack, ConversationVM.stackptr, ConversationVM.result_register) = saved;
        playerdat.SetGameVariable(Var, varWas);
        c.Note($"value 0x{_max + 1:X}: var {_overVar}, returned {_overResult}; value 0x{_max:X}: var {_topVar}; value 5: var {_fiveVar}, returned {_fiveResult}");
        return Task.CompletedTask;
    }

    public override string Assert(ScenarioContext c)
    {
        var k = new Checks();
        k.Equal($"game variable {Var} after x_traps with value 0x{_max + 1:X} (out of range)", 3, _overVar, "val >= 0 && val <= max, else no set: BABLHACK.C");
        k.Equal($"x_traps' result with value 0x{_max + 1:X}", 3, _overResult, "returns the variable: BABLHACK.C");
        k.Equal($"game variable {Var} after x_traps with value 0x{_max:X} (top of range)", _topExpected, _topVar, "the bound is inclusive: BABLHACK.C");
        k.Equal($"game variable {Var} after x_traps with value 5", 5, _fiveVar, "BABLHACK.C");
        k.Equal("x_traps' result with value 5", 5, _fiveResult, "BABLHACK.C");
        return k.Result;
    }
}
