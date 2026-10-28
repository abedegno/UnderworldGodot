using System.Collections.Generic;
using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// A badly failed repair. The original's do_repair rolls skill_check(skill, durability); on -1 it
/// destroys the item only when (rand() &amp; 0x3F) is above quality + skill, and otherwise takes
/// 4 + (rand() &amp; 7) off its quality (destroyed at 0) (uw1/src/event/WORLDEV.C:623-656;
/// uw2/src/event/WORLDEV.C:1004-1037). So a good item in skilled hands, quality + skill above 63,
/// is never destroyed by a critical failure.
///
/// The scenario uses the armour or weapon type with the highest durability, quality 60, skill 10,
/// and for each of a few seeds whose first draw makes the original's skill_check give -1 (found by
/// the scenario), seeds the game's generator and calls the port's repair.RepairObjectSkillCheck,
/// the attempt itself (the confirmation and the time around it are in RepairLogic, a coroutine that
/// waits for the player's answer). Result and quality must be the original's over the same draws:
/// -1 (damaged) and 60 less 4..11.
///
/// The port once destroyed the item when its roll was at or below quality + skill
/// (src/interaction/repair.cs:208), the reverse, so such an item was always lost. (The fate test
/// in UW1's repair_item, which can still save a destroyed item, and the 15 minute floor on the time,
/// are outside this call.)
/// </summary>
public sealed class RepairCriticalFailure : Scenario
{
    const int Quality = 60, Skill = 10;

    public override string Name => "repair-critical-failure";
    public override string Cites => "underworld-exhumed uw1/src/event/WORLDEV.C:623-656 (do_repair); uw2/src/event/WORLDEV.C:1004-1037";

    string _problem;
    int _item = -1, _durability;
    readonly List<(uint seed, int expResult, int expQuality, int result, int quality)> _results = new();

    static int Durability(int id) => id < 0x20 ? (sbyte)weaponObjectDat.durability(id) : (sbyte)armourObjectDat.durability(id);

    (int result, int quality) Dos(uint seed)
    {
        var d = new BatchB.DosRand(seed);
        int check = d.SkillCheck(Skill, _durability);
        int nq;
        switch (check)
        {
            case 2: nq = 64; break;
            case 1: nq = 3 + Skill / 5; break;
            case 0: return (1, Quality);
            default:
                if ((d.Rand() & 0x3F) > Quality + Skill) return (-2, Quality);
                nq = -(4 + (d.Rand() & 7));
                break;
        }
        if (Quality + nq > 63) return (3, 63);
        if (Quality + nq > 0) return (nq > 0 ? 2 : -1, Quality + nq);
        return (-2, 0);
    }

    public override Task Setup(ScenarioContext c)
    {
        // Weapons 0x00..0x0F (minor 0) and armour 0x20..0x3F, the kinds get_rep_diff accepts.
        for (int id = 0; id < 0x40; id++)
        {
            if (id >= 0x10 && id < 0x20) continue;
            int dur = Durability(id);
            if (dur > 0 && dur < 0x7F && (_item < 0 || dur > _durability)) { _item = id; _durability = dur; }
        }
        if (_item < 0) _problem = "no weapon or armour with a durability";
        else if (Skill - _durability + 0 > 2) _problem = $"durability {_durability} too low for skill {Skill} to fail badly";
        return Task.CompletedTask;
    }

    public override Task Act(ScenarioContext c)
    {
        if (_problem != null) return Task.CompletedTask;
        var seeds = new List<uint>();
        for (uint s = 1; s < 20000 && seeds.Count < 4; s++)
            if (new BatchB.DosRand(s).SkillCheck(Skill, _durability) == -1) seeds.Add(s);
        var (x, y) = BatchB.OpenSquareNearPlayer();
        foreach (uint seed in seeds)
        {
            var obj = BatchB.SpawnStatic(_item, x, y);
            if (obj == null) { _problem = "no object could be made"; return Task.CompletedTask; }
            obj.quality = Quality;
            BatchB.Seed(seed);
            int result = repair.RepairObjectSkillCheck(Skill, obj);
            var (er, eq) = Dos(seed);
            _results.Add((seed, er, eq, result, obj.quality));
        }
        c.Note($"item 0x{_item:X}, durability {_durability}, quality {Quality}, skill {Skill}");
        foreach (var r in _results) c.Note($"seed {r.seed}: result {r.result} quality {r.quality} (original {r.expResult}, {r.expQuality})");
        return Task.CompletedTask;
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        if (_results.Count == 0) return "precondition: no seed gives a critical failure";
        string cite = c.Game == "uw2" ? "uw2 WORLDEV.C:1023-1026, destroyed only when (rand() & 0x3F) > quality + skill" : "uw1 WORLDEV.C:642-645, destroyed only when (rand() & 0x3F) > quality + skill";
        var k = new Checks();
        foreach (var r in _results)
        {
            k.Equal($"repair result on a critical failure (seed {r.seed}, quality + skill {Quality + Skill})", r.expResult, r.result, cite);
            k.Equal($"quality after a critical failure (seed {r.seed})", r.expQuality, r.quality, cite);
        }
        return k.Result;
    }
}
