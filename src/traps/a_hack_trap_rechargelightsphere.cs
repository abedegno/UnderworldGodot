namespace Underworld
{

    /// <summary>
    /// Trap that creates an item in Britannia that suits the player skillset
    /// </summary>
    public class a_hack_trap_rechargelightsphere : trap
    {
        public static void Activate(int triggerX, int triggerY)
        {
            var tile = UWTileMap.current_tilemap.Tiles[triggerX,triggerY];
            if (tile.indexObjectList !=0)
            {
                //Recharge all light spheres in the tile.
                var obj = UWTileMap.current_tilemap.LevelObjects[tile.indexObjectList];
                CallBacks.RunCodeOnObjectsInChain(
                    methodToCall: RechargeLightSphere, 
                    obj: obj, 
                    objList: UWTileMap.current_tilemap.LevelObjects );
                
                //Loop entire chain and see if item is or contains a sphere. If so spawn a lightning animo
                var next = tile.indexObjectList;
                while (next != 0)
                {
                    var match = UWTileMap.current_tilemap.LevelObjects[next];
                    if (objectsearch.HasOrIsObj(match, 0x93, UWTileMap.current_tilemap.LevelObjects))
                    {//the object is either a light sphere or is a container that holds one. (underworld-exhumed uw2/src/event/WORLDEV.C:1598-1600)
                        animo.SpawnAnimoInTile(0xC, match.xpos, match.ypos, match.zpos, triggerX, triggerY);
                    }
                    next = match.next;
                }
            }
        }

        static bool RechargeLightSphere(uwObject obj)
        {
            if (obj.item_id == 0x93)
            {
                obj.quality = 0x3F;
            }
            return false;
        }
    }//end class
}//end namespace
