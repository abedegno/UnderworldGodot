using System.Diagnostics;

namespace Underworld
{
    public partial class ConversationVM:UWClass
    {
        /// <summary>
        /// Selects what items the NPC offers in trade.
        /// </summary>
        public static void setup_to_barter(uwObject talker)
        {
            var skippedprimaryweapon = false;
            if (talker.LootSpawnedFlag == 0)
                {
                    npc.spawnloot(talker);
                }
            var nextObj = talker.link;
            var objectcount = 0;
            var slotindex = 0;
            bool LargelootList = false;
            while (nextObj!=0)
            {
                bool skip = false;
                objectcount++;
                if (objectcount>=40)
                {
                    break;
                }
                var obj = UWTileMap.current_tilemap.LevelObjects[nextObj];
                nextObj = obj.next;//before the object leaves the chain
                if (!skippedprimaryweapon)
                    {
                        if (obj.OneF0Class==0)
                        {   //item is a weapon
                            //The first weapon in the npc inventory is excluded.
                            skippedprimaryweapon = true;
                            skip=true;
                        }
                    }
                if (commonObjDat.monetaryvalue(obj.item_id)==0)
                    {//Items of no monetary value will not be offered by the npc
                        skip = true;
                    }
                if ((Rng.r.Next(0,7)<5) && (LargelootList))
                {//RNG seems to happen when NPC has more inventory than slots. Randomly replace earlier items 
                    skip=true;
                }

                if (!skip)
                {
                    Debug.Print($"Slot{slotindex} {obj.a_name}");
                    //the item leaves the NPC's inventory for the slot, and an item it replaces goes back
                    //(uw1 BARTER.C:143-148, uw2 BARTER.C:111-116). end_barter drops what is left near the NPC.
                    ObjectRemover_OLD.RemoveObjectFromLinkedList(talker.link, obj.index, UWTileMap.current_tilemap.LevelObjects, talker.PTR + 6);
                    var replaced = uimanager.GetNPCTradeSlot(slotindex, false);
                    if (replaced != -1)
                    {
                        var replacedObj = UWTileMap.current_tilemap.LevelObjects[replaced];
                        replacedObj.next = talker.link;
                        talker.link = replacedObj.index;
                    }
                    uimanager.SetNPCTradeSlot(slotindex, obj.index, false);
                    slotindex++;
                    if (slotindex>uimanager.NoOfTradeSlots-1)
                    {
                        LargelootList =true;
                        slotindex=0;
                    }
                }
                else
                {
                    Debug.Print($"Excluding {obj.a_name}");
                }
            }
        }

    }//end class
}//end class