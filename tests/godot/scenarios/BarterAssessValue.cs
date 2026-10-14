using System.Threading.Tasks;
using Godot;

namespace Underworld.Scenarios;

/// <summary>
/// A trader values items as assess_value does, and an offer's gain is worked out in 16 bits (UW1 and UW2).
///
/// assess_value takes the class value, 0 for an item the NPC dislikes and 1.5 times for one it likes (does_npc_like:
/// an exact like wins at once), times the quantity, times the quality / 64, then blurs it by up to accuracy % with
/// range after srand(object index) (uw1/src/conv/BARTER.C:810-851, uw2/src/conv/BARTER.C:789-831). The products are
/// 16-bit (Turbo C's int), except UW2's quality product, a long. total_offering sums a side in 16 bits
/// (uw1 BARTER.C:788-803, uw2 BARTER.C:767-782). do_offer values the player's side with likes and the NPC's without,
/// and its gain (player - npc) * 100 / npc is 16-bit, so once the sides differ by more than 327 it wraps and a gross
/// overpayment is refused (uw1 BARTER.C:648-678, uw2 BARTER.C:611-639); do_judgement's gain wraps the same way and
/// calls it "an excellent deal" (uw1 BARTER.C:757-779, uw2 BARTER.C:735-758). A first offer of half of greed or more
/// costs UW1 nothing (uw1 BARTER.C:665-669); UW2 goes on to the gap test (uw2 BARTER.C:628-637).
///
/// The port's GetTradeValueOfItem worked out the blur and threw it away, ignored the quantity, let a dislike by class
/// beat an exact like, and had no 16-bit arithmetic; do_offer valued the NPC's side with likes and applied UW2's
/// first-offer rule in UW1 too (src/conversation/conversation_functions/conversationtrade.cs:152-250, do_offer.cs).
///
/// The scenario starts a conversation with the first NPC on level 1 that has one, then:
/// - values 40 rubies (0xA2) of quality 40 at accuracy 0: UW1 25 * 40 = 1000, * 40 = 40000, -25536 in 16 bits,
///   >> 6 = -399; UW2 50 * 40 = 2000, * 40 as a long = 80000, >> 6 = 1250;
/// - values the same at accuracy 50, which must be range's blur of that value with rand() seeded by the object's index,
///   and the same on a second call;
/// - values one ruby of quality 63 with an exact like of 0xA2 and a dislike of its class: liked, UW1 25 * 3 >> 1 = 37,
///   * 63 >> 6 = 36; UW2 75 * 63 >> 6 = 73;
/// - the overpay: the player offers rubies of quality 40 (UW1 26, 25 * 26 * 40 >> 6 = 406, as underworld-exhumed's
///   uw1 session overpay; UW2 16, 50 * 16 * 40 >> 6 = 500) for one item of value 5 and quality 63 (5 * 63 >> 6 = 4),
///   greed 10, accuracy 0: the gain is (406 - 4) * 100 = 40200, -25336 in 16 bits, / 4 = -6334 (UW2: 49600, -15936,
///   -3984), so do_offer returns 0, a first offer under half of greed costs 1 patience, and nothing changes hands; the
///   player's appraisal (appraise 30, accuracy 5) must wrap to -50 or less, "an excellent deal";
/// - a normal case: one ruby of quality 63 (UW1 24, UW2 49) for one of quality 50 (UW1 19, UW2 39), gain 26 (UW2 25),
///   first with greed 40 (UW1 says nothing and keeps its patience; UW2's gap test (40 * 3) / 2 = 60 > 40 - 25 costs
///   1), then with greed 20, which accepts and takes the ruby.
/// </summary>
public sealed class BarterAssessValue : Scenario
{
    const ulong StartMs = 10000;
    const int Ruby = 0xA2;

    public override string Name => "barter-assess-value";
    public override string Cites => "underworld-exhumed uw1/src/conv/BARTER.C:648-678, 757-779, 788-851, 1083-1109; uw2/src/conv/BARTER.C:611-639, 735-758, 767-831, 1046-1072";

