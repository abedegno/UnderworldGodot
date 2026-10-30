using System;
using System.Collections.Generic;
using System.IO;
using Godot;

namespace Underworld
{
    /// <summary>
    /// enhancements.crittermodels (docs/enhancements.md): UW1 creatures drawn as 3D models instead of
    /// sprites. Drawing only: the game still runs the sprites' animation sequences and frames, and a
    /// model shows the same sequence and frame. A model is made for one body, the critter page files
    /// CRnnPAGE that several creature types share with different aux palettes, so it is found by the
    /// body's number and coloured with each type's aux palette (shader uwmodel_allred).
    ///
    /// Files, in the folder named by the modelpath setting:
    /// - uw1/CRnn.glb, for "model": a skinned mesh whose texture's red channel holds each pixel's slot
    ///   in the aux palette, with one clip per sequence (CritterModel.ClipFor), its key frames at
    ///   eight a second, one per frame of the sequence.
    /// - uw1/CRnn.vox, for "voxel": the same clips as voxels, one grid per key frame (LoadVox).
    /// nn is the body's number in octal, as in the page files' names. The models are not part of the
    /// port: they are made from the game's art, and the player supplies them as they do the game.
    /// </summary>
    public class CritterModel
    {
        static readonly Dictionary<string, CritterModel> cache = new();
        static Shader shader;

        /// <summary>The mode in the settings, or null when creatures are drawn as sprites.</summary>
        public static string Mode
        {
            get
            {
                if (UWClass._RES == UWClass.GAME_UW2) { return null; }    // models are made for UW1 only so far
                var m = uwsettings.instance?.enhancements.crittermodels;
                return (m == "model" || m == "voxel") && !string.IsNullOrEmpty(uwsettings.instance.modelpath) ? m : null;
            }
        }

        /// <summary>The model for this body in the current mode, or null if there is none.</summary>
        public static CritterModel Get(CritterArt art)
        {
            var mode = Mode;
            if (mode == null || art == null) { return null; }
            var path = Path.Combine(uwsettings.instance.modelpath, "uw1", "CR" + Convert.ToString(art.FileID, 8).PadLeft(2, '0') + (mode == "model" ? ".glb" : ".vox"));
            if (cache.TryGetValue(path, out var found)) { return found; }
            CritterModel loaded = null;
            if (File.Exists(path))
            {
                try
                {
                    loaded = mode == "model" ? LoadGlb(path) : LoadVox(path);
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"critter model {path}: {ex.Message}");
                }
            }
            cache[path] = loaded;
            return loaded;
        }

        /// <summary>
        /// The clip for a UW1 animation sequence (CritterArt.GetUW1AnimName). The sprites draw an
        /// attack only when the creature faces the viewer (npc.SetAnimSpriteUW1, uw1 DRAWOBJ.C:214-217)
        /// because the art has no other angles; a model has them all, so it always shows the sequence.
        /// </summary>
        public static string ClipFor(int animation)
        {
            switch (animation)
            {
                case 0x0: return "idle_combat";
                case 0x1: return "attack_bash";
                case 0x2: return "attack_slash";
                case 0x3: return "attack_stab";
                case 0x5: return "attack_secondary";
                case 0x7: return "walking";
                case 0xc: return "death";
                case 0xd: return "cast";
            }
            if ((animation >= 0x20) && (animation <= 0x27)) { return "idle"; }
            if (((animation >= 0x28) && (animation <= 0x2e)) || ((animation >= 0x80) && (animation <= 0x87))) { return "walking"; }
            return "idle_combat";
        }

        /// <summary>Clips that repeat; the others hold their last frame.</summary>
        public static bool Loops(string clip) => clip == "idle" || clip == "idle_combat" || clip == "walking";

        /// <summary>The key frame a sequence's frame shows, for a clip of count key frames.</summary>
        public static int KeyFor(string clip, int frame, int count)
        {
            if (count <= 0) { return 0; }
            frame = Math.Max(frame, 0);
            return Loops(clip) ? frame % count : Math.Min(frame, count - 1);
        }

