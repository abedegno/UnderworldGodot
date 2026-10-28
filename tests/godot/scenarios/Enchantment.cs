using System.Collections.Generic;
using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// Enchantment on an enchanted weapon raises its tier only while the tier is within the caster's
/// power (UW2). The original's sp_enchant, for a weapon (MAJOR_HACK minor 0 or 1) whose passive
/// enchantment is major 0xC with effect below 8 and (effect &amp; ~4) &lt; 3, takes the tier as
/// effect &amp; ~4 and the skill as (level - 8) / 4 + Casting / 11; a tier above the skill destroys
/// the weapon (sp_enchant_destroy), otherwise the effect goes up by one
/// (uw2/src/combat/SPELLS2.C:345-367).
///
/// The port's EnchantObject takes the tier as effect &amp; 0xFFF8, which is 0 for every effect 0..7
/// (src/interaction/enchanting.cs:88), so no weapon tier ever blows up.
///
/// The scenario sets the player to level 8 with Casting 0 (skill 0) and casts the spell's effect,
/// enchanting.EnchantObject, on a weapon in the world at the player's square enchanted with
/// effects 0, 1, 2, 4 and 6 (tiers 0, 1, 2, 0, 2). Tiers 0 go up by one; tiers 1 and 2 destroy it.
/// </summary>
public sealed class EnchantWeaponTier : Scenario
{
    public override string Name => "enchant-weapon-tier";
    public override string[] Games => new[] { "uw2" };
    public override string Cites => "underworld-exhumed uw2/src/combat/SPELLS2.C:345-367, 291-300";

    readonly List<(int effect, bool raised, string state)> _results = new();
    string _problem;

    public override Task Setup(ScenarioContext c)
    {
        playerdat.play_level = 8;
        playerdat.Casting = 0;
        return Task.CompletedTask;
    }

    public override Task Act(ScenarioContext c)
    {
        var objs = UWTileMap.current_tilemap.LevelObjects;
        int px = playerdat.playerObject.tileX, py = playerdat.playerObject.tileY;
        foreach (int effect in new[] { 0, 1, 2, 4, 6 })
        {
            var w = ObjectCreator.spawnObjectInTile(0, px, py, 3, 3, (short)(UWTileMap.current_tilemap.Tiles[px, py].floorHeight << 3));
            if (w == null) { _problem = "no weapon could be made"; return Task.CompletedTask; }
            int id = w.item_id, index = w.index;
            w.is_quant = 1;
            w.enchantment = 1;
            w.flags_full = (short)(w.flags_full & ~4);   // ID_FLAG11 clear: a passive enchantment
            w.link = (short)(0x2C0 | effect);   // LINK_SPECIAL | major 0xC << 4 | effect
            var before = MagicEnchantment.GetSpellEnchantment(w, objs);
            if (before == null || before.SpellMajorClass != 0xC || before.SpellMinorClass != effect || before.IsFlagBit2Set)
            { _problem = $"the weapon does not read back as major 0xC effect {effect}"; return Task.CompletedTask; }
            enchanting.EnchantObject(index, objs, WorldObject: true);
            var after = objs[index];
            bool raised = after != null && after.item_id == id && after.is_quant == 1 && (after.link & 0x1FF) == (0xC0 | (effect + 1));
            string state = after == null ? "gone" : $"item 0x{after.item_id:X} link 0x{after.link:X} quality {after.quality}";
            _results.Add((effect, raised, state));
            c.Note($"effect {effect}: raised {raised}, now {state}");
        }
        return Task.CompletedTask;
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        var k = new Checks();
        foreach (var (effect, raised, state) in _results)
        {
            int tier = effect & ~4;
            int skill = (8 - 8) / 4 + 0 / 11;
            bool expectRaised = tier <= skill;
            if (expectRaised)
                k.True(raised, $"effect {effect} (tier {tier}) expected raised to {effect + 1}, got {state} (SET_ENCHANT(obj, effect + 1): uw2 SPELLS2.C:364)");
            else
                k.True(!raised, $"effect {effect} (tier {tier} above skill {skill}) expected destroyed, got raised to {effect + 1} (diff = effect & ~4, sp_enchant_destroy: uw2 SPELLS2.C:351-361)");
        }
        return k.Result;
    }
}

