using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// Guard: picking an object up from the floor, putting it in the backpack, taking it out and
/// dropping it again moves it between the square and the inventory and keeps the weight right.
///
/// The original: picking up from the 3D view (player_3dget) checks the weight (EncumCheck) and
/// takes the object off its square into the cursor (release_3d, Obj_Rem) (uw1/src/ui/INTERACT.C:433-490);
/// the cursor object carries no weight until it is put in the inventory, where AddToInventory
/// adds ItemWeight (mass times quantity, from COMOBJ.DAT) to the carried weight
/// (uw1/src/inv/INVDATA.C:82-111, 469-483); taking it out again (invRemoveObject) takes that
/// weight off (INVDATA.C:251-293); and a drop from the cursor into the view (DoSpecialActions slot
/// 0x17, uw1/src/inv/BAGS.C:80-86; uw2 BAGS.C:70) is ReturnObject, which puts the object on the
/// square in front of the player when the pointer is low enough for a drop rather than a throw
/// (uw1/src/combat/MISSILE.C:169-233, uw2 MISSILE.C:195). The UW2 functions are the same
/// (uw2/src/ui/INTERACT.C, uw2/src/inv/INVDATA.C).
///
/// The port's calls are the ones its clicks make: pickup.PickUp for the click on the object,
/// uimanager.MoveObjectInHandOutOfOpenedContainer for the drop on the open backpack,
/// uimanager.PickupObjectFromSlot to take it out, pickup.DropObjectByPlayer for the click on the
/// view, with the pointer set at the bottom of the view (uimanager.ViewPortMouseYPos) so it is a
/// drop. The object is a dagger made on the player's square; its mass is read from the game's own
/// COMOBJ.DAT.
/// </summary>
public sealed class GuardPickupDrop : Scenario
{
    const int Dagger = 0x03;   // ITEM_DAGGER, uw1/src/include/items.h:150, uw2 items.h:135

    public override string Name => "guard-pickup-drop";
    public override string Cites => "underworld-exhumed uw1/src/ui/INTERACT.C:433-490 (player_3dget, release_3d), uw1/src/inv/INVDATA.C:82-111, 251-293, 469-483, uw1/src/combat/MISSILE.C:169-233 (ReturnObject); uw2 the same files";

    string _problem;
    int _index, _px, _py, _mass, _w0;
    bool _pickedOk, _offTile, _inHand, _inPack, _outOfPack, _dropped;
    int _wHand, _wPack, _wOut, _wDropped, _slot, _invIndex, _dropIndex, _dropItem, _facing = -1, _tried;
    (int x, int y) _dropAt = (-1, -1);

    public override Task Setup(ScenarioContext c)
    {
        var p = playerdat.playerObject;
        _px = p.tileX; _py = p.tileY;
        var o = ObjectCreator.spawnObjectInTile(Dagger, _px, _py, p.xpos, p.ypos, p.zpos);
        if (o == null) { _problem = "no dagger could be made"; return Task.CompletedTask; }
        _index = o.index;
        if (!Guards.InTileChain(_px, _py, _index)) { _problem = "the dagger is not on the player's square"; return Task.CompletedTask; }
        if (playerdat.ObjectInHand != -1) { _problem = "something is already in the hand"; return Task.CompletedTask; }
        _mass = Guards.Mass(Dagger);
        _w0 = playerdat.WeightCarried;
        if (_mass <= 0 || _w0 + _mass > playerdat.WeightMax) _problem = $"the dagger's mass {_mass} does not suit the test (carrying {_w0} of {playerdat.WeightMax})";
        return Task.CompletedTask;
    }