    string _problem;
    int _cheap;
    int _r40, _r40Index, _r40Blur, _r40Blur2, _r40BlurExpected, _liked;
    int _overQty, _overPlayer, _overNpc, _overResult = -1, _overPatience, _overLast, _overGain32;
    bool _overSlotsKept;
    int _judgePlayer, _judgeNpc, _judgeGain, _judgePlayerExpected, _judgeGainExpected;
    int _normPlayer, _normNpc, _normPatience1, _normLast1, _normResult1 = -1, _normResult2 = -1;
    bool _normTraded;

    /// <summary>
    /// range(value, -accuracy, accuracy) after srand(seed), transcribed from the original: value plus
    /// (value * (-accuracy + rand() * 2 * accuracy / 0x8000)) / 100, the product and the sum 16-bit
    /// (uw1 BARTER.C:839-850, uw2 BARTER.C:819-830).
    /// </summary>
    static int OriginalBlur(int value, int accuracy, int seed)
    {
        int pct = -accuracy + (int)((long)new BorlandRandSource((uint)seed).Rand() * (2 * accuracy) / 0x8000);
        return (short)(value + (short)(value * pct) / 100);
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

        // The expected numbers below assume these class values (COMOBJ.DAT): the ruby 25 in UW1 and 50 in UW2, and an item of 5.
        _cheap = uw1 ? 0xBA : 0xBB;
        int rubyValue = uw1 ? 25 : 50;
        if (commonObjDat.monetaryvalue(Ruby) != rubyValue || commonObjDat.monetaryvalue(_cheap) != 5)
        { _problem = $"class values {commonObjDat.monetaryvalue(Ruby)} and {commonObjDat.monetaryvalue(_cheap)}, expected {rubyValue} and 5"; return; }
        for (int s = 0; s < uimanager.NoOfTradeSlots; s++) { uimanager.SetNPCTradeSlot(s, -1); uimanager.SetPlayerTradeSlot(s, -1); }

        int Make(int item, int qty, int quality)
        {
            int index = TradeHelp.Make(item, qty);
            if (index != 0) objs[index].quality = (short)quality;
            return index;
        }

        int savedLikes = ConversationVM.Likes, savedDislikes = ConversationVM.Dislikes, savedGreed = ConversationVM.TradeThreshold;
        int savedPatience = ConversationVM.TradePatience, savedAccuracy = ConversationVM.NPCAppraisalAccuracy;
        int savedLast = ConversationVM.PreviousEvaluation, savedBonus = ConversationVM.BablTradeBonus, savedAppraise = playerdat.Appraise;
        var savedValues = ConversationVM.StackValues; int savedStack = ConversationVM.stack, savedPtr = ConversationVM.stackptr;
        try
        {
            ConversationVM.StackValues = new short[64];
            ConversationVM.stack = 0;
            // do_offer's five string arguments at sp - 5 .. sp - 1, each pointing at string 0.
            ConversationVM.stackptr = 6;
            for (int k = 1; k <= 5; k++) ConversationVM.Set(k, 20);
            ConversationVM.Set(20, 0);
            // set_likes_dislikes' lists: an exact like of the ruby, a dislike of its class ((0xA2 >> 4) + 1000), each ending in -1.
            ConversationVM.Set(30, Ruby); ConversationVM.Set(31, -1);
            ConversationVM.Set(40, (Ruby >> 4) + 1000); ConversationVM.Set(41, -1);
            ConversationVM.BablTradeBonus = 0;

            // assess_value of 40 rubies, without likes: the quality product.
            _r40Index = Make(Ruby, 40, 40);
            if (_r40Index == 0) { _problem = "no rubies could be made"; return; }
            ConversationVM.Likes = 0; ConversationVM.Dislikes = 0;
            _r40 = ConversationVM.AssessValue(false, _r40Index, 0);
            _r40BlurExpected = OriginalBlur(uw1 ? -399 : 1250, 50, _r40Index);
            _r40Blur = ConversationVM.AssessValue(false, _r40Index, 50);
            _r40Blur2 = ConversationVM.AssessValue(false, _r40Index, 50);

            // An exact like beats a dislike of the class.
            int one = Make(Ruby, 1, 63);
            if (one == 0) { _problem = "no ruby could be made"; return; }
            ConversationVM.Likes = 30; ConversationVM.Dislikes = 40;
            _liked = ConversationVM.AssessValue(true, one, 0);
            ConversationVM.Likes = 0; ConversationVM.Dislikes = 0;

            // The overpay.
            _overQty = uw1 ? 26 : 16;
            int rubies = Make(Ruby, _overQty, 40), item = Make(_cheap, 0, 63);
            if (rubies == 0 || item == 0) { _problem = "the overpay's items could not be made"; return; }
            uimanager.SetPlayerTradeSlot(0, rubies, selected: true);
            uimanager.SetNPCTradeSlot(0, item, selected: true);
            ConversationVM.NPCAppraisalAccuracy = 0;
            ConversationVM.TradeThreshold = 10;
            ConversationVM.TradePatience = 5;
            ConversationVM.PreviousEvaluation = 0;
            _overPlayer = ConversationVM.TotalOffering(true, true, 0);
            _overNpc = ConversationVM.TotalOffering(false, false, 0);
            _overGain32 = (_overPlayer - _overNpc) * 100 / System.Math.Max(1, _overNpc);
            ConversationVM.result_register = -1;
            TradeHelp.RunImport("do_offer", talker);
            _overResult = ConversationVM.result_register;
            _overPatience = ConversationVM.TradePatience;
            _overLast = ConversationVM.PreviousEvaluation;
            _overSlotsKept = uimanager.GetPlayerTradeSlot(0, false) == rubies && uimanager.GetNPCTradeSlot(0, false) == item;

            // The player's appraisal of the same offer: appraise 30, accuracy 50 - 30 * 45 / 30 = 5.
            playerdat.Appraise = 30;
            _judgeGain = ConversationVM.AppraiseOffer(out _judgePlayer, out _judgeNpc);
            _judgePlayerExpected = OriginalBlur(uw1 ? 406 : 500, 5, rubies);
            int npcExpected = OriginalBlur(4, 5, item);
            _judgeGainExpected = npcExpected > 0 ? (short)((_judgePlayerExpected - npcExpected) * 100) / npcExpected : 100;
            c.Note($"overpay: values {_overPlayer} and {_overNpc}, do_offer {_overResult}, gain {_overLast} (32-bit {_overGain32}), patience {_overPatience}; appraisal {_judgePlayer} and {_judgeNpc}, gain {_judgeGain}");

            // A normal offer.
            for (int s = 0; s < uimanager.NoOfTradeSlots; s++) { uimanager.SetNPCTradeSlot(s, -1); uimanager.SetPlayerTradeSlot(s, -1); }
            int mine = Make(Ruby, 1, 63), theirs = Make(Ruby, 1, 50);
            if (mine == 0 || theirs == 0) { _problem = "the normal offer's items could not be made"; return; }
            uimanager.SetPlayerTradeSlot(0, mine, selected: true);
            uimanager.SetNPCTradeSlot(0, theirs, selected: true);
            ConversationVM.TradeThreshold = 40;
            ConversationVM.TradePatience = 5;
            ConversationVM.PreviousEvaluation = 0;
            _normPlayer = ConversationVM.TotalOffering(true, true, 0);
            _normNpc = ConversationVM.TotalOffering(false, false, 0);
            ConversationVM.result_register = -1;
            TradeHelp.RunImport("do_offer", talker);
            _normResult1 = ConversationVM.result_register;
            _normPatience1 = ConversationVM.TradePatience;
            _normLast1 = ConversationVM.PreviousEvaluation;
            ConversationVM.TradeThreshold = 20;
            ConversationVM.result_register = -1;
            TradeHelp.RunImport("do_offer", talker);
            _normResult2 = ConversationVM.result_register;
            _normTraded = uimanager.GetPlayerTradeSlot(0, false) == -1 && TradeHelp.Chain(talker).Contains(mine);
            c.Note($"normal: values {_normPlayer} and {_normNpc}; greed 40: do_offer {_normResult1}, gain {_normLast1}, patience {_normPatience1}; greed 20: do_offer {_normResult2}");
        }
        finally
        {
            ConversationVM.StackValues = savedValues; ConversationVM.stack = savedStack; ConversationVM.stackptr = savedPtr;
            ConversationVM.Likes = savedLikes; ConversationVM.Dislikes = savedDislikes; ConversationVM.TradeThreshold = savedGreed;
            ConversationVM.TradePatience = savedPatience; ConversationVM.NPCAppraisalAccuracy = savedAccuracy;
            ConversationVM.PreviousEvaluation = savedLast; ConversationVM.BablTradeBonus = savedBonus; playerdat.Appraise = savedAppraise;
            ConversationVM.TradeResult = 0;
            for (int s = 0; s < uimanager.NoOfTradeSlots; s++) { uimanager.SetNPCTradeSlot(s, -1); uimanager.SetPlayerTradeSlot(s, -1); }
        }
    }

