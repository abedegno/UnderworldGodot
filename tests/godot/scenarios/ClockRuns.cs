using System;
using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// The 256 Hz clock and the game clock run at the original's rate in game: ten seconds of play add
/// 2560 to main.GlobalPITTimer and to playerdat.ClockValue.
///
/// The original's clock is an AIL timer at 0x100 Hz whose callback increments the time
/// (init_timers and cllbck_tst, underworld-exhumed uw1/src/sound/SOUND.C:168-181,
/// uw2/src/sound/SOUND.C:696-708). check_physics adds the ticks since its last call, at most 0x40,
/// to player->game_clock every frame (uw1/src/motion/PLAYMOVE.C:329-345, uw2/src/motion/PLAYMOVE.C:322-338).
///
/// The port counts the clock in main._PhysicsProcess and advances the game clock by a fixed 0x19
/// each time 0.097659 s (0x19 ticks) has passed. Both must carry the time left over from a frame:
/// dropping it made the clock gain 4 ticks a frame at --fixed-fps 60 (240 a second) and the motion
/// update come every 6 frames (0x19 per 0.1 s, 250 a second).
///
/// The scenario waits for a motion update, so the first counted one adds 0x19 and not the 0x40 of
/// a long gap, then runs 600 frames (10 s at --fixed-fps 60). The clock must rise 2560 within a
/// tick, the game clock 2560 within one update of 0x19.
/// </summary>
public sealed class ClockRuns : Scenario
{
    const int FramesToRun = 600;      // 10 s at --fixed-fps 60
    const int Expected = 10 * 256;    // 1/256 s units: uw1 SOUND.C:179, uw2 SOUND.C:706
    const int OneUpdate = 0x19;

    bool _inGameBefore, _sawUpdate;
    int _before, _after;
    uint _pitBefore, _pitAfter;

    public override string Name => "clock-runs";
    public override string Cites => "underworld-exhumed uw1/src/sound/SOUND.C:168-181, uw2/src/sound/SOUND.C:696-708, uw1/src/motion/PLAYMOVE.C:329-345, uw2/src/motion/PLAYMOVE.C:322-338; Godot main._PhysicsProcess";

    public override async Task Setup(ScenarioContext c)
    {
        // The runner has already started the new game; wait for a motion update and record both clocks.
        _inGameBefore = uimanager.InGame;
        int clock = playerdat.ClockValue;
        for (int f = 0; f < 30 && playerdat.ClockValue == clock; f++) await c.Frames(1);
        _sawUpdate = playerdat.ClockValue != clock;
        _before = playerdat.ClockValue;
        _pitBefore = main.GlobalPITTimer;
        c.Note($"clock before {_before}, 256 Hz clock {_pitBefore}, in game {_inGameBefore}");
    }

    public override async Task Act(ScenarioContext c)
    {
        await c.Frames(FramesToRun);
        _after = playerdat.ClockValue;
        _pitAfter = main.GlobalPITTimer;
        c.Note($"after {FramesToRun} frames: game clock rose {_after - _before}, 256 Hz clock rose {_pitAfter - _pitBefore}, expected {Expected}");
    }

    public override string Assert(ScenarioContext c)
    {
        if (!_inGameBefore) return "precondition: not in game (uimanager.InGame false) when the clock was read";
        if (!_sawUpdate) return "precondition: no motion update in 30 frames before the count began";
        var k = new Checks();
        long pit = _pitAfter - _pitBefore;
        int rose = _after - _before;
        k.True(Math.Abs(pit - Expected) <= 1, $"256 Hz clock rose {pit} in {FramesToRun} frames, expected {Expected} +/- 1");
        k.True(Math.Abs(rose - Expected) <= OneUpdate, $"game clock rose {rose} in {FramesToRun} frames, expected {Expected} +/- 0x19");
        return k.Result;
    }
}
