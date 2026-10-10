using System.IO;
using System.Threading.Tasks;
using Underworld.Sfx;

namespace Underworld.Scenarios;

/// <summary>
/// The length of each UW1 sound effect. The original's read_fx_data reads SOUND\SOUNDS.DAT as a
/// count byte and then 5 bytes an effect, patch, note, volume, then the length high byte first:
/// length = (len_hi &lt;&lt; 8) + len_lo (uw1/src/sound/SOUND.C:681-710).
///
/// The scenario loads the shipped SOUNDS.DAT with the port's SoundsDatLoader.Load in the running
/// UW1 game and compares each entry's DurationWord with the original's reading of the same five
/// bytes. Only counts are reported. This is a loader check rather than play, but it needs the game
/// data and UW1 selected, which the scenarios have.
///
/// UW1 only. The port once read UW1's length low byte first (src/audio/sfx/SoundsDatLoader.cs:45),
/// on the strength of a comment citing only UW2's code; tests/Underworld.Sfx.Tests'
/// Duration_word_is_little_endian pins that reading on a synthetic file and changes with the fix.
/// </summary>
public sealed class Uw1SoundsDatLength : Scenario
{
    public override string Name => "uw1-sounds-dat-length";
    public override string[] Games => new[] { "uw1" };
    public override string Cites => "underworld-exhumed uw1/src/sound/SOUND.C:681-710 (read_fx_data)";

    string _problem;
    int _count, _wrong, _differ;

    public override Task Setup(ScenarioContext c) => Task.CompletedTask;

    public override Task Act(ScenarioContext c)
    {
        string path = Path.Combine(UWClass.BasePath, "SOUND", "SOUNDS.DAT");
        if (!File.Exists(path)) { _problem = $"{path} not found"; return Task.CompletedTask; }
        byte[] d = File.ReadAllBytes(path);
        var entries = SoundsDatLoader.Load(path);
        _count = d[0];
        if (entries.Length != _count) { _problem = $"the loader read {entries.Length} entries, the file holds {_count}"; return Task.CompletedTask; }
        for (int i = 0; i < _count; i++)
        {
            int o = 1 + i * 5;
            int length = (d[o + 3] << 8) + d[o + 4];   // SOUND.C:708
            if (d[o + 3] != d[o + 4]) _differ++;
            if (entries[i].DurationWord != length) _wrong++;
        }
        c.Note($"{_count} effects, {_differ} with two different length bytes, {_wrong} read with another length");
        return Task.CompletedTask;
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        if (_differ == 0) return "precondition: no effect's length bytes differ, so the byte order cannot be seen";
        var k = new Checks();
        k.Equal("UW1 effects whose length is read other than high byte first", 0, _wrong, "uw1 SOUND.C:708, (len_hi << 8) + len_lo");
        return k.Result;
    }
}
