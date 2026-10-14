using System.Diagnostics;

namespace Underworld
{
    //Common data and code for item trading.
    public partial class ConversationVM : UWClass
    {

        /// <summary>
        /// The trade evaluation that has to be met to allow the NPC to trade. 
        /// Calculated using random offset and CritterDat[0xEh] bits 0-3
        /// RNG is seeded by critter item_id
        /// </summary>
        public static int TradeThreshold = 0;

        /// <summary>
        /// How much patience the NPC has for bad or insulting trade offers. 
        /// </summary>
        public static int TradePatience = 0;


        /// <summary>
        /// Measure of how good this NPC is at appraising the value of a trade item.
        /// </summary>
        public static int NPCAppraisalAccuracy = 0;

        /// <summary>
        /// Score of the previous trade evaluation. Used to determine if a better or worse offer has been made on repeated offers
        /// </summary>
        public static int PreviousEvaluation = 0;

        /// <summary>
		/// Pointer to Array in the stack that contains the items the npc likes
		/// </summary>
		public static int Likes = 0;

        /// <summary>
        /// Pointer to the array in the stack that contains the items the npc dislikes
        /// </summary>
        public static int Dislikes = 0;

        /// <summary>
        /// 1 when the last offer or demand was accepted (or give_all_stuff gave), so the player may take the NPC's items
        /// from its trade slots (barter_result, uw1 BARTER.C:92, 356, uw2 BARTER.C:63, 317).
        /// </summary>
        public static int TradeResult = 0;

        /// <summary>
        /// This is a bonus applied by a bablhack in uw2. 
        /// However it is bugged in vanilla since it makes the npc items more valuable and only applies on do_judgement and does not actually help the player make better trades!
        /// </summary>
        public static int BablTradeBonus = 0;

        /// <summary>
        /// Get the total value of the players trade area (selected items)
        /// </summary>
        /// <param name="ApplyLikeDislike"></param>
        /// <param name="appraise_accuracy"></param>
        /// <returns></returns>
        static int GetNPCTradeValue(bool ApplyLikeDislike = false, int appraise_accuracy = 0, bool applyAccuracy = false)
        {
            var result = 0;
            for (int i = 0; i < uimanager.NoOfTradeSlots; i++)
            {
                //if slot select get monetary value, possibly adjusted for likes dislike and player appraisal score
                var index = uimanager.GetNPCTradeSlot(i);
                if (index != -1)
                {
                    var obj = UWTileMap.current_tilemap.LevelObjects[index];
                    result += GetTradeValueOfItem(
                        ApplyLikeDislike: ApplyLikeDislike,
                        appraise_accuracy: appraise_accuracy,
                        applyAccuracy: applyAccuracy,
                        obj: obj);
                }
            }
            return result;
        }


        /// <summary>
        /// Get the total value of the PC trade area (selected items)
        /// </summary>
        /// <param name="ApplyLikeDislike"></param>
        /// <param name="appraise_accuracy"></param>
        /// <param name="applyAccuracy"></param>
        /// <returns></returns>
        static int GetPCTradeValue(bool ApplyLikeDislike = false, int appraise_accuracy = 0, bool applyAccuracy = false)
        {
            var result = 0;
            for (int i = 0; i < uimanager.NoOfTradeSlots; i++)
            {
                //if slot select get monetary value, possibly adjusted for likes dislike and player appraisal score
                var index = uimanager.GetPlayerTradeSlot(i);
                if (index != -1)
                {
                    var obj = UWTileMap.current_tilemap.LevelObjects[index];
                    result += GetTradeValueOfItem(
                        ApplyLikeDislike: ApplyLikeDislike,
                        appraise_accuracy: appraise_accuracy,
                        applyAccuracy: applyAccuracy,
                        obj: obj);
                }
            }
            return result;
        }

        // private static int GetTrueItemValue(bool ApplyLikeDislike, int appraise_accuracy, bool applyAccuracy, int index)
        // {
        //     if (index != -1)
        //     {
        //         var obj = UWTileMap.current_tilemap.LevelObjects[index];
        //         if (obj != null)
        //         {
        //             //obj.ObjectQuantity;
        //             var itemvalue = commonObjDat.monetaryvalue(obj.item_id);
        //             if (ApplyLikeDislike)
        //             {//todo
        //                 itemvalue = itemvalue * 1;
        //             }
        //             itemvalue = itemvalue * obj.ObjectQuantity;
        //             var quality = obj.quality;
        //             //if coin quality = 0x40
        //             if (
        //                 (obj.item_id == 160)
        //                 ||
        //                 ((_RES != GAME_UW2) && (obj.item_id == 161))
        //                 )
        //             {
        //                 quality = 0x3F;
        //             }

