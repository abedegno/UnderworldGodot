using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// Small things the player reads or hears.
///
/// Repair: do_repair adds 3 + skill / 5 quality on a plain success and says "fully repaired"
/// (3) only when quality plus that is above 63; at exactly 63 it sets 63 and returns 2, a partial
/// repair (uw1/src/event/WORLDEV.C:647-655, uw2/src/event/WORLDEV.C:1028-1036). For a few seeds whose
/// first draw makes skill_check(10, durability) give 1, the scenario repairs an item of quality 58
/// (58 + 5 = 63, expected 2) and one of quality 59 (64, expected 3) through repair.RepairObjectSkillCheck.
///
/// Equipment damage: DamageInventory prints "Your &lt;name&gt; were" when the name ends in s, else
/// "was", then " damaged." or " destroyed." (uw1/src/inv/INVDATA.C:446-460, uw2/src/inv/INVDATA.C:427-441).
/// The scenario puts an armour piece whose name ends in s, and one whose name does not, on the head
/// slot in turn and calls damage.DamageEquipment(slot, 4, 4, 2, 0), so the quality drops but the item
/// stays, and reads the message scroll.
///
/// A failed incantation: player_cast calls fail_spell(2), which plays effect 0x16 and prints "The
/// incantation failed." (uw1/src/combat/RUNES.C:221-226, 249-250; uw2/src/combat/RUNES.C:202-207, 238-239).
/// The scenario casts a circle 1 spell with Casting 0 from successive seeds until the scroll shows
/// "The incantation failed." and checks the effects asked for at the avatar during that cast
/// (UWsoundeffects.AvatarRequestObserver, a DEBUG seam).
///
/// A book used on the floor: the player's world use passes how 0 (uw1/src/game/INTERACT.C:552,
/// uw2/src/game/INTERACT.C:452) and UseBook returns at once (uw1/src/obj/USEITEMS.C:925-926,
/// uw2/src/obj/USEITEMS.C:460-461), so nothing is printed. The scenario places a book with text 1 by
/// the player, uses it through use.Use as a world object and reads the scroll.
///
/// UW2 only, Dispel Rune on an inventory object: obj_spells prints "That is not a rune." (0x12E) and
/// waives the cost for anything but a rune of flame or stasis (uw2/src/combat/SPELLS.C:819-822). The
/// scenario holds a cost of 3 and calls the class 7 handler for minor 8 on an inventory object.
///
/// The port said "fully repaired" at 63 (src/interaction/repair.cs), always "was" and no full stop
/// (src/interaction/damage.cs), played no sound on a failed incantation (src/magic/runicmagic.cs),
/// printed the book's description (src/objects/readable.cs) and was silent for Dispel Rune on an
/// inventory object (src/magic/spellcasting_class_7.cs).
/// </summary>
public sealed class MessagesAndSounds : Scenario
{
    const int Skill = 10;

    public override string Name => "messages-and-sounds";
    public override string Cites => "underworld-exhumed uw1/src/event/WORLDEV.C:623-656, uw1/src/inv/INVDATA.C:446-460, uw1/src/combat/RUNES.C:221-226, 249-250, uw1/src/obj/USEITEMS.C:925-926; uw2/src/event/WORLDEV.C:1004-1037, uw2/src/inv/INVDATA.C:427-441, uw2/src/combat/RUNES.C:202-207, 238-239, uw2/src/obj/USEITEMS.C:460-461, uw2/src/combat/SPELLS.C:819-822";

    string _problem;
    readonly List<(string what, int expected, int actual, string cite)> _numbers = new();
    readonly List<(string what, bool ok, string failure)> _facts = new();

    static string ScrollText()
    {
        var text = string.Join(" ", uimanager.instance.scroll.Lines.Select(l => l?.LineText ?? ""));
        text = Regex.Replace(text, @"\[/?color[^\]]*\]", "");
        return Regex.Replace(text, @"\s+", " ").Trim();
    }

    static string Plain(string s) => Regex.Replace(s ?? "", @"\s+", " ").Trim();

    static int Durability(int id) => id < 0x20 ? (sbyte)weaponObjectDat.durability(id) : (sbyte)armourObjectDat.durability(id);

    static bool Tough(int id) => commonObjDat.qualityclass(id) <= 1 && (commonObjDat.scaleresistances(id) & 4) == 0;

