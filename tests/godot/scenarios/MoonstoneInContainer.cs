using System.Reflection;
using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// A moonstone carried in a container still updates the level Gate Travel goes to (UW1 and UW2).
/// The original tests the object picked up from the 3D view, and the object dropped from the
/// cursor into the world, with HasOrIsObj, which is true for a moonstone and for an object holding
/// one anywhere inside it (uw1/src/obj/OBJECTS.C:490-502; uw2/src/obj/OBJECTS.C:475-487). Picking
/// one up clears UW1's moonstone level (uw1/src/ui/INTERACT.C:476-477) or the first of UW2's two
/// that holds this level (uw2/src/ui/INTERACT.C:377-382); dropping one sets UW1's to this level
/// (uw1/src/inv/BAGS.C:81-83) or the first free one of UW2's (uw2/src/inv/BAGS.C:70-78).
///
/// The port tested only whether the object itself was a moonstone (src/interaction/pickup.cs,
/// PickupSpecialCases and DropSpecialCases).
///
/// The scenario puts a sack holding a moonstone on the player's square, sets the (first) moonstone
/// level to this level, clicks the sack in pickup mode (uimanager.InteractWithObjectCollider, the
/// call the view's click makes), clears the moonstone level again, then drops it from the hand into the world
/// (uimanager.DropHeldItemIntoWorld, the call the paperdoll's and the main view's drop makes),
/// turning the player to each open square of the same floor height until the drop is taken.
/// </summary>
public sealed class MoonstoneInContainer : Scenario
{
    const int Sack = 0x80;       // ITEM_SACK, uw1/src/include/items.h:273, uw2 items.h:266
    const int Moonstone = 0x126; // ITEM_MOONSTONE, uw1/src/include/items.h:457, uw2 items.h:448

    public override string Name => "moonstone-in-container";
    public override string Cites => "underworld-exhumed uw1/src/obj/OBJECTS.C:490-502, uw1/src/ui/INTERACT.C:476-477, uw1/src/inv/BAGS.C:81-83; uw2/src/obj/OBJECTS.C:475-487, uw2/src/ui/INTERACT.C:377-382, uw2/src/inv/BAGS.C:70-78";

    string _problem;
    int _sack, _level, _afterPickup = -1, _afterDrop = -1, _tried;
    bool _held, _dropped;

    public override Task Setup(ScenarioContext c)
    {
        var p = playerdat.playerObject;
        var tile = UWTileMap.current_tilemap.Tiles[p.tileX, p.tileY];
        var m = ObjectCreator.spawnObjectInTile(Moonstone, p.tileX, p.tileY, p.xpos, p.ypos, p.zpos, RenderImmediately: false);
        var s = ObjectCreator.spawnObjectInTile(Sack, p.tileX, p.tileY, p.xpos, p.ypos, p.zpos);
        if (m == null || s == null) { _problem = "no sack or moonstone could be made"; return Task.CompletedTask; }
        if (tile.indexObjectList != s.index || s.next != m.index) { _problem = "the sack and moonstone are not at the head of the square's chain"; return Task.CompletedTask; }
        // Put the moonstone in the sack.
        s.next = m.next;
        s.link = m.index;
        m.next = 0;
        _sack = s.index;
        _level = playerdat.dungeon_level;
        playerdat.SetMoonstone(0, _level);
        if (UWClass._RES == UWClass.GAME_UW2) playerdat.SetMoonstone(1, 0);
        if (playerdat.ObjectInHand != -1) _problem = "something is already in the hand";
        return Task.CompletedTask;
    }

    public override async Task Act(ScenarioContext c)
    {
        if (_problem != null) return;
        var mode = uimanager.InteractionMode;
        uimanager.InteractionMode = uimanager.InteractionModes.ModePickup;
        uimanager.InteractWithObjectCollider(_sack, true);
        uimanager.InteractionMode = mode;
        await c.Frames(1);
        _held = playerdat.ObjectInHand == _sack;
        _afterPickup = playerdat.GetMoonstone(0);
        if (!_held) return;
        // Clear it whatever the pickup did, so the drop is measured on its own.
        playerdat.SetMoonstone(0, 0);
        if (UWClass._RES == UWClass.GAME_UW2) playerdat.SetMoonstone(1, 0);

        var drop = typeof(uimanager).GetMethod("DropHeldItemIntoWorld", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        if (drop == null) { _problem = "uimanager.DropHeldItemIntoWorld not found"; return; }
        var pl = playerdat.playerObject;
        var map = UWTileMap.current_tilemap;
        int[] hx = { 0, 1, 0, -1 }, hy = { 1, 0, -1, 0 };   // north, east, south, west
        for (int h = 0; h < 4 && !_dropped; h++)
        {
            int tx = pl.tileX + hx[h], ty = pl.tileY + hy[h];
            if (!UWTileMap.ValidTile(tx, ty) || map.Tiles[tx, ty].tileType != UWTileMap.TILE_OPEN
                || map.Tiles[tx, ty].floorHeight != map.Tiles[pl.tileX, pl.tileY].floorHeight) continue;
            _tried++;
            pl.heading = (short)(h * 2);
            pl.npc_heading = 0;
            drop.Invoke(null, null);
            _dropped = playerdat.ObjectInHand == -1;
        }
        await c.Frames(1);
        _afterDrop = playerdat.GetMoonstone(0);
        c.Note($"level {_level}; moonstone level {_afterPickup} after the pickup, {_afterDrop} after the drop ({_tried} headings tried)");
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        if (!_held) return "precondition: the click did not put the sack in the hand";
        string pick = c.Game == "uw1" ? "uw1 INTERACT.C:476-477" : "uw2 INTERACT.C:377-382";
        string put = c.Game == "uw1" ? "uw1 BAGS.C:81-83" : "uw2 BAGS.C:70-78";
        var k = new Checks();
        k.Equal("moonstone level after picking up a sack holding a moonstone", 0, _afterPickup, $"HasOrIsObj: {pick}");
        if (_tried == 0) return "precondition: no open square of the same floor height beside the player to drop towards; " + (k.Result ?? "");
        if (!_dropped) return $"precondition: the drop was refused facing each of {_tried} open squares; " + (k.Result ?? "");
        k.Equal("moonstone level after dropping a sack holding a moonstone", _level, _afterDrop, $"HasOrIsObj: {put}");
        return k.Result;
    }
}
