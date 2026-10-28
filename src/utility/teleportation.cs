using Peaky.Coroutines;
using System.Collections;
using System.Diagnostics;

namespace Underworld
{

    public class Teleportation : UWClass
    {
        //Constants for level change events
        const int EnterLevelMode = 0;
        const int ExitLevelMode = 1;
        const int LoadSaveMode = 2;
        /// <summary>
        /// Function to call when teleporting.
        /// </summary>
        public delegate void TeleportCallBack();
        static int TeleportLevel = -1;//make these private.
        static int TeleportTileX = -1;
        static int TeleportTileY = -1;
        static int HeadingHeightTeleportFlag = -1;//used in uw2 to control teleporting in to levels at the ceiling.


        /// <summary>
        /// A function to call once teleported.
        /// </summary>
        public static TeleportCallBack CodeToRunOnTeleport;


        /// <summary>
        /// Processes a pending teleportation. Run only on game loop update and not during a trap chain.
        /// </summary>
        public static void HandleTeleportation()
        {
            // new_player_pos searches for room round the queued square, after any level change, and
            // if there is none the player is not moved and dies (uw1 UWEDIT.C:466-471, uw2 UWEDIT.C:480-486).
            bool roomFound = true;
            if (TeleportLevel != -1)
            {
                uimanager.WaitingForFade = true;
                uimanager.FlashColour(1, uimanager.CutsSmall, HaltGame:true);
                if (playerdat.ObjectInHand != -1)
                {
                    //handle moving an object in hand through levels. 
                    Debug.Print("Moving an object through a level while holding it! Dropping it there.");
                    var obj = UWTileMap.current_tilemap.LevelObjects[playerdat.ObjectInHand];

                    //drop objects at the player tile so they cannot be taken out of the dream 
                    //TODO. special case for the telekinesis wand in Scintillus.
                    playerdat.ObjectInHand = -1;
                    uimanager.instance.mousecursor.SetCursorToCursor();
                    var tile = UWTileMap.current_tilemap.Tiles[playerdat.playerObject.tileX, playerdat.playerObject.tileY];
                    obj.zpos = (short)(tile.floorHeight << 3);
                    obj.xpos = 3;
                    obj.ypos = 3;
                    obj.next = tile.indexObjectList;
                    tile.indexObjectList = obj.index;
                    pickup.DropSpecialCases(obj.item_id);

                }

                int fromX = playerdat.playerObject.tileX, fromY = playerdat.playerObject.tileY;
                //remove player from the level
                playerdat.PlacePlayerInTile(newTileX: -1, newTileY: -1, previousTileX: playerdat.playerObject.tileX, previousTileY: playerdat.playerObject.tileY);//removes the player from the tile map data


                //move player object to the save file and clear the data out.
                for (int i = 0; i <= 0x1A; i++)
                {
                    playerdat.pdat[playerdat.PlayerObjectStoragePTR + i] = playerdat.playerObject.DataBuffer[playerdat.playerObject.PTR + i];
                    playerdat.playerObject.DataBuffer[playerdat.playerObject.PTR + i] = 0;
                }

                //handle leave level events
                LevelChangeEvents(ExitLevelMode, playerdat.dungeon_level);

                playerdat.dungeon_level = TeleportLevel;

                //switch level
                UWTileMap.LoadTileMap(
                        newLevelNo: playerdat.dungeon_level - 1,
                        datafolder: playerdat.currentfolder,
                        newGameSession: false);

                //copy back the player object
                //copy player object from the save file back into the tilemap objects
                for (int i = 0; i <= 0x1A; i++)
                {
                    playerdat.playerObject.DataBuffer[playerdat.playerObject.PTR + i] = playerdat.pdat[playerdat.PlayerObjectStoragePTR + i];
                }
                
                //Reset the camera reference as the old reference belonged to a previous map
                playerdat.CameraReference = playerdat.playerObject;

                int tileX = TeleportTileX, tileY = TeleportTileY;
                if ((tileX != -1) && (tileY != -1))
                {
                    roomFound = FindRoomForPlayer(ref tileX, ref tileY);
                }
                if (!roomFound)
                {// the original does not call player_setup, so the player keeps his old position, now on this map
                    playerdat.playerObject.tileX = fromX; playerdat.playerObject.tileY = fromY;
                }
                else if ((tileX != -1) && (tileY != -1))
                {
                    if (_RES == GAME_UW2)
                    {
                        //move to new tile on different level
                        InitialisePlayerOnLevelOrPositionChange(tileX: tileX, tileY: tileY, CeilingSpawn: (HeadingHeightTeleportFlag & 0x1) == 1);
                        if ((HeadingHeightTeleportFlag & 0x20) != 0)
                        {
                            HeadingHeightTeleportFlag &= 0xFFDC;
                            playerdat.playerObject.heading = (short)((HeadingHeightTeleportFlag >> 2) & 0x7);
                            playerdat.PlayerCameraYaw_dseg_8294 = (short)(HeadingHeightTeleportFlag << 0xB);
                            motion.PlayerMotionYaw_dseg_67d6_8296 = (short)(HeadingHeightTeleportFlag << 0xB);
                        }
                    }
                    else
                    {
                        //move to new tile on different level
                        InitialisePlayerOnLevelOrPositionChange(tileX: tileX, tileY: tileY, CeilingSpawn: false);
                    }
                }

                //Handle enter level events
                LevelChangeEvents(EnterLevelMode, playerdat.dungeon_level);

                if (_RES == GAME_UW2)
                {//SCD.ARK then needs to be processed in UW2
                    Debug.Print("Processing SCD due to level transition");
                    scd.ProcessSCDArk(1);
                }
            }
            else
            {
                //teleport within a map
                int tileX = TeleportTileX, tileY = TeleportTileY;
                if ((tileX != -1) && (tileY != -1))
                {
                    roomFound = FindRoomForPlayer(ref tileX, ref tileY);
                }
                if ((tileX != -1) && (tileY != -1) && roomFound)
                {
                    uimanager.WaitingForFade = true;
                    uimanager.FlashColour(1, uimanager.CutsSmall, HaltGame:true);
                    //move to new tile
                    if (_RES == GAME_UW2)
                    {
                        InitialisePlayerOnLevelOrPositionChange(tileX: tileX, tileY: tileY, CeilingSpawn: (HeadingHeightTeleportFlag & 0x1) == 1, removefromtile: true);
                        if ((HeadingHeightTeleportFlag & 0x20) != 0)
                        {
                            HeadingHeightTeleportFlag &= 0xFFDC;
                            playerdat.playerObject.heading = (short)((HeadingHeightTeleportFlag >> 2) & 0x7);
                            playerdat.PlayerCameraYaw_dseg_8294 = (short)(HeadingHeightTeleportFlag << 0xB);
                            motion.PlayerMotionYaw_dseg_67d6_8296 = (short)(HeadingHeightTeleportFlag << 0xB);
                        }
                    }
                    else
                    {
                        InitialisePlayerOnLevelOrPositionChange(tileX: tileX, tileY: tileY, CeilingSpawn: false, removefromtile: true);
                    }
                }
            }


            TeleportLevel = -1;
            TeleportTileX = -1;
            TeleportTileY = -1;
            HeadingHeightTeleportFlag = 0;
            if (CodeToRunOnTeleport != null)
            {//handle a callback.
                CodeToRunOnTeleport();
                CodeToRunOnTeleport = null;//although this should be handled by the method itself
            }
            if (!roomFound)
            {// after the callback, as the original runs npp_func before its search
                playerdat.play_hp = 0;
            }
            uimanager.WaitingForFade = false;
        }

