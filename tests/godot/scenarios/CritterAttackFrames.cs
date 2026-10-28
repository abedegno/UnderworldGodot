using System;
using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// Two things about a critter fighting in melee.
///
/// UW2 steps a critter's frame by the length of its sequence, seq_len, the frame count from CR.AN
/// (uw2/src/critter/AI.C:1006-1013): crit_attack does (frame + 1) % seq_len when the sequence does
/// not change (uw2 AI.C:418-423), and so does the drifting goal 11 (uw2 AI.C:1301). UW1 steps
/// these by (frame + 1) % 4 whatever the art (uw1 AI.C:455, 1315). The port's npc.MaxAnimFrame is
/// the art's last frame, one less than the count (CritterAnimation, src/loaders/critterloader.cs),
/// and it stepped by % MaxAnimFrame, so the last frame never showed and a one-frame sequence
/// divided by zero.
///
/// UW1's crit_offense, after crit_attack, goes on to the give-up test and
/// crit_offense_find_target (uw1 AI.C:362-385), which for a critter on another square more than
/// 8 fine units from its target (how 1, 64 &lt; tdisttsqr) heads for the target's square
/// (uw1 AI.C:489-494), and crit_head_for_loc first sets the critter's destination to it
/// (uw1 PATHFIND.C:1120). UW2 returns after crit_attack (uw2 AI.C:349-352). The port returned in
/// both games (npc.NPC_Goal5_Attack).
///
/// The scenario calls the port's crit_attack, ChooseMeleeAttackToMake, out of reach (dist 0x120,
/// so the walking sequence and no attack) on a UW2 walker in its walking sequence, with MaxAnimFrame
/// as GetCritterAnimationGlobalsForCurrObj sets it from the art, from the frame before the last and
/// from the last; then with MaxAnimFrame 0, as a one-frame sequence leaves it. It runs one AI step
/// (npc.NPCInitialProcess) of a critter on goal 11 in each game from two frames. Last it puts a
/// hostile non-caster on goal 5 on the square beside the player, 9 fine units away on the same
/// height (dist 81, in melee reach), with its destination its own square, sets the AI's globals as
/// NPCInitialProcess and GetDistancesToGTarg do, calls NPC_Goal5_Attack and records the
/// destination.
/// </summary>
public sealed class CritterAttackFrames : Scenario
{
    public override string Name => "critter-attack-frames";
    public override string Cites => "underworld-exhumed uw2/src/critter/AI.C:418-423, 1006-1013, 1301 (seq_len), uw1 AI.C:340-385, 455, 464-500, 1315, uw1 PATHFIND.C:1113-1120";

    const int Uw2Walk = 1, Uw1Walk = 0x2C, Flutter = 0xB, Attack = 5;
    const int DivideByZero = -2;

    string _problem;
    bool _camOk;
    int _id = -1, _last = -1;
    int _fromBeforeLast = -1, _fromLast = -1, _fromOnly = -1;
    int _flutterLast = -1, _flutterA = -1, _flutterB = -1, _flutterFromA = -1, _flutterFromB = -1;
    int _offId = -1, _dist = -1, _destX = -1, _destY = -1, _ownX = -1, _ownY = -1, _playerX = -1, _playerY = -1, _goalAfter = -1;

    static int LastFrame(int id, int seq) =>
        CritterArt.GetCritter(id & 0x3F).Animations.TryGetValue(CritterArt.GetAnimName(seq, 0), out var a) ? a.maxNoOfFrames : -1;

    static bool Uw2 => UWClass._RES == UWClass.GAME_UW2;

    public override async Task Setup(ScenarioContext c)
    {
        c.Note(await BatchA.CameraOnPlayer(c));
        int walk = Uw2 ? Uw2Walk : Uw1Walk;
        _id = BatchA.PickCritter(i => BatchA.Walker(i) && LastFrame(i, walk) >= 2);
        _offId = BatchA.PickCritter(i => BatchA.Walker(i) && !critterObjectDat.isCaster(i));
        if (_id < 0) _problem = "no walking critter with walking art of three frames or more";
        else if (_offId < 0) _problem = "no walking critter that is not a caster";
        playerdat.play_hp = 200; playerdat.playerObject.npc_hp = 200;
    }

