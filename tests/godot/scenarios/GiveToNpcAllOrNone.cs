using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// The conversation built-ins that hand the player's selected trade items to an NPC (UW1 and UW2).
///
/// find_barter_total(item, count out, indices out, total out) fills its array with the object
/// indices of the matching trade items, `matching[matches] = indices[i]`, and counts an object
/// that is not a quantity as 1 (uw1/src/conv/CONVERSE.C:642-667, uw2/src/conv/CONVERSE.C:628-653).
/// The shipped scripts pass that array on: UW1 conversations 161 and 162 give it to give_to_npc,
/// UW2 conversations 1 and 45 read its first entry's link with x_obj_stuff.
///
/// give_to_npc(count, indices) gives nothing and returns 0 unless every listed index is one of
/// the selected trade items, each trade item matching only once (uw1 CONVERSE.C:671-691,
/// uw2 CONVERSE.C:657-676).
///
/// give_ptr_npc skips a trade entry whose item id is 0 (uw1 CONVERSE.C:705, uw2 CONVERSE.C:692)
/// and then looks for the object in the player's inventory, where a trade item is not, so a hand
/// axe (item 0) in the trade area is not given.
///
/// The port filled the array with item ids, gave whichever listed items it found and returned 1
/// if it found any, and gave the hand axe.
///
/// The scenario puts 3 fish, a shortsword and 4 fish in the player's selected trade slots, keeps
/// 2 more fish out of the trade area, and calls the built-ins with the VM's own argument layout.
/// </summary>
public sealed class GiveToNpcAllOrNone : Scenario
{
    public override string Name => "give-to-npc-all-or-none";
    public override string Cites => "underworld-exhumed uw1/src/conv/CONVERSE.C:642-716; uw2/src/conv/CONVERSE.C:628-712";

    const int Fish = 0xB6, Shortsword = 0x04, HandAxe = 0x00;

    string _problem;
    int _fish3, _fish4, _sword, _stray, _axe;
    int _fbtResult = -1, _fbtCount = -1, _fbtTotal = -1, _fbtFirst = -1, _fbtSecond = -1;
    int _swordResult = -1, _swordCount = -1, _swordTotal = -1, _swordFirst = -1;
    int _strayResult = -1, _twiceResult = -1, _givenResult = -1, _axeResult = -1;
    bool _strayNothingGiven, _twiceNothingGiven, _fishGiven, _fishSlotsEmpty, _swordKept, _axeKept;

    public override Task Setup(ScenarioContext c) => Task.CompletedTask;