        /// <summary>
        /// The search new_player_pos makes before it places the player (uw1 UWEDIT.C:466-471, uw2 UWEDIT.C:480-486):
        /// find_good_x_and_y round the square, and if that finds nothing, again with clear set. A square of 0x3F
        /// with a callback queued is a stand-in, as the callback sets the real square (moveto in the
        /// original, uw1 SKILLS.C:769-784, uw2 SKILLS.C:647-661): the player is parked in the middle
        /// of the map with no search until it runs.
        /// </summary>
        /// <returns>false when there is no room even after clearing</returns>
        static bool FindRoomForPlayer(ref int tileX, ref int tileY)
        {
            if ((tileX == 0x3F || tileY == 0x3F) && CodeToRunOnTeleport != null)
            {
                tileX = 32; tileY = 32;
                return true;
            }
            if (FindGoodXAndY(playerdat.playerObject, tileX, tileY, out int x, out int y)
                || FindGoodXAndY(playerdat.playerObject, tileX, tileY, out x, out y, clear: true))
            {
                tileX = x; tileY = y;
                return true;
            }
            return false;
        }



        /// <summary>
        /// Handles the reseting of the player motion params when teleporting to a new tile, starting a new game or changing levels
        /// </summary>
        /// <param name="tileX"></param>
        /// <param name="tileY"></param>
        /// <param name="CeilingSpawn"></param>
        /// <param name="removefromtile"></param>
        public static void InitialisePlayerOnLevelOrPositionChange(int tileX, int tileY, bool CeilingSpawn, bool removefromtile = false)
        {
            var tile = UWTileMap.current_tilemap.Tiles[tileX, tileY];//place object in tile will refresh player object on next game tick.

            if (removefromtile)
            {
                // Take the player out of the tile they are leaving, as the level change
                // path in HandleTeleportation does. Passing -1 for the previous tile removed
                // nothing, so a teleport within a level left the player in the old tile's
                // object chain as well as the new one. A save then carried the player in
                // the old chain, and DOS UW2 refused to save that game again with
                // "Cantcrunch badobjlist", because it counted the player twice.
                playerdat.PlacePlayerInTile(-1, -1, playerdat.playerObject.tileX, playerdat.playerObject.tileY);
            }

            Loader.setAt(MotionHandler.PlayerMotionHandler.handlerdata, 0, 16, 0);//Loader.setAt(UWMotionParamArray.PlayerMotionHandler_dseg_67d6_26AA, 0, 16, 0);
            motion.playerMotionParams.momentum_14 = 0;
            motion.playerMotionParams.gravity_10_Z = 0;
            motion.playerMotionParams.unk_e_Y = 0;

            motion.playerMotionParams.unk_c_X = 0;
            motion.playerMotionParams.unk_a_pitch = 0;
            motion.playerMotionParams.unk_8_y = 0;

            motion.playerMotionParams.unk_6_x = 0;

            motion.playerMotionParams.x_0 = (short)((tileX << 8) + 0x80);

            motion.playerMotionParams.y_2 = (short)((tileY << 8) + 0x80);
            motion.playerMotionParams.z_4 = (short)(tile.floorHeight * 0x40);

            motion.playerMotionParams.unk_24 = 8;
            motion.playerMotionParams.index_20 = 1;



            
            if ((motion.TileTraversalFlags_dseg_67d6_1BA6[tile.tileType] & 0x20) != 0)
            {
                motion.playerMotionParams.z_4 += 0x20;
            }

            if (CeilingSpawn)
            {
                if (1000 - commonObjDat.height(127) > motion.playerMotionParams.z_4)
                {
                    motion.playerMotionParams.z_4 = (short)(1000 - (commonObjDat.height(127) << 3));
                    motion.playerMotionParams.gravity_10_Z = -4;
                }
            }
            playerdat.playerObject.zpos = (short)(motion.playerMotionParams.z_4 >> 3);
            playerdat.playerObject.xpos = 6;
            playerdat.playerObject.ypos = 6;
            playerdat.playerObject.npc_animation = 1;
            playerdat.playerObject.npc_xhome = (short)tileX;
            playerdat.playerObject.npc_yhome = (short)tileY;
            playerdat.playerObject.tileX = tileX;
            playerdat.playerObject.tileY = tileY;
            playerdat.playerObject.next = 0;

            MotionCalcArray.PtrToMotionCalc = new byte[0x20];

            MotionCalcArray.MotionArrayObjectIndexA = 1;
            MotionCalcArray.Radius8 = (byte)commonObjDat.radius(127);
            MotionCalcArray.Height9 = (byte)commonObjDat.height(127);
            MotionCalcArray.x0 = (ushort)(3 + (tileX << 3));
            MotionCalcArray.y2 = (ushort)(3 + (tileY << 3));
            MotionCalcArray.z4 = (ushort)(motion.playerMotionParams.z_4 >> 3);

            motion.ProcessMotionTileHeights_seg028_2941_385(motion.playerMotionParams.unk_24);
            motion.playerMotionParams.tilestate25 = motion.GetTileState(MotionCalcArray.UnkC_terrain | MotionCalcArray.UnkE);

            motion.ProcessPlayerTileState(motion.playerMotionParams.tilestate25, 0);
            motion.WalkOnSurfaceType();
            motion.PlayerMotionUpdateRequired_dseg_D3 = true;

            playerdat.PlacePlayerInTile(newTileX: tileX, newTileY: tileY, previousTileX: -1, previousTileY: -1);
            


            // playerdat.playerObject.zpos = (short)(tile.floorHeight << 3);
            // playerdat.playerObject.xpos = 3; playerdat.playerObject.ypos = 3;
            // playerdat.tileX = tileX; playerdat.tileY = tileY;

            playerdat.PositionPlayerCamera();

            // main.gamecam.Position = uwObject.GetCoordinate(
            //     tileX: playerdat.tileX,
            //     tileY: playerdat.tileY,
            //     _xpos: playerdat.xpos,
            //     _ypos: playerdat.ypos,
            //     _zpos: playerdat.camerazpos);
        }



