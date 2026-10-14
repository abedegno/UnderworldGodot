using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace Underworld.Scenarios;

/// <summary>
/// setup_to_barter's choice of items and do_demand's outcome (UW1 and UW2).
///
/// setup_to_barter looks at up to 0x28 objects of the NPC's inventory (count++ &lt; 0x28). It skips the first weapon, anything
/// worthless and, once every slot has been filled, an item when (rand() &amp; 7) &lt; 5, a chance of 5 in 8. The draw is made only
/// once every slot is full and only for an item the first two tests let through (uw1 BARTER.C:133-140, uw2 BARTER.C:100-107).
/// The port stopped after 39 objects and drew Rng.r.Next(0, 7) &lt; 5, 5 in 7, for every item it looked at
/// (src/conversation/conversation_functions/setup_to_barter.cs).
///
/// do_demand scores the player (level + drawn + health + charisma / 6) against the NPC (class level + mood + health + value
/// demanded / 10), where mood is -1 for an ally, 1 for attitude below 2, else 0. The player must score more; in UW1 an ally
/// also always gives in, and giving in lowers npc_attitude when it is above 0. UW2 has no ally rule and lowers it only above 1
/// (uw1 BARTER.C:707-730, uw2 BARTER.C:682-707). The port had no UW1 ally rule, lowered the attitude only above 1 in UW1, and
/// read the ally bit as npc_hunger &amp; 0x40, which is always 0 because npc_hunger is the low 6 bits of byte 0x19, so an ally never
/// got its -1 (src/conversation/conversation_functions/do_demand.cs).
///
/// The scenario starts a conversation with the first NPC on level 1 that has one, and:
/// - gives it 39 worthless items and then a valued food item: setup_to_barter must put the food item in NPC slot 0;
/// - gives it two valued weapons and a food item: the first weapon must stay in the inventory and the other two fill slots 0 and 1;
/// - gives it a food item for each slot and then worthless and valued items in turn, with a Turbo C rand() seed chosen so that
///   at least one later item replaces a slot's item and at least one is skipped by chance: the slots, the inventory and the
///   generator's state afterwards must be those of the original's loop;
/// - demands one selected food item four times, with the player's level set so the scores fall where wanted: a non-ally with
///   attitude 1 the player beats (gives in; attitude 0 in UW1, 1 in UW2); an ally with attitude 2 the player cannot beat even
///   with the ally's -1 (UW1 gives in, UW2 refuses); an ally the player beats only thanks to that -1 (gives in); and the same
///   NPC not an ally, a tie (refuses).
/// </summary>
public sealed class BarterSetupAndDemand : Scenario
{
    const ulong StartMs = 10000, EndMs = 30000;
    const int MaxMenus = 40;

    public override string Name => "barter-setup-and-demand";
    public override string Cites => "underworld-exhumed uw1/src/conv/BARTER.C:133-154, 707-730; uw2/src/conv/BARTER.C:100-122, 682-707";

    string _problem;
    // setup_to_barter
    int _foodAt40 = -1, _slot0At40 = -2;
    int _weapon1 = -1, _weapon2 = -1, _weaponFood = -1;
    int[] _weaponSlots;
    bool _weapon1InInventory;
    uint _seed;
    int[] _expectSlots, _slots;
    List<int> _expectChain, _chain;
    uint _expectRngState, _rngState;
    int _replacements, _chanceSkips;
    // do_demand: result and attitude after, for the four cases
    readonly int[] _result = { -1, -1, -1, -1 }, _attitude = { -1, -1, -1, -1 };
    int _npcBase;

    /// <summary>Turbo C++'s rand(), from a seed, for working out what the original's loop does.</summary>
    sealed class TurboRand
    {
        public uint State;
        public TurboRand(uint seed) { State = seed; }
        public int Rand() { State = State * 0x015A4E35u + 1; return (int)((State >> 16) & 0x7FFF); }
    }

