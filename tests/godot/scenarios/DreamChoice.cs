using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Godot;

namespace Underworld.Scenarios;

/// <summary>
/// Which dream a night's sleep gives in UW2. The original's dream (uw2/src/game/SKILLS.C:334-381),
/// called at the end of an uninterrupted night (SKILLS.C:538-539), with no dream plant eaten:
/// - the four story dreams come due as X clock 1 reaches 4, 6, 10 and 14; a due stage whose
///   dreamflags bit (quest 145) is CLEAR has not been seen, and the latest such stage is chosen
///   (SKILLS.C:349-356);
/// - with none chosen, a random dream is tried only when rand() % (4 + (sleepfactor &lt;&lt; 2)) == 0,
///   and is then 4 + rand() % 3, dropped if its bit is already set (SKILLS.C:357-368);
/// - a chosen dream n plays cutscene 0x18 + n and its bit is flipped (SKILLS.C:369-375).
///
/// Part 1 sleeps the way a player does (a bedroll, the port's use.Use, then sleep.Sleep(1)) with
/// X clock 1 at 6: quest 145 at 2 must play cutscene 0x18 (stage 0) and at 1 cutscene 0x19 (stage 1),
/// each leaving quest 145 at 3; at 3 (both seen) no story dream may play. Those outcomes do not
/// depend on the random numbers. Part 2 tests the random roll: the night draws many random numbers
/// before the dream, so it calls the port's dream step (sleep.Dreams) directly, with X clock 1 at 2
/// (no story stage due) and quest 145 at 0, so only the roll decides, a fresh Turbo C++ rand() seeded with each of several seeds, and both sleepfactors, and compares
/// the cutscene with the original's dream run on the same rand() stream.
///
/// The port once chose a story stage when its bit was SET, so a new game (quest 145 at 0) never
/// had a story dream, and it tried a random dream every night without the roll
/// (src/World/sleep.cs DreamsUW2). Issue hankmorgan/UnderworldGodot#215.
/// </summary>
public sealed class DreamChoice : Scenario
{
    const int BedrollItem = 0x121, DreamFlagsQuest = 145, XClockCastle = 1, XClockValue = 6, RollXClock = 2, RollFlags = 0, Hunger = 0xC0;
    const ulong SettleMs = 30000;
    static readonly int[] DreamXClocks = { 4, 6, 10, 14 };   // uw2 SKILLS.C:338
    static readonly uint[] Seeds = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 };

    public override string Name => "uw2-dream-choice";
    public override string[] Games => new[] { "uw2" };
    public override string Cites => "underworld-exhumed uw2/src/game/SKILLS.C:334-381 (dream), 538-539 (player_sleep)";

    static readonly FieldInfo CurrentCutscene = typeof(cutsplayer).GetField("currentCutsceneNo", BindingFlags.NonPublic | BindingFlags.Static);
    static readonly MethodInfo DreamsMethod = typeof(sleep).GetMethod("Dreams", BindingFlags.NonPublic | BindingFlags.Static);

    string _problem;
    readonly List<(int flags, bool used, int fatigue, int cutscene, int flagsAfter)> _nights = new();
    readonly List<(uint seed, int sleepfactor, int expected, int actual, int flagsAfter)> _rolls = new();

    /// <summary>The original's dream on a rand() stream from seed: the cutscene it plays, or -1.</summary>
    static int OriginalDream(uint seed, int xclock, int dreamflags, int sleepfactor)
    {
        var rand = new BorlandRandSource(seed);
        int found = -1;
        for (int counter = 0; counter < 4; counter++)
            if (xclock >= DreamXClocks[counter] && (dreamflags & (1 << counter)) != (1 << counter))
                found = counter;                                    // SKILLS.C:350-356
        if (found < 0 && rand.Rand() % (4 + (sleepfactor << 2)) == 0)   // SKILLS.C:357-359; sleepbits 0
        {
            found = 4 + rand.Rand() % 3;                            // SKILLS.C:363
            if (((1 << found) & dreamflags) != 0) found = -1;       // SKILLS.C:364-365
        }
        return found < 0 ? -1 : found + 0x18;                       // SKILLS.C:371
    }

    static int Cutscene() => (int)CurrentCutscene.GetValue(null);

    static async Task Settle(ScenarioContext c)
    {
        if (cutsplayer.IsPlaying) cutsplayer.StopCutscene();
        ulong deadline = Time.GetTicksMsec() + SettleMs;
        int stable = 0;
        while (Time.GetTicksMsec() < deadline && stable < 30)
        {
            await c.Frames(1);
            if (cutsplayer.IsPlaying) cutsplayer.StopCutscene();
            stable = (uimanager.InGame && !cutsplayer.IsPlaying && !uimanager.blockinput) ? stable + 1 : 0;
        }
    }

    public override Task Setup(ScenarioContext c)
    {
        if (CurrentCutscene == null) _problem = "cutsplayer.currentCutsceneNo not found";
        else if (DreamsMethod == null) _problem = "sleep.Dreams not found";
        playerdat.SetQuest(50, 0);
        playerdat.SetQuest(54, 0);
        playerdat.DreamPlantCounter = 0;
        playerdat.SetXClock(XClockCastle, XClockValue);
        return Task.CompletedTask;
    }

    public override async Task Act(ScenarioContext c)
    {
        if (_problem != null) return;

        // Part 1: real nights in a bedroll.
        foreach (int flags in new[] { 2, 1, 3 })
        {
            playerdat.SetQuest(DreamFlagsQuest, flags);
            playerdat.SetXClock(XClockCastle, XClockValue);
            playerdat.DreamPlantCounter = 0;
            playerdat.play_hp = playerdat.max_hp;
            playerdat.play_hunger = Hunger;
            playerdat.play_poison = 0;
            playerdat.play_fatigue = 0x30;
            CurrentCutscene.SetValue(null, -1);
            int slot = ObjectCreator.PrepareNewObject(BedrollItem);
            var bedroll = slot != 0 ? UWTileMap.current_tilemap.LevelObjects[slot] : null;
            if (bedroll == null || bedroll.item_id != BedrollItem) { _problem = "no bedroll could be made"; return; }
            bool used = use.Use(bedroll, playerdat.playerObject, UWTileMap.current_tilemap.LevelObjects, WorldObject: true);
            _nights.Add((flags, used, playerdat.play_fatigue, Cutscene(), playerdat.GetQuest(DreamFlagsQuest)));
            await Settle(c);
        }

        // Part 2: the port's dream step on a known rand() stream.
        foreach (int sleepfactor in new[] { 0, 1 })
            foreach (uint seed in Seeds)
            {
                playerdat.SetQuest(DreamFlagsQuest, RollFlags);
                playerdat.SetXClock(XClockCastle, RollXClock);
                playerdat.DreamPlantCounter = 0;
                CurrentCutscene.SetValue(null, -1);
                Rng.Source = new BorlandRandSource(seed);
                DreamsMethod.Invoke(null, new object[] { sleepfactor });
                _rolls.Add((seed, sleepfactor, OriginalDream(seed, RollXClock, RollFlags, sleepfactor), Cutscene(), playerdat.GetQuest(DreamFlagsQuest)));
                await Settle(c);
            }
        playerdat.SetQuest(DreamFlagsQuest, 0);
        playerdat.SetXClock(XClockCastle, 0);
    }

    static string Cs(int n) => n < 0 ? "none" : $"0x{n:X}";

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        int plays = 0;
        foreach (var r in _rolls) if (r.expected >= 0) plays++;
        if (plays == 0 || plays == _rolls.Count) return $"precondition: the seeds give {plays} random dreams in {_rolls.Count}, so they do not test both sides of the roll";
        var k = new Checks();
        foreach (var n in _nights)
        {
            if (!n.used) return $"precondition: using the bedroll did nothing (quest 145 at {n.flags})";
            if (n.fatigue != 0) return $"precondition: the night with quest 145 at {n.flags} did not run to its end (fatigue 0x{n.fatigue:X})";
            c.Note($"night with X clock 1 at {XClockValue}, quest 145 at {n.flags}: cutscene {Cs(n.cutscene)}, quest 145 after {n.flagsAfter}");
            if (n.flags == 3)
            {
                // No story dream; a random one (0x1C..0x1E) may follow the roll.
                k.True(n.cutscene < 0 || (n.cutscene >= 0x1C && n.cutscene <= 0x1E),
                    $"dream with stages 0 and 1 seen (quest 145 at 3) expected none or a random 0x1C..0x1E, got {Cs(n.cutscene)} (uw2 SKILLS.C:349-368)");
                continue;
            }
            int stage = n.flags == 2 ? 0 : 1;
            k.Equal($"dream cutscene with quest 145 at {n.flags} (stage {stage} unseen)", 0x18 + stage, n.cutscene, "uw2 SKILLS.C:349-356, 371");
            k.Equal($"quest 145 after the dream from {n.flags}", 3, n.flagsAfter, "uw2 SKILLS.C:372");
        }
        int mismatches = 0;
        foreach (var r in _rolls)
        {
            if (r.expected == r.actual) continue;
            mismatches++;
            k.True(false, $"random dream (seed {r.seed}, sleepfactor {r.sleepfactor}, X clock 1 at {RollXClock}, quest 145 at {RollFlags}) expected {Cs(r.expected)}, got {Cs(r.actual)} (uw2 SKILLS.C:357-371)");
        }
        c.Note($"random roll: the original dreams on {plays} of {_rolls.Count} seeded nights; the port differs on {mismatches}");
        foreach (var r in _rolls)
            if (r.expected == r.actual && r.expected >= 0)
                k.Equal($"quest 145 after random dream {Cs(r.expected)} (seed {r.seed})", RollFlags ^ (1 << (r.expected - 0x18)), r.flagsAfter, "uw2 SKILLS.C:372");
        return k.Result;
    }
}
