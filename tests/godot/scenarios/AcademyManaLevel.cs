using System.Collections.Generic;
using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// Where mana can be restored in the Scintillus Academy. The original's restore_mana does nothing
/// in world 5 ((PlayerLevel - 1) / 8 == 5, levels 41 to 48) except on its first level and on its
/// eighth level at x 25 or more, the level within the world being (PlayerLevel - 1) % 8 + 1
/// (uw2/src/combat/SPELLS.C:155-170). A negative amount adds -amount.
///
/// Each row sets the player's dungeon level and x for the call only, calls the port's
/// playerdat.ManaRegenChange(-5) (the call the timed loop and sleep make, restore_mana with -5),
/// and compares the mana gained with the original's rule; level and x are put back afterwards.
///
/// The port once took the level within the world as 1 + dungeon_level % 8, one too high
/// (src/player/playerdatloop.cs:291), blocking the Academy's first level and freeing its last.
/// </summary>
public sealed class AcademyManaLevel : Scenario
{
    static readonly (int Level, int X)[] Rows =
    {
        (40, 30),  // world 4: always
        (41, 30),  // Academy level 1: allowed
        (42, 30),  // Academy level 2: blocked
        (47, 30),  // Academy level 7: blocked whatever x
        (48, 24),  // Academy level 8, x 24: blocked
        (48, 25),  // Academy level 8, x 25: allowed
    };

    public override string Name => "academy-mana-level";
    public override string[] Games => new[] { "uw2" };
    public override string Cites => "underworld-exhumed uw2/src/combat/SPELLS.C:155-170 (restore_mana)";

    readonly List<(int level, int x, int gained)> _results = new();
    bool _restored;

    static bool Allowed(int level, int x) =>
        (level - 1) / 8 != 5 || (level - 1) % 8 + 1 <= 1 || ((level - 1) % 8 + 1 >= 8 && x >= 25);

    public override Task Setup(ScenarioContext c)
    {
        BatchB.ReadyCaster();
        return Task.CompletedTask;
    }

    public override Task Act(ScenarioContext c)
    {
        int level = playerdat.dungeon_level, x = playerdat.playerObject.tileX;
        foreach (var r in Rows)
        {
            playerdat.play_mana = 50;
            playerdat.dungeon_level = r.Level;
            playerdat.playerObject.tileX = r.X;
            playerdat.ManaRegenChange(-5);
            _results.Add((r.Level, r.X, playerdat.play_mana - 50));
            playerdat.dungeon_level = level;
            playerdat.playerObject.tileX = x;
        }
        _restored = playerdat.dungeon_level == level && playerdat.playerObject.tileX == x;
        foreach (var r in _results) c.Note($"level {r.level}, x {r.x}: mana +{r.gained}");
        return Task.CompletedTask;
    }

    public override string Assert(ScenarioContext c)
    {
        if (!_restored) return "precondition: the level or x was not put back";
        if (playerdat.max_mana < 60) return $"precondition: max mana {playerdat.max_mana} leaves no room above 50";
        var k = new Checks();
        foreach (var r in _results)
            k.Equal($"mana restored on level {r.level} at x {r.x}", Allowed(r.level, r.x) ? 5 : 0, r.gained, "uw2 SPELLS.C:159-161, level (PlayerLevel - 1) % 8 + 1");
        return k.Result;
    }
}
