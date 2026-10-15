using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// Where destroyed equipment's debris lands, a badly failed repair, and what the player hears and reads when a cast is refused or backfires.
///
/// Debris: DamageInventory, when a destroyed item leaves debris, makes the junk and puts it with near_mob_put_at(ThePlayer, junk, 6, 0)
/// (uw1/src/inv/INVDATA.C:440-441, uw2/src/inv/INVDATA.C:421-422), at a random spot within 6 eighths of the player's position
/// (drop_around_place, uw1/src/motion/COLLIDE.C:672-706, uw2/src/motion/COLLIDE.C:658-692). On level, open ground (TradeGround), the scenario
/// destroys a quality 1 melee weapon in paper doll slot 0 (how 2, so any slot will do) with damage.DamageEquipment(0, 64, 4, 2, 1) on a few seeds and finds the new
/// static object around the player. Each must be within 6 eighths of the player, and they must not all be at one spot.
///
/// Repair: do_repair's critical failure destroys the item, returning -2 and leaving its quality alone, when (rand() &amp; 0x3F) is
/// above quality + skill, and otherwise takes 4 + (rand() &amp; 7), 4..11, off the quality (uw1/src/event/WORLDEV.C:642-645,
/// uw2/src/event/WORLDEV.C:1023-1026). With quality 20 and skill 0 the scenario picks seeds whose skill_check gives -1, some that
/// destroy and some that damage, one of which takes off 11, and compares repair.RepairObjectSkillCheck's result and quality with the
/// original's over the same draws.
///
/// Refusals: player_cast refuses through fail_spell, which plays effect 0x16 at the avatar (play_effect_here) and prints string
/// why (uw1/src/combat/RUNES.C:221-226, 245-248; uw2/src/combat/RUNES.C:202-207, 232-237). In Britannia UW2 refuses a spell above
/// circle 3 with fail_spell(3), "Casting was not successful." (uw2 RUNES.C:232-233). The scenario casts a circle 4 spell on level 1
/// (UW2), a circle 2 spell at experience level 1, and a circle 1 spell with no mana, and checks the effects asked for at the avatar
/// (UWsoundeffects.AvatarRequestObserver, a DEBUG seam) and the Britannia message.
///
/// Backfire: when skill_check gives -1 the spell becomes a curse, and when do_spell takes it player_cast plays the class's sound
/// (UW1 effect 0x10, uw1 RUNES.C:264-265; UW2 by class and the spell's own minor, 0x10 unless that is 0xC or 9, uw2 RUNES.C:253-305).
/// The casting roll is skill_check(Casting + 5, circle * 2) in UW1 (uw1 RUNES.C:249) and skill_check(Casting, circle * 3) in UW2
/// (uw2 RUNES.C:238), and skill_check gives -1 only for a score, value - target + rand() % 31, of 2 or less (uw1/src/game/SKILLCHK.C:48-60,
/// uw2/src/game/SKILLCHK.C:39). With Casting 0 a circle 1 spell can therefore backfire in UW2 but never in UW1, whose lowest score is 3; UW1 needs
/// circle 2. The scenario takes, for each game, the lowest circle of a class 0..3 spell that can backfire at Casting 0 and the first seed on which
/// the original's skill_check gives -1, casts it once, and requires "The spell backfires." alone on the scroll (UW1's backfire prints nothing more,
/// uw1/src/combat/SPELLS.C:229-246) and the class's sound.
///
/// The port turned a destroyed inventory item into debris in the pack and said "damaged", because DamageObject treated it as a map
/// object where the original's remove_object returns 1 at once for x &lt; 0 (uw1/src/combat/DAMAGE.C:98-99, uw2/src/combat/DAMAGE.C:89-90).
/// Its unreachable branch put the debris on the player's square at (3, 3) (src/interaction/damage.cs). It also destroyed on the reverse roll and set quality 0
/// when it did, took 4..10 (src/interaction/repair.cs), played the refusal sound outside PlaySoundEffectAtAvatar and printed "The
/// incantation failed." in Britannia, and played nothing after a backfire (src/magic/runicmagic.cs).
/// </summary>
public sealed class RepairDebrisAndCastMessages : Scenario
{
    const int Range = 6, Quality = 20, Skill = 0;

    public override string Name => "repair-debris-and-cast-messages";
    public override string Cites => "underworld-exhumed uw1/src/inv/INVDATA.C:440-441, uw1/src/event/WORLDEV.C:623-656, uw1/src/combat/RUNES.C:221-269; uw2/src/inv/INVDATA.C:421-422, uw2/src/event/WORLDEV.C:1004-1037, uw2/src/combat/RUNES.C:202-310";

