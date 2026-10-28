using System.Diagnostics;

namespace Underworld
{
    public partial class ConversationVM:UWClass
    {
        public static void x_skills()
        {
            if (_RES!=GAME_UW2)
            {
                var NewValue = at(at(stack + stackptr-1));
                var SkillNo = at(at(stack + stackptr-2));
                Debug.Print($"x_skills {NewValue} {SkillNo}");
                if (NewValue==10000)
                {
                    //advance skill using skill gain logic
                    playerdat.IncreaseSkill(SkillNo);
                }
                else
                {
                    if ((NewValue>=0) && (NewValue<=30))
                    {
                        playerdat.SetSkillValue(SkillNo, NewValue);                        
                    }
                }
                result_register = playerdat.GetSkillValue(SkillNo);
            }
            else
            {
                //uw2 version differs as it takes into account skillpoints
                Debug.Print("UW2 Version of X_Skills");
                var NewValue = at(at(stack + stackptr-1));
                var SkillNo = at(at(stack + stackptr-2));
                Debug.Print($"x_skills {NewValue} {SkillNo}");
                if (NewValue > 10000)
                {
                    if (playerdat.SkillPoints > 0)
                    {
                        var result = playerdat.IncreaseSkillGroup(SkillNo);
                        playerdat.UpdateHPManaMax(false);//grant_skill_advance ends with player_compute(0), which does not fill mana (uw2 SKILLS.C:220)
                        if (result==1)
                        {
                            playerdat.SkillPoints--;
                        }
                        result_register = result;
                    }
                    else
                    {
                        result_register = 0;
                    }
                }
                else
                {
                    if (NewValue != 10000)
                    {
                        if ((NewValue>=0) && (NewValue<=30))
                        {
                            playerdat.SetSkillValue(SkillNo, NewValue);//no recompute (uw2 BABLHACK.C:430-431)
                        }
                    }
                    else
                    {
                        //General skill increase function based on governing stats. 
                        //Uses UW1 implementation from shrines
                        playerdat.IncreaseSkill(SkillNo);//get_skill, no recompute (uw2 BABLHACK.C:424-425)
                    }
                    result_register = playerdat.GetSkillValue(SkillNo);
                }
            }
        }
    } //end class
}//end namespace