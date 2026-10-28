using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// Guard: eating an apple from the backpack feeds the player by the original's amount and uses
/// the apple up. The original's UseFood takes the nourishment of class 0x0B food from the Food
/// table, OBJECTS.DAT's food bytes (uw1/src/obj/USEITEMS.C:615-616; uw2/src/obj/USEITEMS.C:217-218;
/// the table is read by misc_init, uw1 obj/MISC.C:27), the apple (0xB3) having no case of its
/// own, and player_eat adds it to hunger unless that would pass 0xFF (uw1/src/game/SKILLS.C:640-651,
/// uw2 SKILLS.C:578-589). Then using_punt takes the eaten apple out of the inventory
/// (uw1 USEITEMS.C:716, uw2 USEITEMS.C:363). A use from the backpack has how 1
/// (uw1/src/inv/BAGS.C:104, uw2 BAGS.C:99), which UseFood needs (uw1 USEITEMS.C:608-614).
///
/// The nourishment is read from the game's own OBJECTS.DAT here, not from the port's loader.
/// The scenario sets hunger to 0x40 first so the apple always fits.
/// </summary>
public sealed class GuardEat : Scenario
{
    const int Apple = 0xB3;        // ITEM_APPLE, uw1/src/include/items.h:331, uw2 items.h:323
    const int HungerBefore = 0x40;

    public override string Name => "guard-eat";
    public override string Cites => "underworld-exhumed uw1/src/obj/USEITEMS.C:595-718 (UseFood), uw1/src/game/SKILLS.C:640-651 (player_eat); uw2/src/obj/USEITEMS.C:186-300, uw2/src/game/SKILLS.C:578-589";

    string _problem;
    int _index, _slot, _nutrition, _hungerAfter, _weightBefore, _weightAfter;
    bool _gone;

    public override Task Setup(ScenarioContext c)
    {
        _nutrition = Guards.FoodNutrition(Apple);
        if (_nutrition <= 0 || HungerBefore + _nutrition > 0xFF) { _problem = $"the apple's nourishment {_nutrition} does not fit the test"; return Task.CompletedTask; }
        (_index, _slot) = Guards.PutInBackpack(Apple);
        if (_slot == -1) { _problem = "could not put an apple in the backpack"; return Task.CompletedTask; }
        var apple = playerdat.InventoryObjects[_index];
        if (apple == null || apple.item_id != Apple || apple.ObjectQuantity != 1) { _problem = "the backpack slot does not hold one apple"; return Task.CompletedTask; }
        playerdat.play_hunger = HungerBefore;
        _weightBefore = playerdat.WeightCarried;
        return Task.CompletedTask;
    }

    public override async Task Act(ScenarioContext c)
    {
        if (_problem != null) return;
        uimanager.CurrentSlot = _slot;   // as the click on the slot sets it
        use.Use(playerdat.InventoryObjects[_index], playerdat.playerObject, playerdat.InventoryObjects, WorldObject: false);
        _hungerAfter = playerdat.play_hunger;
        _gone = playerdat.GetInventorySlotListHead(_slot) == 0;
        _weightAfter = playerdat.WeightCarried;
        await c.Frames(1);
        c.Note($"apple nourishment {_nutrition}: hunger 0x{HungerBefore:X} -> 0x{_hungerAfter:X}; slot {_slot} emptied {_gone}; weight {_weightBefore} -> {_weightAfter}");
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        var k = new Checks();
        k.Equal("hunger after eating an apple", HungerBefore + _nutrition, _hungerAfter, "player_eat(Food[3]): uw1 SKILLS.C:644-651, uw2 SKILLS.C:582-589");
        k.True(_gone, $"the apple expected used up, out of backpack slot {_slot}, still there (using_punt: uw1 USEITEMS.C:716, uw2 USEITEMS.C:363)");
        k.Equal("weight carried after eating", _weightBefore - Guards.Mass(Apple), _weightAfter, "the apple's mass leaves with it: InvRemoveOneObject, uw1/src/inv/INVDATA.C:251-287");
        return k.Result;
    }
}
