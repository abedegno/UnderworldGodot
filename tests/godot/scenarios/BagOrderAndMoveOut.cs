using System.Collections.Generic;
using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// Where an object put into a bag goes in the bag's list, and what a drop on the open bag's own slot does (UW1 and UW2).
///
/// The original's PutObjectInBag joins the object with the first stack in the container it can join and otherwise
/// adds it at the end of the container's list with Obj_AddEnd (uw1/src/inv/BAGS.C:441-462; uw2/src/inv/BAGS.C:438-459;
/// Obj_AddEnd: uw1/src/obj/OBJECTS.C:274-283, uw2/src/obj/OBJECTS.C:266-275), so a bag shows its contents in the order
/// they were put in. It adds the object's weight to the player's whatever happens to it (uw1 BAGS.C:435-436, uw2 BAGS.C:432-433).
///
/// A drop on the open bag's own slot (slot 19) puts the object in the container the open bag sits in. ItemFitsSlot first
/// weighs it against that container (uw1/src/inv/INVPANEL.C:530-543, 598-622; uw2/src/inv/INVPANEL.C:516-529, 584-608).
/// When the open bag is in the backpack or on the paperdoll, the container is the player: there must be a free backpack
/// slot, or "There is no place to put that." (uw1 INVPANEL.C:536-541, uw2 INVPANEL.C:522-527), and the object joins the
/// first stack it can in the player's whole list, worn objects included, before it takes that slot (uw1 BAGS.C:408-418,
/// 441-462; uw2 BAGS.C:405-415, 438-459).
///
/// The scenario, in four parts: drops a candle on a sack in the right shoulder that holds a torch at 40, as a click on the
/// occupied slot does (uimanager.UseObjectsTogether): the sack holds the torch then the candle; then a torch at 35, which
/// joins the torch and adds a torch's weight to the player's. Opens a sack inside a
/// sack in the right shoulder and drops on the open bag's slot (uimanager.MoveObjectInHandOutOfOpenedContainer) a torch
/// at 35, which joins the torch at 40 in the outer sack (a stack of 2 at 37) and adds a torch's weight to the player's;
/// then a candle, which goes at the end of the outer sack; then an object heavier than the outer sack holds, which is
/// refused and stays in the hand. Opens a sack in the first backpack slot, with a torch at 40 in the right shoulder, and
/// drops a torch at 35 on the open bag's slot: it joins the shoulder's torch and the backpack gains nothing. Fills the
/// rest of the backpack and drops a key on the open bag's slot: it stays in the hand with "There is no place to put that."
///
/// Last, the player's weight. AddToOccupiedSlot adds the weight of a piece that joins a stack in a slot (uw1 INVPANEL.C:757-761,
/// uw2 INVPANEL.C:748-752). Taking an object out of the inventory takes off its ItemWeight, which counts its contents (uw1
/// INVDATA.C:469-483, 260, 382-383; uw2 INVDATA.C:450-464, 243, 363-364). Armageddon empties the inventory and sets the weight to
/// 0 (uw1 SPELLS.C:815-821). The scenario joins a torch at 35 onto one at 40 in the shoulder; removes a sack holding three
/// torches and a candle (playerdat.RemoveFromInventory) and picks another up from its slot; and clears an inventory with an
/// object in the last backpack slot (playerdat.ClearInventory).
///
/// The port put an object dropped on a bag at the head of its list (src/ui/uimanager_inventory.cs:666-669), dropped the
/// weight of an object that joined a stack in a bag (src/ui/uimanager_inventory.cs:766-790), and a drop on
/// the open bag's slot neither joined a stack nor weighed the object: it went at the head of the parent bag's list, or
/// into a free backpack slot, with no message when there was none (src/ui/uimanager_inventory.cs:888-921). A slot join added no
/// weight (src/ui/uimanager_inventory.cs:703-718); RemoveFromInventory took off only the object's own weight and left its contents
/// behind (src/player/playerdatinventory.cs:558-566); ClearInventory missed the last backpack slot, left every object in place and
/// left the weight (src/player/playerdatinventory.cs:874-897).
/// </summary>
public sealed class BagOrderAndMoveOut : Scenario
{
    const int Sack = 0x80, Torch = 0x91, Candle = 0x92, Key = 0x102;   // ITEM_SACK, ITEM_TORCH, ITEM_CANDLE, ITEM_KEY_102: uw1/src/include/items.h:273, 293-294, 417; uw2 items.h:266, 285-286, 408
    const int Shoulder = 5, Pack0 = 11, PackLast = 18;   // INV_SHOULDER, INV_PACK, INV_PACK_LAST: uw1/src/include/inv.h:91, 95-96

