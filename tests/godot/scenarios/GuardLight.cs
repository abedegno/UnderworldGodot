using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// Guard: lighting a torch from the backpack lights the player's way, and the torch burns down.
///
/// Lighting: the original's UseLight moves a light that is not already in a light slot to the
/// first free one of the inventory slots 5 to 8 (the shoulders and hands) and toggles it lit, its
/// item + 4 (uw1/src/obj/USEITEMS.C:504-560; uw2 USEITEMS.C:601-664). FixPlayerEquips then takes
/// the brightest lit light in those slots, the second byte of its OBJECTS.DAT light entry, as the
/// player's light (uw1/src/game/PLAYDATA.C:427-441, 457); UW2 shows the larger of that and the
/// level's own light (uw2/src/game/PLAYDATA.C:387-416, uw2/src/motion/PHYSICS.C:807-808), so in
/// UW2 the expected level is the larger of the torch's brightness and the level the player had
/// with no light.
///
/// Burning: each tick of the player's slow clock, duration_check counts plyregen[1] up and calls
/// DegradeLights(1, count) (uw1/src/game/PLAYTIME.C:96-97, uw2 PLAYTIME.C:88), which takes a point
/// of quality from a lit light when the count is a multiple of the light's burn rate, the first
/// byte of its OBJECTS.DAT entry (uw1 PLAYTIME.C:150-185, uw2 PLAYTIME.C:174-213). The count
/// restarts at 0 every 24th call in UW1 (PLAYTIME.C:128, 141) and every 60th in UW2 (PLAYTIME.C:151,
/// 165), as the port's does (src/player/playerdatloop.cs:250). Both are multiples of the torch's
/// rate in both games' data, so the scenario counts the multiples among the port's own counts. The port runs the tick every
/// 20 seconds of play (src/player/playerdatloop.cs:97); to keep the run short the scenario moves
/// its private second counter (secondcounter) on to 19 before each tick, so a tick takes one
/// second of frames, and lets the game's own loop do the tick.
///
/// The burn rate and brightness are read from the game's own OBJECTS.DAT, not the port's loader.
/// </summary>
public sealed class GuardLight : Scenario
{
    const int Torch = 0x91, LitTorch = 0x95;   // ITEM_TORCH, ITEM_LIT_TORCH: uw1/src/include/items.h:293, 297; uw2 items.h:285, 289
    const int Ticks = 15;
    const int Quality = 40;

    public override string Name => "guard-light";
    public override string Cites => "underworld-exhumed uw1/src/obj/USEITEMS.C:504-560 (UseLight), uw1/src/game/PLAYDATA.C:427-457 (FixPlayerEquips), uw1/src/game/PLAYTIME.C:96-97, 150-185 (DegradeLights); uw2 USEITEMS.C:601-664, PLAYDATA.C:387-416, PLAYTIME.C:88, 174-213";

    string _problem;
    int _index, _slot, _brightness, _rate, _lightBefore, _lightAfter, _litSlot = -1, _litItem, _qualityLit, _qualityAfter, _ticksSeen, _expectedBurn, _itemAfter;

    public override Task Setup(ScenarioContext c)
    {
        _brightness = Guards.LightBrightness(LitTorch);
        _rate = Guards.LightBurnRate(LitTorch);
        if (_rate == 0) { _problem = "the lit torch has burn rate 0 in OBJECTS.DAT"; return Task.CompletedTask; }
        for (int s = 5; s <= 8; s++)
            if (playerdat.GetInventorySlotListHead(s) != 0) { _problem = $"light slot {s} is not empty at the start"; return Task.CompletedTask; }
        (_index, _slot) = Guards.PutInBackpack(Torch, o => o.quality = Quality);
        if (_slot == -1) { _problem = "could not put a torch in the backpack"; return Task.CompletedTask; }
        playerdat.PlayerStatusUpdate();
        _lightBefore = playerdat.lightlevel;
        return Task.CompletedTask;
    }

    public override async Task Act(ScenarioContext c)
    {
        if (_problem != null) return;
        uimanager.CurrentSlot = _slot;   // as the click on the slot sets it
        use.Use(playerdat.InventoryObjects[_index], playerdat.playerObject, playerdat.InventoryObjects, WorldObject: false);
        for (int s = 5; s <= 8; s++)
        {
            var o = playerdat.GetInventorySlotObject(s);
            if (o != null) { _litSlot = s; _litItem = o.item_id; _qualityLit = o.quality; break; }
        }
        _lightAfter = playerdat.lightlevel;
        if (_litSlot == -1) return;

        for (int t = 0; t < Ticks; t++)
        {
            int before = Guards.Get<int>(typeof(playerdat), "playerUpdateCounter");
            Guards.Set(typeof(playerdat), "secondcounter", 19);
            for (int f = 0; f < 120 && Guards.Get<int>(typeof(playerdat), "playerUpdateCounter") == before && Guards.Get<int>(typeof(playerdat), "secondcounter") != 0; f++)
                await c.Frames(1);
            if (Guards.Get<int>(typeof(playerdat), "secondcounter") != 0) break;   // no tick happened
            _ticksSeen++;
            if ((before + 1) % _rate == 0) _expectedBurn++;
        }
        var lit = playerdat.GetInventorySlotObject(_litSlot);
        _qualityAfter = lit?.quality ?? -1;
        _itemAfter = lit?.item_id ?? -1;
        c.Note($"torch lit in slot {_litSlot} as 0x{_litItem:X}, light {_lightBefore} -> {_lightAfter} (brightness {_brightness}); over {_ticksSeen} ticks at burn rate {_rate}, quality {_qualityLit} -> {_qualityAfter}, item now 0x{_itemAfter:X}");
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        bool uw1 = c.Game == "uw1";
        var k = new Checks();
        k.Equal("slot the torch moved to", 5, _litSlot, "the first free of slots 5..8: uw1 USEITEMS.C:535-543, uw2 USEITEMS.C:637-647");
        if (_litSlot == -1) return k.Result;
        k.Equal("the torch's item after use", LitTorch, _litItem, "inclass + 4: uw1 USEITEMS.C:555-558, uw2 USEITEMS.C:659-664");
        int expectedLight = uw1 ? _brightness : System.Math.Max(_brightness, _lightBefore);
        k.Equal("light level with the lit torch", expectedLight, _lightAfter,
            uw1 ? "Lights[5] brightness: uw1 PLAYDATA.C:427-441, 457" : "max(torch brightness, the level's light): uw2 PLAYDATA.C:387-416");
        if (uw1) k.Equal("light level before the torch", 0, _lightBefore, "no light carried: uw1 PLAYDATA.C:428, 441");
        if (_ticksSeen != Ticks) return $"precondition: only {_ticksSeen} of {Ticks} clock ticks happened; " + (k.Result ?? "");
        k.Equal($"torch quality after {Ticks} ticks", _qualityLit - _expectedBurn, _qualityAfter,
            $"a point each count that is a multiple of the burn rate {_rate}: uw1 PLAYTIME.C:166-176, uw2 PLAYTIME.C:192-202");
        return k.Result;
    }
}
