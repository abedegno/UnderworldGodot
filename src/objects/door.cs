using System;
using System.Diagnostics;
using Godot;

namespace Underworld
{
    /// <summary>
    /// Class to render the moveable door
    /// </summary>
    public class door : model3D
    {
        public static GRLoader tmDoor;
        public Node3D doorNode;
        public int texture;
        public int floorheight;
        public Vector3 position;

        public static bool SkipDoorSound;//set by SCD Door Close to stop door sound effects from playing. Unimplemented.

        public static bool isMoving(uwObject obj)
        {
            return obj.item_id == 463;
        }
        public static bool isSecretDoor(uwObject obj)
        {
            return (obj.item_id == 327) || (obj.item_id == 335);
        }

        Vector3 pivot = Vector3.Zero;

        public const float framethickness = 0.08f;

        public static bool isOpen(uwObject obj)
        {
            return obj.classindex >= 8;
        }

        public static bool isPortcullis(uwObject obj)
        {
            if (isMoving(obj))
            {//use the owner
                return ((obj.owner == 6) || (obj.owner == 0xE));
            }
            else
            {
                return ((obj.classindex == 6) || (obj.classindex == 0xE));
            }
        }


        /// <summary>
        /// How many animation frames this door type has.
        /// </summary>
        public static int NoOfFrames(uwObject obj)
        {
            if (isMoving(obj))
            {//return based on the owner
                if ((obj.owner == 6) || (obj.owner == 14))
                {
                    return 4;
                }
                else
                {
                    return 5;
                }
            }
            else
            {
                if (isPortcullis(obj))
                {
                    return 4;
                }
                else
                {
                    return 5;
                }
            }
        }

        /// <summary>
        /// The angle a door sits at for one animation state.
        ///
        /// DOS shifts the state number into a 16 bit angle space, `state << 12` in
        /// RenderDoor_seg032_2DCA_C56 (UW.EXE 0x2FD9F), where 0x10000 is a full turn. One
        /// state is therefore 0x1000/0x10000 of a turn, 22.5 degrees, and that is fixed
        /// regardless of how many states the door type has.
        ///
        /// This used to be (PI/2) / NoOfFrames, which is the same 22.5 degrees for the four
        /// state portcullis but only 18 for a five state door, leaving it 22.5 degrees short
        /// of DOS at full open. See issue #99.
        /// </summary>
        private const double RadiansPerDoorState = Math.PI / 8;

        public static float GetRadiansForIndex(uwObject obj, int index, int doordir)
        {
            var dir = 1;
            if (doordir == 1)
            {
                dir = -1;
            }
            return (float)(dir * index * RadiansPerDoorState);
        }

        public static float GetHeightForIndex(uwObject obj, int index)
        {
            var unit = 0.8 / NoOfFrames(obj);
            return (float)(index * unit);
        }

        static door()
        {
            tmDoor = new GRLoader(GRLoader.DOORS_GR, GRLoader.GRShaderMode.TextureShader);
            tmDoor.UseRedChannel = true;
        }

        public static door CreateInstance(Node3D parent, uwObject obj, UWTileMap a_tilemap, string name)
        {
            int tileX = obj.tileX;
            int tileY = obj.tileY;
            if (UWTileMap.ValidTile(tileX, tileY))
            {//Mark the tile as having a door.
                a_tilemap.Tiles[tileX, tileY].DoorIndex = obj.index;
            }
            var d = new door(obj);
            if (isSecretDoor(obj))
            {
                d.texture = a_tilemap.Tiles[tileX, tileY].wallTexture; //a_tilemap.texture_map[a_tilemap.Tiles[tileX, tileY].wallTexture];
            }
            else
            {
                if (_RES == GAME_UW2)
                {
                    d.texture = a_tilemap.texture_map[64 + (obj.item_id & 0x7)];
                }
                else
                {
                    d.texture = a_tilemap.texture_map[58 + (obj.item_id & 0x7)];
                }

            }

            d.floorheight = a_tilemap.Tiles[tileX, tileY].floorHeight * 2;
            d.doorNode = d.Generate3DModel(parent, name);
            d.doorNode.Position = d.pivot;

            if (isPortcullis(obj))
            {//translate model up 1 unit
                d.doorNode.Position
                    = new Vector3
                        (d.doorNode.Position.X,
                        d.doorNode.Position.Z + GetHeightForIndex(obj, obj.flags),
                        d.doorNode.Position.Y
                        );

            }
            else
            {// rotate model 90 
                d.doorNode.Rotate(Vector3.Up, GetRadiansForIndex(obj, obj.flags, obj.doordir));
            }

            //DisplayModelPoints(d, parent, 30);
            return d;
        }

        public door(uwObject _uwobject)
        {
            uwobject = _uwobject;
            uwobject.instance = this;
        }

        public static bool Use(uwObject obj, bool PlayerUse)
        {
            //var d = (door)obj.instance;
            if (a_lock.GetIsLocked(obj) && (!isOpen(obj)))
            {//door is locked and closed
                if (PlayerUse)
                {
                    uimanager.AddToMessageScroll("The " + GameStrings.GetObjectNounUW(obj.item_id) + " is locked.");    
                }                
            }
            else
            {   //door unlocked. toggle it's state
                ToggleDoor(obj);
            }
            return true;
        }