    public override Task Act(ScenarioContext c)
    {
        var objs = UWTileMap.current_tilemap.LevelObjects;
        int tslot = ObjectCreator.PrepareNewObject(0x40, ObjectFreeLists.ObjectListType.MobileList);
        if (tslot == 0) { _problem = "no talker could be made"; return Task.CompletedTask; }
        var talker = objs[tslot];
        talker.link = 0;
        talker.LootSpawnedFlag = 1;

        _fish3 = Make(Fish, 3); _sword = Make(Shortsword, 0); _fish4 = Make(Fish, 4);
        _stray = Make(Fish, 2); _axe = Make(HandAxe, 0);
        if (_fish3 == 0 || _sword == 0 || _fish4 == 0 || _stray == 0 || _axe == 0) { _problem = "no objects could be made"; return Task.CompletedTask; }
        for (int s = 0; s < uimanager.NoOfTradeSlots; s++) uimanager.SetPlayerTradeSlot(s, -1);
        uimanager.SetPlayerTradeSlot(0, _fish3, selected: true);
        uimanager.SetPlayerTradeSlot(1, _sword, selected: true);
        uimanager.SetPlayerTradeSlot(2, _fish4, selected: true);

        var savedValues = ConversationVM.StackValues; int savedStack = ConversationVM.stack, savedPtr = ConversationVM.stackptr;
        try
        {
            ConversationVM.StackValues = new short[128];
            ConversationVM.stack = 0;

            _fbtResult = FindBarterTotal(Fish, 40, out _fbtCount, out _fbtTotal);
            _fbtFirst = ConversationVM.at(40); _fbtSecond = ConversationVM.at(41);
            _swordResult = FindBarterTotal(Shortsword, 50, out _swordCount, out _swordTotal);
            _swordFirst = ConversationVM.at(50);

            // a listed object that is not in the trade area: nothing is given
            ConversationVM.Set(60, (short)_fish3); ConversationVM.Set(61, (short)_stray);
            _strayResult = GiveToNpc(talker, 2, 60);
            _strayNothingGiven = talker.link == 0 && uimanager.GetPlayerTradeSlot(0, false) == _fish3;

            // the same trade item listed twice: nothing is given
            ConversationVM.Set(62, (short)_fish3); ConversationVM.Set(63, (short)_fish3);
            _twiceResult = GiveToNpc(talker, 2, 62);
            _twiceNothingGiven = talker.link == 0 && uimanager.GetPlayerTradeSlot(0, false) == _fish3;

            // as UW1's conversations 161 and 162 do: give find_barter_total's array
            _givenResult = GiveToNpc(talker, _fbtCount, 40);
            _fishGiven = InChain(talker, _fish3) && InChain(talker, _fish4);
            _fishSlotsEmpty = uimanager.GetPlayerTradeSlot(0, false) == -1 && uimanager.GetPlayerTradeSlot(2, false) == -1;
            _swordKept = uimanager.GetPlayerTradeSlot(1, false) == _sword && !InChain(talker, _sword);

            // a hand axe in the trade area is skipped by give_ptr_npc
            uimanager.SetPlayerTradeSlot(0, _axe, selected: true);
            ConversationVM.stackptr = 3;
            ConversationVM.Set(1, 12); ConversationVM.Set(12, (short)_axe);
            ConversationVM.Set(2, 13); ConversationVM.Set(13, 1);
            ConversationVM.result_register = -1;
            ConversationVM.give_ptr_npc(talker);
            _axeResult = ConversationVM.result_register;
            _axeKept = uimanager.GetPlayerTradeSlot(0, false) == _axe && !InChain(talker, _axe);
        }
        finally
        {
            ConversationVM.StackValues = savedValues; ConversationVM.stack = savedStack; ConversationVM.stackptr = savedPtr;
            talker.link = 0;
            for (int s = 0; s < uimanager.NoOfTradeSlots; s++) uimanager.SetPlayerTradeSlot(s, -1);
        }
        c.Note($"find_barter_total(fish): {_fbtResult}, count {_fbtCount}, total {_fbtTotal}, array [{_fbtFirst}, {_fbtSecond}] (fish are {_fish3}, {_fish4}); "
            + $"find_barter_total(shortsword): {_swordResult}, count {_swordCount}, total {_swordTotal}, array [{_swordFirst}] (sword is {_sword}); "
            + $"give_to_npc with a stray: {_strayResult}, nothing given {_strayNothingGiven}; listed twice: {_twiceResult}, nothing given {_twiceNothingGiven}; "
            + $"with find_barter_total's array: {_givenResult}, fish given {_fishGiven}, slots empty {_fishSlotsEmpty}, sword kept {_swordKept}; "
            + $"give_ptr_npc(hand axe): {_axeResult}, kept {_axeKept}");
        return Task.CompletedTask;
    }

    /// <summary>A free static object of the item; a quantity of qty when qty is above 0.</summary>
    static int Make(int item, int qty)
    {
        int slot = ObjectCreator.PrepareNewObject(item);
        if (slot == 0) return 0;
        var o = UWTileMap.current_tilemap.LevelObjects[slot];
        o.is_quant = (short)(qty > 0 ? 1 : 0);
        o.link = (short)qty;
        o.next = 0;
        return slot;
    }

