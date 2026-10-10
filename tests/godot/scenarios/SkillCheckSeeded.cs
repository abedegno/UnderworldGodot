using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace Underworld.Scenarios;

/// <summary>
/// The RNG seam reaches the running game: with the runner's BorlandRandSource seeded to a row
/// of the skill_check vector table, playerdat.SkillCheck in the live game gives that row's
/// result and leaves the generator at its seed_out.
///
/// The row is skill_check.csv line 27 (value 20, target 30, seed 2992437923: result 1, a
/// success, seed_out 32214464), the same in uw1 and uw2. The table is the output of the
/// original's own skill_check (underworld-exhumed uw1/src/game/SKILLCHK.C:48,
/// uw2/src/game/SKILLCHK.C:39, the same routine); tests/Underworld.Rules.Tests checks the whole
/// table outside the game. Setup reads the row from the table so the numbers here cannot drift
/// from it.
/// </summary>
public sealed class SkillCheckSeeded : Scenario
{
    const int Value = 20, Target = 30;
    const uint RowSeed = 2992437923;

    long _result = long.MinValue, _seedOut;
    bool _seededBefore;
    string _rowProblem;
    long _expectResult, _expectSeedOut;

    public override string Name => "skill-check-seeded";
    public override string Cites => "underworld-exhumed uw1/src/game/SKILLCHK.C:48 (skill_check), uw2/src/game/SKILLCHK.C:39; vectors/uwN/skill_check.csv:27";
    public override uint Seed => RowSeed;

    public override Task Setup(ScenarioContext c)
    {
        string csv = ProjectSettings.GlobalizePath($"res://tests/Underworld.Rules.Tests/vectors/{c.Game}/skill_check.csv");
        if (!File.Exists(csv)) { _rowProblem = $"no vector table at {csv}"; return Task.CompletedTask; }
        var row = File.ReadLines(csv).Skip(1).Select(l => l.Split(','))
            .FirstOrDefault(f => f.Length == 5 && f[0] == $"{Value}" && f[1] == $"{Target}" && f[2] == $"{RowSeed}");
        if (row == null) { _rowProblem = "the row (20, 30, 2992437923) is not in the table"; return Task.CompletedTask; }
        _expectResult = long.Parse(row[3]);
        _expectSeedOut = long.Parse(row[4]);
        return Task.CompletedTask;
    }

    public override Task Act(ScenarioContext c)
    {
        // Synchronous, with no frame between the runner seeding the source and this call.
        _seededBefore = Rng.Source is BorlandRandSource b && b.Seed == RowSeed;
        _result = (int)playerdat.SkillCheck(Value, Target);
        _seedOut = Rng.Source is BorlandRandSource after ? after.Seed : -1;
        c.Note($"SkillCheck({Value}, {Target}) = {_result}, seed now {_seedOut}; table says {_expectResult}, {_expectSeedOut}");
        return Task.CompletedTask;
    }

    public override string Assert(ScenarioContext c)
    {
        if (_rowProblem != null) return "precondition: " + _rowProblem;
        if (!_seededBefore) return $"precondition: Rng.Source was not a BorlandRandSource at seed {RowSeed} when SkillCheck ran";
        var k = new Checks();
        k.Equal("SkillCheck result", _expectResult, _result, c.Game == "uw1" ? "skill_check.csv:27, uw1 SKILLCHK.C:48" : "skill_check.csv:27, uw2 SKILLCHK.C:39");
        k.Equal("seed after", _expectSeedOut, _seedOut, "skill_check.csv:27");
        return k.Result;
    }
}