    public override string Name => "bag-order-and-move-out";
    public override string Cites => "underworld-exhumed uw1/src/inv/BAGS.C:394-472, INVPANEL.C:530-543; uw2/src/inv/BAGS.C:391-469, INVPANEL.C:516-529";

    string _problem;
    int _heavy = -1;
    string _dropOnBag, _parentAfterJoin, _parentAfterCandle, _parentAfterHeavy, _heavyText, _fullText, _shoulderAfter;
    bool _joinHandEmpty, _heavyHeld, _keyHeld, _packGained;
    int _weightGain, _torchMass, _bagJoinGain;
    string _bagJoin, _slotJoin;
    int _slotJoinGain, _fullBagMass, _removeDrop, _pickupDrop, _clearSlot18, _clearLeft, _clearWeight;
    bool _removeLeft;

    static int Count(uwObject o) => o.is_quant == 1 ? (o.link >= 0x200 ? -1 : o.link) : 1;

    static string Describe(uwObject o) => (o.majorclass == 2 && o.minorclass == 0) ? $"0x{o.item_id & 0x1FE:X}" : $"0x{o.item_id:X} x{Count(o)} q{o.quality}";

    static string Contents(int bag)
    {
        var parts = new List<string>();
        for (int i = playerdat.InventoryObjects[bag].link; i != 0; i = playerdat.InventoryObjects[i].next)
            parts.Add(Describe(playerdat.InventoryObjects[i]));
        return string.Join(", ", parts);
    }

    static string Scroll() => string.Join(" ", System.Linq.Enumerable.Select(uimanager.instance.scroll.Lines, l => l?.LineText ?? "")).Trim();

    /// <summary>Makes an object in the world and shapes it; a sack is made empty.</summary>
    int Make(int item, int qty = 1, int quality = 40, int owner = 0)
    {
        int world = ObjectCreator.SpawnObjectInHand(item, changeInteractionmode: false);
        if (world == 0) { _problem ??= $"no object 0x{item:X} could be made"; return 0; }
        var o = UWTileMap.current_tilemap.LevelObjects[world];
        o.quality = (short)quality;
        o.owner = (short)owner;
        if (item == Sack) { o.is_quant = 0; o.link = 0; }
        else { o.is_quant = 1; o.link = (short)qty; }
        return world;
    }

    /// <summary>Moves a world object into the inventory, unlinked; returns its inventory index.</summary>
    int Stow(int world)
    {
        int index = playerdat.AddObjectToPlayerInventory(world, false);
        playerdat.InventoryObjects[index].next = 0;
        playerdat.ObjectInHand = -1;
        return index;
    }

    int StowInSlot(int world, int slot)
    {
        int index = Stow(world);
        playerdat.SetInventorySlotListHead(slot, index);
        return index;
    }

    /// <summary>Gives a bag the inventory objects in order.</summary>
    static void Fill(int bag, params int[] contents)
    {
        playerdat.InventoryObjects[bag].link = (short)(contents.Length == 0 ? 0 : contents[0]);
        for (int i = 0; i < contents.Length; i++)
            playerdat.InventoryObjects[contents[i]].next = (short)(i + 1 < contents.Length ? contents[i + 1] : 0);
    }

