using System.Reflection;
using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// The time of day steps on in play (UW2). The original's display_scr runs duration_check every
/// 20 seconds of game time (uw2/src/ui/INTERACT.C:110-117), and every 60th check, every 20
/// minutes, duration_check steps the time of day, X clock 0 (XC_TIME), by one, modulo 72
/// (DAY_STEPS), and brings the day schedule up to date (uw2/src/game/PLAYTIME.C:151-163).
///
/// The port's PlayerTimedLoop counts the same 20-second steps in playerUpdateCounter and resets
/// it every 60th (src/player/playerdatloop.cs, the "every 20 mins" block), but only prints two
/// TODOs there, so X clock 0 changes only through sleep, repair or jail (AdvanceTime).
///
/// Twenty minutes of play is 72000 frames, so the scenario sets the port's own counters to the
/// last second before the 60th step (playerUpdateCounter 59, secondcounter 19, by reflection,
/// since they are private) and lets two seconds of frames pass. The precondition is that the
/// counter wrapped to 0, so the 60th step really ran.
/// </summary>
public sealed class TimeOfDayHourlyStep : Scenario
{
    const int DaySteps = 72;    // DAY_STEPS, uw2/src/include/player.h:146

    public override string Name => "time-of-day-hourly-step";
    public override string[] Games => new[] { "uw2" };
    public override string Cites => "underworld-exhumed uw2/src/game/PLAYTIME.C:151-163, uw2/src/ui/INTERACT.C:110-117";

    FieldInfo _counter, _seconds;
    bool _inGame;
    int _before, _after, _counterAfter;
    string _problem;

    public override Task Setup(ScenarioContext c)
    {
        const BindingFlags f = BindingFlags.NonPublic | BindingFlags.Static;
        _counter = typeof(playerdat).GetField("playerUpdateCounter", f);
        _seconds = typeof(playerdat).GetField("secondcounter", f);
        if (_counter == null || _seconds == null) { _problem = "playerdat.playerUpdateCounter or secondcounter not found"; return Task.CompletedTask; }
        playerdat.SetXClock(0, 0x20);
        _counter.SetValue(null, 59);
        _seconds.SetValue(null, 19);
        _inGame = uimanager.InGame;
        _before = playerdat.GetXClock(0);
        return Task.CompletedTask;
    }

    public override async Task Act(ScenarioContext c)
    {
        if (_problem != null) return;
        await c.Frames(120);
        _after = playerdat.GetXClock(0);
        _counterAfter = (int)_counter.GetValue(null);
        c.Note($"X clock 0 {_before} -> {_after}; the 20-second step counter is now {_counterAfter}");
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        if (!_inGame) return "precondition: not in game";
        if (_counterAfter >= 59) return $"precondition: the 60th 20-second step did not run (counter {_counterAfter})";
        var k = new Checks();
        k.Equal("time of day (X clock 0) after the 60th 20-second step", (_before + 1) % DaySteps, _after,
            "xclock[XC_TIME]++ modulo DAY_STEPS: uw2 PLAYTIME.C:152-156");
        return k.Result;
    }
}

/// <summary>
/// A new UW2 game starts with the time of day, X clock 0, at 0. init_char sets xclock[XC_TIME]
/// to 0xF (uw2/src/game/CHARGEN.C:86) but then clears all sixteen X clocks with
/// memset(player->xclock, 0, 0x10) (CHARGEN.C:116), so 0 is what the game starts with; the
/// port does the same (src/player/playerdatainit.cs sets 0xF and then clears the X clocks).
/// A guard: the scenario findings said the port should start at 0xF, which misread init_char.
/// </summary>
public sealed class NewGameTimeOfDay : Scenario
{
    public override string Name => "new-game-time-of-day";
    public override string[] Games => new[] { "uw2" };
    public override string Cites => "underworld-exhumed uw2/src/game/CHARGEN.C:86, 116";

    bool _inGame;
    int _timeOfDay;

    public override Task Setup(ScenarioContext c)
    {
        _inGame = uimanager.InGame;
        _timeOfDay = playerdat.GetXClock(0);
        return Task.CompletedTask;
    }

    public override Task Act(ScenarioContext c) => Task.CompletedTask;

    public override string Assert(ScenarioContext c)
    {
        if (!_inGame) return "precondition: the new game was not in game";
        var k = new Checks();
        k.Equal("new game's time of day (X clock 0)", 0, _timeOfDay, "set to 0xF at uw2 CHARGEN.C:86, then cleared by the memset at :116");
        return k.Result;
    }
}
