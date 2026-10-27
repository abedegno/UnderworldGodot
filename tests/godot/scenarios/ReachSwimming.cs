using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// Whether a swimming player reaches lower (UW2 only). The original's InPickRange takes the height difference
/// dz = player's height - object's height and refuses when it is more than 12 (the object below) or less than -24
/// (above), doubled with a pole. UW2 first lowers dz by swim_count >> 3 when swim_count is above 0x50
/// (uw2/src/ui/INTERACT.C:156-157), so a swimming player reaches lower and not so high. UW1 has no such term
/// (uw1/src/ui/INTERACT.C:280-295; its comment says otherwise, its code does not).
///
/// The scenario finds an open land square with no objects at a floor height from 3 to 13, puts the player in it
/// and a dagger in the same place 16 above him and then 20 below him, and clicks it in use mode
/// (uimanager.InteractWithObjectCollider): once with swim_count 0 and once with 0x60, the count entering water
/// sets (uw1 motion/PHYSICS.C:85). With 0x60 the term is 12: UW2 makes 16 above -28 (out of reach) and 20 below
/// 8 (in reach); UW1 leaves them -16 (in reach) and 20 (out of reach).
/// </summary>
public sealed class ReachSwimming : Scenario
{
    const int Dagger = 0x03;   // ITEM_DAGGER, uw1/src/include/items.h:150, uw2 items.h:135

    public override string Name => "reach-swimming";
    public override string Cites => "underworld-exhumed uw1/src/ui/INTERACT.C:274-296; uw2/src/ui/INTERACT.C:137-162";

    string _problem;
    int _x, _y, _h;

    // what, swim count, dagger height above the player, expect in reach (UW1), expect in reach (UW2)
    static readonly (string what, int swim, int up, bool uw1, bool uw2)[] Cases =
    {
        ("not swimming, 16 above", 0, 16, true, true),
        ("not swimming, 20 below", 0, -20, false, false),
        ("swimming (0x60), 16 above", 0x60, 16, true, false),
        ("swimming (0x60), 20 below", 0x60, -20, false, true),
    };
    readonly string[] _result = new string[Cases.Length];

    static int Class(TileInfo t) => UWClass._RES == UWClass.GAME_UW2 ? (t.terrain & 0xC0) >> 6 : t.terrain;

    public override Task Setup(ScenarioContext c)
    {
        var map = UWTileMap.current_tilemap;
        for (int y = 1; y < 63 && _h == 0; y++)
            for (int x = 1; x < 63 && _h == 0; x++)
            {
                var t = map.Tiles[x, y];
                if (t.tileType == UWTileMap.TILE_OPEN && t.floorHeight >= 3 && t.floorHeight <= 13 && t.indexObjectList == 0 && Class(t) == 0)
                { _x = x; _y = y; _h = t.floorHeight; }
            }
        if (_h == 0) _problem = "no empty open land square at a floor height from 3 to 13";
        else c.Note($"square ({_x}, {_y}) at floor height {_h}");
        return Task.CompletedTask;
    }

    public override async Task Act(ScenarioContext c)
    {
        if (_problem != null) return;
        var p = playerdat.playerObject;
        int ptx = p.tileX, pty = p.tileY; short pxp = p.xpos, pyp = p.ypos, pzp = p.zpos;
        var mode = uimanager.InteractionMode;
        var keepPole = playerdat.usingpole;
        var keepSwim = playerdat.SwimCounter;
        string unable = GameStrings.GetString(1, GameStrings.str_you_are_unable_to_use_that_from_here_).Trim();

        for (int i = 0; i < Cases.Length; i++)
        {
            var k = Cases[i];
            p.tileX = _x; p.tileY = _y; p.xpos = 3; p.ypos = 3; p.zpos = (short)(_h << 3);
            var d = ObjectCreator.spawnObjectInTile(Dagger, _x, _y, 3, 3, (short)((_h << 3) + k.up), RenderImmediately: false);
            if (d == null) { _problem = "no dagger could be made"; break; }
            d.zpos = (short)((_h << 3) + k.up);
            playerdat.ObjectInHand = -1;
            playerdat.usingpole = false;
            playerdat.SwimCounter = (byte)k.swim;
            useon.CurrentItemBeingUsed = null;
            uimanager.instance.scroll.Clear();
            uimanager.InteractionMode = uimanager.InteractionModes.ModeUse;
            uimanager.InteractWithObjectCollider(d.index, true);
            string text = string.Join(" ", System.Linq.Enumerable.Select(uimanager.instance.scroll.Lines, l => l?.LineText ?? ""));
            _result[i] = text.Contains(unable) ? "out of reach" : "in reach";
            playerdat.SwimCounter = keepSwim;
            playerdat.ObjectInHand = -1;
            uimanager.instance.mousecursor.SetCursorToCursor();
            var tile = UWTileMap.current_tilemap.Tiles[_x, _y];
            if (tile.indexObjectList == d.index) tile.indexObjectList = d.next;
            ObjectFreeLists.ReleaseFreeObject(d);
        }

        p.tileX = ptx; p.tileY = pty; p.xpos = pxp; p.ypos = pyp; p.zpos = pzp;
        uimanager.InteractionMode = mode;
        playerdat.usingpole = keepPole;
        playerdat.SwimCounter = keepSwim;
        c.Note(string.Join("; ", System.Linq.Enumerable.Select(System.Linq.Enumerable.Range(0, Cases.Length), i => $"{Cases[i].what}: {_result[i]}")));
        await c.Frames(1);
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        string cite = c.Game == "uw1" ? "InPickRange has no swim term: uw1 INTERACT.C:290-293" : "InPickRange: uw2 INTERACT.C:155-159";
        var k = new Checks();
        for (int i = 0; i < Cases.Length; i++)
        {
            string want = (c.Game == "uw1" ? Cases[i].uw1 : Cases[i].uw2) ? "in reach" : "out of reach";
            k.True(_result[i] == want, $"{Cases[i].what}: expected {want}, was {_result[i] ?? "never clicked"} ({cite})");
        }
        return k.Result;
    }
}