    /// <summary>Puts a world object in the hand (setting the hand while it holds something drops that into the world).</summary>
    static void Hold(int world)
    {
        playerdat.ObjectInHand = -1;
        playerdat.ObjectInHand = world;
        uimanager.UpdateInventoryDisplay();
        uimanager.instance.scroll.Clear();
    }

    static void FreeHand()
    {
        if (playerdat.ObjectInHand > 0)
        {
            ObjectFreeLists.ReleaseFreeObject(UWTileMap.current_tilemap.LevelObjects[playerdat.ObjectInHand]);
            playerdat.ObjectInHand = -1;
            uimanager.instance.mousecursor.SetCursorToCursor();
        }
    }

    /// <summary>Closes any open bag and empties every slot, contents and all.</summary>
    static async Task Clear(ScenarioContext c, short weight)
    {
        FreeHand();
        for (int n = 0; n < 4 && uimanager.OpenedContainerIndex != -1; n++)
            container.Close(uimanager.OpenedContainerIndex, playerdat.InventoryObjects);
        uimanager.OpenedContainerIndex = -1;
        for (int s = 0; s <= PackLast; s++)
        {
            int head = playerdat.GetInventorySlotListHead(s);
            if (head == 0) continue;
            var stack = new Stack<int>();
            stack.Push(head);
            while (stack.Count > 0)
            {
                int i = stack.Pop();
                var o = playerdat.InventoryObjects[i];
                if (o == null) continue;
                if (o.is_quant == 0 && o.link != 0) stack.Push(o.link);
                if (i != head && o.next != 0) stack.Push(o.next);
                if (i != head) playerdat.InventoryObjects[i] = null;
            }
            uimanager.CurrentSlot = s;
            playerdat.RemoveFromInventory(head);
        }
        uimanager.CurrentSlot = -1;
        playerdat.WeightCarried = weight;
        playerdat.PlayerStatusUpdate();
        await c.Frames(1);
    }

    public override Task Setup(ScenarioContext c)
    {
        for (int s = 0; s <= PackLast; s++)
            if (playerdat.GetInventorySlotListHead(s) != 0) _problem = $"slot {s} is not empty at the start";
        if (playerdat.ObjectInHand > 0) _problem = "the player holds something at the start";
        int st = commonObjDat.stackable(Torch);
        if (st == 1 || st == 3) _problem = $"the torch has stack field {st} in this game's data";
        int cap = containerObjectDat.capacity(Sack);
        if (cap <= 0) _problem = $"the sack has capacity {cap}";
        for (int id = 0; id < 0x80 && _heavy < 0; id++)
            if (commonObjDat.mass(id) > cap) _heavy = id;
        if (_heavy < 0) _problem = $"no weapon or armour heavier than the sack's {cap}";
        _torchMass = commonObjDat.mass(Torch);
        return Task.CompletedTask;
    }

