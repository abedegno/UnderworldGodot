using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// Splitting a stack on pickup when no object can be had (UW1 and UW2). The original's AskHowMany
/// asks how many, and for fewer than the whole stack takes a new object with Obj_Alloc(0), copies
/// the stack into it, gives the copy the rest and cuts the stack to the number asked
/// (uw1/src/inv/INVPANEL.C:486-489, uw2/src/inv/INVPANEL.C:473-476). It does not check
/// Obj_Alloc, which returns 0 when the static free list is still empty after Obj_GarbageCollect(3, 10)
/// (uw1/src/obj/OBJECTS.C:239-243, uw2/src/obj/OBJECTS.C:232-236). The copy then goes through a null
/// pointer, the stack is still cut to the number asked, and AskHowMany returns 0, which its callers
/// take as a cancel: nothing is picked up and no message is shown, from the map (player_3dget,
/// uw1/src/ui/INTERACT.C:457-458, uw2/src/ui/INTERACT.C:360-361) and from the inventory
/// (uw1/src/inv/INVPANEL.C:229-230, uw2/src/inv/INVPANEL.C:206-207). The rest of the stack is lost.
///
/// The port's DoPickupQty (src/interaction/pickup.cs, src/ui/uimanager_inventory.cs) used
/// SpawnObjectInHand's slot without checking it for 0, so it copied into level object 0 and gave
/// the rest back to the stack.
///
/// The scenario puts 10 coins on the player's tile and 10 in the backpack. It makes the static free
/// list empty and takes every tile's list that Obj_GarbageCollect(3, 10) would scan (more than 7
/// tiles from the player's home tile) off the map for the pickup, so the allocation fails. It
/// answers 3 to "Move how many?" for each stack, through the port's DoPickupQty. Each stack must
/// then still hold all 10 and stay where it was, the hand must stay empty, and object 0 must be unchanged.
/// DOS would leave 3 and lose the rest; the port keeps the stack whole on purpose (an original bug not copied).
/// </summary>
public sealed class PickupSplitFull : Scenario
{
    const int Count = 10, Asked = 3;

    public override string Name => "pickup-split-full";
    public override string Cites => "underworld-exhumed uw1/src/inv/INVPANEL.C:229-230, 486-489, uw1/src/ui/INTERACT.C:457-458; uw2/src/inv/INVPANEL.C:206-207, 473-476, uw2/src/ui/INTERACT.C:360-361";

    string _problem;
    int _px, _py, _worldIndex, _invIndex;
    bool _allocFailed;
    int _worldQty, _invQty, _handWorld, _handInv;
    bool _worldOnTile, _invInSlot, _object0Same;

    static uwObject[] Objs => UWTileMap.current_tilemap.LevelObjects;

    public override Task Setup(ScenarioContext c)
    {
        _px = playerdat.playerObject.npc_xhome;
        _py = playerdat.playerObject.npc_yhome;
        if (ConvHelp.PlayerCoins() != 0) { _problem = "the new game's player already carries coins"; return Task.CompletedTask; }
        _invIndex = ConvHelp.CoinsInBackpack(Count);
        if (_invIndex < 0) { _problem = "no coins could be put in the backpack"; return Task.CompletedTask; }

        _worldIndex = ObjectCreator.PrepareNewObject(ConvHelp.Coin);
        if (_worldIndex == 0) { _problem = "no coins could be made on the map"; return Task.CompletedTask; }
        var o = Objs[_worldIndex];
        var tile = UWTileMap.current_tilemap.Tiles[_px, _py];
        o.is_quant = 1;
        o.link = Count;
        o.tileX = _px; o.tileY = _py;
        o.next = tile.indexObjectList;
        tile.indexObjectList = (short)_worldIndex;
        return Task.CompletedTask;
    }

