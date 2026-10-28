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
        /// The total value of one side's selected items, each valued by AssessValue, summed in 16 bits as DOS's int is
        /// (total_offering, uw1 BARTER.C:788-803, uw2 BARTER.C:767-782).
        /// With useLikes the NPC's likes and dislikes count.
        /// </summary>
        public static int TotalOffering(bool useLikes, bool playerSide, int accuracy)
        {
            short total = 0;
            for (int i = 0; i < uimanager.NoOfTradeSlots; i++)
            {
                var index = playerSide ? uimanager.GetPlayerTradeSlot(i) : uimanager.GetNPCTradeSlot(i);
                if (index > 0)
                {
                    total = (short)(total + AssessValue(useLikes, index, accuracy));
                }
            }
            return total;
        }

        /// <summary>
        /// An object's value to a trade (assess_value, uw1 BARTER.C:810-843, uw2 BARTER.C:789-823).
        /// The class value, with useLikes 0 for an item the NPC dislikes and 1.5 times for one it likes, times the quantity,
        /// times the quality / 64 (at least 1 unless the quality is 0; UW2 counts its coin as quality 63), blurred by up to accuracy %.
        /// The products are 16-bit as Turbo C's int, so a stack or a valuable item can pass 0x7FFF and come out below 0;
        /// UW2 alone multiplies the quality as a long.
        /// The blur is drawn after srand(object index), so an object is valued the same each time; the original then reseeds
        /// from the clock, and here the port's own generator is put back.
        /// </summary>
        public static int AssessValue(bool useLikes, int index, int accuracy)
        {
            var obj = UWTileMap.current_tilemap.LevelObjects[index];
            int value = (short)commonObjDat.monetaryvalue(obj.item_id);
            if (useLikes)
            {
                var disposition = DoesNpcLike(obj);
                if (disposition == -1)
                {
                    return 0;
                }
                if (disposition != 0)
                {
                    value = (short)(value * 3) >> 1;
                }
            }
            value = (short)(value * obj.ObjectQuantity);
            int quality = obj.quality;
            if ((_RES == GAME_UW2) && (obj.item_id == 0xA0))
            {//ITEM_COIN (uw2 BARTER.C:812); UW1 has no coin rule
                quality = 0x3F;
            }
            if (value > 0)
            {
                if (quality > 0)
                {
                    if (_RES == GAME_UW2)
                    {
                        value = (short)(((long)value * quality) >> 6);
                    }
                    else
                    {
                        value = (short)(value * quality) >> 6;
                    }
                    if (value == 0)
                    {
                        value = 1;
                    }
                }
                else
                {
                    value = 0;
                }
            }
            var source = Rng.Source;
            Rng.Source = new BorlandRandSource((uint)index);
            try
            {
                value = Range(value, -accuracy, accuracy);
            }
            finally
            {
                Rng.Source = source;
            }
            return value;
        }

        /// <summary>
        /// basevalue plus a random min to max % of it (max excluded), the product with basevalue in 16 bits
        /// (range, uw1 BARTER.C:846-851, uw2 BARTER.C:826-831).
        /// </summary>
        static int Range(int basevalue, int min, int max)
        {
            return (short)(basevalue + (short)(basevalue * (min + Rng.RandScale(max - min))) / 100);
        }

        /// <summary>
        /// The gain of an offer in %, (player's value - NPC's value) * 100 / NPC's value, or 100 when the NPC's side is worth nothing.
        /// The product is 16-bit, so once the two sides differ by more than 327 it wraps, and a gross overpayment comes out
        /// as a loss (do_offer and do_judgement, uw1 BARTER.C:656-657, 764-765, uw2 BARTER.C:619-620, 743-744).
        /// </summary>
        public static int TradeGain(int playerValue, int npcValue)
        {
            if (npcValue > 0)
            {
                return (short)((playerValue - npcValue) * 100) / npcValue;
            }
            return 100;
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