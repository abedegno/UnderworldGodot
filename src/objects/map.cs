namespace Underworld
{

    /// <summary>
    /// The map scroll
    /// </summary>
    public class map : objectInstance
    {
        public static bool Use(uwObject obj, bool WorldObject)
        {
            if (WorldObject) { return false; }
            uimanager.AutomapOpenMusic();
            if (_RES == GAME_UW2)
            {
                var level = playerdat.dungeon_level;
                if (!playerdat.AutomapEnabled)
                {//a lost player sees the level of map_scrap (uw2 AUTOMAP.C:108, 128-131)
                    level = playerdat.MapScrap;
                }
                var worldno = worlds.GetWorldNo(level);
                uimanager.DrawAutoMap(level - 1, worldno);
            }
            else
            {
                uimanager.DrawAutoMap(playerdat.dungeon_level - 1, 0);
            }

            return false;
        }
    }//end class
}//end namespace