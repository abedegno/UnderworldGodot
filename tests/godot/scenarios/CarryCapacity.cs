using System.Collections.Generic;
using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// How much the player can carry. The original's player_compute sets the carrying limit to
/// Strength * 20 in UW1 (uw1/src/game/SKILLS.C:112) and 300 + Strength * 13 in UW2
/// (uw2/src/game/SKILLS.C:81), in tenths of a stone.
///
/// For several Strength values the scenario calls the port's playerdat.RecalculateHPManaMaxWeight,
/// the port's player_compute (made at character creation and at each level gained), and reads
/// WeightMax. In UW2 it is a guard; in UW1 it pins the port's use of UW2's rule in both games
/// (src/player/playerdat.cs:794), which lets a Strength 15 character carry 49.5 stones for DOS's 30.
/// </summary>
public sealed class CarryCapacity : Scenario
{
    static readonly int[] Strengths = { 5, 15, 25, 30 };

    public override string Name => "carry-capacity";
    public override string Cites => "underworld-exhumed uw1/src/game/SKILLS.C:102-116 (player_compute); uw2/src/game/SKILLS.C:81";

    readonly List<(int str, int max)> _results = new();
    int _strBack = -1;

    public override Task Setup(ScenarioContext c) => Task.CompletedTask;

    public override Task Act(ScenarioContext c)
    {
        int str0 = playerdat.STR;
        foreach (int s in Strengths)
        {
            playerdat.STR = s;
            if (_strBack < 0) _strBack = playerdat.STR == s ? s : -2;
            playerdat.RecalculateHPManaMaxWeight(false);
            _results.Add((s, playerdat.WeightMax));
        }
        playerdat.STR = str0;
        playerdat.RecalculateHPManaMaxWeight(false);
        foreach (var r in _results) c.Note($"Strength {r.str}: carrying limit {r.max}");
        return Task.CompletedTask;
    }

    public override string Assert(ScenarioContext c)
    {
        if (_strBack != Strengths[0]) return "precondition: Strength did not read back as set";
        bool uw2 = c.Game == "uw2";
        var k = new Checks();
        foreach (var r in _results)
            k.Equal($"carrying limit at Strength {r.str}", uw2 ? 300 + r.str * 13 : r.str * 20, r.max,
                uw2 ? "uw2 SKILLS.C:81" : "uw1 SKILLS.C:112, attr[STR] * 2 * 10");
        return k.Result;
    }
}
