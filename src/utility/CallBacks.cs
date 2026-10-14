using System.Collections.Generic;
using System.Diagnostics;

namespace Underworld
{
    /// <summary>
    /// For call back area effect code/events that take place over a range of tiles, list of objects
    /// </summary>
    /// EG Flagging trespass, homing dart targeting, theft
    public class CallBacks : UWClass
    {

        /// <summary>
        /// A function that runs in an area of effect.
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <param name="obj"></param>
        /// <param name="tile"></param>
        /// <param name="srcIndex"></param>
        /// <returns></returns>
        public delegate bool AreaEffectCallBack(int x, int y, uwObject obj, TileInfo tile, int srcIndex);
        public delegate bool UWObjectCallBackWithoutParams(uwObject obj);
        public delegate void UWObjectCallBackWithParams(uwObject obj, int[] paramsarray);

        /// <summary>
        /// A function that will run after a cutscene is finished. Eg Display victory screen, reset UI, set up main menu after splash screen.
        /// </summary>
        public delegate void CutsceneCallBack();

        public static uwObject CodeCenterObject;

        public static void RunCodeOnTargetsAroundObject(AreaEffectCallBack methodToCall, int CentreObject, int rngProbablity, int targetType, int distanceFromObject, int tileRadius)
        {
            int heading;
            int tileX; int tileY;

                CodeCenterObject = UWTileMap.current_tilemap.LevelObjects[CentreObject];
                 
                if (CodeCenterObject.IsStatic)
                {
                    //static object
                    heading = CodeCenterObject.heading << 5;
                    tileX = CodeCenterObject.tileX; tileY = CodeCenterObject.tileY;
                }
                else
                {
                    //mobile object
                    heading = (CodeCenterObject.heading << 5) + CodeCenterObject.npc_heading;
                    tileX = CodeCenterObject.npc_xhome; tileY = CodeCenterObject.npc_yhome;
                }

                motion.GetCoordinateInDirection(heading, distanceFromObject, ref tileX, ref tileY);

                //No test of the centre: an area whose centre is off the map is clipped to the map by RunCodeOnTargetsInArea, as process_area does. uw1 SPELLS.C:428-450, 509, uw2 SPELLS.C:614-636, 696
                RunCodeOnTargetsInArea(
                    methodToCall: methodToCall,
                    Rng_arg0: rngProbablity,
                    srcItemIndex: CentreObject,
                    typeOfTargetArg8: targetType,
                    tileX0: tileX - tileRadius,
                    tileY0: tileY - tileRadius,
                    xWidth: 1 + (tileRadius << 1),
                    yHeight: 1 + (tileRadius << 1));//radius * 2 + 1. uw1 SPELLS.C:509, uw2 SPELLS.C:696
        }

