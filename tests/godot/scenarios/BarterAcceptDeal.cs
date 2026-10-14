using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace Underworld.Scenarios;

/// <summary>Helpers shared by the accepted-trade scenarios.</summary>
static class TradeHelp
{
    /// <summary>The coin the NPC adds to a pile it has: ITEM_GOLD_COIN 0xA1 in UW1, ITEM_COIN 0xA0 in UW2.</summary>
    public static int MergingCoin(string game) => game == "uw1" ? 0xA1 : 0xA0;

    /// <summary>Makes an object of item id in the hand and takes it out of the hand again. Returns its index, or 0.</summary>
    public static int Make(int itemId, int quantity = 0)
    {
        int index = ObjectCreator.SpawnObjectInHand(itemId);
        playerdat.ObjectInHand = -1;
        uimanager.instance.mousecursor.SetCursorToCursor();
        if (index != 0 && quantity > 0)
        {
            var o = UWTileMap.current_tilemap.LevelObjects[index];
            o.is_quant = 1;
            o.link = (short)quantity;
        }
        return index;
    }

    /// <summary>A slot valuation cache with nothing valued yet (-1 in every slot), for TotalOffering.</summary>
    public static int[] Unvalued() => new[] { -1, -1, -1, -1, -1, -1 };

    /// <summary>
    /// Forgets every slot's valuations, as barter_init does when a conversation starts (uw1 BARTER.C:185-188, uw2 BARTER.C:150-151),
    /// for a scenario that puts new items straight into the slots within one conversation.
    /// </summary>
    public static void ForgetValuations()
    {
        foreach (var values in new[] { ConversationVM.NpcAppraisals, ConversationVM.PlayerAppraisals, ConversationVM.NpcJudgement, ConversationVM.PlayerJudgement })
            System.Array.Fill(values, -1);
    }

    /// <summary>Food items (0xB0 to 0xBF, not weapons) the trade values above 0, so setup_to_barter offers them.</summary>
    public static List<int> ValuedFood() =>
        Enumerable.Range(0xB0, 16).Where(id => commonObjDat.monetaryvalue(id) > 0).ToList();

    /// <summary>The NPC's inventory chain (its link, then next), as object indices.</summary>
    public static List<int> Chain(uwObject npc)
    {
        var objs = UWTileMap.current_tilemap.LevelObjects;
        var list = new List<int>();
        int i = npc.link;
        for (int guard = 0; i > 0 && i < objs.Length && guard < 256; guard++)
        {
            list.Add(i);
            i = objs[i].next;
        }
        return list;
    }

    /// <summary>Finds index in some square's object list.</summary>
    public static bool OnMap(int index, out int tx, out int ty)
    {
        var map = UWTileMap.current_tilemap;
        for (ty = 0; ty < 64; ty++)
            for (tx = 0; tx < 64; tx++)
            {
                int i = map.Tiles[tx, ty].indexObjectList;
                for (int guard = 0; i != 0 && guard < 1024; guard++)
                {
                    if (i == index) return true;
                    i = map.LevelObjects[i].next;
                }
            }
        tx = ty = -1;
        return false;
    }

    /// <summary>Is index on the map within range eighths of a tile of (x, y), in eighths? Says where in where.</summary>
    public static bool NearPoint(int index, int x, int y, int range, out string where)
    {
        if (!OnMap(index, out int tx, out int ty)) { where = "not on the map"; return false; }
        var o = UWTileMap.current_tilemap.LevelObjects[index];
        int ox = (tx << 3) + o.xpos, oy = (ty << 3) + o.ypos;
        where = $"at ({ox}, {oy}) in eighths";
        return System.Math.Abs(ox - x) <= range && System.Math.Abs(oy - y) <= range;
    }

    /// <summary>An owner's position in eighths of a tile (OBJ_HOMEX << 3 + OBJ_FINEX), as near_mob_put_at takes it.</summary>
    public static (int x, int y) Eighths(uwObject o) => ((o.npc_xhome << 3) + o.xpos, (o.npc_yhome << 3) + o.ypos);

