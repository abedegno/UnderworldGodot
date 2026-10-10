using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// Losing experience takes it away. Death costs the player an eighth of their experience in UW1
/// (player_get_exp(-(exp >> 3)), uw1/src/game/SKILLS.C:832) and a ninth in UW2
/// (player_get_exp(-(exp / 9)), uw2/src/game/SKILLS.C:844), and player_get_exp takes a loss
/// off with a floor of 0 (uw1/src/game/SKILLCHK.C:76-82, uw2/src/game/SKILLCHK.C:71-77).
///
/// The port's death paths make the same call (ChangeExperience(-Exp >> 3) and
/// ChangeExperience(-Exp / 9), src/player/playerdatdeath.cs:57, 179), and this scenario makes
/// it directly, from 1800 experience: a loss of 225 in UW1 and 200 in UW2. Known failure today:
/// the negative branch computes Exp - newEXP with newEXP already negative
/// (src/player/playerdat.cs:729), so experience rises by the loss instead.
/// </summary>
public sealed class ExperienceLoss : Scenario
{
    const int Start = 1800;

    public override string Name => "experience-loss";
    public override string Cites => "underworld-exhumed uw1/src/game/SKILLCHK.C:76-82, uw1/src/game/SKILLS.C:832; uw2/src/game/SKILLCHK.C:71-77, uw2/src/game/SKILLS.C:844";

    int _before, _loss, _after;

    public override Task Setup(ScenarioContext c)
    {
        playerdat.Exp = Start;
        _before = playerdat.Exp;
        return Task.CompletedTask;
    }

    public override Task Act(ScenarioContext c)
    {
        // The death penalty's own expression, as each game computes it.
        int change = c.Game == "uw1" ? -playerdat.Exp >> 3 : -playerdat.Exp / 9;
        _loss = -change;
        playerdat.ChangeExperience(change);
        _after = playerdat.Exp;
        c.Note($"experience {_before}, ChangeExperience({change}), now {_after}");
        return Task.CompletedTask;
    }

    public override string Assert(ScenarioContext c)
    {
        if (_before != Start) return $"precondition: experience read back {_before} after setting {Start}";
        if (_loss <= 0) return $"precondition: the loss computed was {_loss}, not a loss";
        var k = new Checks();
        k.Equal("experience after the loss", _before - _loss, _after,
            c.Game == "uw1" ? "uw1 SKILLCHK.C:81, loss from SKILLS.C:832" : "uw2 SKILLCHK.C:76, loss from SKILLS.C:844");
        return k.Result;
    }
}
