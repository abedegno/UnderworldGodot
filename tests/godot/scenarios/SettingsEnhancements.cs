using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// The settings file's enhancements section (docs/enhancements.md): a file written before the
/// section existed loads with every switch at its default, and the section survives a save.
///
/// uwsettings cannot be built outside Godot (its static constructor reads user://), so this runs
/// here rather than in Underworld.Save.Tests. It reads files with JsonFile.TryRead and the game's
/// own options (uwsettings.JsonOpts), as LoadSettings does, from a folder in the temporary home,
/// and never touches uwsettings.instance.
/// </summary>
public sealed class SettingsEnhancements : Scenario
{
    public override string Name => "settings-enhancements";
    public override string[] Games => new[] { "uw1" };
    public override string Cites => "Godot src/utility/config.cs (uwsettings.enhancements); no original behaviour: a port setting";

    readonly Checks _k = new();

    public override Task Setup(ScenarioContext c) => Task.CompletedTask;

    static uwsettings Read(string dir, string json)
    {
        string path = Path.Combine(dir, "settings.json");
        File.WriteAllText(path, json);
        return JsonFile.TryRead<uwsettings>(path, uwsettings.JsonOpts, out var s, out _) == JsonReadOutcome.Ok ? s : null;
    }

    public override Task Act(ScenarioContext c)
    {
        string dir = Path.Combine(Path.GetTempPath(), "uwsettings-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            const string old = "{\n  \"pathuw1\": \"C:\\\\Games\\\\UW\",\n  \"gametoload\": \"UW1\",\n  \"FOV\": 75\n}";
            var a = Read(dir, old);
            _k.True(a != null && a.enhancements != null && a.enhancements.smoothcamera, "a file without enhancements did not load with smoothcamera on");
            var b = Read(dir, "{ \"gametoload\": \"UW1\", \"enhancements\": {} }");
            _k.True(b != null && b.enhancements.smoothcamera, "an empty enhancements section did not load with smoothcamera on");
            var n = Read(dir, "{ \"gametoload\": \"UW1\", \"enhancements\": null }");
            _k.True(n != null && n.enhancements != null && n.enhancements.smoothcamera, "enhancements: null did not load as the defaults");
            if (a != null)
            {
                a.enhancements.smoothcamera = false;
                var back = Read(dir, JsonSerializer.Serialize(a, uwsettings.JsonOpts));
                _k.True(back != null && !back.enhancements.smoothcamera, "smoothcamera false did not survive a save and load");
                _k.True(back != null && back.pathuw1 == a.pathuw1 && back.FOV == a.FOV, "the other settings did not survive the save and load");
            }
        }
        finally { Directory.Delete(dir, true); }
        return Task.CompletedTask;
    }

    public override string Assert(ScenarioContext c) => _k.Result;
}
