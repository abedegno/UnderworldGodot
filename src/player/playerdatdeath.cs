using System;

namespace Underworld
{
    //for handling loop updates for the player.
    public partial class playerdat : Loader
    {
        //public static bool PlayerInDeathMode;
        /// <summary>
        /// Handles player death.
        /// </summary>
        static void PlayerDeath()
        {
            // if (PlayerInDeathMode)
            // {
            //     return;
            // }
            // PlayerInDeathMode = true;
            if ((_RES != GAME_UW2) && (GetQuest(36) == 0))
            {//UW1: once the last talisman is destroyed the player cannot die and keeps 4 HP (player_is_dead, uw1 SKILLS.C:825-829).
                //Quest 36 is the talismans left, player byte 0x6D, which becomes 0xFF once the Slasher of Veils is gone (SKILLS.C:714).
                play_hp = 4;
                return;
            }
            playerdat.play_drawn = 0;

            if (_RES == GAME_UW2)
            {
                PlayerDeathUW2();
            }
            else
            {
                //uw1
                PlayerDeathUW1();
            }
        }


        /// <summary>
        /// if silver tree planted. do the tree animation. 
        /// respawn at tree level via a delegate function.        
        /// otherwise return to main menu and Start death animation.
        /// </summary>
        private static void PlayerDeathUW1()
        {
            UWsoundeffects.KillAllEffects(); // uw1 SKILLS.C:830, seg014_1DC5_C7C
            if (SilverTreeDungeon != 0)
            {
                //resurrect at silver tree
                uimanager.EnableDisable(uimanager.instance.uwviewport,false); 
                cutsplayer.PlayCutscene(CutsceneNo: 0x102, callBackMethod: ResurrectAtSilverTree, useSingleRedChannel: true);
            }
            else
            {
                RealDeath();
            }
        }

        /// <summary>
        /// The end of the game, real_death(1) (uw1 UWEDIT.C:393-424, uw2 UWEDIT.C:408-439): the death
        /// cutscene 0x103, then back to the main menu.
        /// </summary>
        static void RealDeath()
        {
            uimanager.EnableDisable(uimanager.instance.uwviewport,false); 
            cutsplayer.PlayCutscene(CutsceneNo: 0x103, callBackMethod: uimanager.ReturnToMainMenu, useSingleRedChannel: _RES != GAME_UW2);
        }

        /// <summary>
        /// The experience change death costs the player, a negative number: an eighth in UW1
        /// (player_get_exp(-(int)(player->exp >> 3)), uw1 SKILLS.C:832) and a ninth in UW2
        /// (player_get_exp(-(int)(player->exp / 9)), uw2 SKILLS.C:844), truncated before it is negated.
        /// </summary>
        public static int DeathExperienceChange()
        {
            if (_RES == GAME_UW2)
            {
                return -(Exp / 9);
            }
            else
            {
                return -(Exp >> 3);
            }
        }

        public static void ResurrectAtSilverTree()
        {
            uimanager.EnableDisable(uimanager.instance.uwviewport,true); 
            ChangeExperience(DeathExperienceChange());
            ResurrectionEffects();
            Teleportation.CodeToRunOnTeleport = TeleportToSilverTree;
            Teleportation.Teleport(
                character: 0,
                tileX: 0x3F, tileY: 0x3F,//no square: CodeToRunOnTeleport sets it, so no search for room here (uw1 SKILLS.C:863)
                newLevel: SilverTreeDungeon,
                HeadingHeightFlag: 0);
        }

