
using System.Diagnostics;

namespace Underworld
{
    /// <summary>
    /// Specific code for the tomb
    /// </summary>
    public class tomb : UWClass
    {
        /// <summary>
        /// Handles killing of all undead in Loths tomb, and destroying the floating skulls, on arriving in the Tombs once Loth
        /// has been laid to rest (genocide(0xFF) from do_level_hacks, uw2 GAMEWRAP.C:495-497, WORLDEV.C:1686-1698)
        /// </summary>
        /// <param name="raceparam"></param>
        public static void KillLothsLiches(int raceparam)
        {
            Debug.Print("kill all the liches");
            for (int i = 0; i < UWTileMap.current_tilemap.NoOfActiveMobiles; i++)
            {
                var critterindex = UWTileMap.current_tilemap.GetActiveMobileAtIndex(i);
                var obj = UWTileMap.current_tilemap.LevelObjects[critterindex];
                if (obj.majorclass == 1)
                {//npc
                    if (npc.CheckIfMatchingRaceUW2(obj, raceparam))
                    {
                        if (npc.KillCritter(obj))
                        {
                            i--;//reduce index as list count has changed.
                        }
                    }
                }
                else
                {
                    if (raceparam == -1)
                    {
                        if (obj.item_id == 0x13)
                        {//a floating skull is destroyed (destroy_floatskull from genocide(0xFF), uw2 WORLDEV.C:1694)
                            SpellCasting.SmiteUndeadObject(obj);
                        }
                    }
                }
            }
        }
    }//end class
}//end namespace