        /// <summary>
        /// Finds objects in area around tile and runs a function against them
        /// </summary>
        /// <param name="methodToCall"></param>
        /// <param name="Rng_arg0"></param>
        /// <param name="srcItemIndex"></param>
        /// <param name="typeOfTargetArg8"></param>
        /// <param name="tileX0"></param>
        /// <param name="tileY0"></param>
        /// <param name="xWidth"></param>
        /// <param name="yHeight"></param>
        public static void RunCodeOnTargetsInArea(AreaEffectCallBack methodToCall, int Rng_arg0, int srcItemIndex, int typeOfTargetArg8, int tileX0, int tileY0, int xWidth, int yHeight)
        {//process_area, uw1 SPELLS.C:416-483, uw2 SPELLS.C:602-670
            int varE = 0;
            if (
                (tileX0 < 64) && (tileY0 < 64)
                &&
                (tileX0 + xWidth >= 0) && (tileY0 + yHeight >= 0)
                )
            {
                if (tileX0 >= 0)
                {
                    if (tileX0 + xWidth >= 64)
                    {
                        xWidth = xWidth - (tileX0 + xWidth - 64);
                    }
                }
                else
                {//tilex0<0
                    xWidth = xWidth + tileX0;
                    tileX0 = 0;
                }

                if (tileY0 >= 0)
                {
                    if (tileY0 + yHeight > 64)//> here where x has >=. uw1 SPELLS.C:444, uw2 SPELLS.C:630
                    {
                        yHeight = yHeight - (tileY0 + yHeight - 64);
                    }
                }
                else
                {//tilex0<0
                    yHeight = yHeight + tileY0;
                    tileY0 = 0;
                }
                Debug.Print($"Running code on area {tileX0},{tileY0} to {tileX0 + xWidth},{tileY0 + yHeight}");
                if (xWidth > 0 && yHeight > 0)
                {
                    do
                    {
                        for (int di_X = tileX0; di_X <= tileX0 + xWidth; di_X++)
                        {
                            for (int si_Y = tileY0; si_Y <= tileY0 + yHeight; si_Y++)
                            {
                                //Debug.Print($"testing tile {di_X} {si_Y}");
                                if (UWTileMap.ValidTile(di_X, si_Y))
                                {
                                    var currenttile = UWTileMap.current_tilemap.Tiles[di_X, si_Y];
                                    //Random open squares (0x40). UW2 also visits every open square for 0x80, drawing rand() first; UW1 does not.
                                    //uw1 SPELLS.C:463-468, uw2 SPELLS.C:649-655
                                    if ((typeOfTargetArg8 == 0x40) || ((typeOfTargetArg8 == 0x80) && (_RES == GAME_UW2)))
                                    {
                                        if (currenttile.tileType > 0)
                                        {
                                            if ((Rng.r.Next(0, 3 + xWidth * yHeight) < Rng_arg0) || (typeOfTargetArg8 == 0x80))
                                            {
                                                if (methodToCall(di_X, si_Y, null, currenttile, srcItemIndex))
                                                {
                                                    Rng_arg0--;
                                                    if (Rng_arg0 == 0)
                                                    {
                                                        return;
                                                    }
                                                }
                                            }
                                        }
                                    }
                                    if (typeOfTargetArg8 != 0x40)
                                    {
                                        var listIndex = currenttile.indexObjectList;
                                        while (listIndex != 0)
                                        {
                                            var nextObj = UWTileMap.current_tilemap.LevelObjects[listIndex];
                                            if (
                                                (typeOfTargetArg8 == 0x80)
                                                ||
                                                (
                                                    (typeOfTargetArg8 == 0)
                                                    &&
                                                    (nextObj.majorclass == 1)
                                                    &&
                                                    (nextObj.index != srcItemIndex)
                                                )
                                                ||
                                                (typeOfTargetArg8 == 0xC0)
                                            )
                                            {
                                                if (nextObj.majorclass != 7)//this is not vanilla but I don't think these functions are meant to target animos.
                                                {
                                                    if (methodToCall(di_X, si_Y, nextObj, currenttile, srcItemIndex))
                                                    {
                                                        Rng_arg0--;
                                                        if (Rng_arg0 <= 0)//<= for objects, == for squares. uw1 SPELLS.C:476, uw2 SPELLS.C:663
                                                        {
                                                            return;
                                                        }
                                                    }
                                                }
                                            }
                                            listIndex = nextObj.next;
                                        }//while loop
                                    }
                                }
                            }
                        }
                    }
                    while ((typeOfTargetArg8 == 0x40) && (Rng_arg0 > 0) && (varE++ < 4));//up to five passes for random squares. uw1 SPELLS.C:482, uw2 SPELLS.C:669
                }
            }
        }

        /// <summary>
        /// Calls method on each object in the chain starting at first obj
        /// </summary>
        /// <param name="methodToCall"></param>
        /// <param name="obj"></param>
        /// <param name="objList"></param>
        /// <returns></returns>
        public static bool RunCodeOnObjectsInChain(UWObjectCallBackWithoutParams methodToCall, uwObject obj, uwObject[] objList, bool RunOnLinks = true)
        {
            if (obj == null)
            {
                return false;
            }
            else
            {
                while (obj != null)
                {
                    if (methodToCall(obj))
                    {
                        return true;
                    }
                    else
                    {
                        //check the link on the object
                        if ((obj.is_quant == 0 && obj.link > 0) && (RunOnLinks))
                        {
                            var linkedObject = objList[obj.link];
                            if (RunCodeOnObjectsInChain(methodToCall, linkedObject, objList))
                            {
                                return true;
                            }
                            else
                            {
                                //fall down and get next
                            }
                        }
                    }

                    //get next
                    if (obj.next != 0)
                    {
                        obj = objList[obj.next];
                    }
                    else
                    {
                        return false;
                    }
                }//end while
                return false;
            }
        }


