using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace Underworld.Scenarios;

/// <summary>
/// enhancements.crittermodels in play: what happens to a creature drawn as a model when it is
/// clicked, killed, saved and restored, and left behind on another level (docs/enhancements.md,
/// src/npc/crittermodel.cs). Drawing only, so each check is that the game behaves as it does with
/// sprites. The models are made from the game's art and are not in the repo, so this writes its own
/// voxel model, a block a little bigger than a hand, for every UW1 body.
///
/// Checks, with the switch on ("voxel"):
/// - a click picks the creature: the object-information view (the layer ClickOnViewPort reads)
///   holds the creature's index at the middle of its model, as it does at the middle of its sprite
///   with the switch off;
/// - a killed creature (damage.DamageObject) plays the model's death clip, and when it is gone
///   no model is left behind: every model in the scene belongs to a live creature;
/// - a save is the same file for file and byte for byte with the switch on and off (SaveGame.Save
///   twice with no frame between), and after a restore (JourneyOnwards, the restore menu's call)
///   every creature drawn has its model again;
/// - after a change of level (the game's teleport) and back, every creature drawn has a model and
///   every model belongs to a creature of the level shown.
/// </summary>
public sealed class CritterModelsLife : Scenario
{
    const int Goblin = 0x47;    // a gray goblin (body 14, CR16)
    const int SlotA = 1, SlotB = 2;

    public override string Name => "critter-models-life";
    public override string[] Games => new[] { "uw1" };   // models are made for UW1 only so far
    public override string Cites => "Godot uimanager_views.cs ClickOnViewPort (the object-information pixel); damage.DamageObject; SaveGame.Save, uimanager.JourneyOnwards; Teleportation.Teleport; npc.CreateInstance, crittermodel.cs Create";

    string _problem, _folder, _path0;
    readonly System.Collections.Generic.List<string> _fails = new();
    void Check(bool ok, string what) { if (!ok) _fails.Add(what); }

    public override async Task Setup(ScenarioContext c)
    {
        if (!Path.GetFullPath(UWClass.BasePath).StartsWith(Path.GetFullPath(OS.GetEnvironment("HOME"))))
        { _problem = $"the game folder {UWClass.BasePath} is not inside the temporary home; refusing to save"; return; }
        await Guards.CameraOnPlayer(c);
        _folder = Path.Combine(OS.GetUserDataDir(), "critter-models-life");
        Directory.CreateDirectory(Path.Combine(_folder, "uw1"));
        for (int body = 0; body < 32; body++)
            WriteBlock(Path.Combine(_folder, "uw1", "CR" + Convert.ToString(body, 8).PadLeft(2, '0') + ".vox"));
        _path0 = uwsettings.instance.modelpath;
    }

    /// <summary>A voxel file (CritterModel.LoadVox) whose every frame is a block 12 by 40 by 12 voxels of
    /// slot 5 standing on the origin, 9 by 30 by 9 cm, in the clips the game asks for.</summary>
    static void WriteBlock(string path)
    {
        using var w = new BinaryWriter(File.Create(path));
        w.Write("UWVX".ToCharArray()); w.Write((byte)1); w.Write(0.0075f);
        w.Write((short)-6); w.Write((short)0); w.Write((short)-6);   // added to each voxel: the block is centred on the origin
        var clips = new (string name, int frames)[] { ("idle", 1), ("idle_combat", 2), ("walking", 2), ("attack_bash", 2), ("attack_slash", 2), ("attack_stab", 2), ("death", 3) };
        w.Write((byte)clips.Length);
        foreach (var (name, frames) in clips)
        {
            w.Write((byte)name.Length); w.Write(System.Text.Encoding.ASCII.GetBytes(name)); w.Write((byte)frames);
            for (int k = 0; k < frames; k++)
            {
                w.Write((uint)(12 * 40 * 12));
                for (int x = 0; x < 12; x++) for (int y = 0; y < 40; y++) for (int z = 0; z < 12; z++)
                        { w.Write((byte)x); w.Write((byte)y); w.Write((byte)z); w.Write((byte)5); }
            }
        }
    }

    static void SetMode(string mode)
    {
        uwsettings.instance.enhancements.crittermodels = mode;
        foreach (var o in Creatures()) objectInstance.RedrawFull(o);
    }

    /// <summary>The creatures drawn on the level shown.</summary>
    static System.Collections.Generic.IEnumerable<uwObject> Creatures() =>
        UWTileMap.current_tilemap.LevelObjects.Where(o => o != null && o.majorclass == 1 && o.index > 1 && o.instance is npc).ToList();

