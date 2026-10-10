
using System;

namespace Underworld
{
    /// <summary>
    /// Where Rng's random numbers come from. Rand() is a raw value 0 to 32767, as Turbo C++'s
    /// rand() returns; Next(max) is 0 to max - 1, and 0 when max is 0 or less.
    /// </summary>
    public interface IRandSource
    {
        int Rand();
        int Next(int max);
    }

    /// <summary>The default: .NET's generator. Next(max) is Random.Next(max), as Rng used before.</summary>
    public sealed class DotNetRandSource : IRandSource
    {
        readonly Random random = new();
        public int Rand() => random.Next(0x8000);
        public int Next(int max) => max <= 0 ? 0 : random.Next(max);
    }

    /// <summary>
    /// Turbo C++ 1.01's rand(), which the original games use: seed = seed * 0x015A4E35 + 1, and the
    /// value is bits 16 to 30 of the new seed. Seed is the whole 32-bit state.
    /// </summary>
    public sealed class BorlandRandSource : IRandSource
    {
        public uint Seed { get; private set; }
        public BorlandRandSource(uint seed) { Seed = seed; }
        public int Rand()
        {
            Seed = Seed * 0x015A4E35u + 1;
            return (int)((Seed >> 16) & 0x7FFF);
        }
        public int Next(int max) => max <= 0 ? 0 : Rand() % max;
    }

    /// <summary>
    /// Handles RNG calls for the game. Eventually this may replicate the original rng that is based on system time rather than .NET rng.
    /// </summary>
    public class Rng : UWClass
    {
        public static uint Seed;
        const int Multiplier = 0x15A4E35;
        public static Rng r = new();
        //public static Random r_unseeded = new(); //for rng calls where the seed never gets reset

        /// <summary>The source of every draw. Tests set a BorlandRandSource; play uses .NET's.</summary>
        public static IRandSource Source { get; set; } = new DotNetRandSource();

        /// <summary>rand() % n, the form most of the original's call sites use.</summary>
        public static int RandMod(int n) => Source.Rand() % n;

        /// <summary>rand() * n / 0x8000, a value 0 to n - 1, the form rollem uses.</summary>
        public static int RandScale(int n) => (int)((long)Source.Rand() * n / 0x8000);

        public Rng()
        {
            InitRng();
        }

        /// <summary>
        /// Returns a random value which is base value offset by the upper and lower range
        /// </summary>
        /// <param name="basevalue"></param>
        /// <param name="lower_offset"></param>
        /// <param name="upper_offset"></param>
        /// <returns></returns>
        public static int RandomOffset(int basevalue, int lower_offset, int upper_offset)
        {
            var range = upper_offset-lower_offset;
            var rnd = r.Next(range);
            return basevalue + basevalue * (lower_offset + rnd) /100;        
        }


        /// <summary>
        /// Returns NoOfLoops + ({NoOfLoops}D{DiceRange})
        /// </summary>
        /// <param name="NoOfLoops"></param>
        /// <param name="diceRange"></param>
        /// <returns></returns>
        public static int DiceRoll(int NoOfLoops, int diceRange)
        {
            //var di = diceRange;
            var si = NoOfLoops;
            if (diceRange <= 0)
            {
                return si;
            }
            else
            {
                if (NoOfLoops <= 0)
                {
                    return si;
                }
                else
                {
                    while (NoOfLoops != 0)
                    {
                        si += RandScale(diceRange);
                        NoOfLoops--;
                    }
                    return si;
                }
            }
        }


        /// <summary>
        /// The unix epoch is found and stored as the RNG seed.
        /// </summary>
        public static void InitRng()
        {
            Seed = (ushort)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() & 0xFFFF);
            //Debug.Print($"Rng Seeded to {Seed}");
        }

        /// <summary>
        /// An RNG result is obtained by multipying Seed * Constant Multipler. 
        /// The multiplication result is stored as the next seed.
        /// The max value is mod divided into the (result & 0x7FFF) to get the RNG number
        /// </summary>
        /// <param name="max"></param>
        /// <returns></returns>
        public int Next(int max)
        {
            return Source.Next(max);
        }

        public int Next(int min, int max)
        {
            var res = Next(max-min);
            return min + res;
        }

        void RngTest()
        {
            int[] rngtest = new int[100];
            for (int i = 0; i<4000;i++)
            {
                var res = r.Next(100);
                rngtest[res]++;
            }
        }

    }//end class
}//end namespace