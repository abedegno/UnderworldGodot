using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace Underworld.Scenarios;

/// <summary>
/// Palette cycling works on the one palette in memory. cycle_colors rotates its banks of the palette and sends them
/// to the DAC (uw1 gfx/COLCYCLE.C:53-71, uw2 gfx/COLCYCLE.C:49-67); the light tables map a colour index to an index
/// (uw1 map/LIGHTING.C:6-8), so whatever the light level the cycled colours are the same at a given moment. The
/// palette the game loaded is the start of the cycle. Each step rotates four banks of 4 colours one place down and a
/// run of 5 and a run of 3 one place up (rotate_bank, uw1 COLCYCLE.C:27-46): UW1 30h..3Fh and 10h..14h, 15h..17h
/// (uw1 COLCYCLE.C:60-66), UW2 E0h..EFh and 3..7, 8..10 (uw2 COLCYCLE.C:56-62), so the cycle repeats after 60 steps.
/// It steps when the time byte's top two bits change, every 64 ticks of the 256 Hz clock (uw1 COLCYCLE.C:55-59,
/// SOUND.C:167-180).
///
/// The port prebuilt the cycle as textures (src/loaders/paletteloader.cs). Its working palette took the loaded
/// palette's arrays by reference, so building the cycles rotated the loaded palette itself; each light level carried
/// on 28 steps after the one before; it rotated 30h..3Fh (E0h..EFh) as one run of 16 and 10h..17h (3..9) as one run;
/// its table of 28 steps did not loop; and it stepped every 0.2 s.
///
/// Measured on the C# data: every loaded palette against DATA\PALS.DAT; and, with the cycle started at step 55 and
/// the game run for 4 s, at each step the clock reaches, the first row of the texture handed to the shaders as
/// smoothpalette (for the light level drawn and for one other) and the interface palette, against PALS.DAT's
/// palette 0 rotated by the original's rule and mapped through the light table of that row; and the steps taken.
/// </summary>
public sealed class PaletteCycles : Scenario
{
    public override string Name => "palette-cycles";
    public override string Cites => "underworld-exhumed uw1/src/gfx/COLCYCLE.C:27-71, uw2/src/gfx/COLCYCLE.C:23-67, uw1/src/map/LIGHTING.C:6-8, uw1/src/sound/SOUND.C:167-180";

    const int StartStep = 55;

    sealed class Capture
    {
        public int Step, Level, OtherLevel;
        public Image Shaded, OtherShaded, Ui;
    }

    readonly List<Capture> _captures = new();
    int _stepsIn4s = -1;
    bool _wrapped;

    public override Task Setup(ScenarioContext c) => Task.CompletedTask;

    static Capture Take()
    {
        var cap = new Capture { Step = PaletteLoader.NextPaletteCycle_GAME, Level = shade.DrawnLightLevel };
        cap.Shaded = PaletteLoader.ShadedPalette(Palette.CurrentPalette, Palette.ColourTone, cap.Level, cap.Step).GetImage();
        // the same step at another light level
        int keep = playerdat.lightlevel;
        playerdat.lightlevel = (keep == 7) ? 4 : 7;
        cap.OtherLevel = shade.DrawnLightLevel;
        cap.OtherShaded = PaletteLoader.ShadedPalette(Palette.CurrentPalette, Palette.ColourTone, cap.OtherLevel, cap.Step).GetImage();
        playerdat.lightlevel = keep;
        PaletteLoader.UpdateShaderCycleParams();
        cap.Ui = PaletteLoader.Palettes[Palette.CurrentPalette].cycledUIPalette[PaletteLoader.NextPaletteCycle_UI].GetImage();
        return cap;
    }

    public override async Task Act(ScenarioContext c)
    {
        await c.Frames(1);
        PaletteLoader.NextPaletteCycle_GAME = StartStep;
        PaletteLoader.NextPaletteCycle_UI = StartStep;
        PaletteLoader.UpdateShaderCycleParams();
        int steps = 0, last = PaletteLoader.NextPaletteCycle_GAME;
        for (int f = 0; f < 240; f++)
        {//4 s at 60 frames a second
            await c.Frames(1);
            int now = PaletteLoader.NextPaletteCycle_GAME;
            if (now == last) continue;
            steps++;
            if (now < last) _wrapped = true;
            last = now;
            _captures.Add(Take());
        }
        _stepsIn4s = steps;
    }

    /// <summary>rotate_bank (uw1 COLCYCLE.C:27-46): COUNT colours from FIRST one place, upward when UP is set.</summary>
    static void RotateBank(byte[] pal, int first, int count, bool up)
    {
        int step = up ? 3 : -3;
        int p = first * 3 + (up ? (count - 1) * 3 : 0);
        byte s0 = pal[p], s1 = pal[p + 1], s2 = pal[p + 2];
        for (int i = 0; i < count - 1; i++, p -= step)
            for (int j = 0; j < 3; j++)
                pal[p + j] = pal[p - step + j];
        pal[p] = s0; pal[p + 1] = s1; pal[p + 2] = s2;
    }

