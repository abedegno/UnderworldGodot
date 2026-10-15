using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// How many objects of a square the collision scan looks at. The original's ObjectCheck walks each
/// square's object list while n &lt; 0x40, and returns from the whole scan when a square used all 0x40
/// (uw1/src/motion/COLLIDE.C:509-525, uw2/src/motion/COLLIDE.C:495-511). So the 65th object of a
/// square is never checked. The port's ScanForCollisions (src/physics/motion_collisions.cs) is that
/// function.
///
/// The scenario puts on the square north-west of the player an object with a height, which makes a
/// collision record, under a pile of objects with no height, which the scan skips (uw1 COLLIDE.C:518).
/// It then runs ScanForCollisions for a point at the north-west corner of the player's square with
/// radius 3, whose first square is that one (x0 = y0 = (0 - 3 - 11) / 8 = -1, uw1 COLLIDE.C:501-504).
/// - With 63 objects on top, the object is the 64th and must be recorded (the precondition).
/// - With 64 on top, it is the 65th and must not be, and the scan ends there with no record.
///
/// Both games.
/// </summary>
public sealed class CollisionScanLimit : Scenario
{
    public override string Name => "collision-scan-limit";
    public override string Cites => "underworld-exhumed uw1/src/motion/COLLIDE.C:509-525 (ObjectCheck, n < 0x40, n == 0x40); uw2/src/motion/COLLIDE.C:495-511";

    string _problem;
    int _cx, _cy, _solidId = -1, _flatId = -1;
    uwObject _solid;
    int _countAt64 = -1, _countAt65 = -1;
    bool _recordedAt64, _recordedAt65;

    public override Task Setup(ScenarioContext c)
    {
        var p = playerdat.playerObject;
        _cx = p.npc_xhome; _cy = p.npc_yhome;
        if (_cx < 1 || _cy < 1) { _problem = $"the player's square ({_cx},{_cy}) is on the map's edge"; return Task.CompletedTask; }
        for (int id = 0x80; id < 0x180 && (_solidId < 0 || _flatId < 0); id++)
        {
            if (_solidId < 0 && commonObjDat.height(id) > 0) _solidId = id;
            if (_flatId < 0 && commonObjDat.height(id) == 0) _flatId = id;
        }
        if (_solidId < 0 || _flatId < 0) { _problem = "no static object with a height and one without"; return Task.CompletedTask; }
        _solid = BatchB.SpawnStatic(_solidId, _cx - 1, _cy - 1);
        if (_solid == null) { _problem = "the object could not be made"; return Task.CompletedTask; }
        _solid.xpos = 7; _solid.ypos = 7;   // in the corner next to the player's square
        for (int i = 0; i < 63; i++)
            if (BatchB.SpawnStatic(_flatId, _cx - 1, _cy - 1) == null) { _problem = $"only {i} flat objects could be made"; return Task.CompletedTask; }
        return Task.CompletedTask;
    }

    /// <summary>Runs the port's scan for a point at the north-west corner of the player's square.</summary>
    (int count, bool recorded) Scan()
    {
        var saved = MotionCalcArray.PtrToMotionCalc;
        try
        {
            MotionCalcArray.PtrToMotionCalc = new byte[0x20];
            MotionCalcArray.x0 = (ushort)(_cx << 3);
            MotionCalcArray.y2 = (ushort)(_cy << 3);
            MotionCalcArray.z4 = (ushort)_solid.zpos;
            MotionCalcArray.Radius8 = 3;
            MotionCalcArray.Height9 = 8;
            MotionCalcArray.MotionArrayObjectIndexA = 0;
            motion.ScanForCollisions(0, 0);
            int n = MotionCalcArray.Unk14_collisoncount;
            bool found = false;
            for (int i = 0; i < n; i++)
                if (motion.collisionTable[i].link == _solid.index) found = true;
            return (n, found);
        }
        finally { MotionCalcArray.PtrToMotionCalc = saved; }
    }

    public override Task Act(ScenarioContext c)
    {
        if (_problem != null) return Task.CompletedTask;
        (_countAt64, _recordedAt64) = Scan();
        if (BatchB.SpawnStatic(_flatId, _cx - 1, _cy - 1) == null) { _problem = "the 64th flat object could not be made"; return Task.CompletedTask; }
        (_countAt65, _recordedAt65) = Scan();
        c.Note($"object 0x{_solidId:X} (#{_solid.index}) under 0x{_flatId:X}s at ({_cx - 1},{_cy - 1}): as the 64th, {_countAt64} records, its own {_recordedAt64}; as the 65th, {_countAt65} records, its own {_recordedAt65}");
        return Task.CompletedTask;
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        if (!_recordedAt64) return "precondition: the object as the 64th of its square made no collision record";
        var k = new Checks();
        k.True(!_recordedAt65, "the 65th object of a square was checked for collision (uw1 COLLIDE.C:509, uw2 COLLIDE.C:495: n < 0x40)");
        k.Equal("collision records after a square of 64 objects with no height", 0, _countAt65, "uw1 COLLIDE.C:524-525, uw2 COLLIDE.C:510-511: the scan ends");
        return k.Result;
    }
}
