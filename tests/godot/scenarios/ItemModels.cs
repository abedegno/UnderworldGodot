using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace Underworld.Scenarios;

/// <summary>
/// enhancements.itemmodels: UW1 floor objects drawn as 3D models or voxels instead of sprites
/// (docs/enhancements.md, src/objects/itemmodel.cs). Drawing only. The models are made from the game's
/// art and are not in the repo, so this writes its own small voxel model, a 2x3x1 block, for the icon
/// of a floor object on the level.
///
/// Checks: with the switch off (the default) the object is a sprite; on, with a model in the folder,
/// it is drawn as the model, turned by its heading, and its object bytes are left as they were; in
/// "model" mode a voxel file stands in when there is no GLB; an icon with no file, or a damaged file,
/// stays a sprite; an invisible object has neither; a runestone, drawn with icon 224 whatever its item
/// id (runestone.cs), loads icon 224's file.
/// </summary>
public sealed class ItemModels : Scenario
{
    public override string Name => "item-models";
    public override string[] Games => new[] { "uw1" };   // models are made for UW1 only so far
    public override string Cites => "Godot ObjectCreator.CreateSprite, CreateSpriteInstance; runestone.cs; itemmodel.cs";

    string _problem;
    string _folder;
    uwObject _item, _other, _bad;
    readonly System.Collections.Generic.List<string> _fails = new();

    public override async Task Setup(ScenarioContext c)
    {
        await Guards.CameraOnPlayer(c);
        if (uwsettings.instance.enhancements.itemmodels != "off") { _problem = "itemmodels is not off by default"; return; }
        var sprites = UWTileMap.current_tilemap.LevelObjects
            .Where(o => o != null && o.index > 1 && o.invis == 0 && o.instance is genericsprite && Quad(o) != null).ToList();
        _item = sprites.FirstOrDefault();
        _other = sprites.FirstOrDefault(o => o.item_id != _item?.item_id);
        _bad = sprites.FirstOrDefault(o => o.item_id != _item?.item_id && o.item_id != _other?.item_id);
        if (_item == null || _other == null || _bad == null) { _problem = "fewer than three kinds of floor sprite on this level"; return; }
        _folder = Path.Combine(OS.GetUserDataDir(), "item-models-test");
        if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
        Directory.CreateDirectory(Path.Combine(_folder, "uw1"));
    }

    /// <summary>A voxel file (CritterModel.LoadVox's format): one clip "still" of one frame, a 2x3x1 block of slot 0x10.</summary>
    static void WriteBlock(string path, byte version = 1)
    {
        using var w = new BinaryWriter(File.Create(path));
        w.Write("UWVX".ToCharArray()); w.Write(version); w.Write(0.01f);
        w.Write((short)-1); w.Write((short)0); w.Write((short)0);
        w.Write((byte)1);
        w.Write((byte)5); w.Write(System.Text.Encoding.ASCII.GetBytes("still")); w.Write((byte)1);
        w.Write((uint)6);
        for (int x = 0; x < 2; x++) for (int y = 0; y < 3; y++) { w.Write((byte)x); w.Write((byte)y); w.Write((byte)0); w.Write((byte)0x10); }
    }

    static string FileFor(string folder, int icon, string ext) => Path.Combine(folder, "uw1", "OBJ" + icon.ToString("x3") + ext);

    static Node3D Node(uwObject o) => o.instance?.uwnode;
    static MeshInstance3D Quad(uwObject o) => Node(o)?.GetChildren().OfType<MeshInstance3D>().FirstOrDefault(m => m.Mesh is QuadMesh);
    static Node3D Model(uwObject o) => Node(o)?.GetNodeOrNull<Node3D>("model");

    void Check(bool ok, string what) { if (!ok) _fails.Add(what); }

    async Task Redraw(ScenarioContext c, uwObject o) { objectInstance.RedrawFull(o); await c.Frames(2); }

    public override async Task Act(ScenarioContext c)
    {
        if (_problem != null) return;
        Check(Quad(_item) != null && Model(_item) == null, "OffByDefault: with the switch off the object is not a sprite");

        string path0 = uwsettings.instance.modelpath;
        uwsettings.instance.modelpath = _folder;
        uwsettings.instance.enhancements.itemmodels = "voxel";
        short heading0 = _item.heading;
        try
        {
            WriteBlock(FileFor(_folder, _item.item_id, ".vox"));
            var before = _item.DataBuffer.Skip(_item.PTR).Take(_item.IsStatic ? 8 : 27).ToArray();
            await Redraw(c, _item);
            var after = _item.DataBuffer.Skip(_item.PTR).Take(_item.IsStatic ? 8 : 27).ToArray();
            Check(Model(_item) != null && Quad(_item) == null, "VoxelDrawn: with a model in the folder the object is not drawn as the model alone");
            Check(before.SequenceEqual(after), "BytesUnchanged: drawing the model changed the object's bytes");
            _item.heading = 3;
            await Redraw(c, _item);
            float turn = Model(_item)?.RotationDegrees.Y ?? float.NaN;
            Check(Math.Abs(Mathf.Wrap(turn - (-45f * 3), -180f, 180f)) < 0.5f, $"VoxelDrawn: heading 3 turned the model {turn:0.0} degrees, not -135");
            _item.heading = heading0;

            uwsettings.instance.enhancements.itemmodels = "model";
            await Redraw(c, _item);
            Check(Model(_item) != null, "ModelFallsBackToVoxel: in model mode with only a voxel file the object has no model");
            uwsettings.instance.enhancements.itemmodels = "voxel";

            await Redraw(c, _other);
            Check(Quad(_other) != null && Model(_other) == null, "MissingFileKeepsSprite: an icon with no file is not a sprite");

            // a kind of its own: the models are cached by file, so _other's file is already known to be missing
            WriteBlock(FileFor(_folder, _bad.item_id, ".vox"), version: 9);
            await Redraw(c, _bad);
            Check(Quad(_bad) != null && Model(_bad) == null, "BadFileKeepsSprite: a damaged file did not leave the sprite");

            _item.invis = 1;
            await Redraw(c, _item);
            Check(Quad(_item) == null && Model(_item) == null, "InvisibleHasNoModel: an invisible object is drawn");
            _item.invis = 0;

            // a runestone draws icon 224 whatever its item id
            var p = playerdat.playerObject;
            WriteBlock(FileFor(_folder, 224, ".vox"));   // before the stone is made: making it draws it
            var stone = BatchB.SpawnStatic(0xe8, p.tileX, p.tileY);
            if (stone == null) c.Note("KeyedByIcon: skipped, no free static slot");
            else
            {
                await Redraw(c, stone);
                Check(Model(stone) != null, "KeyedByIcon: a runestone (item 0xe8) did not load icon 224's file");
                ObjectRemover.RemoveObjectAndContentsFromTile(stone);
            }
        }
        finally
        {
            _item.heading = heading0; _item.invis = 0;
            uwsettings.instance.enhancements.itemmodels = "off";
            uwsettings.instance.modelpath = path0;
            objectInstance.RedrawFull(_item); objectInstance.RedrawFull(_other); objectInstance.RedrawFull(_bad);
        }
        await c.Frames(2);
        Check(Quad(_item) != null && Model(_item) == null, "with the switch back off the object is not a sprite again");
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        return _fails.Count == 0 ? null : string.Join("; ", _fails.Take(6)) + (_fails.Count > 6 ? $" (and {_fails.Count - 6} more)" : "");
    }
}
