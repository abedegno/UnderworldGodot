namespace Underworld
{
    public partial class ConversationVM : UWClass
    {
        public static void do_decline(uwObject talker)
        {
            //all the NPC's trade items go back to its inventory (uw1 BARTER.C:743, uw2 BARTER.C:720)
            ReturnTradeObjectsToNPC(talker, OnlyUnselected: false);
        }
    }//end class
}//end namespace