using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// UW1: a hostile creature whose whoami is 0x8C will not talk. UW1's TalkTo lets only whoami 0x16, 0x8E and
/// 0xE7 talk whatever their state; any other creature that is hostile (attitude 0) and not an ally, and not in
/// goal 10, gets "You get no response." (uw1/src/conv/CONVERSE.C:136-143). Whoami 0x8C (Patterson) always
/// talking is UW2's rule (uw2/src/conv/CONVERSE.C:117). UW1's CNV.ARK has a conversation in slot 0x8C, so
/// without the refusal one would start.
///
/// The scenario makes a critter next to the player, standing (goal 0), hostile (attitude 0), not an ally, with
/// whoami 0x8C, talks to it (talk.Talk, the call the talk verb makes) and checks the message and that no
/// conversation started.
/// </summary>
public sealed class Uw1HostileNoTalk : Scenario
{
    public override string Name => "uw1-hostile-no-talk";
    public override string[] Games => new[] { "uw1" };
    public override string Cites => "underworld-exhumed uw1/src/conv/CONVERSE.C:117-150; uw2/src/conv/CONVERSE.C:117";

    string _problem;
    uwObject _npc;
    bool _refused, _started;
    string _text;

    public override async Task Setup(ScenarioContext c)
    {
        await c.Frames(1);
        _npc = BatchA.SpawnCritter(0x40, 1, 0);
        if (_npc == null) { _problem = "no critter could be made"; return; }
        _npc.npc_whoami = 0x8C;
        _npc.npc_attitude = 0;
        _npc.npc_goal = 0;
        _npc.npc_gtarg = 0;
        _npc.IsAlly = 0;
        if (uimanager.CurrentGameMode != uimanager.GameModes.GAME) _problem = $"not in play before talking ({uimanager.CurrentGameMode})";
    }

    public override async Task Act(ScenarioContext c)
    {
        if (_problem != null) return;
        uimanager.instance.scroll.Clear();
        talk.Talk(_npc, WorldObject: true);
        _text = string.Join(" ", System.Linq.Enumerable.Select(uimanager.instance.scroll.Lines, l => l?.LineText ?? "")).Trim();
        await c.Frames(2);
        _started = uimanager.CurrentGameMode == uimanager.GameModes.CONVERSATION || ConversationVM.currentConversation != null;
        _refused = _text.Contains(GameStrings.GetString(7, 1).Trim());
        c.Note($"critter {_npc.index} whoami 0x8C attitude 0: \"{_text}\", mode {uimanager.CurrentGameMode}");
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        var k = new Checks();
        k.True(_refused && !_started, $"a hostile whoami 0x8C expected \"You get no response.\" and no conversation, was \"{_text}\", conversation started {_started} (TalkTo: uw1 CONVERSE.C:136-143)");
        return k.Result;
    }
}
