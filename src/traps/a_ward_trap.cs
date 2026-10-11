using System.Diagnostics;

namespace Underworld
{
    public class a_ward_trap : trap
    {
        /// <summary>
        /// A ward trap (and UW1's tell trap) always ends the chain, whether or not it went off
        /// (uw1 TRIGGER.C:403-416, uw2 TRIGGER.C:681-693), so this always returns 0.
        /// </summary>
        public static short Activate(uwObject trapObj, uwObject triggeringCharacter, short triggernext)
        {
            Debug.Print("Ward trap");
            if (triggeringCharacter == null)
            {
                return 0;
            }
            else
            {
                if (trapObj.quality != 0x3F)
                {
                    if (trapObj.quality != triggeringCharacter.classindex)
                    {
                        return 0;
                    }
                }
            }
            
            //run ward
            string direction = GameStrings.GetString(1, 36 + GameStrings.GetDirectionStringToTile(sourceObject: playerdat.playerObject, targetX: triggeringCharacter.tileX, targetY: triggeringCharacter.tileY));
            uimanager.AddToMessageScroll($"{GameStrings.GetString(1,GameStrings.str_your_rune_of_warding_has_been_set_off_)}{direction}");
            a_damage_trap.ApplyDamageTrap(triggeringCharacter: triggeringCharacter, Poison: false, basedamage: 3 + Rng.r.Next(playerdat.Casting));
            return 0;
        }
    }
}