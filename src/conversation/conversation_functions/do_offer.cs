using System.Collections;
namespace Underworld
{
    public partial class ConversationVM : UWClass
    {
        public static IEnumerator do_offer(uwObject talker)
        {
            //Get strings ids from the stack
            var YeahWeTrade = GetConvoStackValueAtPtr(stack + stackptr - 5);//Guess            
            var INoLike = GetConvoStackValueAtPtr(stack + stackptr - 4);//Trade is not enough
            var YouThinkMeStupid = GetConvoStackValueAtPtr(stack + stackptr - 3);//really bad offer made
            var ImTiredOfThis = GetConvoStackValueAtPtr(stack + stackptr - 2); //trade patience ran out

            var YouMakeNoSense = GetConvoStackValueAtPtr(stack + stackptr - 1);//no items offered


            if (TradePatience < 0)
            {
                yield return say_op(ImTiredOfThis);
                result_register = 0;
                yield return 0;
            }
            else
            {
                if (IsPlayerOfferingItems() && IsNPCOfferingItems())
                {
                    //the NPC values the player's items with its likes and dislikes, its own without (uw1 BARTER.C:648-657, uw2 BARTER.C:611-620)
                    var playervalue = TotalOffering(useLikes: true, playerSide: true, accuracy: NPCAppraisalAccuracy);
                    var npcvalue = TotalOffering(useLikes: false, playerSide: false, accuracy: NPCAppraisalAccuracy);
                    var evaluation = TradeGain(playervalue, npcvalue);
                    if (evaluation >= TradeThreshold)
                    {//accept offer
                        yield return say_op(YeahWeTrade);
                        //the NPC's unselected items go back to its inventory, it takes the player's selected items,
                        //and its selected items stay in its slots for the player to take (uw1 BARTER.C:658-663, uw2 BARTER.C:621-626)
                        ReturnTradeObjectsToNPC(talker, OnlyUnselected: true);
                        TradeObjectsToNPC(talker);
                        TradeResult = 1;
                        result_register = 1;
                    }
                    else
                    {
                        //trade is not good enough. Check if it impacts on npc patience, in 16 bits (uw1 BARTER.C:665-676, uw2 BARTER.C:628-637).
                        //A first offer under half of greed is not good enough; in UW1 a first offer of half of greed or more is not tested further.
                        //Then an offer worse than the last is insulting, and one that closed too little of the gap is not good enough.
                        var firstoffer = PreviousEvaluation == 0 && (_RES != GAME_UW2 || (short)(evaluation * 2) < TradeThreshold);
                        if (firstoffer)
                        {
                            if ((short)(evaluation * 2) < TradeThreshold)
                            {
                                yield return say_op(INoLike);
                                TradePatience--;
                            }
                        }
                        else if (evaluation < PreviousEvaluation)
                        {
                            yield return say_op(YouThinkMeStupid);
                            TradePatience -= 2;
                        }
                        else if ((short)((TradeThreshold - PreviousEvaluation) * 3) / 2 > (short)(TradeThreshold - evaluation))
                        {
                            yield return say_op(INoLike);
                            TradePatience--;
                        }
                        result_register = 0;
                        //Store evaluation for next time
                        PreviousEvaluation = evaluation;
                    }
                }
                else
                {//no items offered
                    result_register = 0;
                    TradeResult = 0;
                    yield return say_op(YouMakeNoSense);
                }
            }
            yield return 0;
        }
    }//end class
}//end namespace