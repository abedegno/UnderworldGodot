using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// The Void is drawn without lighting. The original's process_grid sets the shade mode to 0 and lighton to 0
/// for the frame on the UW1 Void, level 9 (uw1/src/3d/GRIDDB.C:242-251), and on UW2's levels 65 to 72 other
/// than 68 (uw2/src/3d/GRIDDB.C:238-249). The draw routines take the light table at shade * lighton
/// (uw1 GRIDDB.C:380, uw2 GRIDDB.C:381), so every distance takes light table 0: no distance shading.
///
/// Headless Godot draws nothing and cannot read global shader parameters back, so the scenario reads the values the
/// port hands its shaders: the palette by distance UpdateShaderCycleParams sets as smoothpalette (which the world and
/// sprite shaders look up), the row of shade.DrawnLightLevel, and shade.DrawnSimpleShade, which RefreshLighting sets as
/// simpleshade (the shade by distance of the object info layer). On a lit level the far rows of smoothpalette are darker than the
/// near ones; where the original turns lighting off every row must be the near row and simpleshade all 0.
///
/// The port had no such case and drew the Void with the player's light level (src/player/playerdatloop.cs:795-799,
/// src/loaders/paletteloader.cs UpdateShaderCycleParams).
/// </summary>
public sealed class VoidUnlit : Scenario
{
    public override string Name => "void-unlit";
    public override string Cites => "underworld-exhumed uw1/src/3d/GRIDDB.C:242-251, 380; uw2/src/3d/GRIDDB.C:238-249, 381";

    int _level0;
    int[] _levels;
    bool[] _unlitExpected;
    readonly System.Collections.Generic.List<string> _results = new();
    readonly System.Collections.Generic.List<bool?> _flat = new(), _simpleDark = new();
    readonly System.Collections.Generic.List<int> _reached = new();

    static void MoveTo(int level)
    {
        Teleportation.CodeToRunOnTeleport = () => { };
        Teleportation.Teleport(0, 0x3F, 0x3F, level, 0);
        Teleportation.HandleTeleportation();
    }

    /// <summary>
    /// True when the last row of the palette by distance UpdateShaderCycleParams hands the shaders as smoothpalette
    /// is its first row (no distance shading).
    /// </summary>
    static bool? Flat()
    {
        var tex = PaletteLoader.Palettes[Palette.CurrentPalette].cycledGamePalette[Palette.ColourTone, shade.DrawnLightLevel, 0];
        var img = tex?.GetImage();
        if (img == null) return null;
        int last = img.GetHeight() - 1;
        for (int x = 0; x < img.GetWidth(); x++)
            if (img.GetPixel(x, 0) != img.GetPixel(x, last)) return false;
        return true;
    }

    /// <summary>True when the simpleshade RefreshLighting hands the shaders marks some distance as shaded.</summary>
    static bool? SimpleDark()
    {
        var img = shade.DrawnSimpleShade?.GetImage();
        if (img == null) return null;
        for (int x = 0; x < img.GetWidth(); x++)
            if (img.GetPixel(x, 0).R8 != 0) return true;
        return false;
    }

    public override Task Setup(ScenarioContext c)
    {
        _level0 = playerdat.dungeon_level;
        if (c.Game == "uw1") { _levels = new[] { 1, 9 }; _unlitExpected = new[] { false, true }; }
        else { _levels = new[] { 1, 65, 68 }; _unlitExpected = new[] { false, true, false }; }
        return Task.CompletedTask;
    }

    public override async Task Act(ScenarioContext c)
    {
        await c.Frames(2);
        foreach (int level in _levels)
        {
            if (playerdat.dungeon_level != level) MoveTo(level);
            _reached.Add(playerdat.dungeon_level);
            await c.Frames(4);
            _flat.Add(Flat());
            _simpleDark.Add(SimpleDark());
            c.Note($"level {playerdat.dungeon_level}, light level {playerdat.lightlevel}: smoothpalette flat {_flat[^1]}, simpleshade has shade {_simpleDark[^1]}");
        }
        if (playerdat.dungeon_level != _level0) MoveTo(_level0);
        await c.Frames(2);
    }

    public override string Assert(ScenarioContext c)
    {
        var k = new Checks();
        string cite = c.Game == "uw1" ? "process_grid: uw1 GRIDDB.C:242-251" : "process_grid: uw2 GRIDDB.C:238-249";
        for (int i = 0; i < _levels.Length; i++)
        {
            if (_reached[i] != _levels[i]) return $"precondition: the move to level {_levels[i]} left the player on level {_reached[i]}";
            if (_flat[i] == null || _simpleDark[i] == null) return $"precondition: the shader parameters could not be read on level {_levels[i]}";
            if (!_unlitExpected[i] && _flat[i] == true) return $"precondition: level {_levels[i]} shows no distance shading, so the check cannot tell lit from unlit";
            if (_unlitExpected[i])
            {
                k.True(_flat[i] == true, $"level {_levels[i]}: expected the palette without distance shading, was shaded ({cite})");
                k.True(_simpleDark[i] == false, $"level {_levels[i]}: expected simpleshade without shade, was shaded ({cite})");
            }
        }
        return k.Result;
    }
}
