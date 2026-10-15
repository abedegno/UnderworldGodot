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
/// The scenario gives twelve of the start level's critters a stored path each, as the port's path
/// finder does when it takes one (src/npc/npcai.cs:1693-1711: the first free slot from
/// FindSetIndexOfBitField, its bit cleared in the mask npc.BitFieldForPathing_dseg_67d6_B4, the
/// critter's has-a-path bit and slot set), so that the player leaves with those critters in mid-path.
/// It then changes level with Teleportation.Teleport and comes back, without touching the mask, and
/// checks after each arrival that the free paths are the 16 less those the level's critters have
/// taken since, and that on the way back no slot is marked in use that no critter holds.
///
/// Both games. The port cleared the critters' bits on entering a level but set the mask to 0xFFFF
/// only at start-up (src/npc/npcai.cs:185), so each path held at a level change was lost for good.
/// </summary>
public sealed class PathsFreedOnLevelChange : Scenario
{
    public override string Name => "paths-freed-on-level-change";
    public override string Cites => "underworld-exhumed uw1/src/critter/PATHFIND.C:945-954, uw1/src/critter/CRITTIME.C:401-407; uw2/src/critter/CRITTIME.C:445-463";

    const string Mask = "BitFieldForPathing_dseg_67d6_B4";
    const ulong TeleportMs = 20000;
    string _problem;
    int _start, _given, _maskSet, _maskAway, _maskBack, _heldAway, _heldBack, _orphans;

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
        var p = playerdat.playerObject;
        Teleportation.Teleport(character: 0, tileX: p.tileX, tileY: p.tileY, newLevel: level, HeadingHeightFlag: 0);
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
        var map = UWTileMap.current_tilemap;
        for (int i = 0; i < map.NoOfActiveMobiles && _given < 12; i++)
        {
            var o = map.LevelObjects[map.GetActiveMobileAtIndex(i)];
            if (o == null || o.index <= 1 || o.majorclass != 1 || o.UnkBit_0X15_Bit7 == 1) continue;
            int slot = 0;
            object[] args = { slot };
            if (!(bool)BatchA.Call(typeof(npc), "FindSetIndexOfBitField", args)) break;
            slot = (int)args[0];
            BatchA.Set(typeof(npc), Mask, BatchA.Get<int>(typeof(npc), Mask) & ~(1 << slot));
            o.UnkBit_0X15_Bit7 = 1;
            o.PathFindIndex_0x16_0_F = (short)slot;
            _given++;
        }
        _maskSet = BatchA.Get<int>(typeof(npc), Mask);
        if (_given < 4) _problem = $"only {_given} critters on level {_start} to give paths to";
        else if (Free(_maskSet) != 16 - _given) _problem = $"the free-path mask 0x{_maskSet:X4} does not show {_given} paths taken";
        return Task.CompletedTask;
    }

    /// <summary>Slots marked in use in the mask that no critter on the level holds.</summary>
    static int Orphans(int mask)
    {
        int held = 0;
        var map = UWTileMap.current_tilemap;
        for (int i = 2; i < 256; i++)
        {
            var o = map.LevelObjects[i];
            if (o != null && o.UnkBit_0X15_Bit7 == 1) held |= 1 << o.PathFindIndex_0x16_0_F;
        }
        return System.Numerics.BitOperations.PopCount((uint)(~mask & 0xFFFF & ~held));
    }

    public override async Task Act(ScenarioContext c)
    {
        if (_problem != null) return;
        if (!await Go(c, _start + 1)) { _problem = $"the teleport to level {_start + 1} did not happen"; return; }
        _maskAway = BatchA.Get<int>(typeof(npc), Mask);
        _heldAway = Held();
        if (!await Go(c, _start)) { _problem = $"the teleport back to level {_start} did not happen"; return; }
        _maskBack = BatchA.Get<int>(typeof(npc), Mask);
        _heldBack = Held();
        _orphans = Orphans(_maskBack);
        c.Note($"{_given} critters given paths, free-path mask 0x{_maskSet:X4}; after entering level {_start + 1} 0x{_maskAway:X4} ({_heldAway} critters holding a path since); after entering level {_start} again 0x{_maskBack:X4} ({_heldBack} holding, {_orphans} slots in use that no critter holds)");
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        var k = new Checks();
        string cite = c.Game == "uw1" ? "uw1 PATHFIND.C:953, CRITTIME.C:406" : "uw2 CRITTIME.C:455, 462";
        // All 16 freed on arrival; any taken since by the level's own critters are held by them.
        k.Equal($"free paths after entering level {_start + 1} (16 less the {_heldAway} held since)", 16 - _heldAway, Free(_maskAway), cite);
        k.Equal($"free paths after entering level {_start} (16 less the {_heldBack} held since)", 16 - _heldBack, Free(_maskBack), cite);
        k.Equal($"path slots in use that no critter holds, back on level {_start}", 0, _orphans, cite);
        return k.Result;
    }
}
