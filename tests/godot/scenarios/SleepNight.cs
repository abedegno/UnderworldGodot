using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace Underworld.Scenarios;

/// <summary>
/// What a night's sleep does to the player. In the original's player_sleep, an uninterrupted
/// night (uw2/src/game/SKILLS.C:421-535; uw1/src/game/SKILLS.C:516-610):
/// - heals restore_hp(regen + regen * comfort - 1), regen being fatigue / 2 + 2 at most 5 and
///   comfort 1 when hunger is above 0x40 and the player sleeps in a bed or bedroll
///   (uw2 SKILLS.C:512-521, uw1 SKILLS.C:585-594); restore_hp with a positive amount n gives
///   max_hp * (n + (rand() &amp; 3)) / 16 + 1, capped at the maximum (uw2/src/combat/SPELLS.C:176-194,
///   uw1/src/combat/SPELLS.C:186);
/// - costs 24 to 55 hunger, player_eat(-24 - (rand() &amp; 0x1F)) (uw2 SKILLS.C:528, uw1 SKILLS.C:601);
/// - takes 32 off drunkenness (uw2 SKILLS.C:529-532, uw1 SKILLS.C:602-605);
/// - burns the lights twice, DegradeLights(hours * 180, 0) for the first 2 to 6 hours and again for
///   the rest of a 7 to 10 hour night (uw2 SKILLS.C:472, 511; uw1 SKILLS.C:549, 584): with counter 0
///   every lit light burns 1 + amount / its burn period, and goes out when that reaches its quality,
///   at quality 1 in UW2 and 0 in UW1 (uw2/src/game/PLAYTIME.C:174-210, uw1/src/game/PLAYTIME.C:150-185).
///
/// The scenario sets fatigue 0x30 (regen 5), hunger 0xC0 (comfort 1 with the bedroll), 10 hit points
/// (so the night is not lengthened), drunkenness 0x30 (a 6 bit field, uw2/src/include/player.h:57), and puts the lit light with the longest burn
/// period at quality 63 on a shoulder. It sleeps the way a player does, using a bedroll (the port's
/// use.Use, then sleep.Sleep(1)), and reads everything as soon as the sleep returns, before the
/// dream and the settle frames. The hours are random, so the light's expected state is the set of
/// outcomes over every possible split of the night. Precondition: the night ran to its end (fatigue 0).
///
/// The port once healed HPRegenerationChange(regen * comfort), cost 2 to 32 hunger, took 16 off
/// drunkenness, and passed hours * 0x180 and hours * 0xB4 to UpdateLightStability as a counter,
/// burning at most a point each time (src/World/sleep.cs:117, 163, 192, 197, UpdateIntoxication).
/// </summary>
public sealed class SleepNight : Scenario
{
    const int BedrollItem = 0x121, Fatigue = 0x30, Hunger = 0xC0, StartHp = 10, Drunk = 0x30, LightQuality = 63;
    const ulong SettleMs = 30000;

    public override string Name => "sleep-night";
    public override string Cites => "underworld-exhumed uw2/src/game/SKILLS.C:421-535, uw2/src/combat/SPELLS.C:176-194, uw2/src/game/PLAYTIME.C:174-210; uw1/src/game/SKILLS.C:516-610, uw1/src/game/PLAYTIME.C:150-185";

    string _problem;
    uwObject _light;
    int _lightItem, _lightPeriod, _maxHp;
    int _hpAfter, _hungerAfter, _drunkAfter, _fatigueAfter, _lightItemAfter, _lightQualityAfter;
    bool _used;

    public override Task Setup(ScenarioContext c)
    {
        // The lit light (types 4..7 of class 0x90) with the longest burn period, so the night leaves it lit.
        _lightItem = -1;
        for (int id = 0x94; id <= 0x97; id++)
            if (lightsourceObjectDat.duration(id) > 0 && (_lightItem < 0 || lightsourceObjectDat.duration(id) > _lightPeriod))
            { _lightItem = id; _lightPeriod = lightsourceObjectDat.duration(id); }
        if (_lightItem < 0) { _problem = "no lit light type with a burn period"; return Task.CompletedTask; }
        _light = BatchB.Equip(_lightItem, 5, LightQuality);
        if (_light == null) { _problem = "the light could not be put on the shoulder (slot 5)"; return Task.CompletedTask; }
        if (c.Game == "uw2") { playerdat.SetQuest(50, 0); playerdat.SetQuest(54, 0); }
        playerdat.play_fatigue = Fatigue;
        playerdat.play_hunger = Hunger;
        playerdat.play_hp = StartHp;
        playerdat.intoxication = Drunk;
        playerdat.play_poison = 0;
        _maxHp = playerdat.max_hp;
        c.Note($"light item 0x{_lightItem:X}, burn period {_lightPeriod}; max hit points {_maxHp}");
        return Task.CompletedTask;
    }

