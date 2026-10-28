using System;
using System.Diagnostics;
using System.Linq;
namespace Underworld
{
    /// <summary>
    /// Class for managing the ai death special cases
    /// </summary>
    public partial class npc : objectInstance
    {

        /// <summary>
        /// Handles special death cases for npcs.
        /// </summary>
        /// <param name="critter"></param>
        /// <param name="mode">0 when initial killing blow, 1 when at end of death animation</param>
        /// <returns>true if NPC should die, otherwise false to stay alive</returns>
        public static bool SpecialDeathCases(uwObject critter, int mode = 0)
        {
            switch (_RES)
            {
                case GAME_UW2:
                    return SpecialDeathCasesUW2(critter, mode);
                default:
                    return SpecialDeathCasesUW1(critter, mode);
            }
        }

        public static bool SpecialDeathCasesUW1(uwObject critter, int mode = 0)
        {
            switch (critter.npc_whoami)
            {
                case 0xB://Thorlson (cut content npc)
                    {
                        if (mode == 0)
                        {
                            talk.Talk(critter, true);
                            critter.npc_hp = 0x3C;//thorlson is unstoppable, once enraged nothing can kill him!
                            return false;
                        }
                        else
                        {
                            return true;
                        }
                    }
                case 0x16://golem
                    {
                        if (mode == 0)
                        {
                            if (critter.TileState_0XA_Bit456 == 0)
                            {
                                critter.TileState_0XA_Bit456 = 1;
                                playerdat.ChangeExperience(500);
                            }
                            combat.EndCombatLoop();
                            critter.ProjectileSourceID = 0; //forget that the player has hit them.
                            critter.npc_animation = 32;//?
                            critter.AnimationFrame = 0;
                            talk.Talk(critter, true);
                            return false;
                        }
                        else
                        {
                            return true;
                        }
                    }
                case 0x18://prisoner (murgo)
                    {
                        if (mode == 1)
                        {
                            playerdat.SetQuest(6, 1);
                        }
                        return true;
                    }
                case 0x1B://garamon
                    {
                        if (mode == 1)
                        {
                            playerdat.CanTalismansBeDestroyed = false;//can talismans be thrown == false (talisman_ok, uw1 WORLDEV.C:558-559)
                        }
                        return true;
                    }
                case 0x6E://gazer
                    {
                        if (mode == 1)
                        {
                            playerdat.SetQuest(4, 1);
                        }
                        return true;
                    }

                case 0x8E://Rodrick
                    {
                        if (mode == 1)
                        {
                            playerdat.SetQuest(11, 1);
                        }
                        return true;
                    }
                case 0xE7://tybal
                    {
                        if (mode == 1)
                        {
                            TybalDeath(critter);
                        }
                        return true;
                    }


                case 0://generic npcs
                default:
                    return true;//to die
            }
        }



