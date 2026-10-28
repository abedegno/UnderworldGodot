using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// Where the castle's people spend each part of the day (UW2). The original's
/// where_shall_we_hang_out (uw2/src/critter/CRITTIME.C:646-731):
/// hour = game_clock / 0xE1000 % 24, stage = ((hour + 1) &gt;&gt; 1) + 11) % 12 (CRITTIME.C:656-657);
/// loc = rand() % 6, and if rand() % 3 == 0 or stage &lt;= 2 the stage decides: stages 3, 5 and 9
/// loc 4, stages 0..2 and 11 loc 0, stages 6..8 loc 1 (CRITTIME.C:658-670). Miranda (0x88) and
/// everyone while XC_CASTLE is 0 keep loc 0, as do Lord British (0x8E) once quest 115 (quests[28]
/// bit 3) is set and Nystul (0x82) from XC_CASTLE 12 (CRITTIME.C:671-676). Loc 5 is the NPC's
/// home (quality, owner), loc 0 its spot from the xs and ys tables (0xA8: 0x2A, 0x24), locs 1..3
/// a random square of a rectangle, bounds included (x1 + rand() % (x2 - x1 + 1), then y), with
/// the fountain pedestal (0x1F, 0x29) moved one east for loc 2, and loc 4 and anything else the
/// square it stands on (CRITTIME.C:677-730).
///
/// The port's GetCastleDestination (src/traps/a_hack_trap_castleschedule.cs:130-232) takes the
/// hour from TwelveHourClock, 0 to 11 (:138), sends stages 3, 5 and 9 to the throne room (:161),
/// keeps Miranda home only once XC_CASTLE is set (:174), leaves loc 5 at the current square, and
/// leaves out the upper bound of the random squares (:228-229).
///
/// The scenario calls GetCastleDestination (private, by reflection) for a castle NPC, Miranda,
/// Nystul, Lord British and Syria, at every hour, with XC_CASTLE 0, 1 and 12 and quest 115 clear
/// and set, and computes the original's answer from the same Turbo C rand() sequence: before each
/// call it copies the BorlandRandSource the port draws from and replays the original's draws on
/// the copy. The NPC's home (quality, owner) is set apart from the square it stands on.
/// </summary>
public sealed class CastleSchedule : Scenario
{
    static readonly byte[] Xs = { 0x2A, 0x24, 0x15, 0x25, 0x16, 0x19, 0x1B, 0x2C, 0x2B, 0x16, 0x15, 0x18, 0x1A, 0x19 };   // CRITTIME.C:681-682
    static readonly byte[] Ys = { 0x2B, 0x33, 0x2A, 0x23, 0x33, 0x2B, 0x24, 0x30, 0x31, 0x25, 0x22, 0x27, 0x30, 0x22 };   // CRITTIME.C:683-684
    static readonly int[] Whoamis = { 0x83, 0x88, 0x82, 0x8E, 0xA8 };
    const int StandX = 0x26, StandY = 0x28, HomeX = 0x1C, HomeY = 0x2E;

    public override string Name => "castle-schedule-hours";
    public override string[] Games => new[] { "uw2" };
    public override string Cites => "underworld-exhumed uw2/src/critter/CRITTIME.C:646-731";

    string _problem;
    int _calls, _drawn;
    readonly List<string> _wrong = new();

    public override Task Setup(ScenarioContext c) => Task.CompletedTask;

