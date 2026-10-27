using System.Diagnostics;

namespace Underworld
{
    public partial class ConversationVM:UWClass
    {
        /// <summary>
        /// The NPC hands over its first item of type arg1 (or, from 1000, of class arg1 - 1000).
        /// Returns 0 when the player's hand is full or the NPC has none; otherwise as NpcBarterGive.
        /// (npc_barter_give, uw1 BARTER.C:951-987, uw2 BARTER.C:931-968)
        /// </summary>
        public static void take_from_npc(uwObject talker)
        {
            var arg1 = GetConvoStackValueAtPtr(stack + stackptr-1);
            Debug.Print($"take_from_npc({arg1})");
            result_register = 0;
            if (playerdat.ObjectInHand != -1)
            {
                return;
            }

            if (talker.LootSpawnedFlag==0)
            {
                npc.spawnloot(talker);
            }

            var nextItem = talker.link;
            var previous = talker;
            while (nextItem!=0)
            {
                var obj = UWTileMap.current_tilemap.LevelObjects[nextItem];
                bool match = false;
                if (arg1>999)
                {
                    if (obj.OneF0Class == arg1 - 1000)
                    {
                        match = true;
                    }
                }
                else
                {
                    if (arg1 == obj.item_id)
                    {
                        match = true;
                    }
                }
                
                if(match)
                {
                    UnlinkFromNPC(talker, previous, obj);
                    result_register = NpcBarterGive(talker, obj);
                    return;
                }
                previous = obj;
                nextItem = obj.next;
            }
        }

        /// <summary>
        /// Takes obj, which follows previous (the NPC itself for the first item), out of the NPC's inventory.
        /// </summary>
        static void UnlinkFromNPC(uwObject talker, uwObject previous, uwObject obj)
        {
            if (talker.link == obj.index)
            {
                talker.link = obj.next;
            }
            else
            {
                previous.next = obj.next;
            }
            obj.next = 0;
        }

        /// <summary>
        /// Gives the player an object taken from the NPC: into the hand if the player can carry it, else into a free
        /// player trade slot, unselected, else onto the floor near the NPC with near_mob_put_at(talker, obj, 5, 0).
        /// Returns 1, or 2 when it was put near the NPC, or 3 when even that failed and the object was freed.
        /// (the second half of npc_barter_give and npc_barter_give_id, uw1 BARTER.C:966-986, 1002-1022, uw2 BARTER.C:946-966, 982-1002)
        /// </summary>
        static int NpcBarterGive(uwObject talker, uwObject obj)
        {
            if (playerdat.CanCarryWeight(obj))
            {
                playerdat.ObjectInHand = obj.index;
                uimanager.instance.mousecursor.SetCursorToObject(obj.item_id);
                return 1;
            }
            for (int i = 0; i < uimanager.NoOfTradeSlots; i++)
            {
                if (uimanager.GetPlayerTradeSlot(i, false) == -1)
                {
                    uimanager.SetPlayerTradeSlot(slotno: i, item_index: obj.index, selected: false);
                    return 1;
                }
            }
            if (motion.NearMobPutAt(talker, obj, 5, false))
            {
                return 2;
            }
            return 3;
        }
    }   //end class
}//end namespace
