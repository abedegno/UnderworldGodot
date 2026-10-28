using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Godot;

namespace Underworld.Scenarios;

/// <summary>
/// UW1's dragons beside the view take an animation as set_screen_frame(SCR_DRAGON, n) gives it
/// (uw1/src/ui/PANELS.C:295-316). Nothing changes, and rand() is not called, when either dragon
/// already has that animation as its goal or its setting. Otherwise rand() &amp; 1 picks the dragon
/// only when both are idle, or both busy with no goal; a dragon that is idle while the other is
/// busy gets it, and with both busy and one goal set the other one gets it. A busy dragon keeps
/// the new goal and plays it once its own animation ends (adjust_dragons, PANELS.C:498-501).
///
/// The port's StartDragonAnimation draws a dragon for every request and gives up when that one is
/// busy (src/ui/uimanager_dragons.cs), and its busy flags are never set.
///
/// The scenario makes five requests at once, as uimanager.StartDragonAnimation's callers do: 1
/// (scroll), 2 (cower), 4 (nod), then 1 and 2 again. It counts the draws by comparing the port's
/// BorlandRandSource with a copy advanced by the original's draws, and over the next seconds
/// records which animations each head shows, from the frames in AnimFrames.
/// </summary>
public sealed class DragonChoice : Scenario
{
    public override string Name => "dragon-choice";
    public override string[] Games => new[] { "uw1" };
    public override string Cites => "underworld-exhumed uw1/src/ui/PANELS.C:295-316, 498-532";
    public override uint Seed => 7;

    // The port's head animations: 1 scroll, 2 cower, 4 nod; the right dragon's rows are 5 on.
    static readonly int[] Requests = { 1, 2, 4, 1, 2 };
    readonly int[] _draws = new int[Requests.Length];
    readonly int[] _expectedDraws = new int[Requests.Length];
    readonly List<int>[] _shown = { new(), new() };
    readonly List<int>[] _expectedShown = { new(), new() };
    string _problem;

    public override async Task Setup(ScenarioContext c)
    {
        // Let anything the new game started finish (an animation is six frames of 0.2 s).
        await Wait(c, 2000);
        if (uimanager.CurrentGameMode != uimanager.GameModes.GAME) _problem = "not in game";
    }

    static async Task Wait(ScenarioContext c, ulong ms)
    {
        ulong until = Time.GetTicksMsec() + ms;
        while (Time.GetTicksMsec() < until) await c.Frames(1);
    }

    public override async Task Act(ScenarioContext c)
    {
        if (_problem != null) return;
        if (Rng.Source is not BorlandRandSource src) { _problem = "Rng.Source is not a BorlandRandSource"; return; }
        var frames = typeof(uimanager).GetField("DragonFrames", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) as ImageTexture[];
        var anims = typeof(uimanager).GetField("AnimFrames", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) as int[,];
        if (frames == null || anims == null) { _problem = "no dragon frames"; return; }

        // The original's choices, on a copy of the port's random source: uw1 PANELS.C:296-315.
        var copy = new BorlandRandSource(src.Seed);
        int first = copy.Rand() & 1;       // both idle: drawn
        _expectedDraws[0] = 1;
        _expectedShown[first].Add(1);
        _expectedShown[1 - first].Add(2); // the other is idle: no draw
        int nod = copy.Rand() & 1;         // both busy, no goal: drawn
        _expectedDraws[2] = 1;
        _expectedShown[nod].Add(4);        // played after its own animation ends
        // 1 and 2 again: each is some dragon's setting, so nothing changes and nothing is drawn.

        for (int i = 0; i < Requests.Length; i++)
        {
            uint before = src.Seed;
            var probe = new BorlandRandSource(before);
            uimanager.StartDragonAnimation(Requests[i]);
            while (probe.Seed != src.Seed && _draws[i] < 8) { probe.Rand(); _draws[i]++; }
        }

        // Which animation each head shows, by the row its frame belongs to (rows 1, 2, 4 and 6, 7, 9).
        var rowOf = new Dictionary<Texture2D, int>();
        foreach (int row in new[] { 1, 2, 4, 6, 7, 9 })
            for (int f = 0; f <= anims.GetUpperBound(1); f++) rowOf[frames[anims[row, f]]] = row;
        ulong until = Time.GetTicksMsec() + 6000;
        while (Time.GetTicksMsec() < until)
        {
            for (int d = 0; d < 2; d++)
            {
                var tex = uimanager.instance.DragonHeads[d].Texture;
                if (tex != null && rowOf.TryGetValue(tex, out int row))
                {
                    int anim = row - 5 * d;
                    var list = _shown[d];
                    if (list.Count == 0 || list[^1] != anim) list.Add(anim);
                }
            }
            await c.Frames(1);
        }
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        var chk = new Checks();
        for (int i = 0; i < Requests.Length; i++)
            chk.Equal($"request {i + 1} (animation {Requests[i]}): rand() calls", _expectedDraws[i], _draws[i], "uw1 PANELS.C:296-312");
        for (int d = 0; d < 2; d++)
        {
            string side = d == 0 ? "left" : "right";
            string got = string.Join(",", _shown[d]), want = string.Join(",", _expectedShown[d]);
            c.Note($"{side} dragon showed [{got}], expected [{want}]");
            chk.True(got == want, $"{side} dragon showed [{got}], expected [{want}] (uw1 PANELS.C:303-315, 498-501)");
        }
        return chk.Result;
    }
}