        public static bool SpecialDeathCasesUW2(uwObject critter, int mode = 0)
        {
            bool WasPitFighter = false;
            if (mode != 0)
            {//handle some initial events
                if (CheckIfMatchingRaceUW2(critter, 0xB))//a_trilkhun&trilkhai
                {
                    //set attitude to 0 on all members of race 0xB
                    SetRaceAttitude(targetrace: 0xB, newAttitute: 0);//uw2 WORLDEV.C:707-708
                }
                if ((playerdat.dungeon_level == 4) && (critter.item_id == 0x4E))
                {//killing worms in the sewers
                    playerdat.SetQuest(135, Math.Min(playerdat.GetQuest(135) + 1, 200));
                }
                if (playerdat.IsFightingInPit)
                {
                    if (pitsofcarnage.IsDuelingAgainstCritter(critter.index))
                    {
                        if (pitsofcarnage.RemovePitFighter(critter.index))
                        {
                            WasPitFighter = true;
                            playerdat.SetQuest(129, playerdat.GetQuest(129) + 1); //if enough duels are fought the pit counter will overflow!
                            if (critter.npc_whoami != 0x64)
                            {//no honour in defeating krillner..., note krillners special case will decrease quest 129 by 1.
                                playerdat.SetQuest(24, 1);
                            }
                        }
                    }
                }
            }




            if
                (
                    (mode == 0) && (critter.UnkBit_0XA_Bit7 == 0)
                    &&
                    (
                        (critter.npc_whoami >= 0x81) && (critter.npc_whoami <= 0x8F)
                        ||
                        (critter.npc_whoami == 0xA8)
                        ||
                        (critter.npc_whoami == 0x95)
                    )
                )
            {
                //friendly britannian npcs
                if ((critter.npc_whoami == 0x8D) && (playerdat.GetXClock(1) >= 8))//lady tori
                {
                    return true; //allow her to die only after the xclock reaches 8
                }
                //revive
                var newhp = (critterObjectDat.avghit(critter.item_id) / 3) - 1;
                critter.npc_hp = (byte)newhp;
                trigger.RunScheduledTriggerInTile_15_29AndSpawnGuards(
                    xArg: critter.npc_xhome,
                    yArg: critter.npc_yhome);
                return false;//do not kill
            }
            else
            {
                //the others
                switch (critter.npc_whoami)
                {
                    case 6://Bishop
                        {
                            if (mode != 0)
                            {
                                playerdat.SetQuest(4, 1);//uw2 WORLDEV.C:789
                            }
                            break;
                        }
                    case 0xB://Freemis
                        {
                            playerdat.SetQuest(10, 1);
                            break;
                        }
                    case 0x1F://lord umbria
                        {
                            if (mode != 0)
                            {
                                a_set_variable_trap.VariableOperationUW2(243, 2, 1);
                                a_do_trap_trespass.HackTrapTrespass(21);
                                thief.FlagTheftToObjectOwner(playerdat.playerObject, 21);
                            }
                            break;
                        }
                    case 0x20:// Praecor Loth
                        {
                            if (mode != 0)
                            {
                                tomb.KillLothsLiches(0x17);
                                playerdat.SetQuest(103, 1);
                                playerdat.SetQuest(7, 1);
                                trigger.TriggerTrapInTile(1, 2);
                            }
                            break;
                        }
                    case 0x2C://mystell
                        {
                            if (mode != 0)
                            {
                                playerdat.SetQuest(66, 1);
                                playerdat.IncrementXClock(15);
                            }
                            break;
                        }
                    case 0x2D://Altara
                        {
                            //original uw2 has a bit of complicated logic for this quest variable change.
                            //I think all it is doing is clearing quest69 in the first stage and
                            // then setting it when altara is finally killed in the second loop
                            if (mode == 0)
                            {
                                playerdat.SetQuest(69, 0);
                            }
                            else
                            {
                                playerdat.SetQuest(69, 1);
                            }
                            break;
                        }
                    case 0x2F://Mors Gothri
                        {
                            if (mode == 0)
                            {
                                if (worlds.GetWorldNo(playerdat.dungeon_level) == 2)
                                {//initial encounter in the keep
                                    if (playerdat.GetQuest(54) == 1)
                                    {
                                        ObjectRemover_OLD.DeleteObjectFromTile_DEPRECIATED(critter.tileX, critter.tileY, critter.index, true);
                                    }
                                    else
                                    {
                                        TalkToDyingNPC(critter);
                                    }
                                    return false;
                                }
                                else
                                {//final encounter
                                    TalkToDyingNPC(critter);
                                    playerdat.SetQuest(64, 1);
                                    playerdat.IncrementXClock(15);
                                    return true;
                                }
                            }
                            break;
                        }
                    case 0x31://Relk
                        {
                            if (mode != 0)
                            {
                                playerdat.SetQuest(123, 1);
                            }
                            break;
                        }
                    case 0x3A://brain creatures in the keep
                        {
                            if (mode != 0)
                            {
                                playerdat.SetQuest(134, playerdat.GetQuest(134) + 1);
                                if (playerdat.GetQuest(134) >= 2)
                                {
                                    playerdat.SetQuest(50, 1);
                                    special_effects.SpecialEffect(effecttype: 4, effectparam: 0x2C);
                                    playerdat.SetQuest(134, 24);
                                }
                            }
                            return true;
                        }
                    case 0x4b: //guard, transforms into hordling
                        {
                            if (mode == 0)
                            {
                                if (critter.item_id != 0x5E)//check if turned yet
                                {
                                    critter.item_id = 0x5E;
                                    critter.npc_hp = 92;
                                    critter.npc_goal = 5;
                                    return false;//do not kill at this point
                                }
                            }
                            return true;
                        }
                    case 0x48://mokpo
                        {
                            if (mode != 0)
                            {
                                playerdat.SetQuest(53, 1);
                                playerdat.IncrementXClock(15);
                            }
                            break;
                        }
                    case 0x65://blog
                        {
                            if (mode != 0)
                            {
                                playerdat.SetQuest(65, 1);
                                playerdat.IncrementXClock(15);
                                if (playerdat.GetQuest(22) != 0)//blog is friend
                                {
                                    if (playerdat.GetQuest(23) == 0) //blog has no yet helped defeat dorstag
                                    {
                                        playerdat.SetQuest(22, 0);//blog no longer friend.
                                    }
                                }

                                if (playerdat.GetQuest(23) != 0)//this is possibly a bug in vanilla. why award rep points when dorstag is already defeated.
                                {
                                    playerdat.SetQuest(129, playerdat.GetQuest(129) + 6);
                                }
                            }
                            break;
                        }
                    case 0x62://Zaria
                        {
                            playerdat.SetQuest(25, 1);
                            if (WasPitFighter)
                            {
                                playerdat.SetQuest(129, playerdat.GetQuest(129) + 3);
                            }
                            break;
                        }
                    case 0x63://Dorstag
                        {
                            if (mode != 0)
                            {
                                playerdat.SetQuest(121, 1);
                                if (playerdat.GetQuest(23) == 0)
                                {//player did not use blog to defeat dorstag
                                    playerdat.SetQuest(129, playerdat.GetQuest(129) + 6);//increment pits "score" by six, uw2 WORLDEV.C:772-773
                                }
                            }
                            break;
                        }
                    case 0x64://krillner
                        {
                            if (WasPitFighter)
                            {
                                playerdat.SetQuest(129, playerdat.GetQuest(129) - 1);
                            }
                            if (playerdat.GetQuest(28) == 0) //Is krillner your slave
                            {
                                //talk to krillner
                                if (mode == 0)
                                {
                                    if (critter.TileState_0XA_Bit456 == 0)
                                    {
                                        critter.TileState_0XA_Bit456 = 1;
                                        playerdat.ChangeExperience(0x32);
                                        //Talk to NPC
                                        TalkToDyingNPC(critter);
                                        return false;
                                    }
                                    else
                                    {
                                        return true;
                                    }
                                }
                            }
                            else
                            {
                                return true;
                            }
                            break;
                        }
                    case 0x80://fissif
                        {
                            if (playerdat.GetQuest(119) == 0)
                            {//talk to fissif
                                if (mode == 0)
                                {
                                    if (critter.TileState_0XA_Bit456 == 0)
                                    {
                                        critter.TileState_0XA_Bit456 = 1;
                                        playerdat.ChangeExperience(0x32);
                                        //Talk to NPC
                                        TalkToDyingNPC(critter);
                                        return false;
                                    }
                                    else
                                    {
                                        return true;
                                    }
                                }
                            }
                            else
                            {
                                return true;
                            }
                            break;
                        }
                    case 0x8B://nelson
                        {
                            if (mode != 0)
                            {
                                if (playerdat.GetXClock(1) >= 0xB)
                                {
                                    if (
                                        (critter.npc_xhome >= 0x14)
                                        &&
                                        (critter.npc_yhome >= 0x25)
                                        &&
                                        (critter.npc_xhome <= 0x16)
                                        &&
                                        (critter.npc_yhome <= 0x27)
                                    )
                                    {
                                        trigger.RunScheduledTriggerInTile_15_29AndSpawnGuards(
                                            xArg: 0x12, 
                                            yArg: 0x28); //spawns guards.
                                    }
                                }
                                return true;
                            }
                            break;
                        }
                    case 0x8C://patterson. when he is hostile
                        {
                            if (mode != 0)
                            {
                                var originalxclock = playerdat.GetXClock(1);
                                TalkToDyingNPC(critter);
                                playerdat.SetXClock(1, 13);
                                if (playerdat.GetXClock(3) >= 6)
                                {
                                    playerdat.SetXClock(1, 14);
                                }
                                if (playerdat.GetXClock(1) < originalxclock)
                                {//xclock can't go backwards
                                    playerdat.SetXClock(1, originalxclock);
                                }
                            }
                            return true;//this might be a problem. what will happen here since this code will run before the conversation is over.
                        }
                    case 0x8D://lady tory
                        {
                            if (mode == 0)
                            {
                                return true;
                            }
                            else
                            {
                                if (playerdat.GetXClock(1) >= 8)
                                {
                                    return true;
                                }
                                else
                                {
                                    return false;
                                }
                            }
                        }
                    case 0x91://the listener
                        {
                            if (mode == 0)
                            {
                                if (combat.JeweledDagger)
                                {
                                    if (combat.currentweapon != null)
                                    {
                                        combat.currentweapon.item_id = 199;//broken dagger
                                        uimanager.UpdateInventoryDisplay();
                                        playerdat.SetQuest(11, 1);
                                        playerdat.IncrementXClock(1);
                                    }
                                }
                                else
                                {
                                    critter.npc_hp = 1;
                                    critter.npc_goal = 5;
                                    return false;//do not kill
                                }
                            }
                            break;
                        }
                    case 0x98:
                        {//bly ductosnore
                            if (mode != 0)
                            {
                                trigger.TriggerTrapInTile(58, 18);
                                trigger.TriggerTrapInTile(60, 18);
                                playerdat.SetQuest(122, 1);
                            }
                            break;
                        }
                }

                //update winloss record.
                if (playerdat.GetQuest(129) > playerdat.GetXClock(14))
                {//uw2 WORLDEV.C:905-906, the best tally rises to the record
                    playerdat.SetXClock(14, playerdat.GetQuest(129));
                }
                return true;
            }
        }



