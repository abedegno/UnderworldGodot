using System;
using System.Diagnostics;
using System.Linq;
using Godot;

namespace Underworld
{
    public partial class npc : objectInstance
    {
        //public static int TESTITEM_ID = 74;
        Label3D myLabel;

        /// <summary>
        /// global shader for npcs.
        /// </summary>
        public static Shader textureshader;

        /// <summary>
        /// Mesh this sprite is drawn on
        /// </summary>
        public uwMeshInstance3D sprite;
        // public uwMeshInstance3D sprite_xfer;
        // public uwMeshInstance3D sprite_objectinfo;

        /// <summary>
        /// The material for rendering this unique npc
        /// </summary>
        public ShaderMaterial material;
        // public ShaderMaterial material_xfer;
        // public ShaderMaterial material_objectinfo;

        /// <summary>
        /// Used to pick the sprite at a particular angle.
        /// </summary>
        static byte[] NPCAnimationHeadingTable = { 0x0, 0x0, 0x0, 0x1, 0x1, 0x1, 0x2, 0x2, 0x2, 0x2, 0x2, 0x3, 0x3, 0x3, 0x4, 0x4, 0x4, 0x4, 0x4, 0x5, 0x5, 0x5, 0x6, 0x6, 0x6, 0x6, 0x6, 0x7, 0x7, 0x7, 0x0, 0x0 };

        /// <summary>
        /// Possible AI goals
        /// </summary>
        public enum npc_goals : byte
        {
            npc_goal_stand_still_0 = 0,
            npc_goal_goto_1 = 1,
            npc_goal_wander_2_confusion = 2,  //used in confusion spell
            npc_goal_follow = 3,
            npc_goal_wander_4 = 4, //possibly this should be another standstill goal
            npc_goal_attack_5 = 5,
            npc_goal_fear_6 = 6,  //goal appears to be attack at a distance using ranged weapons, but also fear??
            npc_goal_stand_still_7 = 7, //same hehaviour as 0
            npc_goal_charmed_8 = 8, //8 is the goal the npc gets when charmed, castle npcs have this too.
            npc_goal_attack_9 = 9, //goal appears to also be attack at a distance, possibly using magic attacks
            npc_goal_want_to_talk = 10,
            npc_goal_stand_still_11 = 11, //This goal is only seen in ethereal void creatures. 0xB
            npc_goal_stand_still_12 = 12,
            npc_goal_unk13 = 13,
            npc_goal_unk14 = 14,
            npc_goal_petrified = 15
        };


        public npc(uwObject _uwobject)
        {
            uwobject = _uwobject;
            try
            {
                SetAnimSprite(animationNo: uwobject.npc_animation, frameNo: uwobject.AnimationFrame, relativeHeading: CalculateFacingAngleToNPC(uwobject));
            }
            catch (Exception ex)
            {
                Debug.Print($"{ex.ToString()}");
            }
        }

        /// <summary>
        /// Creates a rendered version of this object in the gameworld
        /// </summary>
        /// <param name="parent"></param>
        /// <param name="obj"></param>
        /// <returns></returns>
        public static npc CreateInstance(Node3D parent, uwObject obj, string name)
        {
            //obj.item_id = TESTITEM_ID;
            //obj.npc_hp = 1;
            var n = new npc(obj);

            var a_sprite = new uwMeshInstance3D(); //new Sprite3D();
            a_sprite.Name = name;
            a_sprite.Mesh = new QuadMesh();
            a_sprite.Mesh.SurfaceSetMaterial(0, n.material);
            a_sprite.Mesh.Set("size", n.FrameSize);
            n.sprite = a_sprite;
            parent.AddChild(a_sprite);
            a_sprite.Position = new Vector3(0, n.FrameSize.Y / 2 + 0.12f, 0);
            //a_sprite.CreateConvexCollision();
            n.SetAnimSprite(animationNo: obj.npc_animation, frameNo: obj.AnimationFrame, relativeHeading: CalculateFacingAngleToNPC(obj));

            if (ObjectCreator.printlabels)
            {
                string animname;
                animname = CritterArt.GetAnimName(obj.npc_animation, CalculateFacingAngleToNPC(obj));
                Label3D obj_lbl = new();
                obj_lbl.Text = $"{name} {obj.item_id & 0x3F} \nAnim={obj.npc_animation} Frame={obj.AnimationFrame} {animname}\n Goal {obj.npc_goal} Gtarg{obj.npc_gtarg}\nHP:{obj.npc_hp}/{critterObjectDat.avghit(obj.item_id)}";
                obj_lbl.Font = uimanager.instance.Font4X5P;
                obj_lbl.FontSize = 16;
                obj_lbl.Billboard = BaseMaterial3D.BillboardModeEnum.Enabled;
                obj_lbl.Position = new Vector3(0f, 0.6f, 0f);
                parent.AddChild(obj_lbl);
                n.myLabel = obj_lbl;
            }
            obj.instance = n;
            return n;
        }