    public override async Task Act(ScenarioContext c)
    {
        if (_problem != null) return;
        short w0 = playerdat.WeightCarried;

        // 1. A candle dropped on a sack in the shoulder that holds a torch.
        {
            int sack = StowInSlot(Make(Sack), Shoulder);
            int torch = Stow(Make(Torch));
            Fill(sack, torch);
            int candle = Make(Candle);
            if (_problem != null) return;
            Hold(candle);
            uimanager.CurrentSlot = Shoulder;
            uimanager.UseObjectsTogether(candle, sack);
            await c.Frames(1);
            _dropOnBag = Contents(sack);
            short before = playerdat.WeightCarried;
            int t35 = Make(Torch, 1, 35);
            Hold(t35);
            uimanager.CurrentSlot = Shoulder;
            uimanager.UseObjectsTogether(t35, sack);
            await c.Frames(1);
            _bagJoinGain = playerdat.WeightCarried - before;
            _bagJoin = Contents(sack);
            await Clear(c, w0);
        }

        // 2. Drops on the slot of a sack open inside a sack in the shoulder.
        {
            int outer = StowInSlot(Make(Sack), Shoulder);
            int torch = Stow(Make(Torch, 1, 40));
            int inner = Stow(Make(Sack));
            Fill(outer, torch, inner);
            if (_problem != null) return;
            container.Use(playerdat.InventoryObjects[inner], false);
            await c.Frames(1);
            if (uimanager.OpenedContainerIndex != inner || uimanager.OpenedContainerParent != outer) { _problem = $"the inner sack did not open inside the outer one (open {uimanager.OpenedContainerIndex}, parent {uimanager.OpenedContainerParent})"; return; }

            short before = playerdat.WeightCarried;
            int t35 = Make(Torch, 1, 35);
            Hold(t35);
            uimanager.MoveObjectInHandOutOfOpenedContainer(t35);
            await c.Frames(1);
            _joinHandEmpty = playerdat.ObjectInHand == -1;
            _weightGain = playerdat.WeightCarried - before;
            _parentAfterJoin = Contents(outer);

            int candle = Make(Candle);
            Hold(candle);
            uimanager.MoveObjectInHandOutOfOpenedContainer(candle);
            await c.Frames(1);
            _parentAfterCandle = Contents(outer);

            int heavy = Make(_heavy);
            Hold(heavy);
            uimanager.MoveObjectInHandOutOfOpenedContainer(heavy);
            await c.Frames(1);
            _heavyHeld = playerdat.ObjectInHand == heavy;
            _heavyText = Scroll();
            _parentAfterHeavy = Contents(outer);
            await Clear(c, w0);
        }

        // 3. Drops on the slot of a sack open in the backpack.
        {
            int sack = StowInSlot(Make(Sack), Pack0);
            int torch = StowInSlot(Make(Torch, 1, 40), Shoulder);
            if (_problem != null) return;
            container.Use(playerdat.InventoryObjects[sack], false);
            await c.Frames(1);
            if (uimanager.OpenedContainerIndex != sack || uimanager.OpenedContainerParent != -1) { _problem = "the sack in the backpack did not open"; return; }

            int t35 = Make(Torch, 1, 35);
            Hold(t35);
            uimanager.MoveObjectInHandOutOfOpenedContainer(t35);
            await c.Frames(1);
            var sh = playerdat.InventoryObjects[playerdat.GetInventorySlotListHead(Shoulder)];
            _shoulderAfter = sh == null ? "nothing" : Describe(sh) + (playerdat.ObjectInHand == -1 ? "" : ", torch still in hand");
            _packGained = false;
            for (int s = Pack0 + 1; s <= PackLast; s++)
                if (playerdat.GetInventorySlotListHead(s) != 0) _packGained = true;

            for (int s = Pack0 + 1; s <= PackLast; s++)
            {
                if (playerdat.GetInventorySlotListHead(s) == 0) StowInSlot(Make(Candle), s);
            }
            if (_problem != null) return;
            int key = Make(Key, 1, 40, 7);
            Hold(key);
            uimanager.MoveObjectInHandOutOfOpenedContainer(key);
            await c.Frames(1);
            _keyHeld = playerdat.ObjectInHand == key;
            _fullText = Scroll();
            await Clear(c, w0);
        }

        // 4. The player's weight: a slot join, a full bag removed, a full bag picked up, and the inventory cleared.
        {
            StowInSlot(Make(Torch, 1, 40), Shoulder);
            int slotTorch = playerdat.GetInventorySlotListHead(Shoulder);
            short before = playerdat.WeightCarried;
            int t35 = Make(Torch, 1, 35);
            if (_problem != null) return;
            Hold(t35);
            uimanager.CurrentSlot = Shoulder;
            uimanager.UseObjectsTogether(t35, slotTorch);
            await c.Frames(1);
            _slotJoinGain = playerdat.WeightCarried - before;
            _slotJoin = Describe(playerdat.InventoryObjects[playerdat.GetInventorySlotListHead(Shoulder)]);
            await Clear(c, w0);

            int[] FullSack()
            {
                int sack = StowInSlot(Make(Sack), Shoulder);
                int torches = Stow(Make(Torch, 3, 40));
                int candle = Stow(Make(Candle));
                Fill(sack, torches, candle);
                return new[] { sack, torches, candle };
            }
            _fullBagMass = commonObjDat.mass(Sack) + 3 * commonObjDat.mass(Torch) + commonObjDat.mass(Candle);

            var parts = FullSack();
            if (_problem != null) return;
            playerdat.WeightCarried = (short)(w0 + _fullBagMass);
            before = playerdat.WeightCarried;
            uimanager.CurrentSlot = Shoulder;
            playerdat.RemoveFromInventory(parts[0]);
            await c.Frames(1);
            _removeDrop = before - playerdat.WeightCarried;
            _removeLeft = playerdat.InventoryObjects[parts[1]] != null || playerdat.InventoryObjects[parts[2]] != null;
            await Clear(c, w0);

            parts = FullSack();
            if (_problem != null) return;
            playerdat.WeightCarried = (short)(w0 + _fullBagMass);
            before = playerdat.WeightCarried;
            uimanager.CurrentSlot = Shoulder;
            uimanager.PickupObjectFromSlot(playerdat.InventoryObjects[parts[0]], forceFullQty: true);
            await c.Frames(1);
            _pickupDrop = before - playerdat.WeightCarried;
            await Clear(c, w0);

            StowInSlot(Make(Candle), PackLast);
            StowInSlot(Make(Sack), Shoulder);
            if (_problem != null) return;
            playerdat.WeightCarried = (short)(w0 + commonObjDat.mass(Candle) + commonObjDat.mass(Sack));
            playerdat.ClearInventory();
            await c.Frames(1);
            _clearSlot18 = playerdat.GetInventorySlotListHead(PackLast);
            _clearLeft = 0;
            foreach (var o in playerdat.InventoryObjects) if (o != null) _clearLeft++;
            _clearWeight = playerdat.WeightCarried;
            await Clear(c, w0);
        }

        c.Note($"slot join of a torch at 35 on one at 40: {_slotJoin}, weight +{_slotJoinGain}; full sack of mass {_fullBagMass} removed: weight -{_removeDrop}, contents left {_removeLeft}; "
            + $"picked up: weight -{_pickupDrop}; inventory cleared: slot 18 holds {_clearSlot18}, {_clearLeft} objects left, weight {_clearWeight}");
        c.Note($"candle dropped on a sack holding a torch: {_dropOnBag}; then a torch at 35: {_bagJoin} (weight +{_bagJoinGain}); open inner sack, torch at 35 on its slot: {_parentAfterJoin} "
            + $"(hand empty {_joinHandEmpty}, weight +{_weightGain}); then a candle: {_parentAfterCandle}; then 0x{_heavy:X} of mass {commonObjDat.mass(_heavy)}: "
            + $"\"{_heavyText}\", held {_heavyHeld}, outer {_parentAfterHeavy}; open sack in the backpack, torch at 35 on its slot: shoulder {_shoulderAfter}, "
            + $"backpack gained {_packGained}; full backpack, key on its slot: \"{_fullText}\", held {_keyHeld}");
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        bool uw1 = c.Game == "uw1";
        string addEnd = uw1 ? "Obj_AddEnd: uw1 BAGS.C:459-462" : "Obj_AddEnd: uw2 BAGS.C:456-459";
        string join = uw1 ? "uw1 BAGS.C:441-458" : "uw2 BAGS.C:438-455";
        var k = new Checks();
        k.True(_dropOnBag == "0x91 x1 q40, 0x92 x1 q40", $"a candle dropped on a sack holding a torch: expected \"0x91 x1 q40, 0x92 x1 q40\", got \"{_dropOnBag}\" ({addEnd})");
        k.True(_bagJoin == "0x91 x2 q37, 0x92 x1 q40", $"then a torch at 35: expected \"0x91 x2 q37, 0x92 x1 q40\", got \"{_bagJoin}\" ({join})");
        k.Equal("the player's weight after a torch dropped on the sack joined the stack in it", _torchMass, _bagJoinGain, uw1 ? "uw1 BAGS.C:435-436" : "uw2 BAGS.C:432-433");
        k.True(_joinHandEmpty && _parentAfterJoin == "0x91 x2 q37, 0x80", $"a torch at 35 dropped on the slot of a sack open in a sack holding a torch at 40: expected the outer sack \"0x91 x2 q37, 0x80\", got \"{_parentAfterJoin}\" ({join})");
        k.Equal("the player's weight after the torch joined the stack", _torchMass, _weightGain, uw1 ? "uw1 BAGS.C:435-436" : "uw2 BAGS.C:432-433");
        k.True(_parentAfterCandle == "0x91 x2 q37, 0x80, 0x92 x1 q40", $"then a candle: expected \"0x91 x2 q37, 0x80, 0x92 x1 q40\", got \"{_parentAfterCandle}\" ({addEnd})");
        k.True(_heavyHeld && _heavyText.Contains("is too full.") && _parentAfterHeavy == _parentAfterCandle,
            $"then an object heavier than the outer sack holds: expected refused with \"is too full.\", got \"{_heavyText}\", held {_heavyHeld}, outer \"{_parentAfterHeavy}\" ({(uw1 ? "ItemFitsSlot: uw1 INVPANEL.C:543, 598-622" : "ItemFitsSlot: uw2 INVPANEL.C:529, 584-608")})");
        k.True(_shoulderAfter == "0x91 x2 q37" && !_packGained, $"a torch at 35 dropped on the slot of a sack open in the backpack, a torch at 40 in the shoulder: expected the shoulder \"0x91 x2 q37\" and no new backpack object, got \"{_shoulderAfter}\", backpack gained {_packGained} (the player's whole list: {(uw1 ? "uw1 BAGS.C:408-418, 441-458" : "uw2 BAGS.C:405-415, 438-455")})");
        string noPlace = GameStrings.GetString(1, GameStrings.str_there_is_no_place_to_put_that_).Trim();
        k.True(_keyHeld && _fullText.Contains(noPlace), $"a key dropped on the open bag's slot with the backpack full: expected held with \"{noPlace}\", got \"{_fullText}\", held {_keyHeld} ({(uw1 ? "uw1 INVPANEL.C:536-541" : "uw2 INVPANEL.C:522-527")})");
        k.True(_slotJoin == "0x91 x2 q37", $"a torch at 35 dropped on one at 40 in the shoulder: expected \"0x91 x2 q37\", got \"{_slotJoin}\"");
        k.Equal("the player's weight after a torch joined the stack in a slot", _torchMass, _slotJoinGain, uw1 ? "AddToOccupiedSlot: uw1 INVPANEL.C:757-761" : "AddToOccupiedSlot: uw2 INVPANEL.C:748-752");
        string itemWeight = uw1 ? "ItemWeight, uw1 INVDATA.C:469-483; invRemoveObject, takeFromSlot: uw1 INVDATA.C:260, 287, 382-383" : "ItemWeight, uw2 INVDATA.C:450-464; invRemoveObject, takeFromSlot: uw2 INVDATA.C:243, 271, 363-364";
        k.Equal("the player's weight taken off when a full sack is removed from the inventory", _fullBagMass, _removeDrop, itemWeight);
        k.True(!_removeLeft, "the contents of a sack removed from the inventory are still inventory objects");
        k.Equal("the player's weight taken off when a full sack is picked up from a slot", _fullBagMass, _pickupDrop, itemWeight);
        string arma = "Armageddon: uw1 SPELLS.C:815-821";
        k.Equal("the last backpack slot after the inventory is cleared", 0, _clearSlot18, arma);
        k.Equal("inventory objects left after the inventory is cleared", 0, _clearLeft, arma);
        k.Equal("the player's weight after the inventory is cleared", 0, _clearWeight, arma);
        return k.Result;
    }
}