    /// <summary>
    /// Runs a conversation built-in through the VM's own dispatcher (run_imported_function) to its end, without
    /// waiting: nested coroutines are run in turn, waits are skipped and "more" prompts dismissed.
    /// </summary>
    public static void RunImport(string name, uwObject talker)
    {
        var stack = new Stack<IEnumerator>();
        stack.Push(ConversationVM.run_imported_function(new ConversationImports { importname = name }, talker));
        for (int guard = 0; stack.Count > 0 && guard < 100000; guard++)
        {
            MessageDisplay.WaitingForMore = false;
            var top = stack.Peek();
            if (!top.MoveNext()) { stack.Pop(); continue; }
            if (top.Current is IEnumerator inner) stack.Push(inner);
        }
    }
}

/// <summary>
/// An accepted offer leaves the NPC's items in its slots for the player to take (UW1 and UW2).
///
/// do_offer, when the offer is good enough, puts the NPC's unselected slot items back in its inventory
/// (ReturnTradeObjectsToNPC(1)), gives the NPC the player's selected items with probablyTradeObjects and sets
/// barter_result (uw1/src/conv/BARTER.C:658-663, uw2/src/conv/BARTER.C:621-626). probablyTradeObjects skips an
/// item the NPC dislikes (does_npc_like == -1), which stays in the player's slot; adds a coin of the merging type to
/// a pile the NPC has while the two stay under 999; and adds anything else at the head of the NPC's inventory in UW1
/// (Obj_Add, uw1 BARTER.C:567-602) and at the end in UW2 (Obj_AddEnd, uw2 BARTER.C:530-565). The NPC's selected
/// items stay in its slots: the player can pick them up only once barter_result is set (uw1 BARTER.C:356,
/// uw2 BARTER.C:317), and end_barter puts any left there near the NPC with near_mob_put_at(talking_to, obj, 5, 0)
/// (uw1 BARTER.C:212-225, uw2 BARTER.C:172-190).
///
/// The port's SwapTradedObjects gave the NPC every selected item at the head of its inventory, coins as a new pile,
/// and moved the NPC's selected items straight into the player's pack or onto the player's square
/// (src/conversation/conversation_functions/conversationtrade.cs:363-431). Its NPC slots could never be picked up from.
///
/// The scenario first moves the player, and the NPC once the conversation has started, to level, open ground
/// (TradeGround), since an item put down on the slope beside the UW1 start falls and may be culled when it lands.
/// The scenario starts a conversation with the first NPC on level 1 that has one. It gives the NPC three valued items
/// and calls setup_to_barter, which puts them in slots 0, 1 and 2, then gives the NPC a pile of 10 coins, and selects
/// the NPC's slots 0 and 1. The player's slots get 5 coins, an item the NPC dislikes (set_likes_dislikes' list, made
/// on a stack of the scenario's own) and another item, all selected, and one coin not selected. Picking up the
/// NPC's slot 0 must fail. The NPC's greed is set below any offer, and do_offer runs through the VM's dispatcher.
/// Then: it returns 1; the NPC's selected items are still in its slots and out of its inventory; its unselected one
/// is back in its inventory; the coins are in its pile of 15 and freed; the disliked item and the unselected coin
/// are still in the player's slots; the other item is in the NPC's inventory, first in UW1 and last in UW2. The
/// player picks up the NPC's slot 0, which must put that item in the hand, leaves slot 1 and lets the conversation
/// run to its end as end-barter-returns-items does: the item left in slot 1 must be on the map within 5 eighths of
/// the NPC, the disliked item and the coin within 5 eighths of the player.
/// </summary>
public sealed class BarterAcceptDeal : Scenario
{
    const ulong StartMs = 10000, EndMs = 30000;
    const int MaxMenus = 40, Range = 5;

    public override string Name => "barter-accept-deal";
    public override string Cites => "underworld-exhumed uw1/src/conv/BARTER.C:212-225, 356, 567-602, 658-663; uw2/src/conv/BARTER.C:172-190, 317, 530-565, 621-626";

