using System;
using System.Collections.Generic;
using System.IO;
using Godot;

namespace Underworld
{
    /// <summary>
    /// enhancements.itemmodels (docs/enhancements.md): UW1 floor objects drawn as 3D models instead of
    /// sprites. Drawing only. A model is made for one icon of OBJECTS.GR, so it is found by the icon
    /// number ObjectCreator.CreateSprite is given, which is not always the item id (a runestone draws
    /// icon 224, runestone.cs). It lies still, as the object fell, turned by the object's heading,
    /// where the sprite turns to face the viewer.
    ///
    /// Files, in the folder named by the modelpath setting:
    /// - uw1/OBJnnn.glb, for "model": a static mesh whose texture's red channel holds each pixel's main
    ///   palette index, as the icons' pixels do.
    /// - uw1/OBJnnn.vox, for "voxel", and for "model" when there is no GLB: the voxel file of
    ///   CritterModel.LoadVox with one clip, "still", of one frame, each voxel's slot a main palette index.
    /// nnn is the icon number in three hex digits. The models are not part of the port: they are made
    /// from the game's art, and the player supplies them as they do the game.
    /// </summary>
    public class ItemModel
    {
        static readonly Dictionary<string, ItemModel> cache = new();
        static Shader shader;

        /// <summary>The mode in the settings, or null when objects are drawn as sprites.</summary>
        static string Mode
        {
            get
            {
                if (UWClass._RES == UWClass.GAME_UW2) { return null; }    // models are made for UW1 only so far
                var m = uwsettings.instance?.enhancements.itemmodels;
                return (m == "model" || m == "voxel") && !string.IsNullOrEmpty(uwsettings.instance.modelpath) ? m : null;
            }
        }

        Node3D glbTemplate;
        ArrayMesh voxMesh;

        /// <summary>The model for this icon in the current mode, or null if there is none.</summary>
        public static ItemModel Get(int icon)
        {
            var mode = Mode;
            if (mode == null) { return null; }
            var stem = Path.Combine(uwsettings.instance.modelpath, "uw1", "OBJ" + icon.ToString("x3"));
            var path = mode == "model" && File.Exists(stem + ".glb") ? stem + ".glb" : stem + ".vox";
            if (cache.TryGetValue(path, out var found)) { return found; }
            ItemModel loaded = null;
            if (File.Exists(path))
            {
                try
                {
                    loaded = path.EndsWith(".glb") ? LoadGlb(path) : LoadVox(path);
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"item model {path}: {ex.Message}");
                }
            }
            cache[path] = loaded;
            return loaded;
        }

        static ItemModel LoadGlb(string path)
        {
            var doc = new GltfDocument();
            var state = new GltfState();
            var err = doc.AppendFromFile(path, state);
            if (err != Error.Ok) { throw new IOException($"cannot read the GLB ({err})"); }
            return new ItemModel { glbTemplate = (Node3D)doc.GenerateScene(state) };
        }

        /// <summary>The voxel file's first frame of its clip "still".</summary>
        static ItemModel LoadVox(string path)
        {
            using var f = new BinaryReader(File.OpenRead(path));
            if (new string(f.ReadChars(4)) != "UWVX" || f.ReadByte() != 1) { throw new InvalidDataException("not a version 1 voxel file"); }
            float size = f.ReadSingle();
            var origin = new Vector3(f.ReadInt16(), f.ReadInt16(), f.ReadInt16());
            int clips = f.ReadByte();
            for (int c = 0; c < clips; c++)
            {
                var name = System.Text.Encoding.ASCII.GetString(f.ReadBytes(f.ReadByte()));
                int frames = f.ReadByte();
                for (int i = 0; i < frames; i++)
                {
                    int n = (int)f.ReadUInt32();
                    var b = f.ReadBytes(n * 4);
                    if (b.Length != n * 4) { throw new EndOfStreamException("the file ends inside a frame"); }
                    if (name == "still" && i == 0) { return new ItemModel { voxMesh = CritterModel.VoxelMesh(b, n, origin, size) }; }
                }
            }
            throw new InvalidDataException("no clip \"still\"");
        }

        /// <summary>
        /// Draws the model under the object's node in place of the sprite's quad, turned by the object's
        /// heading as a creature's model is (CritterModel.Turn with no fine heading: an eighth of a turn
        /// each), on the layers and with the object index that the sprite's material has, so it is
        /// lit and clicked as the sprite is.
        /// </summary>
        public Node3D Create(Node3D parent, uwObject obj)
        {
            shader ??= (Shader)ResourceLoader.Load("res://resources/shaders/uwmodel_allred.gdshader");
            var material = new ShaderMaterial { Shader = shader };
            material.SetShaderParameter("palette_direct", true);
            material.SetShaderParameter("slot_from_vertex", glbTemplate == null);
            material.SetShaderParameter("objectindex_lowerbytes", obj.index & 0xFF);
            material.SetShaderParameter("objectindex_upperbytes", (obj.index >> 8) & 0xFF);
            var root = new Node3D { Name = "model", Rotation = new Vector3(0, -obj.heading * Mathf.Pi / 4, 0) };
            parent.AddChild(root);
            if (glbTemplate == null)
            {
                var mi = new MeshInstance3D { Mesh = voxMesh, MaterialOverride = material };
                root.AddChild(mi);
                SetLayers(mi);
            }
            else
            {
                var copy = (Node3D)glbTemplate.Duplicate();
                root.AddChild(copy);
                foreach (var node in copy.FindChildren("*", "MeshInstance3D", true, false))
                {
                    var mi = (MeshInstance3D)node;
                    var own = mi.Mesh.SurfaceGetMaterial(0) as BaseMaterial3D;
                    material.SetShaderParameter("slot_texture", own?.AlbedoTexture);
                    mi.MaterialOverride = material;
                    SetLayers(mi);
                }
            }
            return root;
        }

        static void SetLayers(VisualInstance3D v) => v.Layers = main.LayerGeo | main.LayerObjectInfo | main.LayerXFER;
    }
}