        /// <summary>
        /// A dead critter leaves the map as the original's critter loop and instant_kill do it: it is taken off its
        /// square, its loot is made if it has none yet, its remains are left (build_corpse), it drops what it carries
        /// (drop_some_objects), and it is freed (uw1 AI.C:1140-1144, uw2 AI.C:1112-1116, WORLDEV.C:1703-1729).
        /// </summary>
        /// <param name="critter"></param>
        /// <returns>whether the critter was removed</returns>
        private static bool DropRemainsAndLoot(uwObject critter)
        {
            if (!UWTileMap.ValidTile(critter.tileX, critter.tileY))
            {
                return false;//cannot drop an offmap npc
            }
            if (!ObjectRemover_OLD.DeleteObjectFromTile_DEPRECIATED(critter.tileX, critter.tileY, critter.index, RemoveFromWorld: false) && _RES == GAME_UW2)
            {
                return false;//UW2 does nothing more when the critter is not in its square's list (uw2 AI.C:1112, WORLDEV.C:1717)
            }
            //Drop npc loot (spawn if missing)
            if (critter.LootSpawnedFlag == 0)
            {
                spawnloot(critter);
            }
            BuildCorpse(critter);
            DropSomeObjects(critter);
            ObjectFreeLists.ReleaseFreeObject(critter);
            return true;
        }