    string _problem;
    uwObject _talker;
    int _a, _b, _c, _pile, _coins, _disliked, _neutral, _spare;
    bool _takenEarly, _taken;
    int _result = -1, _pileAfter = -1, _coinPiles = -1;
    int[] _npcSlotsAfter, _playerSlotsAfter;
    bool[] _npcSelAfter;
    List<int> _chainAfter;
    int _inHandAfterTake = -1;
    string _bWhere, _dislikedWhere, _spareWhere;
    bool _bNear, _dislikedNear, _spareNear, _bInChainAtEnd;
    int _px, _py, _nx, _ny;

    public override async Task Setup(ScenarioContext c)
    {
        var objs = UWTileMap.current_tilemap.LevelObjects;
        // Level, open ground for both owners, so every spot drop_around_place can pick keeps the item on the map.
        var (gx, gy, gnx, gny) = TradeGround.FindPair();
        if (gx < 0) { _problem = "no level, open ground near the start"; return; }
        if (!await TradeGround.MovePlayer(c, gx, gy)) { _problem = $"the move to ({gx}, {gy}) did not happen"; return; }
        for (int i = 2; i < 256 && uimanager.CurrentGameMode != uimanager.GameModes.CONVERSATION; i++)
        {
            var o = objs[i];
            if (o == null || o.majorclass != 1 || o.npc_whoami == 0 || o.npc_whoami == 255 || o.npc_hp == 0) continue;
            if (!UWTileMap.ValidTile(o.tileX, o.tileY)) continue;
            ConversationVM.StartConversation(o);
            if (uimanager.CurrentGameMode == uimanager.GameModes.CONVERSATION) _talker = o;
        }
        if (_talker == null) { _problem = "no NPC on level 1 would talk"; return; }
        TradeGround.MoveNpc(_talker, gnx, gny);
        ulong deadline = Time.GetTicksMsec() + StartMs;
        int frames = 0;
        while (Time.GetTicksMsec() < deadline && frames < 60) { await c.Frames(1); frames++; }
        if (uimanager.CurrentGameMode != uimanager.GameModes.CONVERSATION) { _problem = "the conversation ended by itself"; return; }
        c.Note($"talking to whoami 0x{_talker.npc_whoami:X} (object {_talker.index})");

        var food = TradeHelp.ValuedFood();
        if (food.Count < 4) { _problem = $"only {food.Count} valued food items"; return; }
        int coin = TradeHelp.MergingCoin(c.Game);

        // Three items for the NPC to offer, and nothing else in its inventory.
        if (_talker.LootSpawnedFlag == 0) npc.spawnloot(_talker);
        _talker.link = 0;
        _a = TradeHelp.Make(food[0]); _b = TradeHelp.Make(food[1]); _c = TradeHelp.Make(food[2]);
        if (_a == 0 || _b == 0 || _c == 0) { _problem = "the NPC's items could not be made"; return; }
        objs[_a].next = (short)_b; objs[_b].next = (short)_c; objs[_c].next = 0;
        _talker.link = (short)_a;
        for (int s = 0; s < uimanager.NoOfTradeSlots; s++) { uimanager.SetNPCTradeSlot(s, -1); uimanager.SetPlayerTradeSlot(s, -1); }
        ConversationVM.setup_to_barter(_talker);
        if (uimanager.GetNPCTradeSlot(0, false) != _a || uimanager.GetNPCTradeSlot(1, false) != _b || uimanager.GetNPCTradeSlot(2, false) != _c)
        { _problem = "setup_to_barter did not put the three items in slots 0, 1 and 2"; return; }
        uimanager.NPCTradeOn(0);
        uimanager.NPCTradeOn(1);

        // The NPC's own coins, which it keeps.
        _pile = TradeHelp.Make(coin, 10);
        if (_pile == 0) { _problem = "no coin pile could be made"; return; }
        objs[_pile].next = _talker.link;
        _talker.link = (short)_pile;

        _coins = TradeHelp.Make(coin, 5);
        _disliked = TradeHelp.Make(food[3]);
        _neutral = TradeHelp.Make(food[0]);
        _spare = TradeHelp.Make(coin, 1);
        if (_coins == 0 || _disliked == 0 || _neutral == 0 || _spare == 0) { _problem = "the player's items could not be made"; return; }
        uimanager.SetPlayerTradeSlot(0, _coins, selected: true);
        uimanager.SetPlayerTradeSlot(1, _disliked, selected: true);
        uimanager.SetPlayerTradeSlot(2, _neutral, selected: true);
        uimanager.SetPlayerTradeSlot(3, _spare, selected: false);

        _takenEarly = uimanager.PickUpFromTradeSlot(0, false);
        if (_takenEarly) return;

        var savedValues = ConversationVM.StackValues; int savedStack = ConversationVM.stack, savedPtr = ConversationVM.stackptr;
        int savedLikes = ConversationVM.Likes, savedDislikes = ConversationVM.Dislikes, savedGreed = ConversationVM.TradeThreshold;
        try
        {
            ConversationVM.StackValues = new short[64];
            ConversationVM.stack = 0;
            // do_offer's five string arguments at sp - 5 .. sp - 1, each pointing at string 0.
            ConversationVM.stackptr = 6;
            for (int k = 1; k <= 5; k++) ConversationVM.Set(k, 20);
            ConversationVM.Set(20, 0);
            // set_likes_dislikes(likes, dislikes): no likes, and a list of one dislike ending in -1.
            ConversationVM.Set(30, -1);
            ConversationVM.Set(40, (short)food[3]); ConversationVM.Set(41, -1);
            ConversationVM.Likes = 30;
            ConversationVM.Dislikes = 40;
            ConversationVM.TradeThreshold = short.MinValue;
            TradeHelp.RunImport("do_offer", _talker);
            _result = ConversationVM.result_register;
        }
        finally
        {
            ConversationVM.StackValues = savedValues; ConversationVM.stack = savedStack; ConversationVM.stackptr = savedPtr;
            ConversationVM.Likes = savedLikes; ConversationVM.Dislikes = savedDislikes; ConversationVM.TradeThreshold = savedGreed;
        }

        _npcSlotsAfter = Enumerable.Range(0, uimanager.NoOfTradeSlots).Select(s => uimanager.GetNPCTradeSlot(s, false)).ToArray();
        _npcSelAfter = Enumerable.Range(0, uimanager.NoOfTradeSlots).Select(s => uimanager.GetNPCTradeSlot(s, true) != -1).ToArray();
        _playerSlotsAfter = Enumerable.Range(0, uimanager.NoOfTradeSlots).Select(s => uimanager.GetPlayerTradeSlot(s, false)).ToArray();
        _chainAfter = TradeHelp.Chain(_talker);
        _pileAfter = objs[_pile].ObjectQuantity;
        _coinPiles = _chainAfter.Count(i => objs[i].item_id == coin);
        c.Note($"do_offer returned {_result}; NPC slots [{string.Join(", ", _npcSlotsAfter)}]; player slots [{string.Join(", ", _playerSlotsAfter)}]; NPC inventory [{string.Join(", ", _chainAfter)}]; pile {_pileAfter}");

        _taken = uimanager.PickUpFromTradeSlot(0, false);
        _inHandAfterTake = playerdat.ObjectInHand;
    }

