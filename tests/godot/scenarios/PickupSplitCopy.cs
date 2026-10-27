using System.Collections;
using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// Picking up part of a stack from the map (UW1 and UW2). The original's AskHowMany takes a new
/// object with Obj_Alloc(0), copies the whole stack into it, gives the copy the rest and cuts the
/// stack itself to the number asked (uw1/src/inv/INVPANEL.C:486-489, uw2/src/inv/INVPANEL.C:473-476).
/// player_3dget puts the copy next in the tile's list after the stack (Obj_Add, uw1/src/ui/INTERACT.C:460,
/// uw2/src/ui/INTERACT.C:363) and then picks up the stack itself as for any object: player_grabbed
/// (owners notice and the object becomes nobody's, uw1/src/critter/CRITTIME.C:470-483,
/// uw2/src/critter/CRITTIME.C:535-549), release_3d (pick-up triggers, off the tile) and
/// DoInventoryDrag (uw1/src/ui/INTERACT.C:476-481, uw2/src/ui/INTERACT.C:370-387). So the original
/// object, cut to the number asked, goes to the hand, and the copy with the rest, holding all of the
/// stack's own values, stays on the map.
///
/// The port's DoPickupQty (src/interaction/pickup.cs) made a new object in the hand and copied it
/// onto itself (it read the source bytes at the new object's offset), so the part picked up kept
/// PrepareNewObject's defaults, and it never ran DoPickup, so the part picked up skipped the
/// theft check and the other pick-up handling. The stack stayed on the map with the rest.
///
/// The scenario puts 10 coins with set values (quality, owner, flags, position in the tile and so
/// on) on the player's tile and answers 3 to "Move how many?" through the port's DoPickupQty.
/// </summary>
public sealed class PickupSplitCopy : Scenario
{
    const int Count = 10, Asked = 3;
    const int Quality = 0x15, Owner = 5, Flags = 2, Doordir = 1, Xpos = 5, Ypos = 2, Heading = 3;

    public override string Name => "pickup-split-copy";
    public override string Cites => "underworld-exhumed uw1/src/inv/INVPANEL.C:486-489, uw1/src/ui/INTERACT.C:457-481, uw1/src/critter/CRITTIME.C:470-483; uw2/src/inv/INVPANEL.C:473-476, uw2/src/ui/INTERACT.C:360-387, uw2/src/critter/CRITTIME.C:535-549";

    string _problem;
    int _px, _py, _worldIndex, _zpos;
    bool _canOwn;
    int _hand, _copy;
    bool _stackOnTile;
    // what the hand and the map copy hold afterwards
    int[] _handVals, _copyVals;

    static uwObject[] Objs => UWTileMap.current_tilemap.LevelObjects;

    static int[] Values(uwObject o) => new int[] { o.item_id, o.quality, o.owner, o.flags, o.doordir, o.invis, o.xpos, o.ypos, o.zpos, o.heading, o.enchantment, o.is_quant, o.link };
    static readonly string[] Names = { "item", "quality", "owner", "flags", "doordir", "invis", "xpos", "ypos", "zpos", "heading", "enchantment", "is_quant", "quantity" };

    public override Task Setup(ScenarioContext c)
    {
        _px = playerdat.playerObject.npc_xhome;
        _py = playerdat.playerObject.npc_yhome;
        _worldIndex = ObjectCreator.PrepareNewObject(ConvHelp.Coin);
        if (_worldIndex == 0) { _problem = "no coins could be made on the map"; return Task.CompletedTask; }
        var o = Objs[_worldIndex];
        var tile = UWTileMap.current_tilemap.Tiles[_px, _py];
        o.is_quant = 1;
        o.link = Count;
        o.quality = Quality; o.owner = Owner; o.flags = Flags; o.doordir = Doordir;
        o.xpos = Xpos; o.ypos = Ypos; o.heading = Heading;
        o.zpos = (short)(tile.floorHeight << 3);
        _zpos = o.zpos;
        o.tileX = _px; o.tileY = _py;
        o.next = tile.indexObjectList;
        tile.indexObjectList = (short)_worldIndex;
        ObjectCreator.RenderObject(o, UWTileMap.current_tilemap);
        _canOwn = commonObjDat.canhaveowner(ConvHelp.Coin);
        return Task.CompletedTask;
    }

    public override Task Act(ScenarioContext c)
    {
        if (_problem != null) return Task.CompletedTask;
        try
        {
            playerdat.ObjectInHand = -1;
            Answer(pickup.DoPickupQty(_worldIndex, Objs, Objs[_worldIndex]));
            _hand = playerdat.ObjectInHand;
            _stackOnTile = ConvHelp.OnMapNear(_worldIndex, _px, _py, 0, out _, out _);
            if (_hand > 0) _handVals = Values(Objs[_hand]);
            // the other coins on the player's tile
            _copy = 0;
            for (int i = UWTileMap.current_tilemap.Tiles[_px, _py].indexObjectList, guard = 0; i != 0 && guard < 1024; i = Objs[i].next, guard++)
                if (i != _hand && Objs[i].item_id == ConvHelp.Coin) { _copy = i; break; }
            if (_copy != 0) _copyVals = Values(Objs[_copy]);
            c.Note($"stack {_worldIndex}, hand {_hand}, coins left on the tile {_copy}; coins can be owned {_canOwn}");
        }
        finally
        {
            if (playerdat.ObjectInHand > 0) { playerdat.ObjectInHand = -1; uimanager.instance.mousecursor.SetCursorToCursor(); }
            MessageDisplay.WaitingForTypedInput = false;
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
        string add = uw2 ? "uw2 INTERACT.C:363" : "uw1 INTERACT.C:460";
        string get = uw2 ? "uw2 INTERACT.C:383-387" : "uw1 INTERACT.C:478-481";
        string grab = uw2 ? "uw2 CRITTIME.C:545-546" : "uw1 CRITTIME.C:480-481";
        var k = new Checks();
        k.Equal("object in hand (the stack itself, cut to the number asked)", _worldIndex, _hand, get);
        k.True(!_stackOnTile, $"the stack picked up is still on the tile ({get})");
        k.True(_copy != 0, $"no coins left on the player's tile ({add})");
        if (_handVals == null || _copyVals == null) return k.Result;
        // the stack's own values; the one picked up becomes nobody's when coins can be owned
        int[] expected = { ConvHelp.Coin, Quality, Owner, Flags, Doordir, 0, Xpos, Ypos, _zpos, Heading, 0, 1, 0 };
        for (int i = 0; i < Names.Length; i++)
        {
            int handWant = i == 2 && _canOwn ? 0 : i == 12 ? Asked : expected[i];
            int copyWant = i == 12 ? Count - Asked : expected[i];
            k.Equal($"{Names[i]} of the coins in hand", handWant, _handVals[i], i == 2 ? grab : split);
            k.Equal($"{Names[i]} of the coins left on the map", copyWant, _copyVals[i], split);
        }
        return k.Result;
    }
}
