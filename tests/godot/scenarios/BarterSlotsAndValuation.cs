using System.Threading.Tasks;
using Godot;

namespace Underworld.Scenarios;

/// <summary>
/// Taking an item out of a trade slot checks its weight and asks how many of a stack, and each slot's valuations are kept
/// as the original keeps them (UW1 and UW2).
///
/// UseTradeSlot asks "Move how many?" for a stack (AskHowMany cuts the stack to the number asked and a copy keeps the rest),
/// then checks the player can carry that many (EncumCheck); if not it puts the stack back together, says "That is too heavy
/// to take." and takes nothing. Otherwise the hand gets the stack, the copy stays in the slot unselected, and the player's
/// valuations of that slot number are forgotten (uw1 BARTER.C:357-376, uw2 BARTER.C:318-336; AskHowMany uw1 INVPANEL.C:452-497,
/// uw2 INVPANEL.C:436-479).
///
/// total_offering values a slot only when its cache entry is -1 (uw1 BARTER.C:788-803, uw2 BARTER.C:767-782). do_offer uses
/// play_appraisals and npc_appraisals, do_judgement play_judgement and npc_judgement (uw1 BARTER.C:648-651, 759-762, uw2
/// BARTER.C:611-614, 737-740). All four are cleared in barter_init (uw1 BARTER.C:185-188, uw2 BARTER.C:150-151); the player's
/// two for a slot when an item is picked out of or put into that slot number (uw1 BARTER.C:375-376, 399-400, uw2 BARTER.C:335-336,
/// 359-360); and npc_barter_give, putting an item into the player's slot i, clears play_judgement[i] and npc_appraisals[i]
/// (uw1 BARTER.C:979, 1015, uw2 BARTER.C:959, 995). setup_to_barter clears nothing. npc_appraisals[i] there is an evident slip
/// for play_appraisals[i], the player's slot that changed; the port clears play_appraisals[i] and this scenario checks that.
///
/// The port took the whole item straight into the hand with no weight check and no question, and valued every slot afresh on
/// every offer (src/ui/uimanager_trade.cs PickUpFromTradeSlot, src/conversation/conversation_functions/conversationtrade.cs
/// TotalOffering).
///
/// The scenario starts a conversation with the first NPC on level 1 that has one, gives it a controlled inventory, and:
/// - offers a ruby (quality 63) for the NPC's food item twice, with greed out of reach: both offers and both appraisals must
///   value the two sides the same;
/// - declines, puts a ruby of quality 50 at the head of the NPC's inventory and calls setup_to_barter, which puts the ruby in
///   NPC slot 0: the next offer and appraisal must still value NPC slot 0 as the food item (the stale case of a second
///   setup_to_barter);
/// - gives the player's ruby to the NPC (give_to_npc's player_barter_give, which clears nothing) and, with the weight limit at 0,
///   has the NPC hand over a second food item with take_id_from_npc, which puts it in player slot 0: the next offer and the
///   appraisal must value player slot 0 afresh as that food item, and NPC slot 0 still as the first food item;
/// - with the weight limit just under a single item's mass, picking it out of a player slot must fail, say "That is too heavy
///   to take." and leave it in the slot;
/// - picking 10 coins out of a player slot, with room for 3, must ask "Move how many?"; answering 3 must put the stack itself in
///   the hand holding 3 and leave a new object holding 7 in the slot, unselected;
/// - doing the same and answering 5 must say "That is too heavy to take.", leave the stack whole in its slot, hand empty, and
///   free the copy.
/// </summary>
public sealed class BarterSlotsAndValuation : Scenario
{
    const ulong StartMs = 10000, EndMs = 30000;
    const int MaxMenus = 40;
    const int Ruby = 0xA2;

    public override string Name => "barter-slots-and-valuation";
    public override string Cites => "underworld-exhumed uw1/src/conv/BARTER.C:185-188, 357-376, 399-400, 648-651, 759-762, 788-803, 979, 1015; uw2/src/conv/BARTER.C:150-151, 318-336, 359-360, 611-614, 737-740, 767-782, 959, 995";

