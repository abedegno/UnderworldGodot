using System;
using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// The game clock runs in game: ten seconds of play add ten seconds to playerdat.ClockValue.
///
/// The original counts the clock in 1/256 s units: check_physics adds the time elapsed since
/// its last call, GAME_TIME() - last_time, to player->game_clock every frame
/// (underworld-exhumed uw1/src/motion/PLAYMOVE.C:329-345, uw2/src/motion/PLAYMOVE.C:322-338),
/// and GAME_TIME() is the 256 Hz PIT count. So 600 frames at 60 fps is 10 s, 2560 units.
///
/// The port advances the clock in main._PhysicsProcess (main.cs:354) by a fixed 0x19 each time
/// 0.097659 s has passed, which is 256 a second. At --fixed-fps 60 that threshold is crossed
/// every 6 frames (0.1 s), so 600 frames give 100 ticks: the first adds 0x40 (more than 0x40 PIT
/// ticks have passed since the last one, as in the original) and the other 99 add 0x19, 2539 in
/// all. Against 2560, the 6-frame quantisation (0x19 per 0.1 s, 250 a second) loses 60 and the
/// first 0x40 tick gains 39. The tolerance is 5%.
/// </summary>
public sealed class ClockRuns : Scenario
{
    const int FramesToRun = 600;      // 10 s at --fixed-fps 60
    const int Expected = 10 * 256;    // 1/256 s units: uw1 PLAYMOVE.C:329-345, uw2 PLAYMOVE.C:322-338
    const double Tolerance = 0.05;

    bool _inGameBefore;
    int _before;
    int _after;

    public override string Name => "clock-runs";
    public override string Cites => "underworld-exhumed uw1/src/motion/PLAYMOVE.C:329-345, uw2/src/motion/PLAYMOVE.C:322-338; Godot main.cs:354";

    public override Task Setup(ScenarioContext c)
    {
        // The runner has already started the new game; record the clock in game.
        _inGameBefore = uimanager.InGame;
        _before = playerdat.ClockValue;
        c.Note($"clock before {_before}, in game {_inGameBefore}");
        return Task.CompletedTask;
    }

    public override async Task Act(ScenarioContext c)
    {
        await c.Frames(FramesToRun);
        _after = playerdat.ClockValue;
        c.Note($"clock after {FramesToRun} frames {_after}, rose {_after - _before}, expected {Expected}");
    }

    public override string Assert(ScenarioContext c)
    {
        if (!_inGameBefore) return "precondition: not in game (uimanager.InGame false) when the clock was read";
        int rose = _after - _before;
        if (Math.Abs(rose - Expected) > Expected * Tolerance)
            return $"clock rose {rose} in {FramesToRun} frames, expected {Expected} +/- 5%";
        return null;
    }
}

