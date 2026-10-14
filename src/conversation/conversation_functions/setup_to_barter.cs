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
            //at most 0x28 objects are looked at (uw1 BARTER.C:133, uw2 BARTER.C:100)
            while ((nextObj!=0) && (objectcount++ < 0x28))
            {
                bool skip = false;
                var obj = UWTileMap.current_tilemap.LevelObjects[nextObj];
                nextObj = obj.next;//before the object leaves the chain
                //Skip the first weapon, anything worthless and, once every slot is full, a random 5 in 8 of the rest.
                //The draw is made only when the first two tests let the item through (uw1 BARTER.C:137-140, uw2 BARTER.C:104-107).
                if ((obj.OneF0Class==0) && (!skippedprimaryweapon))
                {
                    skippedprimaryweapon = true;
                    skip = true;
                }
                else if (commonObjDat.monetaryvalue(obj.item_id)==0)
                {
                    skip = true;
                }
                else if ((LargelootList) && (Rng.RandMod(8) < 5))
                {//(rand() & 7) < 5
                    skip = true;
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