        /// <summary>
        /// Opens the door
        /// </summary>
        /// <param name="doorObj"></param>
        public static void OpenDoor(uwObject doorObj)
        {
            var doorInstance = (door)doorObj.instance;
            if (isMoving(doorObj))
            {//a closing door turns round, an opening one is left alone (uw1 USEITEMS.C:1139-1145, uw2 USEITEMS.C:744-751)
                var state = doorObj.owner & 0xF;
                if (state < 8) { return; }
                doorObj.owner = (short)(state - 8);
                ChangeDoor(doorObj);
                PlayDoorTurnSound(doorObj, closing: false);
                return;
            }
            if (isOpen(doorObj)) { return; }//don't reopen an open door
            if (!isPortcullis(doorObj))
            {//a portcullis rises as it moves instead (uw1 USEITEMS.C:1147-1148, uw2 USEITEMS.C:755-756)
                doorObj.zpos += 24;
            }
            if (TurnIntoMovingDoor(doorObj))
            {

            }
            else
            {
                if (isPortcullis(doorObj))
                {
                    doorInstance.position = new Vector3(0f, GetHeightForIndex(doorObj, NoOfFrames(doorObj)), 0f);
                }
                else
                {
                    //set to open without animation
                    doorInstance.doorNode.Rotate(Vector3.Up, GetRadiansForIndex(doorObj, NoOfFrames(doorObj), doorObj.doordir));
                }
            }
            if (isPortcullis(doorObj))
            {
                UWsoundeffects.PlaySoundEffectAtCoordinate(UWsoundeffects.SoundEffectPortcullis, (doorObj.tileX << 3) + doorObj.xpos, (doorObj.tileY << 3) + doorObj.ypos, 0);
            }
            else
            {
                UWsoundeffects.PlaySoundEffectAtCoordinate(UWsoundeffects.SoundEffectDoor, (doorObj.tileX << 3) + doorObj.xpos, (doorObj.tileY << 3) + doorObj.ypos, 0);
            }
            //playerdat.UpdateAutomap();//trigger an update of visibility
            trigger.TriggerObjectLink(character: 1,
                    ObjectUsed: doorObj,
                    triggerType: (int)triggerObjectDat.OPEN_TRIGGER_TYPE,
                    triggerX: doorObj.tileX,
                    triggerY: doorObj.tileY,
                    objList: UWTileMap.current_tilemap.LevelObjects);
        }


        /// <summary>
        /// Closes the door
        /// </summary>
        /// <param name="doorObj"></param>
        public static void CloseDoor(uwObject doorObj)
        {
            if (doorObj.instance == null) { return; }//no door found.
            var doorInstance = (door)doorObj.instance;
            if (isMoving(doorObj))
            {//an opening door turns round, a closing one is left alone (uw1 USEITEMS.C:1168-1174, uw2 USEITEMS.C:778-784)
                var state = doorObj.owner & 0xF;
                if (state >= 8) { return; }
                doorObj.owner = (short)(state + 8);
                ChangeDoor(doorObj);
                PlayDoorTurnSound(doorObj, closing: true);
                return;
            }
            if (!isOpen(doorObj)) { return; }//don't reclose a closed door
            if (TurnIntoMovingDoor(doorObj))
            {
                //lowered when it shuts, in EndMovingDoor
            }
            else
            {
                doorObj.zpos -= 24;
                //set to closed state without animation
                if (isPortcullis(doorObj))
                {
                    doorInstance.position = new Vector3(0f, GetHeightForIndex(doorObj, 0), 0f);
                }
                else
                {
                    //set to open without animation
                    doorInstance.doorNode.Rotate(Vector3.Up, GetRadiansForIndex(doorObj, 0, doorObj.doordir));
                }
            }
            if (isPortcullis(doorObj))
            {
                UWsoundeffects.PlaySoundEffectAtCoordinate(UWsoundeffects.SoundEffectPortcullis, (doorObj.tileX << 3) + doorObj.xpos, (doorObj.tileY << 3) + doorObj.ypos, 0);
            }
            else
            {
                UWsoundeffects.PlaySoundEffectAtCoordinate(UWsoundeffects.SoundEffectDoor, (doorObj.tileX << 3) + doorObj.xpos, (doorObj.tileY << 3) + doorObj.ypos, 0);
            }


            if ((doorObj.link != 0) && (_RES == GAME_UW2))
            {
                // trigger.CloseTrigger(obj.uwobject, obj.uwobject.link, UWTileMap.current_tilemap.LevelObjects);
                trigger.TriggerObjectLink(
                    character: 1,
                    ObjectUsed: doorObj,
                    triggerType: (int)triggerObjectDat.triggertypes.CLOSE,
                    triggerX: doorObj.tileX,
                    triggerY: doorObj.tileY,
                    objList: UWTileMap.current_tilemap.LevelObjects);
            }
        }

        /// <summary>
        /// Toogles the door (ignores lock state)
        /// </summary>
        /// <param name="obj"></param>
        public static void ToggleDoor(uwObject obj)
        {
            //a moving door (class index 0xF) goes to CloseDoor, as in the original (uw1 USEITEMS.C:1188-1194, uw2 USEITEMS.C:805-811)
            if (isOpen(obj))
            {
                CloseDoor(obj);
            }
            else
            {
                OpenDoor(obj);
            }
        }


        /// <summary>
        /// Turns the door in to an animated moving door that will open or close as the animos are processed.
        /// </summary>
        /// <param name="doorObj"></param>
        /// <returns>true if the door can be animated</returns>
        public static bool TurnIntoMovingDoor(uwObject doorObj)
        {
            if (animo.CreateAnimoLink(obj: doorObj, Duration: NoOfFrames(doorObj),DrawSprite:false))
            {
                //change object props
                doorObj.owner = (short)doorObj.classindex;
                doorObj.item_id = 0x1CF; // a moving door 
                return true;
            }
            return false;
        }

        /// <summary>
        /// One step of a moving door, do_animobj's door case (uw1 EFFECT.C:234-241, uw2 EFFECT.C:225-232).
        /// The open angle is the low three bits of the flags and bit 3 (flags_full & 8) is the direction,
        /// set while the door closes. A portcullis rises or falls 6 a step. A closing door then checks its
        /// doorway (CheckDoor).
        /// </summary>
        /// <returns>true when CheckDoor has set the overlay's duration itself, so it is not counted down this step</returns>
        public static bool MoveDoor(uwObject obj, int frames, AnimationOverlay ovl)
        {
            var lengthSet = false;
            if ((obj.flags_full & 8) != 0)
            {
                frames = -frames;
            }
            if ((obj.owner & 7) == 6)
            {
                obj.zpos = (short)(obj.zpos + frames * 6);
            }
            obj.flags_full = (short)((obj.flags_full & 7) + frames + (obj.flags_full & 8));
            if ((obj.flags_full & 8) != 0)
            {
                lengthSet = !CheckDoor(obj, frames, ovl);
            }
            RefreshDoorModel(obj);
            return lengthSet;
        }

