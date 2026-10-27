using System.IO;
using System.Threading.Tasks;
using Godot;

namespace Underworld.Scenarios;

/// <summary>
/// Where player_compute puts the mana maximum. Both games compute (mana skill + 1) * Intelligence / 8.
///
/// UW1: on level 7 (Tybal's lair) the value goes to saved_mana, not max_mana (uw1/src/game/SKILLS.C:107-111),
/// because while the orb stands the level holds the maximum at nothing: arriving keeps the maximum aside in
/// saved_mana and sets the maximum and mana to 0, leaving puts the maximum back from saved_mana with a quarter
/// of it as mana (do_level_hacks, uw1/src/game/GAMEWRAP.C:459-469). A level gained there raises the maximum
/// the player gets back on leaving, not the one he has on the level.
///
/// UW2: player_compute ends with restore_mana(ThePlayer, 0) (uw2/src/game/SKILLS.C:84), which adds nothing
/// (an amount not above 0 adds -amount) and caps mana at the maximum (uw2/src/combat/SPELLS.C:155-170).
///
/// The port's player_compute is playerdat.RecalculateHPManaMaxWeight, made at character creation and at each
/// level gained. UW1: the scenario sets Intelligence 20 and mana skill 3 (maximum 10) on level 1, goes to
/// level 7 with Teleportation.Teleport, sets mana skill 7 (maximum 20) and recomputes, as a level gained
/// there does, then goes back to level 1. UW2: it fills mana (10), lowers Intelligence to 10 and recomputes (maximum 5), then recomputes again
/// with mana 4, below the maximum.
/// </summary>
public sealed class PlayerComputeMana : Scenario
{
    const int TybalLevel = 7, Int = 20, LowInt = 10, SkillBefore = 3, SkillAfter = 7;
    const ulong TeleportMs = 20000;

    public override string Name => "player-compute-mana";
    public override string Cites => "underworld-exhumed uw1/src/game/SKILLS.C:107-111, uw1/src/game/GAMEWRAP.C:459-469; uw2/src/game/SKILLS.C:84, uw2/src/combat/SPELLS.C:155-170";

    string _problem;
    int _x7, _y7, _x1, _y1, _level1;
    // UW1: after arriving on level 7, after the recompute there, after leaving.
    int _maxIn, _manaIn, _savedIn, _maxRe, _manaRe, _savedRe, _maxOut, _manaOut;
    // UW2: mana after a recompute from above and from below the maximum.
    int _max2, _fromAbove, _fromBelow;

    async Task<bool> Go(ScenarioContext c, int level, int x, int y)
    {
        Teleportation.Teleport(character: 0, tileX: x, tileY: y, newLevel: level, HeadingHeightFlag: 0);
        ulong deadline = Time.GetTicksMsec() + TeleportMs;
        int stable = 0;
        while (Time.GetTicksMsec() < deadline && stable < 10)
        {
            await c.Frames(1);
            stable = (playerdat.dungeon_level == level && !uimanager.WaitingForFade && uimanager.InGame) ? stable + 1 : 0;
        }
        return playerdat.dungeon_level == level;
    }

    public override Task Setup(ScenarioContext c)
    {
        playerdat.INT = Int;
        playerdat.ManaSkill = SkillBefore;
        playerdat.RecalculateHPManaMaxWeight(true);
        _level1 = playerdat.dungeon_level;
        _x1 = playerdat.playerObject.tileX; _y1 = playerdat.playerObject.tileY;
        if (playerdat.max_mana != (SkillBefore + 1) * Int >> 3) _problem = $"the maximum on level {_level1} is {playerdat.max_mana}";
        if (c.Game == "uw1")
        {
            if (playerdat.isOrbDestroyed) _problem = "the orb is already destroyed";
            if (!SleepInVoid.FindOpenSquare(Path.Combine(UWClass.BasePath, "DATA", "LEV.ARK"), TybalLevel, out _x7, out _y7)) _problem = "no open square on level 7 in LEV.ARK";
        }
        return Task.CompletedTask;
    }

    public override async Task Act(ScenarioContext c)
    {
        if (_problem != null) return;
        if (c.Game == "uw2")
        {
            // Full mana (10), then a recompute from a lower Intelligence: maximum 5, mana above it.
            int full = playerdat.play_mana;
            playerdat.INT = LowInt;
            playerdat.RecalculateHPManaMaxWeight(false);
            _max2 = playerdat.max_mana;
            _fromAbove = playerdat.play_mana;
            playerdat.play_mana = 4;
            playerdat.RecalculateHPManaMaxWeight(false);
            _fromBelow = playerdat.play_mana;
            c.Note($"mana {full}, Intelligence {Int} -> {LowInt}: maximum {_max2}, mana {_fromAbove}; mana 4 -> {_fromBelow}");
            return;
        }
        if (!await Go(c, TybalLevel, _x7, _y7)) { _problem = $"the teleport to level {TybalLevel} did not happen"; return; }
        _maxIn = playerdat.max_mana; _manaIn = playerdat.play_mana; _savedIn = playerdat.backup_mana;
        playerdat.ManaSkill = SkillAfter;
        playerdat.RecalculateHPManaMaxWeight(false);
        _maxRe = playerdat.max_mana; _manaRe = playerdat.play_mana; _savedRe = playerdat.backup_mana;
        if (!await Go(c, _level1, _x1, _y1)) { _problem = $"the teleport back to level {_level1} did not happen"; return; }
        _maxOut = playerdat.max_mana; _manaOut = playerdat.play_mana;
        c.Note($"on level 7: mana {_manaIn}/{_maxIn}, saved {_savedIn}; after the recompute {_manaRe}/{_maxRe}, saved {_savedRe}; back on level {_level1}: {_manaOut}/{_maxOut}");
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        var k = new Checks();
        if (c.Game == "uw2")
        {
            if (_max2 != (SkillBefore + 1) * LowInt >> 3) return $"precondition: the maximum from Intelligence {LowInt} is {_max2}";
            k.Equal("mana above the new maximum after a recompute", _max2, _fromAbove, "restore_mana(ThePlayer, 0) caps it: uw2 SKILLS.C:84, SPELLS.C:166-167");
            k.Equal("mana below the maximum after a recompute", 4, _fromBelow, "restore_mana with 0 adds nothing: uw2 SPELLS.C:162-165");
            return k.Result;
        }
        int before = (SkillBefore + 1) * Int >> 3, after = (SkillAfter + 1) * Int >> 3;
        if (_maxIn != 0 || _manaIn != 0 || _savedIn != before)
            return $"precondition: arriving on level 7 left mana {_manaIn}/{_maxIn}, saved {_savedIn} (expected 0/0, saved {before}: uw1 GAMEWRAP.C:462-464)";
        k.Equal("maximum on level 7 after a recompute", 0, _maxRe, "uw1 SKILLS.C:108-109 writes saved_mana there");
        k.Equal("saved maximum on level 7 after a recompute", after, _savedRe, "uw1 SKILLS.C:108-109");
        k.Equal("mana on level 7 after a recompute", 0, _manaRe, "uw1 SKILLS.C:113-114, no restore");
        k.Equal("maximum after leaving level 7", after, _maxOut, "uw1 GAMEWRAP.C:467");
        k.Equal("mana after leaving level 7", after >> 2, _manaOut, "uw1 GAMEWRAP.C:468");
        return k.Result;
    }
}
