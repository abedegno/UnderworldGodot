using System.Threading.Tasks;
using Godot;

namespace Underworld.Scenarios;

/// <summary>
/// A night's sleep passes the night. The original's player_sleep adds 2 to 6 hours to the game
/// clock when the player lies down, and if the sleep is not interrupted the rest of a 7 to 10
/// hour night (plus 1 or 2 hours when HP is below 10) when it ends: UW1 adds hours * 0xE1000 to
/// the clock itself (uw1/src/game/SKILLS.C:545-546, 580-583), UW2 calls
/// pass_time(hours * 3600), which adds seconds << 8 (uw2/src/game/SKILLS.C:463-466, 507-510;
/// uw2/src/event/WORLDEV.C:163-166). The clock is in 1/256 s, so an hour is 0xE1000, and an
/// uninterrupted night at full health moves it on by 7 to 10 hours.
///
/// The scenario sleeps the way a player does, by using a bedroll (item 0x121, the port's
/// use.Use, which calls sleep.Sleep(1); DOS player_sleep(1)), then lets frames pass until any
/// dream has played and the game is back in play. Known failure today: the port's
/// playerdat.AdvanceTime only prints (src/player/playerdatclock.cs:86), so the clock moves only
/// by the seconds of real play the frames take.
///
/// Precondition that the sleep really ran to its end: fatigue, set to 0x30 first, is 0 after
/// (the uninterrupted night zeroes it, uw1 SKILLS.C:589, uw2 SKILLS.C:516; an interrupted sleep
/// would leave the port 0x18) and hunger has dropped (uw1 SKILLS.C:601, uw2 SKILLS.C:528).
/// </summary>
public sealed class SleepPassesTime : Scenario
{
    const long Hour = 0xE1000;                 // 3600 s << 8
    const int BedrollItem = 0x121;            // ITEM_BEDROLL, uw1/uw2 include/items.h
    const ulong SettleMs = 30000;              // dreams are cutscenes with real-time waits

    public override string Name => "sleep-passes-time";
    public override string Cites => "underworld-exhumed uw1/src/game/SKILLS.C:545-546, 580-583; uw2/src/game/SKILLS.C:463-466, 507-510, uw2/src/event/WORLDEV.C:163-166";

    int _clockBefore, _clockAfter, _hungerBefore, _hungerAfter, _fatigueAfter, _hpBefore, _frames;
    bool _bedroll, _settled, _usedOk;

    public override Task Setup(ScenarioContext c)
    {
        playerdat.play_fatigue = 0x30;
        _hungerBefore = playerdat.play_hunger;
        _hpBefore = playerdat.play_hp;
        _clockBefore = playerdat.ClockValue;
        return Task.CompletedTask;
    }

    public override async Task Act(ScenarioContext c)
    {
        int slot = ObjectCreator.PrepareNewObject(BedrollItem);
        var bedroll = slot != 0 ? UWTileMap.current_tilemap.LevelObjects[slot] : null;
        _bedroll = bedroll != null && bedroll.item_id == BedrollItem;
        if (!_bedroll) return;

        _usedOk = use.Use(bedroll, playerdat.playerObject, UWTileMap.current_tilemap.LevelObjects, WorldObject: true);

        // Let any dream play out and the game come back to play.
        ulong deadline = Time.GetTicksMsec() + SettleMs;
        int stable = 0;
        while (Time.GetTicksMsec() < deadline)
        {
            await c.Frames(1);
            _frames++;
            stable = (uimanager.InGame && !cutsplayer.IsPlaying && !uimanager.blockinput) ? stable + 1 : 0;
            if (stable >= 30) { _settled = true; break; }
        }
        _clockAfter = playerdat.ClockValue;
        _hungerAfter = playerdat.play_hunger;
        _fatigueAfter = playerdat.play_fatigue;
        c.Note($"clock rose {_clockAfter - _clockBefore} over {_frames} frames; fatigue 0x30 -> 0x{_fatigueAfter:X}, hunger 0x{_hungerBefore:X} -> 0x{_hungerAfter:X}");
    }

    public override string Assert(ScenarioContext c)
    {
        bool uw1 = c.Game == "uw1";
        if (!_bedroll) return "precondition: no bedroll could be made";
        if (!_usedOk) return "precondition: using the bedroll did nothing";
        if (!_settled) return $"precondition: the game did not come back to play within {SettleMs / 1000} s of the sleep";
        if (_hpBefore < 10) return $"precondition: HP {_hpBefore} is below 10, which lengthens the night";
        if (_fatigueAfter != 0) return $"precondition: the sleep did not run to its end (fatigue 0x{_fatigueAfter:X}, not 0; uw1 SKILLS.C:589, uw2 SKILLS.C:516)";
        if (_hungerAfter >= _hungerBefore) return $"precondition: the sleep did not run to its end (hunger 0x{_hungerBefore:X} -> 0x{_hungerAfter:X}; uw1 SKILLS.C:601, uw2 SKILLS.C:528)";

        // The frames themselves run the clock: at most 0x40 for the first step and 0x19 per 6 frames.
        long play = 0x40 + (_frames / 6 + 1) * 0x19;
        var k = new Checks();
        k.Within("clock advance over the night", 7 * Hour, 10 * Hour + play, (long)_clockAfter - _clockBefore,
            uw1 ? "7 to 10 hours of 0xE1000: uw1 SKILLS.C:545-546, 580-583"
                : "7 to 10 hours through pass_time: uw2 SKILLS.C:463-466, 507-510, WORLDEV.C:163-166");
        return k.Result;
    }
}