        /// <summary>
        /// Leaves a dead critter's remains (build_corpse, uw1 PATHFIND.C:146-167, uw2 PATHFIND.C:146-167). Its fluids
        /// (0xD8 + n in UW1, 0xD9 + n in UW2) go where it lay, at its fine position and height, quality 0x28, at the head
        /// of its square's list, and are settled. Its corpse (0xC0 + n) is made 7 times in 16, and always in UW2's Pits of
        /// Carnage (world 7), with the creature type as owner, and placed within 4 eighths of a tile of it
        /// (near_mob_put_at).
        /// </summary>
        static void BuildCorpse(uwObject critter)
        {
            int x = critter.tileX, y = critter.tileY;
            var fluids = critterObjectDat.fluids(critter.item_id);
            if (fluids != 0)
            {
                var slot = ObjectCreator.PrepareNewObject(fluids + (_RES == GAME_UW2 ? 0xD9 : 0xD8));
                if (slot != 0)
                {
                    var stain = UWTileMap.current_tilemap.LevelObjects[slot];
                    stain.xpos = critter.xpos;
                    stain.ypos = critter.ypos;
                    stain.zpos = critter.zpos;
                    stain.quality = 0x28;
                    stain.tileX = x; stain.tileY = y;
                    var tile = UWTileMap.current_tilemap.Tiles[x, y];
                    stain.next = tile.indexObjectList;
                    tile.indexObjectList = stain.index;
                    objectInstance.RedrawFull(stain);
                    var settled = motion.PlacedObjectCollision_seg030_2BB7_10BC(stain, x, y, 1);
                    if (settled != null && settled != stain)
                    {
                        objectInstance.RedrawFull(settled);
                    }
                }
            }

            var corpse = critterObjectDat.corpse(critter.item_id);
            if (corpse != 0)
            {
                if (Rng.RandMod(16) >= 7 && !((_RES == GAME_UW2) && (worlds.GetWorldNo(playerdat.dungeon_level) == 7)))
                {
                    return;
                }
                var slot = ObjectCreator.PrepareNewObject(0xC0 + corpse);
                if (slot != 0)
                {
                    var remains = UWTileMap.current_tilemap.LevelObjects[slot];
                    remains.owner = (short)(critter.item_id & 0x3F);
                    motion.NearMobPutAt(critter, remains, 4, false);
                }
            }
        }

