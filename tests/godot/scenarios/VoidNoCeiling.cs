using System.Threading.Tasks;
using Godot;

namespace Underworld.Scenarios;

/// <summary>
/// UW2 draws no ceiling in the Ethereal Void. process_grid sets ciels to 0 on levels 65 to 72 other than 68 and to 1
/// everywhere else (uw2/src/3d/GRIDDB.C:237-249), and the tile routine draws a tile's ceiling only while ciels is set
/// (uw2 GRIDDB.C:580). UW1 has no such case.
///
/// The port draws the level's ceiling as one mesh, named CEILING_..., under the tilemap node on every level
/// (src/World/tilemaprender.cs GenerateLevelFromTileMap). Headless Godot draws nothing, so the scenario reads the
/// node: on a level with a ceiling it must be visible, in the Void (65, 66 and 69) it must not be, and on 68 it must be.
/// </summary>
public sealed class VoidNoCeiling : Scenario
{
    public override string Name => "void-no-ceiling";
    public override string[] Games => new[] { "uw2" };
    public override string Cites => "underworld-exhumed uw2/src/3d/GRIDDB.C:237-249, 580";

    static readonly int[] Levels = { 1, 65, 66, 68, 69 };
    static readonly bool[] CeilingExpected = { true, false, false, true, false };

    int _level0;
    readonly System.Collections.Generic.List<int> _reached = new();
    readonly System.Collections.Generic.List<bool?> _visible = new();

    static void MoveTo(int level)
    {
        Teleportation.CodeToRunOnTeleport = () => { };
        Teleportation.Teleport(0, 0x3F, 0x3F, level, 0);
        Teleportation.HandleTeleportation();
    }

    /// <summary>Whether the level's ceiling mesh is visible; null when there is none.</summary>
    static bool? CeilingVisible()
    {
        var tiles = main.instance.GetNode<Node3D>("../tilemap");
        bool? found = null;
        foreach (var child in tiles.GetChildren())
        {
            if (child.IsQueuedForDeletion() || !child.Name.ToString().StartsWith("CEILING_")) continue;
            if (child is Node3D n) found = (found ?? false) || n.Visible;
        }
        return found;
    }

    public override Task Setup(ScenarioContext c)
    {
        _level0 = playerdat.dungeon_level;
        return Task.CompletedTask;
    }

    public override async Task Act(ScenarioContext c)
    {
        await c.Frames(2);
        foreach (int level in Levels)
        {
            if (playerdat.dungeon_level != level) MoveTo(level);
            await c.Frames(2);
            _reached.Add(playerdat.dungeon_level);
            _visible.Add(CeilingVisible());
            c.Note($"level {playerdat.dungeon_level}: ceiling visible {(_visible[^1]?.ToString() ?? "none")}");
        }
        if (playerdat.dungeon_level != _level0) MoveTo(_level0);
        await c.Frames(2);
    }

    public override string Assert(ScenarioContext c)
    {
        var k = new Checks();
        for (int i = 0; i < Levels.Length; i++)
        {
            if (_reached[i] != Levels[i]) return $"precondition: the move to level {Levels[i]} left the player on level {_reached[i]}";
            if (_visible[i] == null) return $"precondition: level {Levels[i]} has no ceiling mesh";
            k.True(_visible[i] == CeilingExpected[i], $"level {Levels[i]}: expected the ceiling {(CeilingExpected[i] ? "drawn" : "hidden")}, was {(_visible[i] == true ? "drawn" : "hidden")} (ciels: uw2 GRIDDB.C:237-249, 580)");
        }
        return k.Result;
    }
}
