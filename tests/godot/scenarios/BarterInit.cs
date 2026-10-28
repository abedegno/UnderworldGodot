using System.Threading.Tasks;
using Godot;

namespace Underworld.Scenarios;

/// <summary>
/// A conversation sets up the NPC's trading temper as barter_init does (UW1 and UW2), and UW2's babl_hack modes 8 and 9
/// change greed and fudge as the original does.
///
/// barter_init seeds rand() with the NPC's object index, then works out greed = range(haggle * 6, -25, 25),
/// patience = range(patience, -20, 100) and npc_assess = range((15 - shrewd) * 6, -25, 50) from the NPC's creature class,
/// and adjusts them by the player's charisma: greed - charm * 2, patience + charm >> 1 (uw1/src/conv/BARTER.C:195-203,
/// uw2/src/conv/BARTER.C:156-164). range is base + base * (min + rand() * (max - min) / 0x8000) / 100
/// (uw1 BARTER.C:846-851, uw2 BARTER.C:826-831). babl_hack mode 8 multiplies greed and mode 9 sets fudge, a signed char
/// (uw2 BABLHACK.C:116-119, uw2 BARTER.C:53).
///
/// The port's set-up (src/conversation/conversationinitialisation.cs:169-173) used the bare haggle nibble for greed and
/// 16 - shrewd for npc_assess, had no charisma adjustment, and drew rand() % n from the scenario's stream, not after
/// srand(index); its babl_hack mode 8 multiplied patience and mode 9 kept the whole 16-bit value
/// (src/conversation/conversation_functions/babl_hack.cs).
///
/// The scenario sets the player's charisma to 13 and starts a conversation with the first NPC on level 1 that has one,
/// then checks greed, patience and npc_assess against the original's formula replayed from a fresh
/// BorlandRandSource seeded with the NPC's object index, and against the numbers that gives for that NPC. In UW2 it then
/// runs babl_hack mode 8 with 3 at greed 20 (60, patience unchanged) and mode 9 with 200 (fudge -56).
/// </summary>
public sealed class BarterInit : Scenario
{
    const ulong StartMs = 10000;
    const int Charm = 13;

    public override string Name => "barter-init";
    public override string Cites => "underworld-exhumed uw1/src/conv/BARTER.C:195-203, 846-851; uw2/src/conv/BARTER.C:53, 156-164, 826-831; uw2/src/conv/BABLHACK.C:116-119";

    string _problem;
    int _whoami, _index, _haggle, _patienceNibble, _shrewd;
    int _greed, _patience, _assess, _greedExpected, _patienceExpected, _assessExpected;
    int _hackGreed = -1, _hackGreedResult, _hackPatience, _hackPatienceBefore, _fudge, _fudgeResult;

    /// <summary>range(base, min, max) as the original: the product with base 16-bit (uw1 BARTER.C:846-851).</summary>
    static int OriginalRange(BorlandRandSource r, int b, int min, int max)
    {
        return (short)(b + (short)(b * (min + (int)((long)r.Rand() * (max - min) / 0x8000))) / 100);
    }

