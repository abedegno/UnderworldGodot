using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// The weight check of a pick-up from the map (UW1 and UW2). The original's player_3dget asks
/// "Move how many?" for a stack first, splits it (AskHowMany) and only then weighs what is taken,
/// EncumCheck(newPlObj) on the stack cut to the number asked. If that is too heavy it puts the
/// stack back together, takes the copy off the tile and says "That is too heavy for you to pick
/// up." (uw1/src/ui/INTERACT.C:457-474, uw2/src/ui/INTERACT.C:360-371; UW2 frees the copy, UW1
/// leaves it on no list). The target of a use-on is never weighed: a click with an item waiting
/// for a target calls ObjectActor straight away (uw1/src/ui/INTERACT.C:605-609,
/// uw2/src/ui/INTERACT.C:506-510).
///
/// The port's PickUp (src/interaction/pickup.cs) weighed the whole clicked object first, before
/// the use-on and before asking how many. So a stack too heavy as a whole could not be partly
/// picked up, and an item could not be used on a heavy target.
///
/// The scenario lowers the carrying capacity and clicks a stack of 10 coins on the player's tile
/// through pickup.PickUp three times: with room for exactly 3 coins, answering 3; with room for
/// one coin's weight less than 3, answering 3; and with no room at all, as the target of a use-on.
/// </summary>
public sealed class PickupWeight : Scenario
{
    const int Count = 10, Asked = 3;

    public override string Name => "pickup-weight";
    public override string Cites => "underworld-exhumed uw1/src/ui/INTERACT.C:457-474, 605-609, uw1/src/inv/INVDATA.C:486-491 (EncumCheck); uw2/src/ui/INTERACT.C:360-371, 506-510, uw2/src/inv/INVDATA.C:467-472";

    string _problem;
    int _px, _py, _mass;
    short _carriedBack;

    // part fits
    int _fitStack, _fitHand, _fitHandQty, _fitCopyQty;
    bool _fitAsked;
    // part too heavy
    int _heavyStack, _heavyHand, _heavyStackQty, _heavyCoinsOnTile, _heavyFreeBefore, _heavyFreeAfter;
    bool _heavyAsked, _heavyStackOnTile;
    // use-on target
    int _target;
    bool _useOnReached;

    static uwObject[] Objs => UWTileMap.current_tilemap.LevelObjects;

    public override Task Setup(ScenarioContext c)
    {
        _px = playerdat.playerObject.npc_xhome;
        _py = playerdat.playerObject.npc_yhome;
        _mass = commonObjDat.mass(ConvHelp.Coin);
        if (_mass <= 0) _problem = $"a coin weighs {_mass}, so no weight can be tested";
        _carriedBack = playerdat.WeightCarried;
        return Task.CompletedTask;
    }

    /// <summary>A stack of Count coins at the head of the player's tile's list.</summary>
    int MakeStack()
    {
        int i = ObjectCreator.PrepareNewObject(ConvHelp.Coin);
        if (i == 0) return 0;
        var o = Objs[i];
        var tile = UWTileMap.current_tilemap.Tiles[_px, _py];
        o.is_quant = 1;
        o.link = Count;
        o.zpos = (short)(tile.floorHeight << 3);
        o.tileX = _px; o.tileY = _py;
        o.next = tile.indexObjectList;
        tile.indexObjectList = (short)i;
        ObjectCreator.RenderObject(o, UWTileMap.current_tilemap);
        return i;
    }

    /// <summary>Leaves the player able to carry exactly room more.</summary>
    static void SetRoom(int room) => playerdat.WeightCarried = (short)(playerdat.WeightMax - room);

    /// <summary>The coins on the player's tile other than the stack, and their quantity.</summary>
    int CoinsOnTile(int except, out int qty)
    {
        int n = 0; qty = 0;
        for (int i = UWTileMap.current_tilemap.Tiles[_px, _py].indexObjectList, guard = 0; i != 0 && guard < 1024; i = Objs[i].next, guard++)
            if (i != except && Objs[i].item_id == ConvHelp.Coin) { n++; qty += Objs[i].link; }
        return n;
    }

    /// <summary>Clicks the stack through PickUp and answers "Move how many?" with Asked, as the player types it.</summary>
    static async Task<bool> ClickAndAnswer(ScenarioContext c, int index)
    {
        MessageDisplay.WaitingForTypedInput = false;
        pickup.PickUp(index, Objs, WorldObject: true);
        bool asked = MessageDisplay.WaitingForTypedInput;
        if (asked)
        {
            uimanager.instance.TypedInput.Text = Asked.ToString();
            MessageDisplay.WaitingForTypedInput = false;
            await c.Frames(3);
        }
        return asked;
    }

    static void EmptyHand()
    {
        if (playerdat.ObjectInHand > 0) { playerdat.ObjectInHand = -1; uimanager.instance.mousecursor.SetCursorToCursor(); }
    }

