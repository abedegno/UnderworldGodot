using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// Losing experience takes it away. Death costs the player an eighth of their experience in UW1
/// (player_get_exp(-(int)(exp >> 3)), uw1/src/game/SKILLS.C:832) and a ninth in UW2
/// (player_get_exp(-(int)(exp / 9)), uw2/src/game/SKILLS.C:844), and player_get_exp takes a loss
/// off with a floor of 0 (uw1/src/game/SKILLCHK.C:76-82, uw2/src/game/SKILLCHK.C:71-77).
///
/// The port's death paths all take their change from playerdat.DeathExperienceChange
/// (src/player/playerdatdeath.cs), and this scenario calls it from 1801 experience, which is a
/// multiple of neither 8 nor 9, so the amount must truncate before it is negated as the
/// original's does: a loss of 225 in UW1 and 200 in UW2. It then checks the floor: a loss
/// larger than the experience left leaves 0.
/// </summary>
public sealed class ExperienceLoss : Scenario
{
    const int Start = 1801;
    const int FloorStart = 100;

    public override string Name => "experience-loss";
    public override string Cites => "underworld-exhumed uw1/src/game/SKILLCHK.C:76-82, uw1/src/game/SKILLS.C:832; uw2/src/game/SKILLCHK.C:71-77, uw2/src/game/SKILLS.C:844";

    int _before, _expected, _change, _after, _floorBefore, _floorAfter;

    public override Task Setup(ScenarioContext c)
    {
        playerdat.Exp = Start;
        _before = playerdat.Exp;
        return Task.CompletedTask;
    }

    public override Task Act(ScenarioContext c)
    {
        // The death penalty as the original computes it: -(int)(exp >> 3) in UW1 (SKILLS.C:832),
        // -(int)(exp / 9) in UW2 (SKILLS.C:844), truncated before it is negated.
        _expected = c.Game == "uw1" ? -(playerdat.Exp >> 3) : -(playerdat.Exp / 9);
        _change = playerdat.DeathExperienceChange();
        playerdat.ChangeExperience(_change);
        _after = playerdat.Exp;
        c.Note($"experience {_before}, ChangeExperience({_change}), now {_after}");

        // A loss larger than what is left: if (-n > exp) exp = 0 (SKILLCHK.C:78-79 / 73-74).
        playerdat.Exp = FloorStart;
        _floorBefore = playerdat.Exp;
        playerdat.ChangeExperience(-(FloorStart + 1));
        _floorAfter = playerdat.Exp;
        c.Note($"experience {_floorBefore}, ChangeExperience({-(FloorStart + 1)}), now {_floorAfter}");
        return Task.CompletedTask;
    }

    public override string Assert(ScenarioContext c)
    {
        if (_before != Start) return $"precondition: experience read back {_before} after setting {Start}";
        if (_floorBefore != FloorStart) return $"precondition: experience read back {_floorBefore} after setting {FloorStart}";
        if (_expected >= 0) return $"precondition: the loss computed was {-_expected}, not a loss";
        var k = new Checks();
        var cite = c.Game == "uw1" ? "uw1 SKILLS.C:832" : "uw2 SKILLS.C:844";
        k.Equal("death experience change", _expected, _change, cite);
        k.Equal("experience after the loss", _before + _expected, _after,
            c.Game == "uw1" ? "uw1 SKILLCHK.C:81, loss from SKILLS.C:832" : "uw2 SKILLCHK.C:76, loss from SKILLS.C:844");
        k.Equal("experience after a loss larger than it", 0, _floorAfter,
            c.Game == "uw1" ? "uw1 SKILLCHK.C:78-79" : "uw2 SKILLCHK.C:73-74");
        return k.Result;
    }
}