    public override Task Act(ScenarioContext c)
    {
        var m = typeof(a_hack_trap_castleschedule).GetMethod("GetCastleDestination", BindingFlags.NonPublic | BindingFlags.Static);
        if (m == null) { _problem = "a_hack_trap_castleschedule.GetCastleDestination not found"; return Task.CompletedTask; }
        int slot = ObjectCreator.PrepareNewObject(0x40, ObjectFreeLists.ObjectListType.MobileList);
        if (slot == 0) { _problem = "no critter could be made"; return Task.CompletedTask; }
        var npc = UWTileMap.current_tilemap.LevelObjects[slot];
        int clockWas = playerdat.ClockValue, castleWas = playerdat.GetXClock(1), lbWas = playerdat.GetQuest(115);

        foreach (int castle in new[] { 0, 1, 12 })
        foreach (int lb in new[] { 0, 1 })
        foreach (int who in Whoamis)
        for (int hour = 0; hour < 24; hour++)
        {
            if (Rng.Source is not BorlandRandSource src) { _problem = "Rng.Source is not a BorlandRandSource"; break; }
            playerdat.ClockValue = 3 * 24 * 0xE1000 + hour * 0xE1000 + 0x7000;   // day 3, a little into the hour
            playerdat.SetXClock(1, castle);
            playerdat.SetQuest(115, lb);
            npc.npc_whoami = (short)who;
            npc.tileX = StandX; npc.tileY = StandY;
            npc.quality = (short)HomeX; npc.owner = (short)HomeY;

            var copy = new BorlandRandSource(src.Seed);
            var (ex, ey) = Original(copy, playerdat.ClockValue, who, castle, lb);
            var args = new object[] { npc, null, null };
            uint seedBefore = src.Seed;
            m.Invoke(null, args);
            _calls++;
            if (((BorlandRandSource)Rng.Source).Seed != seedBefore) _drawn++;
            int gx = (short)args[1], gy = (short)args[2];
            if (gx != ex || gy != ey)
                _wrong.Add($"hour {hour} whoami 0x{who:X} XC_CASTLE {castle} quest115 {lb}: expected ({ex}, {ey}), got ({gx}, {gy})");
            // Both draw differently when they disagree; restart from the copy so every case
            // starts from where the original would be.
            Rng.Source = copy;
        }
        playerdat.ClockValue = clockWas; playerdat.SetXClock(1, castleWas); playerdat.SetQuest(115, lbWas);
        c.Note($"{_calls} cases, {_wrong.Count} wrong");
        return Task.CompletedTask;
    }

    /// <summary>where_shall_we_hang_out, CRITTIME.C:646-731, drawing from r.</summary>
    static (int x, int y) Original(BorlandRandSource r, int clock, int who, int castle, int lb)
    {
        int hour = (int)((uint)clock / 0xE1000u) % 24;
        int stage = (((hour + 1) >> 1) + 11) % 12;
        int loc = r.Rand() % 6;
        if (r.Rand() % 3 == 0 || stage <= 2)
            switch (stage)
            {
                case 3: case 5: case 9: loc = 4; break;
                case 0: case 1: case 2: case 11: loc = 0; break;
                case 6: case 7: case 8: loc = 1; break;
            }
        if (who == 0x88 || castle == 0) loc = 0;
        else if (who == 0x8E && lb == 1) loc = 0;
        else if (who == 0x82 && castle >= 0xC) loc = 0;
        if (loc == 5) return (HomeX, HomeY);
        if (loc == 0) return who == 0xA8 ? (0x2A, 0x24) : (Xs[who - 0x82], Ys[who - 0x82]);
        int x1, y1, x2, y2;
        switch (loc)
        {
            case 1: x1 = 0x1B; y1 = 0x22; x2 = 0x23; y2 = 0x25; break;
            case 2: x1 = 0x1E; y1 = 0x27; x2 = 0x20; y2 = 0x2B; break;
            case 3: x1 = 0x1D; y1 = 0x2D; x2 = 0x21; y2 = 0x33; break;
            default: return (StandX, StandY);
        }
        int x = x1 + r.Rand() % (x2 - x1 + 1);
        int y = y1 + r.Rand() % (y2 - y1 + 1);
        if (loc == 2 && x == 0x1F && y == 0x29) x++;
        return (x, y);
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        if (_drawn == 0) return "precondition: GetCastleDestination drew nothing from Rng.Source";
        if (_wrong.Count == 0) return null;
        return $"castle destination wrong in {_wrong.Count} of {_calls} cases (uw2 CRITTIME.C:646-731), first: " + string.Join("; ", _wrong.GetRange(0, System.Math.Min(4, _wrong.Count)));
    }
}
