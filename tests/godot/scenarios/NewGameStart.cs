using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// A new game starts where and as the original's does, for everything the new-game path itself
/// decides.
///
/// The original's "create character" (MAINMENU.C) blanks the record with init_char(1)
/// (CHARGEN.C, create_player), runs the chargen screens, loads level 1 (GetLevel(1)) and puts
/// the player on the start square (player_setup). The runner's path is the port's
/// counterpart without the screens: playerdat.InitEmptyPlayer, which is meant to mirror
/// init_char(1), then uimanager.JourneyOnwards("DATA").
///
/// Pinned here: the level and the start square, and the init_char fields that no chargen
/// choice changes and that the port sets the same way (experience level, skill points, the
/// game clock, hunger, the detail level; in UW1 the moonstone's level, the talismans left and
/// game variable 0x1A).
///
/// Not pinned: hit points and the attributes. InitEmptyPlayer leaves them 0, and the runner
/// fills in main.LaunchUWDemo's stand-in stats (HP 30/30, STR/DEX/INT 20); they are setup, not
/// the port's behaviour. Chargen-dependent fields (sex, hand, class, skills, portrait, name,
/// difficulty) are not reached by this path at all. The init_char fields the port gets wrong
/// are in new-game-record, so this scenario keeps guarding the ones it gets right.
///
/// Also not pinned: experience and mana. JourneyOnwards' first view already pays exploration
/// experience for the squares first seen (src/utility/visionlos.cs:271-293; measured: 6 in UW1,
/// 0 in UW2), so experience is no longer init_char's 0 when the scenario reads it, and that
/// ChangeExperience call recomputes max mana from the stand-in intelligence
/// (src/player/playerdat.cs:712; 2 in UW1). Both are consequences of the stand-in and the first
/// frame, not of InitEmptyPlayer.
///
/// What is pinned for hit points and mana is the derivation from the record: the act is the
/// recompute the port's chargen makes once the attributes are known
/// (playerdat.RecalculateHPManaMaxWeight(true), src/player/chargen/chargen.cs:223 and :169), the
/// counterpart of the original's player_compute(1) (uw1 CHARGEN.C:258, uw2 CHARGEN.C:242). For
/// the record as it stands (level 1, STR 20, INT 20, mana skill 0) the original gives max HP
/// 30 + 1 * 20 / 5 = 34 and max mana (0 + 1) * 20 >> 3 = 2, with mana filled to it
/// (uw1 SKILLS.C:106-114, uw2 SKILLS.C:78-83). Carrying capacity is left out: UW1's is
/// STR * 20 (uw1 SKILLS.C:112) and the port uses UW2's STR * 13 + 300 in both, a known gap
/// (top-30 UW1 entry 14).
/// </summary>
public sealed class NewGameStart : Scenario
{
    public override string Name => "new-game-start";
    public override string Cites => "underworld-exhumed uw1/src/game/CHARGEN.C:87-139, uw1/src/ui/MAINMENU.C:227-229; uw2/src/game/CHARGEN.C:76-127, uw2/src/ui/MAINMENU.C:200-203";

    bool _inGame;
    int _level, _tileX, _tileY, _xpLevel, _exp, _points, _pointsEarned, _clock, _hunger, _detail, _maxMana, _mana;
    int _moonstone, _talismans, _var1A;

    public override Task Setup(ScenarioContext c)
    {
        // Read straight after the runner's new game: nothing has acted yet.
        _inGame = uimanager.InGame;
        _level = playerdat.dungeon_level;
        _tileX = playerdat.playerObject.tileX;
        _tileY = playerdat.playerObject.tileY;
        _xpLevel = playerdat.play_level;
        _exp = playerdat.Exp;
        _points = playerdat.SkillPoints;
        _pointsEarned = playerdat.SkillPointsTotal;
        _clock = playerdat.ClockValue;
        _hunger = playerdat.play_hunger;
        _detail = playerdat.DetailLevel;
        _maxMana = playerdat.max_mana;
        _mana = playerdat.play_mana;
        _moonstone = playerdat.GetMoonstone(0);
        _talismans = playerdat.GetQuest(36);
        _var1A = playerdat.GetGameVariable(0x1A);
        c.Note($"level {_level} at ({_tileX}, {_tileY}), clock 0x{_clock:X}, hunger 0x{_hunger:X}, mana {_mana}/{_maxMana}");
        return Task.CompletedTask;
    }

