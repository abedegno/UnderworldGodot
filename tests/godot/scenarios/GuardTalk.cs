using System.Threading.Tasks;
using Godot;

namespace Underworld.Scenarios;

/// <summary>
/// Guard: talking to a friendly NPC starts its conversation, and ending the conversation returns
/// the game to play.
///
/// The original's TalkTo starts a conversation with a creature that will talk: whoami not 0xFF
/// and not hostile or attacking the player (or goal 10), the conversation being the CNV.ARK block
/// numbered by its whoami (0x100 plus its class for whoami 0) (uw1/src/conv/CONVERSE.C:117-150,
/// uw2 CONVERSE.C:95-142), then switches to the conversation screen (newscr(4)), where
/// strt_converse runs Converse on talking_to and, when the script ends, goes back to the game
/// screen (newscr(1)) (uw1 CONVERSE.C:177-256).
///
/// The NPC is the nearest creature on the level with a whoami of its own (1..0xFE) that is not
/// hostile (attitude not 0) and not set on the player (goal 5, 6 or 9 with target 1). The
/// conversation is driven as a player would: every [MORE] is passed, and every menu is answered
/// with its last option, which in these scripts is the way out; after at most a few dozen menus
/// the conversation must be over.
/// </summary>
public sealed class GuardTalk : Scenario
{
    const ulong TalkMs = 30000;
    const int MaxMenus = 60;

    public override string Name => "guard-talk";
    public override string Cites => "underworld-exhumed uw1/src/conv/CONVERSE.C:117-150 (TalkTo), 177-256 (strt_converse), 278-336 (Converse); uw2/src/conv/CONVERSE.C:95-142";

    string _problem;
    uwObject _npc;
    int _whoami, _talkerIndex = -1, _talkerWhoami = -1, _cnvNo = -1, _menus;
    bool _started, _ended, _inGameAfter;

    public override async Task Setup(ScenarioContext c)
    {
        var map = UWTileMap.current_tilemap;
        var p = playerdat.playerObject;
        int bestD = int.MaxValue;
        for (int i = 2; i < 256; i++)
        {
            var o = map.LevelObjects[i];
            if (o == null || o.majorclass != 1 || !UWTileMap.ValidTile(o.tileX, o.tileY)) continue;
            if (!Guards.InTileChain(o.tileX, o.tileY, o.index)) continue;
            if (o.npc_whoami < 1 || o.npc_whoami > 0xFE || o.npc_attitude == 0 || o.npc_hp == 0) continue;
            if ((o.npc_goal == 5 || o.npc_goal == 6 || o.npc_goal == 9) && o.npc_gtarg == 1) continue;
            int dd = (o.tileX - p.tileX) * (o.tileX - p.tileX) + (o.tileY - p.tileY) * (o.tileY - p.tileY);
            if (dd < bestD) { bestD = dd; _npc = o; }
        }
        if (_npc == null) { _problem = "no friendly NPC with a whoami of its own on this level"; return; }
        _whoami = _npc.npc_whoami;
        c.Note($"NPC {_npc.index} (0x{_npc.item_id:X}) whoami 0x{_whoami:X} attitude {_npc.npc_attitude} goal {_npc.npc_goal} at ({_npc.tileX}, {_npc.tileY}); player at ({p.tileX}, {p.tileY})");
        await c.Frames(1);
        if (uimanager.CurrentGameMode != uimanager.GameModes.GAME) _problem = $"not in play before talking ({uimanager.CurrentGameMode})";
    }

    public override async Task Act(ScenarioContext c)
    {
        if (_problem != null) return;
        talk.Talk(_npc, WorldObject: true);
        await c.Frames(2);
        _started = uimanager.CurrentGameMode == uimanager.GameModes.CONVERSATION;
        _talkerIndex = ConversationVM.currentTalker?.index ?? -1;
        _talkerWhoami = ConversationVM.currentTalker?.npc_whoami ?? -1;
        _cnvNo = ConversationVM.currentConversation?.conversationNo ?? -1;
        if (!_started) return;

        ulong deadline = Time.GetTicksMsec() + TalkMs;
        while (Time.GetTicksMsec() < deadline && uimanager.CurrentGameMode == uimanager.GameModes.CONVERSATION && _menus <= MaxMenus)
        {
            if (MessageDisplay.WaitingForMore) MessageDisplay.WaitingForMore = false;
            if (MessageDisplay.WaitingForTypedInput) MessageDisplay.WaitingForTypedInput = false;
            if (ConversationVM.WaitingForInput)
            {
                ConversationVM.PlayerNumericAnswer = System.Math.Max(1, ConversationVM.MaxAnswer);
                ConversationVM.WaitingForInput = false;
                _menus++;
            }
            await c.Frames(1);
        }
        for (int f = 0; f < 600 && uimanager.CurrentGameMode != uimanager.GameModes.GAME; f++)
        {
            if (MessageDisplay.WaitingForMore) MessageDisplay.WaitingForMore = false;
            await c.Frames(1);
        }
        _ended = uimanager.CurrentGameMode == uimanager.GameModes.GAME;
        _inGameAfter = uimanager.InGame;
        c.Note($"talker {_talkerIndex} whoami 0x{_talkerWhoami:X}, conversation {_cnvNo}; {_menus} menus answered; mode now {uimanager.CurrentGameMode}, in game {_inGameAfter}");
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        var k = new Checks();
        k.True(_started, $"talking to NPC {_npc.index} (whoami 0x{_whoami:X}) expected to start a conversation, mode {uimanager.CurrentGameMode} (TalkTo: uw1 CONVERSE.C:117-150)");
        if (!_started) return k.Result;
        k.Equal("conversation's talker", _npc.index, _talkerIndex, "talking_to = thing: uw1 CONVERSE.C:133");
        k.Equal("talker's whoami", _whoami, _talkerWhoami, "uw1 CONVERSE.C:134");
        if (c.Game == "uw1")
            k.Equal("conversation block", _whoami, _cnvNo, "cnv_id = whoami: uw1 CONVERSE.C:144-145");
        k.True(_ended && _inGameAfter, $"ending the conversation expected to return to play, mode {uimanager.CurrentGameMode} after {_menus} menus (newscr(1): uw1 CONVERSE.C:253)");
        return k.Result;
    }
}
