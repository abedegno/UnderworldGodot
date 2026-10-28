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
        /// The game clock before which the player may not cast from another object,
        /// nextSpellTime in both games (uw1 USEITEMS.C:70, uw2 OBJUSE.C:39). DOS keeps it in a
        /// global, not in PLAYER.DAT, and reset_times zeroes it on a restore or a death
        /// (uw1 UWEDIT.C:375, uw2 UWEDIT.C:359). Separate from the runic cast interval.
        /// </summary>
        static int NextObjectSpellClock_nextSpellTime = 0;

        /// <summary>
        /// How long checkSpell makes the player wait after casting from an object:
        /// nextSpellTime = game_clock + 0x2FD (uw1 USEITEMS.C:1063, uw2 OBJUSE.C:367).
        /// </summary>
        public const int ObjectSpellInterval = 0x2FD;

        /// <summary>
        /// checkSpell's wait for a use by the player (how nonzero), uw1 USEITEMS.C:1058-1064,
        /// uw2 OBJUSE.C:361-368. While game_clock &lt; nextSpellTime the cast is refused with
        /// effect 0x15 at pan 0x40, and in UW2 string 0xB, "You are not yet ready to cast
        /// another spell."; the caller then casts nothing and spends no charge. Otherwise the
        /// wait starts again from now. Both values are 32 bit and the comparison unsigned.
        /// </summary>
        public static bool ObjectSpellWaitHasElapsed()
        {
            uint now = (uint)playerdat.ClockValue;
            uint next = (uint)NextObjectSpellClock_nextSpellTime;
            if (next > (long)now + ObjectSpellInterval)
            {
                // A stamp DOS could not reach: it never sets one more than 0x2FD ahead, and
                // reset_times zeroes it on a restore or a death. The port's static outlives a
                // game, so loading an earlier save would leave it in the future. Treat it as
                // the zero DOS would hold, as RunicMagic.SpellCastIntervalHasElapsed does.
                next = 0;
            }
            if (now < next)
            {
                UWsoundeffects.PlaySoundEffectAtAvatar(effectno: 0x15, pan: 0x40, velocityOffset: 0);
                if (_RES == GAME_UW2)
                {
                    uimanager.AddToMessageScroll(GameStrings.GetString(1, 0x0B));
                }
                return false;
            }
            NextObjectSpellClock_nextSpellTime = (int)(now + ObjectSpellInterval);
            return true;
        }

        /// <summary>
        /// nextSpellTime = 0. UW2's UseFood does this either side of its checkSpell, so a spell
        /// in food or a potion is cast with no wait and leaves none behind
        /// (uw2 USEITEMS.C:349-351).
        /// </summary>
        public static void ClearObjectSpellWait()
        {
            NextObjectSpellClock_nextSpellTime = 0;
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