        //             itemvalue = (itemvalue * quality) >> 6;
        //             if (applyAccuracy)
        //             {
        //                 //Rng.r = new System.Random(obj.item_id);//RNG is always re-seeded with item_id before getting offset here
        //                 Rng.Seed = (uint)obj.item_id;
        //                 itemvalue = Rng.RandomOffset(itemvalue, -appraise_accuracy, +appraise_accuracy);
        //             }
        //             return itemvalue;
        //         }
        //     }

        //     return 0;
        // }


        /// <summary>
        /// Gets the value of the item trading, account for likes/dislikes and appraisal accuracy
        /// </summary>
        /// <param name="obj"></param>
        /// <param name="ApplyLikeDislike"></param>
        /// <param name="appraise_accuracy"></param>
        /// <param name="applyAccuracy"></param>
        /// <returns></returns>
        static int GetTradeValueOfItem(uwObject obj, bool ApplyLikeDislike = false, int appraise_accuracy = 0, bool applyAccuracy = false)
        {
            float multiplier = 1f;
            int itemvalue = 0;
            var itemCategory = obj.item_id >> 4;

            var quality = obj.quality;
            if (((obj.item_id == 160) || (obj.item_id == 161)) && (_RES != GAME_UW2))
            {
                quality = 63;
            }
            if (((obj.item_id == 160)) && (_RES == GAME_UW2))
            {
                quality = 63;
            }

            if (ApplyLikeDislike)
            {
                //return the value adjusted for the npc likes dislikes.
                if (Likes != 0)
                {//loop the stack starting at Likes until value 0xFF is hit.
                 //if value is >=1000 then liked is a class of object
                 //if value < 1000 then liked is an exact object match
                 //if liked value is 1.5 times
                 //itemvalue = (commonObjDat.monetaryvalue(obj.item_id) / 3) * 2;
                 //Debug.Print($"Likes are {Likes}");                   

                    var arrayindex = Likes;
                    var entry = at(arrayindex);
                    while (entry != -1)
                    {
                        if (entry >= 1000)
                        {
                            if (itemCategory == entry - 1000)
                            {
                                multiplier = 1.5f;
                                Debug.Print($"NPC likes item category of {GameStrings.GetSimpleObjectNameUW(obj.item_id)}");
                            }
                        }
                        else
                        {

                            if (entry == obj.item_id)
                            {
                                multiplier = 1.5f;
                                Debug.Print($"NPC likes item {GameStrings.GetSimpleObjectNameUW(obj.item_id)}");
                            }
                        }
                        arrayindex++;
                        entry = at(arrayindex);
                    }
                }
                if (Dislikes != 0)
                {
                    //if unliked no value
                    //itemvalue = 0;
                    Debug.Print($"Dislikes are {Dislikes}");

                    var arrayindex = Dislikes;
                    var entry = at(arrayindex);
                    while (entry != -1)
                    {
                        if (entry >= 1000)
                        {
                            if (itemCategory == entry - 1000)
                            {
                                multiplier = 0f;
                                Debug.Print($"NPC dislikes item category of {GameStrings.GetSimpleObjectNameUW(obj.item_id)}");
                                return 0;
                            }
                        }
                        else
                        {
                            if (obj.item_id == entry)
                            {
                                multiplier = 0f;
                                Debug.Print($"NPC dislikes item {GameStrings.GetSimpleObjectNameUW(obj.item_id)}");
                                return 0;
                            }
                        }
                        arrayindex++;
                        entry = at(arrayindex);
                    }
                }

                itemvalue = (int)(commonObjDat.monetaryvalue(obj.item_id) * multiplier);
                itemvalue = (itemvalue * quality) >> 6;
            }
            else
            {
                //return the true base value adjusted for quality.
                itemvalue = commonObjDat.monetaryvalue(obj.item_id);
                itemvalue = (itemvalue * quality) >> 6;
            }

            //Adjust the value of the item based on the players/npcs appraisal accuracy score
            var offset = Rng.RandomOffset(itemvalue, -appraise_accuracy, +appraise_accuracy);
            return itemvalue;
        }

