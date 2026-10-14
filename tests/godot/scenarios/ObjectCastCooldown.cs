using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// Casting from an object the player uses has its own wait, separate from the runic one
/// (issue #120). The original's checkSpell, for a use by the player (how nonzero), refuses
/// while player->game_clock &lt; nextSpellTime with sound 0x15 at pan 0x40 (UW2 also prints
/// string 0xB) and returns before the spell or its charge; otherwise it sets
/// nextSpellTime = game_clock + 0x2FD, casts and spends one charge
/// (uw1/src/obj/USEITEMS.C:1047-1074, uw2/src/obj/OBJUSE.C:346-377). UseWand calls it with the
/// use's how (uw1 USEITEMS.C:565-575, uw2 OBJUSE.C:229-238), and a use from the inventory
/// passes 1 (uw1/src/inv/BAGS.C:104, uw2/src/inv/BAGS.C:99).
///
/// The scenario puts a wand of lesser heal with five charges in the backpack and uses it from
/// the inventory four times without letting the clock run: at T (cast), again at T (refused),
/// at T + 0x2FC (refused) and at T + 0x2FD (cast, the comparison is unsigned and allows the
/// cast the moment the clock reaches the threshold). A cast is seen as a spent charge.
///
/// The player using an object in the world passes how 0 (player_3duse, uw1/src/ui/INTERACT.C:543-545,
/// uw2/src/ui/INTERACT.C:450-452), and UseBook returns at once when how is 0
/// (uw1 USEITEMS.C:925-926, uw2 USEITEMS.C:460-461), and UseObj does not go on to checkSpell after
/// a book (trap = 0: uw1 USEITEMS.C:137-138, uw2 OBJUSE.C:113-114). So an enchanted scroll on the
/// floor, read during the wait or after it, casts nothing, is not used up and leaves nextSpellTime
/// alone: a wand used from the inventory straight after a read at T + 2 * 0x2FD still casts.
/// </summary>
public sealed class ObjectCastCooldown : Scenario
{
    const int Wand = 0x98, Spell = 0x120;   // ITEM_WAND_98, ITEM_SPELL: uw2/src/include/items.h:292, 442
    const int Charges = 5;
    const int Interval = 0x2FD;             // uw1 USEITEMS.C:1063, uw2 OBJUSE.C:367
    const int Uw1Scroll = 0x138, Uw2Scroll = 0x134;   // ITEM_SCROLL_138 uw1/src/include/items.h:477, ITEM_SCROLL_134 uw2/src/include/items.h:464

    public override string Name => "object-cast-cooldown";
    public override string Cites => "underworld-exhumed uw1/src/obj/USEITEMS.C:1047-1074, uw2/src/obj/OBJUSE.C:346-377 (checkSpell)";

    string _problem;
    int _wandIndex, _spellIndex, _t;
    int _effect;
    readonly int[] _chargesAfter = new int[5];
    readonly int[] _clockAt = new int[5];
    readonly bool[] _scrollKept = new bool[2];
    readonly int[] _hpAfterRead = new int[2];
    const int LowHp = 1;

    public override Task Setup(ScenarioContext c)
    {
        // Lesser heal, a spell with nothing to aim, from each game's runic table
        // (src/magic/runicmagic.cs: UW2 index 11, UW1 index 8, both major 4 minor 2).
        int effect = c.Game == "uw2" ? 11 : 8;
        _effect = effect;
        int spellWorld = 0;
        var (index, slot) = Guards.PutInBackpack(Wand, wand =>
        {
            spellWorld = ObjectCreator.PrepareNewObject(Spell);
            if (spellWorld == 0) return;
            var spell = UWTileMap.current_tilemap.LevelObjects[spellWorld];
            spell.is_quant = 1;
            spell.flags_full = (short)(spell.flags_full | 4);   // a spell cast on use
            spell.link = (short)effect;
            spell.quality = Charges;
            spell.next = 0;
            wand.is_quant = 0;
            wand.link = (short)spellWorld;
        });
        if (slot == -1 || spellWorld == 0) { _problem = "could not put a wand with a spell in the backpack"; return Task.CompletedTask; }
        _wandIndex = index;
        var wandObj = playerdat.InventoryObjects[index];
        _spellIndex = wandObj.link;
        var e = MagicEnchantment.GetSpellEnchantment(wandObj, playerdat.InventoryObjects);
        if (e == null || e.LinkedSpellObject == null || e.LinkedSpellObject.quality != Charges)
        { _problem = "the wand's spell does not read back from the inventory"; return Task.CompletedTask; }

        // Start clear of any wait an earlier scenario in this process left behind.
        playerdat.ClockValue += Interval + 1;
        _t = playerdat.ClockValue;
        return Task.CompletedTask;
    }

