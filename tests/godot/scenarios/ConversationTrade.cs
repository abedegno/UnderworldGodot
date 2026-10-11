using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;

namespace Underworld.Scenarios;

/// <summary>Helpers shared by the conversation scenarios.</summary>
static class ConvHelp
{
    public const int Coin = 0xA0;   // ITEM_COIN, uwN/src/include/items.h

    /// <summary>
    /// Makes count coins in the hand and puts them in a free backpack slot the way the
    /// inventory does (as GaramonBurial does). Returns the inventory index, or -1.
    /// </summary>
    public static int CoinsInBackpack(int count)
    {
        int slot = ObjectCreator.SpawnObjectInHand(Coin);
        if (slot == 0) return -1;
        var o = UWTileMap.current_tilemap.LevelObjects[slot];
        o.is_quant = 1;
        o.link = (short)count;
        int free = uimanager.FreePaperDollSlot;
        if (free == -1) return -1;
        int index = playerdat.AddObjectToPlayerInventory(slot, false);
        playerdat.SetInventorySlotListHead(free, index);
        playerdat.ObjectInHand = -1;
        uimanager.instance.mousecursor.SetCursorToCursor();
        uimanager.UpdateInventoryDisplay();
        return index;
    }

    /// <summary>The coins the player carries, counted over the inventory objects.</summary>
    public static int PlayerCoins() =>
        playerdat.InventoryObjects.Where(o => o != null && o.item_id == Coin).Sum(o => o.ObjectQuantity);

    /// <summary>The coins in an NPC's inventory chain (its link, then next) in the level objects.</summary>
    public static int NpcCoins(uwObject npc)
    {
        var objs = UWTileMap.current_tilemap.LevelObjects;
        int n = 0, i = npc.link;
        for (int guard = 0; i > 0 && i < objs.Length && guard < 256; guard++)
        {
            var o = objs[i];
            if (o == null) break;
            if (o.item_id == Coin) n += o.ObjectQuantity;
            i = o.next;
        }
        return n;
    }

    /// <summary>Is index in some square's object chain within range squares of (x, y)?</summary>
    public static bool OnMapNear(int index, int x, int y, int range, out int fx, out int fy)
    {
        var map = UWTileMap.current_tilemap;
        for (int ty = y - range; ty <= y + range; ty++)
            for (int tx = x - range; tx <= x + range; tx++)
            {
                if (!UWTileMap.ValidTile(tx, ty)) continue;
                int i = map.Tiles[tx, ty].indexObjectList;
                for (int guard = 0; i != 0 && guard < 1024; guard++)
                {
                    if (i == index) { fx = tx; fy = ty; return true; }
                    i = map.LevelObjects[i].next;
                }
            }
        fx = fy = -1;
        return false;
    }
}

/// <summary>
/// give_ptr_npc takes an object from the player's pack (UW1 and UW2). A conversation's
/// give_ptr_npc(object, qty) gives the NPC an object found with find_inv: if it is not in the
/// trade slots it is taken out of the player's inventory, qty of a stack (all of it when qty is
/// negative or the object is not a quantity), with invRemoveObject, added to the NPC
/// (npc_inv_add), and the call returns 1 (uw1/src/conv/CONVERSE.C:696-716,
/// uw2/src/conv/CONVERSE.C:680-712; UW2 returns 0 for a qty of 0 or less, :688).
///
/// The port's give_ptr_npc looks the index up in the level objects although find_inv returns an
/// index into the player's inventory, links the object into the NPC without taking it out of the
/// player's inventory, and always returns 0 (src/conversation/conversation_functions/give_ptr_npc.cs:28-71).
///
/// The scenario puts five coins in the backpack, finds them as a script does (find_inv(1, 0xA0),
/// item id 0xA0 is major 2 minor 2 class 0), and calls give_ptr_npc for 2 of them and then for
/// the other 3, with the arguments on a conversation stack of its own, as the VM passes them:
/// the top two stack values point at the qty and at the object. The talker is a critter made in
/// the mobile list. The port's calls touch the level object at the inventory index (the player
/// object); its next link is restored afterwards so the level stays sound.
///
/// First, with the static free list made to look empty (StaticFreeListPtr -1, restored
/// afterwards), it picks the coins up from the pack as a click does (uimanager.DoPickup), which
/// must change nothing, and gives 2 and then all 5: the port copies a given object into a new level
/// object, so with no free object the give must fail, return 0, leave the pack as it was and
/// write nothing into object 0, as give_ptr_npc returns 0 when its own allocation fails
/// (uw2 CONVERSE.C:697-704).
/// </summary>
public sealed class GivePtrNpc : Scenario
{
    public override string Name => "give-ptr-npc";
    public override string Cites => "underworld-exhumed uw1/src/conv/CONVERSE.C:696-716; uw2/src/conv/CONVERSE.C:680-712";