        /// <summary>
        /// Runs a function on all matching NPCs that have the whoami
        /// </summary>
        /// <param name="methodToCall"></param>
        /// <param name="whoami"></param>
        /// <param name="paramsArray"></param>
        /// <param name="loopAll"></param>
        public static void RunCodeOnNPCS_WhoAmI(UWObjectCallBackWithParams methodToCall, int whoami, int[] paramsArray, bool loopAll)
        {
            for (int i = 0; i < UWTileMap.current_tilemap.NoOfActiveMobiles; i++)
            {
                var index = UWTileMap.current_tilemap.GetActiveMobileAtIndex(i);
                if ((index != 0) && (index < 256))
                {
                    var obj = UWTileMap.current_tilemap.LevelObjects[index];
                    if (obj.majorclass == 1)
                    {
                        if (obj.npc_whoami == whoami)
                        {
                            methodToCall(
                                obj: obj,
                                paramsarray: paramsArray);
                            if (!loopAll)
                            {
                                return;
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Runs a function on all matching npcs of a race defined by their critter data
        /// </summary>
        /// <param name="methodToCall"></param>
        /// <param name="race"></param>
        /// <param name="paramsArray"></param>
        public static void RunCodeOnRace(UWObjectCallBackWithParams methodToCall, int race, int[] paramsArray, bool loopAll)
        {
            var activeMobiles = new List<int>();
            for (var o = 0; o < UWTileMap.current_tilemap.NoOfActiveMobiles; o++)
            {//construct a list of the mobiles first before processing as this code may cause the NoOfActiveMobiles to change.
                activeMobiles.Add(o);
            }
            foreach (var i in activeMobiles)
            {
                var index = UWTileMap.current_tilemap.GetActiveMobileAtIndex(i);
                if ((index != 0) && (index < 256))
                {
                    var obj = UWTileMap.current_tilemap.LevelObjects[index];
                    if (obj.majorclass == 1)//NPC
                    {
                        if (critterObjectDat.faction(obj.item_id) == race)
                        {
                            methodToCall(obj, paramsArray);
                            if (!loopAll)
                            {
                                return;
                            }
                        }
                    }
                }
            }
        }


        /// <summary>
        /// Runs a function on all NPCS in the map
        /// </summary>
        /// <param name="methodToCall"></param>
        /// <param name="paramsArray"></param>
        public static void RunCodeOnAllNPCS(UWObjectCallBackWithParams methodToCall, int[] paramsArray)
        {
            var activeMobiles = new List<int>();
            for (var o = 0; o < UWTileMap.current_tilemap.NoOfActiveMobiles; o++)
            {//construct a list of the mobiles first before processing as this code may cause the NoOfActiveMobiles to change.
                activeMobiles.Add(o);
            }
            foreach (var i in activeMobiles)
            {
                var index = UWTileMap.current_tilemap.GetActiveMobileAtIndex(i);
                if ((index != 0) && (index < 256))
                {
                    var obj = UWTileMap.current_tilemap.LevelObjects[index];
                    if (obj.majorclass == 1)//NPC
                    {
                        methodToCall(obj, paramsArray);
                    }
                }
            }
        }


        public static void RunCodeOnAllMobiles(UWObjectCallBackWithParams methodToCall, int[] paramsArray)
        {
            var activeMobiles = new List<int>();
            for (var o = 0; o < UWTileMap.current_tilemap.NoOfActiveMobiles; o++)
            {//construct a list of the mobiles first before processing as this code may cause the NoOfActiveMobiles to change.
                activeMobiles.Add(o);
            }
            foreach (var i in activeMobiles)
            {
                var index = UWTileMap.current_tilemap.GetActiveMobileAtIndex(i);
                if ((index != 0) && (index < 256))
                {
                    var obj = UWTileMap.current_tilemap.LevelObjects[index];
                    methodToCall(obj, paramsArray);
                }
            }
        }


        /// <summary>
        /// Runs a function on a specific object.
        /// </summary>
        /// <param name="methodToCall"></param>
        /// <param name="obj"></param>
        /// <param name="paramsArray"></param>
        public static void RunCodeOnObject(UWObjectCallBackWithParams methodToCall, uwObject obj, int[] paramsArray)
        {
            methodToCall(obj, paramsArray);
        }

        public static void RunCodeOnObject(UWObjectCallBackWithoutParams methodToCall, uwObject obj)
        {
            methodToCall(obj);
        }

    }//end class
}//end namespace