    /// <summary>
    /// The original's setup_to_barter on a list of object indices with all slots empty (uw1 BARTER.C:133-154, uw2 BARTER.C:100-122):
    /// the slots and the inventory after it, and the counts of replacements and chance skips. Obj_Add puts an item back at the head.
    /// </summary>
    static (int[] slots, List<int> chain, int replacements, int chanceSkips) Original(List<int> inventory, int slotCount, TurboRand rand)
    {
        var objs = UWTileMap.current_tilemap.LevelObjects;
        var slots = Enumerable.Repeat(-1, slotCount).ToArray();
        var chain = new List<int>(inventory);
        bool skippedWeapon = false, allFilled = false;
        int slot = 0, count = 0, replacements = 0, chanceSkips = 0;
        foreach (var index in inventory)
        {
            if (!(count++ < 0x28)) break;
            var obj = objs[index];
            bool weapon = obj.OneF0Class == 0;
            bool skip;
            if (weapon && !skippedWeapon) skip = true;
            else if (commonObjDat.monetaryvalue(obj.item_id) == 0) skip = true;
            else if (allFilled && (rand.Rand() & 7) < 5) { skip = true; chanceSkips++; }
            else skip = false;
            if (skip)
            {
                if (weapon) skippedWeapon = true;
                continue;
            }
            chain.Remove(index);
            if (slots[slot] != -1) { chain.Insert(0, slots[slot]); replacements++; }
            slots[slot] = index;
            slot++;
            if (slot >= slotCount) { slot = 0; allFilled = true; }
        }
        return (slots, chain, replacements, chanceSkips);
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
        c.Note($"talking to whoami 0x{talker.npc_whoami:X} (object {talker.index}, item 0x{talker.item_id:X})");

        var food = TradeHelp.ValuedFood();
        if (food.Count == 0) { _problem = "no valued food items"; return; }
        // a worthless item that is not a weapon, nor of minor class 0 (which UW1's own test would also take as a weapon)
        int worthless = Enumerable.Range(0x90, 0x50).FirstOrDefault(id => commonObjDat.monetaryvalue(id) == 0 && (id & 0x30) != 0 && id != 0xA0 && id != 0xA1, -1);
        var weapons = Enumerable.Range(0, 16).Where(id => commonObjDat.monetaryvalue(id) > 0).ToList();
        if (worthless == -1 || weapons.Count < 2) { _problem = $"worthless item 0x{worthless:X}, {weapons.Count} valued weapons"; return; }
        int slotCount = uimanager.NoOfTradeSlots;

        int Make(int item)
        {
            int index = TradeHelp.Make(item, 0);
            if (index != 0) objs[index].quality = 63;
            return index;
        }
        void ClearSlots() { for (int s = 0; s < slotCount; s++) { uimanager.SetNPCTradeSlot(s, -1); uimanager.SetPlayerTradeSlot(s, -1); } }
        void SetInventory(List<int> list)
        {
            for (int k = 0; k < list.Count; k++) objs[list[k]].next = (short)(k + 1 < list.Count ? list[k + 1] : 0);
            talker.link = (short)(list.Count > 0 ? list[0] : 0);
        }
        int[] Slots() => Enumerable.Range(0, slotCount).Select(s => uimanager.GetNPCTradeSlot(s, false)).ToArray();

        if (talker.LootSpawnedFlag == 0) npc.spawnloot(talker);
        int savedLink = talker.link;
        var savedSource = Rng.Source;
        var savedValues = ConversationVM.StackValues; int savedStack = ConversationVM.stack, savedPtr = ConversationVM.stackptr;
        int savedAccuracy = ConversationVM.NPCAppraisalAccuracy, savedLikes = ConversationVM.Likes, savedDislikes = ConversationVM.Dislikes;
        int savedLevel = playerdat.play_level, savedCharm = playerdat.Charm, savedMaxHp = playerdat.max_hp, savedHp = playerdat.play_hp;
        short savedAlly = talker.IsAlly; byte savedGoal = talker.npc_goal, savedGtarg = talker.npc_gtarg; int savedNpcHp = talker.npc_hp; var savedNpcLevel = talker.npc_level;
        try
        {
            // 39 worthless items, then a food item as the 40th
            ClearSlots();
            var list = new List<int>();
            for (int k = 0; k < 39; k++) list.Add(Make(worthless));
            _foodAt40 = Make(food[0]);
            list.Add(_foodAt40);
            if (list.Contains(0)) { _problem = "the items could not be made"; return; }
            SetInventory(list);
            ConversationVM.setup_to_barter(talker);
            _slot0At40 = uimanager.GetNPCTradeSlot(0, false);

            // two weapons and a food item
            ClearSlots();
            _weapon1 = Make(weapons[0]); _weapon2 = Make(weapons[1]); _weaponFood = Make(food[0]);
            SetInventory(new List<int> { _weapon1, _weapon2, _weaponFood });
            ConversationVM.setup_to_barter(talker);
            _weaponSlots = Slots();
            _weapon1InInventory = TradeHelp.Chain(talker).Contains(_weapon1);

            // a food item for each slot, then worthless and valued items in turn
            ClearSlots();
            list = new List<int>();
            for (int k = 0; k < slotCount; k++) list.Add(Make(food[k % food.Count]));
            for (int k = 0; k < 8; k++) { list.Add(Make(worthless)); list.Add(Make(food[k % food.Count])); }
            if (list.Contains(0)) { _problem = "the items could not be made"; return; }
            for (uint seed = 1; seed < 1000; seed++)
            {
                var (s, ch, rep, skips) = Original(list, slotCount, new TurboRand(seed));
                if (rep > 0 && skips > 0) { _seed = seed; break; }
            }
            if (_seed == 0) { _problem = "no seed below 1000 gives both a replacement and a chance skip"; return; }
            var rand = new TurboRand(_seed);
            (_expectSlots, _expectChain, _replacements, _chanceSkips) = Original(list, slotCount, rand);
            _expectRngState = rand.State;
            SetInventory(list);
            var source = new BorlandRandSource(_seed);
            Rng.Source = source;
            ConversationVM.setup_to_barter(talker);
            Rng.Source = savedSource;
            _rngState = source.Seed;
            _slots = Slots();
            _chain = TradeHelp.Chain(talker);
            c.Note($"seed {_seed}: {_replacements} replacements, {_chanceSkips} chance skips; slots {string.Join(",", _slots)} (expected {string.Join(",", _expectSlots)})");

            // do_demand. The stack is a copy of the conversation's, with room above it for the string arguments.
            ClearSlots();
            SetInventory(new List<int>());
            int attitudeAddress = ConversationVM.FindVariableAddress("npc_attitude");
            if (attitudeAddress == 0) { _problem = "the conversation has no npc_attitude"; return; }
            ConversationVM.NPCAppraisalAccuracy = 0;
            ConversationVM.Likes = 0; ConversationVM.Dislikes = 0;
            playerdat.max_hp = 30; playerdat.play_hp = 30; playerdat.Charm = 0;
            int avghit = critterObjectDat.avghit(talker.item_id);
            if (avghit > 0 && avghit < 256) talker.npc_hp = (byte)avghit;
            int npcHealth = avghit > 0 ? 2 - (avghit - talker.npc_hp) * 2 / avghit : 1;
            int probe = Make(food[0]);
            int demanded = ConversationVM.AssessValue(false, probe, 0);
            _npcBase = critterObjectDat.npc_level(talker.item_id) + npcHealth + demanded / 10;
            int playerRest = playerdat.play_drawn + 2 + playerdat.Charm / 6;
            c.Note($"NPC score without mood {_npcBase} (level {critterObjectDat.npc_level(talker.item_id)}, health {npcHealth}, demanded {demanded}); player score without level {playerRest}");
            if (_npcBase - 1 < playerRest) { _problem = $"the NPC's score {_npcBase} is too low to set up an ally the player cannot beat"; return; }

            int Demand(bool ally, int attitude, int playerScore, int n)
            {
                ClearSlots();
                TradeHelp.ForgetValuations();
                int item = Make(food[0]);
                uimanager.SetNPCTradeSlot(0, item, true);
                talker.IsAlly = (short)(ally ? 1 : 0);
                talker.npc_goal = savedGoal; talker.npc_gtarg = savedGtarg;
                playerdat.play_level = playerScore - playerRest;
                var values = new short[savedValues.Length + 64];
                System.Array.Copy(savedValues, values, savedValues.Length);
                ConversationVM.StackValues = values;
                ConversationVM.stack = savedValues.Length;
                ConversationVM.stackptr = 6;
                for (int k = 1; k <= 5; k++) ConversationVM.Set(ConversationVM.stack + k, ConversationVM.stack + 20);
                ConversationVM.Set(ConversationVM.stack + 20, 0);
                ConversationVM.Set(attitudeAddress, attitude);
                ConversationVM.result_register = -1;
                TradeHelp.RunImport("do_demand", talker);
                _result[n] = ConversationVM.result_register;
                _attitude[n] = ConversationVM.at(attitudeAddress);
                ConversationVM.StackValues = savedValues; ConversationVM.stack = savedStack; ConversationVM.stackptr = savedPtr;
                ConversationVM.TradeResult = 0;
                return item;
            }

            // a non-ally with attitude 1 (mood 1) the player beats by one
            Demand(false, 1, _npcBase + 2, 0);
            // an ally with attitude 2 (mood -1) the player cannot beat
            Demand(true, 2, playerRest, 1);
            // an ally the player beats only by its -1, and the same NPC not an ally (mood 0), a tie
            Demand(true, 2, _npcBase, 2);
            Demand(false, 2, _npcBase, 3);
            c.Note($"demands: results {string.Join(", ", _result)}, attitudes after {string.Join(", ", _attitude)}");
        }
        finally
        {
            Rng.Source = savedSource;
            ConversationVM.StackValues = savedValues; ConversationVM.stack = savedStack; ConversationVM.stackptr = savedPtr;
            ConversationVM.NPCAppraisalAccuracy = savedAccuracy; ConversationVM.Likes = savedLikes; ConversationVM.Dislikes = savedDislikes;
            playerdat.play_level = savedLevel; playerdat.Charm = savedCharm; playerdat.max_hp = savedMaxHp; playerdat.play_hp = savedHp;
            talker.IsAlly = savedAlly; talker.npc_goal = savedGoal; talker.npc_gtarg = savedGtarg; talker.npc_hp = (byte)savedNpcHp; talker.npc_level = savedNpcLevel;
            ConversationVM.TradeResult = 0;
            ClearSlots();
            talker.link = (short)savedLink;
        }
    }

