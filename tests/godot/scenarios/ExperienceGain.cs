using System.Collections.Generic;
using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// Gaining experience follows each game's own player_get_exp (uw1/src/game/SKILLCHK.C:70-108,
/// uw2/src/game/SKILLCHK.C:63-102), which differ:
///
/// UW1 does not halve a gain; it ignores a gain once experience is above 0x17700, before giving
/// skill points (SKILLCHK.C:85-86); otherwise it halves the gain (plus one) when the player's level
/// is above twice the dungeon level plus two (:88-89), gives a skill point every 3000 (:91-95), adds
/// the gain and checks for new levels every time (:97-105).
///
/// UW2 halves every gain, rounding up or down at random (SKILLCHK.C:69-70), halves it again (plus
/// one) when the player's level is above twice the world plus two (:79-80), gives a skill point
/// every 1500 (:81-86), and then makes no change at all if the gain would take experience above
/// 0x7FFF0 (:88-89). Levels are checked only while experience is at most 0x17700 (:91-92).
///
/// Each case sets experience, level and skill points, makes one gain of 100 on dungeon level 1 and
/// reads them back. The gains are even, so UW2's random rounding cannot change the result.
/// </summary>
public sealed class ExperienceGain : Scenario
{
    const int Gain = 100;

    sealed class Case
    {
        public string What, Cite;
        public int Exp, Level, Earned;          // set up
        public int WantExp, WantLevel, WantEarned; // the original's result; WantEarned < 0 is not checked
        public int GotExp, GotLevel, GotEarned, ReadExp, ReadLevel, ReadEarned;
    }

    readonly List<Case> _cases = new();
    int _dungeon;

    public override string Name => "experience-gain";
    public override string Cites => "underworld-exhumed uw1/src/game/SKILLCHK.C:70-108; uw2/src/game/SKILLCHK.C:63-102";

    public override Task Setup(ScenarioContext c)
    {
        _dungeon = playerdat.dungeon_level;
        if (c.Game == "uw1")
        {
            // Level 3 on dungeon level 1: 2 * 1 + 2 is not below 3, so the gain is whole. 1100 / 500 is
            // still below level_table[2] (3), so no new level.
            _cases.Add(new Case { What = "a gain is not halved", Cite = "uw1 SKILLCHK.C:87-89, 97",
                Exp = 1000, Level = 3, Earned = 0, WantExp = 1000 + Gain, WantLevel = 3, WantEarned = 0 });
            // Level 15: 4 < 15, so 100 / 2 + 1 = 51. 96041 is past 0x17700 (96000) and levels are still
            // checked: 96041 / 500 = 192 reaches level_table[14] (192), level 16. 96041 / 3000 = 32.
            _cases.Add(new Case { What = "levels are checked past 0x17700", Cite = "uw1 SKILLCHK.C:88-105",
                Exp = 95990, Level = 15, Earned = 31, WantExp = 95990 + Gain / 2 + 1, WantLevel = 16, WantEarned = 32 });
            // Above 0x17700 the gain is ignored before any skill point is given.
            _cases.Add(new Case { What = "a gain above 0x17700 is ignored", Cite = "uw1 SKILLCHK.C:85-86",
                Exp = 0x17701, Level = 16, Earned = 0, WantExp = 0x17701, WantLevel = 16, WantEarned = 0 });
        }
        else
        {
            // Level 2 in world 0: 2 is not below 2, so only the first halving: 100 -> 50. 650 / 500 = 1
            // is below level_table[1] (2), so no new level.
            _cases.Add(new Case { What = "a gain is halved", Cite = "uw2 SKILLCHK.C:69-70, 79, 90",
                Exp = 600, Level = 2, Earned = 0, WantExp = 600 + Gain / 2, WantLevel = 2, WantEarned = 0 });
            // Level 16: 100 -> 50 -> 50 / 2 + 1 = 26, and 0x7FFF0 - 10 + 26 is above 0x7FFF0, so no change.
            // The skill points before that test do not fit the byte the port keeps them in, so they are
            // not checked here.
            _cases.Add(new Case { What = "a gain past 0x7FFF0 makes no change", Cite = "uw2 SKILLCHK.C:88-89",
                Exp = 0x7FFF0 - 10, Level = 16, Earned = 0, WantExp = 0x7FFF0 - 10, WantLevel = 16, WantEarned = -1 });
        }
        return Task.CompletedTask;
    }

    public override Task Act(ScenarioContext c)
    {
        foreach (var k in _cases)
        {
            playerdat.Exp = k.Exp;
            playerdat.play_level = k.Level;
            playerdat.SkillPointsTotal = k.Earned;
            playerdat.SkillPoints = 0;
            k.ReadExp = playerdat.Exp; k.ReadLevel = playerdat.play_level; k.ReadEarned = playerdat.SkillPointsTotal;
            playerdat.ChangeExperience(Gain);
            k.GotExp = playerdat.Exp; k.GotLevel = playerdat.play_level; k.GotEarned = playerdat.SkillPointsTotal;
            c.Note($"{k.What}: experience {k.Exp} level {k.Level} earned {k.Earned}, ChangeExperience({Gain}), now {k.GotExp} level {k.GotLevel} earned {k.GotEarned}");
        }
        return Task.CompletedTask;
    }

    public override string Assert(ScenarioContext c)
    {
        if (_dungeon != 1) return $"precondition: on dungeon level {_dungeon}, not 1";
        foreach (var k in _cases)
            if (k.ReadExp != k.Exp || k.ReadLevel != k.Level || k.ReadEarned != k.Earned)
                return $"precondition: {k.What}: read back experience {k.ReadExp} level {k.ReadLevel} earned {k.ReadEarned}";
        var checks = new Checks();
        foreach (var k in _cases)
        {
            checks.Equal($"{k.What}: experience", k.WantExp, k.GotExp, k.Cite);
            checks.Equal($"{k.What}: level", k.WantLevel, k.GotLevel, k.Cite);
            if (k.WantEarned >= 0)
                checks.Equal($"{k.What}: skill points earned", k.WantEarned, k.GotEarned, k.Cite);
        }
        return checks.Result;
    }
}
