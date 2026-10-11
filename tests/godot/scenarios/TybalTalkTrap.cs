using System.Threading.Tasks;
using Godot;

namespace Underworld.Scenarios;

/// <summary>
/// The level 7 hack trap 0x32 makes one of Tybal's people talk (UW1). The original's do_trap_hack
/// case 0x32 runs gronk_whoami(0xD8, 0, 0, gronkify_talkto_player) (uw1/src/event/TRIGGER.C:526-527):
/// the first active critter whose whoami is 0xD8 (uw1/src/combat/SPELLS.C:516-531) is sent home if
/// it was waiting (goal 7 becomes goal 1, GOAL_GO_HOME) and talks to the player (TalkTo)
/// (TRIGGER.C:486-493).
///
/// The scenario makes a critter (item 0x40) in the mobile list next to the player with whoami 0xD8
/// and goal 7, and fires a hack trap of quality 0x32 through trap.ActivateTrap with the player as
/// the one who set it off, as the level's trigger does. Once the state is read, the conversation is
/// ended as a player would end it (as GuardTalk does): every [MORE] passed and every menu answered
/// with its last option, until the game is back in play.
/// </summary>
public sealed class TybalTalkTrap : Scenario
{
    const int Whoami = 0xD8;
    const ulong TalkMs = 30000;
    const int MaxMenus = 60;

    public override string Name => "hack-trap-tybal-talk";
    public override string[] Games => new[] { "uw1" };
    public override string Cites => "underworld-exhumed uw1/src/event/TRIGGER.C:486-493, 526-527; uw1/src/combat/SPELLS.C:516-531";

    string _problem;
    uwObject _npc;
    bool _talking, _canTalk;
    int _talker, _goal;

    public override Task Setup(ScenarioContext c)
    {
        var map = UWTileMap.current_tilemap;
        int px = playerdat.playerObject.tileX, py = playerdat.playerObject.tileY;
        _npc = ObjectCreator.spawnObjectInTile(0x40, px, py, 1, 1, (short)(map.Tiles[px, py].floorHeight << 3), ObjectFreeLists.ObjectListType.MobileList);
        if (_npc == null) { _problem = "no critter could be made"; return Task.CompletedTask; }
        _npc.npc_whoami = Whoami;
        _npc.npc_goal = 7;
        _npc.npc_attitude = 2;
        if (uimanager.CurrentGameMode == uimanager.GameModes.CONVERSATION) _problem = "already in a conversation";
        return Task.CompletedTask;
    }

    public override async Task Act(ScenarioContext c)
    {
        if (_problem != null) return;
        int slot = ObjectCreator.PrepareNewObject(0x183);   // ITEM_HACK_TRAP, uw1/src/include/items.h
        if (slot == 0) { _problem = "no hack trap could be made"; return; }
        var t = UWTileMap.current_tilemap.LevelObjects[slot];
        t.quality = 0x32; t.owner = 0; t.link = 0; t.is_quant = 0;
        trap.ActivateTrap(character: 1, trapObj: t, ObjectUsed: null,
            triggerX: playerdat.playerObject.tileX, triggerY: playerdat.playerObject.tileY, objList: UWTileMap.current_tilemap.LevelObjects);
        await c.Frames(2);
        _talking = uimanager.CurrentGameMode == uimanager.GameModes.CONVERSATION;
        _talker = ConversationVM.currentTalker?.npc_whoami ?? -1;
        _goal = _npc.npc_goal;
        c.Note($"in conversation {_talking} with whoami {(_talker < 0 ? "none" : $"0x{_talker:X}")}; critter's goal {_goal}");
        if (!_talking)
        {
            // Precondition: the critter can be talked to at all (conversation 0xD8 exists), as a
            // player's talk command would start it.
            ConversationVM.StartConversation(_npc);
            _canTalk = uimanager.CurrentGameMode == uimanager.GameModes.CONVERSATION;
            c.Note($"talking to the 0xD8 critter directly starts a conversation: {_canTalk}");
        }
        else _canTalk = true;
        await EndConversation(c);
    }

    /// <summary>Ends the conversation through its script's own exit, so no conversation is left open.</summary>
    static async Task EndConversation(ScenarioContext c)
    {
        int menus = 0;
        ulong deadline = Time.GetTicksMsec() + TalkMs;
        while (Time.GetTicksMsec() < deadline && uimanager.CurrentGameMode == uimanager.GameModes.CONVERSATION && menus <= MaxMenus)
        {
            if (MessageDisplay.WaitingForMore) MessageDisplay.WaitingForMore = false;
            if (MessageDisplay.WaitingForTypedInput) MessageDisplay.WaitingForTypedInput = false;
            if (ConversationVM.WaitingForInput)
            {
                ConversationVM.PlayerNumericAnswer = System.Math.Max(1, ConversationVM.MaxAnswer);
                ConversationVM.WaitingForInput = false;
                menus++;
            }
            await c.Frames(1);
        }
        for (int f = 0; f < 600 && uimanager.CurrentGameMode != uimanager.GameModes.GAME; f++)
        {
            if (MessageDisplay.WaitingForMore) MessageDisplay.WaitingForMore = false;
            await c.Frames(1);
        }
        c.Note($"conversation ended after {menus} menus; mode now {uimanager.CurrentGameMode}");
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        if (!_canTalk) return "precondition: a critter with whoami 0xD8 has no conversation to start";
        var k = new Checks();
        k.True(_talking && _talker == Whoami, $"hack trap 0x32 expected the 0xD8 critter to talk to the player, got in conversation {_talking} with whoami {_talker} (TalkTo: uw1 TRIGGER.C:491, 526-527)");
        k.Equal("the waiting 0xD8 critter's goal", 1, _goal, "goal 7 becomes GOAL_GO_HOME: uw1 TRIGGER.C:488-489");
        return k.Result;
    }
}