    /// <summary>Runs the conversation to its end with the slots empty, as barter-slots-and-valuation does.</summary>
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
        string setup = uw1 ? "uw1 BARTER.C:133-154" : "uw2 BARTER.C:100-122";
        string demand = uw1 ? "uw1 BARTER.C:707-730" : "uw2 BARTER.C:682-707";
        var k = new Checks();
        k.Equal("NPC slot 0 after 39 worthless items and a food item (the 40th object is looked at)", _foodAt40, _slot0At40, setup);

        k.Equal("NPC slot 0 with two weapons and a food item (the first weapon skipped)", _weapon2, _weaponSlots[0], setup);
        k.Equal("NPC slot 1 with two weapons and a food item", _weaponFood, _weaponSlots[1], setup);
        k.True(_weapon1InInventory, $"the first weapon expected still in the NPC's inventory ({setup})");

        k.True(_slots.SequenceEqual(_expectSlots), $"NPC slots after the random round {string.Join(",", _slots)}, expected {string.Join(",", _expectSlots)} ((rand() & 7) < 5 once all are full; {setup})");
        k.True(_chain.SequenceEqual(_expectChain), $"NPC inventory after the random round {string.Join(",", _chain)}, expected {string.Join(",", _expectChain)} ({setup})");
        k.Equal("rand() state after setup_to_barter (one draw per item past the first two tests, once all are full)", _expectRngState, _rngState, setup);

        k.Equal("do_demand, non-ally the player beats: result", 1, _result[0], demand);
        k.Equal("do_demand, non-ally the player beats: npc_attitude 1 after", uw1 ? 0 : 1, _attitude[0], demand);
        k.Equal("do_demand, ally the player cannot beat: result" + (uw1 ? " (UW1: an ally always gives in)" : ""), uw1 ? 1 : 0, _result[1], demand);
        k.Equal("do_demand, ally the player beats only by the ally's -1: result", 1, _result[2], demand);
        k.Equal("do_demand, ally given in: npc_attitude 2 after", 1, _attitude[2], demand);
        k.Equal("do_demand, the same NPC not an ally, a tie: result", 0, _result[3], demand);
        k.Equal("do_demand, refused: npc_attitude 2 after", 2, _attitude[3], demand);
        return k.Result;
    }
}
