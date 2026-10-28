using System.Diagnostics;

namespace Underworld
{
    /// <summary>
    /// What the hell is SCD.ARK? It's an event system. Data is read from scd.ark at the end of conversations, level transitions, by a hack trap, when a block of time is added to the clock and when Killorn is crashing.
    /// </summary>
    public partial class scd : UWClass
    {
        static int BlockIdentifier;
        /// <summary>
        /// Starts the SCD process.
        /// Checks if scd_data is loaded and check each xclock to see if events need to be ran based on various conditions.
        /// </summary>
        public static void ProcessSCDArk(int mode)
        {
            Debug.Print("Starting to process SCU.ARK");
            if (scd_data == null)
            {
                scd_data = new UWBlock[0x10];
            }
            int error_var2 = 0;//flags if an error has occurred and stop execution.
            for (int blockno = 0; blockno < 0x10; blockno++)
            {
                BlockIdentifier = blockno;
                Debug.Print($"Processing SCD Block {blockno}");
                var si_xclock = playerdat.GetXClock(blockno);
                if (!SchedLoad(blockno, out var storedData))
                {
                    Debug.Print($"Error loading scd block {blockno}");
                    return; //stop                     
                }

                if (blockno == 0)
                {
                    error_var2 = ProcessBlock0(
                        currentblock: scd_data[blockno].Data,
                        arg0_xclock: playerdat.GetXClock(0),
                        NoOfMaps: 72,
                        mode_arg4: mode);
                }
                else
                {
                    error_var2 = ProcessGeneralBlock(
                        currentblock: scd_data[blockno].Data,
                        XClockValue: si_xclock,
                        mode: mode);
                }
                //an error stops before the save (uw2/src/event/SCHEDULE.C:213-217).
                SchedSave(blockno, storedData, error_var2 == 0 && ArkHasBeenModified);
                if (error_var2 != 0)
                {
                    return;
                }
            }
        }

        /// <summary>
        /// Runs only the day schedule, SCD.ARK block 0, up to the time of day in X clock 0, as the hourly step does
        /// (Sched_Load(0), Sched_WrapTime(xclock[XC_TIME], DAY_STEPS, mode), Sched_Save(0): uw2 PLAYTIME.C:157-162).
        /// ProcessSCDArk does the same for all 16 blocks.
        /// </summary>
        public static void ProcessSCDBlock0(int mode)
        {
            if (scd_data == null)
            {
                scd_data = new UWBlock[0x10];
            }
            BlockIdentifier = 0;
            if (!SchedLoad(0, out var storedData))
            {
                Debug.Print("Error loading scd block 0");
                return;
            }
            ProcessBlock0(
                currentblock: scd_data[0].Data,
                arg0_xclock: playerdat.GetXClock(0),
                NoOfMaps: 72,
                mode_arg4: mode);
            //the hourly step saves whatever Sched_WrapTime returned, so a row that ran before an error is kept.
            SchedSave(0, storedData, ArkHasBeenModified);
        }

        /// <summary>
        /// Sched_Load: puts a copy of the block as last written back (or as it is in the file) in scd_data
        /// to work on, and clears the modified flag (uw2/src/event/SCHEDULE.C:122-133).
        /// scd_data holds the blocks written back so far, null is the block as it is in the file.
        /// </summary>
        /// <param name="blockno"></param>
        /// <param name="storedData">the block as last written back, null if never written</param>
        /// <returns>False if SCD.ARK could not be read</returns>
        static bool SchedLoad(int blockno, out byte[] storedData)
        {
            storedData = scd_data[blockno]?.Data;
            if (storedData == null)
            {
                if (!LoadSCDBlock(blockno))
                {
                    return false;
                }
            }
            else
            {
                scd_data[blockno].Data = (byte[])storedData.Clone();
            }
            ArkHasBeenModified = false;
            scd_data[blockno].Data[2] = (byte)blockno;
            return true;
        }

        /// <summary>
        /// Sched_Save: keeps the block worked on only when save is set, which Sched_Save gives as a row
        /// having run or deleted itself (uw2/src/event/SCHEDULE.C:66-69, 137-158). Otherwise the changes
        /// are discarded, so a clock change alone is not kept.
        /// </summary>
        /// <param name="blockno"></param>
        /// <param name="storedData">what SchedLoad found in scd_data</param>
        /// <param name="save"></param>
        static void SchedSave(int blockno, byte[] storedData, bool save)
        {
            if (save)
            {
                Debug.Print("Ark file needs to be saved with new data.");
            }
            else if (storedData == null)
            {
                scd_data[blockno] = null;//discard the unsaved changes: the block is as it is in the file.
            }
            else
            {
                scd_data[blockno].Data = storedData;//discard the unsaved changes: back to the block last written.
            }
        }

