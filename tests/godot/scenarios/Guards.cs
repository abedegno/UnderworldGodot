using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using Godot;

namespace Underworld.Scenarios;

/// <summary>
/// Helpers for the guard scenarios (batch D): the original's data tables read straight from the
/// game's own files, so an expected value never comes from the port's loaders; putting an object
/// in a free backpack slot the way the inventory does; and reaching the port's private statics by
/// name. A private member that a fix renames makes the scenario throw MissingMemberException,
/// which KNOWN_FAILURES never accepts, so the rename is noticed rather than hidden.
/// </summary>
static class Guards
{
    const BindingFlags Statics = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

    public static T Get<T>(Type t, string name)
    {
        var f = t.GetField(name, Statics);
        if (f != null) return (T)f.GetValue(null);
        var p = t.GetProperty(name, Statics);
        if (p != null) return (T)p.GetValue(null);
        throw new MissingMemberException(t.Name, name);
    }

    public static void Set(Type t, string name, object value)
    {
        var f = t.GetField(name, Statics);
        if (f != null) { f.SetValue(null, value); return; }
        var p = t.GetProperty(name, Statics);
        if (p != null) { p.SetValue(null, value); return; }
        throw new MissingMemberException(t.Name, name);
    }

    /// <summary>Calls a static method by name, rethrowing what it throws as itself.</summary>
    public static object Call(Type t, string name, params object[] args)
    {
        var m = t.GetMethod(name, Statics) ?? throw new MissingMethodException(t.Name, name);
        try { return m.Invoke(null, args); }
        catch (TargetInvocationException e) when (e.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(e.InnerException).Throw();
            throw;
        }
    }

    static byte[] ReadData(string file) => File.ReadAllBytes(Path.Combine(UWClass.BasePath, "DATA", file));

    /// <summary>
    /// OBJECTS.DAT as init_objects reads it (uw1/src/obj/OBJCLASS.C:37-54, uw2 OBJCLASS.C:36-48): a
    /// 2-byte header, then hack_init's weapons (16 x 8), missiles (16 x 3) and armour (32 x 4)
    /// (uw1 HACK.C:26-28), creature_init's 64 creatures of 0x30 (uw1 critter/CREATURE.C:44, uw2
    /// CREATURE.C:43), then misc_init's containers (16 x 3), lights (16 x 2) and food (16 x 1)
    /// (uw1 obj/MISC.C:25-27, uw2 MISC.C:24-26).
    /// </summary>
    const int ObjDatLights = 2 + 16 * 8 + 16 * 3 + 32 * 4 + 64 * 0x30 + 16 * 3;
    const int ObjDatFood = ObjDatLights + 16 * 2;

    /// <summary>Food[inclass], the nourishment UseFood gives class 0x0B food (uw1 USEITEMS.C:615-616, uw2 USEITEMS.C:217-218). Signed: negative is a drink.</summary>
    public static int FoodNutrition(int item) => (sbyte)ReadData("OBJECTS.DAT")[ObjDatFood + (item & 0xF)];

    /// <summary>Lights[inclass].duration, the burn rate DegradeLights uses (uw1 game/PLAYTIME.C:166, uw2 PLAYTIME.C:192).</summary>
    public static int LightBurnRate(int item) => ReadData("OBJECTS.DAT")[ObjDatLights + (item & 0xF) * 2];

    /// <summary>The second byte of a light's entry, which FixPlayerEquips takes as its brightness (data[1], uw1 game/PLAYDATA.C:434-437, uw2 PLAYDATA.C:394-397).</summary>
    public static int LightBrightness(int item) => ReadData("OBJECTS.DAT")[ObjDatLights + (item & 0xF) * 2 + 1];

    /// <summary>
    /// ComObjData[item].mass in tenths of a stone: COMOBJ.DAT is a 2-byte header then 11-byte
    /// records (uw1 obj/OBJCLASS.C:59-60), mass the 12 bits after height (8), radius (3) and
    /// animated (1) (uw1 include/object.h:30-34).
    /// </summary>
    public static int Mass(int item)
    {
        byte[] d = ReadData("COMOBJ.DAT");
        int o = 2 + item * 11;
        return (d[o + 1] | d[o + 2] << 8) >> 4;
    }