    public override Task Act(ScenarioContext c)
    {
        if (_problem != null) return Task.CompletedTask;
        var m = UWTileMap.current_tilemap;
        int savedPtr = m.StaticFreeListPtr;
        var hidden = new List<(int x, int y, short head)>();
        var object0 = new byte[8];
        System.Array.Copy(Objs[0].DataBuffer, Objs[0].PTR, object0, 0, 8);
        try
        {
            // Nothing free, and nothing Obj_GarbageCollect(3, 10) could free.
            m.StaticFreeListPtr = -1;
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                    if (System.Math.Abs(_px - x) + System.Math.Abs(_py - y) > 7 && m.Tiles[x, y].indexObjectList != 0)
                    {
                        hidden.Add((x, y, m.Tiles[x, y].indexObjectList));
                        m.Tiles[x, y].indexObjectList = 0;
                    }
            _allocFailed = ObjectFreeLists.GetAvailableObjectSlot() == 0 && m.StaticFreeListPtr == -1;
            if (!_allocFailed) { _problem = "an object could still be allocated"; return Task.CompletedTask; }

            playerdat.ObjectInHand = -1;
            Answer(pickup.DoPickupQty(_worldIndex, Objs, Objs[_worldIndex]));
            _handWorld = playerdat.ObjectInHand;
            _worldQty = Objs[_worldIndex].link;
            _worldOnTile = ConvHelp.OnMapNear(_worldIndex, _px, _py, 0, out _, out _);

            playerdat.ObjectInHand = -1;
            Answer(uimanager.DoPickupQty(playerdat.InventoryObjects[_invIndex], uimanager.CurrentSlot));
            _handInv = playerdat.ObjectInHand;
            _invQty = playerdat.InventoryObjects[_invIndex].link;
            _invInSlot = ConvHelp.PlayerCoins() == _invQty;

            var now = new byte[8];
            System.Array.Copy(Objs[0].DataBuffer, Objs[0].PTR, now, 0, 8);
            _object0Same = System.Linq.Enumerable.SequenceEqual(object0, now);
            c.Note($"map stack {_worldQty}, on its tile {_worldOnTile}, hand {_handWorld}; backpack stack {_invQty}, hand {_handInv}; object 0 unchanged {_object0Same}");
        }
        finally
        {
            foreach (var (x, y, head) in hidden) m.Tiles[x, y].indexObjectList = head;
            m.StaticFreeListPtr = savedPtr;
            if (playerdat.ObjectInHand > 0) { playerdat.ObjectInHand = -1; uimanager.instance.mousecursor.SetCursorToCursor(); }
            MessageDisplay.WaitingForTypedInput = false;
            // a new game does not clear the inventory, so the next scenario would find these coins
            if (playerdat.InventoryObjects[_invIndex] != null) playerdat.RemoveFromInventory(_invIndex);
        }
        return Task.CompletedTask;
    }

    /// <summary>Runs a DoPickupQty coroutine to the end, answering the "Move how many?" prompt with Asked.</summary>
    static void Answer(IEnumerator e)
    {
        for (int guard = 0; guard < 16 && e.MoveNext(); guard++)
            if (MessageDisplay.WaitingForTypedInput)
            {
                uimanager.instance.TypedInput.Text = Asked.ToString();
                MessageDisplay.WaitingForTypedInput = false;
            }
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        bool uw2 = c.Game == "uw2";
        string split = uw2 ? "uw2 INVPANEL.C:473-476" : "uw1 INVPANEL.C:486-489";
        string get = uw2 ? "uw2 INTERACT.C:360-361" : "uw1 INTERACT.C:457-458";
        string press = uw2 ? "uw2 INVPANEL.C:206-207" : "uw1 INVPANEL.C:229-230";
        var k = new Checks();
        k.Equal("coins left in the map stack (kept whole; DOS loses all but the number asked)", Count, _worldQty, split);
        k.True(_worldOnTile, $"the map stack left the player's tile ({get})");
        k.Equal("object in hand after the map pickup", -1, _handWorld, get);
        k.Equal("coins left in the backpack stack (kept whole; DOS loses all but the number asked)", Count, _invQty, split);
        k.True(_invInSlot, $"the player carries other coins than the backpack stack ({press})");
        k.Equal("object in hand after the backpack pickup", -1, _handInv, press);
        k.True(_object0Same, $"object 0 was written ({split})");
        return k.Result;
    }
}
