using System;
using System.Diagnostics;
namespace Underworld
{
    /// <summary>
    /// Specific code for the pits of carnage
    /// </summary>
    public class pitsofcarnage:UWClass
    {
        public static bool IsAvatarInPitFightGlobal = false;//used in babl_hack.


        public static uwObject CreateRandomPitFighter(int tileX, int tileY, int isPowerfullProbability )
        {
            var tile = UWTileMap.current_tilemap.Tiles[tileX, tileY];
            bool isPowerfull = false;

            int itemIdToSpawn;

            if (Rng.r.Next(0x10)==0)
            {   //spawn a mage
                itemIdToSpawn = 0x75 + Rng.r.Next(3);
            }
            else
            {   //spawn a fighter
                itemIdToSpawn = 0x78 + Rng.r.Next(2);
            }

            if (Rng.r.Next(3)< isPowerfullProbability )
            {
                isPowerfull = true;
                var rngPowerfull = Rng.r.Next(5);
                if (rngPowerfull <2)
                {
                    switch (rngPowerfull)
                    {
                        case 0:
                            itemIdToSpawn = 0x5C; // great troll
                            break;
                        case 1:
                            itemIdToSpawn = 0x7B;// human
                            break;
                    }
                    isPowerfull = false;
                }
            }

            var obj = ObjectCreator.spawnObjectInTile(
                itemid: itemIdToSpawn, 
                tileX: tileX, tileY: tileY, 
                xpos: (short)Rng.r.Next(1,8), 
                ypos: (short)Rng.r.Next(1,8), 
                zpos: (short)(tile.floorHeight<<3), 
                WhichList: ObjectFreeLists.ObjectListType.MobileList);

            obj.npc_whoami = 0x66;
            if (isPowerfull)
            {
                obj.IsPowerful = 1;
            }
            else
            {
                obj.IsPowerful = 0;
            }

            obj.SpawnedCritter_0XD_Bit8 = 1;
            obj.npc_attitude = 0;
            obj.npc_goal = 5;
            obj.npc_gtarg = 1;
            obj.UnkBit_0XA_Bit7 = 1;
            
            return obj;
        }


        /// <summary>
        /// Checks if the specified npc is fighting against the avatar in the pits
        /// </summary>
        /// <returns></returns>
        public static bool IsDuelingAgainstCritter(int index)
        {
            for (int i = 0; i < 5; i++)
            {
                var currentfighter = playerdat.GetPitFighter(i);  //playerdat.GetAt(0x361 + i);
                if (currentfighter != 0)
                {
                    if (currentfighter == index)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// When avatar flees combat in the pit (arena_player_runs, uw2 WORLDEV.C:2029-2056): the pit fighters stop, the
        /// first one talks to him, and his pit record and fight are cleared.
        /// </summary>
        /// <param name="skipConversation"></param>
        public static void AvatarIsACoward(bool skipConversation = false)
        {
            if (playerdat.IsFightingInPit)
            {               
                int firstFighterIndex = -1;
                IsAvatarInPitFightGlobal = true;
                for (int si = 0; si<5;si++)
                {
                    var fighterindex = playerdat.GetPitFighter(si);
                    if (fighterindex !=0 )
                    {
                        var fighterObj = UWTileMap.current_tilemap.LevelObjects[fighterindex];
                        fighterObj.npc_attitude = 1;
                        fighterObj.ProjectileSourceID = 0;//clear last hit
                        fighterObj.npc_goal = (byte)npc.npc_goals.npc_goal_goto_1;
                        if (firstFighterIndex == -1)
                        {
                            firstFighterIndex = si;
                        }
                        else
                        {
                            playerdat.SetPitFighter(si,0);//
                        }
                    }                    
                }
                if (firstFighterIndex != -1)
                {
                    var firstfighter = UWTileMap.current_tilemap.LevelObjects[playerdat.GetPitFighter(firstFighterIndex)];
                    if (!skipConversation)
                    {
                        talk.Talk(firstfighter, true);
                    }                    
                    playerdat.SetPitFighter(firstFighterIndex, 0);
                }
                //the fight and record end whether or not a fighter was left (uw2 WORLDEV.C:2052-2054)
                playerdat.IsFightingInPit = false;
                playerdat.SetQuest(129,0); //win loss record
                playerdat.SetQuest(133,0);  //jospurs debt.
            }
        }

        /// <summary>
        /// Whether the square x, y is inside the arena of the Pits of Carnage, a ring round 0x1F, 0x1F: at least 3 squares
        /// from the centre on both axes, and within the limit of its quarter on both (in_arena, uw2 WORLDEV.C:963-976).
        /// </summary>
        public static bool InArena(int x, int y)
        {
            int[] limits = { 0xF, 0x10, 0xE, 0xF };
            x -= 0x1F;
            y -= 0x1F;
            var quarter = Mpos(x, y) >> 1;
            x = Math.Abs(x);
            y = Math.Abs(y);
            if (x < 3 || y < 3)
            {
                return false;
            }
            if (limits[quarter] < x || limits[quarter] < y)
            {
                return false;
            }
            return true;
        }

        /// <summary>
        /// The direction of the vector x, y, 0 to 7: 0 is +y, 2 +x, 4 -y, 6 -x, the odd values the diagonals between; a
        /// component counts as zero when it is less than half the other (mpos, uw2 MISCUTIL.C:62-75, which takes chars).
        /// </summary>
        public static int Mpos(int x, int y)
        {
            x = (sbyte)x; y = (sbyte)y;
            if (Math.Abs(x) / 2 > Math.Abs(y))
            {
                return x > 0 ? 2 : 6;
            }
            else if (Math.Abs(y) / 2 > Math.Abs(x))
            {
                return y > 0 ? 0 : 4;
            }
            else if (x < 0)
            {
                return (y > 0 ? 2 : 0) + 5;
            }
            else
            {
                return (y < 0 ? 2 : 0) + 1;
            }
        }

        /// <summary>
        /// Defeats a pit fighter that has fled the arena.
        /// </summary>
        /// <param name="character"></param>
        public static void DefeatLivingPitFighter(int character)
        {
            Debug.Print("Untested removal of defeated living cowardly pit fighter");
            if (playerdat.GetQuest(129) > playerdat.GetXClock(14))
            {
                playerdat.SetXClock(14, playerdat.GetQuest(129));
            }
            var defeatedfighter = UWTileMap.current_tilemap.LevelObjects[character];
            defeatedfighter.npc_goal = (byte)npc.npc_goals.npc_goal_fear_6;
            RemovePitFighter(character);
        }


 /// <summary>
        /// Removes a pit fighter from player data and flags that the player is no longer fighting in the pit if that is the case.
        /// </summary>
        /// <param name="fighterToRemove"></param>
        public static bool RemovePitFighter(int fighterToRemove)
        {
            int fightercounter = 0;
            bool fighterRemoved = false;
            for (int i = 0; i < 5; i++)
            {
                byte currentfighter = playerdat.GetPitFighter(i);
                if (currentfighter != 0)
                {
                    fightercounter++;
                    if (currentfighter == fighterToRemove)
                    {
                        playerdat.SetPitFighter(i, 0);
                        fighterRemoved = true;
                    }
                }
            }
            if (fightercounter <= 1)
            {//no longer fighting
                playerdat.IsFightingInPit = false;
            }
            return fighterRemoved;
        }

    }//end class
}//end namepsace