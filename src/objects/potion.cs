namespace Underworld
{
    public class potion : objectInstance
    {
        public static bool Use(uwObject ObjectUsed, bool WorldObject)
        {
            if (!WorldObject)
            {
                QuaffPotion(ObjectUsed, true);
                return true;
            }
            return false;
        }


        public static void QuaffPotion(uwObject potion, bool UsedFromInventory)
        {
            uimanager.AddToMessageScroll(GameStrings.GetString(1, GameStrings.str_you_quaff_the_potion_in_one_gulp_));
            //play drinking sound.
            UWsoundeffects.PlaySoundEffectAtAvatar(0x1E, 0x40, 0);
            uwObject[] objList;

            MagicEnchantment spell;
            if (UsedFromInventory)
            {
                spell = MagicEnchantment.GetSpellEnchantment(potion, playerdat.InventoryObjects);
                objList = playerdat.InventoryObjects;
            }
            else
            {
                spell = MagicEnchantment.GetSpellEnchantment(potion, UWTileMap.current_tilemap.LevelObjects);
                objList = UWTileMap.current_tilemap.LevelObjects;
            }

            if (spell != null)
            {
                if (_RES == GAME_UW2)
                {
                    // UW2's UseFood clears nextSpellTime either side of checkSpell, so a
                    // potion casts with no wait and leaves none (uw2 USEITEMS.C:349-351).
                    // UW1's UseFood calls checkSpell with how 1 and keeps the wait, and the
                    // potion is drunk either way (uw1 USEITEMS.C:714-716).
                    SpellCasting.ClearObjectSpellWait();
                    MagicEnchantment.CastObjectSpell(potion, spell);
                    SpellCasting.ClearObjectSpellWait();
                }
                else
                {
                    MagicEnchantment.CastObjectSpell(potion, spell);
                }
            }
            else
            {//no spell was attached. See if a damage trap is present.
                if (potion.is_quant==0)
                {
                    if (potion.link!=0)
                    {                       
                        var dmgtrap = objectsearch.FindMatchInObjectChain(
                            ListHeadIndex: potion.link, 
                            majorclass: 6, 
                            minorclass: 0, 
                            classindex: 0, 
                            objList: objList);
                        if (dmgtrap!=null)
                        {
                            use.UseTriggerHasBeenActivated = true;//stop later infinite loop
                            a_damage_trap.ApplyDamageTrap(triggeringCharacter: playerdat.playerObject, Poison: dmgtrap.owner>0, basedamage: dmgtrap.quality);
                        }
                    }
                }
            }
            ObjectCreator.Consume(potion, UsedFromInventory);
        }

    }//end class
}//end namespace