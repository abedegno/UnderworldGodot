using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// Coins given from the trade area join the NPC's coin pile (UW1 and UW2). give_to_npc and the
/// trade-area branch of give_ptr_npc both hand the object over with player_barter_give, which
/// calls npc_inv_add (uw1/src/conv/BARTER.C:898-915, uw2/src/conv/BARTER.C:878-895).
/// npc_inv_add adds a coin to a coin pile the NPC already has when the two together stay under
/// STACK_LIMIT 999, and frees the given object; otherwise it adds the object at the head of the
/// NPC's inventory. The coin it merges is ITEM_GOLD_COIN 0xA1 in UW1 (uw1 BARTER.C:874-894) and
/// ITEM_COIN 0xA0 in UW2 (uw2 BARTER.C:854-874).
///
/// The port's GiveItemIndexToNPC, used by both, only linked the object at the head of the NPC's
/// inventory (src/conversation/conversation_functions/conversationtrade.cs:316-334).
///
/// The scenario makes a talker in the mobile list with a pile of 10 coins, puts coins in the
/// player's trade slots, selected, as dropping them there does, and gives them with the VM's own
/// argument layout: give_to_npc(1, [5 coins]), then give_ptr_npc(3 coins, 3), both of which must
/// merge, then give_ptr_npc(981 coins, 981), which must not, since 18 + 981 is not under 999.
/// </summary>
public sealed class GiveMergeCoins : Scenario
{
    public override string Name => "give-merge-coins";
    public override string Cites => "underworld-exhumed uw1/src/conv/BARTER.C:874-915; uw2/src/conv/BARTER.C:854-895";

    string _problem;
    int _coin;
    int _resultA = -1, _resultB = -1, _resultC = -1;
    int _pilesA, _totalA, _pilesB, _totalB, _pilesC, _totalC;
    bool _slotsEmpty;

    public override Task Setup(ScenarioContext c) => Task.CompletedTask;

    public override Task Act(ScenarioContext c)
    {
        _coin = c.Game == "uw1" ? 0xA1 : 0xA0;
        var objs = UWTileMap.current_tilemap.LevelObjects;
        int tslot = ObjectCreator.PrepareNewObject(0x40, ObjectFreeLists.ObjectListType.MobileList);
        if (tslot == 0) { _problem = "no talker could be made"; return Task.CompletedTask; }
        var talker = objs[tslot];
        talker.link = 0;
        talker.LootSpawnedFlag = 1;
        int pile = Coins(10);
        if (pile == 0) { _problem = "no coin pile could be made"; return Task.CompletedTask; }
        objs[pile].next = 0;
        talker.link = (short)pile;

        int a = Coins(5), b = Coins(3), d = Coins(981);
        if (a == 0 || b == 0 || d == 0) { _problem = "no coins could be made"; return Task.CompletedTask; }
        for (int s = 0; s < uimanager.NoOfTradeSlots; s++) uimanager.SetPlayerTradeSlot(s, -1);
        uimanager.SetPlayerTradeSlot(0, a, selected: true);
        uimanager.SetPlayerTradeSlot(1, b, selected: true);
        uimanager.SetPlayerTradeSlot(2, d, selected: true);

        var savedValues = ConversationVM.StackValues; int savedStack = ConversationVM.stack, savedPtr = ConversationVM.stackptr;
        try
        {
            ConversationVM.StackValues = new short[64];
            ConversationVM.stack = 0;
            // give_to_npc(count, indices): at(at(sp - 2)) is the count, at(sp - 1) the array.
            ConversationVM.stackptr = 3;
            ConversationVM.Set(1, 10); ConversationVM.Set(10, 1);
            ConversationVM.Set(2, 20); ConversationVM.Set(20, a);
            ConversationVM.result_register = -1;
            ConversationVM.give_to_npc(talker);
            _resultA = ConversationVM.result_register;
            Count(talker, out _pilesA, out _totalA);

            _resultB = Give(talker, b, 3);
            Count(talker, out _pilesB, out _totalB);
            _resultC = Give(talker, d, 981);
            Count(talker, out _pilesC, out _totalC);
        }
        finally
        {
            ConversationVM.StackValues = savedValues; ConversationVM.stack = savedStack; ConversationVM.stackptr = savedPtr;
            talker.link = 0;
        }
        _slotsEmpty = uimanager.GetPlayerTradeSlot(0, false) == -1 && uimanager.GetPlayerTradeSlot(1, false) == -1 && uimanager.GetPlayerTradeSlot(2, false) == -1;
        c.Note($"coin 0x{_coin:X}; give_to_npc 5: returned {_resultA}, {_pilesA} piles, {_totalA} coins; give_ptr_npc 3: returned {_resultB}, {_pilesB} piles, {_totalB} coins; give_ptr_npc 981: returned {_resultC}, {_pilesC} piles, {_totalC} coins; slots empty {_slotsEmpty}");
        return Task.CompletedTask;
    }

