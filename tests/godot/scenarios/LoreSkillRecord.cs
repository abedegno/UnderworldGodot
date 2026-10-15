using System.Collections.Generic;
using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// What raising Lore does besides the skill. The original's get_skill, after any attempt on
/// Lore, raised or refused, clears every lore try on the map (clear_all_loretries, which clears
/// heading bit 2 on each object in a tile's list that is not mobile, a rect or a trap, and not
/// render type 2) and stores the Lore skill for the level at player->lore[PlayerLevel], with
/// PlayerLevel counted from 1. UW1 stores it only when PlayerLevel &lt;= 8
/// (uw1/src/game/SKILLS.C:212-217, WORLDEV.C:728-751; lore at 0xC2, player.h:98); UW2 always
/// does (uw2/src/game/SKILLS.C:180-184, WORLDEV.C:1109-1132; lore at 0x310, player.h:101). The
/// port's PLAYER.DAT offsets are DOS's plus one (pdat keeps the xor seed at index 0), so the
/// slot for level L is GetAt(0xC3 + L) in UW1 and GetAt(0x311 + L) in UW2.
///
/// Each row marks a static object on the level 1 map as examined, puts a sentinel in the lore
/// slots for the level and the one below it, sets dungeon_level and Lore, calls the port's
/// playerdat.IncreaseSkill(8) (src/player/playerdatskills.cs), and checks the mark, the slot
/// and the sentinel below. Rows: a raise allowed on level 1, a raise refused at 30 on level 3,
/// a raise refused above twice Intelligence on level 5, and on level 9 a raise allowed and one
/// refused (UW1 records nothing there).
///
/// The port once returned before the bookkeeping when the raise was refused, and stored the
/// value at lore[PlayerLevel - 1], so on UW1's level 9 it wrote lore[8].
/// </summary>
public sealed class LoreSkillRecord : Scenario
{
    // (level, intelligence, lore before, seed)
    static readonly (int Level, int Int, int Before, uint Seed)[] Rows =
    {
        (1, 20, 5, 11),    // allowed
        (3, 20, 30, 22),   // refused: at 30
        (5, 4, 10, 33),    // refused: above twice the attribute
        (9, 20, 6, 44),    // allowed, level 9
        (9, 20, 30, 55),   // refused, level 9
    };

    const byte Sentinel = 0xA5;

    public override string Name => "lore-skill-record";
    public override string Cites => "underworld-exhumed uw1/src/game/SKILLS.C:199-217, uw1/src/event/WORLDEV.C:728-751, uw1/src/include/player.h:98; uw2/src/game/SKILLS.C:167-184, uw2/src/event/WORLDEV.C:1109-1132, uw2/src/include/player.h:101";

    record Result(int Level, int Before, int After, int Raised, bool Examined, int Slot, int Below, int Heading);
    readonly List<Result> _results = new();
    uwObject _obj;

    static int SlotOffset(bool uw2, int level) => (uw2 ? 0x311 : 0xC3) + level;

    /// <summary>The first object at the head of a tile list that clear_loretry clears.</summary>
    static uwObject FindClearable()
    {
        var map = UWTileMap.current_tilemap;
        for (int x = 0; x <= 63; x++)
            for (int y = 0; y <= 63; y++)
            {
                int i = map.Tiles[x, y].indexObjectList;
                if (i == 0) continue;
                var o = map.LevelObjects[i];
                if (o.IsStatic && o.majorclass != 5 && o.majorclass != 6 && commonObjDat.rendertype(o.item_id) != 2)
                    return o;
            }
        return null;
    }

    public override Task Setup(ScenarioContext c)
    {
        _obj = FindClearable();
        return Task.CompletedTask;
    }

    public override Task Act(ScenarioContext c)
    {
        if (_obj == null) return Task.CompletedTask;
        bool uw2 = c.Game == "uw2";
        int level = playerdat.dungeon_level;
        foreach (var r in Rows)
        {
            _obj.heading = (short)((_obj.heading & 3) | 4);
            bool examined = (_obj.heading & 4) != 0;
            playerdat.SetAt(SlotOffset(uw2, r.Level), Sentinel);
            playerdat.SetAt(SlotOffset(uw2, r.Level - 1), Sentinel);
            playerdat.dungeon_level = r.Level;
            playerdat.INT = r.Int;
            playerdat.SetSkillValue(8, r.Before);
            int before = playerdat.Lore;
            BatchB.Seed(r.Seed);
            int raised = playerdat.IncreaseSkill(8);
            _results.Add(new Result(r.Level, before, playerdat.Lore, raised, examined,
                playerdat.GetAt(SlotOffset(uw2, r.Level)), playerdat.GetAt(SlotOffset(uw2, r.Level - 1)), _obj.heading));
            playerdat.dungeon_level = level;
        }
        foreach (var r in _results)
            c.Note($"level {r.Level}: lore {r.Before} -> {r.After} (result {r.Raised}), heading {r.Heading}, slot {r.Slot}, slot below {r.Below}");
        return Task.CompletedTask;
    }

    public override string Assert(ScenarioContext c)
    {
        if (_obj == null) return "precondition: no static object on the level 1 map for clear_loretry to clear";
        bool uw2 = c.Game == "uw2";
        string cite = uw2 ? "uw2 SKILLS.C:180-184" : "uw1 SKILLS.C:212-217";
        string triesCite = uw2 ? "uw2 SKILLS.C:182, WORLDEV.C:1109-1132" : "uw1 SKILLS.C:214, WORLDEV.C:728-751";
        foreach (var r in _results)
        {
            if (!r.Examined) return $"precondition: the object on level {r.Level} was not marked examined";
            if (r.Before != Rows[_results.IndexOf(r)].Before) return $"precondition: Lore read back {r.Before} on level {r.Level}";
        }
        var k = new Checks();
        for (int i = 0; i < _results.Count; i++)
        {
            var r = _results[i];
            bool refused = Rows[i].Before >= 30 || Rows[i].Int * 2 < Rows[i].Before;
            string what = $"Lore {(refused ? "refused" : "raised")} from {r.Before} on level {r.Level}";
            k.Equal($"{what}: result", refused ? 0 : 1, r.Raised, uw2 ? "uw2 SKILLS.C:167-168" : "uw1 SKILLS.C:199-200");
            k.Equal($"{what}: examined bit after", 0, r.Heading & 4, triesCite);
            bool records = uw2 || r.Level <= 8;
            k.Equal($"{what}: lore slot for the level", records ? r.After : Sentinel, r.Slot, cite);
            k.Equal($"{what}: lore slot for the level below", Sentinel, r.Below, cite);
        }
        return k.Result;
    }
}
