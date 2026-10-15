using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// Alliances wear off. The player's slow update calls yearly_checkup every 24th tick in UW1
/// (uw1/src/game/PLAYTIME.C:128-141) and every 30th in UW2 (uw2/src/game/PLAYTIME.C:137-149); for
/// every active critter it rerolls the fed bit and, unless rand() % 4 is 1, clears the ally bit
/// (uw1/src/critter/CRITTIME.C:78-89, uw2/src/critter/CRITTIME.C:76-87). So after one such tick
/// each ally stays one with chance 1 in 4.
///
/// The scenario makes eight allied critters beside the player, sets the port's update counter
/// one 20-second tick short of 120 (a multiple of both 24 and 30, so whichever cadence the port
/// uses, the next tick is a slow one) and its second counter one second short, and lets the
/// game run about two seconds. At least one of the eight must have lost its ally bit: all eight
/// keeping it has chance (1/4)^8.
///
/// Both games. The port's slow update has no equivalent (src/player/playerdatloop.cs:233); the ally
/// bit is cleared only on sleep and a level change.
/// </summary>
public sealed class AlliesWearOff : Scenario
{
    public override string Name => "allies-wear-off";
    public override string Cites => "underworld-exhumed uw1/src/critter/CRITTIME.C:78-89, uw1/src/game/PLAYTIME.C:128-141; uw2/src/critter/CRITTIME.C:76-87, uw2/src/game/PLAYTIME.C:137-149";

    const int Allies = 8;
    string _problem;
    readonly List<uwObject> _allies = new();
    int _counterBefore, _counterAfter, _alliesAfter;

    public override Task Setup(ScenarioContext c)
    {
        int id = BatchA.PickCritter(BatchA.Walker);
        for (int i = 0; i < Allies; i++)
        {
            var o = BatchA.SpawnCritter(id, (i % 3) - 1, (i / 3) - 1);
            if (o == null) { _problem = "no critter could be made"; return Task.CompletedTask; }
            o.IsAlly = 1;
            o.npc_attitude = 3;
            _allies.Add(o);
        }
        if (_allies.Any(a => a.IsAlly != 1)) _problem = "an ally bit did not read back set";
        return Task.CompletedTask;
    }

    public override async Task Act(ScenarioContext c)
    {
        if (_problem != null) return;
        var t = typeof(playerdat);
        BatchA.Set(t, "playerUpdateCounter", 119);
        BatchA.Set(t, "secondcounter", 19);
        _counterBefore = BatchA.Get<int>(t, "playerUpdateCounter");
        await c.Frames(150);
        _counterAfter = BatchA.Get<int>(t, "playerUpdateCounter");
        _alliesAfter = _allies.Count(a => a.IsAlly == 1 && a.majorclass == 1);
        c.Note($"update counter {_counterBefore} -> {_counterAfter}; allies {Allies} -> {_alliesAfter}");
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        // The counter passes 120 and the 20-minute step resets it to 0 (playerdatloop.cs).
        if (_counterAfter >= _counterBefore) return $"precondition: no slow tick ran (update counter {_counterBefore} -> {_counterAfter})";
        var k = new Checks();
        k.True(_alliesAfter < Allies, $"allies after a slow tick expected fewer than {Allies} (each kept 1 time in 4), got {_alliesAfter} still allies ({(c.Game == "uw1" ? "uw1 CRITTIME.C:86-87, PLAYTIME.C:135" : "uw2 CRITTIME.C:84-85, PLAYTIME.C:144")})");
        return k.Result;
    }
}
