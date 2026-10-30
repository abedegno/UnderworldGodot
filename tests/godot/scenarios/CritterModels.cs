using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace Underworld.Scenarios;

/// <summary>
/// enhancements.crittermodels: UW1 creatures drawn as 3D models instead of sprites (docs/enhancements.md,
/// src/npc/crittermodel.cs). Drawing only. The models are made from the game's art and are not in the
/// repo, so this writes its own small voxel model, a column of voxels that grows with the frame, for
/// the body of a creature on the level.
///
/// Checks: with the switch off (the default) no creature has a model; a sequence picks its clip
/// (CritterArt.GetUW1AnimName's names), and a frame its key, looping or holding the last; the model
/// faces the camera exactly when the sprite drawn is the front one (npc.CalculateFacingAngleToNPC
/// gives 4), for every heading and camera direction; with the switch on and a model in the folder,
/// a creature redrawn gets the model, shows its sequence and frame, and showing it leaves the
/// creature's object bytes as they were.
/// </summary>
public sealed class CritterModels : Scenario
{
    public override string Name => "critter-models";
    public override string[] Games => new[] { "uw1" };   // models are made for UW1 only so far
    public override string Cites => "Godot npc.cs SetAnimSpriteUW1, CalculateFacingAngleToNPC; playerdatcamera.cs PositionCamera (a camera on a creature); cameraRender.cs Draw";

    string _problem;
    string _folder;
    uwObject _critter;
    readonly System.Collections.Generic.List<string> _fails = new();

    public override async Task Setup(ScenarioContext c)
    {
        await Guards.CameraOnPlayer(c);
        if (uwsettings.instance.enhancements.crittermodels != "off") { _problem = "crittermodels is not off by default"; return; }
        var map = UWTileMap.current_tilemap;
        _critter = map.LevelObjects.FirstOrDefault(o => o != null && o.majorclass == 1 && o.index > 1 && o.item_id < 127 && o.instance is npc);
        if (_critter == null) { _problem = "no creature drawn on this level"; return; }
        _folder = Path.Combine(OS.GetUserDataDir(), "critter-models-test");
        Directory.CreateDirectory(Path.Combine(_folder, "uw1"));
    }

    /// <summary>A voxel file (CritterModel.LoadVox): clip idle_combat with 3 frames and attack_bash with 2,
    /// frame k a column of k + 1 voxels of slot 5.</summary>
    static void WriteModel(string path)
    {
        using var w = new BinaryWriter(File.Create(path));
        w.Write("UWVX".ToCharArray()); w.Write((byte)1); w.Write(0.0075f);
        w.Write((short)0); w.Write((short)0); w.Write((short)0);
        var clips = new (string name, int frames)[] { ("idle_combat", 3), ("attack_bash", 2) };
        w.Write((byte)clips.Length);
        foreach (var (name, frames) in clips)
        {
            w.Write((byte)name.Length); w.Write(System.Text.Encoding.ASCII.GetBytes(name)); w.Write((byte)frames);
            for (int k = 0; k < frames; k++)
            {
                w.Write((uint)(k + 1));
                for (int v = 0; v <= k; v++) { w.Write((byte)0); w.Write((byte)v); w.Write((byte)0); w.Write((byte)5); }
            }
        }
    }

    void Check(bool ok, string what) { if (!ok) _fails.Add(what); }