        /// <summary>
        /// check_door (uw1 EFFECT.C:441-474, uw2 EFFECT.C:445-479). A closing door tests whether it still fits
        /// in its doorway (can_place, flier, range 8) at the height it will have when shut. If something is
        /// in the way it turns round to opening, goes back the step less one, and its run becomes
        /// limit - duration + 1 (limit 5, 4 for a portcullis). UW2 also fires the door's open triggers
        /// (checkTrap with how 8); UW1 does not.
        /// </summary>
        /// <returns>true when the way is clear</returns>
        static bool CheckDoor(uwObject obj, int frames, AnimationOverlay ovl)
        {
            var limit = 5;
            var minor = obj.owner & 0xF;
            var item = (obj.owner >> 4 << 4) + minor + 0x140;
            int z = obj.zpos;
            if ((minor & 7) != 6)
            {
                z -= 0x18;
            }
            if (!motion.TestIfObjectFitsInTile(item, obj.index, (ovl.tileX << 3) + obj.xpos, (ovl.tileY << 3) + obj.ypos, z, 1, 8))
            {
                if ((obj.owner & 7) == 6)
                {//the original also tests a door that is not moving, which cannot happen here
                    limit = 4;
                }
                obj.flags_full = (short)(obj.flags_full & 7);
                if (_RES == GAME_UW2)
                {
                    trigger.TriggerObjectLink(
                        character: 0,
                        ObjectUsed: obj,
                        triggerType: (int)triggerObjectDat.triggertypes.OPEN_UW2,
                        triggerX: ovl.tileX,
                        triggerY: ovl.tileY,
                        objList: UWTileMap.current_tilemap.LevelObjects);
                }
                obj.flags_full = (short)((obj.flags_full & 8) + ((obj.flags_full & 7) - frames - 1));
                ovl.Duration = limit - ovl.Duration + 1;
                return false;
            }
            return true;
        }

        /// <summary>
        /// The end of a moving door's run, toast_animobj's door case (uw1 EFFECT.C:97-143, uw2 EFFECT.C:84-136).
        /// Any steps left are taken first (the class has the finish flag 0x80 in OBJECTS.DAT). An opening door
        /// becomes the open door. A closing door is lowered 0x18 (not a portcullis) and becomes the closed
        /// door, unless something now stands in its doorway (can_place), when it turns round (changeDoor)
        /// and keeps moving. Closing plays the shutting sound, 0xC (UW2: 0x26 for a portcullis, and none
        /// while SkipDoorSound, the original's quick_time). The direction bit then points the other way.
        /// </summary>
        public static void EndMovingDoor(uwObject obj, AnimationOverlay ovl)
        {
            if (ovl.Duration != 0)
            {
                MoveDoor(obj, ovl.Duration, ovl);
            }
            var minor = obj.owner >> 4;
            var state = obj.owner & 0xF;
            int z = obj.zpos;
            if ((obj.flags_full & 8) != 0)
            {
                if (state >= 8)
                {
                    state -= 8;
                }
                if ((state & 7) != 6)
                {
                    z -= 0x18;
                }
                if (!motion.TestIfObjectFitsInTile(0x140 + (minor << 4) + state, obj.index, (ovl.tileX << 3) + obj.xpos, (ovl.tileY << 3) + obj.ypos, z, 1, 8))
                {
                    obj.owner = (short)state;
                    ovl.Duration = 0;
                    ChangeDoor(obj, ovl);
                    return;
                }
                var x = (ovl.tileX << 3) + obj.xpos;
                var y = (ovl.tileY << 3) + obj.ypos;
                if (_RES != GAME_UW2)
                {
                    UWsoundeffects.PlaySoundEffectAtCoordinate(0xC, x, y, 0);
                }
                else if (!SkipDoorSound)
                {//the original takes the type from the last door opened or closed (door_type), not this one
                    UWsoundeffects.PlaySoundEffectAtCoordinate((byte)((state & 7) != 6 ? 0xC : 0x26), x, y, 0);
                }
            }
            else
            {
                state |= 8;
            }
            obj.zpos = (short)z;
            obj.item_id = 0x140 + (minor << 4) + state;
            obj.owner = 0;
            obj.flags_full = (short)(obj.flags_full ^ 8);
            ovl.Duration = 0;
            RefreshDoorModel(obj);
        }

        /// <summary>
        /// changeDoor (uw1 USEITEMS.C:1114-1128, uw2 USEITEMS.C:716-732). Turns a moving door round: flips the
        /// direction bit and makes the steps left into the steps done (5, 4 for a portcullis, less the
        /// duration left). UW2 also fires the door's traps with how 9 when it now closes, 8 when it now opens.
        /// The callers set the owner's open bit first.
        /// </summary>
        public static void ChangeDoor(uwObject doorObj, AnimationOverlay ovl = null)
        {
            var len = NoOfFrames(doorObj);
            doorObj.flags_full = (short)(doorObj.flags_full ^ 8);
            if (_RES == GAME_UW2)
            {
                trigger.TriggerObjectLink(
                    character: 0,
                    ObjectUsed: doorObj,
                    triggerType: (int)((doorObj.flags_full & 8) != 0 ? triggerObjectDat.triggertypes.CLOSE : triggerObjectDat.triggertypes.OPEN_UW2),
                    triggerX: doorObj.tileX,
                    triggerY: doorObj.tileY,
                    objList: UWTileMap.current_tilemap.LevelObjects);
            }
            ovl ??= FindDoorOverlay(doorObj.index);
            if (ovl != null)
            {
                ovl.Duration = len - ovl.Duration;
            }
        }

        /// <summary>
        /// The running overlay of a moving door (find_anim). A finished one keeps its link with duration 0,
        /// the port's free slot, so only a live one counts.
        /// </summary>
        public static AnimationOverlay FindDoorOverlay(int index)
        {
            foreach (var ovl in UWTileMap.current_tilemap.Overlays)
            {
                if (ovl != null && ovl.link == index && ovl.Duration != 0)
                {
                    return ovl;
                }
            }
            return null;
        }