    void UseWand(int step, int clock)
    {
        playerdat.ClockValue = clock;
        _clockAt[step] = clock;
        var wand = playerdat.InventoryObjects[_wandIndex];
        use.Use(ObjectUsed: wand, UsingObjectOrCharacter: playerdat.playerObject, objList: playerdat.InventoryObjects, WorldObject: false);
        var spell = playerdat.InventoryObjects[_spellIndex];
        _chargesAfter[step] = (wand.link == _spellIndex && spell != null) ? spell.quality : -1;
    }

    /// <summary>
    /// Puts an enchanted scroll of lesser heal (stored by index, as the wand's spell) on the
    /// floor of the player's square, with the player's hit points at LowHp, and uses it from the
    /// world at clock. Records whether the scroll is still there and the hit points after.
    /// </summary>
    void ReadWorldScroll(int read, int clock, ScenarioContext c)
    {
        playerdat.ClockValue = clock;
        int px = playerdat.playerObject.tileX, py = playerdat.playerObject.tileY;
        var scroll = ObjectCreator.spawnObjectInTile(c.Game == "uw2" ? Uw2Scroll : Uw1Scroll, px, py, 3, 3,
            (short)(UWTileMap.current_tilemap.Tiles[px, py].floorHeight << 3), RenderImmediately: false);
        if (scroll == null) { _problem = "no scroll could be put on the floor"; return; }
        scroll.is_quant = 1;
        scroll.enchantment = 1;
        scroll.flags_full = (short)(scroll.flags_full | 4);   // a spell cast on use
        scroll.link = (short)_effect;
        var objs = UWTileMap.current_tilemap.LevelObjects;
        var e = MagicEnchantment.GetSpellEnchantment(scroll, objs);
        if (e == null) { _problem = "the scroll's spell does not read back from the world"; return; }
        int index = scroll.index;
        playerdat.play_hp = LowHp;
        use.Use(ObjectUsed: scroll, UsingObjectOrCharacter: playerdat.playerObject, objList: objs, WorldObject: true);
        _hpAfterRead[read] = playerdat.play_hp;
        _scrollKept[read] = Guards.InTileChain(px, py, index) && objs[index].item_id == (c.Game == "uw2" ? Uw2Scroll : Uw1Scroll);
    }

    public override async Task Act(ScenarioContext c)
    {
        if (_problem != null) return;
        UseWand(0, _t);
        UseWand(1, _t);
        UseWand(2, _t + Interval - 1);
        UseWand(3, _t + Interval);
        // The wait set by use 3 runs to T + 2 * 0x2FD. A floor read during it, then one after it
        // followed at once by the wand.
        ReadWorldScroll(0, _t + Interval, c);
        if (_problem != null) return;
        ReadWorldScroll(1, _t + 2 * Interval, c);
        if (_problem != null) return;
        UseWand(4, _t + 2 * Interval);
        await c.Frames(2);
        c.Note($"T = {_t}: charges after each use {string.Join(", ", _chargesAfter)} (started with {Charges}); floor scroll kept {_scrollKept[0]}, {_scrollKept[1]}; hp after the reads {_hpAfterRead[0]}, {_hpAfterRead[1]} (set to {LowHp})");
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        var k = new Checks();
        k.Equal("charges after the first use at T", Charges - 1, _chargesAfter[0], "cast, nextSpellTime = T + 0x2FD: uw1 USEITEMS.C:1063, uw2 OBJUSE.C:367");
        if (_chargesAfter[0] != Charges - 1) return "precondition: the first use did not cast: " + k.Result;
        k.Equal("charges after a second use at T", Charges - 1, _chargesAfter[1], "refused while game_clock < nextSpellTime: uw1 USEITEMS.C:1059-1061, uw2 OBJUSE.C:362-365");
        k.Equal("charges after a use at T + 0x2FC", Charges - 1, _chargesAfter[2], "refused while game_clock < nextSpellTime: uw1 USEITEMS.C:1059-1061, uw2 OBJUSE.C:362-365");
        k.Equal("charges after a use at T + 0x2FD", Charges - 2, _chargesAfter[3], "allowed once game_clock reaches nextSpellTime: uw1 USEITEMS.C:1059, uw2 OBJUSE.C:362");
        const string book = "UseBook returns when how is 0, a use from the world: uw1 USEITEMS.C:925-926, uw2 USEITEMS.C:460-461";
        k.True(_scrollKept[0], "floor scroll used up by a read during the wait (" + book + ")");
        k.Equal("hit points after a floor read during the wait", LowHp, _hpAfterRead[0], book);
        k.True(_scrollKept[1], "floor scroll used up by a read once the wait is over (" + book + ")");
        k.Equal("hit points after a floor read once the wait is over", LowHp, _hpAfterRead[1], book);
        k.Equal("charges after a wand use straight after the floor read at T + 2 * 0x2FD", Charges - 3, _chargesAfter[4], "a floor read sets no nextSpellTime (" + book + "); allowed at nextSpellTime: uw1 USEITEMS.C:1059, uw2 OBJUSE.C:362");
        return k.Result;
    }
}
