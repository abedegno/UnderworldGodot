using System.Threading.Tasks;
using Godot;

namespace Underworld.Scenarios;

/// <summary>
/// Level, open ground for the trade scenarios. end_barter and take_from_npc put items down at random spots within 5 eighths
/// of their owner (near_mob_put_at, drop_around_place). In UW1 the player starts beside a slope down (tiles (31, 3) and
/// (32, 3) are a floor lower), and an item put there at the player's height falls and on landing may be culled
/// (ObjectHitsFloorTile's culling test), so whether it stayed on the map depended on the random draws. These helpers move
/// the player and the talker to squares whose whole neighbourhood within 1 tile is open floor at one height, not water or
/// lava, so every spot drop_around_place can pick is level ground.
/// </summary>
static class TradeGround
{
    /// <summary>
    /// True when the 3 by 3 squares around (x, y) are open floor at height h, not water or lava, and hold no coin (a
    /// coin put down could join it), no creature and no trap or trigger.
    /// </summary>
    static bool OpenAround(int x, int y, int h)
    {
        var map = UWTileMap.current_tilemap;
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int tx = x + dx, ty = y + dy;
                if (!UWTileMap.ValidTile(tx, ty)) return false;
                var t = map.Tiles[tx, ty];
                if (t.tileType != UWTileMap.TILE_OPEN || t.floorHeight != h || t.isWater || t.isLava) return false;
                int i = t.indexObjectList;
                for (int guard = 0; i != 0 && guard < 1024; guard++)
                {
                    var o = map.LevelObjects[i];
                    if (o.item_id == 0xA0 || o.item_id == 0xA1 || o.majorclass == 1 || o.majorclass == 6) return false;
                    i = o.next;
                }
            }
        return true;
    }

    /// <summary>
    /// The nearest square to the player with level, open ground around it, and the nearest other such square at the same height
    /// at least 3 squares from it, for the talker, so the two neighbourhoods do not overlap; (-1, -1, -1, -1) if there are none.
    /// </summary>
    public static (int px, int py, int nx, int ny) FindPair()
    {
        var map = UWTileMap.current_tilemap;
        var p = playerdat.playerObject;
        foreach (var (x, y) in Around(p.tileX, p.tileY))
        {
            int h = map.Tiles[x, y].floorHeight;
            if (!OpenAround(x, y, h)) continue;
            foreach (var (nx, ny) in Around(x, y))
            {
                if (System.Math.Max(System.Math.Abs(nx - x), System.Math.Abs(ny - y)) >= 3 && map.Tiles[nx, ny].floorHeight == h && OpenAround(nx, ny, h))
                    return (x, y, nx, ny);
            }
        }
        return (-1, -1, -1, -1);
    }

    /// <summary>The squares on the map in rings of growing distance from (x, y), itself first.</summary>
    static System.Collections.Generic.IEnumerable<(int, int)> Around(int x, int y)
    {
        for (int d = 0; d < 64; d++)
            for (int dy = -d; dy <= d; dy++)
                for (int dx = -d; dx <= d; dx++)
                    if (System.Math.Max(System.Math.Abs(dx), System.Math.Abs(dy)) == d && UWTileMap.ValidTile(x + dx, y + dy))
                        yield return (x + dx, y + dy);
    }

    /// <summary>Teleports the player to (x, y) on this level and waits until the move has happened and the game is running.</summary>
    public static async Task<bool> MovePlayer(ScenarioContext c, int x, int y)
    {
        Teleportation.Teleport(character: 0, tileX: x, tileY: y, newLevel: 0, HeadingHeightFlag: 0);
        ulong deadline = Time.GetTicksMsec() + 20000;
        int stable = 0;
        while (Time.GetTicksMsec() < deadline && stable < 10)
        {
            await c.Frames(1);
            var p = playerdat.playerObject;
            stable = (p.tileX == x && p.tileY == y && !uimanager.WaitingForFade && uimanager.InGame) ? stable + 1 : 0;
        }
        return stable >= 10;
    }

    /// <summary>Moves an NPC to the middle of square (x, y), on its floor, taking it out of its old square's list and putting it at the head of the new one's.</summary>
    public static void MoveNpc(uwObject npc, int x, int y)
    {
        var map = UWTileMap.current_tilemap;
        if (UWTileMap.ValidTile(npc.tileX, npc.tileY))
        {
            var old = map.Tiles[npc.tileX, npc.tileY];
            ObjectRemover_OLD.RemoveObjectFromLinkedList(old.indexObjectList, npc.index, map.LevelObjects, old.Ptr + 2);
        }
        var tile = map.Tiles[x, y];
        npc.tileX = x; npc.tileY = y;
        npc.npc_xhome = (short)x; npc.npc_yhome = (short)y;
        npc.xpos = 3; npc.ypos = 3;
        npc.zpos = (short)(tile.floorHeight << 3);
        npc.next = tile.indexObjectList;
        tile.indexObjectList = npc.index;
    }
}
