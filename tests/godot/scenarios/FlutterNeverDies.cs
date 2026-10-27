using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// A creature whose goal is follow (3) or flutter (11) never finishes dying (UW1). The original's critter_ai sends
/// those two goals to critter_mv before it looks at the dying sequence, so the branch that removes a dying creature
/// at frame 3 and leaves its loot and corpse never runs for them (uw1/src/critter/AI.C:1133-1146); the kill itself
/// only starts the sequence (go_into_dying_sequence, AI.C:1422-1432).
///
/// Observed in DOS by Underworld Revisited (PQMarine, MIT): Assets/UWDataImport/UWData/UWCritterRules.cs:486-494
/// (a goblin with goal 11 walked on dying, could still be hit, and left nothing).
///
/// The scenario puts two walking creatures within three squares of the player, one with goal 11 and one with goal 0 (the control), kills
/// each with one blow from the player (damage.DamageObject), and lets five seconds pass. The control must be gone
/// from its square, so the dying path ran; the fluttering one must still be in the level.
/// </summary>
public sealed class FlutterNeverDies : Scenario
{
    public override string Name => "flutter-never-dies";
    public override string[] Games => new[] { "uw1" };
    public override string Cites => "underworld-exhumed uw1/src/critter/AI.C:1133-1146, 1422-1432";

    string _problem;
    uwObject _flutter, _control;
    int _flutterIndex, _controlIndex, _id;
    bool _flutterThere, _controlThere;

    static bool InLevel(int index, int item)
    {
        var map = UWTileMap.current_tilemap;
        for (int x = 0; x < 64; x++)
            for (int y = 0; y < 64; y++)
                for (int i = map.Tiles[x, y].indexObjectList, g = 0; i != 0 && g < 256; g++, i = map.LevelObjects[i].next)
                    if (i == index && map.LevelObjects[i].item_id == item) return true;
        return false;
    }

    public override async Task Setup(ScenarioContext c)
    {
        await Guards.CameraOnPlayer(c);
        _id = BatchB.FindCritter(id => BatchA.Walker(id));
        if (_id < 0) { _problem = "no walking creature type"; return; }
        var p = playerdat.playerObject;
        var tiles = UWTileMap.current_tilemap.Tiles;
        for (int n = 0; n < 49 && _control == null; n++)
        {
            int dx = n % 7 - 3, dy = n / 7 - 3;
            int x = p.tileX + dx, y = p.tileY + dy;
            if ((dx == 0 && dy == 0) || !UWTileMap.ValidTile(x, y) || tiles[x, y].tileType != UWTileMap.TILE_OPEN) continue;
            var o = BatchB.SpawnCritter(_id, x, y);
            if (o == null) break;
            o.npc_attitude = 2; o.IsAlly = 0; o.npc_gtarg = 0; o.npc_hp = 2; o.npc_whoami = 0;
            if (_flutter == null) { _flutter = o; o.npc_goal = 11; }
            else { _control = o; o.npc_goal = 0; break; }
        }
        if (_control == null) { _problem = "fewer than two open squares within three of the player"; return; }
        _flutterIndex = _flutter.index; _controlIndex = _control.index;
        c.Note($"creature 0x{_id:X}: goal 11 at ({_flutter.tileX}, {_flutter.tileY}), goal 0 at ({_control.tileX}, {_control.tileY})");
    }

    public override async Task Act(ScenarioContext c)
    {
        if (_problem != null) return;
        foreach (var o in new[] { _flutter, _control })
            damage.DamageObject(objToDamage: o, basedamage: 50, damagetype: 4, objList: UWTileMap.current_tilemap.LevelObjects, WorldObject: true, damagesource: 1);
        await c.Frames(300);
        _flutterThere = InLevel(_flutterIndex, _id);
        _controlThere = InLevel(_controlIndex, _id);
        c.Note($"after five seconds: goal 11 creature {(_flutterThere ? $"still there (hp {_flutter.npc_hp}, goal {_flutter.npc_goal}, animation {_flutter.npc_animation})" : "gone")}, control {(_controlThere ? $"still there (hp {_control.npc_hp}, animation {_control.npc_animation})" : "gone")}");
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        if (_controlThere) return "precondition: the control creature (goal 0) did not die and leave, so the dying path never ran";
        var k = new Checks();
        k.True(_flutterThere, "a creature with goal 11 finished dying and left the level; it must keep moving with the dying sequence (critter_ai: uw1 AI.C:1133-1135)");
        return k.Result;
    }
}