        static npc()
        {
            textureshader = (Shader)ResourceLoader.Load("res://resources/shaders/uwnpc_allred.gdshader");
        }

        public void SetAnimSprite(int animationNo, short frameNo, int relativeHeading)
        {
            if (_RES != GAME_UW2)
            {
                SetAnimSpriteUW1(animationNo, frameNo, relativeHeading);
                return;
            }
            if (uwobject.AnimationFrame >= 8)
            {
                uwobject.AnimationFrame = 0;
            }
            string animname = CritterArt.GetAnimName(animationNo, relativeHeading); // "idle_front";
            var crit = CritterArt.GetCritter(
                CritterToLoad: this.uwobject.item_id & 0x3F, 
                GetStoneArt: (_RES == GAME_UW2) && (this.uwobject.npc_goal== (byte)npc.npc_goals.npc_goal_petrified));
            if (crit.Animations.ContainsKey(animname))
            {
                uwobject.AnimationFrame = (byte)ApplyCritterAnimation(obj: uwobject, animationNo: animationNo, frameNo: frameNo, animname: animname, crit: crit);
            }
            else
            {
                if ((animationNo == ANIMATION_COMBAT_SLASH) || (animationNo == ANIMATION_COMBAT_STAB))
                {
                    animationNo = ANIMATION_COMBAT_BASH;//fallback to bash when unable to load slash or stab.
                }
                else
                {
                    uwobject.npc_animation = 0; //default animation to zero;
                    Debug.Print($"{animname} ({animationNo}) was not found for {this.uwobject.a_name}");
                }

                uwobject.AnimationFrame = (byte)ApplyCritterAnimation(obj: uwobject, animationNo: animationNo, frameNo: frameNo, animname: CritterArt.GetAnimName(0, 0), crit: crit);
            }
            if (ObjectCreator.printlabels)
            {
                if (myLabel != null)
                {
                    myLabel.Text = $"{uwobject.a_name} {uwobject.item_id & 0x3F} \nAnim={uwobject.npc_animation} Frame={uwobject.AnimationFrame} {animname}\n Goal {uwobject.npc_goal} Gtarg{uwobject.npc_gtarg}\nHP:{uwobject.npc_hp}/{critterObjectDat.avghit(uwobject.item_id)}";
                }
            }
        }

        /// <summary>
        /// Draws a UW1 critter the way do_obj and do_uwcrit do, without changing its sequence or
        /// frame: the AI counts its frames whatever the art holds. A sequence below 0x20 other than
        /// dying is drawn only when the critter faces the viewer, else its standing view
        /// (uw1 DRAWOBJ.C:214-217). A frame the animation lacks draws the first image of its page,
        /// and a sequence with no animation draws nothing (uw1 PGCACHE.ASM:917-934).
        /// </summary>
        private void SetAnimSpriteUW1(int animationNo, short frameNo, int relativeHeading)
        {
            if ((animationNo < 0x20) && (animationNo != ANIMATION_DEATH) && (((relativeHeading + 5) & 7) >= 3))
            {
                animationNo = ANIMATION_IDLE;
            }
            string animname = CritterArt.GetAnimName(animationNo, relativeHeading);
            var crit = CritterArt.GetCritter(this.uwobject.item_id & 0x3F);
            SetCritterMaterial(uwobject);
            if (crit.Animations.TryGetValue(animname, out var anim))
            {
                ShowCritterSprite(crit, (frameNo < 8) && (anim.animIndices[frameNo] != -1) ? anim.animIndices[frameNo] : anim.pageFirstSprite);
                if (sprite != null)
                {
                    sprite.Visible = true;
                }
            }
            else if (sprite != null)
            {
                sprite.Visible = false;
            }
            if (ObjectCreator.printlabels)
            {
                if (myLabel != null)
                {
                    myLabel.Text = $"{uwobject.a_name} {uwobject.item_id & 0x3F} \nAnim={uwobject.npc_animation} Frame={uwobject.AnimationFrame} {animname}\n Goal {uwobject.npc_goal} Gtarg{uwobject.npc_gtarg}\nHP:{uwobject.npc_hp}/{critterObjectDat.avghit(uwobject.item_id)}";
                }
            }
        }

