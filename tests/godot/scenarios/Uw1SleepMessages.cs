using System.Threading.Tasks;
using Godot;

namespace Underworld.Scenarios;

/// <summary>
/// UW1's sleep: what an interrupted sleep costs, and the message a full night ends with.
///
/// In UW1's player_sleep (uw1/src/game/SKILLS.C:516-610) an interrupted sleep
/// (wandering_monster_check) takes 0x20 off fatigue, or leaves 0 (uw1 SKILLS.C:563-567), prints
/// string 0x15, costs 12 to 27 hunger, player_eat(-12 - (rand() &amp; 0xF)), and takes 16 off
/// drunkenness (uw1 SKILLS.C:569-574). UW2 takes 0x18 (uw2/src/game/SKILLS.C:492-493). A full
/// night ends with game_sprint(0x13 - comfort) after dream() whether or not a dream played
/// (uw1 SKILLS.C:606-608), comfort being 1 when hunger is above 0x40 and the player sleeps in a
/// bed or bedroll (uw1 SKILLS.C:585); UW1's dream() prints nothing (uw1 SKILLS.C:452-484).
///
/// The scenario sleeps a full night on a bedroll the way a player does (use.Use, then
/// sleep.Sleep(1)) with hunger 0xC0, so comfort is 1, waits for any dream to finish, and reads the
/// message scroll: it must hold string 0x12 once and not string 0x13. Then it sets fatigue 0x30,
/// hunger 0xC0 and drunkenness 0x30 and runs the port's interrupted-sleep step
/// (sleep.SleepInterupted, private, called by name) and expects fatigue 0x10.
/// Precondition: the night ran to its end (fatigue 0).
///
/// The port took 0x18 off fatigue for both games (src/World/sleep.cs SleepInterupted).
/// </summary>
public sealed class Uw1SleepMessages : Scenario
{
    const int BedrollItem = 0x121, Fatigue = 0x30, Hunger = 0xC0, Drunk = 0x30;
    const ulong SettleMs = 30000;

    public override string Name => "uw1-sleep-messages";
    public override string[] Games => new[] { "uw1" };
    public override string Cites => "underworld-exhumed uw1/src/game/SKILLS.C:452-484, 563-574, 585, 606-608";

    string _problem;
    bool _used, _settled;
    int _fatigueAfterNight, _restedCount, _uneasyCount;
    int _fatigueAfterInterrupt, _hungerAfterInterrupt, _drunkAfterInterrupt, _interruptedCount;

    public override Task Setup(ScenarioContext c)
    {
        playerdat.play_fatigue = Fatigue;
        playerdat.play_hunger = Hunger;
        playerdat.play_poison = 0;
        if (playerdat.play_hp < 10) playerdat.play_hp = 10;
        return Task.CompletedTask;
    }

    static int CountOnScroll(int stringNo)
    {
        string text = GameStrings.GetString(1, stringNo).Trim();
        int n = 0;
        foreach (var line in uimanager.instance.scroll.Lines)
            if (line != null && text != "" && line.LineText.Contains(text)) n++;
        return n;
    }

    public override async Task Act(ScenarioContext c)
    {
        int slot = ObjectCreator.PrepareNewObject(BedrollItem);
        var bedroll = slot != 0 ? UWTileMap.current_tilemap.LevelObjects[slot] : null;
        if (bedroll == null || bedroll.item_id != BedrollItem) { _problem = "no bedroll could be made"; return; }
        uimanager.instance.scroll.Clear();
        _used = use.Use(bedroll, playerdat.playerObject, UWTileMap.current_tilemap.LevelObjects, WorldObject: true);
        _fatigueAfterNight = playerdat.play_fatigue;

        ulong deadline = Time.GetTicksMsec() + SettleMs;
        int stable = 0;
        while (Time.GetTicksMsec() < deadline)
        {
            await c.Frames(1);
            stable = (uimanager.InGame && !cutsplayer.IsPlaying && !uimanager.blockinput) ? stable + 1 : 0;
            if (stable >= 30) { _settled = true; break; }
        }
        _restedCount = CountOnScroll(0x12);
        _uneasyCount = CountOnScroll(0x13);

        // The interrupted sleep.
        uimanager.instance.scroll.Clear();
        playerdat.play_fatigue = Fatigue;
        playerdat.play_hunger = Hunger;
        playerdat.intoxication = Drunk;
        BatchA.Call(typeof(sleep), "SleepInterupted");
        _fatigueAfterInterrupt = playerdat.play_fatigue;
        _hungerAfterInterrupt = playerdat.play_hunger;
        _drunkAfterInterrupt = playerdat.intoxication;
        await c.Frames(5);
        _interruptedCount = CountOnScroll(0x15);
        c.Note($"night: fatigue 0x{Fatigue:X} -> 0x{_fatigueAfterNight:X}, string 0x12 x{_restedCount}, 0x13 x{_uneasyCount}; interrupted: fatigue 0x{Fatigue:X} -> 0x{_fatigueAfterInterrupt:X}, hunger 0x{Hunger:X} -> 0x{_hungerAfterInterrupt:X}, drunkenness 0x{Drunk:X} -> 0x{_drunkAfterInterrupt:X}");
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        if (!_used) return "precondition: using the bedroll did nothing";
        if (!_settled) return $"precondition: the game did not come back to play within {SettleMs / 1000} s of the sleep";
        if (_fatigueAfterNight != 0) return $"precondition: the sleep did not run to its end (fatigue 0x{_fatigueAfterNight:X}, not 0)";
        var k = new Checks();
        k.Equal("fatigue after an interrupted sleep (0x20 less)", Fatigue - 0x20, _fatigueAfterInterrupt, "uw1 SKILLS.C:563-567");
        k.Within("hunger after an interrupted sleep (12..27 less)", Hunger - 27, Hunger - 12, _hungerAfterInterrupt, "uw1 SKILLS.C:570");
        k.Equal("drunkenness after an interrupted sleep (16 less)", Drunk - 0x10, _drunkAfterInterrupt, "uw1 SKILLS.C:571-574");
        k.Equal("string 0x15 on the scroll after an interrupted sleep", 1, _interruptedCount, "uw1 SKILLS.C:569");
        k.Equal("string 0x12 (comfort 1) on the scroll after a full night", 1, _restedCount, "uw1 SKILLS.C:585, 606-608");
        k.Equal("string 0x13 (comfort 0) on the scroll after a comfortable night", 0, _uneasyCount, "uw1 SKILLS.C:585, 608");
        return k.Result;
    }
}
