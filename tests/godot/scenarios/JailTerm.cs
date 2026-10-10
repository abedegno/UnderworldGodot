using System.Reflection;
using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// Jail (UW2). When the player is beaten in Lord British's castle the original's
/// put_player_in_jail sets his hit points to his strength (ThePlayer->hp = playerdat->attr[0],
/// uw2/src/event/WORLDEV.C:2287) and passes (0x28 - hour) * 0x3C - (rand() % 0x5A + 0x3C)
/// seconds, where hour is game_clock / 0x3C00 % 0x14 (WORLDEV.C:2291-2295): between 0x3C and
/// 0x95 seconds less than (0x28 - hour) minutes.
///
/// The port's GoToJail (src/player/playerdatdeath.cs:203-207) sets HP to the maximum and adds
/// the random part where DOS subtracts it, (0x28 - hour) * 0x3C - rand(0x5A) + 0x3C, so the term
/// is 0x3C - 0x59 + 0x3C .. 0x3C + 0x3C seconds longer: never in DOS's range.
///
/// GoToJail is the teleport callback of the death path; it is private, so the scenario calls it
/// by reflection on level 1 (the castle), where the new game starts. The runner's new game has
/// 20 strength and 30 hit points, so the two HP rules differ.
/// </summary>
public sealed class JailTerm : Scenario
{
    public override string Name => "jail-term";
    public override string[] Games => new[] { "uw2" };
    public override string Cites => "underworld-exhumed uw2/src/event/WORLDEV.C:2287, 2291-2295, 163-169";

    string _problem;
    int _clockBefore, _clockAfter, _hour, _str, _maxHp, _hpAfter, _level;

    public override Task Setup(ScenarioContext c)
    {
        _level = playerdat.dungeon_level;
        if (_level != 1) _problem = $"the new game is on level {_level}, not the castle";
        _str = playerdat.STR;
        _maxHp = playerdat.max_hp;
        if (_str == _maxHp) _problem = $"strength and maximum HP are both {_str}; the two rules could not be told apart";
        return Task.CompletedTask;
    }

    public override Task Act(ScenarioContext c)
    {
        if (_problem != null) return Task.CompletedTask;
        var jail = typeof(playerdat).GetMethod("GoToJail", BindingFlags.NonPublic | BindingFlags.Static);
        if (jail == null) { _problem = "playerdat.GoToJail not found"; return Task.CompletedTask; }
        _clockBefore = playerdat.ClockValue;
        _hour = (_clockBefore / 0x3C00) % 0x14;
        jail.Invoke(null, null);
        _clockAfter = playerdat.ClockValue;
        _hpAfter = playerdat.play_hp;
        c.Note($"hour term {_hour}; clock rose {_clockAfter - _clockBefore} ({(_clockAfter - _clockBefore) >> 8} s); HP {_hpAfter}, strength {_str}, max HP {_maxHp}");
        return Task.CompletedTask;
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        long baseSeconds = (0x28 - _hour) * 0x3C;
        var k = new Checks();
        k.Within("jail term in seconds", baseSeconds - (0x59 + 0x3C), baseSeconds - 0x3C, (long)(_clockAfter - _clockBefore) >> 8,
            "(0x28 - hour) * 0x3C - (rand() % 0x5A + 0x3C): uw2 WORLDEV.C:2295");
        k.Equal("HP after jail", _str, _hpAfter, "ThePlayer->hp = playerdat->attr[0]: uw2 WORLDEV.C:2287");
        return k.Result;
    }
}