    public override async Task Act(ScenarioContext c)
    {
        if (_problem != null) return;
        var objects = UWTileMap.current_tilemap.LevelObjects;

        // The click on the dagger in the view.
        _pickedOk = pickup.PickUp(_index, objects, WorldObject: true);
        await c.Frames(1);
        _offTile = !Guards.InTileChain(_px, _py, _index);
        _inHand = playerdat.ObjectInHand == _index;
        _wHand = playerdat.WeightCarried;
        if (!_inHand) return;

        // The drop on the open backpack.
        _slot = uimanager.FreePaperDollSlot;
        uimanager.MoveObjectInHandOutOfOpenedContainer(playerdat.ObjectInHand);
        await c.Frames(1);
        _invIndex = _slot == -1 ? 0 : playerdat.GetInventorySlotListHead(_slot);
        var packed = _invIndex == 0 ? null : playerdat.InventoryObjects[_invIndex];
        _inPack = packed != null && packed.item_id == Dagger && playerdat.ObjectInHand == -1;
        _wPack = playerdat.WeightCarried;
        if (!_inPack) return;

        // The click on the dagger in the backpack picks it up again.
        uimanager.CurrentSlot = _slot;
        uimanager.PickupObjectFromSlot(packed);
        await c.Frames(1);
        _outOfPack = playerdat.GetInventorySlotListHead(_slot) == 0 && playerdat.ObjectInHand > 0;
        _wOut = playerdat.WeightCarried;
        if (!_outOfPack) return;

        // The click low in the view drops it. ReturnObject drops in front of the player and
        // refuses ("There is no space to drop that") unless both its spots fit
        // (uw1 MISSILE.C:200-212), so the player turns to each open square of the same floor
        // height in turn (headings 0, 2, 4, 6) until a drop is taken.
        var pl = playerdat.playerObject;
        var map = UWTileMap.current_tilemap;
        int[] hx = { 0, 1, 0, -1 }, hy = { 1, 0, -1, 0 };   // north, east, south, west
        var before = new System.Collections.Generic.HashSet<int>(DaggersNear(pl.tileX, pl.tileY).ConvertAll(d => d.index));
        _dropIndex = playerdat.ObjectInHand;
        uimanager.ViewPortMouseXPos = 4 * (uimanager.Window3DLeftBorder + uimanager.Window3DMaxX / 2);
        uimanager.ViewPortMouseYPos = 4 * 200;   // the bottom of the view: a drop, not a throw (motion.InitPlayerProjectileValues)
        for (int h = 0; h < 4 && !_dropped; h++)
        {
            int tx = pl.tileX + hx[h], ty = pl.tileY + hy[h];
            if (!UWTileMap.ValidTile(tx, ty) || map.Tiles[tx, ty].tileType != UWTileMap.TILE_OPEN
                || map.Tiles[tx, ty].floorHeight != map.Tiles[pl.tileX, pl.tileY].floorHeight) continue;
            _facing = h * 2;
            _tried++;
            pl.heading = (short)_facing;
            pl.npc_heading = 0;
            _dropped = pickup.DropObjectByPlayer(objects[_dropIndex], false);
        }
        if (_dropped) { playerdat.ObjectInHand = -1; uimanager.instance.mousecursor.SetCursorToCursor(); }
        await c.Frames(30);   // a dropped object can fall to the floor first
        foreach (var d in DaggersNear(pl.tileX, pl.tileY))
            if (!before.Contains(d.index)) { _dropAt = d.at; _dropItem = Dagger; }
        _wDropped = playerdat.WeightCarried;
        c.Note($"dagger mass {_mass}; weight {_w0} start, {_wHand} in hand, {_wPack} in pack slot {_slot}, {_wOut} out, {_wDropped} dropped; facing {_facing} after {_tried} tries, dropped at ({_dropAt.x}, {_dropAt.y}) from the player's ({pl.tileX}, {pl.tileY})");
    }

    /// <summary>The daggers in the object chains of the squares within 1 of (x, y), with their squares.</summary>
    static System.Collections.Generic.List<(int index, (int x, int y) at)> DaggersNear(int x, int y)
    {
        var found = new System.Collections.Generic.List<(int, (int, int))>();
        var objects = UWTileMap.current_tilemap.LevelObjects;
        for (int yy = y - 1; yy <= y + 1; yy++)
            for (int xx = x - 1; xx <= x + 1; xx++)
            {
                if (!UWTileMap.ValidTile(xx, yy)) continue;
                int i = UWTileMap.current_tilemap.Tiles[xx, yy].indexObjectList;
                for (int g = 0; i != 0 && g < 1024; g++)
                {
                    if (objects[i].item_id == Dagger) found.Add((i, (xx, yy)));
                    i = objects[i].next;
                }
            }
        return found;
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        var k = new Checks();
        k.True(_pickedOk && _offTile && _inHand, $"pick up: expected the dagger off its square and in the hand, got pickup {_pickedOk}, off the square {_offTile}, in hand {_inHand} (player_3dget, release_3d: uw1 INTERACT.C:433-490)");
        if (!_inHand) return k.Result;
        k.Equal("weight with the dagger in the hand", _w0, _wHand, "the cursor object weighs nothing until it is put away: uw1 INTERACT.C:459-470, INVDATA.C:107");
        k.True(_inPack, $"put away: expected the dagger in backpack slot {_slot}, got slot holding {_invIndex}");
        if (!_inPack) return k.Result;
        k.Equal("weight with the dagger in the backpack", _w0 + _mass, _wPack, "AddToInventory adds ItemWeight: uw1 INVDATA.C:97, 107, 469-483");
        k.True(_outOfPack, "take out: expected the backpack slot empty and the dagger in the hand");
        if (!_outOfPack) return k.Result;
        k.Equal("weight after taking the dagger out", _w0, _wOut, "invRemoveObject takes ItemWeight off: uw1 INVDATA.C:251-293");
        if (_tried == 0) return "precondition: no open square of the same floor height beside the player to drop towards; " + (k.Result ?? "");
        k.True(_dropped, $"drop: DropObjectByPlayer refused facing each of {_tried} open squares (ReturnObject puts it in front of the player: uw1 MISSILE.C:200-232)");
        k.True(_dropAt.x >= 0 && _dropItem == Dagger, $"drop: expected a new dagger in a square's object chain within 1 of the player, found one at ({_dropAt.x}, {_dropAt.y}) as item 0x{_dropItem:X} (uw1 MISSILE.C:200-232)");
        k.Equal("weight after the drop", _w0, _wDropped, "a dropped object is no longer carried: uw1 BAGS.C:80-87");
        k.Equal("object in the hand after the drop", -1, playerdat.ObjectInHand, "CursorObjPtr = 0: uw1 BAGS.C:84");
        return k.Result;
    }
}