        /// <summary>
        /// A dead critter drops what it carries round where it stood, each object within 6 eighths of a tile of it with
        /// put_at (drop_some_objects and drop_link_chain, uw1 TREASURE.C:49-86, uw2 TREASURE.C:37-82). In UW2 any lock is
        /// removed first, each object that can be owned belongs to the critter's race, and lit lights are put out; UW1
        /// gives the owner to the critter itself, which is then freed, so the objects keep theirs.
        /// </summary>
        static void DropSomeObjects(uwObject critter)
        {
            var objList = UWTileMap.current_tilemap.LevelObjects;
            if (critter.is_quant != 0)
            {
                return;
            }
            var race = critterObjectDat.faction(critter.item_id);
            if (_RES == GAME_UW2)
            {//remove_lock(cont, 1): uw2 DAMAGE.C:35-55
                var lockObj = objectsearch.FindMatchInObjectChain(critter.link, 4, 0, 0xF, objList, SkipNext: false, SkipLinks: true);
                while (lockObj != null)
                {
                    if (ObjectRemover_OLD.RemoveObjectFromLinkedList(critter.link, lockObj.index, objList, critter.PTR + 6))
                    {
                        ObjectFreeLists.ReleaseFreeObject(lockObj);
                    }
                    lockObj = objectsearch.FindMatchInObjectChain(critter.link, 4, 0, 0xF, objList, SkipNext: false, SkipLinks: true);
                }
            }
            var next = (int)critter.link;
            if (next == 0)
            {
                return;
            }
            critter.link = 0;
            var x = (critter.tileX << 3) + critter.xpos;
            var y = (critter.tileY << 3) + critter.ypos;
            while (next != 0)
            {
                var obj = objList[next];
                next = obj.next;
                obj.next = 0;
                if (_RES == GAME_UW2)
                {
                    if (race != 0 && commonObjDat.canhaveowner(obj.item_id))
                    {
                        obj.owner = (short)race;
                    }
                    if (obj.item_id >= 0x94 && obj.item_id <= 0x97)
                    {//FIRST_LIT_LIGHT to ITEM_LIT_LIGHT_SPHERE: put out
                        obj.item_id -= 4;
                    }
                }
                motion.PutAt(x, y, critter.zpos, obj, 6, false);
            }
        }

