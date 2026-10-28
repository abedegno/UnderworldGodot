using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// How far the hand reaches, counted in eighths of a square (UW1 and UW2). The original's InPickRange
/// turns both positions into eighths (square times 8 plus the fine position 0 to 7) and refuses when the
/// squared distance is above the reach: 0x90 (twelve eighths) for the bare hand, 0x190 (twenty) while the
/// pole is in use (uw1/src/ui/INTERACT.C:274-296, uw1/src/game/PLAYDATA.C:165-166; uw2/src/ui/INTERACT.C:137-162,
/// uw2/src/game/PLAYDATA.C:141-142). Out of reach, a use says "You are unable to use that from here." (uw1
/// INTERACT.C:551-554) and a pick-up "That is too far away to take." (string 0x5D, INTERACT.C:482-484; uw2
/// INTERACT.C:451, 387-389).
///
/// Observed in DOS by Underworld Revisited (PQMarine, MIT): Assets/UWDataImport/UWData/UWReachRules.cs:17-21, 63-64.
///
/// The scenario finds three open land squares in a row west to east at one floor height with no objects,
/// puts the player in the first and a dagger in the second or third, and clicks it (uimanager.InteractWithObjectCollider,
/// the call the view's click makes): in use mode at 11 eighths across and 1 along (122, in reach) and at 12 and 1
/// (145, out of reach); in pick-up mode at the same two spots; and in use mode with the pole at 20 and 0 (400, in
/// reach) and 20 and 1 (401, out of reach).
/// </summary>
public sealed class ReachEighths : Scenario
{
    const int Dagger = 0x03;   // ITEM_DAGGER, uw1/src/include/items.h:150, uw2 items.h:135

    public override string Name => "reach-eighths";
    public override string Cites => "underworld-exhumed uw1/src/ui/INTERACT.C:274-296, 482-484, 551-554, uw1/src/game/PLAYDATA.C:165-166; uw2/src/ui/INTERACT.C:137-162, 387-389, 451, uw2/src/game/PLAYDATA.C:141-142";

    string _problem;
    int _x, _y, _h;

    // mode, player fine x, player fine y, dagger square offset east, dagger fine x, dagger fine y, pole, expect in reach
    static readonly (string what, bool use, int px, int py, int dx, int ox, int oy, bool pole, bool reach)[] Cases =
    {
        ("use at 11 and 1 eighths (122)", true, 4, 3, 1, 7, 4, false, true),
        ("use at 12 and 1 eighths (145)", true, 3, 3, 1, 7, 4, false, false),
        ("pick-up at 11 and 1 eighths (122)", false, 4, 3, 1, 7, 4, false, true),
        ("pick-up at 12 and 1 eighths (145)", false, 3, 3, 1, 7, 4, false, false),
        ("use with the pole at 20 and 0 eighths (400)", true, 3, 3, 2, 7, 3, true, true),
        ("use with the pole at 20 and 1 eighths (401)", true, 3, 3, 2, 7, 4, true, false),
    };
    readonly string[] _result = new string[Cases.Length];

    static int Class(TileInfo t) => UWClass._RES == UWClass.GAME_UW2 ? (t.terrain & 0xC0) >> 6 : t.terrain;

    public override Task Setup(ScenarioContext c)
    {
        var map = UWTileMap.current_tilemap;
        for (int y = 1; y < 63 && _h == 0; y++)
            for (int x = 1; x < 61 && _h == 0; x++)
            {
                int h = map.Tiles[x, y].floorHeight;
                if (h < 2 || h > 13) continue;
                bool ok = true;
                for (int i = 0; i < 3; i++)
                {
                    var t = map.Tiles[x + i, y];
                    if (t.tileType != UWTileMap.TILE_OPEN || t.floorHeight != h || t.indexObjectList != 0 || Class(t) != 0) ok = false;
                }
                if (ok) { _x = x; _y = y; _h = h; }
            }
        if (_h == 0) _problem = "no three empty open land squares in a row at one floor height from 2 to 13";
        else c.Note($"squares ({_x}, {_y}) to ({_x + 2}, {_y}) at floor height {_h}");
        return Task.CompletedTask;
    }

    public override async Task Act(ScenarioContext c)
    {
        if (_problem != null) return;
        var p = playerdat.playerObject;
        int ptx = p.tileX, pty = p.tileY; short pxp = p.xpos, pyp = p.ypos, pzp = p.zpos;
        var mode = uimanager.InteractionMode;
        var keepPole = playerdat.usingpole;
        string tooFar = GameStrings.GetString(1, GameStrings.str_that_is_too_far_away_to_take_).Trim();
        string unable = GameStrings.GetString(1, GameStrings.str_you_are_unable_to_use_that_from_here_).Trim();

        for (int i = 0; i < Cases.Length; i++)
        {
            var k = Cases[i];
            p.tileX = _x; p.tileY = _y; p.xpos = (short)k.px; p.ypos = (short)k.py; p.zpos = (short)(_h << 3);
            var d = ObjectCreator.spawnObjectInTile(Dagger, _x + k.dx, _y, (short)k.ox, (short)k.oy, (short)(_h << 3), RenderImmediately: false);
            if (d == null) { _problem = "no dagger could be made"; break; }
            playerdat.ObjectInHand = -1;
            playerdat.usingpole = k.pole;
            useon.CurrentItemBeingUsed = null;
            uimanager.instance.scroll.Clear();
            uimanager.InteractionMode = k.use ? uimanager.InteractionModes.ModeUse : uimanager.InteractionModes.ModePickup;
            uimanager.InteractWithObjectCollider(d.index, true);
            string text = string.Join(" ", System.Linq.Enumerable.Select(uimanager.instance.scroll.Lines, l => l?.LineText ?? ""));
            bool taken = playerdat.ObjectInHand == d.index;
            bool refused = k.use ? text.Contains(unable) : text.Contains(tooFar);
            _result[i] = refused ? "out of reach" : (k.use || taken ? "in reach" : $"neither taken nor refused ({text.Trim()})");
            playerdat.ObjectInHand = -1;
            uimanager.instance.mousecursor.SetCursorToCursor();
            playerdat.usingpole = keepPole;
            // take the dagger away again, wherever it is
            var tile = UWTileMap.current_tilemap.Tiles[_x + k.dx, _y];
            if (tile.indexObjectList == d.index) tile.indexObjectList = d.next;
            ObjectFreeLists.ReleaseFreeObject(d);
        }

        p.tileX = ptx; p.tileY = pty; p.xpos = pxp; p.ypos = pyp; p.zpos = pzp;
        uimanager.InteractionMode = mode;
        playerdat.usingpole = keepPole;
        c.Note(string.Join("; ", System.Linq.Enumerable.Select(System.Linq.Enumerable.Range(0, Cases.Length), i => $"{Cases[i].what}: {_result[i]}")));
        await c.Frames(1);
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        string cite = c.Game == "uw1" ? "InPickRange: uw1 INTERACT.C:280-290, PLAYDATA.C:165-166" : "InPickRange: uw2 INTERACT.C:144-154, PLAYDATA.C:141-142";
        var k = new Checks();
        for (int i = 0; i < Cases.Length; i++)
        {
            string want = Cases[i].reach ? "in reach" : "out of reach";
            k.True(_result[i] == want, $"{Cases[i].what}: expected {want}, was {_result[i] ?? "never clicked"} ({cite})");
        }
        return k.Result;
    }
}
