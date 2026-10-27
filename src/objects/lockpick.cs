namespace Underworld
{
    public class lockpick : objectInstance
    {

        public static bool Use(uwObject obj, bool WorldObject)
        {
            if (!WorldObject)
            {
                useon.CurrentItemBeingUsed = new useon(obj, WorldObject);
                //print use message
                //8	What lock would you like to try to pick?
                uimanager.AddToMessageScroll(GameStrings.GetString(1, 8));
            }
            return true;
        }

        /// <summary>
        /// Picking a lock: checkLock (door.CharacterDoorLockAndKeyInteraction) with minus the Pick Lock skill in UW1
        /// and minus (Pick Lock + 1) in UW2. It sets off the unlock traps and deletes the lock, or only clears its
        /// locked bit, and leaves the door shut. Only UW2 can break the pick, on a fumble unless a dexterity check
        /// against 20 passes, and with no Pick Lock skill it reports a lock that is not locked as a failed attempt
        /// (uw1 USEITEMS.C:222-249; uw2 USEITEMS.C:525-557).
        /// </summary>
        public static bool UseOn(uwObject LockpickObject, uwObject targetObject)
        {//assumes lockpick is in inventory. door is in world
            int skill;
            if (_RES == GAME_UW2)
            {
                skill = -(playerdat.PickLock + 1);
            }
            else
            {
                skill = -playerdat.PickLock;
            }
            var result = door.CharacterDoorLockAndKeyInteraction(null, targetObject, skill);
            if (_RES == GAME_UW2)
            {
                if (((result == 5) && (playerdat.SkillCheck(playerdat.DEX, 20) > playerdat.SkillCheckResult.Fail)) || ((result == 4) && (skill == -1)))
                {
                    result = 0;
                }
            }
            switch (result)
            {
                case 1:
                    //3 There is no lock on that.
                    uimanager.AddToMessageScroll(GameStrings.GetString(1, 3));
                    break;
                case 5:
                    //UW2 134 You broke your pick.
                    playerdat.RemoveFromInventory(LockpickObject.index, true);
                    uimanager.AddToMessageScroll(GameStrings.GetString(1, 134));
                    break;
                case 0:
                    uimanager.AddToMessageScroll(GameStrings.GetString(1, GameStrings.str_your_lockpicking_attempt_failed_));
                    break;
                case 4:
                    uimanager.AddToMessageScroll(GameStrings.GetString(1, GameStrings.str_that_is_not_locked_));
                    break;
                default:
                    UWsoundeffects.PlaySoundEffectAtAvatar(UWsoundeffects.SoundEffectLockPick, 0x40, 0);
                    uimanager.AddToMessageScroll(GameStrings.GetString(1, GameStrings.str_you_succeed_in_picking_the_lock_));
                    break;
            }
            return true;
        }
    }//end class
}//end namespace