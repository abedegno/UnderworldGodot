using System.Collections.Generic;
using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// A fleeing or wandering critter steering round the player, crit_avoid_player
/// (uw1/src/critter/AI.C:685-713, uw2/src/critter/AI.C:652-686, the same code). crit_flee runs
/// its heading through it with how 0x18 (uw1 AI.C:663, uw2 AI.C:633). Measured from the critter's
/// own position (myxpost, myypost) to the player's: only when the player is closer than how fine
/// units does it steer. th is the heading away from the player, (deltatotheta(dx, dy) + 4) % 8
/// &lt;&lt; 5 (deltatotheta, uw1/src/critter/PATHFIND.C:1076-1086); a heading within 90 degrees of
/// th is kept, the others are bent 45 degrees away from the player.
///
/// The scenario is a vector table: the critter's position at offsets from the player's, near
/// and far, and headings all round; it calls the port's VectorsToPlayer with the AI's globals as
/// a critter fleeing the player has them (its target, the player, at the player's position; its
/// own position at the offset) and compares the heading with the original's.
///
/// Both games. The port steered when the player was far rather than near, measured from the
/// target instead of the critter, and its test for a heading to keep was always true
/// (VectorsToPlayer, src/npc/npcai.cs). The rest of crit_flee is critter-flees.
/// </summary>
public sealed class FleeingCritterSteers : Scenario
{
    public override string Name => "fleeing-critter-steers";
    public override string Cites => "underworld-exhumed uw1/src/critter/AI.C:663, 685-713, uw1/src/critter/PATHFIND.C:1076-1086; uw2/src/critter/AI.C:633, 652-686";

    const int How = 0x18;
    static readonly (int x, int y)[] Offsets = { (5, 3), (-6, 2), (0, -9), (-4, -4), (12, 0), (7, -13), (0, 23), (30, 4), (-25, -25), (40, 0) };

    int _cases, _steered;
    readonly List<string> _diffs = new();

    public override Task Setup(ScenarioContext c) => Task.CompletedTask;

    /// <summary>deltatotheta, uw1/src/critter/PATHFIND.C:1076-1086, with its char arguments.</summary>
    static int DeltaToTheta(int xi, int yi)
    {
        sbyte x = (sbyte)xi, y = (sbyte)yi;
        sbyte dx = (sbyte)(x << 1), dy = (sbyte)(y << 1);
        if (y > dx)
        {
            if (x > -(int)dy) return y > -(int)dx ? 0 : 7;
            return x > dy ? 5 : 6;
        }
        if (x > -(int)dy) return x > dy ? 2 : 1;
        return y > -(int)dx ? 3 : 4;
    }

    /// <summary>crit_avoid_player, uw1/src/critter/AI.C:685-713.</summary>
    static int AvoidPlayer(int heading, int how, int px, int py, int myx, int myy)
    {
        int dx = px - myx, dy = py - myy;
        uint dist = (uint)(dx * dx + dy * dy);
        if (how * how > dist)
        {
            int th = (byte)(((DeltaToTheta(dx, dy) + 4) % 8) << 5);
            int diff = (byte)((th + 0x100 - heading) % 0x100);
            if (diff < 0x40 || diff > 0xC0) return heading;
            if (diff < 0x60) return (th + 0xE0) % 0x100;
            if (diff < 0x80) return (heading + 0x20) % 0x100;
            if (diff > 0xA0) return (th + 0x20) % 0x100;
            return (heading + 0xE0) % 0x100;
        }
        return heading;
    }

    public override Task Act(ScenarioContext c)
    {
        var p = playerdat.playerObject;
        int px = (p.npc_xhome << 3) + p.xpos, py = (p.npc_yhome << 3) + p.ypos;
        var t = typeof(npc);
        foreach (var (ox, oy) in Offsets)
        for (int heading = 0; heading < 0x100; heading += 0x10)
        {
            int myx = px + ox, myy = py + oy;
            // The flee target is the player (crit_flee's mytarget); the critter is at the offset.
            BatchA.Set(t, "currentGTargXCoord", px);
            BatchA.Set(t, "currentGTargYCoord", py);
            BatchA.Set(t, "currObjXCoordinate", myx);
            BatchA.Set(t, "currObjYCoordinate", myy);
            int expected = AvoidPlayer(heading, How, px, py, myx, myy);
            int actual = (int)BatchA.Call(t, "VectorsToPlayer", heading, How);
            _cases++;
            if (expected != heading) _steered++;
            if (expected != actual)
                _diffs.Add($"offset ({ox}, {oy}) heading 0x{heading:X2}: port 0x{actual:X2}, DOS 0x{expected:X2}");
        }
        c.Note($"{_cases} cases, DOS steers {_steered}; {_diffs.Count} differ");
        return Task.CompletedTask;
    }

    public override string Assert(ScenarioContext c)
    {
        if (_steered == 0 || _steered == _cases) return $"precondition: the table is not a test, DOS steers {_steered} of {_cases}";
        if (_diffs.Count == 0) return null;
        return $"steering round the player differs from crit_avoid_player on {_diffs.Count} of {_cases}: {string.Join("; ", _diffs.GetRange(0, System.Math.Min(4, _diffs.Count)))} ({(c.Game == "uw1" ? "uw1 AI.C:685-713" : "uw2 AI.C:652-686")})";
    }
}