    public override async Task Act(ScenarioContext c)
    {
        if (_problem != null || _takenEarly) return;
        int menus = 0;
        ulong deadline = Time.GetTicksMsec() + EndMs;
        while (Time.GetTicksMsec() < deadline && uimanager.CurrentGameMode == uimanager.GameModes.CONVERSATION && menus <= MaxMenus)
        {
            (_px, _py) = TradeHelp.Eighths(playerdat.playerObject);
            (_nx, _ny) = TradeHelp.Eighths(_talker);
            if (MessageDisplay.WaitingForMore) MessageDisplay.WaitingForMore = false;
            if (MessageDisplay.WaitingForTypedInput) MessageDisplay.WaitingForTypedInput = false;
            if (ConversationVM.WaitingForInput)
            {
                ConversationVM.PlayerNumericAnswer = System.Math.Max(1, ConversationVM.MaxAnswer);
                ConversationVM.WaitingForInput = false;
                menus++;
            }
            await c.Frames(1);
        }
        c.Note($"{menus} menus answered; mode now {uimanager.CurrentGameMode}; player at ({_px}, {_py}), NPC at ({_nx}, {_ny}) in eighths");
        if (uimanager.CurrentGameMode == uimanager.GameModes.CONVERSATION) { _problem = "the conversation did not end"; return; }
        _bInChainAtEnd = TradeHelp.Chain(_talker).Contains(_b);
        _bNear = TradeHelp.NearPoint(_b, _nx, _ny, Range, out _bWhere);
        _dislikedNear = TradeHelp.NearPoint(_disliked, _px, _py, Range, out _dislikedWhere);
        _spareNear = TradeHelp.NearPoint(_spare, _px, _py, Range, out _spareWhere);
        c.Note($"NPC's untaken item {_bWhere}; disliked item {_dislikedWhere}; unselected coin {_spareWhere}");
        if (playerdat.ObjectInHand != -1)
        {
            playerdat.ObjectInHand = -1;
            uimanager.instance.mousecursor.SetCursorToCursor();
        }
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        bool uw1 = c.Game == "uw1";
        string gate = uw1 ? "uw1 BARTER.C:356" : "uw2 BARTER.C:317";
        string offer = uw1 ? "uw1 BARTER.C:658-663" : "uw2 BARTER.C:621-626";
        string trade = uw1 ? "uw1 BARTER.C:567-602" : "uw2 BARTER.C:530-565";
        string end = uw1 ? "uw1 BARTER.C:212-225" : "uw2 BARTER.C:172-190";
        var k = new Checks();
        k.True(!_takenEarly, $"NPC's slot item picked up before any offer was accepted ({gate})");
        if (_takenEarly) return k.Result;
        k.Equal("do_offer's result", 1, _result, offer);
        k.Equal("NPC's slot 0 after the offer (its selected item, left for the player)", _a, _npcSlotsAfter[0], offer);
        k.Equal("NPC's slot 1 after the offer (its selected item, left for the player)", _b, _npcSlotsAfter[1], offer);
        k.True(_npcSelAfter[0] && _npcSelAfter[1], $"NPC's slots 0 and 1 expected still selected after the offer ({offer})");
        k.Equal("NPC's slot 2 after the offer (its unselected item goes back)", -1, _npcSlotsAfter[2], offer);
        k.True(!_chainAfter.Contains(_a) && !_chainAfter.Contains(_b), $"NPC's selected items expected out of its inventory after the offer ({offer})");
        k.Equal("NPC's coin pile after 5 coins were traded to it", 15, _pileAfter, trade);
        k.Equal("coin piles in the NPC's inventory", 1, _coinPiles, trade);
        k.True(!_chainAfter.Contains(_coins), $"the traded coins expected merged into the NPC's pile, not added ({trade})");
        k.Equal("player's slot 0 (coins, traded)", -1, _playerSlotsAfter[0], trade);
        k.Equal("player's slot 1 (an item the NPC dislikes, kept)", _disliked, _playerSlotsAfter[1], trade);
        k.Equal("player's slot 2 (traded)", -1, _playerSlotsAfter[2], trade);
        k.Equal("player's slot 3 (not selected, kept)", _spare, _playerSlotsAfter[3], trade);
        var expected = uw1 ? new[] { _neutral, _c, _pile } : new[] { _c, _pile, _neutral };
        k.True(_chainAfter.SequenceEqual(expected),
            $"NPC's inventory after the offer expected [{string.Join(", ", expected)}] ({(uw1 ? "unselected item and traded item each at the head, Obj_Add" : "unselected item at the head, Obj_Add; traded item at the end, Obj_AddEnd")}), was [{string.Join(", ", _chainAfter)}] ({trade}, {offer})");
        k.True(_taken && _inHandAfterTake == _a, $"picking up the NPC's slot 0 after the accepted offer expected item {_a} in the hand, was {_inHandAfterTake} ({gate})");
        k.True(!_bInChainAtEnd && _bNear, $"NPC's untaken item expected put within {Range} eighths of the NPC when the conversation ended, {_bWhere}{(_bInChainAtEnd ? ", in its inventory" : "")} ({end})");
        k.True(_dislikedNear, $"disliked item left in the player's slot expected within {Range} eighths of the player, {_dislikedWhere} ({end})");
        k.True(_spareNear, $"unselected coin expected within {Range} eighths of the player, {_spareWhere} ({end})");
        return k.Result;
    }
}