        /// <summary>
        /// Handles a request to teleport a charcter. Does not do the final teleport but queues it up for the HandleTeleport function to process.
        /// </summary>
        /// <param name="character"></param>
        /// <param name="tileX"></param>
        /// <param name="tileY"></param>
        /// <param name="newLevel"></param>
        public static int Teleport(int character, int tileX, int tileY, int newLevel, int HeadingHeightFlag)
        {
            if (_RES == GAME_UW2)
            {
                return TeleportUW2(character: character, tileX: tileX, tileY: tileY, newLevel: newLevel, NewHeadingHeightFlag: HeadingHeightFlag);
            }
            else
            {
                return TeleportUW1(character: character, tileX: tileX, tileY: tileY, newLevel: newLevel);
            }
        }

        /// <summary>
        /// Applies specific UW2 rules for teleportation.
        /// </summary>
        /// <param name="character"></param>
        /// <param name="tileX"></param>
        /// <param name="tileY"></param>
        /// <param name="newLevel"></param>
        /// <param name="NewHeadingHeightFlag"></param>
        static int TeleportUW2(int character, int tileX, int tileY, int newLevel, int NewHeadingHeightFlag)
        {
            if (character == 0)
            {
                if (playerdat.DreamPlantCounter > 0)
                {
                    if (playerdat.DreamingInVoid)
                    {
                        if (newLevel != 0)
                        {
                            if (worlds.GetWorldNo(newLevel) != 8)
                            {
                                return 2;
                            }
                        }
                    }
                }
                if (playerdat.GetQuest(112) != 0)//avatar fighting in the castle
                {
                    if (newLevel != 0)
                    {
                        if (newLevel != 1)
                        {
                            ChangeUW2Quests();
                            goto ProcessTeleport;
                        }
                    }
                    if ((tileY != 38) || (tileX < 42) || (tileX > 43))
                    {//when not in tile 38,43?
                        ChangeUW2Quests();
                        goto ProcessTeleport;
                    }
                }

            ProcessTeleport:
                if (newLevel != playerdat.dungeon_level)
                {
                    if (character != 0)
                    {
                        //non PC teleport to a new level. Should not happen.
                        return 2;
                    }
                }

                // A teleport within this level goes to the nearest square round the
                // destination where the player fits, and fails if there is none
                // (uw2 WORLDEV.C:199-205).
                if ((newLevel == 0 || newLevel == playerdat.dungeon_level) && tileX != 0x3F && tileY != 0x3F)
                {
                    if (!FindGoodXAndY(playerdat.playerObject, tileX, tileY, out int goodX, out int goodY))
                    {
                        CodeToRunOnTeleport = null;//no teleport, so nothing to run after it (uw2 WORLDEV.C:228-229)
                        return 2;
                    }
                    tileX = goodX; tileY = goodY;
                }

                //queue up the teleport.
                if (newLevel == 0)
                {
                    TeleportLevel = -1;
                }
                else
                {
                    TeleportLevel = newLevel;
                }
                TeleportTileX = tileX;
                TeleportTileY = tileY;
                HeadingHeightTeleportFlag = NewHeadingHeightFlag;
                //

                if (newLevel != 0)
                {
                    if (newLevel == playerdat.dungeon_level)
                    {
                        TeleportLevel = -1;//same level.
                    }
                }

                bool coward = false;
                if (playerdat.IsFightingInPit)
                {

                    if (newLevel != playerdat.dungeon_level)
                    {
                        //avatar is a coward
                        coward = true;
                    }
                    else
                    {
                        //Check if avatar is a coward by checking if they are still in an arena, when teleporting outside the arenas but still within the level. (eg using a moonstone)
                        Debug.Print("todo check if avatar is a coward and has left the arena!");
                    }
                }
                if (coward)
                {
                    pitsofcarnage.AvatarIsACoward(skipConversation: true);
                }

                return 0x10;
            }
            else
            {
                return 2;
            }
        }



