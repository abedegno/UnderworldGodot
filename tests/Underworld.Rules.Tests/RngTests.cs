using Underworld;

namespace Underworld.Rules.Tests;

[Collection("Rules")]
public class RngTests
{
    [Fact]
    public void Borland_source_gives_turbo_c_sequence_from_seed_1()
    {
        var b = new BorlandRandSource(1);
        Assert.Equal(new[] { 346, 130, 10982, 1090, 11656 },
            new[] { b.Rand(), b.Rand(), b.Rand(), b.Rand(), b.Rand() });
        Assert.Equal(0xAD889112u, b.Seed);
    }

    [Fact]
    public void Borland_source_next_is_rand_mod_and_zero_for_empty_range()
    {
        var b = new BorlandRandSource(1);
        Assert.Equal(346 % 31, b.Next(31));
        Assert.Equal(0, b.Next(0));
    }

    [Fact]
    public void RandMod_and_RandScale_take_the_original_forms()
    {
        var saved = Rng.Source;
        try
        {
            Rng.Source = new BorlandRandSource(1);
            Assert.Equal(346 % 31, Rng.RandMod(31));          // rand() % 31
            Assert.Equal((int)(130L * 6 / 0x8000), Rng.RandScale(6)); // rand() * 6 / 0x8000
        }
        finally { Rng.Source = saved; }
    }

    [Fact]
    public void Default_source_next_stays_in_range_and_uses_dotnet()
    {
        Assert.IsType<DotNetRandSource>(Rng.Source);
        for (int i = 0; i < 2000; i++)
        {
            var v = Rng.r.Next(7);
            Assert.InRange(v, 0, 6);
            Assert.InRange(Rng.Source.Rand(), 0, 32767);
        }
    }
}
