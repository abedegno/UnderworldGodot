using System.Diagnostics;

namespace Underworld
{
    /// <summary>
    /// Some utility code for NPCS
    /// </summary>
    public partial class npc : objectInstance
    {
        /// <summary>
        /// Does some checks that the npc is part of a target race.
        /// </summary>
        /// <param name="critter"></param>
        /// <param name="targetRace"></param>
        /// <returns></returns>
        public static bool CheckIfMatchingRaceUW2(uwObject critter, int targetRace)
        {
            if (critter.majorclass == 1)
            {
                if (targetRace == -1)
                {//special case for undead.
                    var testdam = 1;
                    if (damage.ScaleDamage(critter.item_id, ref testdam, 0x80) == 0)
                    {
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }
                else
                {
                    return (critterObjectDat.faction(critter.item_id) == targetRace);
                }
            }
            else
            {
                if (targetRace == -1)
                {
                    if (critter.item_id == 0xB)//a skull. possibly a special case for UW2 loths tomb
                    {
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }
                else
                {
                    return false;
                }
            }
        }


        static void SetRaceAttitude(int targetrace, int newAttitute)
        {
            for (var tileX = 0; tileX <= 63; tileX++)
            {
                for (var tileY = 0; tileY <= 63; tileY++)
                {
                    if (UWTileMap.ValidTile(tileX, tileY))
                    {
                        var next = UWTileMap.current_tilemap.Tiles[tileX, tileY].indexObjectList;
                        while (next != 0)
                        {
                            var obj = UWTileMap.current_tilemap.LevelObjects[next];
                            if (obj.majorclass == 1)
                            {//npc
                                if (critterObjectDat.faction(obj.item_id) == targetrace)
                                {
                                    obj.npc_attitude = (short)newAttitute;
                                    if (newAttitute == 0)
                                    {
                                        obj.ProjectileSourceID = 0; //clear last hit index
                                    }
                                }
                            }
                            next = obj.next;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// For use with delegates
        /// {target,goal}
        /// </summary>
        /// <param name="critter"></param>
        /// <param name="paramsarray"></param>
        public static void set_goal_and_target_by_array(uwObject critter, int[] paramsarray)
        {
            SetGoalAndGtarg(
                critter: critter,
                goal: paramsarray[1],
                target: paramsarray[0]);
        }

        /// <summary>
        /// For use with delegates
        /// </summary>
        /// <param name="critter"></param>
        /// <param name="paramsarray"></param>
        public static void set_attitude_by_array(uwObject critter, int[] paramsarray)
        {
            set_attitude(critter, paramsarray[0]);
        }

        public static void set_unkABit7(uwObject critter, int[] paramsarray)
        {
            critter.UnkBit_0XA_Bit7 = (short)paramsarray[0];
        }


        /// <summary>
        /// Used by event based attitude changes. Eg avatar going to jail
        /// </summary>
        /// <param name="critter"></param>
        /// <param name="newAttitute"></param>
        public static void set_attitude(uwObject critter, int newAttitute)
        {
            critter.npc_attitude = (short)newAttitute;
            if (newAttitute != 0)
            {
                critter.ProjectileSourceID = 0;
            }
        }

        /// <summary>
        /// teleport_critter (uw2 CRITTIME.C:374-410; UW1 has none, uw1 CRITTIME.C:22). Moves a critter to
        /// tile (x, y): a critter already on that tile stays put and succeeds; otherwise it fails on a solid
        /// tile, where can_place refuses, or if it cannot be taken out of its old tile.
        /// </summary>
        public static bool moveNPCToTile(uwObject critter, int destTileX, int destTileY)
        {
            Debug.Print($"Moving {critter.a_name} to {destTileX},{destTileY} from {critter.tileX}, {critter.tileY}");
            if (UWTileMap.ValidTile(destTileX, destTileY) && UWTileMap.ValidTile(critter.tileX, critter.tileY))
            {
                if (critter.npc_xhome == destTileX && critter.npc_yhome == destTileY)
                {//already there (uw2 CRITTIME.C:384-385)
                    return true;
                }
                var destTile = UWTileMap.current_tilemap.Tiles[destTileX, destTileY];
                int z;
                if (critterObjectDat.isFlier(critter.item_id))
                {//fliers go half way up (uw2 CRITTIME.C:387-390)
                    z = ((destTile.floorHeight << 3) + 0x80) >> 1;
                }
                else
                {
                    z = destTile.floorHeight << 3;
                }
                if (PlaceCritterInTile(critter, destTileX, destTileY, z, critterObjectDat.isFlier(critter.item_id) ? 1 : 0))
                {
                    //Clear some bits relating to AI (uw2 CRITTIME.C:403-406)
                    critter.UnkBit_0x19_4 = 0;
                    critter.UnkBit_0x19_5 = 0;
                    critter.UnkBit_0x19_0_likelyincombat = 0;
                    critter.UnkBit_0x19_1 = 0;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// The placement shared by teleport_critter (uw2 CRITTIME.C:391-402), up_crit (uw1 CRITTIME.C:177-192,
        /// uw2 CRITTIME.C:176-192) and wander_that_monster (uw1 CRITTIME.C:347-358, uw2 CRITTIME.C:349-360):
        /// the fine position from the tile type, refused on a solid tile, then refused unless can_place says the
        /// critter fits at that point at height z, then out of its old tile's list and into the new one.
        /// </summary>
        public static bool PlaceCritterInTile(uwObject critter, int destTileX, int destTileY, int z, int flier)
        {
            if (!UWTileMap.ValidTile(destTileX, destTileY) || !UWTileMap.ValidTile(critter.tileX, critter.tileY))
            {
                return false;
            }
            var sourceTile = UWTileMap.current_tilemap.Tiles[critter.tileX, critter.tileY];
            var destTile = UWTileMap.current_tilemap.Tiles[destTileX, destTileY];
            if (!set_gridx_and_y_based_on_tile_type(destTile.tileType, out int fx, out int fy))
            {
                Debug.Print($"Attempt to move {critter.a_name} to solid tile {destTileX},{destTileY}");
                return false;
            }
            if (!motion.TestIfObjectFitsInTile(critter.item_id, critter.index, (destTileX << 3) + fx, (destTileY << 3) + fy, z, flier, 8))
            {
                return false;
            }
            if (!ObjectRemover_OLD.RemoveObjectFromLinkedList(sourceTile.indexObjectList, critter.index, UWTileMap.current_tilemap.LevelObjects, sourceTile.Ptr + 2))
            {
                return false;
            }
            critter.next = destTile.indexObjectList;
            destTile.indexObjectList = critter.index;
            critter.tileX = destTileX; critter.tileY = destTileY;
            critter.npc_xhome = (short)destTileX;
            critter.npc_yhome = (short)destTileY;
            critter.xpos = (short)fx; critter.ypos = (short)fy;
            critter.zpos = (short)z;
            Reposition(critter);
            return true;
        }

        /// <summary>
        /// set_gridx_and_y_based_on_tile_type (uw1 CRITTIME.C:95-124, uw2 CRITTIME.C:93-122): where in a tile of
        /// this type to put a critter. The middle of an open or sloped tile, the open corner of a diagonal; false
        /// for a solid tile. Type 5 gets 6, 1 like type 2 in both games, as the original does.
        /// </summary>
        public static bool set_gridx_and_y_based_on_tile_type(int tileType, out int x, out int y)
        {
            switch (tileType)
            {
                case UWTileMap.TILE_SOLID:
                    x = 0; y = 0;
                    return false;
                case UWTileMap.TILE_DIAG_SE:
                    x = 6; y = 1; break;
                case UWTileMap.TILE_DIAG_SW:
                    x = 1; y = 1; break;
                case UWTileMap.TILE_DIAG_NE:
                    x = 6; y = 6; break;
                case UWTileMap.TILE_DIAG_NW:
                    x = 6; y = 1; break;
                default:
                    x = 4; y = 4; break;
            }
            return true;
        }

    }//end class
}//end namespace