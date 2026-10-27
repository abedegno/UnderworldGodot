using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// Where fishing is refused (UW1 and UW2). The original's go_fish looks at the square 11 eighths ahead of the
/// player and refuses with "You cannot fish there. Perhaps somewhere else." unless it is water and
/// (player z >> 3) > (uint16)(water height - 1): the player's height step must be at least the water's, and
/// water of height 0 always refuses, because 0 - 1 as an unsigned word is 0xFFFF (uw1/src/event/WORLDEV.C:314-327,
/// 344-346; uw2/src/event/WORLDEV.C:532-534).
///
/// Observed in DOS by Underworld Revisited (PQMarine, MIT): Assets/UWDataImport/UWData/UWFishingRules.cs:9-19.
///
/// The scenario stands the player at height step 2 on an open square, makes the open square ahead a water floor
/// (a water texture slot of the level's own texture map), and fishes (fishingpole.use from the inventory) with
/// the water at heights 0, 2 and 3. Only height 2 may get past the refusal.
///
/// The port allowed the water up to two steps above the player (src/objects/fishingpole.cs:31), so it fished in
/// water of height 0 and 3.
/// </summary>
public sealed class FishingWaterHeight : Scenario
{
    const int PlayerStep = 2;

    public override string Name => "fishing-water-height";
    public override string Cites => "underworld-exhumed uw1/src/event/WORLDEV.C:314-327, 344-346; uw2/src/event/WORLDEV.C:532-534";

    string _problem;
    int _tx, _ty, _water = -1;
    short _yaw;
    static readonly int[] Heights = { 0, 2, 3 };
    static readonly bool[] Refused = { true, false, true };
    readonly bool?[] _refused = new bool?[3];

    static int Class(TileInfo t) => UWClass._RES == UWClass.GAME_UW2 ? (t.terrain & 0xC0) >> 6 : t.terrain;
    static bool IsWater(TileInfo t) => UWClass._RES == UWClass.GAME_UW2 ? Class(t) == 1 : Class(t) == 0x10;

    public override Task Setup(ScenarioContext c)
    {
        var p = playerdat.playerObject;
        var tiles = UWTileMap.current_tilemap.Tiles;
        if (tiles[p.tileX, p.tileY].tileType == UWTileMap.TILE_SOLID) { _problem = "the player stands in a solid square"; return Task.CompletedTask; }
        foreach (short yaw in new short[] { 0, 0x4000, unchecked((short)0x8000), unchecked((short)0xC000) })
        {
            int x = ((p.tileX << 3) + 3), y = ((p.tileY << 3) + 3);
            motion.GetCoordinateInDirection((yaw >> 8) & 0xFF, 0xB, ref x, ref y);
            int tx = x >> 3, ty = y >> 3;
            if ((tx == p.tileX && ty == p.tileY) || !UWTileMap.ValidTile(tx, ty)) continue;
            if (tiles[tx, ty].tileType != UWTileMap.TILE_OPEN) continue;
            _tx = tx; _ty = ty; _yaw = yaw;
            break;
        }
        if (_tx == 0 && _ty == 0) { _problem = "no open square 11 eighths ahead of the player in any of the four directions"; return Task.CompletedTask; }

        var t = tiles[_tx, _ty];
        int keep = t.floorTexture;
        for (int k = 0; k < 16 && _water < 0; k++)
        {
            try
            {
                t.floorTexture = (short)k;
                if (IsWater(t)) _water = k;
            }
            catch (System.IndexOutOfRangeException) { }
        }
        t.floorTexture = (short)keep;
        if (_water < 0) _problem = "no water floor texture in this level's texture map";
        c.Note($"player at ({p.tileX}, {p.tileY}) step {PlayerStep}, fishing into ({_tx}, {_ty}) with water slot {_water}, yaw 0x{(ushort)_yaw:X}");
        return Task.CompletedTask;
    }

    public override async Task Act(ScenarioContext c)
    {
        if (_problem != null) return;
        var p = playerdat.playerObject;
        var t = UWTileMap.current_tilemap.Tiles[_tx, _ty];
        int keepFloor = t.floorTexture, keepHeight = t.floorHeight;
        short keepZ = p.zpos, keepYaw = playerdat.PlayerCameraYaw_dseg_8294;
        int keepX = motion.playerMotionParams.x_0, keepY = motion.playerMotionParams.y_2;
        string refusal = GameStrings.GetString(1, GameStrings.str_you_cannot_fish_there__perhaps_somewhere_else_).Trim();

        for (int i = 0; i < Heights.Length; i++)
        {
            t.floorTexture = (short)_water;
            t.floorHeight = (short)Heights[i];
            p.zpos = PlayerStep << 3;
            playerdat.PlayerCameraYaw_dseg_8294 = _yaw;
            motion.playerMotionParams.x_0 = (short)((p.tileX << 8) + (3 << 5) + 0xF);
            motion.playerMotionParams.y_2 = (short)((p.tileY << 8) + (3 << 5) + 0xF);
            uimanager.instance.scroll.Clear();
            fishingpole.use(p, false);
            string text = string.Join(" ", System.Linq.Enumerable.Select(uimanager.instance.scroll.Lines, l => l?.LineText ?? ""));
            if (text.Trim().Length > 0) _refused[i] = text.Contains(refusal);
            if (playerdat.ObjectInHand > 0)
            {// a fish was caught: let it go
                ObjectFreeLists.ReleaseFreeObject(UWTileMap.current_tilemap.LevelObjects[playerdat.ObjectInHand]);
                playerdat.ObjectInHand = -1;
                uimanager.instance.mousecursor.SetCursorToCursor();
            }
            c.Note($"water at height {Heights[i]}: {(_refused[i] == null ? "no message" : _refused[i] == true ? "refused" : "fished")} ({text.Trim()})");
        }

        t.floorTexture = (short)keepFloor;
        t.floorHeight = (short)keepHeight;
        p.zpos = keepZ;
        playerdat.PlayerCameraYaw_dseg_8294 = keepYaw;
        motion.playerMotionParams.x_0 = (short)keepX;
        motion.playerMotionParams.y_2 = (short)keepY;
        await c.Frames(1);
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        string cite = c.Game == "uw1" ? "go_fish: uw1 WORLDEV.C:326-327" : "go_fish: uw2 WORLDEV.C:534";
        var k = new Checks();
        for (int i = 0; i < Heights.Length; i++)
        {
            if (_refused[i] == null) { k.True(false, $"water at height {Heights[i]}: fishing printed nothing"); continue; }
            k.True(_refused[i] == Refused[i], $"water at height {Heights[i]} with the player at step {PlayerStep}: expected {(Refused[i] ? "refused" : "fished")}, was {(_refused[i] == true ? "refused" : "fished")} ({cite})");
        }
        return k.Result;
    }
}