        /// <summary>
        /// Handles tybals death.
        /// </summary>
        /// <param name="tybal"></param>
        public static void TybalDeath(uwObject tybal)
        {
            cutsplayer.PlayCutscene(2, null);
            //set quest variables
            var tmp = playerdat.GetQuest(37);
            tmp |= 4;
            playerdat.SetQuest(37, tmp);//This flags that the next dream will be of garamon asking you to bury him.
            //remove a move trigger from the prison
            var tile = UWTileMap.current_tilemap.Tiles[0x17, 0x38];//trap in the nw prison area
            var next = tile.indexObjectList;
            var previous = 0;
            while (next != 0)
            {
                var nextObj = UWTileMap.current_tilemap.LevelObjects[next];
                if (nextObj.item_id == 0x1A0)
                {
                    next = nextObj.next;
                    if (previous == 0)
                    {//head of tile
                        tile.indexObjectList = nextObj.next;
                        nextObj.next = 0;
                    }
                    else
                    {
                        var previousObject = UWTileMap.current_tilemap.LevelObjects[previous];
                        previousObject.next = nextObj.next;
                        nextObj.next = 0;
                    }
                    ObjectFreeLists.ReleaseFreeObject(nextObj);
                }
                else
                {//not a match. try next
                    previous = nextObj.index;
                    next = nextObj.next;
                }
            }

            int[] WhotoRemove = new int[] { 0xDE, 0xD1, 0xD8, 0xD2, 0xDC, 0xD5, 0xD8, 0xD4, 0xD3, 0xDD };

            for (int i = 1; i <= 255; i++)
            {
                var n = UWTileMap.current_tilemap.LevelObjects[i];
                if (n.majorclass == 1)
                {
                    if (UWTileMap.ValidTile(n.tileX, n.tileY))
                    {
                        if (WhotoRemove.Contains(n.npc_whoami))
                        {
                            ObjectRemover_OLD.DeleteObjectFromTile_DEPRECIATED(n.tileX, n.tileY, n.index, true);
                        }
                    }
                }
            }
        }

        public static void TalkToDyingNPC(uwObject critter)
        {
            combat.EndCombatLoop();
            critter.ProjectileSourceID = 0;
            critter.npc_animation = ANIMATION_IDLE;
            critter.AnimationFrame = 0;
            critter.npc_attitude = 2;
            critter.npc_goal = 8;
            playerdat.FreezeTimeEnchantment = false;//?
            talk.Talk(ConversationNPC: critter, WorldObject: true, dyingNPC: true);
            //todo: in uw2 npc_talkedto gets cleared here. does this matter and if so how would implement it seeing as the conversation runs in a co-routine
        }


        /// <summary>
        /// Outright kills an npc
        /// </summary>
        /// <param name="critter"></param>
        /// <returns>whether the critter was removed (instant_kill, uw2 WORLDEV.C:1703-1729)</returns>
        public static bool KillCritter(uwObject critter)
        {
            SpecialDeathCases(critter, 0);
            SpecialDeathCases(critter, 1);
            return DropRemainsAndLoot(critter);
        }
    }//end class
}//end namespace