        static void ChangeUW2Quests()
        {
            playerdat.SetQuest(112, 0);//checked when talking to LB. You have been fighting with the others
            playerdat.SetQuest(124, 1);//is referenced in teleport_trap in relation to quest 112 fighting in the castle. 
        }

        static int TeleportUW1(int character, int tileX, int tileY, int newLevel)
        {
            // A teleport within this level goes to the nearest square round the
            // destination where the player fits, and fails if there is none
            // (uw1 WORLDEV.C:183-190).
            if ((newLevel == 0 || newLevel == playerdat.dungeon_level) && tileX != 0x3F && tileY != 0x3F)
            {
                if (!FindGoodXAndY(playerdat.playerObject, tileX, tileY, out int goodX, out int goodY))
                {
                    CodeToRunOnTeleport = null;//no teleport, so nothing to run after it (uw1 WORLDEV.C:183-201)
                    return 2;
                }
                tileX = goodX; tileY = goodY;
            }
            if (newLevel != 0)
            {
                if (newLevel == playerdat.dungeon_level)
                {
                    TeleportLevel = -1;//same level.
                }
                else
                {
                    TeleportLevel = newLevel;
                }
            }
            else
            {
                TeleportLevel = -1;
            }
            TeleportTileX = tileX;
            TeleportTileY = tileY;
            return 2;//do_teleport returns 0x10 here (uw1 WORLDEV.C:199); no caller acts on the value
        }

