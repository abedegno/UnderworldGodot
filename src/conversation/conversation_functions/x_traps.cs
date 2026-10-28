using System.Diagnostics;

namespace Underworld
{
    public partial class ConversationVM:UWClass
    {
        /// <summary>
        /// x_traps(arg2 variable, arg1 value): sets a game variable when the value is in range and returns the variable's value.
        /// UW1 sets game_vars[var] when the value is 0 to 0x3F (uw1 BABLHACK.C:116-128).
        /// UW2 calls set_numbered_variable(var, 2, val) when the value is 0 to 0x3FF (uw2 BABLHACK.C:435-446).
        /// </summary>
        public static void x_traps()
        {
            Debug.Print($"X_traps {GetConvoStackValueAtPtr(stack + stackptr-1)},{GetConvoStackValueAtPtr(stack + stackptr-2)}");

            var di_arg1 = GetConvoStackValueAtPtr(stack + stackptr-2);//the variable
            var si_arg0 = GetConvoStackValueAtPtr(stack + stackptr-1);//the value

            if (_RES != GAME_UW2)
            {
                if ((si_arg0 >= 0) && (si_arg0 <= 0x3F))
                {
                    playerdat.SetGameVariable(di_arg1, si_arg0);
                }
                result_register = playerdat.GetGameVariable(di_arg1);
                return;
            }

            if ((si_arg0 >= 0) && (si_arg0 <= 0x3FF))
            {
                //sets variable di to value in si
                a_set_variable_trap.VariableOperationUW2(
                    LeftVariable: di_arg1, 
                    Operation: 2, 
                    RightVariable: si_arg0);
            }

            result_register = a_check_variable_trap.GetVarQuestOrClockValue(di_arg1);
        }
    }//end class
}//end namespace
