using System.Reflection;
using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// Health that comes back on its own. The original's duration_check runs every 20 seconds of
/// game time; on every 30th call in UW2 (uw2/src/game/PLAYTIME.C:137) and every 24th in UW1
/// (uw1/src/game/PLAYTIME.C:128) it makes a Strength check against 15 and, if it passes, gives
/// exactly one hit point: restore_hp(ThePlayer, -1), a negative amount being added as it is
/// (uw2 PLAYTIME.C:148-149, uw2/src/combat/SPELLS.C:176-194; uw1 PLAYTIME.C:139-140). So that
/// call heals 0 or 1, whatever the maximum.
///
/// The scenario sets Strength 30 and low hit points, then drives the port's own timed loop,
/// playerdat.PlayerTimedLoop, 20 seconds a call (each call is one 20 second step), until the
/// step whose counter is a multiple of 30, the port's "every 5 minutes" block
/// (src/player/playerdatloop.cs), and measures the hit points that step gave. The port's counter
/// is private, so it is read by reflection; the interval itself is not what this scenario tests.
///
/// The port once checked Strength against 10 and passed the check's result to
/// HPRegenerationChange, which gives 1 + ((rand + n) * max_hp) >> 4, a large share of the
/// maximum (playerdatloop.cs:240-243).
/// </summary>
public sealed class NaturalHealing : Scenario
{
    const int Strength = 30, StartHp = 5;

    public override string Name => "natural-healing";
    public override string Cites => "underworld-exhumed uw2/src/game/PLAYTIME.C:137-149, uw2/src/combat/SPELLS.C:176-194; uw1/src/game/PLAYTIME.C:128-141, uw1/src/combat/SPELLS.C:186";

    string _problem;
    int _hpBefore, _hpAfter, _maxHp, _calls;
    bool _reached;

    public override Task Setup(ScenarioContext c)
    {
        playerdat.STR = Strength;
        playerdat.play_hp = StartHp;
        playerdat.play_hunger = 0xC0;
        playerdat.play_poison = 0;
        if (playerdat.max_hp <= StartHp + 1) _problem = $"maximum hit points {playerdat.max_hp} leave no room to heal";
        return Task.CompletedTask;
    }

    public override Task Act(ScenarioContext c)
    {
        if (_problem != null) return Task.CompletedTask;
        var counter = typeof(playerdat).GetField("playerUpdateCounter", BindingFlags.NonPublic | BindingFlags.Static);
        if (counter == null) { _problem = "playerdat.playerUpdateCounter not found"; return Task.CompletedTask; }
        for (_calls = 1; _calls <= 61; _calls++)
        {
            playerdat.play_hp = StartHp;
            _hpBefore = playerdat.play_hp;
            playerdat.PlayerTimedLoop(20.0);
            int n = (int)counter.GetValue(null);
            if (n % 30 == 0)
            {
                _reached = true;
                _hpAfter = playerdat.play_hp;
                _maxHp = playerdat.max_hp;
                break;
            }
        }
        c.Note($"Strength {Strength}, hit points {_hpBefore} of {_maxHp}: the 5 minute step gave {_hpAfter - _hpBefore} (after {_calls} steps)");
        return Task.CompletedTask;
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        if (!_reached) return "precondition: the timed loop never reached its 5 minute step in 61 calls";
        var k = new Checks();
        k.Within("hit points regained in one regeneration step", 0, 1, _hpAfter - _hpBefore,
            c.Game == "uw2" ? "uw2 PLAYTIME.C:148-149, restore_hp(ThePlayer, -1)" : "uw1 PLAYTIME.C:139-140, restore_hp(ThePlayer, -1)");
        return k.Result;
    }
}