    string _problem;
    readonly List<(string what, int expected, int actual, string cite)> _numbers = new();
    readonly List<(bool ok, string failure)> _facts = new();

    static string ScrollText()
    {
        var text = string.Join(" ", uimanager.instance.scroll.Lines.Select(l => l?.LineText ?? ""));
        text = Regex.Replace(text, @"\[/?color[^\]]*\]", "");
        return Regex.Replace(text, @"\s+", " ").Trim();
    }

    static string Plain(string s) => Regex.Replace(s ?? "", @"\s+", " ").Trim();

    static string Effects(List<byte> heard) => heard.Count == 0 ? "none" : string.Join(", ", heard.Select(e => $"0x{e:X}"));

    static bool Tough(int id) => commonObjDat.qualityclass(id) <= 1 && (commonObjDat.scaleresistances(id) & 4) == 0;

    static int Durability(int id) => id < 0x20 ? (sbyte)weaponObjectDat.durability(id) : (sbyte)armourObjectDat.durability(id);

    public override Task Setup(ScenarioContext c) => Task.CompletedTask;

    static HashSet<int> ObjectsAround(int x, int y)
    {
        var map = UWTileMap.current_tilemap;
        var found = new HashSet<int>();
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (!UWTileMap.ValidTile(x + dx, y + dy)) continue;
                int i = map.Tiles[x + dx, y + dy].indexObjectList;
                for (int guard = 0; i != 0 && guard < 1024; guard++)
                {
                    found.Add(i);
                    i = map.LevelObjects[i].next;
                }
            }
        return found;
    }

    async Task Debris(ScenarioContext c)
    {
        int weapon = -1;
        for (int id = 0; id < 0x10 && weapon < 0; id++)
            if (Tough(id)) weapon = id;
        if (weapon < 0) { _problem = "no melee weapon to destroy"; return; }
        var (gx, gy, _, _) = TradeGround.FindPair();
        if (gx < 0) { _problem = "no level, open ground on this level"; return; }
        if (!await TradeGround.MovePlayer(c, gx, gy)) { _problem = $"the player could not be moved to ({gx}, {gy})"; return; }

        string cite = c.Game == "uw1" ? "near_mob_put_at(ThePlayer, junk, 6, 0), uw1 INVDATA.C:441" : "near_mob_put_at(ThePlayer, junk, 6, 0), uw2 INVDATA.C:422";
        var spots = new HashSet<(int, int)>();
        var map = UWTileMap.current_tilemap;
        for (uint seed = 1; seed <= 4; seed++)
        {
            playerdat.SetInventorySlotListHead(0, 0);
            if (BatchB.Equip(weapon, 0, 1) == null) { _problem = $"could not put 0x{weapon:X} in slot 0"; return; }
            var p = playerdat.playerObject;
            int px = (p.npc_xhome << 3) + p.xpos, py = (p.npc_yhome << 3) + p.ypos;
            var before = ObjectsAround(p.tileX, p.tileY);
            BatchB.Seed(seed);
            int r = damage.DamageEquipment(0, 64, 4, 2, 1);
            var added = ObjectsAround(p.tileX, p.tileY).Except(before).Where(i => map.LevelObjects[i].IsStatic).ToList();
            c.Note($"seed {seed}: DamageEquipment {r}, player at ({px}, {py}) in eighths, new static objects {string.Join(", ", added.Select(i => $"{i} (0x{map.LevelObjects[i].item_id:X}) at ({(map.LevelObjects[i].tileX << 3) + map.LevelObjects[i].xpos}, {(map.LevelObjects[i].tileY << 3) + map.LevelObjects[i].ypos})"))}");
            if (r != 1) { _problem = $"seed {seed}: DamageEquipment gave {r}, not destroyed"; return; }
            if (added.Count != 1) { _facts.Add((false, $"seed {seed}: {added.Count} new static objects around the player, expected the one piece of debris ({cite})")); continue; }
            var junk = map.LevelObjects[added[0]];
            int jx = (junk.tileX << 3) + junk.xpos, jy = (junk.tileY << 3) + junk.ypos;
            spots.Add((jx, jy));
            _facts.Add((System.Math.Abs(jx - px) <= Range && System.Math.Abs(jy - py) <= Range,
                $"seed {seed}: debris at ({jx}, {jy}), more than {Range} eighths from the player at ({px}, {py}) ({cite})"));
            ObjectRemover_OLD.DeleteObjectFromTile_DEPRECIATED(junk.tileX, junk.tileY, junk.index, true);
        }
        playerdat.SetInventorySlotListHead(0, 0);
        _facts.Add((spots.Count > 1, $"debris from 4 seeds all at one spot {string.Join(" ", spots)}, not at random within {Range} eighths ({cite}, drop_around_place)"));
    }

    void Repair(ScenarioContext c)
    {
        int item = -1, dur = 0;
        for (int id = 0; id < 0x40; id++)
        {
            if (id >= 0x10 && id < 0x20) continue;
            int d = Durability(id);
            if (d > 0 && d < 0x7F && (item < 0 || d > dur)) { item = id; dur = d; }
        }
        if (item < 0) { _problem = "no weapon or armour with a durability"; return; }

        // The original's do_repair over the same draws, for a critical failure.
        (int result, int quality, bool destroyRoll, int off) Dos(uint seed)
        {
            var d = new BatchB.DosRand(seed);
            d.SkillCheck(Skill, dur);
            if ((d.Rand() & 0x3F) > Quality + Skill) return (-2, Quality, true, 0);
            int off = 4 + (d.Rand() & 7);
            return Quality - off > 0 ? (-1, Quality - off, false, off) : (-2, 0, false, off);
        }

        var seeds = new List<uint>();
        int destroys = 0, damages = 0;
        bool eleven = false;
        for (uint s = 1; s < 50000 && (destroys < 2 || damages < 2 || !eleven); s++)
        {
            if (new BatchB.DosRand(s).SkillCheck(Skill, dur) != -1) continue;
            var o = Dos(s);
            if (o.destroyRoll && destroys < 2) { destroys++; seeds.Add(s); }
            else if (!o.destroyRoll && o.off == 11 && !eleven) { eleven = true; damages++; seeds.Add(s); }
            else if (!o.destroyRoll && o.off != 11 && damages < 2) { damages++; seeds.Add(s); }
        }
        if (destroys == 0 || !eleven) { _problem = $"no seed for a destroying or an 11 point critical failure at durability {dur}"; return; }
        var (x, y) = BatchB.OpenSquareNearPlayer();
        string cite = c.Game == "uw1" ? "uw1 WORLDEV.C:642-645, -2 untouched when (rand() & 0x3F) > quality + skill, else 4 + (rand() & 7) off" : "uw2 WORLDEV.C:1023-1026, -2 untouched when (rand() & 0x3F) > quality + skill, else 4 + (rand() & 7) off";
        foreach (uint seed in seeds)
        {
            var obj = BatchB.SpawnStatic(item, x, y);
            if (obj == null) { _problem = "no object could be made"; return; }
            obj.quality = Quality;
            BatchB.Seed(seed);
            int result = repair.RepairObjectSkillCheck(Skill, obj);
            var e = Dos(seed);
            c.Note($"repair seed {seed}: result {result} quality {obj.quality} (original {e.result}, {e.quality})");
            _numbers.Add(($"repair result on a critical failure (seed {seed}, quality {Quality}, skill {Skill})", e.result, result, cite));
            _numbers.Add(($"quality after it (seed {seed})", e.quality, obj.quality, cite));
            ObjectRemover_OLD.DeleteObjectFromTile_DEPRECIATED(obj.tileX, obj.tileY, obj.index, true);
        }
    }

    static RunicMagic Pick(int circle)
    {
        return RunicMagic.SpellList.FirstOrDefault(s => s != null && s.SpellLevel == circle && s.SpellMajorClass <= 3);
    }

    async Task Refusals(ScenarioContext c)
    {
        var heard = new List<byte>();
        UWsoundeffects.AvatarRequestObserver = e => heard.Add(e);
        try
        {
            var cases = new List<(string what, RunicMagic spell, System.Action prepare, string message, string cite)>();
            if (c.Game == "uw2")
            {
                if (playerdat.dungeon_level < 1 || playerdat.dungeon_level > 8) { _problem = $"the player is on level {playerdat.dungeon_level}, not in Britannia"; return; }
                cases.Add(("a circle 4 spell in Britannia", Pick(4), () => { }, Plain(GameStrings.GetString(1, GameStrings.str_casting_was_not_successful_)), "fail_spell(3), uw2 RUNES.C:202-207, 232-233"));
            }
            cases.Add(("a circle 2 spell at experience level 1", Pick(2), () => playerdat.play_level = 1, null, c.Game == "uw1" ? "fail_spell(0), uw1 RUNES.C:221-226, 245-246" : "fail_spell(0), uw2 RUNES.C:202-207, 234-235"));
            cases.Add(("a circle 1 spell with no mana", Pick(1), () => playerdat.play_mana = 0, null, c.Game == "uw1" ? "fail_spell(1), uw1 RUNES.C:221-226, 247-248" : "fail_spell(1), uw2 RUNES.C:202-207, 236-237"));
            foreach (var (what, spell, prepare, message, cite) in cases)
            {
                if (spell == null) { _problem = $"no spell for {what}"; return; }
                BatchB.ReadyCaster();
                prepare();
                BatchB.SelectSpell(spell.SpellIndex);
                uimanager.instance.scroll.Clear();
                heard.Clear();
                BatchB.CastSelected();
                await c.Frames(2);
                SpellCasting.currentSpell = null;
                RunicMagic.PendingSpellCost = 0;
                string seen = ScrollText();
                c.Note($"{what}: scroll \"{seen}\", effects asked for: {Effects(heard)}");
                _facts.Add((heard.Contains(UWsoundeffects.SoundEffectSpellFailure), $"{what}: effect 0x16 not asked for at the avatar (asked for: {Effects(heard)}) ({cite})"));
                if (message != null) _facts.Add((seen == message, $"{what}: expected \"{message}\", scroll \"{seen}\" ({cite})"));
            }
        }
        finally
        {
            UWsoundeffects.AvatarRequestObserver = null;
        }
    }

    async Task Backfire(ScenarioContext c)
    {
        // The original's casting roll for circle n at Casting 0 (uw1 RUNES.C:249, uw2 RUNES.C:238), and the lowest circle at which
        // its best score, value - target with rand() % 31 at 0, is 2 or less, so skill_check can give -1 (SKILLCHK.C).
        const int Casting = 0;
        (int value, int target) Roll(int circle) => c.Game == "uw1" ? (Casting + 5, circle * 2) : (Casting, circle * 3);
        int circle = 1;
        while (circle <= 8 && Roll(circle).value - Roll(circle).target > 2) circle++;
        var spell = Pick(circle);
        if (spell == null) { _problem = $"no circle {circle} spell of class 0..3"; return; }
        var (value, target) = Roll(circle);
        uint seed = 0;
        for (uint s = 1; s < 50000 && seed == 0; s++)
            if (new BatchB.DosRand(s).SkillCheck(value, target) == -1) seed = s;
        if (seed == 0) { _problem = $"no seed under 50000 gives skill_check({value}, {target}) -1"; return; }
        int minor = spell.SpellMinorClass;
        byte expected = c.Game == "uw1" ? (byte)0x10 : minor == 0xC ? (byte)0x29 : minor == 9 ? (byte)0x28 : (byte)0x10;
        string roll = c.Game == "uw1" ? $"uw1 RUNES.C:249, skill_check(Casting + 5, circle * 2) = skill_check({value}, {target}) gives -1 on seed {seed}, then string 0xD6"
                                      : $"uw2 RUNES.C:238, skill_check(Casting, circle * 3) = skill_check({value}, {target}) gives -1 on seed {seed}, then string 0xD6";
        string cite = c.Game == "uw1" ? "uw1 RUNES.C:264-265, effect 0x10 once do_spell takes the curse" : "uw2 RUNES.C:253-306, SPELLC_BACKFIRE's sound by the spell's own minor";
        string backfires = Plain(GameStrings.GetString(1, GameStrings.str_the_spell_backfires_));
        var heard = new List<byte>();
        UWsoundeffects.AvatarRequestObserver = e => heard.Add(e);
        try
        {
            BatchB.ReadyCaster(Casting);
            playerdat.play_hp = playerdat.max_hp;
            BatchB.SelectSpell(spell.SpellIndex);
            uimanager.instance.scroll.Clear();
            heard.Clear();
            BatchB.Seed(seed);
            BatchB.CastSelected();
            await c.Frames(2);
            SpellCasting.currentSpell = null;
            RunicMagic.PendingSpellCost = 0;
            string seen = ScrollText();
            c.Note($"circle {circle} spell {spell.SpellIndex}, minor 0x{minor:X}, seed {seed}: scroll \"{seen}\", effects asked for: {Effects(heard)}");
            _facts.Add((seen == backfires, $"a circle {circle} cast at Casting {Casting} on seed {seed}: expected \"{backfires}\" alone, scroll \"{seen}\" ({roll})"));
            _facts.Add((heard.Count == 1 && heard[0] == expected, $"a backfire the curse took: expected effect 0x{expected:X}, asked for: {Effects(heard)} ({cite})"));
        }
        finally
        {
            UWsoundeffects.AvatarRequestObserver = null;
        }
    }

    public override async Task Act(ScenarioContext c)
    {
        Repair(c);
        if (_problem != null) return;
        await Refusals(c);
        if (_problem != null) return;
        await Backfire(c);
        if (_problem != null) return;
        await Debris(c);
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
