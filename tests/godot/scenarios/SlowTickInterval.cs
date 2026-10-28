using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// How often the player's slow block runs. The original's duration_check runs every 20 seconds of
/// game time and counts plyregen[1] up; its slow block (food, drink, wandering monsters, fatigue,
/// natural healing) runs when the count is a multiple of 24 in UW1, which then restarts the count
/// (uw1/src/game/PLAYTIME.C:128-142), and of 30 in UW2, which restarts it at 60
/// (uw2/src/game/PLAYTIME.C:137-165). So it runs every 24th call in UW1 and every 30th in UW2.
///
/// The scenario starts the port's counters (playerdat.playerUpdateCounter, secondcounter, private,
/// set by reflection) at 0, calls the port's own playerdat.PlayerTimedLoop(20.0), one 20 second
/// step a call, 70 times, and notes the calls on which play_fatigue went up, which the slow block
/// does (uw1 PLAYTIME.C:137-138, uw2 PLAYTIME.C:146-147).
///
/// The port once ran the block every 30th call in both games (src/player/playerdatloop.cs).
/// </summary>
public sealed class SlowTickInterval : Scenario
{
    const int Calls = 70;

    public override string Name => "slow-tick-interval";
    public override string Cites => "underworld-exhumed uw1/src/game/PLAYTIME.C:128-142; uw2/src/game/PLAYTIME.C:137-165";

    string _problem;
    readonly List<int> _runs = new();

    public override Task Setup(ScenarioContext c)
    {
        playerdat.play_hunger = 0xC0;
        playerdat.play_poison = 0;
        return Task.CompletedTask;
    }

    public override Task Act(ScenarioContext c)
    {
        var counter = typeof(playerdat).GetField("playerUpdateCounter", BindingFlags.NonPublic | BindingFlags.Static);
        var seconds = typeof(playerdat).GetField("secondcounter", BindingFlags.NonPublic | BindingFlags.Static);
        if (counter == null || seconds == null) { _problem = "playerdat's private counters not found"; return Task.CompletedTask; }
        counter.SetValue(null, 0);
        seconds.SetValue(null, 0);
        for (int call = 1; call <= Calls; call++)
        {
            playerdat.play_fatigue = 0;
            playerdat.PlayerTimedLoop(20.0);
            if (playerdat.play_fatigue != 0) _runs.Add(call);
        }
        c.Note($"slow block ran on calls {string.Join(", ", _runs)}");
        return Task.CompletedTask;
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        bool uw2 = c.Game == "uw2";
        int interval = uw2 ? 30 : 24;
        string cite = uw2 ? "every 30th call: uw2 PLAYTIME.C:137, 165" : "every 24th call, the count restarting: uw1 PLAYTIME.C:128, 141";
        var k = new Checks();
        k.Equal("times the slow block ran in 70 calls", Calls / interval, _runs.Count, cite);
        for (int i = 0; i < _runs.Count && i < Calls / interval; i++)
            k.Equal($"call of slow block run {i + 1}", interval * (i + 1), _runs[i], cite);
        return k.Result;
    }
}
