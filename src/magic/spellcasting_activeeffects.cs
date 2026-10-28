namespace Underworld
{
    public  partial class SpellCasting : UWClass
    {
/// <summary>
        /// Applies player active status effects: set_curmagic (uw1 PLAYTIME.C:219-247, uw2 PLAYTIME.C:246-274).
        /// </summary>
        /// <param name="major"></param>
        /// <param name="minor"></param>
        /// <param name="stabilityclass"></param>
        /// <returns>false when all three slots are taken and the spell is refused</returns>
        static bool PlayerActiveStatusEffectSpells(int major, int minor, int stabilityclass)
        {//updates the player status active effects. The actual impact of the spell is processed by PlayerRefreshUpdates()
            if (playerdat.ActiveSpellEffectCount < 3)
            {
                var stability = 0;
                //calc stability, in duration checks (uw1 PLAYTIME.C:227-241, uw2 PLAYTIME.C:254-268)
                switch (stabilityclass)
                {
                    case 0x80:
                        stability = Rng.DiceRoll(3, 20) + 24; break;
                    case 0x40:
                        stability = Rng.DiceRoll(2, 8) + 6; break;
                    case 1:
                        stability = 1; break;
                    case 0:
                        stability = Rng.DiceRoll(2, 3); break;
                    default:
                        stability = 0; break;
                }

                //apply effect to player data
                playerdat.SetSpellEffect(
                    index: playerdat.ActiveSpellEffectCount,
                    effectid: (minor << 4) + major,
                    stability: stability);
                playerdat.ActiveSpellEffectCount++;
                return true;
            }
            else
            {
                return false; //no more effects allowed
            }
        }
    }//end class
}//end namespace