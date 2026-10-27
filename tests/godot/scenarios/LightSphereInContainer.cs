using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// UW2's light sphere recharger (hack trap 35) sparkles once on each object of the square that is a
/// light sphere or holds one. The original's recharge_lightbulbs recharges every light sphere in
/// the square's chain and their contents (Obj_Check with recharge_a_lightbulb), then walks the
/// square's chain and puts effect 0xC on each object for which HasOrIsObj(obj, 0x93) is true
/// (uw2/src/event/WORLDEV.C:1576-1601; HasOrIsObj uw2/src/obj/OBJECTS.C:475-487; put_effect
/// uw2/src/obj/EFFECT.C:349-376).
///
/// The port searched each object's following chain for a light sphere at the top level only, so a
/// sack holding a sphere got no sparkle, and a sphere further down the chain got one for every
/// object before it (src/traps/a_hack_trap_rechargelightsphere.cs).
///
/// The scenario puts a sack holding a light sphere of quality 1 on an empty open square, runs the
/// trap's hack (a_hack_trap_rechargelightsphere.Activate, the call hack_trap 35 makes) there, and
/// counts the effect objects (item 0x1C0 + 0xC) in the square's chain.
/// </summary>
public sealed class LightSphereInContainer : Scenario
{
    const int Sack = 0x80;         // ITEM_SACK, uw2/src/include/items.h:266
    const int LightSphere = 0x93;  // uw2/src/event/WORLDEV.C:1599
    const int Effect = 0x1C0 + 0xC; // put_effect's cls + FIRST_ANIMOBJ, uw2/src/obj/EFFECT.C:355, uw2/src/include/items.h:129

    public override string Name => "light-sphere-in-container";
    public override string[] Games => new[] { "uw2" };
    public override string Cites => "underworld-exhumed uw2/src/event/WORLDEV.C:1576-1601, uw2/src/obj/OBJECTS.C:475-487";

    string _problem;
    int _x = -1, _y, _effects = -1, _quality = -1, _sphere;

    public override Task Setup(ScenarioContext c)
    {
        var map = UWTileMap.current_tilemap;
        var p = playerdat.playerObject;
        for (int r = 2; r < 20 && _x < 0; r++)
            for (int y = p.tileY - r; y <= p.tileY + r && _x < 0; y++)
                for (int x = p.tileX - r; x <= p.tileX + r && _x < 0; x++)
                    if (UWTileMap.ValidTile(x, y) && map.Tiles[x, y].tileType == UWTileMap.TILE_OPEN && map.Tiles[x, y].indexObjectList == 0)
                    { _x = x; _y = y; }
        if (_x < 0) { _problem = "no empty open square near the player"; return Task.CompletedTask; }
        short z = (short)(map.Tiles[_x, _y].floorHeight << 3);
        var sphere = ObjectCreator.spawnObjectInTile(LightSphere, _x, _y, 3, 3, z, RenderImmediately: false);
        var sack = ObjectCreator.spawnObjectInTile(Sack, _x, _y, 3, 3, z);
        if (sphere == null || sack == null) { _problem = "no sack or light sphere could be made"; return Task.CompletedTask; }
        // Put the sphere in the sack.
        sack.next = 0;
        sack.link = sphere.index;
        sphere.next = 0;
        sphere.quality = 1;
        _sphere = sphere.index;
        map.Tiles[_x, _y].indexObjectList = sack.index;
        return Task.CompletedTask;
    }

    public override async Task Act(ScenarioContext c)
    {
        if (_problem != null) return;
        a_hack_trap_rechargelightsphere.Activate(_x, _y);
        var objects = UWTileMap.current_tilemap.LevelObjects;
        _effects = 0;
        int i = UWTileMap.current_tilemap.Tiles[_x, _y].indexObjectList;
        for (int g = 0; i != 0 && g < 1024; g++)
        {
            if (objects[i].item_id == Effect) _effects++;
            i = objects[i].next;
        }
        _quality = objects[_sphere].quality;
        c.Note($"square ({_x}, {_y}): {_effects} effects, sphere quality {_quality}");
        await c.Frames(1);
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        var k = new Checks();
        k.Equal("light sphere quality inside the sack", 0x3F, _quality, "recharge_a_lightbulb through Obj_Check: uw2 WORLDEV.C:1576-1583, 1597, OBJECTS.C:104-122");
        k.Equal("sparkles on the square", 1, _effects, "HasOrIsObj(obj, 0x93) for the sack: uw2 WORLDEV.C:1598-1600");
        return k.Result;
    }
}
