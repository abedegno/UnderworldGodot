using System.Diagnostics;

namespace Underworld
{
    public  partial class SpellCasting : UWClass
    {

        /// <summary>
        /// Casts spells from enchanted equipment. Not all spells will cast from here.
        /// </summary>
        /// <param name="majorclass"></param>
        /// <param name="minorclass"></param>
        /// <param name="TriggeredByInventoryEvent">First time trigger from adding to inventory, used for curse objects initial message in UW2</param>
        public static void CastEnchantedItemSpell(int majorclass, int minorclass, bool TriggeredByInventoryEvent, ref int DamageResistance, ref int StealthBonus, int PaperDollSlot)
        {
            switch (majorclass)
            {
                case 0://lighting spells
                    {
                        if (playerdat.lightlevel < minorclass)
                        {
                            playerdat.lightlevel = minorclass;
                        }
                        if (minorclass == 5)
                        {
                            //apply mono palette for nightvision.
                            Palette.ColourTone = 1;
                        }
                        break;
                    }
                case 1: //Motion abilities
                    {//set relevant bit
                        playerdat.MagicalMotionAbilities = (byte)(playerdat.MagicalMotionAbilities | (1 << (minorclass - 1)));
                        break;
                    }
                case 2: // resistances
                    {//these apply a blanket damage resistance to each body part.
                        if (minorclass > DamageResistance)
                        {
                            DamageResistance = minorclass;
                        }
                        break;
                    }
                case 3://bonuses
                    {
                        CastClass3Enchantment(minorclass, ref StealthBonus);
                        break;
                    }
                case 9://curses
                    {
                        CastClass9_Curse(minorclass, TriggeredByInventoryEvent);
                        break;
                    }
                case 0xB:
                    {
                        CastClassBEnchantment(minorclass); break;
                    }
                case 0xC: //toughness or protection.
                    {   //toughness adds armour to a body part. Protection makes the body part harder to hit
                        //slots 0 to 4 give one location (defence_slot_index), the rings both body and arms; toughness always goes to the first location's armour, once per location (uw2 PLAYDATA.C:249-264, uw1 PLAYDATA.C:271-286)
                        if (PaperDollSlot < 0) { break; }
                        int[] defence_slot_index = new int[] { 3, 0, 1, 2, 2 };
                        int[] slots;
                        if (PaperDollSlot > 4)
                        {
                            slots = new int[] { 0, 1 };
                        }
                        else
                        {
                            slots = new int[] { defence_slot_index[PaperDollSlot] };
                        }
                        for (int i = 0; i < slots.Length; i++)
                        {
                            var toughness = 0;
                            if ((minorclass & 0x8) == 0)
                            {
                                //protection
                                playerdat.LocationalProtectionValues[slots[i]] += (minorclass & 0x7) + 1;
                            }
                            else
                            {
                                //toughness
                                toughness = (minorclass & 0x7) + 1;
                            }
                            playerdat.LocationalArmourValues[slots[0]] += toughness;
                        }
                        break;
                    }
                case 0xD:
                    {
                        if (_RES!=GAME_UW2)
                        {
                            switch(minorclass)
                            {
                                case 4://maze navigation
                                {
                                    playerdat.MazeNavigation = true;
                                    break;
                                }
                            }
                        }
                        break;
                    }
                default:
                    Debug.Print($"Unhandled enchantment {majorclass} {minorclass}");
                    break;
            }
        }

        /// <summary>
        /// Casts appropiate spells from objects
        /// </summary>
        /// <param name="spellno"></param>
        /// <param name="caster"></param>
        public static void CastSpellFromObject(int spellno, uwObject caster)
        {
            if (((spellno & 0xC0) == 0) && (spellno <= RunicMagic.SpellList.GetUpperBound(0)))
            {
                //do a runic table lookup
                var spell = RunicMagic.SpellList[spellno];
                RunicMagic.CastRunicSpellWithoutRules(spell, caster);
            }
            else
            {
                var major = 12 + ((spellno & 0xC0) >> 6);
                var minor = spellno & 0x3F;
                CastSpell(
                    majorclass: major,
                    minorclass: minor,
                    caster: caster,
                    target: null,
                    tileX: playerdat.playerObject.tileX,
                    tileY: playerdat.playerObject.tileY,
                    CastOnEquip: false);
            }
        }

    }//end class
}//end namespace