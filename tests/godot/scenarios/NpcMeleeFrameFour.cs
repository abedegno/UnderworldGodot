using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// UW1 counts a critter's melee blow and its dying itself, whatever its art holds. critter_ai steps
/// the frame by one each AI step; in an attack sequence (1 to 3) it strikes on frame 4 and puts the
/// critter back in the combat stance, sequence 0 frame 0 (uw1/src/critter/AI.C:1148-1161), and in
/// the dying sequence (0xC) it removes the critter on frame 3 (uw1 AI.C:1135-1147). crit_attack, the
/// melee manoeuvre, sets the sequence without touching the frame and then steps the frame,
/// (frame + 1) % 4, unless it starts an attack, which sets frame 0 and returns
/// (uw1 AI.C:407-458). UW2 ends these on the art's last frame, seq_lframe (uw2 AI.C:1107-1135), and
/// steps crit_attack's frame by the art's length (uw2 AI.C:418-422), so this is UW1 only.
///
/// The scenario makes a hostile critter beside the player whose first attack's art stops short of
/// frame 4 and runs AI steps (npc.NPCInitialProcess, then SetAnimSprite, as
/// main.ProcessMobileObjects does) from frame 0 of that attack, recording the frames, the step on
/// which it strikes (combat's AttackingCharacter, set first thing by combat.NPCExecuteAttack) and the
/// sequence and frame after. It then puts a critter in the dying sequence at frame 0 and records the
/// step on which the AI removes it (NPCInitialProcess returns false). Every UW1 critter with dying
/// art has it to frame 3, so the critter is one with no art at all (0x4F, 0x7D and 0x7E have none),
/// for which the port's last frame is whatever the critter run before it left; the scenario leaves
/// it at 1. Last it calls the port's crit_attack, ChooseMeleeAttackToMake, out of reach
/// (dist 0x120, so no attack and no random draw) with the art's last frame set to 2, from frame 2
/// of the combat stance and from frame 3 of walking, and records the frame it leaves. The sequence
/// crit_attack chooses is critter-melee-choice's business.
///
/// The port ended the attack and the dying on the art's last frame (MaxAnimFrame) and struck on it
/// when the art was short (src/npc/npcai.cs, NPCInitialProcess), and its crit_attack used UW2's
/// frame logic, frame 0 on a new sequence and else a step by the art's last frame
/// (ChooseMeleeAttackToMake).
/// </summary>
public sealed class NpcMeleeFrameFour : Scenario
{
    public override string Name => "npc-melee-frame-four";
    public override string[] Games => new[] { "uw1" };
    public override string Cites => "underworld-exhumed uw1/src/critter/AI.C:1135-1161 (critter_ai), uw1 AI.C:395-458 (crit_attack), uw1/src/include/critter.h:124-133";

    const int SeqAttack1 = 1, SeqDying = 0xC, SeqWalk = 0x2C, StrikeFrame = 4, Steps = 10;

    /// <summary>The art's last frame another critter run before this one could leave in npc.MaxAnimFrame.</summary>
    const int StaleLastFrame = 1;

    string _problem;
    bool _camOk, _meleeDrawn;
    int _meleeId = -1, _dyingId = -1, _meleeArt;
    string _meleeFrames = "", _dyingFrames = "";
    int _strikeStep = -1, _strikeFrame = -1, _seqAfter = -1, _frameAfter = -1, _goneStep = -1, _goneFrame = -1;
    int _walkSeq = -1, _walkFrame = -1, _stepSeq = -1, _stepFrame = -1;

    /// <summary>The last frame of the port's art for sequence seq of item id, -1 if none.</summary>
    static int LastFrame(int id, int seq) =>
        CritterArt.GetCritter(id & 0x3F).Animations.TryGetValue(CritterArt.GetAnimName(seq, 0), out var a) ? a.maxNoOfFrames : -1;

    public override async Task Setup(ScenarioContext c)
    {
        c.Note(await BatchA.CameraOnPlayer(c));
        _meleeId = BatchA.PickCritter(i => BatchA.Walker(i) && LastFrame(i, SeqAttack1) >= 0 && LastFrame(i, SeqAttack1) < StrikeFrame);
        _dyingId = BatchA.PickCritter(i => LastFrame(i, SeqDying) < 0);
        if (_meleeId < 0) _problem = "no walking critter whose first attack's art stops short of frame 4";
        else if (_dyingId < 0) _problem = "no critter without dying art";
        else
        {
            _meleeArt = LastFrame(_meleeId, SeqAttack1);
        }
        playerdat.play_hp = 200; playerdat.playerObject.npc_hp = 200;   // the blows must not end the game
    }