        private void SetCritterMaterial(uwObject obj)
        {
            if (material == null)
            {//create the initial material
                var newmaterial = new ShaderMaterial();
                newmaterial.Shader = textureshader;

                newmaterial.SetShaderParameter("albedo", new Color(1, 1, 1, 1));
                newmaterial.SetShaderParameter("uv1_scale", new Vector3(1, 1, 1));
                newmaterial.SetShaderParameter("uv2_scale", new Vector3(1, 1, 1));
                newmaterial.SetShaderParameter("UseAlpha", true);
                newmaterial.SetShaderParameter("objectindex_lowerbytes", obj.index & 0xFF);
                newmaterial.SetShaderParameter("objectindex_upperbytes", (obj.index>>8) & 0xFF);                
                material = newmaterial;
            }
        }

        /// <summary>
        /// Shows the critter's sprite spriteNo.
        /// </summary>
        private void ShowCritterSprite(CritterArt crit, int spriteNo)
        {
            //Render nonxfer bits
            var texture = crit.animSprites[spriteNo];
            FrameSize = new Vector2(
                ArtLoader.NPCSpriteScale * texture.GetWidth(),
                ArtLoader.NPCSpriteScale * texture.GetHeight()
                );
            material.SetShaderParameter("texture_albedo", (Texture)texture);
            if (sprite != null)
            {
                sprite.Mesh.Set("size", FrameSize);
                sprite.Layers = main.LayerGeo | main.LayerObjectInfo | main.LayerXFER;
            }
        }

        private short ApplyCritterAnimation(uwObject obj, int animationNo, short frameNo, string animname, CritterArt crit)
        {
            var anim = crit.Animations[animname];
            SetCritterMaterial(obj);

            //assign the params to the shader
            if (frameNo >= 8) { frameNo = 0; }
            if (anim.animIndices[frameNo] == -1)
            {
                frameNo = 0;
            }

            if (anim.animIndices[frameNo] != -1)
            {
                ShowCritterSprite(crit, anim.animIndices[frameNo]);
                return frameNo;
            }
            else
            {
                Debug.Print($"invalid animation {animationNo} {frameNo} for {this.uwobject.item_id}");
            }
            return 0;
        }


        public static void RedrawAnimation(uwObject n)
        {
            short CalcedFacing = CalculateFacingAngleToNPC(n);
            var np = (npc)(n.instance);
            if (np != null)
            {
                np.SetAnimSprite(n.npc_animation, n.AnimationFrame, CalcedFacing);
            }
        }

        /// <summary>
        /// Works out which animation angle to use based on player camera and npc heading.
        /// </summary>
        /// <param name="n"></param>
        /// <returns></returns>
        public static short CalculateFacingAngleToNPC(uwObject n)
        {
            var ax = (playerdat.CameraPointer2C + motion.PlayerCardinalHeadingLookupTable[playerdat.CameraYawHeadingRelated_2B52]) >> 0xB;
            ax = ((0x20 + (n.heading << 2)) - ax) & 0x1F; //index to lookup an animation table.
            var result = NPCAnimationHeadingTable[ax];
            //Debug.Print($"npc {n.a_name} {n.heading} -> {result}");
            return result;
        }

        public static bool LookAt(uwObject critter)
        {
            if ((critter.npc_whoami >= 240) && (critter.npc_whoami != 248))//Ethereal void creatures 
            {
                EtheralVoidNPCDescription(critter);
            }
            else
            {
                if ((_RES != GAME_UW2) && (critter.npc_whoami == 248))
                {//slasher of veils
                    var name = critter.a_name;
                    uimanager.AddToMessageScroll($"You see {name}");
                }
                else
                {
                    RegularNPCDescription(critter);
                }
            }
            return true;
        }

        /// <summary>
        /// Describes Mr Jaws and his friends
        /// </summary>
        /// <param name="critter"></param>
        private static void EtheralVoidNPCDescription(uwObject critter)
        {
            if (_RES == GAME_UW2)
            {
                var id = critter.npc_animation + 277;
                var name = GameStrings.GetString(1, id);
                uimanager.AddToMessageScroll($"You see {name}");
            }
            else
            {
                uimanager.AddToMessageScroll($"You see {critter.a_name.Replace("_", " ")}");
            }
        }