    /// <summary>find_barter_total(item, count, array, total): at(at(sp - 4)) is the item, at(sp - 3), at(sp - 2) and at(sp - 1) where count, array and total go.</summary>
    static int FindBarterTotal(int item, int array, out int count, out int total)
    {
        ConversationVM.stackptr = 5;
        ConversationVM.Set(1, 20); ConversationVM.Set(20, (short)item);
        ConversationVM.Set(2, 21); ConversationVM.Set(21, -1);
        ConversationVM.Set(3, (short)array);
        ConversationVM.Set(4, 22); ConversationVM.Set(22, -1);
        ConversationVM.result_register = -1;
        ConversationVM.find_barter_total();
        count = ConversationVM.at(21);
        total = ConversationVM.at(22);
        return ConversationVM.result_register;
    }

    /// <summary>give_to_npc(count, indices): at(at(sp - 2)) is the count, at(sp - 1) the array.</summary>
    static int GiveToNpc(uwObject talker, int count, int array)
    {
        ConversationVM.stackptr = 3;
        ConversationVM.Set(1, 30); ConversationVM.Set(30, (short)count);
        ConversationVM.Set(2, (short)array);
        ConversationVM.result_register = -1;
        ConversationVM.give_to_npc(talker);
        return ConversationVM.result_register;
    }

    static bool InChain(uwObject npc, int index)
    {
        var objs = UWTileMap.current_tilemap.LevelObjects;
        int i = npc.link;
        for (int guard = 0; i > 0 && i < objs.Length && guard < 256; guard++)
        {
            if (i == index) return true;
            i = objs[i].next;
        }
        return false;
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        bool uw1 = c.Game == "uw1";
        string fbt = uw1 ? "uw1 CONVERSE.C:642-667" : "uw2 CONVERSE.C:628-653";
        string give = uw1 ? "uw1 CONVERSE.C:671-691" : "uw2 CONVERSE.C:657-676";
        var k = new Checks();
        k.Equal("find_barter_total(fish) result", 1, _fbtResult, fbt);
        k.Equal("find_barter_total(fish) count", 2, _fbtCount, fbt);
        k.Equal("find_barter_total(fish) total", 7, _fbtTotal, fbt);
        k.Equal("find_barter_total(fish) array[0], the first fish's object index", _fish3, _fbtFirst, fbt);
        k.Equal("find_barter_total(fish) array[1], the second fish's object index", _fish4, _fbtSecond, fbt);
        k.Equal("find_barter_total(shortsword) count", 1, _swordCount, fbt);
        k.Equal("find_barter_total(shortsword) total (not a quantity, counts 1)", 1, _swordTotal, fbt);
        k.Equal("find_barter_total(shortsword) array[0]", _sword, _swordFirst, fbt);
        k.Equal("give_to_npc of a trade item and an object not in the trade area", 0, _strayResult, give);
        k.True(_strayNothingGiven, $"give_to_npc gave the trade item although the other listed object is not in the trade area ({give})");
        k.Equal("give_to_npc of the same trade item twice", 0, _twiceResult, give);
        k.True(_twiceNothingGiven, $"give_to_npc gave a trade item listed twice ({give})");
        k.Equal("give_to_npc of find_barter_total's array", 1, _givenResult, give);
        k.True(_fishGiven, $"give_to_npc of find_barter_total's array did not give both fish to the NPC ({give})");
        k.True(_fishSlotsEmpty, "the given fish were left in their trade slots (player_barter_give clears them)");
        k.True(_swordKept, "the unlisted shortsword left its trade slot");
        k.Equal("give_ptr_npc of a hand axe (item 0) in the trade area", 0, _axeResult, uw1 ? "uw1 CONVERSE.C:705-715" : "uw2 CONVERSE.C:692-711");
        k.True(_axeKept, "give_ptr_npc gave a hand axe from the trade area, an entry the original skips");
        return k.Result;
    }
}