    /// <summary>The models in the scene, and how many of them belong to no creature drawn on the level shown.</summary>
    (int all, int stray, int without) ModelCount()
    {
        var tree = (SceneTree)Engine.GetMainLoop();
        var nodes = tree.Root.FindChildren("model", "Node3D", true, false).Where(n => GodotObject.IsInstanceValid(n) && !n.IsQueuedForDeletion()).ToList();
        var owned = Creatures().Select(o => (o.instance as npc)?.model?.root).Where(r => r != null).ToHashSet();
        int without = Creatures().Count(o => (o.instance as npc)?.model == null);
        return (nodes.Count, nodes.Count(n => !owned.Contains(n)), without);
    }

    async Task<bool> Settled(ScenarioContext c)
    {
        int stable = 0;
        for (int f = 0; f < 900 && stable < 20; f++)
        {
            await c.Frames(1);
            stable = (uimanager.InGame && !uimanager.WaitingForFade && uimanager.CurrentGameMode == uimanager.GameModes.GAME) ? stable + 1 : 0;
        }
        return stable >= 20;
    }

    public override async Task Act(ScenarioContext c)
    {
        if (_problem != null) return;
        uwsettings.instance.modelpath = _folder;
        try
        {
            await Pick(c);
            await Death(c);
            await SaveRestore(c);
            await LevelChange(c);
        }
        finally
        {
            uwsettings.instance.enhancements.crittermodels = "off";
            uwsettings.instance.modelpath = _path0;
        }
    }

    /// <summary>A goblin two squares ahead of the player, facing him, lit.</summary>
    async Task<uwObject> GoblinAhead(ScenarioContext c)
    {
        var map = UWTileMap.current_tilemap; var p = playerdat.playerObject;
        for (int d = 0; d < 4; d++)
        {
            int x1 = p.tileX + GuardWalk.Dx[d], y1 = p.tileY + GuardWalk.Dy[d], x2 = x1 + GuardWalk.Dx[d], y2 = y1 + GuardWalk.Dy[d];
            if (!UWTileMap.ValidTile(x2, y2)) continue;
            var t0 = map.Tiles[p.tileX, p.tileY]; var t1 = map.Tiles[x1, y1]; var t2 = map.Tiles[x2, y2];
            if (t1.tileType != UWTileMap.TILE_OPEN || t2.tileType != UWTileMap.TILE_OPEN || t1.floorHeight != t0.floorHeight || t2.floorHeight != t0.floorHeight) continue;
            var g = BatchB.SpawnCritter(Goblin, x2, y2);
            if (g == null) return null;
            g.npc_goal = 0; g.npc_animation = 0x20;
            playerdat.PlayerCameraYaw_dseg_8294 = GuardWalk.Yaw[d];
            playerdat.lightlevel = 4; playerdat.RefreshLighting();
            objectInstance.RedrawFull(g);
            await c.Frames(10);
            return g;
        }
        return null;
    }

    /// <summary>The object index the object-information view holds at a point of the world, or -1.</summary>
    static int PickAt(Vector3 world)
    {
        var vp = uimanager.instance.uwsubviewport_objectinfo; var cam = vp.GetCamera3D();
        if (cam == null || cam.IsPositionBehind(world)) return -1;
        var s = cam.UnprojectPosition(world);
        RenderingServer.ForceDraw(false);
        var img = vp.GetTexture().GetImage();
        if (s.X < 0 || s.Y < 0 || s.X >= img.GetWidth() || s.Y >= img.GetHeight()) return -1;
        var px = img.GetPixel((int)s.X, (int)s.Y);
        return px.R8 == 0 ? (px.B8 << 8) | px.G8 : -1;
    }

    async Task Pick(ScenarioContext c)
    {
        var g = await GoblinAhead(c);
        if (g == null) { _problem = "no open run of two squares beside the player for a goblin"; return; }
        if (DisplayServer.GetName() == "headless")
        {
            // nothing is drawn headless, so there is no object-information view to read
            SetMode("voxel"); await c.Frames(5);
            c.Note("headless: the click check needs a window (the scenario runner without --headless) and is skipped");
            return;
        }
        var n = g.instance as npc;
        var mid = n.sprite.GlobalPosition;               // the middle of the sprite
        int sprite = PickAt(mid);
        SetMode("voxel"); await c.Frames(5);
        n = g.instance as npc;
        int model = n?.model == null ? -2 : PickAt(n.model.root.GlobalPosition + new Vector3(0, 0.15f, 0));   // the block's middle
        c.Note($"goblin {g.index}: the object-information view holds {sprite} at its sprite (switch off), {model} at its model (voxel)");
        if (sprite != g.index) { _problem = $"the sprite of goblin {g.index} picks {sprite} with the switch off, so the probe is wrong"; return; }
        Check(model == g.index, model == -2 ? "the goblin has no model with the switch on" : $"a click on goblin {g.index}'s model picks {model}");
    }