        /// <summary>
        /// Special case that is called on xclock 0
        /// </summary>
        /// <param name="currentblock"></param>
        /// <param name="arg0_xclock"></param>
        /// <param name="NoOfMaps"></param>
        /// <param name="mode_arg4"></param>
        /// <returns></returns>
        static int ProcessBlock0(byte[] currentblock, int arg0_xclock, int NoOfMaps, int mode_arg4)
        {
            Debug.Print($"ProcessBlock0 for {arg0_xclock}");
            var di = arg0_xclock % NoOfMaps;
            var var1 = 0;
            if (currentblock[4 + (playerdat.dungeon_level * 4)] > di)
            {
                Debug.Print($"ProcessBlock0: found valid block to process at {4 + (playerdat.dungeon_level * 4)}");
                var1 = ProcessGeneralBlock(
                    currentblock: currentblock,
                    XClockValue: NoOfMaps - 1,
                    mode: mode_arg4);
                if (var1 != 0)
                {
                    return var1;
                }
                else
                {
                    currentblock[4 + (playerdat.dungeon_level * 4)] = 0;
                    currentblock[6 + (playerdat.dungeon_level * 4)] = 0;
                }
            }

            return ProcessGeneralBlock(
                currentblock: currentblock,
                XClockValue: di,
                mode: mode_arg4);

        }

        /// <summary>
        /// General Processing of a SCD block.
        /// </summary>
        /// <param name="currentblock"></param>
        /// <param name="XClockValue"></param>
        /// <param name="mode"></param>
        /// <returns></returns>
        static int ProcessGeneralBlock(byte[] currentblock, int XClockValue, int mode)
        {
            //Sched_SetTime only sets the schedule's clock for the level. It does not change the player's xclocks (uw2/src/event/SCHEDULE.C:173-188)
            Debug.Print($"ProcessGeneralBlock xclockvalue {XClockValue}");
            var si_offset = currentblock[4 + playerdat.dungeon_level * 4];
            var di = XClockValue;
            currentblock[4 + (playerdat.dungeon_level * 4)] = (byte)XClockValue;
            if (si_offset != di)
            {
                if (si_offset > di)
                {
                    if (mode == 0)
                    {
                        return SomethingWithSCDRows_ovr151_D3(mode);//TOOD figure out.
                    }
                    else
                    {
                        currentblock[6 + (playerdat.dungeon_level * 4)] = 0;
                        return FindSCDRowsToExecute(currentblock, mode);
                    }
                }
                else
                {
                    return FindSCDRowsToExecute(currentblock, mode);
                }
            }
            else
            {
                return 0;
            }
        }


        static int SomethingWithSCDRows_ovr151_D3(int mode)
        {
            Debug.Print("Something with SCD Rows");
            return 0;
        }

        /// <summary>
        /// Loops event rows to find possible matches to run
        /// </summary>
        /// <param name="currentblock"></param>
        /// <param name="mode"></param>
        /// <returns></returns>
        static int FindSCDRowsToExecute(byte[] currentblock, int mode)
        {
            int var1 = 0;
            Debug.Print("Trying to find SCD Rows to Execute");
            var si = currentblock[6 + (playerdat.dungeon_level * 4)];
            if (currentblock[2] == 0xF)
            {
                si = 0;
            }

            while (currentblock[0] > si)
            {
                if (currentblock[324 + (si * 16)] <= currentblock[4 + (playerdat.dungeon_level * 4)])
                {
                    if (mode != 0)
                    {
                        var1 = ProcessSCDEventRow(currentblock, 324 + (si * 16));
                        switch (var1)
                        {
                            case 0:
                                ArkHasBeenModified = true;
                                si++;
                                break;
                            case 4: //deleted record: the next row has moved into this index, so look at it next (uw2/src/event/SCHEDULE.C:68-69, 76)
                                ArkHasBeenModified = true;
                                Debug.Print("Deleted Record");
                                break;
                            case 5:
                                si++;
                                break;
                            default:
                                currentblock[6 + playerdat.dungeon_level * 4] = si;
                                return var1;
                        }
                    }
                    else
                    {
                        si++;
                    }
                }
                else
                {
                    break;
                }
            }

            currentblock[6 + (playerdat.dungeon_level * 4)] = si;
            // if (scd_data[2] == 0xF)
            // {
            //     si = 0;
            // }

            return 0;
        }


        /// <summary>
        /// Calls the event based on player being in the matching level, world or any levels
        /// </summary>
        /// <param name="currentblock"></param>
        /// <param name="eventOffset"></param>
        /// <returns></returns>
        static int ProcessSCDEventRow(byte[] currentblock, int eventOffset)
        {            
            if (
                (currentblock[eventOffset + 2] == playerdat.dungeon_level)
                || (currentblock[eventOffset + 2] == 0xFF)
                || (currentblock[eventOffset + 2] - 246 == playerdat.CurrentWorld)
                )
            {
                Debug.Print($"Process Event Row at Offset {eventOffset}  #{(eventOffset-324)/16}");
                //DoEvent = true;
                // var var1 = 1;
                // var var2 = 1;

                if ((sbyte)(currentblock[eventOffset + 4]) >= 0)
                {
                    if ((sbyte)(currentblock[eventOffset + 4]) <0xC)
                    {                      
                        var var2 = RunSCDFunction(
                            currentblock: currentblock,
                            eventOffset: eventOffset);
                        
                        if (currentblock[eventOffset + 3] != 0)
                        {
                            Debug.Print($"{eventOffset} to be deleted!");
                            DeleteRow(currentblock, eventOffset);
                            return 4;//return row has been deleted.
                        }
                        else
                        {
                            return var2;//result of function call.
                        }
                    }
                    else
                    {
                        return 6; //error stops processing
                    }
                }
                else
                {
                    return 5;//process next
                }
            }
            else
            {
                return 5;//process next
            }
        }
    }//end class
}//end namespace