        /// <summary>
        /// Check if at least one item in the player trade is selected
        /// </summary>
        /// <returns></returns>
        static bool IsPlayerOfferingItems()
        {
            for (int i = 0; i < uimanager.NoOfTradeSlots; i++)
            {
                if (uimanager.GetPlayerTradeSlot(i) != -1)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Check if at least one item in the npc trade is selected
        /// </summary>
        /// <returns></returns>
        static bool IsNPCOfferingItems()
        {
            for (int i = 0; i < uimanager.NoOfTradeSlots; i++)
            {
                if (uimanager.GetNPCTradeSlot(i) != -1)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Creates arrays of the selected items and their item indices
        /// </summary>
        /// <param name="itemIds"></param>
        /// <param name="itemdIndices"></param>
        /// <returns>No of items</returns>
        static int GetPlayerSelectedTradeItems(out int[] itemIds, out int[] itemdIndices)
        {
            int count = 0;
            itemIds = new int[uimanager.NoOfTradeSlots];
            itemdIndices = new int[uimanager.NoOfTradeSlots];

            for (int i = 0; i < uimanager.NoOfTradeSlots; i++)
            {
                var item = uimanager.GetPlayerTradeSlot(i);
                if (item != -1)
                {
                    var obj = UWTileMap.current_tilemap.LevelObjects[item];
                    itemIds[count] = obj.item_id;
                    itemdIndices[count] = item;
                    count++;
                }
            }
            return count;
        }


        /// <summary>
        /// Gives the item at the specified index from the trade area to the npc, adding coins to a pile it already has
        /// (player_barter_give, uw1 BARTER.C:898-915, uw2 BARTER.C:878-895)
        /// </summary>
        /// <param name="talker"></param>
        /// <param name="itemindex"></param>
        public static void GiveItemIndexToNPC(uwObject talker, int itemindex)
        {
            for (int i = 0; i < uimanager.NoOfTradeSlots; i++)
            {
                var x = uimanager.GetPlayerTradeSlot(i, false);
                if (x != -1)
                {
                    var obj = UWTileMap.current_tilemap.LevelObjects[x];
                    if (obj.index == itemindex)
                    {
                        NpcInvAdd(talker, obj);
                        uimanager.SetPlayerTradeSlot(i, -1, false);
                        return;
                    }
                }
            }
        }

        /// <summary>
        /// Puts the items in the NPC's trade slots back in its inventory: all of them, or only those not selected.
        /// setup_to_barter takes them out of the inventory while they are in the slots.
        /// (ReturnTradeObjectsToNPC, uw1 BARTER.C:544-565, uw2 BARTER.C:507-528)
        /// </summary>
        public static void ReturnTradeObjectsToNPC(uwObject talker, bool OnlyUnselected)
        {
            for (int i = 0; i < uimanager.NoOfTradeSlots; i++)
            {
                var objindex = uimanager.GetNPCTradeSlot(i, false);
                if (objindex != -1)
                {
                    if (!OnlyUnselected || uimanager.GetNPCTradeSlot(i, true) == -1)
                    {
                        //insert at head of npcs object list
                        var obj = UWTileMap.current_tilemap.LevelObjects[objindex];
                        obj.next = talker.link;
                        talker.link = obj.index;
                        uimanager.SetNPCTradeSlot(i, -1, false);
                    }
                }
            }
        }

        /// <summary>
        /// On an accepted offer the NPC takes each selected item of the player's that it does not dislike, adding coins to a pile
        /// it already has as npc_inv_add does, and the item leaves its slot. A disliked item stays in the slot.
        /// UW1 adds the rest at the head of the NPC's inventory, UW2 at the end.
        /// The NPC's selected items stay in its slots for the player to take.
        /// (probablyTradeObjects, uw1 BARTER.C:567-602, uw2 BARTER.C:530-565)
        /// </summary>
        /// <param name="talker"></param>
        private static void TradeObjectsToNPC(uwObject talker)
        {
            for (int i = 0; i < uimanager.NoOfTradeSlots; i++)
            {
                var objindex = uimanager.GetPlayerTradeSlot(i);
                if (objindex != -1)
                {
                    var tradedobject = UWTileMap.current_tilemap.LevelObjects[objindex];
                    if (DoesNpcLike(tradedobject) != -1)
                    {
                        NpcInvAdd(talker, tradedobject, AddAtEnd: _RES == GAME_UW2);
                        uimanager.SetPlayerTradeSlot(i, -1, false);
                    }
                }
            }
            uimanager.UpdateInventoryDisplay();
        }

        /// <summary>
        /// 1 when the NPC likes the object, -1 when it dislikes it or it is worthless, else 0.
        /// A list entry of 1000 or more names a class, (item_id >> 4) + 1000. An exact like wins at once;
        /// otherwise a dislike, exact or by class, beats a like by class.
        /// (does_npc_like, uw1 BARTER.C:1083-1109, uw2 BARTER.C:1046-1072)
        /// </summary>
        static int DoesNpcLike(uwObject obj)
        {
            if (commonObjDat.monetaryvalue(obj.item_id) == 0)
            {
                return -1;
            }
            var category = (obj.item_id >> 4) + 1000;
            var liked = 0;
            if (Likes != 0)
            {
                for (var i = Likes; at(i) > -1; i++)
                {
                    if (at(i) < 1000)
                    {
                        if (at(i) == obj.item_id) { return 1; }
                    }
                    else if (at(i) == category) { liked = 1; }
                }
            }
            if (Dislikes != 0)
            {
                for (var i = Dislikes; at(i) > -1; i++)
                {
                    if (at(i) < 1000)
                    {
                        if (at(i) == obj.item_id) { return -1; }
                    }
                    else if (at(i) == category) { liked = -1; }
                }
            }
            return liked;
        }

    } //end class
}//end namespace