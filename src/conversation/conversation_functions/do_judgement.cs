using System.Collections;
using Peaky.Coroutines;

namespace Underworld
{
    public partial class ConversationVM : UWClass
    {

        /// <summary>
        /// Assesses the value of the offered trade.
        /// </summary>
        public static IEnumerator do_judgement()
        {
            int appraisal_certainty;
            int final_evaluation;
            var evaluation = AppraiseOffer(out _, out _);

            switch (playerdat.Appraise)
            {
                case < 6:
                    appraisal_certainty = 0; break;
                case >=6 and <12:
                    appraisal_certainty = 1; break;
                case >=12 and <18:
                    appraisal_certainty = 2; break;
                case >=18 and <24:
                    appraisal_certainty = 3; break;
                case >=24:
                    appraisal_certainty = 4; break;                    
            }          

            switch (evaluation)
            {
                case > 50:
                    final_evaluation =0; break;
                case >35 and <=50:
                    final_evaluation = 1;break;
                case >25 and <=35:
                    final_evaluation = 2;break;
                case >10 and <=25:
                    final_evaluation = 3;break;
                case >-10 and <=10:
                    final_evaluation = 4;break;
                case >-25 and <=-10:
                    final_evaluation = 5;break;
                case >-35 and <=-25:
                    final_evaluation = 6;break;
                case >-50 and <=-35:
                    final_evaluation = 7;break;
                case <=-50:
                    final_evaluation = 8;break;
            }

            string output =
                GameStrings.GetString(7, 3 + appraisal_certainty)
                +
                GameStrings.GetString(7, 2)
                +
                GameStrings.GetString(7, 8 + final_evaluation);

            //Debug.Print(output);
            uimanager.AddToMessageScroll(
                stringToAdd: output, 
                option: PRINT_SAY, 
                mode: MessageDisplay.MessageDisplayMode.TemporaryMessage);   

            while (uimanager.MessageScrollIsTemporary)
            {//ugh
                yield return new WaitForSeconds(0.1f);
            }        
            yield return 0;
        }

        /// <summary>
        /// The player's appraisal of the offer: both sides valued without likes, blurred by 50 - appraise * 1.5 %, the babl_hack
        /// bonus added to the NPC's side (UW2's fudge), and the gain in % (do_judgement, uw1 BARTER.C:757-765, uw2 BARTER.C:735-744).
        /// </summary>
        public static int AppraiseOffer(out int playervalue, out int npcvalue)
        {
            var appraise_accuracy = 50 - ((playerdat.Appraise * 45) / 30); //the variant range +/- that will be used to assess item values.
            playervalue = TotalOffering(useLikes: false, playerSide: true, accuracy: appraise_accuracy);
            npcvalue = (short)(TotalOffering(useLikes: false, playerSide: false, accuracy: appraise_accuracy) + BablTradeBonus);
            return TradeGain(playervalue, npcvalue);
        }
    }//end class
}//end namespace