    /// <summary>The port's crit_attack out of reach from frame frame of the walking sequence, with MaxAnimFrame last; the frame it leaves, DivideByZero if it threw that.</summary>
    static int Manoeuvre(uwObject critter, int frame, int last)
    {
        var t = typeof(npc);
        critter.npc_animation = Uw2Walk;
        critter.AnimationFrame = (byte)frame;
        BatchA.Set(t, "currentGTargXVector", 0xC);
        BatchA.Set(t, "currentGTargYVector", 0xC);
        BatchA.Set(t, "currentGoalTarget", playerdat.playerObject);
        npc.MaxAnimFrame = last;
        try { BatchA.Call(t, "ChooseMeleeAttackToMake", critter, 0x120); }
        catch (DivideByZeroException) { return DivideByZero; }
        return critter.npc_animation == Uw2Walk ? critter.AnimationFrame : -1;
    }

    /// <summary>One AI step of critter on goal 11 from frame frame of its walking sequence; the frame it leaves.</summary>
    int FlutterStep(uwObject critter, int frame)
    {
        critter.npc_goal = Flutter;
        critter.npc_animation = (short)(Uw2 ? Uw2Walk : Uw1Walk);
        critter.AnimationFrame = (byte)frame;
        try { npc.NPCInitialProcess(critter); }
        catch (DivideByZeroException) { return DivideByZero; }
        _flutterLast = npc.MaxAnimFrame;
        return critter.AnimationFrame;
    }

    void Offense()
    {
        var p = playerdat.playerObject;
        int dx = p.xpos <= 6 ? 1 : -1;
        var critter = BatchA.SpawnCritter(_offId, dx, 0, xpos: (short)(p.xpos + dx), ypos: (short)p.ypos);
        if (critter == null) { _problem = "no critter could be made"; return; }
        if (UWTileMap.current_tilemap.Tiles[critter.tileX, critter.tileY].floorHeight != UWTileMap.current_tilemap.Tiles[p.tileX, p.tileY].floorHeight)
        { _problem = "the square beside the player is at another height"; return; }
        critter.npc_attitude = 0;
        critter.npc_goal = Attack;
        critter.npc_gtarg = 1;
        critter.npc_animation = 0;
        critter.AnimationFrame = 0;
        critter.TargetTileX = (short)critter.tileX;
        critter.TargetTileY = (short)critter.tileY;
        _ownX = critter.TargetTileX; _ownY = critter.TargetTileY;
        _playerX = p.npc_xhome; _playerY = p.npc_yhome;
        var t = typeof(npc);
        // as NPCInitialProcess sets them after the critter's motion
        BatchA.Set(t, "IsNPCActive_dseg_67d6_2234", true);
        BatchA.Set(t, "currObj_XHome", (int)critter.npc_xhome);
        BatchA.Set(t, "currObj_YHome", (int)critter.npc_yhome);
        BatchA.Set(t, "currObj_FloorHeight", critter.zpos >> 3);
        BatchA.Set(t, "currObjXCoordinate", (critter.npc_xhome << 3) + critter.xpos);
        BatchA.Set(t, "currObjYCoordinate", (critter.npc_yhome << 3) + critter.ypos);
        BatchA.Set(t, "currObjQualityX", (int)critter.quality);
        BatchA.Set(t, "currObjOwnerY", (int)critter.owner);
        if (!npc.GetDistancesToGTarg(critter)) { _problem = "the player is dead"; return; }
        int vx = BatchA.Get<int>(t, "currentGTargXVector"), vy = BatchA.Get<int>(t, "currentGTargYVector");
        _dist = vx * vx + vy * vy;
        BatchA.Call(t, "NPC_Goal5_Attack", critter);
        _destX = critter.TargetTileX; _destY = critter.TargetTileY;
        _goalAfter = critter.npc_goal;
    }