/// <summary>
/// take_from_npc and take_id_from_npc put what the hand and the trade slots cannot hold near the NPC (UW1 and UW2).
///
/// npc_barter_give and npc_barter_give_id return 0 at once when the player's hand holds something, and 0 when the
/// NPC has no such item. Otherwise they take the item out of the NPC's inventory and put it in the hand if the player
/// can carry it (EncumCheck), else in a free player trade slot, unselected, returning 1; else near the NPC with
/// near_mob_put_at(talking_to, obj, 5, 0), returning 2, or 3 when that fails (uw1/src/conv/BARTER.C:951-987,
/// 991-1024, uw2/src/conv/BARTER.C:931-968, 971-1004).
///
/// The port's take_id_from_npc put the item in a trade slot when the hand was full, and dropped it on the player's
/// square when the slots were full or it was too heavy (src/conversation/conversation_functions/take_id_from_npc.cs:37, 56);
/// take_from_npc took the item out of the NPC's inventory and left it nowhere when it was too heavy and the slots
/// were full.
///
/// The scenario moves the first NPC on level 1 to level, open ground (TradeGround), so nothing it puts down falls and is culled.
/// It gives the NPC three valued items, fills every player trade slot with a coin and
/// sets the player's weight limit to 0 (restored afterwards), so nothing can go in the hand or a slot. With the
/// VM's own argument layout and dispatcher, take_id_from_npc of the first item and take_from_npc of the second
/// item's type must each return 2 and put the item within 5 eighths of the NPC, out of its inventory. Then with an
/// object in the hand take_id_from_npc of the third item must return 0 and leave it in the NPC's inventory.
/// </summary>
public sealed class TakeFromNpcNear : Scenario
{
    const int Range = 5;

