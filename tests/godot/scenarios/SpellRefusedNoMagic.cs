using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// A spell the player casts where magic does not work. The original's player_cast takes the mana
/// before it calls do_spell for every class but the missiles in UW1 (uw1/src/combat/RUNES.C:259-263),
/// and for every class but the missiles and the targeted class 7 spells in UW2
/// (uw2/src/combat/RUNES.C:248-251). do_spell then refuses on an anti-magic square, and in UW1 also
/// on level 9, the Ethereal Void (uw1/src/combat/SPELLS.C:98-102, uw2/src/combat/SPELLS.C:86-90).
/// player_cast then drops only the cost it was still holding and calls fail_spell(3), which plays
/// effect 0x16 and prints "Casting was not successful.", string 0xD2 + 3 in UW1 and 0xE1 + 3 in UW2
/// (uw1 RUNES.C:221-226, 268-269; uw2 RUNES.C:202-207, 309-310). So the mana already paid stays
/// spent.
///
/// For each row the scenario selects the runes of the lowest circle spell of a class, marks the
/// player's square anti-magic (or, for the UW1 Void row, sets the dungeon level to 9) and casts
/// through RunicMagic.CastRunicSpell, the cast button, with Casting 30, which no circle 1 to 3
/// spell can fail under either game's skill check. It measures the mana spent and reads the message
/// scroll. A first cast with too little mana checks that the scroll can be read: it must show "You
/// do not have enough mana to cast the spell.".
///
/// The port once refused before any cost was paid and said nothing (src/magic/spellcasting.cs).
/// </summary>
public sealed class SpellRefusedNoMagic : Scenario
{
    public override string Name => "spell-refused-no-magic";
    public override string Cites => "underworld-exhumed uw1/src/combat/RUNES.C:221-226, 259-269; uw1/src/combat/SPELLS.C:98-102; uw2/src/combat/RUNES.C:202-207, 248-251, 309-310; uw2/src/combat/SPELLS.C:86-90";

    string _problem;
    bool _scrollReadable;
    readonly List<(string what, int cost, bool paid, int spent, bool said)> _rows = new();

    public override Task Setup(ScenarioContext c) => Task.CompletedTask;

    /// <summary>The lowest circle spell of the class, or null.</summary>
    static RunicMagic Lowest(int major) => RunicMagic.SpellList.Where(s => s != null && s.SpellMajorClass == major)
        .OrderBy(s => s.SpellLevel).FirstOrDefault();

    static string ScrollText()
    {
        var text = string.Join(" ", uimanager.instance.scroll.Lines.Select(l => l?.LineText ?? ""));
        text = Regex.Replace(text, @"\[/?color[^\]]*\]", "");
        return Regex.Replace(text, @"\s+", " ").Trim();
    }

    static string Plain(string s) => Regex.Replace(s ?? "", @"\s+", " ").Trim();

    async Task<bool> CastAndRead(ScenarioContext c, RunicMagic spell, string expected)
    {
        uimanager.instance.scroll.Clear();
        BatchB.SelectSpell(spell.SpellIndex);
        BatchB.CastSelected();
        await c.Frames(3);
        return ScrollText().Contains(Plain(expected));
    }

    public override async Task Act(ScenarioContext c)
    {
        bool uw1 = c.Game == "uw1";
        string notSuccessful = GameStrings.GetString(1, uw1 ? 0xD2 + 3 : 0xE1 + 3);
        var rows = new List<(string what, int major, bool paid, bool voidLevel)>
        {
            ("light (class 0) on an anti-magic square", 0, true, false),
            ("missile (class 5) on an anti-magic square", 5, false, false),
            ("targeted (class 7) on an anti-magic square", 7, uw1, false),
        };
        if (uw1) rows.Add(("light (class 0) in the Void", 0, true, true));

        // the scroll can be read: a cast with too little mana prints its refusal
        var light = Lowest(0);
        if (light == null) { _problem = "no class 0 spell"; return; }
        BatchB.ReadyCaster();
        playerdat.play_mana = 0;
        _scrollReadable = await CastAndRead(c, light, GameStrings.GetString(1, GameStrings.str_you_do_not_have_enough_mana_to_cast_the_spell_));
        c.Note($"cast with no mana: scroll \"{ScrollText()}\"");

        var p = playerdat.playerObject;
        var tile = UWTileMap.current_tilemap.Tiles[p.tileX, p.tileY];
        foreach (var r in rows)
        {
            var spell = Lowest(r.major);
            if (spell == null || spell.SpellLevel > 3) { _problem = $"no circle 1 to 3 spell of class {r.major}"; return; }
            BatchB.ReadyCaster();
            int before = playerdat.play_mana;
            var noMagic = tile.noMagic;
            int level = playerdat.dungeon_level;
            if (r.voidLevel) playerdat.dungeon_level = 9;
            else tile.noMagic = 1;
            uimanager.instance.scroll.Clear();
            BatchB.SelectSpell(spell.SpellIndex);
            BatchB.CastSelected();
            tile.noMagic = noMagic;
            playerdat.dungeon_level = level;
            bool waiting = SpellCasting.currentSpell != null;
            SpellCasting.currentSpell = null;
            RunicMagic.PendingSpellCost = 0;
            uimanager.instance.mousecursor.SetCursorToCursor();
            await c.Frames(3);
            int spent = before - playerdat.play_mana;
            bool said = ScrollText().Contains(Plain(notSuccessful));
            if (waiting) { _problem = $"{r.what}: the spell was not refused, it is waiting for a target"; return; }
            _rows.Add((r.what, spell.ManaCost, r.paid, spent, said));
            c.Note($"{r.what}: spell {spell.SpellIndex} circle {spell.SpellLevel}, cost {spell.ManaCost}, mana spent {spent}, scroll \"{ScrollText()}\"");
        }
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        if (!_scrollReadable) return "precondition: the scroll did not show the not-enough-mana message, so it cannot be read";
        string cite = c.Game == "uw1" ? "uw1 RUNES.C:259-268, SPELLS.C:98-102" : "uw2 RUNES.C:248-251, 309-310, SPELLS.C:86-90";
        string say = c.Game == "uw1" ? "uw1 RUNES.C:221-226, 269 fail_spell(3)" : "uw2 RUNES.C:202-207, 310 fail_spell(3)";
        var k = new Checks();
        foreach (var r in _rows)
        {
            k.Equal($"mana spent on a refused {r.what}", r.paid ? r.cost : 0, r.spent, cite);
            k.True(r.said, $"refused {r.what}: no \"Casting was not successful.\" on the scroll ({say})");
        }
        return k.Result;
    }
}
