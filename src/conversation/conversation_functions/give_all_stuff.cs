namespace Underworld
{
    public partial class ConversationVM : UWClass
    {
        /// <summary>
        /// The NPC gives the player everything in its trade slots: every filled slot is selected and left there for the
        /// player to take, as after an accepted offer. Returns 0 when its slots are empty.
        /// (give_all_stuff, uw2 BARTER.C:1076-1088)
        /// </summary>
        public static void give_all_stuff(uwObject talker)
        {
            result_register = 0;//default case if nothing gets moved.
            for (int s = 0; s < uimanager.NoOfTradeSlots; s++)
            {
                if (uimanager.GetNPCTradeSlot(slotno: s, OnlySelected: false) != -1)
                {//mark the slot as selected
                    uimanager.NPCTradeOn(s);
                    result_register = 1;//at least one item to give
                }
            }
            if (result_register == 0)
            {
                return;
            }
            ReturnTradeObjectsToNPC(talker, OnlyUnselected: true);
            TradeResult = 1;
        }
    }//end class
}//end namespace
