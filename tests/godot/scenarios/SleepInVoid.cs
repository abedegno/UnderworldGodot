using System.IO;
using System.Threading.Tasks;
using Godot;

namespace Underworld.Scenarios;

/// <summary>
/// No sleeping in the Ethereal Void. The original's player_sleep refuses a chosen sleep on level 9
/// (PlayerLevel == LEVEL_VOID), with "You can't go to sleep here!" (string 0x14), before anything
/// else happens: no time passes and fatigue is untouched (uw1/src/game/SKILLS.C:523-528).
///
/// The scenario finds an open square on level 9 in the shipped DATA\LEV.ARK, goes there through the
/// game's Teleportation.Teleport, sets fatigue 0x30, and uses a bedroll as a player does (the port's
/// use.Use, then sleep.Sleep(1)). Fatigue must still be 0x30 and the clock must not have jumped.
///
/// UW1 only. The port once had no level 9 test (src/World/sleep.cs:25), so the player slept in the Void.
/// </summary>
public sealed class SleepInVoid : Scenario
{
    const int BedrollItem = 0x121, VoidLevel = 9, Fatigue = 0x30;
    const long Hour = 0xE1000;
    const ulong TeleportMs = 20000;

    public override string Name => "sleep-in-void";
    public override string[] Games => new[] { "uw1" };
    public override string Cites => "underworld-exhumed uw1/src/game/SKILLS.C:523-528 (player_sleep)";

    string _problem;
    int _x, _y, _fatigueAfter, _clockBefore, _clockAfter;
    bool _used;

    public override async Task Setup(ScenarioContext c)
    {
        if (!FindOpenSquare(Path.Combine(UWClass.BasePath, "DATA", "LEV.ARK"), VoidLevel, out _x, out _y)) { _problem = "no open square on level 9 in LEV.ARK"; return; }
        Teleportation.Teleport(character: 0, tileX: _x, tileY: _y, newLevel: VoidLevel, HeadingHeightFlag: 0);
        ulong deadline = Time.GetTicksMsec() + TeleportMs;
        int stable = 0;
        while (Time.GetTicksMsec() < deadline && stable < 10)
        {
            await c.Frames(1);
            stable = (playerdat.dungeon_level == VoidLevel && !uimanager.WaitingForFade && uimanager.InGame) ? stable + 1 : 0;
        }
        if (playerdat.dungeon_level != VoidLevel) _problem = $"the teleport to level 9 did not happen (on level {playerdat.dungeon_level})";
    }

    public override Task Act(ScenarioContext c)
    {
        if (_problem != null) return Task.CompletedTask;
        playerdat.play_fatigue = Fatigue;
        _clockBefore = playerdat.ClockValue;
        int slot = ObjectCreator.PrepareNewObject(BedrollItem);
        var bedroll = slot != 0 ? UWTileMap.current_tilemap.LevelObjects[slot] : null;
        if (bedroll == null) { _problem = "no bedroll could be made"; return Task.CompletedTask; }
        _used = use.Use(bedroll, playerdat.playerObject, UWTileMap.current_tilemap.LevelObjects, WorldObject: true);
        _fatigueAfter = playerdat.play_fatigue;
        _clockAfter = playerdat.ClockValue;
        c.Note($"on level {playerdat.dungeon_level} at ({_x}, {_y}): fatigue 0x{Fatigue:X} -> 0x{_fatigueAfter:X}, clock rose {_clockAfter - _clockBefore}");
        return Task.CompletedTask;
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        if (!_used) return "precondition: using the bedroll did nothing at all";
        var k = new Checks();
        k.Equal("fatigue after trying to sleep in the Void (refused)", Fatigue, _fatigueAfter, "uw1 SKILLS.C:525-528");
        k.True((long)_clockAfter - _clockBefore < Hour, $"clock after trying to sleep in the Void expected unchanged, rose {_clockAfter - _clockBefore} (uw1 SKILLS.C:525-528)");
        return k.Result;
    }

    /// <summary>The first open square (tile type 1) away from the edge on a level in UW1's LEV.ARK (64x64 tiles of 4 bytes).</summary>
    static bool FindOpenSquare(string path, int level, out int x, out int y)
    {
        x = y = 0;
        if (!File.Exists(path)) return false;
        byte[] d = File.ReadAllBytes(path);
        uint U32(int o) => (uint)(d[o] | d[o + 1] << 8 | d[o + 2] << 16 | d[o + 3] << 24);
        int b = (int)U32(2 + 4 * (level - 1));
        if (b == 0) return false;
        for (int ty = 8; ty < 56; ty++)
            for (int tx = 8; tx < 56; tx++)
                if ((U32(b + (ty * 64 + tx) * 4) & 0xF) == 1) { x = tx; y = ty; return true; }
        return false;
    }
}