        /// <summary>
        /// The sound when a moving door is turned round. The original reads the type from the moving door,
        /// whose class index is 0xF, so even a portcullis plays the door sound (uw1 USEITEMS.C:1153-1158, 1179-1184;
        /// uw2 USEITEMS.C:743, 762-767, 776, 793-802). UW2's CloseDoor plays none while quick_time (SkipDoorSound).
        /// </summary>
        static void PlayDoorTurnSound(uwObject doorObj, bool closing)
        {
            if (closing && _RES == GAME_UW2 && SkipDoorSound) { return; }
            UWsoundeffects.PlaySoundEffectAtCoordinate(UWsoundeffects.SoundEffectDoor, (doorObj.tileX << 3) + doorObj.xpos, (doorObj.tileY << 3) + doorObj.ypos, 0);
        }

        /// <summary>
        /// Turns or raises the door's model to its open angle, the low three bits of its flags.
        /// </summary>
        static void RefreshDoorModel(uwObject obj)
        {
            var doorInstance = (door)obj.instance;
            if (doorInstance == null) { return; }
            if (isPortcullis(obj))
            {
                //Set z based on flags
                doorInstance.doorNode.Position = new Vector3(0f, GetHeightForIndex(obj, obj.flags), 0f);
            }
            else
            {
                //Set rotate based on flags
                doorInstance.doorNode.Rotation = Vector3.Zero;
                doorInstance.doorNode.Rotate(Vector3.Up, GetRadiansForIndex(obj, obj.flags, obj.doordir));
            }
        }

        public static bool LookAt(uwObject doorobject)
        {
            if (_RES != GAME_UW2)
            {
                if ((doorobject.owner & 0x1) == 1)
                {
                    uimanager.AddToMessageScroll(GameStrings.GetString(1, 0x83));//the door is spiked
                    return true;
                }
            }
            return look.PrintLookDescription(
                obj: doorobject,
                objList: UWTileMap.current_tilemap.LevelObjects,
                lorecheckresult: 3);
        }