        /// <summary>
        /// Describes an NPC in terms of their mood, race and name
        /// </summary>
        /// <param name="critter"></param>
        private static void RegularNPCDescription(uwObject critter)
        {
            var name = critter.a_name;
            var lowercasename = char.IsLower(name.First<char>());//check if name is lower case. if so do not print it
            string npcrace = GameStrings.GetObjectNounUW(critter.item_id);
            var mood = GameStrings.GetString(5, 96 + critter.npc_attitude);

            var article = "a";
            switch (mood.ToUpper().Substring(0, 1))
            {
                case "A":
                case "E":
                case "I":
                case "O":
                case "U":
                    article = "an"; break;

            }
            if ((critter.npc_whoami != 0))
            {
                if (lowercasename)
                {//print the whoami as the race only
                    uimanager.AddToMessageScroll($"You see {article} {mood} {name}");
                }
                else
                {//print race
                    uimanager.AddToMessageScroll($"You see {article} {mood} {npcrace} named {name}");
                }
            }
            else
            {
                uimanager.AddToMessageScroll($"You see {article} {mood} {npcrace}");
            }
        }

        /// <summary>
        /// Gets a list of spells and properties the npc has.
        /// </summary>
        /// <param name="critter"></param>
        /// <param name="prop"></param>
        /// <returns></returns>
        public static bool ListNPCProperties(uwObject critter, out string propertystring)
        {
            var propertycount = 0;
            byte[] prop = new byte[] { 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff };
            propertystring = "";
            if (critterObjectDat.NPCPower_0x2DBits1To7(critter.item_id) != 0)
            {
                int si = 0;

                for (si = 0; si < 3; si++)
                {//read in the spells
                    prop[si] = (byte)critterObjectDat.spell(critter.item_id, si);
                }
                if (critterObjectDat.generaltype(critter.item_id) == 0x17)
                {//liches?
                    if (critterObjectDat.isFlier(critter.item_id))
                    {
                        prop[si++] = 0x39;//can cast fly
                    }
                    if (critterObjectDat.dooropenskill(critter.item_id) >= 0x2D)
                    {
                        prop[si++] = 0x23;//can cast open
                    }
                    var testdam = 1;
                    var scale = damage.ScaleDamage(critter.item_id, ref testdam, 8);

                    if (scale == 0)
                    {
                        prop[si++] = 0x1C; //can cast flameproof
                    }

                    if (critterObjectDat.NPCPower_0x2DBits1To7(critter.item_id) >= 0x19)
                    {
                        if (critterObjectDat.level(critter.item_id) >= 9)
                        {
                            prop[si++] = 0x3b; //can cast ironflesh
                            propertycount++;
                        }
                    }
                }

                //Make sure properties are unique
                if (prop[0] == prop[1])
                {
                    prop[1] = 0xff;
                }
                else
                {
                    if (prop[1] == prop[2])
                    {
                        prop[1] = 0xff;
                    }
                }
                if (prop[0] == prop[2])
                {
                    prop[2] = 0xff;
                }

                for (si = 0; si < 8; si++)
                {
                    if (prop[si] != 0xff)
                    {
                        propertycount++;
                    }
                }
                if (propertycount == 0)
                {
                    return false;
                }
                else
                {
                    var propertiesremaining = propertycount - 1;
                    for (si = 0; si < 8; si++)
                    {
                        //loop the properties and turn them into spellnames
                        if (prop[si] != 0xff)
                        {
                            int major = (prop[si] & 0xC0) >> 6;
                            int minor = prop[si] & 0x3F;
                            if (major == 0)
                            {
                                var spell = RunicMagic.SpellList[prop[si]];
                                if (spell.SpellMajorClass == 4)
                                {//if healing? and a golem??
                                    if (critterObjectDat.generaltype(critter.item_id) == 0xF)
                                    {
                                        major = 1; minor = 6;//bouncing???
                                    }
                                }
                            }
                            if (major <= 0)
                            {
                                minor += 0x100;
                            }
                            else
                            {
                                minor += ((major + 0xC0) << 4);
                            }

                            propertystring += GameStrings.GetString(6, minor);
                            if (propertiesremaining >= 2)
                            {
                                propertystring += ", ";
                            }
                            else
                            {
                                if ((propertiesremaining == 1) && (propertycount > 1))
                                {
                                    propertystring += GameStrings.GetString(0x274); //AND
                                }
                            }
                            propertiesremaining--;
                        }
                    }
                }
            }
            return (propertycount > 0);
        }