    string _problem;
    int _found, _resultA, _resultB, _playerAfterA, _npcAfterA, _playerAfterB, _npcAfterB, _playerBefore;
    int _fullSplit = -1, _fullAll = -1, _playerAfterFull, _npcAfterFull, _pickup = -1, _handBefore, _handAfter;
    bool _obj0Same;

    public override Task Setup(ScenarioContext c)
    {
        if (ConvHelp.PlayerCoins() != 0) { _problem = "the new game's player already carries coins"; return Task.CompletedTask; }
        if (ConvHelp.CoinsInBackpack(5) < 0) { _problem = "no coins could be put in the backpack"; return Task.CompletedTask; }
        _playerBefore = ConvHelp.PlayerCoins();
        return Task.CompletedTask;
    }

    public override Task Act(ScenarioContext c)
    {
        if (_problem != null) return Task.CompletedTask;
        int tslot = ObjectCreator.PrepareNewObject(0x40, ObjectFreeLists.ObjectListType.MobileList);
        if (tslot == 0) { _problem = "no talker could be made"; return Task.CompletedTask; }
        var talker = UWTileMap.current_tilemap.LevelObjects[tslot];
        talker.link = 0;
        talker.LootSpawnedFlag = 1;

        var savedValues = ConversationVM.StackValues; int savedStack = ConversationVM.stack, savedPtr = ConversationVM.stackptr;
        var player = playerdat.playerObject; short playerNext = player.next;
        try
        {
            ConversationVM.StackValues = new short[64];
            ConversationVM.stack = 0;
            ConversationVM.stackptr = 3;
            // find_inv(character, item): at(at(sp - 1)) is the character, at(at(sp - 2)) the item.
            ConversationVM.Set(2, 10); ConversationVM.Set(10, 1);
            ConversationVM.Set(1, 11); ConversationVM.Set(11, ConvHelp.Coin);
            ConversationVM.find_inv(talker);
            _found = ConversationVM.result_register;
            if (_found <= 0) { _problem = "find_inv(1, 0xA0) did not find the coins in the player's inventory"; return Task.CompletedTask; }

            // No free static object: both gives must fail and change nothing.
            var map = UWTileMap.current_tilemap;
            var obj0 = map.LevelObjects[0];
            var obj0Before = new byte[8];
            for (int b = 0; b < 8; b++) obj0Before[b] = obj0.DataBuffer[obj0.PTR + b];
            int savedFree = map.StaticFreeListPtr;
            try
            {
                map.StaticFreeListPtr = -1;
                _handBefore = playerdat.ObjectInHand;
                _pickup = uimanager.DoPickup(playerdat.InventoryObjects[_found]);
                _handAfter = playerdat.ObjectInHand;
                _fullSplit = Give(c, talker, _found, 2);
                _fullAll = Give(c, talker, _found, 5);
            }
            finally { map.StaticFreeListPtr = savedFree; }
            _obj0Same = true;
            for (int b = 0; b < 8; b++) if (obj0.DataBuffer[obj0.PTR + b] != obj0Before[b]) _obj0Same = false;
            _playerAfterFull = ConvHelp.PlayerCoins(); _npcAfterFull = ConvHelp.NpcCoins(talker);
            c.Note($"no free object: pickup returned {_pickup}, hand {_handBefore} -> {_handAfter}; give 2 returned {_fullSplit}, give 5 returned {_fullAll}, player {_playerAfterFull}, NPC {_npcAfterFull}, object 0 unchanged {_obj0Same}");
            if (_npcAfterFull != 0 || _playerAfterFull != 5) return Task.CompletedTask;

            _resultA = Give(c, talker, _found, 2);
            _playerAfterA = ConvHelp.PlayerCoins(); _npcAfterA = ConvHelp.NpcCoins(talker);
            _resultB = Give(c, talker, _found, 3);
            _playerAfterB = ConvHelp.PlayerCoins(); _npcAfterB = ConvHelp.NpcCoins(talker);
            c.Note($"find_inv gave index {_found}; give 2: returned {_resultA}, player {_playerAfterA}, NPC {_npcAfterA}; give 3: returned {_resultB}, player {_playerAfterB}, NPC {_npcAfterB}");
        }
        finally
        {
            ConversationVM.StackValues = savedValues; ConversationVM.stack = savedStack; ConversationVM.stackptr = savedPtr;
            player.next = playerNext;
            talker.link = 0;
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// give_ptr_npc(object, qty): at(at(sp - 1)) is qty, at(at(sp - 2)) the object. An exception
    /// from the call is not caught here: it fails the scenario with its own message.
    /// </summary>
    static int Give(ScenarioContext c, uwObject talker, int index, int qty)
    {
        ConversationVM.stackptr = 3;
        ConversationVM.Set(2, 12); ConversationVM.Set(12, qty);
        ConversationVM.Set(1, 13); ConversationVM.Set(13, index);
        ConversationVM.result_register = -1;
        ConversationVM.give_ptr_npc(talker);
        return ConversationVM.result_register;
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        if (_playerBefore != 5) return $"precondition: the player carries {_playerBefore} coins, not 5";
        string cite = c.Game == "uw1" ? "uw1 CONVERSE.C:710-715" : "uw2 CONVERSE.C:708-711";
        var k = new Checks();
        k.Equal("DoPickup result with no free object", 0, _pickup, "the port copies a picked-up object into a new level object");
        k.Equal("object in hand after a pickup with no free object", _handBefore, _handAfter, "nothing is picked up");
        k.Equal("give_ptr_npc(coins, 2) result with no free object", 0, _fullSplit, "uw2 CONVERSE.C:697-704");
        k.Equal("give_ptr_npc(coins, 5) result with no free object", 0, _fullAll, "uw2 CONVERSE.C:697-704");
        k.Equal("player's coins after the pickup and gives with no free object", 5, _playerAfterFull, cite);
        k.Equal("NPC's coins after the gives with no free object", 0, _npcAfterFull, cite);
        k.True(_obj0Same, "object 0 was written by a pickup or give with no free object");
        if (_playerAfterFull != 5 || _npcAfterFull != 0) return k.Result;
        k.Equal("give_ptr_npc(coins, 2) result", 1, _resultA, cite);
        k.Equal("player's coins after giving 2", 3, _playerAfterA, "invRemoveObject(obj, qty): " + cite);
        k.Equal("NPC's coins after giving 2", 2, _npcAfterA, "npc_inv_add: " + cite);
        k.Equal("give_ptr_npc(coins, 3) result", 1, _resultB, cite);
        k.Equal("player's coins after giving the other 3", 0, _playerAfterB, "invRemoveObject(obj, qty): " + cite);
        k.Equal("NPC's coins after giving the other 3", 5, _npcAfterB, "npc_inv_add: " + cite);
        return k.Result;
    }
}

/// <summary>
/// Items left in the player's trade area when a conversation ends are given back (UW1 and UW2).
/// The original's end_barter puts every item still in a trade slot back, selected or not: the
/// player's near the player, the NPC's near the NPC, with near_mob_put_at(..., 5, 0)
/// (uw1/src/conv/BARTER.C:212-225, uw2/src/conv/BARTER.C:172-190).
///
/// The port's ExitConversation drops only the player's selected slots at the player's feet
/// (GetPlayerTradeSlot(i, true), src/conversation/conversationvm.cs:640-646), so an item offered
/// and then withdrawn (deselected) is in no list afterwards.
///
/// The scenario starts a conversation with the first NPC on level 1 that has one
/// (ConversationVM.StartConversation, as talking to it does), puts one coin in the first trade slot
/// selected and one in the second deselected (SetPlayerTradeSlot, as dropping an item there and
/// clicking it does), and ends the conversation through the port's ExitConversation (private, by
/// reflection; the end of the script reaches it). Both coins must be on the map within 5 squares
/// of the player.
/// </summary>
public sealed class EndBarter : Scenario
{
    const ulong StartMs = 10000;

    public override string Name => "end-barter-returns-items";
    public override string Cites => "underworld-exhumed uw1/src/conv/BARTER.C:212-225; uw2/src/conv/BARTER.C:172-190";

    string _problem;
    int _selected, _deselected, _talkerWhoami;
    bool _selOnMap, _deselOnMap;
    int _sx, _sy, _dx, _dy;

    public override async Task Setup(ScenarioContext c)
    {
        var objs = UWTileMap.current_tilemap.LevelObjects;
        for (int i = 2; i < 256 && uimanager.CurrentGameMode != uimanager.GameModes.CONVERSATION; i++)
        {
            var o = objs[i];
            if (o == null || o.majorclass != 1 || o.npc_whoami == 0 || o.npc_whoami == 255 || o.npc_hp == 0) continue;
            if (!UWTileMap.ValidTile(o.tileX, o.tileY)) continue;
            ConversationVM.StartConversation(o);
            if (uimanager.CurrentGameMode == uimanager.GameModes.CONVERSATION) _talkerWhoami = o.npc_whoami;
        }
        if (uimanager.CurrentGameMode != uimanager.GameModes.CONVERSATION) { _problem = "no NPC on level 1 would talk"; return; }
        ulong deadline = Time.GetTicksMsec() + StartMs;
        int frames = 0;
        while (Time.GetTicksMsec() < deadline && frames < 60) { await c.Frames(1); frames++; }
        if (uimanager.CurrentGameMode != uimanager.GameModes.CONVERSATION) { _problem = "the conversation ended by itself"; return; }
        c.Note($"talking to whoami 0x{_talkerWhoami:X}");

        for (int s = 0; s < uimanager.NoOfTradeSlots; s++) uimanager.SetPlayerTradeSlot(s, -1);
        _selected = ObjectCreator.SpawnObjectInHand(ConvHelp.Coin);
        playerdat.ObjectInHand = -1;
        _deselected = ObjectCreator.SpawnObjectInHand(ConvHelp.Coin);
        playerdat.ObjectInHand = -1;
        uimanager.instance.mousecursor.SetCursorToCursor();
        if (_selected == 0 || _deselected == 0) { _problem = "no coins could be made"; return; }
        uimanager.SetPlayerTradeSlot(0, _selected, selected: true);
        uimanager.SetPlayerTradeSlot(1, _deselected, selected: false);
        if (uimanager.GetPlayerTradeSlot(0, false) != _selected || uimanager.GetPlayerTradeSlot(1, false) != _deselected)
            _problem = "the coins are not in the trade slots";
    }

    public override Task Act(ScenarioContext c)
    {
        if (_problem != null) return Task.CompletedTask;
        var exit = typeof(ConversationVM).GetMethod("ExitConversation", BindingFlags.NonPublic | BindingFlags.Static);
        if (exit == null) { _problem = "ConversationVM.ExitConversation not found"; return Task.CompletedTask; }
        exit.Invoke(null, new object[] { ConversationVM.currentTalker, ConversationVM.currentConversation });
        int px = playerdat.playerObject.tileX, py = playerdat.playerObject.tileY;
        _selOnMap = ConvHelp.OnMapNear(_selected, px, py, 5, out _sx, out _sy);
        _deselOnMap = ConvHelp.OnMapNear(_deselected, px, py, 5, out _dx, out _dy);
        c.Note($"player at ({px}, {py}); selected coin on the map {_selOnMap} at ({_sx}, {_sy}); deselected coin on the map {_deselOnMap} at ({_dx}, {_dy})");
        return Task.CompletedTask;
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        if (!_selOnMap) return "precondition: the selected coin was not put back either; the conversation end did not run its give-back";
        string cite = c.Game == "uw1" ? "uw1 BARTER.C:217-221" : "uw2 BARTER.C:178-182";
        var k = new Checks();
        k.True(_deselOnMap, $"deselected item left in the trade area expected put back near the player, in no square within 5 ({cite})");
        return k.Result;
    }
}
