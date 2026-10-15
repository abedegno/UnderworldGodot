using System.Collections.Generic;
using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// What a skill point buys. The original's get_skill, which trainers and shrines call, refuses a
/// skill above twice its governing attribute or at 30; otherwise it adds 1, then 1 more when the
/// attribute is not strength and half the attribute's value is above the skill, then 1 more when
/// the skill is still below the attribute's value and rand() % skill_rng[attribute] is below
/// attribute - skill, at most 30 (uw2/src/game/SKILLS.C:157-185; uw1/src/game/SKILLS.C:189-217).
/// skill_rng is {25, 30, 15} in UW2 (uw2 SKILLS.C:149) and {25, 40, 10} in UW1 (uw1 SKILLS.C:179).
/// The governing attribute, prime, is strength below skill 7, intelligence below 10, dexterity
/// above (uw2 SKILLS.C:107-114, uw1 SKILLS.C:137-144).
///
/// Each row sets an attribute and a skill, seeds the game's generator, calls the port's
/// playerdat.IncreaseSkill (src/player/playerdatskills.cs), and compares the skill after with the
/// original's rule over the same seed. Lore (skill 8) is left out, as raising it also resets
/// object identification.
///
/// The port once compared the skill with the attribute's index (0, 1 or 2) rather than its value,
/// so neither extra point came once a skill was above 2, nested the third point under the
/// not-strength test, and used UW1's ranges in UW2 (playerdatskills.cs:513-547).
/// </summary>
public sealed class SkillPointGain : Scenario
{
    // (skill, attribute value, skill before, seed)
    static readonly (int Skill, int Attr, int Before, uint Seed)[] Rows =
    {
        (0, 25, 5, 11),    // attack, strength: no second point, third by chance
        (3, 20, 18, 22),   // sword, strength, just below the attribute
        (10, 24, 4, 33),   // a dexterity skill well below half the attribute: two, maybe three
        (12, 16, 15, 44),  // dexterity, skill near the attribute
        (7, 28, 10, 55),   // mana, intelligence
        (9, 30, 6, 66),    // casting, intelligence
        (11, 20, 29, 77),  // at 29: one point, capped at 30
        (5, 5, 11, 88),    // mace above twice its attribute: refused
    };

    public override string Name => "skill-point-gain";
    public override string Cites => "underworld-exhumed uw2/src/game/SKILLS.C:107-114, 149, 157-185; uw1/src/game/SKILLS.C:137-144, 179, 189-217";

    readonly List<(int skill, int attr, int before, int expected, int actual, int setBack)> _results = new();

    static int Prime(int skill) => skill < 7 ? 0 : skill < 10 ? 2 : 1;

    static int Expected(bool uw2, int skill, int attr, int value, uint seed)
    {
        int[] rng = uw2 ? new[] { 25, 30, 15 } : new[] { 25, 40, 10 };
        int stat = Prime(skill);
        if (attr * 2 < value || value >= 30) return value;
        var d = new BatchB.DosRand(seed);
        value++;
        if (stat != 0 && attr / 2 > value) value++;
        if (value < attr && d.Rand() % rng[stat] < attr - value) value++;
        return value > 30 ? 30 : value;
    }

    static void SetAttribute(int stat, int value)
    {
        switch (stat)
        {
            case 0: playerdat.STR = value; break;
            case 1: playerdat.DEX = value; break;
            default: playerdat.INT = value; break;
        }
    }

    public override Task Setup(ScenarioContext c) => Task.CompletedTask;

    public override Task Act(ScenarioContext c)
    {
        bool uw2 = c.Game == "uw2";
        foreach (var r in Rows)
        {
            int stat = Prime(r.Skill);
            SetAttribute(stat, r.Attr);
            playerdat.SetSkillValue(r.Skill, r.Before);
            int setBack = playerdat.GetSkillValue(r.Skill);
            BatchB.Seed(r.Seed);
            playerdat.IncreaseSkill(r.Skill);
            int after = playerdat.GetSkillValue(r.Skill);
            _results.Add((r.Skill, r.Attr, r.Before, Expected(uw2, r.Skill, r.Attr, r.Before, r.Seed), after, setBack));
        }
        foreach (var r in _results)
            c.Note($"skill {r.skill}, attribute {r.attr}: {r.before} -> {r.actual} (original {r.expected})");
        return Task.CompletedTask;
    }

    public override string Assert(ScenarioContext c)
    {
        bool uw2 = c.Game == "uw2";
        foreach (var r in _results)
            if (r.setBack != r.before) return $"precondition: skill {r.skill} read back {r.setBack} after setting {r.before}";
        var k = new Checks();
        foreach (var r in _results)
            k.Equal($"skill {r.skill} (attribute {r.attr}) raised from {r.before}", r.expected, r.actual,
                uw2 ? "uw2 SKILLS.C:168-180, skill_rng SKILLS.C:149" : "uw1 SKILLS.C:200-212, skill_rng SKILLS.C:179");
        return k.Result;
    }
}
