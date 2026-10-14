using System.Diagnostics;

namespace Underworld
{
    public partial class ConversationVM : UWClass
    {
        /// <summary>
        /// give_ptr_npc(object, qty): gives the NPC an object from the trade area, or else qty of it from the player's inventory
        /// (uw1 CONVERSE.C:696-716, uw2 CONVERSE.C:680-712)
        /// </summary>
        /// <param name="talker"></param>
        public static void give_ptr_npc(uwObject talker)
        {
            var qty = at(at(stack + stackptr-1));
            var ObjectIndex = at(at(stack + stackptr-2));
            Debug.Print($"{qty} of {ObjectIndex}");
            if (_RES == GAME_UW2 && qty <= 0)
            {//uw2 gives nothing for a qty of 0 or less (uw2 CONVERSE.C:688)
                result_register = 0;
                return;
            }
            //try and find in trade area first. The shipped scripts pass an index from find_barter or show_inv, a level object index like the trade slots' (find_inv's player result is an inventory index, numbered separately in the port)
            var itemcount = GetPlayerSelectedTradeItems(out int[] itemIds, out int[] itemIndices);

            for (int i=0; i<itemcount;i++)
            {
                if (ObjectIndex == itemIndices[i])
                {
                    var tradeObject = UWTileMap.current_tilemap.LevelObjects[ObjectIndex];
                    if (_RES == GAME_UW2 && tradeObject.is_quant == 1 && tradeObject.link < 0x200 && tradeObject.link > qty)
                    {//uw2 gives qty of a stack in the trade area and leaves the rest there (uw2 CONVERSE.C:694-703)
                        var copyIndex = ObjectFreeLists.GetAvailableObjectSlot();
                        if (copyIndex == 0)
                        {
                            result_register = 0;
                            return;
                        }
                        var copy = UWTileMap.current_tilemap.LevelObjects[copyIndex];
                        for (int b = 0; b < 8; b++)
                        {
                            copy.DataBuffer[copy.PTR + b] = tradeObject.DataBuffer[tradeObject.PTR + b];
                        }
                        copy.link = (short)qty;
                        tradeObject.link = (short)(tradeObject.link - qty);
                        NpcInvAdd(talker, copy);
                    }
                    else
                    {
                        GiveItemIndexToNPC(talker, ObjectIndex);
                    }
                    result_register = 1;
                    return;
                }
            }

            //if not found take qty of the object out of the player's inventory, all of it for a negative qty or an object that is not a quantity (uw1 CONVERSE.C:710-715, uw2 CONVERSE.C:706-711)
            if (ObjectIndex <= 0 || ObjectIndex >= playerdat.InventoryObjects.Length || playerdat.InventoryObjects[ObjectIndex] == null)
            {//not carried (invRemoveObject returns 0)
                result_register = 0;
                return;
            }
            var ptrObject = playerdat.InventoryObjects[ObjectIndex];
            if (qty < 0 || ptrObject.is_quant == 0 || ptrObject.link >= 0x200)
            {
                qty = -1;
            }
            if (qty > 0 && ptrObject.link > 1 && qty < ptrObject.link)
            {//split the stack: qty goes to the npc, the rest stays where it is (invRemoveObject and takeFromSlot, uw1 INVDATA.C:251-293 and 341-391)
                var newIndex = playerdat.AddInventoryObjectToWorld(
                    objIndex: ObjectIndex,
                    updateUI: false,
                    RemoveNext: false,
                    DestroyInventoryObject: false);
                if (newIndex == 0)
                {//no free object for the given part
                    result_register = 0;
                    return;
                }
                var given = UWTileMap.current_tilemap.LevelObjects[newIndex];
                given.link = (short)qty;
                ptrObject.link = (short)(ptrObject.link - qty);
                playerdat.WeightCarried -= (short)(commonObjDat.mass(ptrObject.item_id) * qty);
                uimanager.RefreshWeightDisplay();
                NpcInvAdd(talker, given);
            }
            else
            {//give all of it
                var newIndex = playerdat.AddInventoryObjectToWorld(
                    objIndex: ObjectIndex,
                    updateUI: false,
                    RemoveNext: false);
                if (newIndex == 0)
                {//no free object to move it into
                    result_register = 0;
                    return;
                }
                NpcInvAdd(talker, UWTileMap.current_tilemap.LevelObjects[newIndex]);
            }
            uimanager.UpdateInventoryDisplay();
            playerdat.PlayerStatusUpdate();//FixPlayerEquips
            result_register = 1;
        }

        /// <summary>
        /// npc_inv_add: adds an object to the NPC's inventory, adding it to a coin pile the NPC already has when the total stays under 999.
        /// The coin merged is ITEM_GOLD_COIN 0xA1 in uw1 (uw1 BARTER.C:874-894), ITEM_COIN 0xA0 in uw2 (uw2 BARTER.C:854-874).
        /// AddAtEnd adds anything else at the end of the inventory, as uw2's accepted offer does (Obj_AddEnd, uw2 BARTER.C:556).
        /// </summary>
        static void NpcInvAdd(uwObject talker, uwObject obj, bool AddAtEnd = false)
        {
            var objList = UWTileMap.current_tilemap.LevelObjects;
            var coin = _RES == GAME_UW2 ? 0xA0 : 0xA1;
            if (obj.item_id == coin && obj.is_quant == 1 && obj.link < 0x200)
            {
                var next = (int)talker.link;
                for (int guard = 0; next != 0 && guard < 1024; guard++)
                {
                    var other = objList[next];
                    if (other.item_id == obj.item_id && other.is_quant == 1 && other.link < 0x200 && obj.link + other.link < 999)
                    {
                        other.link = (short)(other.link + obj.link);
                        ObjectFreeLists.ReleaseFreeObject(obj);
                        return;
                    }
                    next = other.next;
                }
            }
            if (AddAtEnd && talker.link != 0)
            {
                var last = objList[talker.link];
                while (last.next != 0)
                {
                    last = objList[last.next];
                }
                obj.next = 0;
                last.next = obj.index;
                return;
            }
            obj.next = talker.link;
            talker.link = obj.index;
        }
    }//end class
}//end namespace