    public override async Task Act(ScenarioContext c)
    {
        if (_problem != null) return;
        try
        {
            useon.CurrentItemBeingUsed = null;

            // 1. Room for exactly the part taken: the part is picked up, though the stack is too heavy as a whole.
            EmptyHand();
            _fitStack = MakeStack();
            if (_fitStack == 0) { _problem = "no coins could be made on the map"; return; }
            SetRoom(_mass * Asked);
            _fitAsked = await ClickAndAnswer(c, _fitStack);
            _fitHand = playerdat.ObjectInHand;
            _fitHandQty = _fitHand > 0 ? Objs[_fitHand].link : 0;
            CoinsOnTile(_fitHand, out _fitCopyQty);
            c.Note($"room {_mass * Asked} for 10 coins of weight {_mass}: asked how many {_fitAsked}, hand {_fitHand} holds {_fitHandQty}, {_fitCopyQty} coins left on the tile");
            EmptyHand();
            // clear the tile of the first case's coins
            ClearCoins();

            // 2. One short of room for the part taken: nothing is picked up and the stack is whole again.
            _heavyStack = MakeStack();
            if (_heavyStack == 0) { _problem = "no coins could be made on the map"; return; }
            SetRoom(_mass * Asked - 1);
            _heavyFreeBefore = UWTileMap.current_tilemap.StaticFreeListPtr;
            _heavyAsked = await ClickAndAnswer(c, _heavyStack);
            _heavyFreeAfter = UWTileMap.current_tilemap.StaticFreeListPtr;
            _heavyHand = playerdat.ObjectInHand;
            _heavyStackOnTile = ConvHelp.OnMapNear(_heavyStack, _px, _py, 0, out _, out _);
            _heavyStackQty = Objs[_heavyStack].link;
            _heavyCoinsOnTile = CoinsOnTile(_heavyStack, out _);
            c.Note($"room {_mass * Asked - 1}: asked how many {_heavyAsked}, hand {_heavyHand}, stack on the tile {_heavyStackOnTile} with {_heavyStackQty}, other coins on the tile {_heavyCoinsOnTile}, static free list pointer {_heavyFreeBefore} before and {_heavyFreeAfter} after");
            EmptyHand();

            // 3. No room at all: the same stack as the target of a use-on still reaches the use-on.
            _target = _heavyStack;
            SetRoom(0);
            int tool = ObjectCreator.PrepareNewObject(ConvHelp.Coin);
            if (tool == 0) { _problem = "no object could be made to use on the stack"; return; }
            useon.CurrentItemBeingUsed = new useon(Objs[tool], true);
            pickup.PickUp(_target, Objs, WorldObject: true);
            _useOnReached = useon.CurrentItemBeingUsed == null;   // UseOn clears it as it finishes
            c.Note($"no room: the use-on of a coin on the stack reached UseOn {_useOnReached}");
            useon.CurrentItemBeingUsed = null;
            ObjectFreeLists.ReleaseFreeObject(Objs[tool]);
        }
        finally
        {
            EmptyHand();
            MessageDisplay.WaitingForTypedInput = false;
            useon.CurrentItemBeingUsed = null;
            playerdat.WeightCarried = _carriedBack;
        }
    }

    /// <summary>Takes this scenario's coins off the player's tile.</summary>
    void ClearCoins()
    {
        var tile = UWTileMap.current_tilemap.Tiles[_px, _py];
        while (tile.indexObjectList != 0 && Objs[tile.indexObjectList].item_id == ConvHelp.Coin)
        {
            var o = Objs[tile.indexObjectList];
            tile.indexObjectList = o.next;
            o.next = 0;
            ObjectFreeLists.ReleaseFreeObject(o);
        }
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        bool uw2 = c.Game == "uw2";
        string ask = uw2 ? "uw2 INTERACT.C:360-363" : "uw1 INTERACT.C:457-460";
        string weigh = uw2 ? "uw2 INTERACT.C:365-371" : "uw1 INTERACT.C:462-474";
        string target = uw2 ? "uw2 INTERACT.C:506-510" : "uw1 INTERACT.C:605-609";
        var k = new Checks();
        k.True(_fitAsked, $"with room for {Asked} coins of a stack of {Count}, expected to be asked how many, the stack was weighed whole and refused ({ask})");
        k.Equal("object in hand with room for the part taken", _fitStack, _fitHand, weigh);
        k.Equal("coins in hand", Asked, _fitHandQty, weigh);
        k.Equal("coins left on the tile", Count - Asked, _fitCopyQty, ask);
        k.True(_heavyAsked, $"with room for less than {Asked} coins, expected to be asked how many first ({ask})");
        k.True(_heavyHand <= 0, $"with too little room for the part taken, expected nothing in hand, got {_heavyHand} ({weigh})");
        k.True(_heavyStackOnTile, $"the stack expected still on the tile ({weigh})");
        k.Equal("coins in the stack put back together", Count, _heavyStackQty, weigh);
        k.Equal("other coins on the tile (the split copy, taken off the tile)", 0, _heavyCoinsOnTile, weigh);
        k.Equal("static free list pointer (the copy freed, as UW2 does)", _heavyFreeBefore, _heavyFreeAfter, "uw2 INTERACT.C:367-368");
        k.True(_useOnReached, $"a use-on with no room to carry expected to reach UseOn, the target was weighed and refused ({target})");
        return k.Result;
    }
}