        /// <summary>
        /// Handles checking if the critter can operate this door or lock
        /// Unimplemented: this function is used to operate the lock in vanilla underowrld
        /// </summary>
        /// <param name="character"></param>
        /// <param name="doorobj"></param>
        /// <param name="skillnegated">Also key index used.</param>
        /// <returns></returns>
        public static int CharacterDoorLockAndKeyInteraction(uwObject character, uwObject doorobj, int skillnegated)
        {
            if ((doorobj.is_quant == 0) && (doorobj.link <= 0))
            {
                var LockObject = objectsearch.FindMatchInObjectChain(
                    ListHeadIndex: doorobj.link,
                    majorclass: 4, minorclass: 0, classindex: 0xF,
                    objList: UWTileMap.current_tilemap.LevelObjects,
                    SkipNext: false, SkipLinks: true);

                if (LockObject == null)
                {
                    return 1;
                }
                else
                {
                    if (LockObject.flags0 != 0)
                    {
                        //seg040_34E7_899:
                        if (skillnegated >= 0)
                        {
                            //seg040_34E7_901
                            if (skillnegated <= 0)
                            {
                                //or skillnegated==0
                                return 1;
                            }
                            else
                            {
                                if ((LockObject.link & 0x1FF) == 0)
                                {
                                    //seg040_34E7_928:
                                    return 0;
                                }
                                else
                                {
                                    //seg040_34E7:0917
                                    if ((LockObject.link & 0x1FF) == skillnegated)
                                    {
                                        //seg040_34E7_935:
                                        //unlock trigger
                                        trigger.TriggerObjectLink(character?.index ?? 0, doorobj, 0xB, doorobj.tileX, doorobj.tileY, UWTileMap.current_tilemap.LevelObjects);
                                        if (LockObject.flags1 == 0)
                                        {
                                            //seg040_34E7_963:
                                            if (ObjectRemover_OLD.RemoveObjectFromLinkedList(doorobj.link, LockObject.index, UWTileMap.current_tilemap.LevelObjects, doorobj.PTR + 6))
                                            {
                                                ObjectFreeLists.ReleaseFreeObject(LockObject);
                                            }
                                            return 3;
                                        }
                                        else
                                        {
                                            //seg040_34E7_985
                                            LockObject.flags0 = 0;
                                            return 3;
                                        }
                                    }
                                    else
                                    {
                                        return 0;
                                    }
                                }
                            }
                        }
                        else
                        {
                            //seg040_34E7_8A0:
                            //lockpick skill check
                            var di_checkresult = playerdat.SkillCheck(-skillnegated, LockObject.zpos * 3);
                            if (
                                ((LockObject.zpos == 0xE) && (-skillnegated < 0x20))
                                ||
                                (LockObject.zpos == 0xF)
                                ||
                                (di_checkresult == playerdat.SkillCheckResult.Fail)
                                )
                            {
                                //seg040_34E7_8E7
                                if (di_checkresult == playerdat.SkillCheckResult.CritFail)
                                {
                                    //seg040_34E7_8EC
                                    return 5;
                                }
                                else
                                {
                                    //seg040_34E7_8F1:
                                    return 0;
                                }
                            }
                            else
                            {
                                //seg040_34E7_8F6
                                if (di_checkresult == playerdat.SkillCheckResult.CritFail)
                                {
                                    return 5;
                                }
                                else
                                {
                                    //seg040_34E7_935: (again)
                                    //unlock trigger
                                    trigger.TriggerObjectLink(character?.index ?? 0, doorobj, 0xB, doorobj.tileX, doorobj.tileY, UWTileMap.current_tilemap.LevelObjects);
                                    if (LockObject.flags1 == 0)
                                    {
                                        //seg040_34E7_963:
                                        if (ObjectRemover_OLD.RemoveObjectFromLinkedList(doorobj.link, LockObject.index, UWTileMap.current_tilemap.LevelObjects, doorobj.PTR + 6))
                                        {
                                            ObjectFreeLists.ReleaseFreeObject(LockObject);
                                        }
                                        return 3;
                                    }
                                    else
                                    {
                                        //seg040_34E7_985
                                        LockObject.flags0 = 0;
                                        return 3;
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        //seg040_34E7:081F
                        if (skillnegated <= 0)
                        {
                            return 4;
                        }
                        else
                        {
                            //seg040_34E7_832
                            if ((doorobj.OneF0Class == 0x14) && (doorobj.classindex >= 0x8))
                            {
                                //seg040_34E7_862:
                                return 4;
                            }
                            else
                            {
                                if (LockObject.link != skillnegated)
                                {
                                    return 0;
                                }
                                else
                                {
                                    //seg040_34E7_87C
                                    LockObject.flags0 = 1;
                                    return 2;
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                return 1;
            }
        }



        //******************************RENDERING INFO**********************************/
        public override Vector3[] ModelVertices()
        {
            if (isPortcullis(this.uwobject))
            {
                Vector3[] v = new Vector3[28];

                //left vertical bar top
                v[0] = new Vector3(-0.17625f * 1.2f, 0.8125f * 1.2f, framethickness / 2f);
                v[1] = new Vector3(-0.13625f * 1.2f, 0.8125f * 1.2f, framethickness / 2f);

                //center vertical bar top
                v[2] = new Vector3(0.02f, 0.8125f * 1.2f, framethickness / 2f);
                v[3] = new Vector3(-0.02f, 0.8125f * 1.2f, framethickness / 2f);

                //right vertical bar top
                v[4] = new Vector3(0.17625f * 1.2f, 0.8125f * 1.2f, framethickness / 2f);
                v[5] = new Vector3(0.13625f * 1.2f, 0.8125f * 1.2f, framethickness / 2f);

                //left vertical bar bottom
                v[6] = new Vector3(-0.17625f * 1.2f, 0f, framethickness / 2f);
                v[7] = new Vector3(-0.13625f * 1.2f, 0f, framethickness / 2f);

                //Centre vertical bar bottom
                v[8] = new Vector3(0.02f, 0f, framethickness / 2f);
                v[9] = new Vector3(-0.02f, 0f, framethickness / 2f);

                //right vertical bar bottom
                v[10] = new Vector3(0.17625f * 1.2f, 0f, framethickness / 2f);
                v[11] = new Vector3(0.13625f * 1.2f, 0f, framethickness / 2f);


                //cross bar 1
                v[12] = new Vector3(-0.3125f * 1.2f, 0.67f * 1.2f, framethickness / 2f);
                v[13] = new Vector3(-0.3125f * 1.2f, 0.63f * 1.2f, framethickness / 2f);
                v[14] = new Vector3(0.3125f * 1.2f, 0.67f * 1.2f, framethickness / 2f);
                v[15] = new Vector3(0.3125f * 1.2f, 0.63f * 1.2f, framethickness / 2f);

                //cross bar 2
                v[16] = new Vector3(-0.3125f * 1.2f, 0.5075f * 1.2f, framethickness / 2f);
                v[17] = new Vector3(-0.3125f * 1.2f, 0.4675f * 1.2f, framethickness / 2f);
                v[18] = new Vector3(0.3125f * 1.2f, 0.5075f * 1.2f, framethickness / 2f);
                v[19] = new Vector3(0.3125f * 1.2f, 0.4675f * 1.2f, framethickness / 2f);

                //cross bar 3
                v[20] = new Vector3(-0.3125f * 1.2f, 0.345f * 1.2f, framethickness / 2f);
                v[21] = new Vector3(-0.3125f * 1.2f, 0.305f * 1.2f, framethickness / 2f);
                v[22] = new Vector3(0.3125f * 1.2f, 0.345f * 1.2f, framethickness / 2f);
                v[23] = new Vector3(0.3125f * 1.2f, 0.305f * 1.2f, framethickness / 2f);

                //cross bar 4
                v[24] = new Vector3(-0.3125f * 1.2f, 0.1825f * 1.2f, framethickness / 2f);
                v[25] = new Vector3(-0.3125f * 1.2f, 0.1425f * 1.2f, framethickness / 2f);
                v[26] = new Vector3(0.3125f * 1.2f, 0.1825f * 1.2f, framethickness / 2f);
                v[27] = new Vector3(0.3125f * 1.2f, 0.1425f * 1.2f, framethickness / 2f);


                //0.4875
                //0.325
                //0.1625


                return v;
            }
            else
            {
                ///Same vertices as the doorframe.
                //float ceilingAdjustment = (float)(32 - floorheight) * 0.15f;//- position.Y; 
                //float frameadjustment = (float)(floorheight+4) * 0.15f ;   //0.8125f

                Vector3[] v = new Vector3[8];
                v[0] = new Vector3(-0.3125f * 1.2f, 0f, 0f); //frame //bottom //right //front           
                v[1] = new Vector3(-0.3125f * 1.2f, 0.8125f * 1.2f, 0f); //frame //right //top //front
                v[2] = new Vector3(0.3125f * 1.2f, 0.8125f * 1.2f, 0f);  //frame // left //top //front
                v[3] = new Vector3(0.3125f * 1.2f, 0f, 0f);//frame // left //bottom //front
                v[4] = new Vector3(-0.3125f * 1.2f, 0f, framethickness);  //rear
                v[5] = new Vector3(-0.3125f * 1.2f, 0.8125f * 1.2f, framethickness); //frame //rear
                v[6] = new Vector3(0.3125f * 1.2f, 0.8125f * 1.2f, framethickness);  //frame //rear
                v[7] = new Vector3(0.3125f * 1.2f, 0f, framethickness);  //rear 
                var pivotindex = 0;
                if (uwobject.doordir == 1)
                {
                    pivotindex = 4;// doors rotate the opposite direction when doordir is set
                }
                pivot = v[pivotindex];
                for (int i = 0; i < 8; i++)
                {   //translate away so that v[0] is at the model origin.
                    if (i != pivotindex)
                    {
                        v[i] -= v[pivotindex];
                    }
                }
                v[pivotindex] = Vector3.Zero;

                return v;
            }
        }

        public override Vector2[] ModelUVs(Vector3[] verts)
        {
            if (isPortcullis(this.uwobject))
            {
                return base.ModelUVs(verts);
            }
            else
            {
                if (!isSecretDoor(this.uwobject))
                { //normal door textures have a black border at the top that needs to excluded 
                    var uv = new Vector2[8];
                    uv[0] = new Vector2(1f, 1f);
                    uv[1] = new Vector2(1f, 0.2f);
                    uv[2] = new Vector2(0f, 0.2f);
                    uv[3] = new Vector2(0f, 1f);

                    uv[4] = new Vector2(1f, 1f);
                    uv[5] = new Vector2(1f, 0.2f);
                    uv[6] = new Vector2(0f, 0.2f);
                    uv[7] = new Vector2(0f, 1f);
                    return uv;
                }
                else
                {
                    float distanceToFloor = (float)(32 - floorheight) * 0.15f; //distance to floor
                    float distanceToFrameHead = distanceToFloor - (0.8125f * 1.2f);   //0.975

                    float ceilingHeight = 32 * 0.15f;  //4.8f
                    float floor = floorheight * 0.15f;
                    distanceToFloor = ceilingHeight - floor;
                    var vectorToFloor = distanceToFloor / 1.2f;
                    var vectorToFrameHead = (vectorToFloor / distanceToFloor) * distanceToFrameHead;

                    var frameV0 = 0.185f;
                    var frameV1 = 1f - frameV0;//0.760416667f;
                    var uv = new Vector2[8];
                    uv[0] = new Vector2(frameV1, vectorToFloor);
                    uv[1] = new Vector2(frameV1, vectorToFrameHead);
                    uv[2] = new Vector2(frameV0, vectorToFrameHead);
                    uv[3] = new Vector2(frameV0, vectorToFloor);

                    uv[4] = new Vector2(frameV0, vectorToFloor);
                    uv[5] = new Vector2(frameV0, vectorToFrameHead);
                    uv[6] = new Vector2(frameV1, vectorToFrameHead);
                    uv[7] = new Vector2(frameV1, vectorToFloor);

                    return uv;
                }

            }


        }

        public override int NoOfMeshes()
        {
            if (!isPortcullis(this.uwobject))
            {
                return 3;
            }
            else
            {
                return 2;
            }
        }

        public override int[] ModelTriangles(int meshNo)
        {
            if (!isPortcullis(this.uwobject))
            {
                int[] tris = new int[6];
                switch (meshNo)
                {
                    case 0: //Door Front
                        tris[0] = 0;
                        tris[1] = 3;
                        tris[2] = 2;
                        tris[3] = 2;
                        tris[4] = 1;
                        tris[5] = 0;
                        return tris;
                    case 1: //Door Rear
                        tris[0] = 7;
                        tris[1] = 4;
                        tris[2] = 5;
                        tris[3] = 5;
                        tris[4] = 6;
                        tris[5] = 7;
                        return tris;
                    case 2: // Door trim
                        {
                            tris = new int[6 * 4];
                            tris[0] = 4;
                            tris[1] = 0;
                            tris[2] = 1;
                            tris[3] = 1;
                            tris[4] = 5;
                            tris[5] = 4;

                            tris[6] = 5;
                            tris[7] = 1;
                            tris[8] = 2;
                            tris[9] = 2;
                            tris[10] = 6;
                            tris[11] = 5;

                            tris[12] = 3;
                            tris[13] = 7;
                            tris[14] = 6;
                            tris[15] = 6;
                            tris[16] = 2;
                            tris[17] = 3;

                            tris[18] = 0;
                            tris[19] = 4;
                            tris[20] = 7;
                            tris[21] = 7;
                            tris[22] = 3;
                            tris[23] = 0;
                            return tris;
                        }

                }
            }
            else
            {//is portcullis
                switch (meshNo)
                {
                    case 0:
                        { //vertical bars
                            var tris = new int[36];

                            //vertical bar left
                            tris[0] = 7;
                            tris[1] = 6;
                            tris[2] = 0;
                            tris[3] = 0;
                            tris[4] = 1;
                            tris[5] = 7;
                            tris[6] = 6;
                            tris[7] = 7;
                            tris[8] = 1;
                            tris[9] = 1;
                            tris[10] = 0;
                            tris[11] = 6;

                            //center vertical bar
                            tris[12] = 9;
                            tris[13] = 8;
                            tris[14] = 2;
                            tris[15] = 2;
                            tris[16] = 3;
                            tris[17] = 9;

                            tris[18] = 8;
                            tris[19] = 9;
                            tris[20] = 3;
                            tris[21] = 3;
                            tris[22] = 2;
                            tris[23] = 8;

                            //right vertical bar
                            tris[24] = 11;
                            tris[25] = 10;
                            tris[26] = 4;
                            tris[27] = 4;
                            tris[28] = 5;
                            tris[29] = 11;

                            tris[30] = 10;
                            tris[31] = 11;
                            tris[32] = 5;
                            tris[33] = 5;
                            tris[34] = 4;
                            tris[35] = 10;

                            return tris;
                        }
                    case 1:
                        {
                            var tris = new int[48];
                            //bar1
                            tris[0] = 15;
                            tris[1] = 14;
                            tris[2] = 12;
                            tris[3] = 12;
                            tris[4] = 13;
                            tris[5] = 15;

                            tris[6] = 13;
                            tris[7] = 12;
                            tris[8] = 14;
                            tris[9] = 14;
                            tris[10] = 15;
                            tris[11] = 13;

                            //bar2

                            tris[12] = 19;
                            tris[13] = 18;
                            tris[14] = 16;
                            tris[15] = 16;
                            tris[16] = 17;
                            tris[17] = 19;

                            tris[18] = 17;
                            tris[19] = 16;
                            tris[20] = 18;
                            tris[21] = 18;
                            tris[22] = 19;
                            tris[23] = 17;

                            //bar3                            
                            tris[24] = 21;
                            tris[25] = 20;
                            tris[26] = 22;
                            tris[27] = 22;
                            tris[28] = 23;
                            tris[29] = 21;

                            tris[30] = 23;
                            tris[31] = 22;
                            tris[32] = 20;
                            tris[33] = 20;
                            tris[34] = 21;
                            tris[35] = 23;

                            //bar 4
                            tris[36] = 25;
                            tris[37] = 24;
                            tris[38] = 26;
                            tris[39] = 26;
                            tris[40] = 27;
                            tris[41] = 25;

                            tris[42] = 27;
                            tris[43] = 26;
                            tris[44] = 24;
                            tris[45] = 24;
                            tris[46] = 25;
                            tris[47] = 27;
                            return tris;
                        }
                }
            }
            return base.ModelTriangles(meshNo);
        }

        public override int ModelColour(int meshNo)
        {
            if (isPortcullis(this.uwobject))
            {
                if (_RES == GAME_UW2)
                {
                    return 83;
                }
                else
                {
                    return 206;
                }
            }
            else
            {
                switch (meshNo)
                {
                    case 0:
                    case 1:
                        return texture;
                    case 2:
                        return 0;
                }
            }

            return base.ModelColour(meshNo);
        }

        public override ShaderMaterial GetMaterial(int textureno, int surface)
        {
            if (isPortcullis(this.uwobject))
            {
                return base.GetMaterial(textureno, surface);
            }
            else
            {
                switch (surface)
                {
                    case 0: //door texture
                    case 1:
                        if (!isSecretDoor(this.uwobject))
                        {
                            return tmDoor.GetMaterialForObject(textureno, uwobject);
                        }
                        else
                        {
                            return tileMapRender.mapTexturesWalls.GetMaterialForObject(
                                textureno: textureno, 
                                texturemap: UWTileMap.current_tilemap.texture_map, 
                                obj: uwobject);
                        }
                }
            }

            return base.GetMaterial(textureno, surface);
        }
    } //end class door


    /// <summary>
    /// Class to render the doorway frames
    /// </summary>
    public class doorway : model3D
    {
        //const float doorwidth = 0.8f;
        //const float doorframewidth = 1.2f;
        // const float doorSideWidth = (doorframewidth - doorwidth) / 2f;
        // const float doorheight = 7f * 0.15f;

        public int texture;
        public float floorheight;
        // public Vector3 position;
        public Node3D doorFrameNode;

        public bool isOpen
        {
            get
            {
                return uwobject.classindex >= 8;
            }
        }
        public static doorway CreateInstance(Node3D parent, uwObject obj, UWTileMap a_tilemap, string name)
        {
            int tileX = obj.tileX;
            int tileY = obj.tileY;
            var doorTile = a_tilemap.Tiles[tileX, tileY];
            var dw = new doorway(obj);
            dw.texture = doorTile.wallTexture; //a_tilemap.texture_map[a_tilemap.Tiles[tileX, tileY].wallTexture];
            dw.floorheight = doorTile.floorHeight * 2;//uses floorheight since portculli use zpos when opened // (float)(obj.zpos) / 4f; //a_tilemap.Tiles[tileX, tileY].floorHeight;
            //n.position = parent.Position;
            //a portcullis. 

            dw.doorFrameNode = dw.Generate3DModel(parent, name);
            if (dw.isOpen)//&& (obj.item_id != 463))
            {//fix for map bug where some open doors extend out of the map. Force them onto a lower zpos without changing data
                var newZ = dw.uwobject.zpos - 24;
                
                if (UWTileMap.ValidTile(tileX, tileY))
                {         
                    var floorHeight = UWTileMap.current_tilemap.Tiles[tileX, tileY].floorHeight << 3;           
                    if (newZ < floorHeight)
                    {
                        newZ = floorHeight;
                        Debug.Print($"Repositioning {obj.a_name} {obj.index} zpos {obj.zpos} to floor height {tileX},{tileY}");
                    }
                    parent.Position = new Vector3(
                        x: parent.Position.X, 
                        y: ((floorHeight<<3)/1024f) * tileMapRender.godotscale.Y,
                        z: parent.Position.Z);
                }
            }

            SetModelRotation(parent, dw);

            switch (dw.uwobject.heading * 45)
            {//align model node in centre of tile along it's axis
                case tileMapRender.Heading6:
                    parent.Position = new Vector3(parent.Position.X, parent.Position.Y, (tileY * 1.2f) + 0.6f);
                    break;
                case tileMapRender.heading2:
                    parent.Position = new Vector3(parent.Position.X, parent.Position.Y, (tileY * 1.2f) + 0.6f);
                    break;
                case tileMapRender.heading4:
                    parent.Position = new Vector3((tileX * -1.2f) - 0.6f, parent.Position.Y, parent.Position.Z);
                    break;
                case tileMapRender.heading0:
                    parent.Position = new Vector3((tileX * -1.2f) - 0.6f, parent.Position.Y, parent.Position.Z);
                    break;
                default:
                    Debug.Print("Unhandled model centre");
                    break;
            }

            //DisplayModelPoints(dw,parent);
            return dw;
        }

        public doorway(uwObject _uwobject)
        {
            uwobject = _uwobject;
        }

        public override int NoOfMeshes()
        {
            return 8;
        }

        public override Vector3[] ModelVertices()
        {
            float ceilingAdjustment = (float)(32 - floorheight) * 0.15f;//- position.Y;   //distance from object to ceiling
                                                                        //float frameadjustment = (float)(floorheight+4) * 0.15f ;   //0.8125f

            Vector3[] v = new Vector3[20];
            v[0] = new Vector3(-0.3125f * 1.2f, 0f, 0f);
            v[1] = new Vector3(-0.3125f * 1.2f, 0.8125f * 1.2f, 0f); //frame
            v[2] = new Vector3(0.3125f * 1.2f, 0.8125f * 1.2f, 0f);  //frame
            v[3] = new Vector3(0.3125f * 1.2f, 0f, 0f);
            v[4] = new Vector3(-0.3125f * 1.2f, 0f, door.framethickness);  //rear
            v[5] = new Vector3(-0.3125f * 1.2f, 0.8125f * 1.2f, door.framethickness); //frame //rear
            v[6] = new Vector3(0.3125f * 1.2f, 0.8125f * 1.2f, door.framethickness);  //frame //rear
            v[7] = new Vector3(0.3125f * 1.2f, 0f, door.framethickness);  //rear
            v[8] = new Vector3(-0.6f, 0f, 0f);
            v[9] = new Vector3(-0.6f, 0f, door.framethickness);   //rear
            v[10] = new Vector3(0.6f, 0f, 0f);
            v[11] = new Vector3(0.6f, 0f, door.framethickness);   //rear
            v[12] = new Vector3(-0.6f, 0.8125f * 1.2f, 0f);  //level with frame //right
            v[13] = new Vector3(-0.6f, 0.8125f * 1.2f, door.framethickness); //level with frame //rear //right
            v[14] = new Vector3(0.6f, 0.8125f * 1.2f, 0f);  //level with frame  //left
            v[15] = new Vector3(0.6f, 0.8125f * 1.2f, door.framethickness);  //level with frame //rear //left
            v[16] = new Vector3(-0.6f, ceilingAdjustment, 0f);  //ceiling
            v[17] = new Vector3(-0.6f, ceilingAdjustment, door.framethickness); //ceiling //rear
            v[18] = new Vector3(0.6f, ceilingAdjustment, 0f);       //ceiling
            v[19] = new Vector3(0.6f, ceilingAdjustment, door.framethickness);  //ceiling //rear
            return v;
        }

        public override int[] ModelTriangles(int meshNo)
        {
            int[] tris = new int[6];

            switch (meshNo)
            {
                case 0:  //over the frame front
                    tris[0] = 16;
                    tris[1] = 12;
                    tris[2] = 14;
                    tris[3] = 14;
                    tris[4] = 18;
                    tris[5] = 16;
                    return tris;
                case 1: // left of frame front.
                    tris[0] = 2;
                    tris[1] = 3;
                    tris[2] = 10;
                    tris[3] = 10;
                    tris[4] = 14;
                    tris[5] = 2;
                    return tris;
                case 2:// right of frame front
                    tris[0] = 12;
                    tris[1] = 8;
                    tris[2] = 0;
                    tris[3] = 0;
                    tris[4] = 1;
                    tris[5] = 12;
                    return tris;

                case 3: // over the frame rear
                    tris[0] = 17;
                    tris[1] = 19;
                    tris[2] = 15;
                    tris[3] = 15;
                    tris[4] = 13;
                    tris[5] = 17;
                    return tris;

                case 4: // left of frame rear
                    tris[0] = 13;
                    tris[1] = 5;
                    tris[2] = 4;
                    tris[3] = 4;
                    tris[4] = 9;
                    tris[5] = 13;
                    return tris;

                case 5: // right of frame rear
                    tris[0] = 6;
                    tris[1] = 15;
                    tris[2] = 11;
                    tris[3] = 11;
                    tris[4] = 7;
                    tris[5] = 6;
                    return tris;

                case 6: //inner frame
                    {
                        tris = new int[18];

                        //right
                        tris[0] = 4;
                        tris[1] = 5;
                        tris[2] = 1;
                        tris[3] = 1;
                        tris[4] = 0;
                        tris[5] = 4;

                        //top
                        tris[6] = 5;
                        tris[7] = 6;
                        tris[8] = 2;
                        tris[9] = 2;
                        tris[10] = 1;
                        tris[11] = 5;

                        //left
                        tris[12] = 2;
                        tris[13] = 6;
                        tris[14] = 7;
                        tris[15] = 7;
                        tris[16] = 3;
                        tris[17] = 2;
                        return tris;
                    }
                case 7:
                    {
                        tris = new int[18];
                        tris[0] = 9;
                        tris[1] = 8;
                        tris[2] = 16;
                        tris[3] = 16;
                        tris[4] = 17;
                        tris[5] = 9;

                        tris[6] = 17;
                        tris[7] = 16;
                        tris[8] = 18;
                        tris[9] = 18;
                        tris[10] = 19;
                        tris[11] = 17;

                        tris[12] = 10;
                        tris[13] = 11;
                        tris[14] = 19;
                        tris[15] = 19;
                        tris[16] = 18;
                        tris[17] = 10;
                        return tris;
                    }
            }
            return base.ModelTriangles(meshNo);
        }

        public override Vector2[] ModelUVs(Vector3[] verts)
        {
            Vector2[] v = new Vector2[20];
            float distanceToFloor = (float)(32 - floorheight) * 0.15f; //distance to floor
            float distanceToFrameHead = distanceToFloor - (0.8125f * 1.2f);   //0.975

            float ceilingHeight = 32 * 0.15f;  //4.8f
            float floor = floorheight * 0.15f;
            distanceToFloor = ceilingHeight - floor;
            var vectorToFloor = distanceToFloor / 1.2f;
            var vectorToFrameHead = (vectorToFloor / distanceToFloor) * distanceToFrameHead;

            var frameV0 = 0.185f;
            var frameV1 = 1f - frameV0;//0.760416667f;

            v[0] = new Vector2(frameV1, vectorToFloor);
            v[3] = new Vector2(frameV0, vectorToFloor);
            v[4] = new Vector2(frameV0, vectorToFloor);
            v[7] = new Vector2(frameV1, vectorToFloor);
            v[8] = new Vector2(1f, vectorToFloor);
            v[9] = new Vector2(0f, vectorToFloor);
            v[10] = new Vector2(0f, vectorToFloor);
            v[11] = new Vector2(1f, vectorToFloor);

            //midpoint
            //1,2,5,6,12,13,14,15
            v[1] = new Vector2(frameV1, vectorToFrameHead);
            v[2] = new Vector2(frameV0, vectorToFrameHead);
            v[5] = new Vector2(frameV0, vectorToFrameHead);
            v[6] = new Vector2(frameV1, vectorToFrameHead);
            v[12] = new Vector2(1f, vectorToFrameHead);
            v[13] = new Vector2(0f, vectorToFrameHead);
            v[14] = new Vector2(0f, vectorToFrameHead);
            v[15] = new Vector2(1f, vectorToFrameHead);

            // //Top Vertices            
            v[16] = new Vector2(1, 0f);
            v[17] = new Vector2(0, 0f);
            v[18] = new Vector2(0, 0f);
            v[19] = new Vector2(1, 0f);

            return v;
        }

        public override ShaderMaterial GetMaterial(int textureno, int surface)
        {//Get the material texture from tmobj   
            if (surface != 6)
            {
                return tileMapRender.mapTexturesWalls.GetMaterialForObject(
                    textureno: texture, 
                    texturemap: UWTileMap.current_tilemap.texture_map, 
                    obj: uwobject);
            }
            else
            {
                return base.GetMaterial(0, 6);
            }
        }


    }//end class
} // end namespace