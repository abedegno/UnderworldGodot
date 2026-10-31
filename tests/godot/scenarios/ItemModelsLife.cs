using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace Underworld.Scenarios;

/// <summary>
/// enhancements.itemmodels in play (docs/enhancements.md, src/objects/itemmodel.cs): a floor object drawn as a
/// model is clicked, picked up, dropped, saved and restored and left on another level as with its sprite. The
/// models are made from the game's art and are not in the repo, so this writes a block voxel model for every icon.
///
/// Checks: a click on the model's middle picks the object (the object-information pixel, as a click on the
/// viewport reads it, uimanager_views.cs ClickOnViewPort; needs a window); an object picked up leaves no model
/// behind, and dropped (thrown, pickup.DropObjectByPlayer) it lands with a model; a save taken with the switch on
/// is the same byte for byte as one taken with it off, and a restore draws the models again; a level change and
/// back draws them again and leaves no model of the old level behind.
///
/// The click check needs a window and a run of its own: after another scenario in the same run (a new game scene)
/// the object-information view was not rendered at all, every pixel 0, while the game view drew, as with
/// critter-models-life. Headless, as the suite runs, it is skipped.
/// </summary>
public sealed class ItemModelsLife : Scenario
{
    public override string Name => "item-models-life";
    public override string[] Games => new[] { "uw1" };   // models are made for UW1 only so far
    public override string Cites => "Godot uimanager_views.cs ClickOnViewPort (the object-information pixel); pickup.PickUp, DropObjectByPlayer; SaveGame.Save, uimanager.JourneyOnwards; Teleportation.Teleport; ObjectCreator.CreateSprite, itemmodel.cs Create";

    const int SlotA = 3, SlotB = 4;
    const int Potion = 0xbb;
    string _problem, _folder, _path0;
    readonly System.Collections.Generic.List<string> _fails = new(), _strays = new();
    void Check(bool ok, string what) { if (!ok) _fails.Add(what); }

    public override async Task Setup(ScenarioContext c)
    {
        if (!Path.GetFullPath(UWClass.BasePath).StartsWith(Path.GetFullPath(OS.GetEnvironment("HOME"))))
        { _problem = $"the game folder {UWClass.BasePath} is not inside the temporary home; refusing to save"; return; }
        await Guards.CameraOnPlayer(c);
        _folder = Path.Combine(OS.GetUserDataDir(), "item-models-life");
        Directory.CreateDirectory(Path.Combine(_folder, "uw1"));
        for (int icon = 0; icon < 0x200; icon++) WriteBlock(Path.Combine(_folder, "uw1", "OBJ" + icon.ToString("x3") + ".vox"));
        _path0 = uwsettings.instance.modelpath;
    }

    /// <summary>A voxel file (ItemModel.LoadVox): clip "still", a block 10 by 20 by 10 voxels of index 0x10
    /// standing on the origin, 10 by 20 by 10 cm.</summary>
    static void WriteBlock(string path)
    {
        using var w = new BinaryWriter(File.Create(path));
        w.Write("UWVX".ToCharArray()); w.Write((byte)1); w.Write(0.01f);
        w.Write((short)-5); w.Write((short)0); w.Write((short)-5);
        w.Write((byte)1);
        w.Write((byte)5); w.Write(System.Text.Encoding.ASCII.GetBytes("still")); w.Write((byte)1);
        w.Write((uint)(10 * 20 * 10));
        for (int x = 0; x < 10; x++) for (int y = 0; y < 20; y++) for (int z = 0; z < 10; z++) { w.Write((byte)x); w.Write((byte)y); w.Write((byte)z); w.Write((byte)0x10); }
    }

    static Node3D Model(uwObject o) => o?.instance?.uwnode is Node3D n && GodotObject.IsInstanceValid(n) ? n.GetNodeOrNull<Node3D>("model") : null;
    static MeshInstance3D Quad(uwObject o) => o?.instance?.uwnode is Node3D n && GodotObject.IsInstanceValid(n) ? n.GetChildren().OfType<MeshInstance3D>().FirstOrDefault(m => m.Mesh is QuadMesh) : null;

    /// <summary>The floor objects drawn as sprites or item models on the level shown.</summary>
    static System.Collections.Generic.List<uwObject> Items() =>
        UWTileMap.current_tilemap.LevelObjects.Where(o => o != null && o.index > 1 && o.instance is genericsprite && o.invis == 0).ToList();

    static void SetMode(string mode)
    {
        uwsettings.instance.enhancements.itemmodels = mode;
        foreach (var o in Items()) objectInstance.RedrawFull(o);
    }

