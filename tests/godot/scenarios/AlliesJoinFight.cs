using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// An ally joins the player's fight. In critter_mv (uw1/src/critter/AI.C:1194-1235, uw2/src/critter/AI.C:1168-1227), a critter that does not ignore fights (bA_1) and is the player's ally, within
/// 0x200 game_clock units of the player hitting a critter (crithittime) and within its hearing
/// of that place, turns on the critter the player hit: attitude hostile, b19 bit 0, and unless it
/// is cornered or fleeing, goal 5 (attack) with that critter as its target,
/// critter_set_goal(GOAL_ATTACK, OBJ_ALLY(meptr) ? crithit : 1) (uw1 AI.C:1227-1235).
///
/// The scenario makes an ally and another critter of a different race beside the player, hits the
/// other critter as the player (damage.DamageObject with source 1, which records the hit as the
/// player's blow does, src/interaction/damage.cs:89-99), and runs the ally's AI step,
/// npc.NPCInitialProcess, as main.ProcessMobileObjects does.
///
/// Both games. The port skips the reaction for any ally (src/npc/npcai.cs:607).
/// </summary>
public sealed class AlliesJoinFight : Scenario
{
    public override string Name => "allies-join-fight";
    public override string Cites => "underworld-exhumed uw1/src/critter/AI.C:1194-1235 (critter_mv); uw2/src/critter/AI.C:1168-1227";

    string _problem;
    uwObject _ally, _victim;
    int _goal, _gtarg, _hitIndex, _hitTime, _clock;
    bool _camOk;

    public override async Task Setup(ScenarioContext c)
    {
        c.Note(await BatchA.CameraOnPlayer(c));
        int allyId = BatchA.PickCritter(i => BatchA.Walker(i) && critterObjectDat.noisedetectionrange(i) >= 3);
        int victimId = allyId < 0 ? -1 : BatchA.PickCritter(i => BatchA.Walker(i) && critterObjectDat.faction(i) != critterObjectDat.faction(allyId)
            && critterObjectDat.generaltype(i) != critterObjectDat.generaltype(allyId));
        if (allyId < 0 || victimId < 0) { _problem = $"no creature types to use (ally {allyId}, other {victimId})"; return; }
        _ally = BatchA.SpawnCritter(allyId, 0, 0);
        _victim = BatchA.SpawnCritter(victimId, 1, 0);
        if (_ally == null || _victim == null) { _problem = "no critters could be made"; return; }
        _ally.IsAlly = 1;
        _ally.npc_attitude = 3;
        _ally.npc_goal = 0;
        _ally.UnkBit_0XA_Bit7 = 0;
        _victim.UnkBit_0XA_Bit7 = 0;
        if (_ally.IsAlly != 1) _problem = "the ally bit did not read back set";
    }

    public override Task Act(ScenarioContext c)
    {
        if (_problem != null) return Task.CompletedTask;
        damage.DamageObject(objToDamage: _victim, basedamage: 1, damagetype: 4, objList: UWTileMap.current_tilemap.LevelObjects, WorldObject: true, damagesource: 1);
        _hitIndex = playerdat.LastDamagedNPCIndex;
        _hitTime = playerdat.LastDamagedNPCTime;
        _clock = playerdat.ClockValue;
        _camOk = playerdat.CameraTileX == playerdat.playerObject.tileX && playerdat.CameraTileY == playerdat.playerObject.tileY;
        npc.NPCInitialProcess(_ally);
        _goal = _ally.npc_goal;
        _gtarg = _ally.npc_gtarg;
        c.Note($"ally 0x{_ally.item_id:X} (index {_ally.index}), hit critter 0x{_victim.item_id:X} (index {_victim.index}); after the ally's step: goal {_goal}, target {_gtarg}, attitude {_ally.npc_attitude}");
        return Task.CompletedTask;
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        if (_hitIndex != _victim.index) return $"precondition: the player's hit was not recorded (last hit {_hitIndex}, critter {_victim.index})";
        if (!_camOk) return "precondition: the camera's square is not the player's, so the port's AI would not run the ally";
        var k = new Checks();
        string cite = c.Game == "uw1" ? "uw1 AI.C:1227-1233" : "uw2 AI.C:1218-1225";
        k.Equal("ally's goal after the player hit a critter nearby (5, attack)", 5, _goal, cite);
        k.Equal("ally's target (the critter the player hit)", _victim.index, _gtarg, cite);
        return k.Result;
    }
}