    int _str, _int, _manaSkill, _xpLevelAtAct, _maxHpAfter, _maxManaAfter, _manaAfter;

    public override Task Act(ScenarioContext c)
    {
        _str = playerdat.STR; _int = playerdat.INT; _manaSkill = playerdat.ManaSkill; _xpLevelAtAct = playerdat.play_level;
        playerdat.RecalculateHPManaMaxWeight(true);
        _maxHpAfter = playerdat.max_hp; _maxManaAfter = playerdat.max_mana; _manaAfter = playerdat.play_mana;
        c.Note($"experience {_exp} after the first view; recompute from STR {_str} INT {_int} mana skill {_manaSkill}: max HP {_maxHpAfter}, mana {_manaAfter}/{_maxManaAfter}");
        return Task.CompletedTask;
    }

    public override string Assert(ScenarioContext c)
    {
        if (!_inGame) return "precondition: the new game was not in game";
        bool uw1 = c.Game == "uw1";
        var k = new Checks();
        if (_str != 20 || _int != 20 || _manaSkill != 0 || _xpLevelAtAct != 1)
            return $"precondition: the record is not level 1, STR 20, INT 20, mana skill 0 (got {_xpLevelAtAct}, {_str}, {_int}, {_manaSkill})";
        k.Equal("max HP from the record", 30 + 1 * 20 / 5, _maxHpAfter, uw1 ? "uw1 SKILLS.C:106" : "uw2 SKILLS.C:78");
        k.Equal("max mana from the record", (0 + 1) * 20 >> 3, _maxManaAfter, uw1 ? "uw1 SKILLS.C:107, 111" : "uw2 SKILLS.C:79-80");
        k.Equal("mana filled", (0 + 1) * 20 >> 3, _manaAfter, uw1 ? "uw1 SKILLS.C:113-114" : "uw2 SKILLS.C:82-83");
        k.Equal("dungeon level", 1, _level, uw1 ? "uw1 MAINMENU.C:227" : "uw2 MAINMENU.C:200");
        k.Equal("start tile x", uw1 ? 0x20 : 0x13, _tileX, uw1 ? "uw1 MAINMENU.C:229" : "uw2 MAINMENU.C:203");
        k.Equal("start tile y", uw1 ? 2 : 0x30, _tileY, uw1 ? "uw1 MAINMENU.C:229" : "uw2 MAINMENU.C:203");
        k.Equal("experience level", 1, _xpLevel, uw1 ? "uw1 CHARGEN.C:95" : "uw2 CHARGEN.C:82");
        k.Equal("skill points", 1, _points, uw1 ? "uw1 CHARGEN.C:93" : "uw2 CHARGEN.C:80");
        k.Equal("skill points earned", 0, _pointsEarned, uw1 ? "uw1 CHARGEN.C:94" : "uw2 CHARGEN.C:81");
        // One physics frame has passed since the new game, so allow the first clock step (0x40).
        long clock0 = uw1 ? 0x10B3000 : 0x465000;
        k.Within("game clock", clock0, clock0 + 0x40, _clock, uw1 ? "uw1 CHARGEN.C:96" : "uw2 CHARGEN.C:84");
        k.Equal("hunger", 0xC0, _hunger, uw1 ? "uw1 CHARGEN.C:129" : "uw2 CHARGEN.C:117");
        k.Equal("detail level", 3, _detail, uw1 ? "uw1 CHARGEN.C:112" : "uw2 CHARGEN.C:99");
        if (uw1)
        {
            k.Equal("moonstone level", 2, _moonstone, "uw1 CHARGEN.C:97");
            k.Equal("talismans left (quest 36)", 8, _talismans, "uw1 CHARGEN.C:114");
            k.Equal("game variable 0x1A", 0x35, _var1A, "uw1 CHARGEN.C:128");
        }
        return k.Result;
    }
}

