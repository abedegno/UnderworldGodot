using System.Diagnostics;

namespace Underworld
{
    //Clock/time player data
    public partial class playerdat : Loader
    {


        /// <summary>
        /// The value of the clock in the player data
        /// </summary>
        public static int ClockValue
        {
            get
            {
                if (_RES == GAME_UW2)
                {
                    return GetAt32(0x36A);
                }
                else
                {
                    return GetAt32(0xCF);
                }
            }
            set
            {
                if (_RES==GAME_UW2)
                {
                    SetAt32(0x36A, value);
                }
                else
                {
                    SetAt32(0xCF, value);
                }
            }
        }

        public static int game_time
        {
            get
            {
                return ClockValue / 0x3BC4;
            }
        }

        public static int game_mins
        {
            get
            {
                return game_time % 0x5A0;
            }
        }

        public static int game_days
        {
            get
            {
                return ClockValue / 0x1502e80;
            }
        }


        /// <summary>
        /// Hour for pocket watch
        /// </summary>
        public static int TwelveHourClock
        {
            get
            {
                return (ClockValue / 0xE1000) % 12;
            }
        }

        /// <summary>
        /// Minute for pocket watch
        /// </summary>
        public static int Minute
        {
            get
            {
                return (ClockValue / 0x3C00) % 60;
            }
        }

        /// <summary>
        /// Lets a span of seconds of game time pass (sleeping, repairing, jail).
        /// The clock is in 1/256 seconds so the span is shifted by 8.
        /// UW2 is pass_time (uw2/src/event/WORLDEV.C:163-170): the time of day xclock 0 is then
        /// recalculated from the clock in 20 minute steps (0x4B000), 72 to the day, and the SCD schedules
        /// are brought up to date (Sched_SetAllClocks(1)). pass_time also sets lastDurCheck to the clock
        /// so the duration checks do not catch up on the skipped time; the port's duration checks
        /// run on elapsed real time (PlayerTimedLoop) rather than on the clock so there is nothing to reset.
        /// UW1 has no pass_time. Its callers add the time to the clock directly (uw1/src/game/SKILLS.C:546, 583,
        /// uw1/src/event/WORLDEV.C:705).
        /// </summary>
        /// <param name="span">seconds</param>
        public static void AdvanceTime(int span)
        {
            Debug.Print ($"Advance time {span}");
            ClockValue += span << 8;
            if (_RES == GAME_UW2)
            {
                SetXClock(0, (ClockValue / 0x4B000) % 0x48);
                scd.ProcessSCDArk(1);
            }
        }
    }  //end class
}//end namespace