    uwObject PutInSlot(int item, int slot)
    {
        int s = ObjectCreator.SpawnObjectInHand(item);
        if (s == 0) return null;
        int inv = playerdat.AddObjectToPlayerInventory(s, false);
        playerdat.SetInventorySlotListHead(slot, inv);
        playerdat.ObjectInHand = -1;
        uimanager.instance.mousecursor.SetCursorToCursor();
        var o = playerdat.InventoryObjects[inv];
        if (o == null || playerdat.GetInventorySlotListHead(slot) != inv) return null;
        o.quality = 0x28;
        return o;
    }

    public override Task Setup(ScenarioContext c) => Task.CompletedTask;

    void Repair()
    {
        int item = -1, dur = 0;
        for (int id = 0; id < 0x40; id++)
        {
            if (id >= 0x10 && id < 0x20) continue;
            int d = Durability(id);
            if (d > 0 && d < 0x7F && (item < 0 || d < dur)) { item = id; dur = d; }
        }
        if (item < 0) { _problem = "no weapon or armour with a durability"; return; }
        var seeds = new List<uint>();
        for (uint s = 1; s < 20000 && seeds.Count < 3; s++)
            if (new BatchB.DosRand(s).SkillCheck(Skill, dur) == 1) seeds.Add(s);
        if (seeds.Count == 0) { _problem = $"no seed gives a plain success at durability {dur}"; return; }
        var (x, y) = BatchB.OpenSquareNearPlayer();
        foreach (uint seed in seeds)
            foreach (var (q, expected) in new[] { (58, 2), (59, 3) })
            {
                var obj = BatchB.SpawnStatic(item, x, y);
                if (obj == null) { _problem = "no object could be made"; return; }
                obj.quality = (short)q;
                BatchB.Seed(seed);
                int result = repair.RepairObjectSkillCheck(Skill, obj);
                string cite = "do_repair: quality + new_quality > 63 for 3, else 2 (uw1 WORLDEV.C:647-655, uw2 WORLDEV.C:1028-1036)";
                _numbers.Add(($"repair result from quality {q} + {3 + Skill / 5} (seed {seed})", expected, result, cite));
                _numbers.Add(($"quality after that repair (seed {seed})", 63, obj.quality, cite));
            }
    }

    void EquipmentDamage(ScenarioContext c)
    {
        int plural = -1, single = -1;
        for (int id = 0x20; id < 0x40; id++)
        {
            if (!Tough(id)) continue;
            string n = GameStrings.GetObjectNounUW(id);
            if (string.IsNullOrEmpty(n)) continue;
            if (n.EndsWith("s")) { if (plural < 0) plural = id; }
            else if (single < 0) single = id;
        }
        if (plural < 0 || single < 0) { _problem = $"no armour to use (plural {plural}, single {single})"; return; }
        string cite = "DamageInventory: were when the name ends in s, then ' damaged.' (uw1 INVDATA.C:446-460, uw2 INVDATA.C:427-441)";
        foreach (var (id, verb) in new[] { (plural, "were"), (single, "was") })
        {
            playerdat.SetInventorySlotListHead(0, 0);
            var o = PutInSlot(id, 0);
            if (o == null) { _problem = $"could not put 0x{id:X} on the head"; return; }
            uimanager.instance.scroll.Clear();
            int r = damage.DamageEquipment(0, 4, 4, 2, 0);
            string expected = $"Your {GameStrings.GetObjectNounUW(id)} {verb} damaged.";
            string seen = ScrollText();
            c.Note($"item 0x{id:X}: DamageEquipment {r}, scroll \"{seen}\"");
            _numbers.Add(($"DamageEquipment result for 0x{id:X} (damaged, not destroyed)", 0, r, cite));
            _facts.Add(($"0x{id:X}", seen == Plain(expected), $"equipment damage message: expected \"{expected}\", scroll \"{seen}\" ({cite})"));
            playerdat.SetInventorySlotListHead(0, 0);
        }
    }