        /// <summary>
        /// Breaks down the angle in the the facing sector. Clockwise from 0)
        /// </summary>
        /// <param name="angle">Angle.</param>
        static short facing(float angle)
        {
            if ((angle >= -22.5) && (angle <= 22.5))
            {
                return 0;//*AnimRange;//Facing forward
            }
            else
            {
                if ((angle > 22.5) && (angle <= 67.5))
                {//Facing forward left
                    return 1;//*AnimRange;
                }
                else
                {
                    if ((angle > 67.5) && (angle <= 112.5))
                    {//facing (right)
                        return 2;//*AnimRange;
                    }
                    else
                    {
                        if ((angle > 112.5) && (angle <= 157.5))
                        {//Facing away left
                            return 3;//*AnimRange;
                        }
                        else
                        {
                            if (((angle > 157.5) && (angle <= 180.0)) || ((angle >= -180) && (angle <= -157.5)))
                            {//Facing away
                                return 4;//*AnimRange;
                            }
                            else
                            {
                                if ((angle >= -157.5) && (angle < -112.5))
                                {//Facing away right
                                    return 5;//*AnimRange;
                                }
                                else
                                {
                                    if ((angle > -112.5) && (angle < -67.5))
                                    {//Facing (left)
                                        return 6;//*AnimRange;
                                    }
                                    else
                                    {
                                        if ((angle > -67.5) && (angle < -22.5))
                                        {//Facing forward right
                                            return 7;//*AnimRange;
                                        }
                                        else
                                        {
                                            return 0;//*AnimRange;//default
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }


        /// <summary>
        /// When sleeping or other circumstances certain npcs will be returned to their home tiles.
        /// Will take a count of how many npcs of the given general type are hostile or friendly.
        /// </summary>
        /// <param name="obj"></param>
        /// <param name="generaltypes"></param>
        public static void ReturnNPCToHomeTile(uwObject obj, ref int[] generaltypes)
        {
            if (!UWTileMap.ValidTile(obj.tileX, obj.tileY))
            {
                return;
            }
            if (obj.SpawnedCritter_0XD_Bit8 == 0)
            {
                obj.UnkBit_0x19_7 = 0;
                obj.IsAlly = 0;
                obj.UnkBit_0x19_4 = 0;
                obj.UnkBit_0x19_5 = 0;
                obj.UnkBit_0x19_0_likelyincombat = 0;
                obj.UnkBit_0x19_1 = 0;//whether or not the move home succeeds (uw1 CRITTIME.C:158, uw2 CRITTIME.C:156)
                if (_RES != GAME_UW2 || obj.npc_goal != (byte)npc.npc_goals.npc_goal_want_to_talk)
                {//UW1 turns every critter, UW2 all but one waiting to talk (uw1 CRITTIME.C:159, uw2 CRITTIME.C:157-158)
                    obj.heading = (short)(Rng.r.Next(8) & 7);
                }
                var avghit = critterObjectDat.avghit(obj.item_id);
                if (obj.npc_hp < avghit)
                {
                    if (obj.UnkBit_0XD_Bit9_StopHPRegen == 0)
                    {
                        obj.npc_hp = (byte)((obj.npc_hp + avghit) / 2);//half way to the average (uw1 CRITTIME.C:160-161, uw2 CRITTIME.C:159-160)
                    }
                }
                if (obj.UnkBit_0XA_Bit7 == 0)
                {
                    var generaltype = critterObjectDat.generaltype(obj.item_id);
                    switch (obj.npc_attitude)
                    {
                        case 0:
                            generaltypes[generaltype]--;
                            break;
                        case 3:
                            generaltypes[generaltype]++;
                            break;
                    }
                }

                if ((obj.quality != obj.npc_xhome) || (obj.owner != obj.npc_yhome))
                {//go to the home square in quality and owner; npc_xhome and npc_yhome are the square it stands on (uw1 CRITTIME.C:172-192, uw2 CRITTIME.C:171-192)
                    npc.moveNPCToTile(obj, obj.quality, obj.owner);
                }

            }
            else
            {
                //remove temporary spawned npcs
                // DOS frees the critter and everything it carries, Obj_FreeLinkChain (uw1
                // CRITTIME.C:149-150, uw2 CRITTIME.C:147-148). Freeing the critter alone left
                // its inventory allocated and in no list.
                ObjectRemover.RemoveObjectAndContentsFromTile(obj);
            }
        }

    }//end class
}//end namespace