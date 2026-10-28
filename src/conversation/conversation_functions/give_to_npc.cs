namespace Underworld
{
    public partial class ConversationVM : UWClass
    {
        /// <summary>
        /// give_to_npc(count, indices): gives the listed objects to the NPC only if every one is among the player's selected trade items,
        /// each trade item matching once; otherwise gives nothing and returns 0 (uw1 CONVERSE.C:671-691, uw2 CONVERSE.C:657-676)
        /// </summary>
        public static void give_to_npc(uwObject talker)
        {
            var CountItems_arg1 = GetConvoStackValueAtPtr(stack + stackptr - 2);//how many items to give
            var ItemIndicesToGive_Array = at(stack + stackptr - 1); // array of object indices to give, as find_barter_total returns them

            //Get the player inventory
            var itemcount = GetPlayerSelectedTradeItems(out int[] itemIds, out int[] itemIndices);
            if (itemcount < CountItems_arg1)
            {//not enough items selected.
                result_register = 0;
                return;
            }
            //every listed object must be a selected trade item, and no trade item may be used twice
            var used = new bool[itemcount];
            for (int s = 0; s < CountItems_arg1; s++)
            {
                var itemIndexToFind = at(ItemIndicesToGive_Array + s);
                bool itemFound = false;
                for (int i = 0; i < itemcount && !itemFound; i++)
                {
                    if (itemIndices[i] == itemIndexToFind && !used[i])
                    {
                        used[i] = true;
                        itemFound = true;
                    }
                }
                if (!itemFound)
                {
                    result_register = 0;
                    return;
                }
            }
            for (int s = 0; s < CountItems_arg1; s++)
            {
                GiveItemIndexToNPC(talker, at(ItemIndicesToGive_Array + s));
            }
            result_register = 1;
        }
    }//end class
}//end namespace