    public override Task Act(ScenarioContext c) => Task.CompletedTask;

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        bool uw1 = c.Game == "uw1";
        string assess = uw1 ? "uw1 BARTER.C:810-851" : "uw2 BARTER.C:789-831";
        string like = uw1 ? "uw1 BARTER.C:819-823, 1083-1109" : "uw2 BARTER.C:798-802, 1046-1072";
        string offer = uw1 ? "uw1 BARTER.C:648-678" : "uw2 BARTER.C:611-639";
        string judge = uw1 ? "uw1 BARTER.C:757-779" : "uw2 BARTER.C:735-758";
        var k = new Checks();
        k.Equal("40 rubies of quality 40, accuracy 0", uw1 ? -399 : 1250, _r40, assess);
        k.Equal($"40 rubies at accuracy 50 (range after srand({_r40Index}))", _r40BlurExpected, _r40Blur, assess);
        k.Equal("40 rubies at accuracy 50, valued again", _r40Blur, _r40Blur2, assess);
        k.Equal("a ruby of quality 63, liked exactly and disliked by class", uw1 ? 36 : 73, _liked, like);

        k.Equal($"{_overQty} rubies of quality 40 offered, valued by the NPC", uw1 ? 406 : 500, _overPlayer, assess);
        k.Equal("the NPC's item of value 5 and quality 63", 4, _overNpc, assess);
        k.Equal("the overpay's gain, wrapped in 16 bits (last_offer)", uw1 ? -6334 : -3984, _overLast, offer);
        k.Equal("do_offer's result for the overpay", 0, _overResult, offer);
        k.Equal("patience after the overpay (a first offer under half of greed)", 4, _overPatience, offer);
        k.True(_overSlotsKept, $"the overpay's items expected still in their slots ({offer})");
        k.Equal("the player's appraisal of the rubies (accuracy 5)", _judgePlayerExpected, _judgePlayer, judge);
        k.Equal("the player's appraisal's gain", _judgeGainExpected, _judgeGain, judge);
        k.True(_judgeGain <= -50, $"the player's appraisal of the overpay expected \"an excellent deal\" (gain -50 or less), gain {_judgeGain} ({judge})");

        k.Equal("the normal offer's ruby of quality 63", uw1 ? 24 : 49, _normPlayer, assess);
        k.Equal("the NPC's ruby of quality 50", uw1 ? 19 : 39, _normNpc, assess);
        k.Equal("the normal offer's gain (last_offer)", uw1 ? 26 : 25, _normLast1, offer);
        k.Equal("do_offer's result at greed 40", 0, _normResult1, offer);
        k.Equal("patience after a first offer of half of greed or more", uw1 ? 5 : 4, _normPatience1, offer);
        k.Equal("do_offer's result at greed 20", 1, _normResult2, offer);
        k.True(_normTraded, $"the accepted offer's ruby expected in the NPC's inventory ({offer})");
        return k.Result;
    }
}