    /// <summary>A free static object made a stack of count coins, or 0.</summary>
    int Coins(int count)
    {
        int slot = ObjectCreator.PrepareNewObject(_coin);
        if (slot == 0) return 0;
        var o = UWTileMap.current_tilemap.LevelObjects[slot];
        o.is_quant = 1;
        o.link = (short)count;
        o.next = 0;
        return slot;
    }

    /// <summary>give_ptr_npc(object, qty): at(at(sp - 1)) is qty, at(at(sp - 2)) the object.</summary>
    static int Give(uwObject talker, int index, int qty)
    {
        ConversationVM.stackptr = 3;
        ConversationVM.Set(2, 12); ConversationVM.Set(12, qty);
        ConversationVM.Set(1, 13); ConversationVM.Set(13, index);
        ConversationVM.result_register = -1;
        ConversationVM.give_ptr_npc(talker);
        return ConversationVM.result_register;
    }

    /// <summary>The coin objects in the NPC's inventory chain and the coins in them.</summary>
    void Count(uwObject npc, out int piles, out int total)
    {
        var objs = UWTileMap.current_tilemap.LevelObjects;
        piles = 0; total = 0;
        int i = npc.link;
        for (int guard = 0; i > 0 && i < objs.Length && guard < 256; guard++)
        {
            var o = objs[i];
            if (o.item_id == _coin) { piles++; total += o.link; }
            i = o.next;
        }
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        string cite = c.Game == "uw1" ? "npc_inv_add, uw1 BARTER.C:874-894" : "npc_inv_add, uw2 BARTER.C:854-874";
        var k = new Checks();
        k.Equal("give_to_npc result", 1, _resultA, c.Game == "uw1" ? "uw1 CONVERSE.C:689-690" : "uw2 CONVERSE.C:674-675");
        k.Equal("NPC's coins after give_to_npc of 5", 15, _totalA, cite);
        k.Equal("NPC's coin piles after give_to_npc of 5", 1, _pilesA, cite);
        k.Equal("give_ptr_npc result", 1, _resultB, c.Game == "uw1" ? "uw1 CONVERSE.C:704-707" : "uw2 CONVERSE.C:694-708");
        k.Equal("NPC's coins after give_ptr_npc of 3", 18, _totalB, cite);
        k.Equal("NPC's coin piles after give_ptr_npc of 3", 1, _pilesB, cite);
        k.Equal("give_ptr_npc result for 981", 1, _resultC, c.Game == "uw1" ? "uw1 CONVERSE.C:704-707" : "uw2 CONVERSE.C:694-708");
        k.Equal("NPC's coins after give_ptr_npc of 981", 999, _totalC, cite);
        k.Equal("NPC's coin piles after give_ptr_npc of 981 (18 + 981 is not under STACK_LIMIT 999)", 2, _pilesC, cite);
        k.True(_slotsEmpty, "a given item was left in its trade slot (player_barter_give clears it)");
        return k.Result;
    }
}