    /// <summary>The item models in the scene, how many belong to no floor object of the level shown, and how many
    /// floor objects have none.</summary>
    (int all, int stray, int without) ModelCount()
    {
        var tree = (SceneTree)Engine.GetMainLoop();
        var items = Items();
        // owned: under any object's node of the level shown (a runestone is not a genericsprite but draws an icon too)
        var owned = UWTileMap.current_tilemap.LevelObjects.Where(o => o?.instance?.uwnode is Node3D n && GodotObject.IsInstanceValid(n))
            .SelectMany(o => o.instance.uwnode.GetChildren().OfType<Node3D>()).ToHashSet();
        // an item model, not a creature's: its mesh has the direct palette (itemmodel.cs Create)
        static bool IsItem(Node n) => n.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>()
            .Any(m => m.MaterialOverride is ShaderMaterial sm && (bool)sm.GetShaderParameter("palette_direct"));
        var nodes = tree.Root.FindChildren("model", "Node3D", true, false)
            .Where(n => GodotObject.IsInstanceValid(n) && !n.IsQueuedForDeletion() && IsItem(n)).ToList();
        foreach (var n in nodes.Where(n => !owned.Contains(n)).Take(3)) _strays.Add($"{n.GetParent()?.Name} at {((Node3D)n).GlobalPosition}");
        return (nodes.Count, nodes.Count(n => !owned.Contains(n)), items.Count(o => Model(o) == null));
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
            await PickUpAndDrop(c);
            await SaveRestore(c);
            await LevelChange(c);
        }
        finally
        {
            uwsettings.instance.enhancements.itemmodels = "off";
            uwsettings.instance.modelpath = _path0;
        }
    }

    uwObject _potion; Vector3 _spot;

    /// <summary>A potion on an empty square two squares ahead of the player, lit. Empty: with every icon a block, an
    /// object beside it on its square (a plant, the first time) stands in front of its model.</summary>
    async Task<uwObject> PotionAhead(ScenarioContext c)
    {
        var map = UWTileMap.current_tilemap; var p0 = playerdat.playerObject;
        bool Empty(int x, int y) => UWTileMap.ValidTile(x, y) && map.Tiles[x, y].tileType == UWTileMap.TILE_OPEN && map.Tiles[x, y].indexObjectList == 0;
        foreach (var (x, y) in Enumerable.Range(0, 64 * 64).Select(i => (x: i % 64, y: i / 64)).OrderBy(t => Math.Abs(t.x - p0.tileX) + Math.Abs(t.y - p0.tileY)).Take(400))
            for (int d = 0; d < 4; d++)
            {
                int x1 = x + GuardWalk.Dx[d], y1 = y + GuardWalk.Dy[d], x2 = x1 + GuardWalk.Dx[d], y2 = y1 + GuardWalk.Dy[d];
                if (!Empty(x, y) || !Empty(x1, y1) || !Empty(x2, y2) || map.Tiles[x2, y2].floorHeight != map.Tiles[x, y].floorHeight) continue;
                if (!await Guards.TeleportHere(c, x, y)) continue;
                var o = BatchB.SpawnStatic(Potion, x2, y2);
                if (o == null) return null;
                o.xpos = 3; o.ypos = 3; objectInstance.Reposition(o);
                playerdat.PlayerCameraYaw_dseg_8294 = GuardWalk.Yaw[d];
                playerdat.lightlevel = 4; playerdat.RefreshLighting();
                objectInstance.RedrawFull(o);
                await c.Frames(10);
                return o;
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
        _potion = await PotionAhead(c);
        if (_potion == null) { _problem = "no empty open run of two squares near the player for a potion"; return; }
        if (DisplayServer.GetName() == "headless")
        {
            SetMode("voxel"); await c.Frames(5);
            c.Note("headless: the click check needs a window (the scenario runner without --headless) and is skipped");
            return;
        }
        int sprite = Quad(_potion) is MeshInstance3D q ? PickAt(q.GlobalPosition) : -2;
        SetMode("voxel"); await c.Frames(5);
        var m = Model(_potion);
        int model = m == null ? -2 : PickAt(m.GlobalPosition + new Vector3(0, 0.1f, 0));   // the block's middle
        if (model > 0 && model != _potion.index && UWTileMap.current_tilemap.LevelObjects[model] is uwObject other)
            c.Note($"object {model} is item {other.item_id:x3} at ({other.tileX},{other.tileY}) z {other.zpos}, the potion at ({_potion.tileX},{_potion.tileY}) z {_potion.zpos}");
        c.Note($"potion {_potion.index}: the object-information view holds {sprite} at its sprite (switch off), {model} at its model (voxel)");
        if (sprite != _potion.index) { _problem = $"the sprite of potion {_potion.index} picks {sprite} with the switch off, so the probe is wrong"; return; }
        Check(model == _potion.index, model == -2 ? "Pick: the potion has no model with the switch on" : $"Pick: a click on potion {_potion.index}'s model picks {model}");
    }

    async Task PickUpAndDrop(ScenarioContext c)
    {
        if (_problem != null) return;
        SetMode("voxel");
        Check(Model(_potion) != null, "PickUpAndDrop: the potion has no model before it is picked up");
        _spot = Model(_potion)?.GlobalPosition ?? Vector3.Zero;
        pickup.PickUp(_potion.index, UWTileMap.current_tilemap.LevelObjects, true);
        await c.Frames(5);
        int held = playerdat.ObjectInHand;
        Check(held > 0, "PickUpAndDrop: the potion did not reach the hand");
        // a pick-up keeps the object's node and moves it to its new coordinate, off the map (pickup.DoPickup), sprite or model
        var where = Model(_potion)?.GlobalPosition;
        Check(where == null || where.Value.DistanceTo(_spot) > 1.2f, $"PickUpAndDrop: the picked-up potion's model was left on the floor at {where}");
        if (held <= 0) return;
        var inHand = UWTileMap.current_tilemap.LevelObjects[held];
        // a second red potion lying elsewhere already: the search below must find the dropped one, not this
        var decoy = BatchB.SpawnStatic(Potion, playerdat.playerObject.tileX, Math.Clamp(playerdat.playerObject.tileY - 6, 1, 62));
        if (decoy != null) objectInstance.RedrawFull(decoy);
        // the red potions lying on the map before the drop: the dropped one must be none of them
        var before = Items().Where(o => o.item_id == Potion && UWTileMap.ValidTile(o.tileX, o.tileY)).Select(o => o.index).ToHashSet();
        bool dropped = pickup.DropObjectByPlayer(inHand, false);
        if (dropped) { playerdat.ObjectInHand = -1; uimanager.instance.mousecursor.SetCursorToCursor(); }
        Check(dropped, "PickUpAndDrop: the potion could not be dropped");
        // it is thrown: wait until it lies still somewhere
        uwObject landed = null;
        for (int f = 0; f < 600 && landed == null; f++)
        {
            await c.Frames(1);
            landed = Items().FirstOrDefault(o => o.item_id == Potion && o.instance?.uwnode != null && o.IsStatic
                && UWTileMap.ValidTile(o.tileX, o.tileY) && !before.Contains(o.index));
        }
        await c.Frames(10);
        c.Note(landed == null ? "dropped potion: not found lying anywhere" : $"dropped potion: object {landed.index} at ({landed.tileX},{landed.tileY})");
        Check(landed == null || decoy == null || landed.index != decoy.index, "PickUpAndDrop: the search found the other red potion, not the dropped one");
        Check(landed != null && Model(landed) != null, "PickUpAndDrop: the dropped potion landed without a model");
        Check(landed == null || Quad(landed) == null, "PickUpAndDrop: the dropped potion shows its sprite as well");
    }

    async Task SaveRestore(ScenarioContext c)
    {
        if (_problem != null) return;
        SetMode("voxel");
        SaveGame.Save(SlotA, "items");
        uwsettings.instance.enhancements.itemmodels = "off";   // no frame between the two saves
        SaveGame.Save(SlotB, "items");
        string a = Path.Combine(UWClass.BasePath, $"SAVE{SlotA}"), b = Path.Combine(UWClass.BasePath, $"SAVE{SlotB}");
        var fa = Directory.GetFiles(a).Select(Path.GetFileName).OrderBy(x => x).ToArray();
        var fb = Directory.GetFiles(b).Select(Path.GetFileName).OrderBy(x => x).ToArray();
        var differ = fa.Where(f => fb.Contains(f) && !File.ReadAllBytes(Path.Combine(a, f)).SequenceEqual(File.ReadAllBytes(Path.Combine(b, f)))).ToList();
        c.Note($"saved with the switch on and off: {fa.Length} and {fb.Length} files, {differ.Count} differ {string.Join(",", differ)}");
        Check(fa.SequenceEqual(fb), $"SaveRestore: the saves have different files: {string.Join(",", fa)} against {string.Join(",", fb)}");
        Check(differ.Count == 0, $"SaveRestore: the save with models on differs in {string.Join(",", differ)}");

        SetMode("voxel");
        uimanager.instance.JourneyOnwards($"SAVE{SlotA}");
        if (!await Settled(c)) { _problem = "the game did not come back to play after the restore"; return; }
        await c.Frames(5);
        var (all, stray, without) = ModelCount();
        c.Note($"restored: {Items().Count} floor objects drawn, {without} without a model, {stray} of {all} item models without an object");
        Check(without == 0, $"SaveRestore: {without} floor objects have no model after the restore");
        Check(stray == 0, $"SaveRestore: {stray} item models without an object after the restore: {string.Join("; ", _strays)}");
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
            c.Note($"level {to}: {Items().Count} floor objects drawn, {without} without a model, {stray} of {all} item models without an object");
            Check(without == 0, $"LevelChange: {without} floor objects on level {to} have no model");
            Check(stray == 0, $"LevelChange: {stray} item models without an object of level {to}");
        }
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        return _fails.Count == 0 ? null : string.Join("; ", _fails.Take(6)) + (_fails.Count > 6 ? $" (and {_fails.Count - 6} more)" : "");
    }
}