    public override string Name => "take-from-npc-near-npc";
    public override string Cites => "underworld-exhumed uw1/src/conv/BARTER.C:951-1024; uw2/src/conv/BARTER.C:931-1004";

    string _problem;
    int _x, _y, _z, _resultId = -1, _resultType = -1, _resultHandFull = -1;
    bool _xNear, _yNear, _xInChain, _yInChain, _zInChain, _slotsUnchanged, _handUnchanged;
    string _xWhere, _yWhere;
    int _nx, _ny;

    public override Task Setup(ScenarioContext c) => Task.CompletedTask;

    public override Task Act(ScenarioContext c)
    {
        var objs = UWTileMap.current_tilemap.LevelObjects;
        uwObject talker = null;
        for (int i = 2; i < 256 && talker == null; i++)
        {
            var o = objs[i];
            if (o == null || o.majorclass != 1 || o.npc_whoami == 0 || o.npc_whoami == 255 || o.npc_hp == 0) continue;
            if (!UWTileMap.ValidTile(o.tileX, o.tileY)) continue;
            talker = o;
        }
        if (talker == null) { _problem = "no NPC on level 1"; return Task.CompletedTask; }
        // Level, open ground around the NPC, so every spot drop_around_place can pick keeps the item on the map.
        var (gx, _, gnx, gny) = TradeGround.FindPair();
        if (gx < 0) { _problem = "no level, open ground near the start"; return Task.CompletedTask; }
        TradeGround.MoveNpc(talker, gnx, gny);
        (_nx, _ny) = TradeHelp.Eighths(talker);
        var food = TradeHelp.ValuedFood();
        if (food.Count < 3) { _problem = $"only {food.Count} valued food items"; return Task.CompletedTask; }

        int savedLink = talker.link, savedLoot = talker.LootSpawnedFlag;
        talker.LootSpawnedFlag = 1;
        _x = TradeHelp.Make(food[0]); _y = TradeHelp.Make(food[1]); _z = TradeHelp.Make(food[2]);
        if (_x == 0 || _y == 0 || _z == 0) { _problem = "the NPC's items could not be made"; return Task.CompletedTask; }
        objs[_x].next = (short)_y; objs[_y].next = (short)_z; objs[_z].next = 0;
        talker.link = (short)_x;
        var slots = new int[uimanager.NoOfTradeSlots];
        for (int s = 0; s < slots.Length; s++)
        {
            slots[s] = TradeHelp.Make(ConvHelp.Coin, 1);
            if (slots[s] == 0) { _problem = "no coins could be made"; return Task.CompletedTask; }
            uimanager.SetPlayerTradeSlot(s, slots[s], selected: false);
        }

        var savedValues = ConversationVM.StackValues; int savedStack = ConversationVM.stack, savedPtr = ConversationVM.stackptr;
        short savedWeight = playerdat.WeightMax;
        int held = 0;
        try
        {
            ConversationVM.StackValues = new short[64];
            ConversationVM.stack = 0;
            ConversationVM.stackptr = 3;
            ConversationVM.Set(2, 10);
            playerdat.WeightMax = 0;

            ConversationVM.Set(10, (short)_x);
            ConversationVM.result_register = -1;
            TradeHelp.RunImport("take_id_from_npc", talker);
            _resultId = ConversationVM.result_register;

            ConversationVM.Set(10, (short)objs[_y].item_id);
            ConversationVM.result_register = -1;
            TradeHelp.RunImport("take_from_npc", talker);
            _resultType = ConversationVM.result_register;

            held = TradeHelp.Make(ConvHelp.Coin, 1);
            playerdat.ObjectInHand = held;
            ConversationVM.Set(10, (short)_z);
            ConversationVM.result_register = -1;
            TradeHelp.RunImport("take_id_from_npc", talker);
            _resultHandFull = ConversationVM.result_register;
            _handUnchanged = playerdat.ObjectInHand == held;
        }
        finally
        {
            ConversationVM.StackValues = savedValues; ConversationVM.stack = savedStack; ConversationVM.stackptr = savedPtr;
            playerdat.WeightMax = savedWeight;
            playerdat.ObjectInHand = -1;
            uimanager.instance.mousecursor.SetCursorToCursor();
        }
        var chain = TradeHelp.Chain(talker);
        _xInChain = chain.Contains(_x); _yInChain = chain.Contains(_y); _zInChain = chain.Contains(_z);
        _xNear = TradeHelp.NearPoint(_x, _nx, _ny, Range, out _xWhere);
        _yNear = TradeHelp.NearPoint(_y, _nx, _ny, Range, out _yWhere);
        _slotsUnchanged = Enumerable.Range(0, slots.Length).All(s => uimanager.GetPlayerTradeSlot(s, false) == slots[s]);
        c.Note($"NPC {talker.index} at ({_nx}, {_ny}) in eighths; take_id_from_npc {_resultId}, item {_xWhere}; take_from_npc {_resultType}, item {_yWhere}; hand full {_resultHandFull}");
        talker.link = (short)savedLink; talker.LootSpawnedFlag = (short)savedLoot;
        for (int s = 0; s < slots.Length; s++) uimanager.SetPlayerTradeSlot(s, -1);
        return Task.CompletedTask;
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        bool uw1 = c.Game == "uw1";
        string byId = uw1 ? "uw1 BARTER.C:991-1024" : "uw2 BARTER.C:971-1004";
        string byType = uw1 ? "uw1 BARTER.C:951-987" : "uw2 BARTER.C:931-968";
        var k = new Checks();
        k.Equal("take_id_from_npc with the hand and slots unable to take it", 2, _resultId, byId);
        k.True(!_xInChain && _xNear, $"take_id_from_npc's item expected within {Range} eighths of the NPC, {_xWhere} ({byId})");
        k.Equal("take_from_npc with the hand and slots unable to take it", 2, _resultType, byType);
        k.True(!_yInChain && _yNear, $"take_from_npc's item expected within {Range} eighths of the NPC, {_yWhere} ({byType})");
        k.Equal("take_id_from_npc with an object in the hand", 0, _resultHandFull, byId);
        k.True(_zInChain, $"take_id_from_npc with an object in the hand expected the item left in the NPC's inventory ({byId})");
        k.True(_handUnchanged, $"take_id_from_npc expected to leave the hand as it was ({byId})");
        k.True(_slotsUnchanged, $"the player's full trade slots expected unchanged ({byId})");
        return k.Result;
    }
}
