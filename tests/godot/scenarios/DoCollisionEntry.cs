using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// How an object reaching the floor decides between settling and the collision (bounce) path
/// (issue #117). The original's do_zbounce settles it (stop_me, terrain FOOT_WATER, a thud)
/// when nothing was hit (MP.hit == -1), the floor plus radius reaches the new z, and:
///   UW1: (1 &amp; Ppd.hits0) and CP->vel[2] &lt; 0 (uw1/src/motion/MOTION.C:505-511);
///   UW2: (3 &amp; Ppd.hits0) == 1 and CP->vel[2] &lt;= 0 (uw2/src/motion/MOTION.C:514-527).
/// Otherwise it goes on to the collision path, which bounces the object and, once it is slow,
/// sets the terrain byte to 1 &lt;&lt; (hits0 &amp; 3) (uw1 MOTION.C:552).
///
/// The port's DoCollision_seg031_2CFA_D1F (src/physics/motion_collisions.cs) is that routine:
/// UnkC_terrain_base is hits0, unk_a_pitch is vel[2], tilestate25 is the terrain byte. The
/// scenario calls it for an object on the floor with nothing hit, twice per game:
///   A: terrain class 3, vel[2] = -10. UW1 settles (terrain 2); UW2 bounces (terrain 8).
///   B: terrain class 1, vel[2] = 0, momentum 100, elasticity 10. UW2 settles (momentum 0);
///      UW1 bounces, which damps the momentum to 100 - 100 * 5 / 30 = 84 and leaves it moving
///      (uw1 MOTION.C:515-552).
/// </summary>
public sealed class DoCollisionEntry : Scenario
{
    const int Dagger = 0x03;
    const int Z = 40;

    public override string Name => "docollision-entry";
    public override string Cites => "underworld-exhumed uw1/src/motion/MOTION.C:490-552, uw2/src/motion/MOTION.C:497-527 (do_zbounce)";

    string _problem;
    uwObject _obj;
    int _aTerrain = -1, _bMomentum = -1, _bTerrain = -1;

    public override Task Setup(ScenarioContext c)
    {
        var p = playerdat.playerObject;
        _obj = ObjectCreator.spawnObjectInTile(Dagger, p.tileX, p.tileY, 3, 3, (short)(UWTileMap.current_tilemap.Tiles[p.tileX, p.tileY].floorHeight << 3), RenderImmediately: false);
        if (_obj == null) _problem = "no dagger could be made";
        return Task.CompletedTask;
    }

    UWMotionParamArray Land(int terrain, short pitch, short momentum, byte elasticity)
    {
        var mp = new UWMotionParamArray();
        mp.index_20 = _obj.index;
        mp.x_0 = (short)(_obj.tileX << 8);
        mp.y_2 = (short)(_obj.tileY << 8);
        mp.unk_a_pitch = pitch;
        mp.momentum_14 = momentum;
        mp.unk_16_relatedtoPitch = elasticity;
        mp.tilestate25 = 0x10;   // in the air, as ProcessCollisions leaves it before calling
        MotionCalcArray.MotionArrayObjectIndexA_base = (short)_obj.index;
        MotionCalcArray.UnkC_terrain_base = (short)terrain;
        MotionCalcArray.Unk10_base = Z;        // the floor
        MotionCalcArray.Radius8_base = 1;
        MotionCalcArray.UnkE_base = 0;
        MotionCalcArray.z4_base = Z;
        UWMotionParamArray.Gravity_Related_dseg_67d6_41F = 0;   // MP.zspeed <= 4: no partial-step time
        UWMotionParamArray.CollisionZposHeightRelated_dseg_67d6_419 = Z; // MP.targz, on the floor
        UWMotionParamArray.ACollisionIndex_dseg_67d6_416 = -1;  // MP.hit == -1
        motion.DoCollision_seg031_2CFA_D1F(mp, MotionHandler.ObjectMotionHandler);
        return mp;
    }

    public override Task Act(ScenarioContext c)
    {
        if (_problem != null) return Task.CompletedTask;
        var a = Land(terrain: 3, pitch: -10, momentum: 0, elasticity: 10);
        _aTerrain = a.tilestate25;
        var b = Land(terrain: 1, pitch: 0, momentum: 100, elasticity: 10);
        _bMomentum = b.momentum_14;
        _bTerrain = b.tilestate25;
        c.Note($"A (terrain 3, vel -10): terrain byte {_aTerrain}; B (terrain 1, vel 0, momentum 100): momentum {_bMomentum}, terrain byte {_bTerrain}");
        return Task.CompletedTask;
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        var k = new Checks();
        if (c.Game == "uw1")
        {
            k.Equal("A: terrain byte after landing on class 3 with vel -10", 2, _aTerrain, "settles, (1 & hits0) && vel < 0, terrain FOOT_WATER: uw1 MOTION.C:505-508");
            k.Equal("B: momentum after landing on class 1 with vel 0", 84, _bMomentum, "vel 0 is not < 0, so it bounces and damps: uw1 MOTION.C:505-506, 515-552");
        }
        else
        {
            k.Equal("A: terrain byte after landing on class 3 with vel -10", 8, _aTerrain, "(3 & hits0) != 1, so it bounces and sets 1 << 3: uw2 MOTION.C:514");
            k.Equal("B: momentum after landing on class 1 with vel 0", 0, _bMomentum, "settles on vel <= 0, stop_me: uw2 MOTION.C:514-518");
        }
        k.Equal("B: terrain byte", 2, _bTerrain, "terrain 2 either way (stop_me's FOOT_WATER, or 1 << 1)");
        return k.Result;
    }
}
