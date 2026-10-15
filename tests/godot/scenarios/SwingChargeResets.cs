using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// A critter's blow uses up its charge. critter_ai, at the end of a melee sequence, sets the
/// critter back to the combat stance and clears its attack charge, SET_ATKFRAME(meptr, 0)
/// (uw1/src/critter/AI.C:1155-1160, uw2/src/critter/AI.C:1131-1134); the charge then winds up
/// again one step per manoeuvre (crit_attack, uw1 AI.C:448-449, uw2 AI.C:438-439) and sets the
/// blow's strength (atk_charge[ATKFRAME], uw1 AI.C:1155, uw2 AI.C:1127). At the end of a cast
/// (UW2: a cast or shot, sequence 6) it also clears the cast slot, SET_CAST(meptr, 0)
/// (uw1 AI.C:1163-1168, uw2 AI.C:1146-1152).
///
/// The scenario makes a hostile critter beside the player, puts it at the last frame of a melee
/// attack with charge 9, and runs the port's AI step for it (npc.NPCInitialProcess, as
/// main.ProcessMobileObjects does) until the attack sequence ends; then the same for the last frame
/// of a cast with cast slot 1. The charge and the cast slot must be 0 afterwards.
///
/// Both games. The port ends both sequences without clearing either (src/npc/npcai.cs:441, 477).
/// </summary>
public sealed class SwingChargeResets : Scenario
{
    public override string Name => "npc-swing-charge-resets";
    public override string Cites => "underworld-exhumed uw1/src/critter/AI.C:1150-1170 (critter_ai); uw2/src/critter/AI.C:1121-1153";

    string _problem;
    uwObject _critter;
    bool _camOk;
    int _meleeSeqAfter, _chargeAfter, _castSeqAfter, _castAfter, _meleeSteps, _castSteps, _castSet;

    public override async Task Setup(ScenarioContext c)
    {
        c.Note(await BatchA.CameraOnPlayer(c));
        int id = BatchA.PickCritter(i => BatchA.Walker(i) && critterObjectDat.isCaster(i));
        if (id < 0) id = BatchA.PickCritter(BatchA.Walker);
        _critter = BatchA.SpawnCritter(id, 0, 0);
        if (_critter == null) { _problem = "no critter could be made"; return; }
        _critter.npc_attitude = 0;
        _critter.npc_goal = 5;
        _critter.npc_gtarg = 1;
        playerdat.play_hp = 200; playerdat.playerObject.npc_hp = 200;   // the blow must not end the game
    }

    /// <summary>Runs AI steps until the critter leaves the sequence; returns the steps taken.</summary>
    static int RunOut(uwObject cr, int seq)
    {
        int steps = 0;
        while (cr.npc_animation == seq && steps < 12)
        {
            npc.NPCInitialProcess(cr);
            steps++;
        }
        return steps;
    }

    public override Task Act(ScenarioContext c)
    {
        if (_problem != null) return Task.CompletedTask;
        _camOk = playerdat.CameraTileX == playerdat.playerObject.tileX && playerdat.CameraTileY == playerdat.playerObject.tileY;
        // A melee blow: the last frame of the first attack, charge 9.
        int attack = npc.ANIMATION_COMBAT_BASH;
        _critter.npc_animation = (short)attack;
        npc.GetCritterAnimationGlobalsForCurrObj(_critter);
        _critter.AnimationFrame = (byte)System.Math.Max(npc.MaxAnimFrame, 0);
        _critter.SwingChargeIndex = 9;
        _meleeSteps = RunOut(_critter, attack);
        _meleeSeqAfter = _critter.npc_animation;
        _chargeAfter = _critter.SwingChargeIndex;

        // A cast: the last frame of the cast sequence, cast slot 1. UW1 casts and ends on frame 4
        // whatever the art holds (uw1 AI.C:1163-1170); UW2 ends on the art's last frame, past the
        // cast frame 3 (uw2 AI.C:1137, 1146).
        int cast = npc.ANIMATION_MAGICATTACK;
        _critter.npc_animation = (short)cast;
        npc.GetCritterAnimationGlobalsForCurrObj(_critter);
        _critter.AnimationFrame = (byte)(c.Game == "uw1" ? 4 : System.Math.Max(npc.MaxAnimFrame, 4));
        _critter.npc_spellindex = 1;
        _castSet = _critter.npc_spellindex;
        _castSteps = RunOut(_critter, cast);
        _castSeqAfter = _critter.npc_animation;
        _castAfter = _critter.npc_spellindex;
        c.Note($"critter 0x{_critter.item_id:X}: melee ended after {_meleeSteps} steps in sequence {_meleeSeqAfter}, charge 9 -> {_chargeAfter}; cast ended after {_castSteps} steps in sequence {_castSeqAfter}, cast slot {_castSet} -> {_castAfter}");
        return Task.CompletedTask;
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        if (!_camOk) return "precondition: the camera's square is not the player's, so the port's AI would not run the critter";
        if (_meleeSeqAfter == npc.ANIMATION_COMBAT_BASH) return "precondition: the melee sequence never ended";
        if (_castSet != 1) return "precondition: the cast slot did not read back 1";
        if (_castSeqAfter == npc.ANIMATION_MAGICATTACK) return "precondition: the cast sequence never ended";
        var k = new Checks();
        k.Equal("attack charge after the blow", 0, _chargeAfter, c.Game == "uw1" ? "uw1 AI.C:1159" : "uw2 AI.C:1133");
        k.Equal("cast slot after the cast", 0, _castAfter, c.Game == "uw1" ? "uw1 AI.C:1168" : "uw2 AI.C:1151");
        return k.Result;
    }
}
