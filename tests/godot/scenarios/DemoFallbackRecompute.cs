using System.Reflection;
using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// The demo start with no DPLAYER file (main.LaunchUWDemo's fallback, main.cs) sets stand-in
/// attributes. Whenever the original sets the attributes of a new character it then calls
/// player_compute(1) (uw1/src/game/CHARGEN.C:137, 259), which derives the maximum hit points
/// (30 + level * Strength / 5), the maximum mana ((mana skill + 1) * Intelligence >> 3), the carrying
/// limit, and fills the mana (uw1/src/game/SKILLS.C:102-116). Without it the carrying limit stays 0
/// and the mana empty.
///
/// The scenario calls main.LaunchUWDemo (by reflection; the full game has no DPLAYER file, so it takes
/// the fallback) and reads the derived values straight after. Hit points and mana are checked against
/// the original's formulas. The carrying limit is checked against the port's own recompute
/// (playerdat.RecalculateHPManaMaxWeight), because the port's formula is UW2's in both games, a separate
/// known gap (carry-capacity in KNOWN_FAILURES).
///
/// UW1 only: the demo is UW1's.
/// </summary>
public sealed class DemoFallbackRecompute : Scenario
{
    public override string Name => "demo-fallback-recompute";
    public override string[] Games => new[] { "uw1" };
    public override string Cites => "underworld-exhumed uw1/src/game/SKILLS.C:102-116 (player_compute), uw1/src/game/CHARGEN.C:137, 259 (player_compute(1) after the attributes)";

    string _problem;
    bool _inGame;
    int _str, _int, _manaSkill, _level, _maxHp, _maxMana, _mana, _weightMax, _portWeightMax;

    public override Task Setup(ScenarioContext c)
    {
        if (System.IO.Directory.GetFiles(System.IO.Path.Combine(UWClass.BasePath, "DATA"), "DPLAYER.*").Length > 0)
            _problem = "the game folder has a DPLAYER file, so LaunchUWDemo does not take the fallback";
        return Task.CompletedTask;
    }

    public override async Task Act(ScenarioContext c)
    {
        if (_problem != null) return;
        var launch = typeof(global::main).GetMethod("LaunchUWDemo", BindingFlags.NonPublic | BindingFlags.Static);
        if (launch == null) { _problem = "main.LaunchUWDemo not found"; return; }
        launch.Invoke(null, null);
        _str = playerdat.STR; _int = playerdat.INT; _manaSkill = playerdat.ManaSkill; _level = playerdat.play_level;
        _maxHp = playerdat.max_hp; _maxMana = playerdat.max_mana; _mana = playerdat.play_mana; _weightMax = playerdat.WeightMax;
        playerdat.RecalculateHPManaMaxWeight(false);
        _portWeightMax = playerdat.WeightMax;
        await c.Frames(1);
        _inGame = uimanager.InGame;
        c.Note($"STR {_str} INT {_int} mana skill {_manaSkill} level {_level}: max HP {_maxHp}, mana {_mana}/{_maxMana}, carrying limit {_weightMax} (port's recompute {_portWeightMax})");
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        if (!_inGame) return "precondition: the demo start is not in game";
        if (_str == 0 || _int == 0) return "precondition: the fallback set no attributes";
        var k = new Checks();
        k.Equal("maximum hit points", 30 + _level * _str / 5, _maxHp, "uw1 SKILLS.C:106");
        k.Equal("maximum mana", (_manaSkill + 1) * _int >> 3, _maxMana, "uw1 SKILLS.C:107-111");
        k.Equal("mana", _maxMana, _mana, "uw1 SKILLS.C:113-114, filled by player_compute(1)");
        k.Equal("carrying limit (the port's recompute)", _portWeightMax, _weightMax, "uw1 SKILLS.C:112, set by player_compute");
        return k.Result;
    }
}