    public override async Task Act(ScenarioContext c)
    {
        if (_problem != null) return;
        int slot = ObjectCreator.PrepareNewObject(BedrollItem);
        var bedroll = slot != 0 ? UWTileMap.current_tilemap.LevelObjects[slot] : null;
        if (bedroll == null || bedroll.item_id != BedrollItem) { _problem = "no bedroll could be made"; return; }
        _used = use.Use(bedroll, playerdat.playerObject, UWTileMap.current_tilemap.LevelObjects, WorldObject: true);
        _hpAfter = playerdat.play_hp;
        _hungerAfter = playerdat.play_hunger;
        _drunkAfter = playerdat.intoxication;
        _fatigueAfter = playerdat.play_fatigue;
        _lightItemAfter = _light.item_id;
        _lightQualityAfter = _light.quality;
        c.Note($"after the night: hit points {StartHp} -> {_hpAfter}, hunger 0x{Hunger:X} -> 0x{_hungerAfter:X}, drunkenness 0x{Drunk:X} -> 0x{_drunkAfter:X}, fatigue 0x{_fatigueAfter:X}, light 0x{_lightItemAfter:X} quality {LightQuality} -> {_lightQualityAfter}");

        ulong deadline = Time.GetTicksMsec() + SettleMs;
        int stable = 0;
        while (Time.GetTicksMsec() < deadline && stable < 30)
        {
            await c.Frames(1);
            stable = (uimanager.InGame && !cutsplayer.IsPlaying && !uimanager.blockinput) ? stable + 1 : 0;
        }
    }

    /// <summary>Every (item, quality) the light can end at, over the night's possible hours.</summary>
    HashSet<(int, int)> LightOutcomes(bool uw2)
    {
        var set = new HashSet<(int, int)>();
        for (int first = 2; first <= 6; first++)
            for (int total = 7; total <= 10; total++)
            {
                int item = _lightItem, q = LightQuality;
                foreach (int hours in new[] { first, total - first })
                {
                    if (item != _lightItem) break;      // out already: no longer a lit light
                    int amount = hours * 180, burn = 1 + (amount > 1 ? amount / _lightPeriod : 0);   // counter 0 % period == 0
                    if (uw2 ? q - 1 > burn : q > burn) q -= burn;
                    else { q = uw2 ? 1 : 0; item -= 4; }
                }
                set.Add((item, q));
            }
        return set;
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        if (!_used) return "precondition: using the bedroll did nothing";
        if (_fatigueAfter != 0) return $"precondition: the sleep did not run to its end (fatigue 0x{_fatigueAfter:X}, not 0)";
        bool uw2 = c.Game == "uw2";
        var k = new Checks();
        int lo = System.Math.Min(_maxHp, StartHp + (_maxHp * 9 >> 4) + 1), hi = System.Math.Min(_maxHp, StartHp + (_maxHp * 12 >> 4) + 1);
        k.Within("hit points after the night (restore_hp(9): max * (9..12) / 16 + 1)", lo, hi, _hpAfter,
            uw2 ? "uw2 SKILLS.C:512-521, SPELLS.C:181-186" : "uw1 SKILLS.C:585-594, SPELLS.C:186");
        k.Within("hunger after the night (24..55 less)", Hunger - 55, Hunger - 24, _hungerAfter, uw2 ? "uw2 SKILLS.C:528" : "uw1 SKILLS.C:601");
        k.Equal("drunkenness after the night (32 less)", Drunk - 0x20, _drunkAfter, uw2 ? "uw2 SKILLS.C:529-532" : "uw1 SKILLS.C:602-605");
        var outcomes = LightOutcomes(uw2);
        var lows = new List<string>();
        foreach (var (i, q) in outcomes) lows.Add($"0x{i:X} q{q}");
        k.True(outcomes.Contains((_lightItemAfter, _lightQualityAfter)),
            $"light after the night expected one of [{string.Join(", ", lows)}], got 0x{_lightItemAfter:X} q{_lightQualityAfter} ({(uw2 ? "uw2 SKILLS.C:472, 511, PLAYTIME.C:174-210" : "uw1 SKILLS.C:549, 584, PLAYTIME.C:150-185")}, burn 1 + hours * 180 / period)");
        return k.Result;
    }
}