    /// <summary>
    /// Makes item in the hand and puts it in the first free backpack slot, as the inventory does
    /// when an object in the hand is dropped on the open backpack
    /// (uimanager.MoveObjectInHandOutOfOpenedContainer). Returns the inventory index and slot, or
    /// (0, -1) on failure.
    /// </summary>
    public static (int index, int slot) PutInBackpack(int item, Action<uwObject> prepare = null)
    {
        int world = ObjectCreator.SpawnObjectInHand(item);
        if (world == 0) return (0, -1);
        prepare?.Invoke(UWTileMap.current_tilemap.LevelObjects[world]);
        int slot = uimanager.FreePaperDollSlot;
        if (slot == -1) return (0, -1);
        int index = playerdat.AddObjectToPlayerInventory(world, false);
        playerdat.SetInventorySlotListHead(slot, index);
        playerdat.ObjectInHand = -1;
        uimanager.instance.mousecursor.SetCursorToCursor();
        uimanager.UpdateInventoryDisplay();
        return (index, slot);
    }

    /// <summary>Whether object index is in the object chain of tile (x, y).</summary>
    public static bool InTileChain(int x, int y, int index)
    {
        if (!UWTileMap.ValidTile(x, y)) return false;
        var objects = UWTileMap.current_tilemap.LevelObjects;
        int i = UWTileMap.current_tilemap.Tiles[x, y].indexObjectList;
        for (int guard = 0; i != 0 && guard < 1024; guard++)
        {
            if (i == index) return true;
            i = objects[i].next;
        }
        return false;
    }

    /// <summary>The square whose object chain holds index, within r squares of (x, y), or (-1, -1).</summary>
    public static (int x, int y) FindInChains(int x, int y, int r, int index)
    {
        for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
                if (InTileChain(x + dx, y + dy, index)) return (x + dx, y + dy);
        return (-1, -1);
    }

    /// <summary>
    /// Moves the player to square (x, y) of this level the game's way (Teleportation.Teleport, as
    /// a teleport trap within the level does) and waits until he is there and the game is back in
    /// play. True if he arrived.
    /// </summary>
    public static async Task<bool> TeleportHere(ScenarioContext c, int x, int y)
    {
        Teleportation.Teleport(character: 0, tileX: x, tileY: y, newLevel: 0, HeadingHeightFlag: 0);
        ulong deadline = Time.GetTicksMsec() + 20000;
        int stable = 0;
        while (Time.GetTicksMsec() < deadline && stable < 10)
        {
            await c.Frames(1);
            var p = playerdat.playerObject;
            stable = (p.tileX == x && p.tileY == y && !uimanager.WaitingForFade && uimanager.InGame) ? stable + 1 : 0;
        }
        return stable >= 10;
    }

    /// <summary>Presses (or releases) a key as the keyboard does, through Godot's input.</summary>
    public static void Key(Godot.Key key, bool pressed)
    {
        if (pressed) held.Add(key); else held.Remove(key);
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed });
    }

    static readonly HashSet<Godot.Key> held = new();

    /// <summary>Releases every key Key left pressed, so a scenario that threw mid-press does not hold it for the next.</summary>
    public static void ReleaseHeldKeys()
    {
        foreach (var key in held.ToArray()) Key(key, false);
    }

    /// <summary>
    /// The port's AI and animations run only near playerdat.CameraTileX/Y, which a headless new
    /// game sets only after a few frames: lets them pass and, if the camera is still not on the
    /// player's square, positions it there (playerdat.PositionPlayerCamera).
    /// </summary>
    public static async Task<string> CameraOnPlayer(ScenarioContext c)
    {
        var p = playerdat.playerObject;
        await c.Frames(3);
        if (playerdat.CameraTileX == p.tileX && playerdat.CameraTileY == p.tileY) return "camera already on the player";
        playerdat.PositionPlayerCamera(false);
        if (playerdat.CameraTileX == p.tileX && playerdat.CameraTileY == p.tileY) return "camera positioned on the player";
        playerdat.CameraTileX = (short)p.tileX;
        playerdat.CameraTileY = (short)p.tileY;
        return "camera square set to the player's";
    }
}