        /// <summary>
        /// find_good_x_and_y (uw1 WORLDEV.C:76-168, uw2 WORLDEV.C:65-157, the same code in both games).
        /// A breadth-first search outwards from (x, y), at most 20 squares a ring, through the box
        /// xmin..xmin+9, ymin..ymin+9 round it, for the first square where the object fits at the
        /// square's centre (can_place at x * 8 + 3, y * 8 + 3, on the floor, raised by 4 on a slope).
        /// Solid squares are not searched through and diagonals only through their open sides.
        /// With clear set (only new_player_pos sets it, uw1 UWEDIT.C:467, uw2 UWEDIT.C:481) each
        /// square searched first loses the first object with a height, or the first mobile object,
        /// that Obj_Punt is allowed to delete (uw1 WORLDEV.C:114-122, uw2 WORLDEV.C:103-111).
        /// </summary>
        /// <returns>false when there is no such square</returns>
        public static bool FindGoodXAndY(uwObject obj, int x, int y, out int newX, out int newY, bool clear = false)
        {
            newX = x; newY = y;
            int xmin = System.Math.Min(System.Math.Max(1, x - 4), 0x3A);
            int ymin = System.Math.Min(System.Math.Max(1, y - 4), 0x3A);
            int xmax = System.Math.Min(xmin + 9, 0x3E);
            int ymax = System.Math.Min(ymin + 9, 0x3E);
            // The original's visited is uint16[9] and only its first 9 bytes are cleared, so
            // column xmin + 9 writes past it and later columns start with whatever was on the
            // stack. Here every column starts clear.
            var visited = new int[10];
            var cur = new System.Collections.Generic.List<(int, int)> { (x, y) };
            var next = new System.Collections.Generic.List<(int, int)>();
            if (x >= xmin && y >= ymin)
            {// the original marks x 0 or y 0 one place outside the array
                visited[x - xmin] |= 1 << (y - ymin);
            }

            void Visit(int tx, int ty)
            {
                if (next.Count < 20 && tx >= xmin && tx <= xmax && ty >= ymin && ty <= ymax
                    && (visited[tx - xmin] & (1 << (ty - ymin))) == 0)
                {
                    next.Add((tx, ty));
                    visited[tx - xmin] |= 1 << (ty - ymin);
                }
            }

            while (cur.Count > 0)
            {
                foreach (var (tx, ty) in cur)
                {
                    var tile = UWTileMap.current_tilemap.Tiles[tx, ty];
                    if (clear)
                    {
                        var objIndex = tile.indexObjectList;
                        while (objIndex != 0)
                        {
                            var o = UWTileMap.current_tilemap.LevelObjects[objIndex];
                            objIndex = o.next;
                            if (commonObjDat.height(o.item_id) != 0 || o.index < 0x100)//a height, or a mobile object (IsMobElem)
                            {
                                if (ObjectRemover.RemoveObject_experimental(
                                    objectlist: UWTileMap.current_tilemap.LevelObjects,
                                    buffer: UWTileMap.current_tilemap.lev_ark_block.Data,
                                    ListHeadPTR: (int)(tile.Ptr + 2),
                                    objToRemove: o,
                                    ForceCull: false) == null)
                                {
                                    break;// unlinking the deleted object cleared its next, which ends the original's walk
                                }
                            }
                        }
                    }
                    int z = (tile.floorHeight << 3) + (((motion.TileTraversalFlags_dseg_67d6_1BA6[tile.tileType] & 0x20) != 0) ? 4 : 0);
                    if (motion.TestIfObjectFitsInTile(obj.item_id, obj.index, tx * 8 + 3, ty * 8 + 3, z, 0, 8))
                    {
                        newX = tx; newY = ty;
                        return true;
                    }
                    switch (tile.tileType)
                    {
                        case UWTileMap.TILE_SOLID:
                            break;
                        case UWTileMap.TILE_DIAG_SE:
                            Visit(tx + 1, ty); Visit(tx, ty - 1);
                            break;
                        case UWTileMap.TILE_DIAG_SW:
                            Visit(tx - 1, ty); Visit(tx, ty - 1);
                            break;
                        case UWTileMap.TILE_DIAG_NE:
                            Visit(tx + 1, ty); Visit(tx, ty + 1);
                            break;
                        case UWTileMap.TILE_DIAG_NW:
                            Visit(tx - 1, ty); Visit(tx, ty + 1);
                            break;
                        default:
                            Visit(tx - 1, ty); Visit(tx, ty - 1); Visit(tx + 1, ty); Visit(tx, ty + 1);
                            break;
                    }
                }
                (cur, next) = (next, cur);
                next.Clear();
            }
            return false;
        }