    /// <summary>One step of cycle_colors (uw1 COLCYCLE.C:60-66, uw2 COLCYCLE.C:56-62).</summary>
    static void CycleColors(byte[] pal, bool uw2)
    {
        int banks = uw2 ? 0xE0 : 0x30, runs = uw2 ? 3 : 0x10;
        for (int b = 0; b < 4; b++) RotateBank(pal, banks + b * 4, 4, false);
        RotateBank(pal, runs, 5, true);
        RotateBank(pal, runs + 5, 3, true);
    }

    /// <summary>The first colour x of row 0 of img that is not pal's colour map[x] (x itself without a map), or -1.</summary>
    static int FirstWrong(Image img, byte[] pal, lightmap map)
    {
        for (int x = 1; x < 256; x++)
        {
            int i = (map == null) ? x : map.red[x];
            var col = img.GetPixel(x, 0);
            if (col.R8 != (byte)(pal[i * 3] << 2) || col.G8 != (byte)(pal[i * 3 + 1] << 2) || col.B8 != (byte)(pal[i * 3 + 2] << 2)) return x;
        }
        return -1;
    }

    /// <summary>The light table the nearest distance of a light level takes, which is row 0 of its smoothpalette.</summary>
    static lightmap RowZeroMap(int level)
    {
        int row = (level == shade.Unlit) ? 0 : shade.shadesdata[level].shadingbasedata[0];
        return PaletteLoader.light[row];
    }

    public override string Assert(ScenarioContext c)
    {
        var path = System.IO.Path.Combine(UWClass.BasePath, "DATA", "PALS.DAT");
        if (!System.IO.File.Exists(path)) return "precondition: DATA\\PALS.DAT not found";
        var pals = System.IO.File.ReadAllBytes(path);
        var k = new Checks();

        // the loaded palettes are left as PALS.DAT has them
        for (int p = 0; p < PaletteLoader.Palettes.Length; p++)
        {
            int changed = 0, first = -1;
            for (int i = 0; i < 256; i++)
            {
                var pal = PaletteLoader.Palettes[p];
                int o = p * 768 + i * 3;
                if (pal.red[i] != (byte)(pals[o] << 2) || pal.green[i] != (byte)(pals[o + 1] << 2) || pal.blue[i] != (byte)(pals[o + 2] << 2))
                {
                    changed++;
                    if (first < 0) first = i;
                }
            }
            k.True(changed == 0, $"palette {p}: {changed} colours differ from PALS.DAT after building the cycles, the first at index {first} (cycling works on a copy: COLCYCLE.C)");
        }

        if (Palette.CurrentPalette != 0 || Palette.ColourTone != 0) return $"precondition: palette {Palette.CurrentPalette}, colour tone {Palette.ColourTone}, not palette 0 in colour";
        if (_captures.Count < 10) return $"precondition: only {_captures.Count} cycle steps were seen";
        if (!_wrapped) return "precondition: the cycle never wrapped back to its first step";

        // each step the clock reached is PALS.DAT's palette 0 after step + 1 steps of cycle_colors
        bool uw2 = c.Game == "uw2";
        foreach (var cap in _captures)
        {
            var expected = new byte[768];
            System.Array.Copy(pals, 0, expected, 0, 768);
            for (int n = 0; n < (cap.Step + 1) % 60; n++) CycleColors(expected, uw2);
            int a = FirstWrong(cap.Shaded, expected, RowZeroMap(cap.Level));
            int b = FirstWrong(cap.OtherShaded, expected, RowZeroMap(cap.OtherLevel));
            int u = FirstWrong(cap.Ui, expected, null);
            string rule = $"{(cap.Step + 1) % 60} steps of cycle_colors: uw1 COLCYCLE.C:53-71, uw2 COLCYCLE.C:49-67";
            k.True(a < 0, $"step {cap.Step}: smoothpalette at light level {cap.Level} has colour {a} wrong ({rule})");
            k.True(b < 0, $"step {cap.Step}: smoothpalette at light level {cap.OtherLevel} has colour {b} wrong ({rule})");
            k.True(u < 0, $"step {cap.Step}: the interface palette has colour {u} wrong ({rule})");
        }

        // 64 ticks of the 256 Hz clock a step: 16 steps in 4 s
        k.Within("palette cycle steps in 4 s", 15, 16, _stepsIn4s, "cycle_colors steps when t >> 6 changes: COLCYCLE.C; the clock is 256 Hz: uw1 SOUND.C:167-180");
        return k.Result;
    }
}