        /// <summary>
        /// The model's turn for a creature's heading: heading (3 bits) and npc_heading (5 bits) make the
        /// same 16-bit yaw the camera uses (playerdatcamera.cs, attached to a creature). The camera's
        /// Godot turn is pi - yaw/32767 pi and it looks down its -z, and a model faces +z, so the model
        /// turns by -yaw/32767 pi to face where a camera on the creature would look.
        /// </summary>
        public static float Turn(uwObject obj)
        {
            var yaw = (short)((obj.npc_heading << 8) + (obj.heading << 0xD));
            return (float)(-(yaw / 32767f) * Math.PI);
        }

        Node3D glbTemplate;
        readonly Dictionary<string, int> keyCount = new();
        readonly Dictionary<string, ArrayMesh[]> voxFrames = new();
        // each voxel frame's bytes until it is first shown: building every frame of every clip at load
        // took ten seconds for a level's creatures, and most frames are seldom seen
        readonly Dictionary<string, byte[][]> voxData = new();
        Vector3 voxOrigin;
        float voxSize;
        bool IsVoxel => glbTemplate == null;

        static CritterModel LoadGlb(string path)
        {
            var doc = new GltfDocument();
            var state = new GltfState();
            var err = doc.AppendFromFile(path, state);
            if (err != Error.Ok) { throw new IOException($"cannot read the GLB ({err})"); }
            var m = new CritterModel { glbTemplate = (Node3D)doc.GenerateScene(state) };
            var players = m.glbTemplate.FindChildren("*", "AnimationPlayer", true, false);
            if (players.Count == 0) { throw new InvalidDataException("no animations"); }
            var ap = (AnimationPlayer)players[0];
            foreach (var name in ap.GetAnimationList())
            {
                // the looping clips end on their first frame again, for blending; frames step, so it is not one of them
                var anim = ap.GetAnimation(name);
                int keys = (int)Math.Round(anim.Length * 8) + 1;
                m.keyCount[name] = Loops(name) && keys > 1 ? keys - 1 : keys;
            }
            return m;
        }

        /// <summary>
        /// The voxel file: "UWVX", version byte 1, f32 voxel size, s16 x0, y0, z0 (the grid's first
        /// voxel, in voxels, in the model's own frame), u8 clip count; per clip a u8-length name and a
        /// u8 frame count; per frame a u32 voxel count and four bytes per voxel: x, y, z and slot.
        /// </summary>
        static CritterModel LoadVox(string path)
        {
            using var f = new BinaryReader(File.OpenRead(path));
            if (new string(f.ReadChars(4)) != "UWVX" || f.ReadByte() != 1) { throw new InvalidDataException("not a version 1 voxel file"); }
            float size = f.ReadSingle();
            var origin = new Vector3(f.ReadInt16(), f.ReadInt16(), f.ReadInt16());
            var m = new CritterModel { voxOrigin = origin, voxSize = size };
            int clips = f.ReadByte();
            for (int c = 0; c < clips; c++)
            {
                var name = System.Text.Encoding.ASCII.GetString(f.ReadBytes(f.ReadByte()));
                var data = new byte[f.ReadByte()][];
                for (int i = 0; i < data.Length; i++)
                {
                    int n = (int)f.ReadUInt32();
                    data[i] = f.ReadBytes(n * 4);
                    if (data[i].Length != n * 4) { throw new EndOfStreamException("the file ends inside a frame"); }
                }
                m.voxData[name] = data;
                m.voxFrames[name] = new ArrayMesh[data.Length];
                m.keyCount[name] = data.Length;
            }
            return m;
        }

        /// <summary>A voxel frame's mesh, built the first time it is shown.</summary>
        ArrayMesh VoxFrame(string clip, int key)
        {
            var frames = voxFrames[clip];
            if (frames[key] == null)
            {
                var b = voxData[clip][key];
                frames[key] = VoxelMesh(b, b.Length / 4, voxOrigin, voxSize);
                voxData[clip][key] = null;
            }
            return frames[key];
        }