        /// <summary>
        /// Moves the player to the position of the moonstone after teleportation using the Gate Travel Spell
        /// </summary>
        public static void JumpToMoonStoneOnLevel()
        {
            //find the moonstone
            var moonstone = objectsearch.FindMatchInFullObjectList(4, 2, 6, UWTileMap.current_tilemap.LevelObjects);
            if (moonstone != null)
            {
                if (UWTileMap.ValidTile(moonstone.tileX, moonstone.tileY))
                {
                    // do_mstone only sets the square; new_player_pos then searches round it as for
                    // any move (uw1 SKILLS.C:809-812, uw2 SKILLS.C:726-729).
                    if (!PlacePlayerNear(moonstone.tileX, moonstone.tileY))
                    {
                        playerdat.play_hp = 0;
                    }
                }
            }
        }

        /// <summary>
        /// For a teleport callback that sets the square itself, as npp_func does in the original: the rest of
        /// new_player_pos, which searches for room round the square and puts the player there
        /// (uw1 UWEDIT.C:466-471, uw2 UWEDIT.C:480-486).
        /// </summary>
        /// <returns>false when there is no room even after clearing; the player is not moved</returns>
        public static bool PlacePlayerNear(int tileX, int tileY)
        {
            if (!FindRoomForPlayer(ref tileX, ref tileY))
            {
                return false;
            }
            InitialisePlayerOnLevelOrPositionChange(tileX, tileY, CeilingSpawn: false, removefromtile: true);
            return true;
        }


