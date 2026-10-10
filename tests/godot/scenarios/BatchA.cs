using System;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace Underworld.Scenarios;

/// <summary>
/// Helpers shared by the combat and critter scenarios: making a critter beside the player, picking
/// a creature type by its CRITTER data, and reaching the port's private statics by name. A
/// private member that a fix renames makes the scenario fail with a MissingMemberException, which
/// KNOWN_FAILURES never accepts, so the rename is noticed rather than hidden.
/// </summary>
static class BatchA
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

    /// <summary>
    /// Makes a critter of item id in the square (dx, dy) from the player's, standing (goal 0),
    /// mellow (attitude 2), with its home and position in that square and at least 40 hit points.
    /// Null if no mobile slot was free or the square is off the map.
    /// </summary>
    public static uwObject SpawnCritter(int item, int dx = 0, int dy = 0, short xpos = 3, short ypos = 3)
    {
        var p = playerdat.playerObject;
        int tx = p.tileX + dx, ty = p.tileY + dy;
        if (!UWTileMap.ValidTile(tx, ty)) return null;
        var o = ObjectCreator.spawnObjectInTile(
            itemid: item, tileX: tx, tileY: ty, xpos: xpos, ypos: ypos, zpos: p.zpos,
            WhichList: ObjectFreeLists.ObjectListType.MobileList, RenderImmediately: false);
        if (o == null) return null;
        ObjectCreator.InitialiseCritter(o);
        o.npc_xhome = (short)tx;
        o.npc_yhome = (short)ty;
        o.quality = (short)tx;
        o.owner = (short)ty;
        o.npc_goal = 0;
        o.npc_gtarg = 0;
        o.npc_attitude = 2;
        o.IsAlly = 0;
        o.npc_hp = 0xC8;
        o.npc_whoami = 0;
        return o;
    }

    /// <summary>
    /// The port's AI runs a critter only within 10 squares of playerdat.CameraTileX/Y
    /// (src/npc/npcai.cs:226-233), which the camera sets when it is positioned. A new game in the
    /// headless runner has not positioned it, so this lets a few frames pass and, if the camera's
    /// square is still not the player's, positions the camera on the player
    /// (playerdat.PositionPlayerCamera); failing that it sets the square as the camera would. The
    /// result says which.
    /// </summary>
    public static async System.Threading.Tasks.Task<string> CameraOnPlayer(ScenarioContext c)
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

    /// <summary>The first creature (0x40..0x7E) that suits, or -1.</summary>
    public static int PickCritter(Func<int, bool> suits)
    {
        for (int id = 0x40; id < 0x7F; id++)
            if (suits(id)) return id;
        return -1;
    }

    /// <summary>A plain walking creature: not a flier or swimmer, not passive.</summary>
    public static bool Walker(int id) =>
        critterObjectDat.avghit(id) > 0 && !critterObjectDat.isFlier(id) && !critterObjectDat.isSwimmer(id)
        && !critterObjectDat.unkPassivenessProperty(id);
}
