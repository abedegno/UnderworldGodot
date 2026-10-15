using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// Picking an object up from the 3D view is refused when the terrain is in the way, not only
/// when it is too far or too high (UW1 and UW2). The original's player_3dget takes the object only
/// when InPickRange (distance and height) passes and BlockingTerrain does not refuse
/// (uw1/src/ui/INTERACT.C:453-455; uw2/src/ui/INTERACT.C:356-358). BlockingTerrain, at a non-zero
/// reach, refuses an object below the bottom of the player's reach band (his z less 8) or above
/// it (that plus 32); then walks the tiles from the player's to the object's and refuses, at the
/// normal reach 0x90, a tile whose floor is below the bottom of the band divided by 8, and, before
/// the object's own tile, a tile of another terrain class than the player's (water, lava, ice),
/// unless a bridge on the tile lies between the bottom of the band and the object
/// (uw1/src/ui/INTERACT.C:300-370; uw2/src/ui/INTERACT.C:165-237).
///
/// The port checked distance and height only (src/ui/uimanager_interaction.cs, CanReach).
///
/// The scenario finds three open squares in a row west to east at one floor height (2 to 13) with
/// land floors and no objects, puts the player in the first at fine x 7, and a dagger in the third
/// at fine x 0, nine fine units away, inside the reach of 12. It clicks the dagger in pickup mode
/// (uimanager.InteractWithObjectCollider, the call the view's click makes) five times: plain; with
/// the middle square's floor changed to a water texture of the level's own texture map; the same
/// with a bridge on the middle square at the floor's z; with the middle square's floor two steps
/// lower; and with the dagger 10 below the player's z (inside InPickRange's 12, below the band's
/// 8). Only the first and third may take it.
/// </summary>
public sealed class ReachBlockingTerrain : Scenario
{
    const int Dagger = 0x03;   // ITEM_DAGGER, uw1/src/include/items.h:150, uw2 items.h:135
    const int Bridge = 0x164;  // ITEM_BRIDGE, uw1/src/include/items.h:525, uw2 items.h:516

    public override string Name => "reach-blocking-terrain";
    public override string Cites => "underworld-exhumed uw1/src/ui/INTERACT.C:300-370, 453-455; uw2/src/ui/INTERACT.C:165-237, 356-358";

    string _problem;
    int _x, _y, _h, _water = -1;
    readonly bool?[] _took = new bool?[5];
    static readonly string[] Cases =
    {
        "plain, nothing in the way",
        "water floor on the square between",
        "water floor on the square between, bridge on it",
        "square between two floor steps lower",
        "dagger 10 below the player",
    };
    static readonly bool[] Expected = { true, false, true, false, false };

    static int Class(TileInfo t) => UWClass._RES == UWClass.GAME_UW2 ? (t.terrain & 0xC0) >> 6 : t.terrain;

    public override Task Setup(ScenarioContext c)
    {
        var map = UWTileMap.current_tilemap;
        for (int y = 1; y < 63 && _h == 0; y++)
            for (int x = 1; x < 61 && _h == 0; x++)
            {
                var a = map.Tiles[x, y];
                int h = a.floorHeight;
                if (h < 2 || h > 13) continue;
                bool ok = true;
                for (int i = 0; i < 3; i++)
                {
                    var t = map.Tiles[x + i, y];
                    if (t.tileType != UWTileMap.TILE_OPEN || t.floorHeight != h || t.indexObjectList != 0 || Class(t) != 0) ok = false;
                }
                if (ok) { _x = x; _y = y; _h = h; }
            }
        if (_h == 0) { _problem = "no three empty open land squares in a row at one floor height from 2 to 13"; return Task.CompletedTask; }

        // A floor texture slot of this level whose terrain is water (UW1 TERRAIN.DAT word 0x10, UW2 class 1).
        var mid = map.Tiles[_x + 1, _y];
        int keep = mid.floorTexture;
        for (int k = 0; k < 16 && _water < 0; k++)
        {
            try
            {
                mid.floorTexture = (short)k;
                if (UWClass._RES == UWClass.GAME_UW2 ? Class(mid) == 1 : Class(mid) == 0x10) _water = k;
            }
            catch (System.IndexOutOfRangeException) { }
        }
        mid.floorTexture = (short)keep;
        if (_water < 0) _problem = "no water floor texture in this level's texture map";
        c.Note($"squares ({_x}, {_y}) to ({_x + 2}, {_y}) at floor height {_h}; water floor slot {_water}");
        return Task.CompletedTask;
    }

    public override async Task Act(ScenarioContext c)
    {
        if (_problem != null) return;
        var map = UWTileMap.current_tilemap;
        var p = playerdat.playerObject;
        var mid = map.Tiles[_x + 1, _y];
        int keepFloor = mid.floorTexture, keepHeight = mid.floorHeight;
        int ptx = p.tileX, pty = p.tileY; short pxp = p.xpos, pyp = p.ypos, pzp = p.zpos;
        var mode = uimanager.InteractionMode;

        for (int i = 0; i < Cases.Length; i++)
        {
            mid.floorTexture = (short)(i == 1 || i == 2 ? _water : keepFloor);
            mid.floorHeight = (short)(i == 3 ? _h - 2 : keepHeight);
            uwObject bridge = null;
            if (i == 2)
            {
                bridge = ObjectCreator.spawnObjectInTile(Bridge, _x + 1, _y, 3, 3, (short)(_h << 3), RenderImmediately: false);
                if (bridge == null) { _problem = "no bridge could be made"; break; }
            }
            p.tileX = _x; p.tileY = _y; p.xpos = 7; p.ypos = 3; p.zpos = (short)(_h << 3);
            short dz = (short)(i == 4 ? (_h << 3) - 10 : _h << 3);
            var d = ObjectCreator.spawnObjectInTile(Dagger, _x + 2, _y, 0, 3, dz, RenderImmediately: false);
            if (d == null) { _problem = "no dagger could be made"; break; }
            playerdat.ObjectInHand = -1;
            uimanager.InteractionMode = uimanager.InteractionModes.ModePickup;
            uimanager.InteractWithObjectCollider(d.index, true);
            _took[i] = playerdat.ObjectInHand == d.index;
            playerdat.ObjectInHand = -1;
            uimanager.instance.mousecursor.SetCursorToCursor();
            if (bridge != null)
            {// the middle square was empty: take the bridge off it again
                mid.indexObjectList = 0;
                ObjectFreeLists.ReleaseFreeObject(bridge);
            }
        }

        mid.floorTexture = (short)keepFloor;
        mid.floorHeight = (short)keepHeight;
        p.tileX = ptx; p.tileY = pty; p.xpos = pxp; p.ypos = pyp; p.zpos = pzp;
        uimanager.InteractionMode = mode;
        await c.Frames(1);
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        string cite = c.Game == "uw1" ? "BlockingTerrain: uw1 INTERACT.C:315-370, player_3dget 453-455" : "BlockingTerrain: uw2 INTERACT.C:180-237, player_3dget 356-358";
        var k = new Checks();
        for (int i = 0; i < Cases.Length; i++)
        {
            if (_took[i] == null) { k.True(false, $"{Cases[i]}: the click never ran"); continue; }
            k.True(_took[i] == Expected[i], $"{Cases[i]}: expected the dagger {(Expected[i] ? "taken" : "refused")}, it was {(_took[i] == true ? "taken" : "refused")} ({cite})");
        }
        return k.Result;
    }
}
