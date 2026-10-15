using System.Collections.Generic;
using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// A targeted spell that does not work costs no mana. When the player clicks a target, the
/// original's target_spells (class 7, minors 0..7, the critter spells) spends the cost held in
/// mspell_mused only if the spell's handler returns true (uw2/src/combat/SPELLS.C:772-773), and
/// obj_spells (minors 8 and up, the object spells) spends it at the end unless the spell found
/// nothing to work on: Dispel Rune on anything but a rune of flame or stasis, Mending on something
/// mendable() refuses, Gate Travel on anything but a moonstone each set mspell_mused to 0 first
/// (SPELLS.C:821-823, 840-842, 873-875, 914-916). Every other object spell is paid whatever it
/// finds.
///
/// For each spell the scenario does what the cast button does once the checks have passed (cost in
/// RunicMagic.PendingSpellCost, then SpellCasting.CastSpell; targeted-spell-mana covers the button
/// itself, whose circle limit in Britannia rules out most of these spells), then clicks a target
/// through SpellCasting.CastCurrentSpellOnRayCastTarget, as a click in the view does, and measures
/// the mana spent over both. The targets are a coin (item 0xA0, uw2/src/include/items.h: not a critter, rune
/// or moonstone, and mendable() returns 0 for misc minor 2, uw2/src/combat/SPELLS2.C:412-423) and
/// a critter that does not bleed. Waived: Bleeding and Study Monster on the coin (sp_bleed and
/// sp_study_monster return 0, SPELLS.C:479-480, SPELLS2.C:135-139), Smite Foe on the bloodless
/// critter (SPELLS.C:498-500), Dispel Rune, Mending and Gate Travel on the coin. Paid: Cause Fear on
/// the critter (sp_fear returns hit_critter_goal's 1, SPELLS.C:536-539, 367-373) and Name
/// Enchantment on the coin (no waiver, SPELLS.C:849-854).
///
/// Last, a cast on an anti-magic square: do_spell refuses it (uw2/src/combat/SPELLS.C:86-90) and
/// player_cast sets mspell_mused to 0 (uw2/src/combat/RUNES.C:309), so no cost is left held.
///
/// The port once charged every one of them, at the cast: its class 7 handlers returned nothing. A
/// refused cast left its cost in PendingSpellCost.
/// </summary>
public sealed class TargetedSpellWaived : Scenario
{
    const int Coin = 0xA0;   // uw2/src/include/items.h

    public override string Name => "targeted-spell-waived";
    public override string[] Games => new[] { "uw2" };
    public override string Cites => "underworld-exhumed uw2/src/combat/SPELLS.C:772-773 (target_spells), 821-823, 840-842, 873-875, 914-916 (obj_spells)";

    // minor, target ("coin" or "critter"), whether the original pays, citation
    static readonly (int minor, string target, bool paid, string cite)[] Cases =
    {
        (0, "coin", false, "SPELLS.C:479-480, 772"),
        (7, "coin", false, "SPELLS2.C:135-139, SPELLS.C:772"),
        (6, "critter", false, "SPELLS.C:498-500, 772"),
        (1, "critter", true, "SPELLS.C:536-539, 367-373, 772-773"),
        (8, "coin", false, "SPELLS.C:821-823"),
        (9, "coin", false, "SPELLS.C:840-842"),
        (15, "coin", false, "SPELLS.C:873-875"),
        (11, "coin", true, "SPELLS.C:849-854, 915"),
    };

    string _problem;
    uwObject _coin, _critter;
    readonly Dictionary<int, int> _cost = new(), _spent = new();
    int _refusedHeld;
    bool _refusedWaiting;

    public override Task Setup(ScenarioContext c)
    {
        foreach (var s in RunicMagic.SpellList)
            if (s != null && s.SpellMajorClass == 7) _cost[s.SpellMinorClass] = s.ManaCost;
        foreach (var t in Cases)
            if (!_cost.ContainsKey(t.minor)) _problem = $"no class 7 spell of minor {t.minor} in the spell list";
        int critterId = BatchB.FindCritter(id => critterObjectDat.bleed(id) == 0);
        if (critterId < 0) { _problem = "no critter type that does not bleed"; return Task.CompletedTask; }
        var (x, y) = BatchB.OpenSquareNearPlayer();
        _coin = BatchB.SpawnStatic(Coin, x, y);
        _critter = BatchB.SpawnCritter(critterId, x, y);
        if (_coin == null || _critter == null) _problem = "the coin or the critter could not be made";
        c.Note($"critter item 0x{critterId:X}");
        return Task.CompletedTask;
    }

    public override Task Act(ScenarioContext c)
    {
        if (_problem != null) return Task.CompletedTask;
        foreach (var t in Cases)
        {
            BatchB.ReadyCaster();
            int before = playerdat.play_mana;
            RunicMagic.PendingSpellCost = _cost[t.minor];
            var p = playerdat.playerObject;
            SpellCasting.CastSpell(7, t.minor, p, null, p.tileX, p.tileY, false);
            if (SpellCasting.currentSpell == null) { _problem = $"minor {t.minor} is not waiting for a target after the cast"; break; }
            var target = t.target == "coin" ? _coin : _critter;
            SpellCasting.CastCurrentSpellOnRayCastTarget(target.index, UWTileMap.current_tilemap.LevelObjects, true);
            _spent[t.minor] = before - playerdat.play_mana;
            if (SpellCasting.currentSpell != null) _problem = $"minor {t.minor} still waiting for a target after the click";
            SpellCasting.currentSpell = null;
            RunicMagic.PendingSpellCost = 0;
            c.Note($"class 7 minor {t.minor} on the {t.target}: cost {_cost[t.minor]}, mana spent {_spent[t.minor]}");
        }

        // Refused on an anti-magic square: do_spell returns 0 and player_cast drops the held cost.
        BatchB.ReadyCaster();
        var pl = playerdat.playerObject;
        var tile = UWTileMap.current_tilemap.Tiles[pl.tileX, pl.tileY];
        var noMagic = tile.noMagic;
        tile.noMagic = 1;
        RunicMagic.PendingSpellCost = _cost[1];
        SpellCasting.CastSpell(7, 1, pl, null, pl.tileX, pl.tileY, false);
        tile.noMagic = noMagic;
        _refusedHeld = RunicMagic.PendingSpellCost;
        _refusedWaiting = SpellCasting.currentSpell != null;
        SpellCasting.currentSpell = null;
        RunicMagic.PendingSpellCost = 0;
        c.Note($"cast on an anti-magic square: cost still held {_refusedHeld}, waiting for a target {_refusedWaiting}");
        return Task.CompletedTask;
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        var k = new Checks();
        foreach (var t in Cases)
            k.Equal($"mana spent on class 7 minor {t.minor} clicked on the {t.target}", t.paid ? _cost[t.minor] : 0, _spent[t.minor], "uw2 " + t.cite);
        if (_refusedWaiting) return "precondition: the cast on an anti-magic square was not refused";
        k.Equal("cost still held after a cast refused on an anti-magic square", 0, _refusedHeld, "uw2 SPELLS.C:89-90, RUNES.C:309");
        return k.Result;
    }
}