        static readonly Vector3I[] Dirs = { new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0), new(0, 0, 1), new(0, 0, -1) };
        // the corners of the face on each side, anticlockwise seen from outside
        static readonly Vector3[][] Quads = {
            new Vector3[] { new(1, 0, 0), new(1, 1, 0), new(1, 1, 1), new(1, 0, 1) },
            new Vector3[] { new(0, 0, 1), new(0, 1, 1), new(0, 1, 0), new(0, 0, 0) },
            new Vector3[] { new(0, 1, 0), new(0, 1, 1), new(1, 1, 1), new(1, 1, 0) },
            new Vector3[] { new(0, 0, 1), new(0, 0, 0), new(1, 0, 0), new(1, 0, 1) },
            new Vector3[] { new(1, 0, 1), new(1, 1, 1), new(0, 1, 1), new(0, 0, 1) },
            new Vector3[] { new(0, 0, 0), new(0, 1, 0), new(1, 1, 0), new(1, 0, 0) } };

        /// <summary>
        /// A mesh of the voxels' outer faces; each face's vertex colour red is its slot / 255. Faces of one
        /// slot that touch on the same layer are joined into rectangles (greedy meshing), the same picture
        /// with about half the triangles; neighbouring voxels seldom share a slot, so it is not more.
        /// </summary>
        public static ArrayMesh VoxelMesh(byte[] b, int n, Vector3 origin, float size)
        {
            var pos = new List<Vector3>(); var nor = new List<Vector3>(); var col = new List<Color>(); var idx = new List<int>();
            if (n > 0)
            {
                // the frame's voxels in a grid one voxel bigger all round, each cell its slot + 1 (0 empty)
                var lo = new Vector3I(255, 255, 255); var hi = new Vector3I(0, 0, 0);
                for (int i = 0; i < n; i++)
                {
                    for (int k = 0; k < 3; k++) { lo[k] = Math.Min(lo[k], b[4 * i + k]); hi[k] = Math.Max(hi[k], b[4 * i + k]); }
                }
                var dim = hi - lo + new Vector3I(3, 3, 3);
                var grid = new byte[dim.X * dim.Y * dim.Z];
                int At(int x, int y, int z) => (z * dim.Y + y) * dim.X + x;
                for (int i = 0; i < n; i++) { grid[At(b[4 * i] - lo.X + 1, b[4 * i + 1] - lo.Y + 1, b[4 * i + 2] - lo.Z + 1)] = (byte)(b[4 * i + 3] + 1); }
                var c3 = new int[3];
                for (int d = 0; d < 6; d++)
                {
                    int a = d / 2, ua = (a + 1) % 3, va = (a + 2) % 3, du = dim[ua], dv = dim[va];
                    var mask = new int[du * dv];
                    for (int l = 1; l < dim[a] - 1; l++)
                    {
                        // the slot + 1 of each face on this layer that looks out to this side, else 0
                        for (int v = 0; v < dv; v++)
                        {
                            for (int u = 0; u < du; u++)
                            {
                                c3[a] = l; c3[ua] = u; c3[va] = v;
                                int g = grid[At(c3[0], c3[1], c3[2])];
                                c3[a] = l + Dirs[d][a];
                                mask[v * du + u] = g != 0 && grid[At(c3[0], c3[1], c3[2])] == 0 ? g : 0;
                            }
                        }
                        for (int v = 0; v < dv; v++)
                        {
                            for (int u = 0; u < du; u++)
                            {
                                int s = mask[v * du + u];
                                if (s == 0) { continue; }
                                int w = 1; while (u + w < du && mask[v * du + u + w] == s) { w++; }
                                int h = 1;
                                while (v + h < dv)
                                {
                                    int k = 0; while (k < w && mask[(v + h) * du + u + k] == s) { k++; }
                                    if (k < w) { break; }
                                    h++;
                                }
                                for (int y = v; y < v + h; y++) { for (int x = u; x < u + w; x++) { mask[y * du + x] = 0; } }
                                var c = new Color((s - 1) / 255f, 0, 0);
                                int start = pos.Count;
                                foreach (var q in Quads[d])
                                {
                                    // the unit face's corner, stretched over the rectangle, back in the file's voxels
                                    var corner = new Vector3();
                                    corner[a] = l - 1 + lo[a] + q[a]; corner[ua] = u - 1 + lo[ua] + q[ua] * w; corner[va] = v - 1 + lo[va] + q[va] * h;
                                    pos.Add((corner + origin) * size);
                                    nor.Add(new Vector3(Dirs[d].X, Dirs[d].Y, Dirs[d].Z));
                                    col.Add(c);
                                }
                                idx.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
                            }
                        }
                    }
                }
            }
            var arr = new Godot.Collections.Array();
            arr.Resize((int)Mesh.ArrayType.Max);
            arr[(int)Mesh.ArrayType.Vertex] = pos.ToArray();
            arr[(int)Mesh.ArrayType.Normal] = nor.ToArray();
            arr[(int)Mesh.ArrayType.Color] = col.ToArray();
            arr[(int)Mesh.ArrayType.Index] = idx.ToArray();
            var mesh = new ArrayMesh();
            if (idx.Count > 0) { mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arr); }
            return mesh;
        }

        /// <summary>A drawn copy of the model for one creature, coloured with its type's aux palette.</summary>
        public Instance Create(Node3D parent, uwObject obj, CritterArt art)
        {
            shader ??= (Shader)ResourceLoader.Load("res://resources/shaders/uwmodel_allred.gdshader");
            var material = new ShaderMaterial { Shader = shader };
            var aux = new int[32];
            for (int i = 0; i < 32; i++) { aux[i] = art.AuxPalette[i]; }
            material.SetShaderParameter("auxpalette", aux);
            material.SetShaderParameter("slot_from_vertex", IsVoxel);
            material.SetShaderParameter("objectindex_lowerbytes", obj.index & 0xFF);
            material.SetShaderParameter("objectindex_upperbytes", (obj.index >> 8) & 0xFF);
            var inst = new Instance { model = this, root = new Node3D { Name = "model" } };
            parent.AddChild(inst.root);
            if (IsVoxel)
            {
                inst.voxel = new MeshInstance3D { MaterialOverride = material };
                inst.root.AddChild(inst.voxel);
                SetLayers(inst.voxel);
            }
            else
            {
                var copy = (Node3D)glbTemplate.Duplicate();
                inst.root.AddChild(copy);
                foreach (var node in copy.FindChildren("*", "MeshInstance3D", true, false))
                {
                    var mi = (MeshInstance3D)node;
                    var own = mi.Mesh.SurfaceGetMaterial(0) as BaseMaterial3D;
                    material.SetShaderParameter("slot_texture", own?.AlbedoTexture);
                    mi.MaterialOverride = material;
                    SetLayers(mi);
                }
                inst.player = (AnimationPlayer)copy.FindChildren("*", "AnimationPlayer", true, false)[0];
            }
            return inst;
        }

        static void SetLayers(VisualInstance3D v) => v.Layers = main.LayerGeo | main.LayerObjectInfo | main.LayerXFER;

        public class Instance
        {
            public CritterModel model;
            public Node3D root;
            public MeshInstance3D voxel;
            public AnimationPlayer player;
            /// <summary>The clip and key frame last shown, for tests.</summary>
            public string Clip;
            public int Key;

            /// <summary>Shows the creature's sequence and frame, turned to its heading.</summary>
            public void Show(uwObject obj, int animation, int frame)
            {
                var clip = ClipFor(animation);
                if (!model.keyCount.ContainsKey(clip)) { clip = model.keyCount.ContainsKey("idle_combat") ? "idle_combat" : "idle"; }
                if (!model.keyCount.TryGetValue(clip, out int count)) { return; }
                Clip = clip;
                Key = KeyFor(clip, frame, count);
                root.Rotation = new Vector3(0, Turn(obj), 0);
                if (voxel != null)
                {
                    voxel.Mesh = model.VoxFrame(clip, Key);
                }
                else
                {
                    player.Play(clip, -1, 0);
                    player.Seek(Key / 8.0, true);
                }
            }

            public bool Visible { set => root.Visible = value; }
        }
    }
}
