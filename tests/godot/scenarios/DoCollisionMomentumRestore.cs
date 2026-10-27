using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// UW2's do_zbounce, after a bounce has damped the speed, puts the saved speed back when
///   ((hits0 &amp; 3) == 3 &amp;&amp; (hits0 &amp; 4)) || ((hits1 &amp; 0x40) &amp;&amp; !(hits1 &amp; 0x800))
/// (uw2/src/motion/MOTION.C:558, 600-601). UW1 has no such step (uw1 MOTION.C:515-552).
///
/// The port's DoCollision_seg031_2CFA_D1F (src/physics/motion_collisions.cs) is that routine:
/// UnkC_terrain_base is hits0, UnkE_base is hits1, momentum_14 is CP->speed. The scenario lands
/// a dagger on the floor with nothing hit, vel[2] = -10, speed 100 and bounce factor 10, on
/// terrain whose low bits are not 1, so it takes the bounce path (MOTION.C:514) and the speed is
/// damped to 100 - 5 * 100 / 30 = 84 (MOTION.C:566). Four cases, one for each way the test can
/// go:
///   A: hits0 3, hits1 0:      no restore, 84.
///   B: hits0 7, hits1 0:      restore, 100 (class 3 with bit 2 set).
///   C: hits0 2, hits1 0x40:   restore, 100.
///   D: hits0 2, hits1 0x840:  no restore, 84 (0x800 blocks it).
/// </summary>
public sealed class DoCollisionMomentumRestore : Scenario
{
    const int Dagger = 0x03;
    const int Z = 40;

    public override string Name => "docollision-momentum-restore";
    public override string[] Games => new[] { "uw2" };
    public override string Cites => "underworld-exhumed uw2/src/motion/MOTION.C:514, 556-602 (do_zbounce)";

    string _problem;
    uwObject _obj;
    readonly int[] _speed = { -1, -1, -1, -1 };

    public override Task Setup(ScenarioContext c)
    {
        var p = playerdat.playerObject;
        _obj = ObjectCreator.spawnObjectInTile(Dagger, p.tileX, p.tileY, 3, 3, (short)(UWTileMap.current_tilemap.Tiles[p.tileX, p.tileY].floorHeight << 3), RenderImmediately: false);
        if (_obj == null) _problem = "no dagger could be made";
        return Task.CompletedTask;
    }

    short Land(int hits0, int hits1)
    {
        var mp = new UWMotionParamArray();
        mp.index_20 = _obj.index;
        mp.x_0 = (short)(_obj.tileX << 8);
        mp.y_2 = (short)(_obj.tileY << 8);
        mp.unk_a_pitch = -10;
        mp.momentum_14 = 100;
        mp.unk_16_relatedtoPitch = 10;
        mp.tilestate25 = 0x10;   // in the air, as ProcessCollisions leaves it before calling
        MotionCalcArray.MotionArrayObjectIndexA_base = (short)_obj.index;
        MotionCalcArray.UnkC_terrain_base = (short)hits0;
        MotionCalcArray.UnkE_base = (short)hits1;
        MotionCalcArray.Unk10_base = Z;        // the floor
        MotionCalcArray.Radius8_base = 1;
        MotionCalcArray.z4_base = Z;
        UWMotionParamArray.Gravity_Related_dseg_67d6_41F = 0;   // MP.zspeed <= 4: no partial-step time
        UWMotionParamArray.CollisionZposHeightRelated_dseg_67d6_419 = Z; // MP.targz, on the floor
        UWMotionParamArray.ACollisionIndex_dseg_67d6_416 = -1;  // MP.hit == -1
        motion.DoCollision_seg031_2CFA_D1F(mp, MotionHandler.ObjectMotionHandler);
        return mp.momentum_14;
    }

    public override Task Act(ScenarioContext c)
    {
        if (_problem != null) return Task.CompletedTask;
        _speed[0] = Land(3, 0);
        _speed[1] = Land(7, 0);
        _speed[2] = Land(2, 0x40);
        _speed[3] = Land(2, 0x840);
        c.Note($"speed after the bounce: A {_speed[0]}, B {_speed[1]}, C {_speed[2]}, D {_speed[3]}");
        return Task.CompletedTask;
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        var k = new Checks();
        k.Equal("A: speed, hits0 3, hits1 0", 84, _speed[0], "damped, bit 2 of hits0 clear and no 0x40: uw2 MOTION.C:566, 600");
        k.Equal("B: speed, hits0 7, hits1 0", 100, _speed[1], "restored, (hits0 & 3) == 3 and hits0 & 4: uw2 MOTION.C:600-601");
        k.Equal("C: speed, hits0 2, hits1 0x40", 100, _speed[2], "restored, hits1 & 0x40 without 0x800: uw2 MOTION.C:600-601");
        k.Equal("D: speed, hits0 2, hits1 0x840", 84, _speed[3], "damped, 0x800 blocks the restore: uw2 MOTION.C:566, 600");
        return k.Result;
    }
}