    string _problem;
    string _tooHeavy;
    int[] _gain = new int[4];
    int[] _apPlayer = new int[4], _apNpc = new int[4];
    int _freshStaleGain, _expectSlipGain, _freshSlipGain, _expectSlipApPlayer;
    int _takeResult = -1;
    bool _takenToSlot;
    // the weight check on a single item
    bool _singleRefused, _singleStays, _singleSaid;
    // the stack split
    bool _asked, _askSaid;
    int _hand = -1, _handQty, _restIndex = -1, _restQty;
    bool _restUnselected, _restSameItem;
    int _stack;
    // the stack too heavy for the number asked
    bool _asked2, _heavySaid;
    int _stack2, _stack2Qty, _hand2 = -1, _slot2After = -1, _freePtrBefore, _freePtrAfter;

    static string ScrollText()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var line in uimanager.instance.scroll.Lines) if (line != null) sb.Append(line.LineText).Append('\n');
        return sb.ToString();
    }

    public override async Task Setup(ScenarioContext c)
    {
        bool uw1 = c.Game == "uw1";
        var objs = UWTileMap.current_tilemap.LevelObjects;
        uwObject talker = null;
        for (int i = 2; i < 256 && uimanager.CurrentGameMode != uimanager.GameModes.CONVERSATION; i++)
        {
            var o = objs[i];
            if (o == null || o.majorclass != 1 || o.npc_whoami == 0 || o.npc_whoami == 255 || o.npc_hp == 0) continue;
            if (!UWTileMap.ValidTile(o.tileX, o.tileY)) continue;
            ConversationVM.StartConversation(o);
            if (uimanager.CurrentGameMode == uimanager.GameModes.CONVERSATION) talker = o;
        }
        if (talker == null) { _problem = "no NPC on level 1 would talk"; return; }
        ulong deadline = Time.GetTicksMsec() + StartMs;
        int frames = 0;
        while (Time.GetTicksMsec() < deadline && frames < 60) { await c.Frames(1); frames++; }
        if (uimanager.CurrentGameMode != uimanager.GameModes.CONVERSATION) { _problem = "the conversation ended by itself"; return; }
        c.Note($"talking to whoami 0x{talker.npc_whoami:X} (object {talker.index})");
        _tooHeavy = GameStrings.GetString(1, GameStrings.str_that_is_too_heavy_to_take_).Trim();

        var food = TradeHelp.ValuedFood();
        if (food.Count < 2) { _problem = $"only {food.Count} valued food items"; return; }
        int coin = TradeHelp.MergingCoin(c.Game);
        int coinMass = commonObjDat.mass(coin);
        if (coinMass <= 0 || commonObjDat.mass(food[0]) <= 0) { _problem = $"masses {coinMass} (coin) and {commonObjDat.mass(food[0])} (food), expected above 0"; return; }

        int Make(int item, int qty, int quality)
        {
            int index = TradeHelp.Make(item, qty);
            if (index != 0) objs[index].quality = (short)quality;
            return index;
        }
        void ClearSlots() { for (int s = 0; s < uimanager.NoOfTradeSlots; s++) { uimanager.SetNPCTradeSlot(s, -1); uimanager.SetPlayerTradeSlot(s, -1); } }

        if (talker.LootSpawnedFlag == 0) npc.spawnloot(talker);
        int savedLink = talker.link;
        int savedLikes = ConversationVM.Likes, savedDislikes = ConversationVM.Dislikes, savedGreed = ConversationVM.TradeThreshold;
        int savedPatience = ConversationVM.TradePatience, savedAccuracy = ConversationVM.NPCAppraisalAccuracy, savedLast = ConversationVM.PreviousEvaluation, savedAppraise = playerdat.Appraise;
        var savedValues = ConversationVM.StackValues; int savedStack = ConversationVM.stack, savedPtr = ConversationVM.stackptr;
        short savedWeight = playerdat.WeightMax;
        try
        {
            ConversationVM.StackValues = new short[64];
            ConversationVM.stack = 0;
            ConversationVM.Likes = 0; ConversationVM.Dislikes = 0;
            // greed out of reach and valuations without blur, so no offer is accepted and the values are exact
            ConversationVM.TradeThreshold = short.MaxValue;
            ConversationVM.NPCAppraisalAccuracy = 0;
            ConversationVM.TradePatience = 50;
            ConversationVM.PreviousEvaluation = 0;
            playerdat.Appraise = 20;
            int acc = ConversationVM.NPCAppraisalAccuracy;
            int appraiseAcc = 50 - playerdat.Appraise * 45 / 30;

            void Offer(int n)
            {
                // do_offer's five string arguments at sp - 5 .. sp - 1, each pointing at string 0.
                ConversationVM.stackptr = 6;
                for (int k = 1; k <= 5; k++) ConversationVM.Set(k, 20);
                ConversationVM.Set(20, 0);
                TradeHelp.RunImport("do_offer", talker);
                _gain[n] = ConversationVM.PreviousEvaluation;
                ConversationVM.AppraiseOffer(out _apPlayer[n], out _apNpc[n]);
            }

            // The NPC's inventory is one food item, which setup_to_barter puts in its slot 0.
            ClearSlots();
            int foodA = Make(food[0], 0, 63), ruby63 = Make(Ruby, 1, 63), ruby50 = Make(Ruby, 1, 50), foodC = Make(food[1], 0, 63);
            if (foodA == 0 || ruby63 == 0 || ruby50 == 0 || foodC == 0) { _problem = "the trade items could not be made"; return; }
            objs[foodA].next = 0;
            talker.link = (short)foodA;
            ConversationVM.setup_to_barter(talker);
            if (uimanager.GetNPCTradeSlot(0, false) != foodA) { _problem = "setup_to_barter did not put the food item in NPC slot 0"; return; }
            uimanager.NPCTradeOn(0);
            uimanager.SetPlayerTradeSlot(0, ruby63, selected: true);

            // Two offers of the same items.
            Offer(0);
            Offer(1);

            // A second setup_to_barter puts a ruby of quality 50 in NPC slot 0; npc_appraisals[0] and npc_judgement[0] are kept.
            TradeHelp.RunImport("do_decline", talker);
            objs[ruby50].next = talker.link;
            talker.link = (short)ruby50;
            ConversationVM.setup_to_barter(talker);
            if (uimanager.GetNPCTradeSlot(0, false) != ruby50) { _problem = "the second setup_to_barter did not put the ruby in NPC slot 0"; return; }
            uimanager.NPCTradeOn(0);
            for (int s = 1; s < uimanager.NoOfTradeSlots; s++) uimanager.NpcTradeOff(s);
            int rubyValue = ConversationVM.AssessValue(true, ruby63, acc);
            int ruby50Value = ConversationVM.AssessValue(false, ruby50, acc);
            _freshStaleGain = ConversationVM.TradeGain(rubyValue, ruby50Value);
            Offer(2);

            // give_to_npc empties player slot 0 without clearing its valuations; take_id_from_npc then puts a food item there and
            // clears play_judgement[0] and play_appraisals[0] (DOS clears npc_appraisals[0] in place of the latter, a slip not copied).
            ConversationVM.GiveItemIndexToNPC(talker, ruby63);
            objs[foodC].next = talker.link;
            talker.link = (short)foodC;
            playerdat.WeightMax = 0;
            ConversationVM.stackptr = 3;
            ConversationVM.Set(2, 10);
            ConversationVM.Set(10, (short)foodC);
            ConversationVM.result_register = -1;
            TradeHelp.RunImport("take_id_from_npc", talker);
            _takeResult = ConversationVM.result_register;
            playerdat.WeightMax = savedWeight;
            _takenToSlot = uimanager.GetPlayerTradeSlot(0, false) == foodC;
            uimanager.PlayerTradeOn(0);
            int foodAValue = ConversationVM.AssessValue(false, foodA, acc);
            _expectSlipGain = ConversationVM.TradeGain(ConversationVM.AssessValue(true, foodC, acc), foodAValue);
            _freshSlipGain = ConversationVM.TradeGain(rubyValue, ruby50Value);
            _expectSlipApPlayer = ConversationVM.AssessValue(false, foodC, appraiseAcc);
            Offer(3);
            c.Note($"gains {string.Join(", ", _gain)}; appraisals player {string.Join(", ", _apPlayer)}, NPC {string.Join(", ", _apNpc)}; fresh gains {_freshStaleGain}, {_freshSlipGain}");

            // The weight check on a single item.
            ClearSlots();
            int single = Make(food[0], 0, 63);
            if (single == 0) { _problem = "the single item could not be made"; return; }
            uimanager.SetPlayerTradeSlot(0, single, selected: true);
            playerdat.WeightMax = (short)(playerdat.WeightCarried + commonObjDat.mass(food[0]) - 1);
            uimanager.instance.scroll.Clear();
            _singleRefused = !uimanager.PickUpFromTradeSlot(0, true) && playerdat.ObjectInHand == -1;
            await c.Frames(5);
            _singleStays = uimanager.GetPlayerTradeSlot(0, false) == single;
            _singleSaid = ScrollText().Contains(_tooHeavy);
            if (playerdat.ObjectInHand != -1) { playerdat.ObjectInHand = -1; uimanager.instance.mousecursor.SetCursorToCursor(); }

            // A stack: room for 3 coins, take 3 of 10.
            ClearSlots();
            playerdat.WeightMax = (short)(playerdat.WeightCarried + 3 * coinMass);
            _stack = Make(coin, 10, 40);
            _stack2 = Make(coin, 10, 40);
            if (_stack == 0 || _stack2 == 0) { _problem = "no coins could be made"; return; }
            uimanager.SetPlayerTradeSlot(1, _stack, selected: true);
            uimanager.SetPlayerTradeSlot(2, _stack2, selected: true);
            uimanager.PickUpFromTradeSlot(1, true);
            for (int f = 0; f < 30 && !MessageDisplay.WaitingForTypedInput; f++) await c.Frames(1);
            _asked = MessageDisplay.WaitingForTypedInput;
            _askSaid = ScrollText().Contains("Move how many?");
            if (_asked)
            {
                uimanager.instance.TypedInput.Text = "3";
                MessageDisplay.WaitingForTypedInput = false;
                for (int f = 0; f < 30 && playerdat.ObjectInHand == -1; f++) await c.Frames(1);
            }
            _hand = playerdat.ObjectInHand;
            if (_hand != -1) _handQty = objs[_hand].ObjectQuantity;
            _restIndex = uimanager.GetPlayerTradeSlot(1, false);
            if (_restIndex > 0)
            {
                _restQty = objs[_restIndex].ObjectQuantity;
                _restUnselected = uimanager.GetPlayerTradeSlot(1, true) == -1;
                _restSameItem = objs[_restIndex].item_id == coin && objs[_restIndex].quality == 40;
            }
            if (playerdat.ObjectInHand != -1) { playerdat.ObjectInHand = -1; uimanager.instance.mousecursor.SetCursorToCursor(); }

            // The same, asking for 5, which is too heavy.
            uimanager.instance.scroll.Clear();
            _freePtrBefore = UWTileMap.current_tilemap.StaticFreeListPtr;
            uimanager.PickUpFromTradeSlot(2, true);
            for (int f = 0; f < 30 && !MessageDisplay.WaitingForTypedInput; f++) await c.Frames(1);
            _asked2 = MessageDisplay.WaitingForTypedInput;
            if (_asked2)
            {
                uimanager.instance.TypedInput.Text = "5";
                MessageDisplay.WaitingForTypedInput = false;
            }
            await c.Frames(10);
            _freePtrAfter = UWTileMap.current_tilemap.StaticFreeListPtr;
            _heavySaid = ScrollText().Contains(_tooHeavy);
            _hand2 = playerdat.ObjectInHand;
            _slot2After = uimanager.GetPlayerTradeSlot(2, false);
            _stack2Qty = objs[_stack2].ObjectQuantity;
            c.Note($"single: refused {_singleRefused}, said {_singleSaid}; take 3: asked {_asked}, hand {_hand} ({_handQty}), slot {_restIndex} ({_restQty}); take 5: asked {_asked2}, said {_heavySaid}, hand {_hand2}, slot {_slot2After} ({_stack2Qty}), free list {_freePtrBefore} -> {_freePtrAfter}");
        }
        finally
        {
            MessageDisplay.WaitingForTypedInput = false;
            ConversationVM.StackValues = savedValues; ConversationVM.stack = savedStack; ConversationVM.stackptr = savedPtr;
            ConversationVM.Likes = savedLikes; ConversationVM.Dislikes = savedDislikes; ConversationVM.TradeThreshold = savedGreed;
            ConversationVM.TradePatience = savedPatience; ConversationVM.NPCAppraisalAccuracy = savedAccuracy; ConversationVM.PreviousEvaluation = savedLast; playerdat.Appraise = savedAppraise;
            playerdat.WeightMax = savedWeight;
            ConversationVM.TradeResult = 0;
            if (playerdat.ObjectInHand != -1) { playerdat.ObjectInHand = -1; uimanager.instance.mousecursor.SetCursorToCursor(); }
            ClearSlots();
            talker.link = (short)savedLink;
        }
    }

    /// <summary>
    /// Runs the conversation to its end, as barter-accept-deal does, so that it does not hold up the next scenario's game. The
    /// slots and the hand are empty by now, so end_barter puts nothing down.
    /// </summary>
    public override async Task Act(ScenarioContext c)
    {
        if (_problem != null) return;
        int menus = 0;
        ulong deadline = Time.GetTicksMsec() + EndMs;
        while (Time.GetTicksMsec() < deadline && uimanager.CurrentGameMode == uimanager.GameModes.CONVERSATION && menus <= MaxMenus)
        {
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
        c.Note($"{menus} menus answered; mode now {uimanager.CurrentGameMode}");
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        bool uw1 = c.Game == "uw1";
        string cache = uw1 ? "uw1 BARTER.C:648-651, 759-762, 788-803" : "uw2 BARTER.C:611-614, 737-740, 767-782";
        string give = uw1 ? "uw1 BARTER.C:979, 1015" : "uw2 BARTER.C:959, 995";
        string take = uw1 ? "uw1 BARTER.C:357-376" : "uw2 BARTER.C:318-336";
        var k = new Checks();
        k.Equal("the second offer's gain, same items", _gain[0], _gain[1], cache);
        k.Equal("the second appraisal of the player's side", _apPlayer[0], _apPlayer[1], cache);
        k.Equal("the second appraisal of the NPC's side", _apNpc[0], _apNpc[1], cache);

        k.True(_freshStaleGain != _gain[0], $"precondition: the ruby of quality 50 valued afresh must give a gain other than the food item's ({_freshStaleGain})");
        k.Equal("the gain after a second setup_to_barter (NPC slot 0 still valued as the food item)", _gain[0], _gain[2], cache);
        k.Equal("the appraisal of the NPC's side after a second setup_to_barter", _apNpc[0], _apNpc[2], cache);

        k.Equal("take_id_from_npc's result", 1, _takeResult, give);
        k.True(_takenToSlot, $"the food item expected in player slot 0 ({give})");
        k.True(_freshSlipGain != _expectSlipGain, $"precondition: DOS's slip (old ruby, NPC slot 0 afresh) must give a gain other than the corrected one ({_freshSlipGain})");
        k.Equal("the gain after take_id_from_npc (player slot 0 afresh, NPC slot 0 still the food item)", _expectSlipGain, _gain[3], give);
        k.Equal("the appraisal of the player's side after take_id_from_npc (afresh)", _expectSlipApPlayer, _apPlayer[3], give);
        k.Equal("the appraisal of the NPC's side after take_id_from_npc (still the food item)", _apNpc[0], _apNpc[3], give);

        k.True(_singleRefused, $"picking up an item too heavy to carry expected to fail ({take})");
        k.True(_singleStays, $"the item too heavy to take expected still in its slot ({take})");
        k.True(_singleSaid, $"expected \"{_tooHeavy}\" ({take})");

        k.True(_asked, $"picking up 10 coins expected to ask how many ({take})");
        k.True(_askSaid, $"expected \"Move how many?\" ({take})");
        k.Equal("the object in the hand (the stack itself)", _stack, _hand, take);
        k.Equal("coins in the hand", 3, _handQty, take);
        k.True(_restIndex > 0 && _restIndex != _stack, $"a new object expected in the slot with the rest, found {_restIndex} ({take})");
        k.Equal("coins left in the slot", 7, _restQty, take);
        k.True(_restUnselected, $"the slot expected unselected after the pick ({take})");
        k.True(_restSameItem, $"the rest expected to be the same coins, quality 40 ({take})");

        k.True(_asked2, $"picking up the second 10 coins expected to ask how many ({take})");
        k.True(_heavySaid, $"5 coins with room for 3 expected \"{_tooHeavy}\" ({take})");
        k.Equal("the hand after the refused 5", -1, _hand2, take);
        k.Equal("the slot after the refused 5", _stack2, _slot2After, take);
        k.Equal("coins in the stack after the refused 5", 10, _stack2Qty, take);
        k.Equal("the static free list after the refused 5 (the copy freed)", _freePtrBefore, _freePtrAfter, take);
        return k.Result;
    }
}
