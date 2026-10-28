using System.Diagnostics;

namespace Underworld
{
    public partial class scd:UWClass
    {
        /// <summary>
        /// SCD event 4, ev_set_qbit: sets bit (quest &amp; 3) of the 32 bit quest variable quest &gt;&gt; 2 to the row's value
        /// (uw2 SCDEVENT.C:220-230). The quest is row byte 5 and the value row byte 6 (uw2 event.h:36-38).
        /// </summary>
        public static int ChangeQuest(byte[] currentblock, int eventOffset)
        {
            Debug.Print ("Change Quest in SCD");
            PrintRow(
                currentblock: currentblock, 
                eventOffset: eventOffset);
            var quest = currentblock[eventOffset + 5];
            var value = currentblock[eventOffset + 6];
            var bit = quest & 3;
            var offset = 0x67 + ((quest >> 2) * 4);//player->quests[quest >> 2], as GetQuest addresses them
            var questvar = playerdat.GetAt32(offset);
            questvar = (questvar & ~(1 << bit)) + (value << bit);
            playerdat.SetAt32(offset, questvar);
            return 0;
        }
    }//end class
}//end namespace