        private static bool PlayerDeathUW2()
        {
            //If in Britannia.
            //Check if was killed by a castle inhabitant.get sent to jail. otherwise die.
            //else If in other dimension.
            //Check if dreaming in void. then so wake up where you were sleeping
            //If dueling in the pits then lose duel
            //respawn in gem room.
            if (dungeon_level == 1)
            {
                //playerObject.ProjectileSourceID = 235;  // for testing against LB
                //Check if was killed by an ally
                if (playerObject.ProjectileSourceID != 0)
                {
                    var killer = UWTileMap.current_tilemap.LevelObjects[playerObject.ProjectileSourceID];
                    if (killer != null)
                    {   //was avatar killed by a castle inhabitant or Syria or a castle guard.
                        if (
                            ((killer.npc_whoami >= 0x81) && (killer.npc_whoami < 0x8F))
                            ||
                            (killer.npc_whoami == 0xA8)
                            ||
                            (killer.npc_whoami == 0x95)
                            )
                        {
                            if (killer.UnkBit_0XA_Bit7 == 0)
                            {
                                ResurrectionEffects();
                                //send to jail
                                Teleportation.CodeToRunOnTeleport = GoToJail;
                                if (Teleportation.Teleport(
                                    character: 0,
                                    tileX: 42, tileY: 38,
                                    newLevel: 1,
                                    HeadingHeightFlag: 0) != 0x10)
                                {//put_player_in_jail carries on where the player is when do_teleport fails (uw2 WORLDEV.C:2296-2314)
                                    GoToJail();
                                }
                                return false;
                            }
                        }
                    }
                }

            }

            if (DreamingInVoid)
            {
                //check if died while dreaming
                sleep.AwakenFromTheVoid();
                return false;
            }

            UWsoundeffects.KillAllEffects(); // uw2 SKILLS.C:842, once jail and the Void are ruled out

            if (CurrentWorld != 0)
            {
                //respawn in the gem chamber                        
                if (IsFightingInPit)
                {
                    SetQuest(129, 0);//arena win record
                    SetQuest(133, 0);//jospur debt
                                     //clear fighters
                    for (int i = 0; i < 5; i++)
                    {
                        SetPitFighter(i, 0);//clear opponents.
                    }
                    IsFightingInPit = false;
                }
                ResurrectionEffects();
                Teleportation.CodeToRunOnTeleport = ResurrectAtBlackrockGem;
                Teleportation.Teleport(
                    character: 0,
                    tileX: 0x3F, tileY: 0x3F,//no square: ResurrectAtBlackrockGem sets it (uw2 SKILLS.C:853-854)
                    newLevel: 5,
                    HeadingHeightFlag: 0);
            }
            else
            {
                RealDeath();
            }

            return true;
        }




        /// <summary>
        /// UW1's do_resurrect (uw1 SKILLS.C:790-806), run as the teleport callback once the tree's level is loaded:
        /// the square is the silver tree's (moveto, which leaves 0, 0 when there is none, SKILLS.C:769-784), and
        /// new_player_pos then searches for room round it. With no room even after clearing, player_is_dead
        /// ends the game (real_death(1), SKILLS.C:863-875). The hit points were set by ResurrectionEffects.
        /// </summary>
        static void TeleportToSilverTree()
        {
            Teleportation.CodeToRunOnTeleport = null;
            //PlayerInDeathMode = false;
            int tileX = 0, tileY = 0;
            var tree = objectsearch.FindMatchInFullObjectList(7, 0, 0xA, UWTileMap.current_tilemap.LevelObjects);
            if (tree != null && UWTileMap.ValidTile(tree.tileX, tree.tileY))
            {
                tileX = tree.tileX; tileY = tree.tileY;
            }
            if (!Teleportation.PlacePlayerNear(tileX, tileY))
            {
                RealDeath();
            }
        }


        /// <summary>
        /// Avatar will respawn at the black rock if they die in another world. UW2's do_gem (uw2 SKILLS.C:663-711),
        /// run as the teleport callback once level 5 is loaded: the square is 0x1C, 0x28 moved by
        /// move_along(rand(), 8), and a blood stain is left on it if it is an open square. new_player_pos then
        /// searches for room round it, and with no room even after clearing, player_is_dead ends the game
        /// (real_death(1), SKILLS.C:853-865). The hit points were set by ResurrectionEffects.
        /// </summary>
        static void ResurrectAtBlackrockGem()
        {
            ChangeExperience(DeathExperienceChange());
            Teleportation.CodeToRunOnTeleport = null;
            //PlayerInDeathMode = false;
            int x = 0x1C, y = 0x28;
            int xoff = Rng.RandMod(5) + 1;
            int yoff = Rng.RandMod(5) + 1;
            motion.GetCoordinateInDirection(heading: Rng.Source.Rand(), distance: 8, X0: ref x, Y0: ref y);
            var tile = UWTileMap.current_tilemap.Tiles[x, y];
            if (tile.tileType == UWTileMap.TILE_OPEN)
            {// put_at with a range of 0 leaves it at this spot; the original then sets its fine x from yoff and its fine y from xoff
                ObjectCreator.spawnObjectInTile(0xDF, x, y, (short)yoff, (short)xoff, (short)(tile.floorHeight << 3));
            }
            if (Teleportation.PlacePlayerNear(x, y))
            {
                uimanager.AddToMessageScroll(GameStrings.GetString(1, 0x169));
            }
            else
            {
                RealDeath();
            }
        }

