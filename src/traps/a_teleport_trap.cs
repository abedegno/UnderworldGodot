namespace Underworld
{

    /// <summary>
    /// Trap that moves player to another tile or to another level
    /// </summary>
    public class a_teleport_trap : trap
    {

        public static int Activate(int character, uwObject trapObj, uwObject[] objList)
        {
            int NewHeadingHeightTeleportFlag = 0;
            if (_RES == GAME_UW2)
            {
                NewHeadingHeightTeleportFlag = trapObj.xpos & 0x1;
                if (((trapObj.xpos & 0x2)>>1) == 1)
                {
                    NewHeadingHeightTeleportFlag = NewHeadingHeightTeleportFlag | ((trapObj.heading + 8)<<2);
                }
                if ((character == 1) && (((trapObj.xpos & 0x4) >> 2) == 1))
                {//the trap loses the player: his map stops filling in and the map screen shows the level he left (uw2 TRIGGER.C:316-321)
                    if (playerdat.AutomapEnabled)
                    {
                        playerdat.MapScrap = playerdat.dungeon_level;
                    }
                    playerdat.AutomapEnabled = false;
                }
            }
                Teleportation.Teleport(
                    character: 0, 
                    tileX: trapObj.quality, 
                    tileY: trapObj.owner, 
                    newLevel: trapObj.zpos, 
                    HeadingHeightFlag: NewHeadingHeightTeleportFlag);            

            uimanager.FlashColour(1, uimanager.Cuts3DWin, 0.1f);
            return trapObj.link;
        }

    }//end class
}//end namespace