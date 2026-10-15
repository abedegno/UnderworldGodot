namespace Underworld
{

    /// <summary>
    /// Trap that moves NPCs around the castle map based on time of day and XClock value.
    /// </summary>
    public class a_hack_trap_castleschedule : trap
    {
        /// <summary>
        /// Castle default tileX for NPCs starting at nystrul (130);
        /// </summary>
        static short[] CastleX = new short[]{42,
                                        36,
                                        21,
                                        37,
                                        22,
                                        25,
                                        27,
                                        44,
                                        43,
                                        22,
                                        21,
                                        24,
                                        26,
                                        25
                                        };

        static short[] CastleY = new short[]{43,
                                        51,
                                        42,
                                        35,
                                        51,
                                        43,
                                        36,
                                        48,
                                        49,
                                        37,
                                        34,
                                        39,
                                        48,
                                        34
                                        };

        public static void Activate()
        {
            var di_xclock = playerdat.GetXClock(1);
            if (playerdat.GetQuest(109) == 0)
            {
                return;// player has not yet spoken to LB
            }
            if (di_xclock >= 0x10)
            {
                return; //at end game.
            }

            //DOS runs whoami 0x81 to 0x8F inclusive (uw2 WORLDEV.C:1166). 0x81 is skipped here: the tables start at 0x82,
            //so for 0x81 DOS reads below them (uw2 CRITTIME.C:690-693), a bug in the original that is not copied.
            for (int si_whoami = 0x82; si_whoami <= 0x8F; si_whoami++)
            {
                var run = true;
                switch (si_whoami)
                {
                    case 0x8E:// Lord british
                        {
                            if (playerdat.GetQuest(112) == 1)
                            {//Lord british and avatar is fighting in the castle with friendlies (LB needs to appear at the jail to let avatar out.)
                                run = false;
                            }
                            break;
                        }
                    case 0x8D:// Lady Tori
                        {
                            if (di_xclock >= 8)
                            {   //Spoiler. Tori has been murdered at this point
                                run = false;
                            }
                            break;
                        }
                    case 0x8B://Nelson
                    case 0x8C://Patterson
                        {
                            if (di_xclock >= 0xB)
                            {
                                //Spoiler. Patterson is the traitor and kills Nelson
                                run = false;
                            }
                            break;
                        }

                }
                if (run)
                {
                    CallBacks.RunCodeOnNPCS_WhoAmI(DoCastleschedule, si_whoami, null, false);
                }
            }
            //Special case for Syria as her WhoAmI is out of sequence
            CallBacks.RunCodeOnNPCS_WhoAmI(DoCastleschedule, 0xA8, null, false);
        }


        /// <summary>
        /// Figures out where the castle NPC should go to and moves them
        /// </summary>
        /// <param name="critter"></param>
        /// <param name="paramsarray"></param>
        static void DoCastleschedule(uwObject critter, int[] paramsarray)
        {
            short newX; short newY;
            GetCastleDestination(
                critter: critter,
                NewTileX: out newX,
                NewTileY: out newY);
            critter.quality = newX;
            critter.owner = newY;
            critter.npc_goal = (byte)npc.npc_goals.npc_goal_goto_1;

            //Check if current location and destination is not in front of player
            if (!uwObject.CheckIfInFrontOfPlayer(xcoord: (critter.npc_xhome << 3) + critter.xpos, ycoord: (critter.npc_yhome << 3) + critter.ypos))
            {
                if (!uwObject.CheckIfInFrontOfPlayer(xcoord: (newX << 3) + 3, ycoord: (newY << 3) + 3))
                {
                    npc.moveNPCToTile(
                        critter: critter,
                        destTileX: newX, destTileY: newY);
                }
            }
        }

        /// <summary>
        /// Determines where the NPC should move to based on time of day.
        /// where_shall_we_hang_out (uw2 CRITTIME.C:646-731). The day is split into two-hour stages from the hour, 0 to 23:
        /// stage 0 is 1 to 3 am, up to stage 11, 11 pm to 1 am. A random location 0 to 5 is picked, but at night (stages 0 to 2)
        /// and otherwise one time in three the stage decides.
        /// </summary>
        /// <param name="critter"></param>
        /// <param name="NewTileX"></param>
        /// <param name="NewTileY"></param>
        static void GetCastleDestination(uwObject critter, out short NewTileX, out short NewTileY)
        {
            var hour = (playerdat.ClockValue / 0xE1000) % 24;//a 24 hour clock (uw2 CRITTIME.C:656)
            var varC_stage = (((hour + 1) >> 1) + 0xB) % 0xC; // can be 0 to 11 (uw2 CRITTIME.C:657)

            var di_location = Rng.RandMod(6);//pick a random location.

            if ((Rng.RandMod(3) == 0) || (varC_stage <= 2))
            {
                switch (varC_stage)
                {
                    case 3:
                    case 5:
                    case 9:
                        di_location = 4;//stay where it is (uw2 CRITTIME.C:661-663)
                        break;
                    case 0:
                    case 1:
                    case 2:
                    case 11:
                        di_location = 0;//go to default location
                        break;
                    case 6:
                    case 7:
                    case 8:
                        di_location = 1;//go to the lobby.
                        break;
                }
            }

            //special cases: Miranda always, and everyone before the castle plot starts (XC_CASTLE 0), Lord British once quest 115 is set, and Nystul after the murders (uw2 CRITTIME.C:671-676)
            if (
                (critter.npc_whoami == 0x88 || playerdat.GetXClock(1) == 0)
                ||
                (critter.npc_whoami == 0x8E && playerdat.GetQuest(115) != 0)
                ||
                (critter.npc_whoami == 0x82 && playerdat.GetXClock(1) >= 0xC)
                )
            {
                di_location = 0;//go to default location
            }

            if (di_location == 5)
            {//stay home (uw2 CRITTIME.C:677-679)
                NewTileX = critter.quality;
                NewTileY = critter.owner;
            }
            else if (di_location == 0)
            {//when 0 go to the NPC default hangout location.
                if (critter.npc_whoami == 0xA8)
                {//special case for Syria
                    NewTileX = 0x2A; //guardroom/training room
                    NewTileY = 0x24;
                }
                else
                {
                    NewTileX = CastleX[critter.npc_whoami - 130];
                    NewTileY = CastleY[critter.npc_whoami - 130];
                }
            }
            else
            {
                int locX1; int locX2; int locY1; int locY2;
                switch (di_location)
                {
                    case 1://castle lobby
                        locX1 = 0x1B;
                        locY1 = 0x22;
                        locX2 = 0x23;
                        locY2 = 0x25;
                        break;
                    case 2://fountain area
                        locX1 = 0x1E;
                        locY1 = 0x27;
                        locX2 = 0x20;
                        locY2 = 0x2B;
                        break;
                    case 3://Throne room
                        locX1 = 0x1D;
                        locY1 = 0x2D;
                        locX2 = 0x21;
                        locY2 = 0x33;
                        break;
                    case 4://kitchens
                           //Although the kitchens have co-ordinates defined (0x23, 0x2F to 0x2F, 0x34) the case falls into the default in DOS (uw2 CRITTIME.C:718-726)
                    default:
                        NewTileX = (short)critter.tileX;//remain at current position
                        NewTileY = (short)critter.tileY;
                        return;
                }

                //Find random spot within the bounds of the locations, bounds included (uw2 CRITTIME.C:727-728)
                NewTileX = (short)(locX1 + Rng.RandMod(locX2 - locX1 + 1));
                NewTileY = (short)(locY1 + Rng.RandMod(locY2 - locY1 + 1));
                if ((di_location == 2) && (NewTileX == 0x1F) && (NewTileY == 0x29))
                {//fountain, special case for the fountain pedestal location.
                    NewTileX++;
                }
            }
        }

    }//end class
}//end namespace