        /// <summary>
        /// Handle events that happen when player leaves and enters dungeon levels
        /// </summary>
        /// <param name="mode">0 = new game/enter level, 1 = leave level, 2 = unused and 3 = load save game</param>
        /// <param name="dungeon">Either the new or previous dungeon.</param>
        static void LevelChangeEvents(int mode, int dungeon)
        {
            if (_RES == GAME_UW2)
            {
                LevelChangeEventsUW2(mode, dungeon);
            }
            else
            {
                LevelChangeEventsUW1(mode, dungeon);
            }
        }

        /// <summary>
        /// Handle events that happen when player leaves and enters dungeon levels in UW2
        /// </summary>
        /// <param name="mode">0 = new game/enter level, 1 = leave level, 2 = unused and 3 = load save game</param>
        /// <param name="dungeon">Either the new or previous dungeon.</param>
        static void LevelChangeEventsUW2(int mode, int dungeon)
        {
            if (playerdat.armageddon && mode == EnterLevelMode)
            {//entering a level under the influence of armageddon
                Debug.Print("Armageddon flag has been set. Resetting map!");
                UWTileMap.ResetMap(0);
            }
            else
            {
                if (mode == EnterLevelMode)
                {
                    playerdat.LastDamagedNPCIndex = 0; playerdat.LastDamagedNPCType = -1;
                    CallBacks.RunCodeOnAllMobiles(ClearUnkBit_0x15_7_OnMobiles, null);
                    npc.BitFieldForPathing_dseg_67d6_B4 = 0xFFFF;//clear_paths frees all 16 stored paths. uw2 CRITTIME.C:455
                    if (playerdat.play_drawn == 0)
                    {
                        //reselect level theme music.
                        Debug.Print("Todo Refresh music themes");
                    }
                    if (dungeon == 1)
                    {
                        playerdat.playerObject.ProjectileSourceID = 0;
                        Debug.Print($"Reentering britannia. make sure door at {TeleportTileX},{TeleportTileY} is set to closed if moving");
                    }
                }
                else
                {
                    if (mode == ExitLevelMode)
                    {
                        //Calm NPCs?
                        //Debug.Print("'Calm NPCS'");
                        uwObject.ResetMobileObjects();
                    }
                }

                //World specific events
                switch ((worlds.world_ids)worlds.GetWorldNo(dungeon))
                {
                    case worlds.world_ids.PrisonTower:
                        {
                            if (mode == ExitLevelMode)
                            {
                                //do a check to see if player has been fighting goblins and set quest 60 is that is the case.
                                prisontower.PrisonTowerQuest60();

                                if (dungeon == 10)
                                {
                                    trigger.TriggerTrapInTile(0x1F, 0x24);
                                    trigger.TriggerTrapInTile(0x23, 0x22);
                                }
                            }
                            break;
                        }
                    case worlds.world_ids.Killorn:
                        //handle killorn is crashing
                        if ((playerdat.GetQuest(50) == 1) && (mode != EnterLevelMode))
                        {
                            if (dungeon == 17)
                            {
                                killorn.KilornIsCrashing(isEnteringLevel: true);
                            }
                        }
                        break;
                    case worlds.world_ids.Academy:
                        //Handle removing the puzzle wand from inventory on level 3 and returning it to it's home position
                        if ((dungeon == 43) && (mode == ExitLevelMode))
                        {
                            academy.RemoveAcademyWand();
                        }
                        break;
                    case worlds.world_ids.Tomb:
                        //Handle killing undead when Loth has been put to rest.
                        if ((playerdat.GetQuest(7) == 1) && (mode == EnterLevelMode))
                        {
                            //loth is dead
                            tomb.KillLothsLiches(-1);
                        }
                        break;
                    case worlds.world_ids.Ethereal:
                        //Handle automap enabled settings
                        if (mode == EnterLevelMode)
                        {
                            playerdat.AutomapEnabled_backup = playerdat.AutomapEnabled;// needs to be backedup due to rarely seen mechanics involving getting lost
                            playerdat.AutomapEnabled = false;
                        }
                        else
                        {
                            if (mode == ExitLevelMode)
                            {
                                playerdat.AutomapEnabled = playerdat.AutomapEnabled_backup;
                            }
                        }
                        break;
                }
            }
        }