/// <summary>
/// Enchantment recharges a wand whose spell is stored by index (UW2): a guard. The audit said the
/// port converts such a spell to its class and effect and then never recharges it. In the
/// original, decode_obj_spell never returns major 0 for a spell with ID_FLAG11 (bits 6-8 zero
/// give -1, uw2/src/obj/OBJUSE.C:466-470), so sp_enchant's conversion (SPELLS2.C:329-332) never
/// runs and the wand goes to chargeable and charge_object (SPELLS2.C:333-334); the port reads the
/// same -1 (src/magic/MagicEnchantment.cs:212-216) and recharges it too.
///
/// charge_object: base = effect / 8 + 1 (4 from 0x40), diff = (level + 1) / 2 - base + 8,
/// q = quality / diff, chance = ((0x10 - diff + q) &lt;&lt; 10) / (q + 0x18); (rand() &amp; 0x3FF) &lt; chance
/// blows the wand up (its item becomes debris), otherwise useNSpellCharges(obj, -1 - Casting / 15)
/// adds the charges (SPELLS2.C:262-288, OBJUSE.C:492-512). The scenario replays the draw on a
/// copy of the port's BorlandRandSource and checks the outcome; a wand of 5 charges, effect 0x10,
/// at level 8 and Casting 0.
/// </summary>
public sealed class EnchantRechargeWand : Scenario
{
    const int Wand = 0x98, Spell = 0x120;   // ITEM_WAND_98, ITEM_SPELL: uw2/src/include/items.h:292, 442
    const int Effect = 0x10, Charges = 5;

    public override string Name => "enchant-recharge-wand";
    public override string[] Games => new[] { "uw2" };
    public override string Cites => "underworld-exhumed uw2/src/combat/SPELLS2.C:262-288, 323-341; uw2/src/obj/OBJUSE.C:448-475, 492-512";

    string _problem;
    bool _expectBlown, _blown;
    int _chargesAfter, _itemAfter, _draw;

    public override Task Setup(ScenarioContext c)
    {
        playerdat.play_level = 8;
        playerdat.Casting = 0;
        return Task.CompletedTask;
    }

    public override Task Act(ScenarioContext c)
    {
        var objs = UWTileMap.current_tilemap.LevelObjects;
        int px = playerdat.playerObject.tileX, py = playerdat.playerObject.tileY;
        var wand = ObjectCreator.spawnObjectInTile(Wand, px, py, 3, 3, (short)(UWTileMap.current_tilemap.Tiles[px, py].floorHeight << 3));
        int sslot = ObjectCreator.PrepareNewObject(Spell);
        if (wand == null || sslot == 0) { _problem = "no wand or spell could be made"; return Task.CompletedTask; }
        var spell = objs[sslot];
        spell.is_quant = 1;
        spell.flags_full = (short)(spell.flags_full | 4);   // ID_FLAG11 set: a spell cast on use
        spell.link = Effect;            // bits 6-8 zero: stored by index, major -1
        spell.quality = Charges;
        spell.next = 0;
        wand.is_quant = 0;
        wand.link = (short)spell.index;
        var e = MagicEnchantment.GetSpellEnchantment(wand, objs);
        if (e == null || !e.IsFlagBit2Set || e.SpellMajorClass != -1 || e.SpellMinorClass != Effect)
        { _problem = "the wand's spell does not read back as stored by index (major -1)"; return Task.CompletedTask; }
        if (Rng.Source is not BorlandRandSource src) { _problem = "Rng.Source is not a BorlandRandSource"; return Task.CompletedTask; }

        int level = playerdat.play_level;
        int b = Effect < 0x40 ? Effect / 8 + 1 : 4;
        int diff = (level + 1) / 2 - b + 8;
        int q = Charges / diff;
        int chance = ((0x10 - diff + q) << 10) / (q + 0x18);
        _draw = new BorlandRandSource(src.Seed).Rand();
        _expectBlown = (_draw & 0x3FF) < chance;

        enchanting.EnchantObject(wand.index, objs, WorldObject: true);
        _itemAfter = objs[wand.index]?.item_id ?? -1;
        _blown = _itemAfter != Wand;
        _chargesAfter = spell.quality;
        c.Note($"rand() {_draw}: & 0x3FF = {_draw & 0x3FF}, chance {chance}; wand now item 0x{_itemAfter:X}, spell charges {_chargesAfter}");
        return Task.CompletedTask;
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        var k = new Checks();
        k.True(_blown == _expectBlown, $"wand blown up expected {_expectBlown}, got {_blown} ((rand() & 0x3FF) < chance: uw2 SPELLS2.C:278-283)");
        if (!_expectBlown)
            k.Equal("wand charges after the recharge", Charges + 1 + playerdat.Casting / 15, _chargesAfter, "useNSpellCharges(obj, -1 - Casting / 15): uw2 SPELLS2.C:285, OBJUSE.C:503-505");
        return k.Result;
    }
}
