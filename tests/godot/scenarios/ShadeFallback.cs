using System.Reflection;
using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// The distance shades when SHADES.DAT cannot be read. The original's set_light returns before touching the
/// shading globals when the file does not open (uw1 map/LIGHTING.C:66-68, uw2 map/LIGHTING.C:71-72), so the grid
/// keeps what init_3d built: preset_grid(8) (uw1 3d/VIEW3D.C:240, uw2 3d/VIEW3D.C:238) with the globals' initial
/// values, smooth_div 6, smooth_base 0 and smooth_lowpass 0 (UW1 UW.EXE 3963:064C, UW2 UW2.EXE 370D:064C, the words
/// 6, 0, 0). preset_grid gives distance i the shade (cSqRt(2 * (8i)^2) * smooth_div >> 6) + smooth_lowpass + smooth_base,
/// at most 14, and 15 beyond the range (uw1 VIEW3D.C:501-519).
///
/// The port's fallback, shade.CreateEmptyShades, meant full light but passed a viewing distance of 20, which the
/// constructor masks to 4, so every light level went dark beyond distance 4 (src/loaders/shadesdatloader.cs).
/// The scenario runs the fallback through reflection and reads the shade rows it builds, then puts the real ones back.
/// </summary>
public sealed class ShadeFallback : Scenario
{
    public override string Name => "shade-fallback";
    public override string Cites => "underworld-exhumed uw1/src/map/LIGHTING.C:66-68, uw1/src/3d/VIEW3D.C:240, 494-519; uw2 LIGHTING.C:71-72, VIEW3D.C:238";

    short[][] _rows;
    bool _ran;

    public override Task Setup(ScenarioContext c) => Task.CompletedTask;

    public override Task Act(ScenarioContext c)
    {
        var real = shade.shadesdata;
        var fallback = typeof(shade).GetMethod("CreateEmptyShades", BindingFlags.NonPublic | BindingFlags.Static);
        if (fallback == null) return Task.CompletedTask;
        try
        {
            fallback.Invoke(null, null);
            _rows = new short[shade.shadesdata.Length][];
            for (int l = 0; l < shade.shadesdata.Length; l++)
                _rows[l] = shade.shadesdata[l].shadingbasedata;
            _ran = true;
        }
        finally
        {
            shade.shadesdata = real;
        }
        return Task.CompletedTask;
    }

    /// <summary>preset_grid(8)'s shade for distance i with smooth_div 6, smooth_base 0, smooth_lowpass 0.</summary>
    static int Expected(int i)
    {
        if (i > 8) return 15;
        int t = (i << 8) >> 5;
        int u = UnderWorldSqrt.sqrt_vanilla((uint)(t * t * 2));
        t = (u * 6) >> 6;
        if (t < 0) t = 0;
        return t > 14 ? 14 : t;
    }

    public override string Assert(ScenarioContext c)
    {
        if (!_ran) return "precondition: shade.CreateEmptyShades could not be run";
        if (_rows.Length != 8) return $"precondition: the fallback built {_rows.Length} light levels, not 8";
        var k = new Checks();
        var expected = new int[16];
        for (int i = 0; i < 16; i++) expected[i] = Expected(i);
        string want = string.Join(",", expected);
        for (int l = 0; l < 8; l++)
        {
            string got = string.Join(",", _rows[l]);
            k.True(got == want, $"light level {l}: expected the shades by distance {want}, got {got} (init_3d's preset_grid(8): VIEW3D.C:240, 501-519)");
        }
        return k.Result;
    }
}