        /// <summary>
        /// Handle events that happen when player leaves and enters dungeon levels in UW1
        /// </summary>
        /// <param name="mode">0 = new game/enter level, 1 = leave level, 2 = unused and 3 = load save game</param>
        /// <param name="dungeon">Either the new or previous dungeon.</param>
        static void LevelChangeEventsUW1(int mode, int dungeon)
        {
            if (playerdat.armageddon && mode == EnterLevelMode)
            {//entering a level under the influence of armageddon
                UWTileMap.ResetMap(0);
            }
            else
            {
                if (mode == EnterLevelMode)
                {
                    playerdat.LastDamagedNPCIndex = 0; playerdat.LastDamagedNPCType = -1;
                    CallBacks.RunCodeOnAllMobiles(ClearUnkBit_0x15_7_OnMobiles, null);
                    npc.BitFieldForPathing_dseg_67d6_B4 = 0xFFFF;//clear_paths frees all 16 stored paths. uw1 PATHFIND.C:953
                }
                else
                {
                    if (mode == ExitLevelMode)
                    {
                        //Calm NPCs?
                        //Debug.Print("'Calm NPCS'");
                        uwObject.ResetMobileObjects();
                    }
                }
                switch ((worlds.UW1_Dungeons)dungeon)
                {
                    case worlds.UW1_Dungeons.Tybal:
                        {
                            if (!playerdat.isOrbDestroyed)
                            {
                                switch (mode)
                                {
                                    case EnterLevelMode:
                                        //entering lair while orb is not yet destroyed
                                        playerdat.backup_mana = playerdat.max_mana;
                                        playerdat.max_mana = 0;
                                        playerdat.play_mana = 0;
                                        break;
                                    case ExitLevelMode://restore drained mana.
                                        playerdat.max_mana = playerdat.backup_mana;
                                        playerdat.play_mana = (playerdat.max_mana >> 2);
                                        break;
                                }
                            }
                            break;
                        }
                    case worlds.UW1_Dungeons.Ethereal:
                        {
                            switch (mode)
                            {
                                case EnterLevelMode:
                                    if (playerdat.AutomapEnabled)
                                    {//yes this is correct. Backup mana is repurposed for this use on the void.
                                        playerdat.backup_mana = 1;
                                    }
                                    else
                                    {
                                        playerdat.backup_mana = 0;
                                    }
                                    playerdat.AutomapEnabled = false;//and the automap is off in the Void (uw1 GAMEWRAP.C:473-475)
                                    break;
                                case ExitLevelMode:
                                    playerdat.AutomapEnabled = (playerdat.backup_mana > 0);
                                    break;
                                case LoadSaveMode:
                                    playerdat.AutomapEnabled = false;
                                    break;
                            }
                            break;
                        }
                }
            }
        }


        static void ClearUnkBit_0x15_7_OnMobiles(uwObject obj, int[] paramsarray)
        {
            obj.UnkBit_0X15_Bit7 = 0;
        }

    }//end class
}//end namespace