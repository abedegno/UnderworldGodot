namespace Underworld
{
    public class doorkey : objectInstance
    {
        public static bool Use(uwObject obj, bool WorldObject)
        {
            if (!WorldObject)
            {
                //flag we are using the key
                useon.CurrentItemBeingUsed = new useon(obj, WorldObject);
                //print use message
                uimanager.AddToMessageScroll(GameStrings.GetString(1, 7));
            }
            return true;
        }

        public static bool LookAt(uwObject obj , bool WorldObject)
        {
            if (WorldObject)
            {
                uimanager.AddToMessageScroll(GameStrings.GetObjectNounUW(obj.item_id));
            }
            else
            {
                uimanager.AddToMessageScroll(GameStrings.GetString(5, obj.owner + 100));
            }
            return true;
        }

        /// <summary>
        /// Using a key: checkLock (door.CharacterDoorLockAndKeyInteraction) with the key's owner, the number of the lock
        /// it fits, and the message checkLock's answer + 2: "The key does not fit.", "There is no lock on that.", "The key
        /// locks the lock.", "The key unlocks the lock.", "That is already open." Unlocking leaves the door shut
        /// (uw1 USEITEMS.C:251-263; uw2 USEITEMS.C:561-575).
        /// </summary>
        public static bool UseOn(uwObject KeyObject, uwObject targetObject)
        {
            var result = door.CharacterDoorLockAndKeyInteraction(null, targetObject, KeyObject.owner);
            uimanager.AddToMessageScroll(GameStrings.GetString(1, result + 2));
            return true;
        }
    }// end class
}//end namespace