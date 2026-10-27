using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// Which sequence a critter shoots or casts in. UW1's critter_ai keeps them apart: SEQ_FIRE (5)
/// always fires the critter's missile on frame 4, and SEQ_CAST (0xD) casts only while a cast slot
/// is set, `OBJ_SEQ(meptr) == SEQ_CAST &amp;&amp; OBJ_CAST(meptr)`; with no slot it falls through to
/// critter_mv like any other sequence (uw1/src/critter/AI.C:1162-1180, critter.h:128-131). UW2 has
/// one sequence, 6, for both, and on frame 3 casts if the slot is set, else fires
/// (uw2/src/critter/AI.C:1136-1145).
///
/// The scenario makes a critter near the player, puts it on the shot frame of each case and runs
/// one AI step for it (npc.NPCInitialProcess, as main.ProcessMobileObjects does), watching for a
/// missile launched, item 0x10 + the critter's missile type (mycst->arms[0].item &amp; ID_INCLASS,
/// the port's WeaponLootTableLookup). UW1: a caster in the cast sequence with slot 0 launches
/// nothing; a critter in the fire sequence with slot 1 fires its missile. UW2: sequence 6 with
/// slot 0 fires the missile.
///
/// The port's UW1 takes both 5 and 0xD into its ranged branch and chooses by the slot
/// (src/npc/npcai.cs:452, 456), so it fires a missile from the cast sequence and casts from the
/// fire sequence.
/// </summary>
public sealed class NpcCastNeedsSlot : Scenario
{
    public override string Name => "npc-cast-needs-slot";
    public override string Cites => "underworld-exhumed uw1/src/critter/AI.C:1162-1180, uw1/src/include/critter.h:128-131; uw2/src/critter/AI.C:1136-1145";

    string _problem;
    bool _camOk;
    int _castId = -1, _fireId = -1;
    // the missile launched in each step, -1 for none
    int _castNoSlot = -1, _fireWithSlot = -1;

    bool Uw1(ScenarioContext c) => c.Game == "uw1";
    int FireSeq(ScenarioContext c) => Uw1(c) ? 5 : 6;
    int CastSeq(ScenarioContext c) => Uw1(c) ? 0xD : 6;
    int ShotFrame(ScenarioContext c) => Uw1(c) ? 4 : 3;

    /// <summary>
    /// Whether the port has art for frame of sequence seq. The port's UW2 renderer puts a critter
    /// whose art lacks the frame back to frame 0 (src/npc/npc.cs, ApplyCritterAnimation), and then
    /// it would never reach the shot frame.
    /// </summary>
    static bool HasFrame(int id, int seq, int frame) =>
        CritterArt.GetCritter(id & 0x3F).Animations.TryGetValue(CritterArt.GetAnimName(seq, 0), out var a) && a.animIndices[frame] != -1;

    public override async Task Setup(ScenarioContext c)
    {
        c.Note(await BatchA.CameraOnPlayer(c));
        _castId = BatchA.PickCritter(i => BatchA.Walker(i) && critterObjectDat.spell(i, 0) != 0 && critterObjectDat.spell(i, 0) != 0xFF
            && HasFrame(i, CastSeq(c), ShotFrame(c)));
        _fireId = BatchA.PickCritter(i => BatchA.Walker(i) && critterObjectDat.WeaponLootTableLookup(i) != 0
            && HasFrame(i, FireSeq(c), ShotFrame(c)));
        if (_fireId < 0) _fireId = BatchA.PickCritter(i => BatchA.Walker(i) && HasFrame(i, FireSeq(c), ShotFrame(c)));
        if (_castId < 0 || _fireId < 0) _problem = "no walking critter with art for the shot frame";
    }

    /// <summary>
    /// One AI step for a new critter of item id from the shot frame of seq with cast slot slot; the
    /// missile item the port launched (motion.RangedAmmoItemID, which NPCMissileLaunch and missile
    /// spells set before making the object), or -1 for none.
    /// </summary>
    int Step(ScenarioContext c, int id, int seq, int slot)
    {
        var critter = BatchA.SpawnCritter(id, 2, 0);
        if (critter == null) { _problem = "no critter could be made"; return -1; }
        critter.npc_gtarg = 1;
        critter.npc_animation = (short)seq;
        critter.AnimationFrame = (byte)ShotFrame(c);
        critter.npc_spellindex = (short)slot;
        motion.RangedAmmoItemID = -1;
        npc.NPCInitialProcess(critter);
        return motion.RangedAmmoItemID;
    }

    public override Task Act(ScenarioContext c)
    {
        if (_problem != null) return Task.CompletedTask;
        _camOk = playerdat.CameraTileX == playerdat.playerObject.tileX && playerdat.CameraTileY == playerdat.playerObject.tileY;
        _castNoSlot = Step(c, _castId, CastSeq(c), 0);
        if (Uw1(c)) _fireWithSlot = Step(c, _fireId, FireSeq(c), 1);
        c.Note($"cast critter 0x{_castId:X} (missile 0x{0x10 + critterObjectDat.WeaponLootTableLookup(_castId):X}) in sequence {CastSeq(c)} with no slot launched 0x{_castNoSlot:X}"
            + (Uw1(c) ? $"; fire critter 0x{_fireId:X} (missile 0x{0x10 + critterObjectDat.WeaponLootTableLookup(_fireId):X}) in sequence 5 with slot 1 launched 0x{_fireWithSlot:X}" : ""));
        return Task.CompletedTask;
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        if (!_camOk) return "precondition: the camera's square is not the player's, so the port's AI would not run the critter";
        var k = new Checks();
        if (Uw1(c))
        {
            k.Equal("missile launched from the cast sequence (0xD) with no cast slot (-1 none)", -1, _castNoSlot, "uw1 AI.C:1162, 1179-1180");
            k.Equal("missile launched from the fire sequence (5) with cast slot 1", 0x10 + critterObjectDat.WeaponLootTableLookup(_fireId), _fireWithSlot, "uw1 AI.C:1171-1174");
        }
        else
            k.Equal("missile launched from sequence 6 with no cast slot", 0x10 + critterObjectDat.WeaponLootTableLookup(_castId), _castNoSlot, "uw2 AI.C:1142-1144");
        return k.Result;
    }
}