        /// <summary>
        /// Avatar goes to jail if killed while fighting the inhabitents of the castle.
        /// (if this happens before talking to LB the game will leave the avatar permanently in jail), 
        /// otherwise Lord British will teleport in to free the avatar
        /// </summary>
        static void GoToJail()
        {
            Teleportation.CodeToRunOnTeleport = null;
            play_hp = STR;//hit points set to strength (uw2 WORLDEV.C:2287)
            ChangeExperience(DeathExperienceChange());//loss of exp
            //PlayerInDeathMode = false;
            playerdat.playerObject.npc_animation = 1;
            XMIMusic.ChangeThemeMusic(0xA);//play depressed theme
            
            //make humans friendly, set quests, run a trap, advance the clock and move LB to the prison to release you.
            uwObject.ResetMobileObjects();
            
            var newtime = (ClockValue / 0x3C00) % 20;
            newtime = Math.Abs(0x28 - newtime);
            newtime = newtime * 0x3C;
            newtime = newtime - (Rng.r.Next(0x5A) + 0x3C);//the random part is subtracted (uw2 WORLDEV.C:2295)
            AdvanceTime(newtime);
            
            CallBacks.RunCodeOnRace(npc.set_attitude_by_array, 0x1C, new int[] { 2 }, true);
            CallBacks.RunCodeOnRace(npc.set_goal_and_target_by_array, 0x1C, new int[] { 0, 1 }, true); // goal=1, gtarg=0
            
            SetQuest(112, 1);//avatar has been fighting.
            
            //uimanager.AddToMessageScroll(GameStrings.GetString(9, 4));//you awaken in jail message is printed by a text_string trap

            //Find LB and move him to outside the jail doorway.
            uwObject LBritish = null;
            for (int i = 1; i < 256; i++)
            {//find lord british
                LBritish = UWTileMap.current_tilemap.LevelObjects[i];
                if (LBritish.npc_whoami == 0x8E)
                {
                    break;
                }
                LBritish = null;
            }
            if (LBritish != null)
            {
                npc.moveNPCToTile(critter: LBritish, destTileX: 40, destTileY: 35); //vanilla is 42,34
                LBritish.quality = 40; //0x28;
                LBritish.owner = 38; //0x27;
                LBritish.npc_goal = 1;
            }

            trigger.TriggerTrapInTile(39, 37);

            //put weapons away.
            playerdat.PutWeaponAway();

            //Close doors in tile 0x2A, 0x26
            var tile = UWTileMap.current_tilemap.Tiles[0x2A, 0x26];
            var doorObj = objectsearch.FindMatchInTile(tileX: 0x2A, tileY: 0x26, majorclass: 5, minorclass: 0, classindex: -1);
            if (doorObj != null)
            {
                if (doorObj.classindex > 0x8)
                {
                    door.CloseDoor(doorObj);    
                }                
            }
        }



        /// <summary>
        /// Processes standard character changes when resurrected.
        /// </summary>
        static void ResurrectionEffects()
        {
            ResurrectionHPRestoration();
            ResurrectionManaRestoration();
            play_poison = 0;
            ActiveSpellEffectCount = 0;
            AutomapEnabled = true;
        }

        /// <summary>
        /// Restores an amount of HP on resurrection
        /// </summary>
        static void ResurrectionHPRestoration()
        {
            if (max_hp > 8)
            {
                play_hp = max_hp - 2 - Rng.RandScale(3);//rand() * 3L / 0x8000L (uw1 SKILLS.C:794, uw2 SKILLS.C:686)
            }
            else
            {
                play_hp = max_hp;
            }
        }

        static void ResurrectionManaRestoration()
        {
            if (max_mana > 8)
            {
                play_mana = max_mana - (2 + max_mana/8);
            }
            else
            {
                play_mana = max_mana;
            }
        }

    }//end class
}//end namespace