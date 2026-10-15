using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// The Ethereal Void's effects at the end of UW1. The original tests for them once per player
/// update, with no timer: display_scr, which runs every frame, ends with
/// "if (PlayerLevel == 9 &amp;&amp; (rand() &amp; 0x1F) == 0) EtherealVoidSpecialEffects()"
/// (uw1/src/ui/INTERACT.C:256-257). The effect then flashes the view, takes hit points (for 21 to
/// 50 of them, rand() % 3 when (rand() &amp; 3) &lt; 1, else one when !(rand() &amp; 7)), starts a type 0x40 shake of rand() % 30 + 15 only when rand() % 12 is
/// not 0, and points the compass at rand() &amp; 0xF (uw1/src/motion/PHYSICS.C:160-178).
///
/// The scenario puts the player on level 9 with 40 hit points and drives the port's own
/// update, playerdat.PlayerTimedLoop, Updates times at 1/60 s each (under a second in all, so the
/// once-a-second block never runs and draws nothing). Beside it, a second BorlandRandSource with
/// the same seed plays the original's draws. After every update the generator's state, the hit
/// points and the shake counter must match the model. The seed is chosen so the effect fires
/// three times (updates 10, 21 and 31), one of them skips the shake, and in all three the 21-50
/// band's (rand() &amp; 3) &lt; 1 gives a different answer from rand() % 3 &lt; 1.
///
/// The port gated the test on playertimer &gt; 0.2 s (playerdatloop.cs), so it drew nothing for the
/// first 12 updates, always started the shake, and tested the 21-50 band with rand() % 3 &lt; 1
/// (etherealvoid.cs).
/// </summary>
public sealed class Uw1EndgameEffects : Scenario
{
    const int Updates = 50, StartHp = 40, MaxHp = 60;
    const double Delta = 1.0 / 60.0;

    public override string Name => "uw1-endgame-effects";
    public override string[] Games => new[] { "uw1" };
    public override string Cites => "underworld-exhumed uw1/src/ui/INTERACT.C:256-257 (display_scr), uw1/src/motion/PHYSICS.C:160-178 (EtherealVoidSpecialEffects)";
    public override uint Seed => 195;

    string _problem;
    int _levelBefore;
    readonly List<int> _expectFired = new();
    int _expectSkips, _bandDiffers;
    string _firstMismatch;
    int _mismatches;

    public override Task Setup(ScenarioContext c)
    {
        _levelBefore = playerdat.dungeon_level;
        if (!uimanager.InGame || uimanager.blockinput) _problem = $"not in game ({uimanager.InGame}) or input blocked ({uimanager.blockinput}), so PlayerTimedLoop does nothing";
        var timer = typeof(playerdat).GetField("playertimer", BindingFlags.NonPublic | BindingFlags.Static);
        if (timer == null) _problem = "playerdat.playertimer not found";
        else timer.SetValue(null, 0.0);
        playerdat.max_hp = MaxHp;
        playerdat.play_hp = StartHp;
        if (playerdat.play_hp != StartHp) _problem = $"hit points are {playerdat.play_hp}, not {StartHp}";
        playerdat.playerObject.AccumulatedDamage = 0;
        playerdat.Shake40_Duration_740 = 0;
        playerdat.dungeon_level = 9;
        return Task.CompletedTask;
    }

    public override Task Act(ScenarioContext c)
    {
        if (_problem != null) return Task.CompletedTask;
        if (Rng.Source is not BorlandRandSource live) { _problem = "Rng.Source is not a BorlandRandSource"; return Task.CompletedTask; }
        var model = new BorlandRandSource(live.Seed);
        int hp = StartHp;
        for (int i = 0; i < Updates; i++)
        {
            // The original's draws for this update (INTERACT.C:256, PHYSICS.C:166-177).
            int shake = 0;
            if ((model.Rand() & 0x1F) == 0)
            {
                _expectFired.Add(i);
                if (hp > 100)
                    hp -= model.Rand() % 6;
                else if (hp > 50)
                    hp -= model.Rand() % 4;
                else
                {
                    bool band = false;
                    if (hp > 20)
                    {
                        int r = model.Rand();
                        band = (r & 3) < 1;
                        if (band != (r % 3 < 1)) _bandDiffers++;
                    }
                    if (band)
                        hp -= model.Rand() % 3;
                    else if (hp > 1 && (model.Rand() & 7) == 0)
                        hp--;
                }
                if (model.Rand() % 12 != 0)
                    shake = model.Rand() % 30 + 15;
                else
                    _expectSkips++;
                model.Rand();                    // compass frame
            }

            playerdat.Shake40_Duration_740 = 0;
            playerdat.PlayerTimedLoop(Delta);
            var seedNow = (Rng.Source as BorlandRandSource)?.Seed ?? 0;

            var k = new Checks();
            k.Equal($"generator state after update {i}", model.Seed, seedNow, "INTERACT.C:256, PHYSICS.C:165-177");
            k.Equal($"hit points after update {i}", hp, playerdat.play_hp, "PHYSICS.C:165-173");
            k.Equal($"type 0x40 shake after update {i}", shake, playerdat.Shake40_Duration_740, "PHYSICS.C:175-176");
            if (k.Result != null)
            {
                _mismatches++;
                _firstMismatch ??= k.Result;
                // Follow the port from here, so later updates are judged on their own draws.
                model = new BorlandRandSource(seedNow);
                hp = playerdat.play_hp;
            }
        }
        playerdat.dungeon_level = _levelBefore;
        c.Note($"the original fires at updates [{string.Join(", ", _expectFired)}], {_expectSkips} without a shake, {_bandDiffers} where the 21-50 band test differs from % 3; {_mismatches} of {Updates} updates differed");
        return Task.CompletedTask;
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        if (_expectFired.Count < 2 || _expectSkips < 1 || _bandDiffers < 1) return "precondition: the seed no longer gives several effects, a skipped shake and a 21-50 band draw where & 3 and % 3 disagree";
        if (_firstMismatch == null) return null;
        return $"endgame effects differ from display_scr's per-update test in {_mismatches} of {Updates} updates; first: {_firstMismatch}";
    }
}
