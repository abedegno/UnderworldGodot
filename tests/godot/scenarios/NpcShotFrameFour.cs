using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// UW1 counts a critter's shot or cast to frame 4 itself, whatever its art holds. critter_ai steps
/// the frame by one each AI step and, on frame 4, casts (SEQ_CAST 0xD, with a cast slot) or fires
/// (SEQ_FIRE 5) and puts the critter back in the combat stance, sequence 0 frame 0
/// (uw1/src/critter/AI.C:1162-1180). The art only decides what is drawn: do_obj passes the sequence
/// and OBJ_FRAME to do_uwcrit (uw1/src/3d/DRAWOBJ.C:211-223), and parse_cpg draws the page's first
/// image for a frame the animation lacks and nothing for a sequence the critter has no animation
/// for (uw1/src/3d/PGCACHE.ASM:906-934); neither writes back into the critter.
///
/// The scenario makes two critters whose art stops short of frame 4 near the player and draws
/// them (ObjectCreator.RenderObject): a caster in the cast sequence with cast slot 1, and a critter
/// with a missile weapon in the fire sequence. It then runs AI steps from frame 0 as
/// main.ProcessMobileObjects does, npc.NPCInitialProcess and then npc.SetAnimSprite, and records
/// the frames, the step on which a missile was launched (motion.RangedAmmoItemID, set before the
/// missile object is made; the caster picked, 0x45, casts a missile spell, so its cast shows there) and the sequence and frame after it. Each must launch on the fifth
/// step, from frame 4, and be in sequence 0 frame 0 after it.
///
/// The port ended the sequence at the art's last frame (MaxAnimFrame, src/npc/npcai.cs) and its
/// renderer set a frame the art lacks back to 0 and a sequence with no art to 0
/// (src/npc/npc.cs:141, 174-178), so neither critter reached frame 4.
/// </summary>
public sealed class NpcShotFrameFour : Scenario
{
    public override string Name => "npc-shot-frame-four";
    public override string[] Games => new[] { "uw1" };
    public override string Cites => "underworld-exhumed uw1/src/critter/AI.C:1162-1180, uw1/src/3d/DRAWOBJ.C:211-223, uw1/src/3d/PGCACHE.ASM:906-934";

    const int SeqFire = 5, SeqCast = 0xD, ShotFrame = 4, Steps = 8;

    string _problem;
    bool _camOk;
    int _castId = -1, _fireId = -1;
    Run _cast, _fire;

    /// <summary>What one critter did over the AI steps.</summary>
    sealed class Run
    {
        public string Frames = "";
        public int LaunchStep = -1, LaunchFrame = -1, Missile = -1, SeqAfter = -1, FrameAfter = -1;
        public bool Drawn;
    }

    /// <summary>Whether the port's art for item id has frame of sequence seq.</summary>
    static bool HasFrame(int id, int seq, int frame) =>
        CritterArt.GetCritter(id & 0x3F).Animations.TryGetValue(CritterArt.GetAnimName(seq, 0), out var a) && a.animIndices[frame] != -1;

    public override async Task Setup(ScenarioContext c)
    {
        c.Note(await BatchA.CameraOnPlayer(c));
        _castId = BatchA.PickCritter(i => BatchA.Walker(i) && critterObjectDat.spell(i, 0) != 0 && critterObjectDat.spell(i, 0) != 0xFF
            && !HasFrame(i, SeqCast, ShotFrame));
        _fireId = BatchA.PickCritter(i => BatchA.Walker(i) && critterObjectDat.WeaponLootTableLookup(i) != 0
            && critterObjectDat.spell(i, 0) == 0 && !HasFrame(i, SeqFire, ShotFrame));
        if (_castId < 0 || _fireId < 0) _problem = "no walking critter whose art lacks the shot frame";
    }

    /// <summary>
    /// Makes a drawn critter of item id in sequence seq, frame 0, with cast slot slot, and runs AI
    /// steps for it, drawing it after each as the game loop does.
    /// </summary>
    Run Steps_(int id, int seq, int slot)
    {
        var r = new Run();
        var critter = BatchA.SpawnCritter(id, 2, 0);
        if (critter == null) { _problem = "no critter could be made"; return r; }
        ObjectCreator.RenderObject(critter, UWTileMap.current_tilemap);
        r.Drawn = critter.instance is npc;
        critter.npc_gtarg = 1;
        critter.npc_animation = (short)seq;
        critter.AnimationFrame = 0;
        critter.npc_spellindex = (short)slot;
        for (int step = 1; step <= Steps && r.LaunchStep < 0; step++)
        {
            int frame = critter.AnimationFrame;
            r.Frames += (r.Frames.Length > 0 ? "," : "") + frame;
            motion.RangedAmmoItemID = -1;
            npc.NPCInitialProcess(critter);
            if (critter.instance is npc n)
                n.SetAnimSprite(animationNo: critter.npc_animation, frameNo: critter.AnimationFrame, relativeHeading: npc.CalculateFacingAngleToNPC(critter));
            if (motion.RangedAmmoItemID != -1)
            {
                r.LaunchStep = step;
                r.LaunchFrame = frame;
                r.Missile = motion.RangedAmmoItemID;
                r.SeqAfter = critter.npc_animation;
                r.FrameAfter = critter.AnimationFrame;
            }
        }
        return r;
    }

    public override Task Act(ScenarioContext c)
    {
        if (_problem != null) return Task.CompletedTask;
        _camOk = playerdat.CameraTileX == playerdat.playerObject.tileX && playerdat.CameraTileY == playerdat.playerObject.tileY;
        _cast = Steps_(_castId, SeqCast, 1);
        _fire = Steps_(_fireId, SeqFire, 0);
        c.Note($"caster 0x{_castId:X} (spell 0x{critterObjectDat.spell(_castId, 0):X}) frames {_cast.Frames}, launched 0x{_cast.Missile:X} on step {_cast.LaunchStep} from frame {_cast.LaunchFrame}, then sequence {_cast.SeqAfter} frame {_cast.FrameAfter}");
        c.Note($"archer 0x{_fireId:X} frames {_fire.Frames}, launched 0x{_fire.Missile:X} on step {_fire.LaunchStep} from frame {_fire.LaunchFrame}, then sequence {_fire.SeqAfter} frame {_fire.FrameAfter}");
        return Task.CompletedTask;
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        if (!_camOk) return "precondition: the camera's square is not the player's, so the port's AI would not run the critter";
        if (!_cast.Drawn || !_fire.Drawn) return "precondition: a critter was not drawn, so the renderer was not exercised";
        var k = new Checks();
        foreach (var (what, r, missile) in new[] {
            ("cast sequence (0xD) with cast slot 1", _cast, -2),
            ("fire sequence (5)", _fire, 0x10 + critterObjectDat.WeaponLootTableLookup(_fireId)) })
        {
            k.Equal($"AI step that launched from the {what} (-1 none)", 5, r.LaunchStep, "uw1 AI.C:1163-1170, 1172-1178");
            k.Equal($"frame launched from in the {what}", ShotFrame, r.LaunchFrame, "uw1 AI.C:1163, 1172");
            if (missile != -2) k.Equal($"missile launched from the {what}", missile, r.Missile, "uw1 AI.C:1173-1175");
            k.Equal($"sequence after launching from the {what}", 0, r.SeqAfter, "uw1 AI.C:1166, 1176");
            k.Equal($"frame after launching from the {what}", 0, r.FrameAfter, "uw1 AI.C:1167, 1177");
        }
        return k.Result;
    }
}
