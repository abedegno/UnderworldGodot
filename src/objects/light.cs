namespace Underworld
{
    public class light: objectInstance
    {
        public static bool Use(uwObject obj, bool WorldObject)
        {
            if (WorldObject) { return false; }

            if (IsUsedUp(obj))
            {//refused before the light is moved or toggled either way (uw1 USEITEMS.C:522-525, uw2 USEITEMS.C:620-624)
                uimanager.AddToMessageScroll(GameStrings.GetString(1, GameStrings.str_that_light_is_already_used_up_));
                return true;
            }

            if (uimanager.CurrentSlot >= 5 && uimanager.CurrentSlot <= 8)
            {
                if (ToggleLight(obj))
                {
                    if (_RES == GAME_UW2)
                    {
                        UWsoundeffects.PlaySoundEffectAtAvatar(UWsoundeffects.SoundEffectLight,0x40,0);
                    }                    
                }
            }
            else if (obj.classindex >= 4)
            {//a lit light is not one of the items that are moved, so it is put out where it is (uw1 USEITEMS.C:532-533, 555-556; uw2 USEITEMS.C:633-634, 659-660)
                LightOff(obj);
            }
            else
            {
                var freeslot = -1;
                //try and find a free slot to move the lightsource to.
                for (int i = 5; i <= 8; i++)
                {
                    if (playerdat.GetInventorySlotListHead(i) == 0)
                    {
                        freeslot = i;
                        break;
                    }
                }
                if (freeslot != -1)
                {
                    //move from slot by picking it up and dropping it.
                    if (obj.ObjectQuantity==1)
                    {
                        uimanager.PickupObjectFromSlot(obj);      
                    }
                    else
                    {
                        //pick up a single instance
                        obj.link--;
                        var newObjIndex = ObjectCreator.SpawnObjectInHand(obj.item_id); //spawning in hand is very handy here
                        var cloneObj = UWTileMap.current_tilemap.LevelObjects[newObjIndex];
                        cloneObj.link = 1;
                        cloneObj.quality = obj.quality;
                        cloneObj.owner = obj.owner;
                        playerdat.ObjectInHand = cloneObj.index;
                    }
                                  
                    var newObj = UWTileMap.current_tilemap.LevelObjects[playerdat.ObjectInHand];
                    if (LightOn(newObj)) //assuming light starts in an off state
                    {
                        if (_RES == GAME_UW2)
                        {//only UW2 plays a sound, and only when a light is lit (uw2 USEITEMS.C:659-664; uw1 USEITEMS.C:555-558 has none)
                            UWsoundeffects.PlaySoundEffectAtAvatar(UWsoundeffects.SoundEffectLight,0x40,0);
                        }
                    }
                    uimanager.CurrentSlot = freeslot;
                    uimanager.PickupToEmptySlot(playerdat.ObjectInHand);
                }
                else
                {//no free light slot (uw1 USEITEMS.C:544-547, uw2 USEITEMS.C:648-651)
                    uimanager.AddToMessageScroll(GameStrings.GetString(1, GameStrings.str_your_hands_are_full_));
                }
            }

            uimanager.UpdateInventoryDisplay();
            playerdat.PlayerStatusUpdate();
            return true;
        }

        /// <summary>
        /// Return true if light is now on.
        /// </summary>
        /// <param name="obj"></param>
        /// <returns></returns>
        public static bool ToggleLight(uwObject obj)
        {
            //turn light on or off.
            if (obj.classindex <= 3)
            {
                return LightOn(obj);
            }
            else
            {
                LightOff(obj);
                return false;
            }
        }

        public static void LightOff(uwObject obj)
        {
            if (obj.classindex >= 4)
            {
                obj.item_id -= 4;
            }
        }

        /// <summary>
        /// A used-up light will not light: quality 0 in UW1 (uw1 USEITEMS.C:522-525), 1 or less in UW2 (uw2 USEITEMS.C:620-624)
        /// </summary>
        public static bool IsUsedUp(uwObject obj)
        {
            return (_RES == GAME_UW2) ? (obj.quality <= 1) : (obj.quality == 0);
        }

        /// <summary>
        /// Return true if the light was lit.
        /// </summary>
        public static bool LightOn(uwObject obj)
        {
            if (obj.classindex <= 3)
            {
                if (!IsUsedUp(obj))
                {
                    obj.item_id += 4;
                    return true;
                }
                else
                {
                    uimanager.AddToMessageScroll(GameStrings.GetString(1,GameStrings.str_that_light_is_already_used_up_));
                }                
            }
            return false;
        }


        
    }//end class
}//end namespace