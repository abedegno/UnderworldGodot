using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// Guard: using a switch that drives a door trap flips the switch and moves the door.
///
/// The original's UseRect flips a button or switch (minor class 3) to its other picture, inclass
/// + 8 modulo 16 (uw1/src/obj/USEITEMS.C:992-998); UseObj then sets off what the switch holds,
/// checkTrap with kind 4 (USEITEMS.C:173, 1079-1098), so its use trigger (0x1A2) runs UseTrigger
/// and the trap it names (uw1/src/event/TRIGGER.C:104-120). A door trap (0x188) at the trigger's
/// square opens (quality 1), closes (2) or toggles (3) the door there, after swapping its lock for
/// the trap's (TRIGGER.C:339-386). The door then moves as any door does: open is inclass | 8 and
/// 0x18 higher (USEITEMS.C:1132-1161, uw1/src/obj/EFFECT.C:97-140). UW2 is the same
/// (uw2/src/obj/USEITEMS.C, uw2/src/event/TRIGGER.C).
///
/// The switch comes from the level's live objects: the nearest switch 0x170..0x17F whose link
/// is a use trigger whose own link is a door trap, with a closed door 0x140..0x145 on the
/// trigger's square (UW1 level 1 has one beside the start).
/// </summary>
public sealed class GuardSwitch : Scenario
{
    const int UseTrigger = 0x1A2, DoorTrap = 0x188, MovingDoor = 0x1CF;

    public override string Name => "guard-switch";
    public override string Cites => "underworld-exhumed uw1/src/obj/USEITEMS.C:173, 992-998, 1079-1098, 1132-1194; uw1/src/event/TRIGGER.C:104-120, 339-386; uw1/src/obj/EFFECT.C:97-140; uw2 the same functions";

    string _problem;
    uwObject _switch, _door;
    int _switchItem0, _switchItemAfter, _doorItem0, _doorZ0, _doorItemAfter, _doorZAfter, _trapQuality;
    bool _settled;

    public override Task Setup(ScenarioContext c)
    {
        var map = UWTileMap.current_tilemap;
        var p = playerdat.playerObject;
        int bestD = int.MaxValue;
        for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
            {
                int i = map.Tiles[x, y].indexObjectList;
                for (int guard = 0; i != 0 && guard < 1024; guard++)
                {
                    var o = map.LevelObjects[i];
                    if (o == null) break;
                    if (o.item_id >= 0x170 && o.item_id <= 0x17F && o.is_quant == 0 && o.link > 0)
                    {
                        var trig = map.LevelObjects[o.link];
                        var trap = trig != null && trig.item_id == UseTrigger && trig.link > 0 ? map.LevelObjects[trig.link] : null;
                        if (trap != null && trap.item_id == DoorTrap && trap.quality >= 1 && trap.quality <= 3 && UWTileMap.ValidTile(trig.quality, trig.owner))
                        {
                            var door = DoorOn(trig.quality, trig.owner);
                            int dd = (x - p.tileX) * (x - p.tileX) + (y - p.tileY) * (y - p.tileY);
                            if (door != null && door.item_id < 0x146 && trap.quality != 2 && dd < bestD)
                            {
                                bestD = dd; _switch = o; _door = door; _trapQuality = trap.quality;
                            }
                        }
                    }
                    i = o.next;
                }
            }
        if (_switch == null) { _problem = "no switch on this level drives a door trap at a closed door"; return Task.CompletedTask; }
        c.Note($"switch {_switch.index} (0x{_switch.item_id:X}) at ({_switch.tileX}, {_switch.tileY}) drives door {_door.index} (0x{_door.item_id:X}) at ({_door.tileX}, {_door.tileY}) with a door trap of quality {_trapQuality}");
        return Task.CompletedTask;
    }

    static uwObject DoorOn(int x, int y)
    {
        var map = UWTileMap.current_tilemap;
        int i = map.Tiles[x, y].indexObjectList;
        for (int guard = 0; i != 0 && guard < 1024; guard++)
        {
            var o = map.LevelObjects[i];
            if (o == null) break;
            if (o.item_id >= 0x140 && o.item_id <= 0x147) return o;   // the first door (MAJOR_RECT, minor 0), as uw1 TRIGGER.C:343 finds it
            i = o.next;
        }
        return null;
    }

    public override async Task Act(ScenarioContext c)
    {
        if (_problem != null) return;
        _switchItem0 = _switch.item_id; _doorItem0 = _door.item_id; _doorZ0 = _door.zpos;
        use.Use(_switch, playerdat.playerObject, UWTileMap.current_tilemap.LevelObjects, WorldObject: true);
        _switchItemAfter = _switch.item_id;
        await c.Frames(2);
        for (int f = 0; f < 600 && _door.item_id == MovingDoor; f++) await c.Frames(1);
        _settled = _door.item_id != MovingDoor;
        _doorItemAfter = _door.item_id; _doorZAfter = _door.zpos;
        c.Note($"switch 0x{_switchItem0:X} -> 0x{_switchItemAfter:X}; door 0x{_doorItem0:X} z {_doorZ0} -> 0x{_doorItemAfter:X} z {_doorZAfter}");
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        var k = new Checks();
        k.Equal("switch item after the use", (_switchItem0 & ~0xF) | ((_switchItem0 + 8) & 0xF), _switchItemAfter, "inclass + 8 & 0xF: uw1 USEITEMS.C:992-997");
        k.True(_settled, "the door was still moving 600 frames after the switch");
        k.Equal("door item after the switch's door trap", _doorItem0 + 8, _doorItemAfter, "the trap opens (1) or toggles (3) the closed door: uw1 TRIGGER.C:360-369, EFFECT.C:133-138");
        k.Equal("door height after the switch's door trap", _doorZ0 + 0x18, _doorZAfter, "OpenDoor raises it 0x18: uw1 USEITEMS.C:1147-1148");
        return k.Result;
    }
}