/// <summary>
/// The init_char fields that playerdat.InitEmptyPlayer sets differently from the original, read
/// from the same new game. Listed in KNOWN_FAILURES; each one a player can notice:
///
/// - Fatigue: UW1 starts at 0x40 (uw1 CHARGEN.C:120), UW2 at 0x30 (uw2 CHARGEN.C:105).
///   InitEmptyPlayer sets 0x30 in both (src/player/playerdatainit.cs:152), so a new UW1
///   character is less tired than DOS's, which changes how the first night's sleep restores
///   (fatigue / 2 + 2, uw1 SKILLS.C:586).
/// - food_heal: UW1 0x40 (uw1 CHARGEN.C:121), UW2 0x30 (uw2 CHARGEN.C:106). InitEmptyPlayer
///   sets foodhealthbonus to 0 (src/player/playerdatainit.cs:166). Eating heals food_heal / 8
///   HP in UW1 (uw1 SKILLS.C:654) and / 6 in UW2 (uw2 SKILLS.C:592), at most 8, so in DOS a
///   new character's first meal heals 8 HP; in the port it heals nothing until the bonus has
///   built up again (src/player/playerdatstatus.cs:676).
/// - UW2's moonstones: DOS puts them on levels 3 and 45 (uw2 CHARGEN.C:87-88). InitEmptyPlayer
///   calls SetMoonstone(1, 3) then SetMoonstone(1, 45) (src/player/playerdatainit.cs:212-213),
///   so moonstone 0 stays 0 and the one on level 3 is lost.
///
/// The same record fields are read as in new-game-start (after the runner's new game, before
/// anything acts); the stand-in stats do not touch them.
/// </summary>
public sealed class NewGameRecord : Scenario
{
    public override string Name => "new-game-record";
    public override string Cites => "underworld-exhumed uw1/src/game/CHARGEN.C:120-121; uw2/src/game/CHARGEN.C:87-88, 105-106";

    bool _inGame;
    int _fatigue, _foodHeal, _moon0, _moon1;

    public override Task Setup(ScenarioContext c)
    {
        _inGame = uimanager.InGame;
        _fatigue = playerdat.play_fatigue;
        _foodHeal = playerdat.foodhealthbonus;
        _moon0 = playerdat.GetMoonstone(0);
        _moon1 = playerdat.GetMoonstone(1);
        c.Note($"fatigue 0x{_fatigue:X}, food_heal 0x{_foodHeal:X}, moonstones {_moon0}, {_moon1}");
        return Task.CompletedTask;
    }

    public override Task Act(ScenarioContext c) => Task.CompletedTask;

    public override string Assert(ScenarioContext c)
    {
        if (!_inGame) return "precondition: the new game was not in game";
        bool uw1 = c.Game == "uw1";
        var k = new Checks();
        k.Equal("fatigue", uw1 ? 0x40 : 0x30, _fatigue, uw1 ? "uw1 CHARGEN.C:120" : "uw2 CHARGEN.C:105");
        k.Equal("food_heal", uw1 ? 0x40 : 0x30, _foodHeal, uw1 ? "uw1 CHARGEN.C:121" : "uw2 CHARGEN.C:106");
        if (!uw1)
        {
            k.Equal("moonstone 0 level", 3, _moon0, "uw2 CHARGEN.C:87");
            k.Equal("moonstone 1 level", 0x2D, _moon1, "uw2 CHARGEN.C:88");
        }
        return k.Result;
    }
}
