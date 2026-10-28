using System.Collections.Generic;
using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// UW1's create object trap. The original's UseTrap, case TRAP_CREATE_OBJECT, makes one chance roll,
/// (rand() * 0x3F) / 0x8000 &lt; quality meaning nothing is made, then copies the linked template
/// and puts the copy at the template's own fine x and y and height in the trap's square
/// (put_at((x &lt;&lt; 3) + finex, (y &lt;&lt; 3) + finey, z, ...)) (uw1/src/event/TRIGGER.C:297-317).
///
/// The scenario makes a template, an apple (0xB3) at fine position (2, 5) and the square's floor
/// height, marked with owner 0x2A, and a create object trap (0x187) of quality 32 linked to it. For
/// 30 spread-out seeds it seeds the game's generator and fires the trap through the port's trap
/// dispatcher, trap.ActivateTrap, at an open square next to the player; then it counts the marked
/// apples in that square and checks where each landed. The count must be the number of seeds whose
/// first draw passes the original's single roll.
///
/// UW1 only. The port once rolled the chance twice, in Activate (rand() % 0x3F) and again in
/// CreateObjectUW1 (src/traps/a_create_object_trap.cs:18, 59), so a trap succeeded about
/// ((63 - q) / 63) squared of the time, and put the copy at a random spot in the square
/// (a_create_object_trap.cs:194).
/// </summary>
public sealed class Uw1CreateObjectTrap : Scenario
{
    const int Apple = 0xB3, TrapItem = 0x187, Marker = 0x2A, Quality = 32, FineX = 2, FineY = 5, Trials = 30;

    public override string Name => "uw1-create-object-trap";
    public override string[] Games => new[] { "uw1" };
    public override string Cites => "underworld-exhumed uw1/src/event/TRIGGER.C:297-317 (UseTrap, TRAP_CREATE_OBJECT)";

    string _problem;
    int _x, _y, _expected, _made, _misplaced, _z;
    readonly List<string> _places = new();

    public override Task Setup(ScenarioContext c)
    {
        (_x, _y) = BatchB.OpenSquareNearPlayer();
        int ts = ObjectCreator.PrepareNewObject(Apple), tr = ObjectCreator.PrepareNewObject(TrapItem);
        if (ts == 0 || tr == 0) { _problem = "the template or the trap could not be made"; return Task.CompletedTask; }
        var template = UWTileMap.current_tilemap.LevelObjects[ts];
        _z = UWTileMap.current_tilemap.Tiles[_x, _y].floorHeight << 3;
        template.xpos = (short)FineX; template.ypos = (short)FineY; template.zpos = (short)_z;
        template.owner = (short)Marker;
        var trap = UWTileMap.current_tilemap.LevelObjects[tr];
        trap.is_quant = 0;
        trap.link = (short)ts;
        trap.quality = (short)Quality;
        _trap = trap;
        if (Count() != 0) _problem = "marked apples are already in the square";
        return Task.CompletedTask;
    }

    uwObject _trap;

    int Count()
    {
        int n = 0;
        var map = UWTileMap.current_tilemap;
        for (int i = map.Tiles[_x, _y].indexObjectList, guard = 0; i != 0 && guard < 1024; guard++)
        {
            var o = map.LevelObjects[i];
            if (o == null) break;
            if (o.item_id == Apple && o.owner == Marker)
            {
                n++;
                if (o.xpos != FineX || o.ypos != FineY || o.zpos != _z) { _misplaced++; if (_places.Count < 5) _places.Add($"({o.xpos},{o.ypos},{o.zpos})"); }
            }
            i = o.next;
        }
        return n;
    }

    public override Task Act(ScenarioContext c)
    {
        if (_problem != null) return Task.CompletedTask;
        for (uint t = 1; t <= Trials; t++)
        {
            uint seed = t * 2654435761u;   // spread out: small seeds all start with a small first draw
            var d = new BatchB.DosRand(seed);
            if ((int)((long)d.Rand() * 0x3F / 0x8000) >= Quality) _expected++;   // TRIGGER.C:299-300
            BatchB.Seed(seed);
            trap.ActivateTrap(0, _trap, null, _x, _y, UWTileMap.current_tilemap.LevelObjects);
        }
        _misplaced = 0;
        _places.Clear();
        _made = Count();
        c.Note($"trap quality {Quality} over {Trials} seeds: {_made} made (original {_expected}); {_misplaced} not at ({FineX},{FineY},{_z}) {string.Join(" ", _places)}");
        return Task.CompletedTask;
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        if (_expected == 0) return "precondition: no seed passes the original's roll";
        var k = new Checks();
        k.Equal($"objects made by a quality {Quality} create object trap over {Trials} seeds", _expected, _made, "uw1 TRIGGER.C:299-300, one roll (rand() * 0x3F) / 0x8000 < quality");
        k.Equal("copies not at the template's fine position and height", 0, _misplaced, "uw1 TRIGGER.C:315-316, put_at at the template's finex, finey and z");
        return k.Result;
    }
}