    public override async Task Act(ScenarioContext c)
    {
        if (_problem != null) return;

        // off: no creature has a model
        int withModel = UWTileMap.current_tilemap.LevelObjects.Count(o => o?.instance is npc n && n.model != null);
        Check(withModel == 0, $"with the switch off {withModel} creatures have a model");

        // sequences and frames
        Check(CritterModel.ClipFor(0x0) == "idle_combat" && CritterModel.ClipFor(0x1) == "attack_bash" && CritterModel.ClipFor(0x2) == "attack_slash"
            && CritterModel.ClipFor(0x3) == "attack_stab" && CritterModel.ClipFor(0x5) == "attack_secondary" && CritterModel.ClipFor(0x7) == "walking"
            && CritterModel.ClipFor(0xc) == "death" && CritterModel.ClipFor(0x20) == "idle" && CritterModel.ClipFor(0x24) == "idle"
            && CritterModel.ClipFor(0x2c) == "walking" && CritterModel.ClipFor(0x84) == "walking", "a sequence picked the wrong clip");
        Check(CritterModel.KeyFor("idle_combat", 9, 7) == 2 && CritterModel.KeyFor("attack_bash", 6, 5) == 4 && CritterModel.KeyFor("attack_bash", 2, 5) == 2,
            "a frame picked the wrong key (loops wrap, others hold the last)");

        // facing: the model faces the camera exactly when the sprite is the front view
        var probe = new uwObject { DataBuffer = new byte[27], PTR = 0 };   // a mobile object's 27 bytes, for its headings
        // the sprite's view follows the 3-bit heading only, so compare at fine heading 0; a view's bucket
        // is two 32nds of a turn either side, and the camera's 32nd is rounded down, so the camera is at
        // most 33.75 degrees off the creature's line (cos 33.75 = 0.83); a model turned the wrong way is -1
        int tested = 0;
        for (int heading = 0; heading < 8; heading++)
                for (int camera = -32768; camera < 32768; camera += 1024)
                {
                    probe.heading = (short)heading; probe.npc_heading = 0;
                    var turn = CritterModel.Turn(probe);
                    var face = new Vector3((float)Math.Sin(turn), 0, (float)Math.Cos(turn));            // the model's +z, turned
                    float cy = (float)(Math.PI - camera / 32767f * Math.PI);
                    var look = new Vector3(-(float)Math.Sin(cy), 0, -(float)Math.Cos(cy));                 // the camera's -z, turned
                    int view = SpriteView(probe, (short)camera);
                    float toward = -face.Dot(look);                                                       // 1 when facing the camera
                    if (view == 4) Check(toward > 0.8f, $"heading {heading}, camera {camera}: front sprite but the model faces {toward:0.00} toward it");
                    if (view == 0) Check(toward < -0.8f, $"heading {heading}, camera {camera}: rear sprite but the model faces {toward:0.00} toward it");
                    tested++;
                }
        c.Note($"{tested} headings and camera directions compared with the sprite's view");
        // the fine heading (5 bits, an eighth of a turn) turns the model part of the way to the next heading
        for (int heading = 0; heading < 8; heading++)
            for (int fine = 1; fine < 32; fine++)
            {
                probe.heading = (short)heading; probe.npc_heading = 0; float t0 = CritterModel.Turn(probe);
                probe.npc_heading = (short)fine; float t = CritterModel.Turn(probe);
                float step = Mathf.Wrap(t - t0, -Mathf.Pi, Mathf.Pi);
                Check(Math.Abs(step - (-fine / 32f * Mathf.Pi / 4)) < 0.001f, $"heading {heading} fine {fine} turns the model {Mathf.RadToDeg(step):0.0} degrees, not {-fine * 45f / 32:0.0}");
            }

        // on, with a model in the folder
        var art = CritterArt.GetCritter(_critter.item_id & 0x3F);
        var file = Path.Combine(_folder, "uw1", "CR" + Convert.ToString(art.FileID, 8).PadLeft(2, '0') + ".vox");
        WriteModel(file);
        string path0 = uwsettings.instance.modelpath;
        uwsettings.instance.modelpath = _folder;
        uwsettings.instance.enhancements.crittermodels = "voxel";
        try
        {
            objectInstance.RedrawFull(_critter);
            await c.Frames(2);
            var n = _critter.instance as npc;
            Check(n?.model != null, "with the switch on and a model in the folder the creature has no model");
            if (n?.model != null)
            {
                var before = _critter.DataBuffer.Skip(_critter.PTR).Take(_critter.IsStatic ? 8 : 27).ToArray();
                n.model.Show(_critter, 0x1, 7);
                Check(n.model.Clip == "attack_bash" && n.model.Key == 1, $"sequence 1 frame 7 showed {n.model.Clip} key {n.model.Key}, not attack_bash key 1");
                n.model.Show(_critter, 0x2, 1);
                Check(n.model.Clip == "idle_combat" && n.model.Key == 1, $"a sequence the model lacks showed {n.model.Clip} key {n.model.Key}, not idle_combat key 1");
                Check(n.model.voxel?.Mesh is ArrayMesh m && m.GetSurfaceCount() == 1, "the voxel frame has no mesh");
                var after = _critter.DataBuffer.Skip(_critter.PTR).Take(_critter.IsStatic ? 8 : 27).ToArray();
                Check(before.SequenceEqual(after), "showing the model changed the creature's object bytes");
                Check(n.sprite == null || !n.sprite.Visible, "the sprite still shows beside the model");
            }
        }
        finally
        {
            uwsettings.instance.enhancements.crittermodels = "off";
            uwsettings.instance.modelpath = path0;
            objectInstance.RedrawFull(_critter);
        }
        await c.Frames(2);
        Check((_critter.instance as npc)?.model == null, "with the switch back off the creature still has a model");
    }

    /// <summary>The sprite view npc.CalculateFacingAngleToNPC picks, for a camera yaw instead of the player's.</summary>
    static int SpriteView(uwObject n, short cameraYaw)
    {
        int ax = (ushort)cameraYaw >> 0xB;
        ax = ((0x20 + (n.heading << 2)) - ax) & 0x1F;
        return new byte[] { 0x0, 0x0, 0x0, 0x1, 0x1, 0x1, 0x2, 0x2, 0x2, 0x2, 0x2, 0x3, 0x3, 0x3, 0x4, 0x4, 0x4, 0x4, 0x4, 0x5, 0x5, 0x5, 0x6, 0x6, 0x6, 0x6, 0x6, 0x7, 0x7, 0x7, 0x0, 0x0 }[ax];
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        return _fails.Count == 0 ? null : string.Join("; ", _fails.Take(6)) + (_fails.Count > 6 ? $" (and {_fails.Count - 6} more)" : "");
    }
}
