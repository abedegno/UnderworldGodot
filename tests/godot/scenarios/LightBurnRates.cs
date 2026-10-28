using System.Collections.Generic;
using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// How fast lights burn in play. Every 20 seconds of game time the original's duration_check calls
/// DegradeLights(1, counter) (uw2/src/game/PLAYTIME.C:88, uw1/src/game/PLAYTIME.C:97): a lit light
/// (class 0x90, types 4 to 7) in a light slot loses a point of quality when the counter is a
/// multiple of its burn period from the light table (Lights[type].duration), and goes out, turning
/// into its unlit type (4 lower), when it would burn down to nothing: at quality 1 in UW2, where it
/// is left at 1, and at 0 in UW1 (uw2 PLAYTIME.C:174-210; uw1 PLAYTIME.C:150-185).
///
/// For each lit light type with a burn period, the scenario puts one of quality 12 on a shoulder
/// (slot 5, a light slot) and calls the port's playerdat.UpdateLightStability for counters 1 to 40
/// (the port's DegradeLights over its 20 second counter), then compares the item and quality with the
/// original's rule over the same counters, using the port's own table (lightsourceObjectDat.duration).
///
/// The port once used the light's type number as its burn period, leaving the table value unused,
/// and put a light out when it reached quality 1 in both games (src/player/playerdatloop.cs:378-380).
/// </summary>
public sealed class LightBurnRates : Scenario
{
    const int Quality = 12, Counters = 40;

    public override string Name => "light-burn-rates";
    public override string Cites => "underworld-exhumed uw2/src/game/PLAYTIME.C:88, 174-210 (DegradeLights); uw1/src/game/PLAYTIME.C:97, 150-185";

    string _problem;
    readonly List<(int item, int period, int itemAfter, int qualityAfter)> _results = new();

    public override Task Setup(ScenarioContext c)
    {
        for (int s = 5; s <= 8; s++)
            if (playerdat.GetInventorySlotListHead(s) != 0) { _problem = $"light slot {s} is not empty in the new game"; break; }
        return Task.CompletedTask;
    }

    public override Task Act(ScenarioContext c)
    {
        if (_problem != null) return Task.CompletedTask;
        for (int item = 0x94; item <= 0x97; item++)
        {
            int period = lightsourceObjectDat.duration(item);
            if (period == 0) continue;
            var light = BatchB.Equip(item, 5, Quality);
            if (light == null) { _problem = $"light 0x{item:X} could not be put on the shoulder"; return Task.CompletedTask; }
            for (int counter = 1; counter <= Counters; counter++) playerdat.UpdateLightStability(counter);
            _results.Add((item, period, light.item_id, light.quality));
            BatchB.Unequip(5);
            c.Note($"light 0x{item:X}, burn period {period}: quality {Quality} -> {light.quality}, item 0x{light.item_id:X}");
        }
        if (_results.Count == 0) _problem = "no lit light type has a burn period";
        return Task.CompletedTask;
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        bool uw2 = c.Game == "uw2";
        var k = new Checks();
        foreach (var r in _results)
        {
            int item = r.item, q = Quality;
            for (int counter = 1; counter <= Counters && item == r.item; counter++)
            {
                if (counter % r.period != 0) continue;   // amount 1: no extra burn
                int burn = 1;
                if (uw2 ? q - 1 > burn : q > burn) q -= burn;
                else { q = uw2 ? 1 : 0; item -= 4; }
            }
            string cite = uw2 ? "uw2 PLAYTIME.C:191-206, burn period from Lights[]" : "uw1 PLAYTIME.C:166-181, burn period from Lights[]";
            k.Equal($"light 0x{r.item:X} (period {r.period}) item after {Counters} steps", item, r.itemAfter, cite);
            k.Equal($"light 0x{r.item:X} (period {r.period}) quality after {Counters} steps", q, r.qualityAfter, cite);
        }
        return k.Result;
    }
}
