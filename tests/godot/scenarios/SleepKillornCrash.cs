using System.Threading.Tasks;
using Godot;

namespace Underworld.Scenarios;

/// <summary>
/// Sleeping through Killorn Keep's crash. In the original's player_sleep, once the first 2 to 6
/// hours have passed, the keep crashes and the player takes 255 damage if quest 50 is set and
/// quest 54 is not (quests[12] bit 2 and quests[13] bit 2), wherever the player sleeps
/// (uw2/src/game/SKILLS.C:467-471); a dead player's sleep ends there (SKILLS.C:484-488).
///
/// The scenario sleeps twice on level 1, using a bedroll as a player does (the port's use.Use,
/// then sleep.Sleep(1)): first with quests 50 and 54 both set, when the player must live, then
/// with 50 set and 54 clear, when the player must be dead as soon as the sleep returns.
///
/// The port once tested for world 2 (Killorn) and quest 54 set, the reverse
/// (src/World/sleep.cs:105-115), so a player who slept out the countdown anywhere else escaped the
/// crash and one who slept in Killorn after it was safe died.
/// </summary>
public sealed class SleepKillornCrash : Scenario
{
    const int BedrollItem = 0x121;
    const ulong SettleMs = 30000;

    public override string Name => "sleep-killorn-crash";
    public override string[] Games => new[] { "uw2" };
    public override string Cites => "underworld-exhumed uw2/src/game/SKILLS.C:463-488 (player_sleep)";

    string _problem;
    int _hpSafe, _hpCrash, _hpBefore, _level;
    bool _usedSafe, _usedCrash, _settled;

    bool Sleep()
    {
        int slot = ObjectCreator.PrepareNewObject(BedrollItem);
        var bedroll = slot != 0 ? UWTileMap.current_tilemap.LevelObjects[slot] : null;
        if (bedroll == null) return false;
        return use.Use(bedroll, playerdat.playerObject, UWTileMap.current_tilemap.LevelObjects, WorldObject: true);
    }

    public override Task Setup(ScenarioContext c)
    {
        _level = playerdat.dungeon_level;
        playerdat.play_hp = playerdat.max_hp;
        playerdat.play_hunger = 0xC0;
        playerdat.play_poison = 0;
        _hpBefore = playerdat.play_hp;
        return Task.CompletedTask;
    }

    public override async Task Act(ScenarioContext c)
    {
        playerdat.SetQuest(50, 1);
        playerdat.SetQuest(54, 1);
        playerdat.play_fatigue = 0x30;
        _usedSafe = Sleep();
        _hpSafe = playerdat.play_hp;

        ulong deadline = Time.GetTicksMsec() + SettleMs;
        int stable = 0;
        while (Time.GetTicksMsec() < deadline && stable < 30)
        {
            await c.Frames(1);
            stable = (uimanager.InGame && !cutsplayer.IsPlaying && !uimanager.blockinput) ? stable + 1 : 0;
        }
        _settled = stable >= 30;

        playerdat.SetQuest(54, 0);
        playerdat.play_hp = playerdat.max_hp;
        playerdat.play_fatigue = 0x30;
        _usedCrash = Sleep();
        _hpCrash = playerdat.play_hp;
        c.Note($"level {_level}: quests 50 and 54 set, hit points after sleeping {_hpSafe}; quest 50 set and 54 clear, hit points after sleeping {_hpCrash}");
        // Leave nothing that would kill the next scenario's player.
        playerdat.SetQuest(50, 0);
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        if (_level < 1 || _level > 8) return $"precondition: the player is on level {_level}, not in Britannia";
        if (!_usedSafe || !_usedCrash) return "precondition: using a bedroll did nothing";
        if (!_settled) return "precondition: the game did not come back to play after the first night";
        var k = new Checks();
        k.True(_hpSafe > 0, $"with quests 50 and 54 set the player expected alive after sleeping, got {_hpSafe} hit points (SKILLS.C:467, quest 54 set: no crash)");
        k.Equal("hit points after sleeping with quest 50 set and 54 clear (Killorn crashes)", 0, _hpCrash, "uw2 SKILLS.C:467-471, damage_item 0xFF");
        return k.Result;
    }
}