    async Task Death(ScenarioContext c)
    {
        if (_problem != null) return;
        var g = Creatures().FirstOrDefault(o => o.item_id == Goblin && (o.instance as npc)?.model != null);
        if (g == null) { _problem = "no goblin drawn as a model to kill"; return; }
        int index = g.index; bool sawDeath = false; int frames = 0;
        damage.DamageObject(g, basedamage: g.npc_hp + 1, damagetype: 0, objList: UWTileMap.current_tilemap.LevelObjects, WorldObject: true, damagesource: 1);
        for (; frames < 900; frames++)
        {
            await c.Frames(1);
            var o = UWTileMap.current_tilemap.LevelObjects[index];
            var m = (o?.instance as npc)?.model;
            if (m != null && m.Clip == "death") sawDeath = true;
            if (o == null || o.item_id != Goblin || o.instance == null) break;
        }
        await c.Frames(5);
        var (all, stray, without) = ModelCount();
        c.Note($"goblin {index} killed: death clip {(sawDeath ? "shown" : "not shown")}, gone after {frames} frames; {all} models in the scene, {stray} without a creature");
        Check(sawDeath, "the killed goblin's model never showed its death clip");
        Check(frames < 900, "the killed goblin was still there after 900 frames");
        Check(stray == 0, $"{stray} models left in the scene after the goblin died");
    }

    async Task SaveRestore(ScenarioContext c)
    {
        if (_problem != null) return;
        SetMode("voxel");
        SaveGame.Save(SlotA, "models");
        uwsettings.instance.enhancements.crittermodels = "off";   // no frame between the two saves
        SaveGame.Save(SlotB, "models");
        string a = Path.Combine(UWClass.BasePath, $"SAVE{SlotA}"), b = Path.Combine(UWClass.BasePath, $"SAVE{SlotB}");
        var fa = Directory.GetFiles(a).Select(Path.GetFileName).OrderBy(x => x).ToArray();
        var fb = Directory.GetFiles(b).Select(Path.GetFileName).OrderBy(x => x).ToArray();
        var differ = fa.Where(f => fb.Contains(f) && !File.ReadAllBytes(Path.Combine(a, f)).SequenceEqual(File.ReadAllBytes(Path.Combine(b, f)))).ToList();
        c.Note($"saved with the switch on and off: {fa.Length} and {fb.Length} files, {differ.Count} differ {string.Join(",", differ)}");
        Check(fa.SequenceEqual(fb), $"the saves have different files: {string.Join(",", fa)} against {string.Join(",", fb)}");
        Check(differ.Count == 0, $"the save with models on differs in {string.Join(",", differ)}");

        SetMode("voxel");
        uimanager.instance.JourneyOnwards($"SAVE{SlotA}");
        if (!await Settled(c)) { _problem = "the game did not come back to play after the restore"; return; }
        await c.Frames(5);
        var (all, stray, without) = ModelCount();
        c.Note($"restored: {Creatures().Count()} creatures drawn, {without} without a model, {stray} of {all} models without a creature");
        Check(without == 0, $"{without} creatures have no model after the restore");
        Check(stray == 0, $"{stray} models without a creature after the restore");
    }

    async Task LevelChange(ScenarioContext c)
    {
        if (_problem != null) return;
        int from = playerdat.dungeon_level;
        foreach (int to in new[] { from + 1, from })
        {
            Teleportation.Teleport(0, 32, 32, to, 0);
            for (int f = 0; f < 600 && playerdat.dungeon_level != to; f++) await c.Frames(1);
            if (playerdat.dungeon_level != to || !await Settled(c)) { _problem = $"the teleport to level {to} did not happen"; return; }
            await c.Frames(5);
            var (all, stray, without) = ModelCount();
            c.Note($"level {to}: {Creatures().Count()} creatures drawn, {without} without a model, {stray} of {all} models without a creature");
            Check(without == 0, $"{without} creatures on level {to} have no model");
            Check(stray == 0, $"{stray} models without a creature of level {to}");
        }
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        return _fails.Count == 0 ? null : string.Join("; ", _fails.Take(6)) + (_fails.Count > 6 ? $" (and {_fails.Count - 6} more)" : "");
    }
}