    public override async Task Setup(ScenarioContext c)
    {
        var objs = UWTileMap.current_tilemap.LevelObjects;
        playerdat.Charm = Charm;
        uwObject talker = null;
        for (int i = 2; i < 256 && uimanager.CurrentGameMode != uimanager.GameModes.CONVERSATION; i++)
        {
            var o = objs[i];
            if (o == null || o.majorclass != 1 || o.npc_whoami == 0 || o.npc_whoami == 255 || o.npc_hp == 0) continue;
            if (!UWTileMap.ValidTile(o.tileX, o.tileY)) continue;
            ConversationVM.StartConversation(o);
            if (uimanager.CurrentGameMode == uimanager.GameModes.CONVERSATION) talker = o;
        }
        if (talker == null) { _problem = "no NPC on level 1 would talk"; return; }
        _greed = ConversationVM.TradeThreshold;
        _patience = ConversationVM.TradePatience;
        _assess = ConversationVM.NPCAppraisalAccuracy;
        _whoami = talker.npc_whoami;
        _index = talker.index;
        // The creature class's nibbles (critter.h: shrewd is 0x0D's high nibble, haggle and patience 0x0E's low and high).
        _haggle = critterObjectDat.TradeThreshold(talker.item_id);
        _patienceNibble = critterObjectDat.TradePatience(talker.item_id);
        _shrewd = critterObjectDat.TradeAppraisal(talker.item_id);
        var r = new BorlandRandSource((uint)_index);
        _greedExpected = OriginalRange(r, _haggle * 6, -25, 25) - Charm * 2;
        _patienceExpected = OriginalRange(r, _patienceNibble, -20, 100) + (Charm >> 1);
        _assessExpected = OriginalRange(r, (15 - _shrewd) * 6, -25, 50);
        c.Note($"talking to whoami 0x{_whoami:X} (object {_index}, item 0x{talker.item_id:X}), haggle {_haggle}, patience {_patienceNibble}, shrewd {_shrewd}");
        c.Note($"greed {_greed}, patience {_patience}, npc_assess {_assess}");

        ulong deadline = Time.GetTicksMsec() + StartMs;
        int frames = 0;
        while (Time.GetTicksMsec() < deadline && frames < 30) { await c.Frames(1); frames++; }

        if (c.Game != "uw2") return;
        int savedGreed = ConversationVM.TradeThreshold, savedPatience = ConversationVM.TradePatience, savedBonus = ConversationVM.BablTradeBonus;
        var savedValues = ConversationVM.StackValues; int savedStack = ConversationVM.stack, savedPtr = ConversationVM.stackptr;
        try
        {
            ConversationVM.StackValues = new short[64];
            ConversationVM.stack = 0;
            // babl_hack's arguments: the mode at sp - 1 and its value at sp - 2, each a pointer into memory.
            ConversationVM.stackptr = 3;
            ConversationVM.Set(2, 20); ConversationVM.Set(1, 21);
            ConversationVM.Set(20, 8); ConversationVM.Set(21, 3);
            ConversationVM.TradeThreshold = 20;
            _hackPatienceBefore = ConversationVM.TradePatience;
            ConversationVM.babl_hack(talker);
            _hackGreed = ConversationVM.TradeThreshold;
            _hackGreedResult = ConversationVM.result_register;
            _hackPatience = ConversationVM.TradePatience;

            ConversationVM.Set(20, 9); ConversationVM.Set(21, 200);
            ConversationVM.babl_hack(talker);
            _fudge = ConversationVM.BablTradeBonus;
            _fudgeResult = ConversationVM.result_register;
        }
        finally
        {
            ConversationVM.StackValues = savedValues; ConversationVM.stack = savedStack; ConversationVM.stackptr = savedPtr;
            ConversationVM.TradeThreshold = savedGreed; ConversationVM.TradePatience = savedPatience; ConversationVM.BablTradeBonus = savedBonus;
        }
    }

    public override Task Act(ScenarioContext c) => Task.CompletedTask;

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        bool uw1 = c.Game == "uw1";
        string init = uw1 ? "uw1 BARTER.C:195-203" : "uw2 BARTER.C:156-164";
        var k = new Checks();
        k.Equal("greed, replayed after srand(object index)", _greedExpected, _greed, init);
        k.Equal("patience, replayed after srand(object index)", _patienceExpected, _patience, init);
        k.Equal("npc_assess, replayed after srand(object index)", _assessExpected, _assess, init);
        if (uw1)
        {
            // Object 220, item 0x47, haggle 9, patience 6, shrewd 4. srand(220)'s draws 10651, 18437, 28246 give
            // range(54, -25, 25) = 54 + 54 * -9 / 100 = 50, range(6, -20, 100) = 6 + 6 * 47 / 100 = 8 and
            // range(66, -25, 50) = 66 + 66 * 39 / 100 = 91; with charisma 13, greed 50 - 26 = 24 and patience 8 + 6 = 14.
            k.Equal("whoami of the first NPC that talks", 0x42, _whoami, init);
            k.Equal("greed", 24, _greed, init);
            k.Equal("patience", 14, _patience, init);
            k.Equal("npc_assess", 91, _assess, init);
        }
        else
        {
            // Object 216, item 0x71, haggle 4, patience 7, shrewd 5. srand(216)'s draws 9265, 19299, 17607 give
            // range(24, -25, 25) = 24 + 24 * -11 / 100 = 22, range(7, -20, 100) = 7 + 7 * 50 / 100 = 10 and
            // range(60, -25, 50) = 60 + 60 * 15 / 100 = 69; with charisma 13, greed 22 - 26 = -4 and patience 10 + 6 = 16.
            k.Equal("whoami of the first NPC that talks", 0x8A, _whoami, init);
            k.Equal("greed", -4, _greed, init);
            k.Equal("patience", 16, _patience, init);
            k.Equal("npc_assess", 69, _assess, init);
            string hack = "uw2 BABLHACK.C:116-119";
            k.Equal("greed after babl_hack 8 with 3 at greed 20", 60, _hackGreed, hack);
            k.Equal("babl_hack 8's result", 60, _hackGreedResult, hack);
            k.Equal("patience after babl_hack 8", _hackPatienceBefore, _hackPatience, hack);
            k.Equal("fudge after babl_hack 9 with 200 (a signed char)", -56, _fudge, "uw2 BARTER.C:53, BABLHACK.C:116-117");
            k.Equal("babl_hack 9's result", -56, _fudgeResult, hack);
        }
        return k.Result;
    }
}
