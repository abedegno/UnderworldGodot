using System.Threading.Tasks;
using Godot;

namespace Underworld.Scenarios;

/// <summary>
/// Entering a level frees the critters' stored paths. init_level_creature_stuff
/// (uw1/src/critter/CRITTIME.C:401-407, uw2/src/critter/CRITTIME.C:458-463), run on entering a
/// level, calls clear_paths (uw1/src/critter/PATHFIND.C:945-954, uw2/src/critter/CRITTIME.C:445-456),
/// which clears every critter's has-a-path bit and sets freepaths, the mask of the 16 stored
/// paths that are free, back to 0xFFFF.
///
/// The scenario marks twelve of the 16 paths in use in the port's mask (npc's
/// BitFieldForPathing_dseg_67d6_B4, as paths held by critters when the player leaves), changes level
/// with Teleportation.Teleport and comes back, and checks after each arrival that the free paths are the 16 less those the level's critters have taken since.
///
/// Both games. The port clears the critters' bits on entering a level
/// (src/utility/teleportation.cs:567) but sets the mask to 0xFFFF only at start-up
/// (src/npc/npcai.cs:185), so each path held at a level change is lost for good.
/// </summary>
public sealed class PathsFreedOnLevelChange : Scenario
{
    public override string Name => "paths-freed-on-level-change";
    public override string Cites => "underworld-exhumed uw1/src/critter/PATHFIND.C:945-954, uw1/src/critter/CRITTIME.C:401-407; uw2/src/critter/CRITTIME.C:445-463";

    const string Mask = "BitFieldForPathing_dseg_67d6_B4";
    const ulong TeleportMs = 20000;
    string _problem;
    int _start, _maskSet, _maskAway, _maskBack, _heldAway, _heldBack;

    /// <summary>Critters on the level holding a stored path (b15 bit 7, the port's UnkBit_0X15_Bit7).</summary>
    static int Held()
    {
        int n = 0;
        var map = UWTileMap.current_tilemap;
        for (int i = 0; i < map.NoOfActiveMobiles; i++)
        {
            var o = map.LevelObjects[map.GetActiveMobileAtIndex(i)];
            if (o != null && o.index > 1 && o.majorclass == 1 && o.UnkBit_0X15_Bit7 == 1) n++;
        }
        return n;
    }

    static int Free(int mask) => System.Numerics.BitOperations.PopCount((uint)(mask & 0xFFFF));

    async Task<bool> Go(ScenarioContext c, int level)
    {
        // Any square will do. A stand-in square (0x3F) with a callback queued parks the player in
        // the middle of the map without a search for room, so he cannot be killed by arriving in rock
        // (new_player_pos, uw1/src/game/UWEDIT.C:466-471, uw2/src/game/UWEDIT.C:480-486).
        Teleportation.CodeToRunOnTeleport = () => { };
        Teleportation.Teleport(character: 0, tileX: 0x3F, tileY: 0x3F, newLevel: level, HeadingHeightFlag: 0);
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
        _start = playerdat.dungeon_level;
        BatchA.Set(typeof(npc), Mask, 0x000F);   // paths 4..15 held
        _maskSet = BatchA.Get<int>(typeof(npc), Mask);
        if (_maskSet != 0x000F) _problem = "the free-path mask did not read back 0x000F";
        return Task.CompletedTask;
    }

    public override async Task Act(ScenarioContext c)
    {
        if (_problem != null) return;
        if (!await Go(c, _start + 1)) { _problem = $"the teleport to level {_start + 1} did not happen"; return; }
        _maskAway = BatchA.Get<int>(typeof(npc), Mask);
        _heldAway = Held();
        BatchA.Set(typeof(npc), Mask, 0x000F);
        if (!await Go(c, _start)) { _problem = $"the teleport back to level {_start} did not happen"; return; }
        _maskBack = BatchA.Get<int>(typeof(npc), Mask);
        _heldBack = Held();
        c.Note($"free-path mask 0x{_maskSet:X4}; after entering level {_start + 1} 0x{_maskAway:X4} ({_heldAway} critters holding a path since); set 0x000F, after entering level {_start} 0x{_maskBack:X4} ({_heldBack} holding)");
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        var k = new Checks();
        string cite = c.Game == "uw1" ? "uw1 PATHFIND.C:953, CRITTIME.C:406" : "uw2 CRITTIME.C:455, 462";
        // All 16 freed on arrival; any taken since by the level's own critters are held by them.
        k.Equal($"free paths after entering level {_start + 1} (16 less the {_heldAway} held since)", 16 - _heldAway, Free(_maskAway), cite);
        k.Equal($"free paths after entering level {_start} (16 less the {_heldBack} held since)", 16 - _heldBack, Free(_maskBack), cite);
        return k.Result;
    }
}