    async Task FailedIncantation(ScenarioContext c)
    {
        var spell = RunicMagic.SpellList.Where(s => s != null && s.SpellMajorClass == 0).OrderBy(s => s.SpellLevel).FirstOrDefault();
        if (spell == null) { _problem = "no class 0 spell"; return; }
        string failed = Plain(GameStrings.GetString(1, GameStrings.str_the_incantation_failed_));
        var heard = new List<byte>();
        UWsoundeffects.AvatarRequestObserver = e => heard.Add(e);
        try
        {
            for (uint seed = 1; seed < 200; seed++)
            {
                BatchB.ReadyCaster(0);
                playerdat.play_hp = playerdat.max_hp;
                BatchB.SelectSpell(spell.SpellIndex);
                uimanager.instance.scroll.Clear();
                heard.Clear();
                BatchB.Seed(seed);
                BatchB.CastSelected();
                await c.Frames(2);
                SpellCasting.currentSpell = null;
                RunicMagic.PendingSpellCost = 0;
                if (ScrollText() == failed)
                {
                    c.Note($"seed {seed}: \"{ScrollText()}\", effects asked for: {string.Join(", ", heard.Select(e => $"0x{e:X}"))}");
                    _facts.Add(("failed incantation", heard.Contains(UWsoundeffects.SoundEffectSpellFailure),
                        $"failed incantation: no effect 0x16 (asked for: {string.Join(", ", heard.Select(e => $"0x{e:X}"))}) (fail_spell(2), {(c.Game == "uw1" ? "uw1 RUNES.C:221-226, 249-250" : "uw2 RUNES.C:202-207, 238-239")})"));
                    return;
                }
            }
            _problem = "no seed under 200 gave \"The incantation failed.\"";
        }
        finally
        {
            UWsoundeffects.AvatarRequestObserver = null;
        }
    }

    void FloorBook(ScenarioContext c)
    {
        var (x, y) = BatchB.OpenSquareNearPlayer();
        var book = BatchB.SpawnStatic(0x130, x, y);
        if (book == null) { _problem = "no book could be made"; return; }
        book.is_quant = 1;
        book.link = 1;
        book.enchantment = 0;
        book.flags1 = 0;
        uimanager.instance.scroll.Clear();
        use.Use(book, playerdat.playerObject, UWTileMap.current_tilemap.LevelObjects, true);
        string seen = ScrollText();
        c.Note($"book used on the floor: scroll \"{seen}\"");
        _facts.Add(("floor book", seen == "", $"a book used on the floor printed \"{seen}\"; UseBook returns at once for how 0 ({(c.Game == "uw1" ? "uw1 USEITEMS.C:925-926" : "uw2 USEITEMS.C:460-461")})"));
    }

    void DispelRuneInInventory(ScenarioContext c)
    {
        int s = ObjectCreator.SpawnObjectInHand(0x20);
        if (s == 0) { _problem = "no inventory object could be made"; return; }
        int inv = playerdat.AddObjectToPlayerInventory(s, false);
        playerdat.ObjectInHand = -1;
        uimanager.instance.mousecursor.SetCursorToCursor();
        playerdat.play_mana = 50;
        RunicMagic.PendingSpellCost = 3;
        uimanager.instance.scroll.Clear();
        SpellCasting.CastClass7_SpellsOnCallBack(8, inv, playerdat.InventoryObjects, playerdat.playerObject, WorldObject: false);
        string seen = ScrollText();
        string expected = Plain(GameStrings.GetString(1, 0x12E));
        c.Note($"Dispel Rune on inventory object {inv}: mana {playerdat.play_mana}, scroll \"{seen}\"");
        string cite = "obj_spells case 8 (uw2 SPELLS.C:819-822)";
        _numbers.Add(("mana after Dispel Rune on an inventory object", 50, playerdat.play_mana, cite));
        _numbers.Add(("cost still held after it", 0, RunicMagic.PendingSpellCost, cite));
        _facts.Add(("dispel rune", seen == expected, $"Dispel Rune on an inventory object: expected \"{expected}\", scroll \"{seen}\" ({cite})"));
    }

    public override async Task Act(ScenarioContext c)
    {
        Repair();
        if (_problem != null) return;
        EquipmentDamage(c);
        if (_problem != null) return;
        await FailedIncantation(c);
        if (_problem != null) return;
        FloorBook(c);
        if (_problem != null) return;
        if (c.Game == "uw2") DispelRuneInInventory(c);
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        var k = new Checks();
        foreach (var n in _numbers) k.Equal(n.what, n.expected, n.actual, n.cite);
        foreach (var f in _facts) k.True(f.ok, f.failure);
        return k.Result;
    }
}
