using System.Diagnostics;

namespace Underworld
{
    public partial class ConversationVM : UWClass
    {
        /// <summary>
        /// The NPC hands over the object with this index, if it is in its inventory.
        /// Returns 0 when the player's hand is full or the NPC does not have it; otherwise as NpcBarterGive.
        /// (npc_barter_give_id, uw1 BARTER.C:991-1024, uw2 BARTER.C:971-1004)
        /// </summary>
        public static void take_id_from_npc(uwObject talker)
        {
            var id = GetConvoStackValueAtPtr(stack + stackptr-1);
            Debug.Print($"Take ID {id}");
            result_register = 0;
            if (playerdat.ObjectInHand != -1)
            {
                return;
            }
            var nextItem = talker.link;
            var previous = talker;
            while (nextItem != 0)
            {
                var obj = UWTileMap.current_tilemap.LevelObjects[nextItem];
                if (obj.index == id)
                {
                    UnlinkFromNPC(talker, previous, obj);
                    result_register = NpcBarterGive(talker, obj);
                    return;
                }
                previous = obj;
                nextItem = obj.next;
            }
        }

    }//end class
}//end namespace