    public override Task Act(ScenarioContext c)
    {
        if (_problem != null) return Task.CompletedTask;
        _camOk = playerdat.CameraTileX == playerdat.playerObject.tileX && playerdat.CameraTileY == playerdat.playerObject.tileY;
        if (Uw2)
        {
            var critter = BatchA.SpawnCritter(_id, 1, 0);
            if (critter == null) { _problem = "no critter could be made"; return Task.CompletedTask; }
            critter.npc_animation = Uw2Walk;
            npc.GetCritterAnimationGlobalsForCurrObj(critter);
            _last = npc.MaxAnimFrame;
            _fromBeforeLast = Manoeuvre(critter, _last - 1, _last);
            _fromLast = Manoeuvre(critter, _last, _last);
            _fromOnly = Manoeuvre(critter, 0, 0);
            c.Note($"crit_attack 0x{_id:X}, walking art to frame {_last}: from frame {_last - 1} to {_fromBeforeLast}, from {_last} to {_fromLast}; one-frame sequence from 0 to {_fromOnly} ({DivideByZero} divided by zero)");
        }
        var flutter = BatchA.SpawnCritter(_id, 0, 1);
        if (flutter == null) { _problem = "no critter could be made"; return Task.CompletedTask; }
        _flutterFromA = Uw2 ? LastFrame(_id, Uw2Walk) - 1 : 2;
        _flutterA = FlutterStep(flutter, _flutterFromA);
        _flutterFromB = Uw2 ? LastFrame(_id, Uw2Walk) : 3;
        _flutterB = FlutterStep(flutter, _flutterFromB);
        c.Note($"goal 11 0x{_id:X} (art's last frame {_flutterLast}): from frame {_flutterFromA} to {_flutterA}, from {_flutterFromB} to {_flutterB}");
        Offense();
        if (_problem != null) return Task.CompletedTask;
        c.Note($"crit_offense 0x{_offId:X} at dist {_dist}: destination ({_ownX}, {_ownY}) became ({_destX}, {_destY}), player at ({_playerX}, {_playerY}), goal after {_goalAfter}");
        return Task.CompletedTask;
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        if (!_camOk) return "precondition: the camera's square is not the player's, so the port's AI would not run the critter";
        if (_dist <= 64 || _dist >= 0x64) return $"precondition: the critter is at dist {_dist}, not in reach and more than 8 fine units away";
        var k = new Checks();
        if (Uw2)
        {
            if (_last != LastFrame(_id, Uw2Walk)) return $"precondition: MaxAnimFrame {_last} is not the walking art's last frame {LastFrame(_id, Uw2Walk)}";
            k.Equal($"crit_attack's frame after frame {_last - 1} of {_last + 1}", _last, _fromBeforeLast, "uw2 AI.C:423");
            k.Equal($"crit_attack's frame after the last frame, {_last}", 0, _fromLast, "uw2 AI.C:423");
            k.Equal("crit_attack's frame in a one-frame sequence", 0, _fromOnly, "uw2 AI.C:423");
            int len = LastFrame(_id, Uw2Walk) + 1;
            k.Equal($"goal 11's frame after frame {_flutterFromA}", (_flutterFromA + 1) % len, _flutterA, "uw2 AI.C:1301");
            k.Equal($"goal 11's frame after frame {_flutterFromB}", (_flutterFromB + 1) % len, _flutterB, "uw2 AI.C:1301");
            k.Equal("destination x after crit_attack (returns)", _ownX, _destX, "uw2 AI.C:349-352");
            k.Equal("destination y after crit_attack (returns)", _ownY, _destY, "uw2 AI.C:349-352");
        }
        else
        {
            k.Equal("goal 11's frame after frame 2", 3, _flutterA, "uw1 AI.C:1315");
            k.Equal("goal 11's frame after frame 3", 0, _flutterB, "uw1 AI.C:1315");
            k.Equal("destination x after crit_attack (heads for the target's square)", _playerX, _destX, "uw1 AI.C:385, 489-494, PATHFIND.C:1120");
            k.Equal("destination y after crit_attack (heads for the target's square)", _playerY, _destY, "uw1 AI.C:385, 489-494, PATHFIND.C:1120");
        }
        return k.Result;
    }
}