    void Melee()
    {
        var critter = BatchA.SpawnCritter(_meleeId, 0, 0);
        if (critter == null) { _problem = "no critter could be made"; return; }
        ObjectCreator.RenderObject(critter, UWTileMap.current_tilemap);
        _meleeDrawn = critter.instance is npc;
        critter.npc_attitude = 0;
        critter.npc_goal = 5;
        critter.npc_gtarg = 1;
        critter.npc_animation = SeqAttack1;
        critter.AnimationFrame = 0;
        for (int step = 1; step <= Steps && _strikeStep < 0; step++)
        {
            int frame = critter.AnimationFrame;
            _meleeFrames += (_meleeFrames.Length > 0 ? "," : "") + frame;
            BatchA.Set(typeof(combat), "AttackingCharacter", null);
            npc.NPCInitialProcess(critter);
            if (critter.instance is npc n)
                n.SetAnimSprite(animationNo: critter.npc_animation, frameNo: critter.AnimationFrame, relativeHeading: npc.CalculateFacingAngleToNPC(critter));
            if (BatchA.Get<uwObject>(typeof(combat), "AttackingCharacter") == critter)
            {
                _strikeStep = step;
                _strikeFrame = frame;
                _seqAfter = critter.npc_animation;
                _frameAfter = critter.AnimationFrame;
            }
        }
    }

    void Dying()
    {
        var critter = BatchA.SpawnCritter(_dyingId, 0, 1);
        if (critter == null) { _problem = "no critter could be made"; return; }
        critter.npc_hp = 0;
        critter.npc_animation = SeqDying;
        critter.AnimationFrame = 0;
        for (int step = 1; step <= Steps; step++)
        {
            npc.MaxAnimFrame = StaleLastFrame;
            int frame = critter.AnimationFrame;
            _dyingFrames += (_dyingFrames.Length > 0 ? "," : "") + frame;
            if (!npc.NPCInitialProcess(critter))
            {
                _goneStep = step;
                _goneFrame = frame;
                break;
            }
        }
    }

    /// <summary>The port's crit_attack out of reach from sequence seq, frame frame; the sequence and frame it leaves.</summary>
    static (int seq, int frame) Manoeuvre(uwObject critter, int seq, int frame)
    {
        var t = typeof(npc);
        critter.npc_animation = (short)seq;
        critter.AnimationFrame = (byte)frame;
        BatchA.Set(t, "currentGTargXVector", 3);
        BatchA.Set(t, "currentGTargYVector", 2);
        BatchA.Set(t, "currentGoalTarget", playerdat.playerObject);
        npc.MaxAnimFrame = 2;
        BatchA.Call(t, "ChooseMeleeAttackToMake", critter, 0x120);
        return (critter.npc_animation, critter.AnimationFrame);
    }

    public override Task Act(ScenarioContext c)
    {
        if (_problem != null) return Task.CompletedTask;
        _camOk = playerdat.CameraTileX == playerdat.playerObject.tileX && playerdat.CameraTileY == playerdat.playerObject.tileY;
        Melee();
        if (_problem != null) return Task.CompletedTask;
        Dying();
        if (_problem != null) return Task.CompletedTask;
        var walker = BatchA.SpawnCritter(_meleeId, 1, 0);
        if (walker == null) { _problem = "no critter could be made"; return Task.CompletedTask; }
        (_walkSeq, _walkFrame) = Manoeuvre(walker, 0, 2);
        (_stepSeq, _stepFrame) = Manoeuvre(walker, SeqWalk, 3);
        c.Note($"melee 0x{_meleeId:X} (attack art to frame {_meleeArt}) frames {_meleeFrames}, struck on step {_strikeStep} from frame {_strikeFrame}, then sequence {_seqAfter} frame {_frameAfter}");
        c.Note($"dying 0x{_dyingId:X} (no dying art, last frame left at {StaleLastFrame}) frames {_dyingFrames}, removed on step {_goneStep} from frame {_goneFrame}");
        c.Note($"crit_attack out of reach: from sequence 0 frame 2 to sequence {_walkSeq} frame {_walkFrame}; from sequence 0x2C frame 3 to sequence {_stepSeq} frame {_stepFrame}");
        return Task.CompletedTask;
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        if (!_camOk) return "precondition: the camera's square is not the player's, so the port's AI would not run the critter";
        if (!_meleeDrawn) return "precondition: the critter was not drawn, so the renderer was not exercised";
        var k = new Checks();
        k.Equal("AI step that struck from the first attack (-1 none)", 5, _strikeStep, "uw1 AI.C:1154-1161");
        k.Equal("frame struck from", StrikeFrame, _strikeFrame, "uw1 AI.C:1154");
        k.Equal("sequence after the blow", 0, _seqAfter, "uw1 AI.C:1157");
        k.Equal("frame after the blow", 0, _frameAfter, "uw1 AI.C:1158");
        k.Equal("AI step that removed the dying critter (-1 none)", 4, _goneStep, "uw1 AI.C:1135-1147");
        k.Equal("frame the dying critter was removed from", 3, _goneFrame, "uw1 AI.C:1136");
        k.Equal("crit_attack's frame after frame 2", 3, _walkFrame, "uw1 AI.C:455");
        k.Equal("crit_attack's frame after frame 3", 0, _stepFrame, "uw1 AI.C:455");
        return k.Result;
    }
}
