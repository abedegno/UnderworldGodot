using System;
using System.Linq;

namespace Underworld.Scenarios;

/// <summary>
/// Shared pieces for the player and magic rule scenarios (batch B): the original's random
/// number formulas over Turbo C++'s rand(), so expected values can be computed from a seed the
/// way the original would draw them, and small helpers that put objects where a scenario needs
/// them through the game's own object and inventory code.
/// </summary>
static class BatchB
{
    /// <summary>Turbo C++'s rand(), the generator the original uses (src/utility/rng.cs mirrors it).</summary>
    public sealed class DosRand
    {
        readonly BorlandRandSource _r;
        public DosRand(uint seed) { _r = new BorlandRandSource(seed); }
        public int Rand() => _r.Rand();

        /// <summary>rollem: dice dice of sides sides, each 1..sides (uwN/src/sys/UTIL.C:72-80).</summary>
        public int Rollem(int dice, int sides)
        {
            int total = dice;
            if (sides > 0 && dice > 0)
                while (dice-- > 0)
                    total += (int)((long)Rand() * sides / 0x8000);
            return total;
        }

        /// <summary>skill_check: value - target + rand() % 31, graded 2, 1, 0, -1 (uw2/src/game/SKILLCHK.C:39, uw1 SKILLCHK.C:48).</summary>
        public int SkillCheck(int value, int target)
        {
            int score = value - target + Rand() % 31;
            if (score > 28) return 2;
            if (score > 15) return 1;
            if (score > 2) return 0;
            return -1;
        }
    }

    /// <summary>Installs a fresh seeded generator as the game's source of draws.</summary>
    public static void Seed(uint seed) => Rng.Source = new BorlandRandSource(seed);

    /// <summary>
    /// A critter of item id at the given square, made as the game makes one (PrepareNewObject on
    /// the mobile list, which initialises it) and linked into the square's object chain the way
    /// CastClass8_Summoning places a summoned monster. Null if no slot was free.
    /// </summary>
    public static uwObject SpawnCritter(int itemId, int tileX, int tileY)
    {
        int slot = ObjectCreator.PrepareNewObject(itemId, ObjectFreeLists.ObjectListType.MobileList);
        if (slot == 0) return null;
        var obj = UWTileMap.current_tilemap.LevelObjects[slot];
        var tile = UWTileMap.current_tilemap.Tiles[tileX, tileY];
        obj.tileX = tileX;
        obj.tileY = tileY;
        obj.npc_xhome = (short)tileX;
        obj.npc_yhome = (short)tileY;
        obj.xpos = 3;
        obj.ypos = 3;
        obj.zpos = (short)(tile.floorHeight << 3);
        obj.next = tile.indexObjectList;
        tile.indexObjectList = obj.index;
        ObjectCreator.RenderObject(obj, UWTileMap.current_tilemap);
        return obj;
    }

    /// <summary>A static object of item id at the given square, linked into its chain. Null if no slot was free.</summary>
    public static uwObject SpawnStatic(int itemId, int tileX, int tileY)
    {
        int slot = ObjectCreator.PrepareNewObject(itemId, ObjectFreeLists.ObjectListType.StaticList);
        if (slot == 0) return null;
        var obj = UWTileMap.current_tilemap.LevelObjects[slot];
        var tile = UWTileMap.current_tilemap.Tiles[tileX, tileY];
        obj.tileX = tileX;
        obj.tileY = tileY;
        obj.zpos = (short)(tile.floorHeight << 3);
        obj.next = tile.indexObjectList;
        tile.indexObjectList = obj.index;
        ObjectCreator.RenderObject(obj, UWTileMap.current_tilemap);
        return obj;
    }

    /// <summary>
    /// Puts a new object of item id with the given quality into paper doll slot paperSlot (0 helm ..
    /// 4 boots, 5 and 6 shoulders, 7 and 8 hands, 9 and 10 rings), the way the inventory moves an
    /// object into a slot (AddObjectToPlayerInventory, SetInventorySlotListHead). Returns the
    /// inventory object, or null if the slot was taken or no object could be made.
    /// </summary>
    public static uwObject Equip(int itemId, int paperSlot, int quality)
    {
        if (playerdat.GetInventorySlotListHead(paperSlot) != 0) return null;
        int slot = ObjectCreator.PrepareNewObject(itemId, ObjectFreeLists.ObjectListType.StaticList);
        if (slot == 0) return null;
        UWTileMap.current_tilemap.LevelObjects[slot].quality = (short)quality;
        int index = playerdat.AddObjectToPlayerInventory(slot, false);
        playerdat.SetInventorySlotListHead(paperSlot, index);
        return playerdat.InventoryObjects[index];
    }

    /// <summary>Empties a paper doll slot (the object stays in the inventory data, unlinked).</summary>
    public static void Unequip(int paperSlot) => playerdat.SetInventorySlotListHead(paperSlot, 0);

    /// <summary>The first creature id 0x40..0x7F (not the adventurer) for which pick is true, or -1.</summary>
    public static int FindCritter(Func<int, bool> pick)
    {
        foreach (int id in Enumerable.Range(0x40, 0x3F))
            if (critterObjectDat.avghit(id) > 0 && pick(id)) return id;
        return -1;
    }

    /// <summary>
    /// Makes the player able to cast anything: level 16, Casting 30, mana and intelligence 30
    /// (so max mana is (30 + 1) * 30 / 8 = 116), 100 mana, no spell waiting for a target.
    /// </summary>
    public static void ReadyCaster(int casting = 30)
    {
        playerdat.play_level = 16;
        playerdat.INT = 30;
        playerdat.SetSkillValue(7, 30);      // mana
        playerdat.Casting = casting;
        playerdat.RecalculateHPManaMaxWeight(false);
        playerdat.play_mana = 100;
        SpellCasting.currentSpell = null;
        RunicMagic.PendingSpellCost = 0;
    }

    /// <summary>Selects the runes of spell index idx on the rune shelf, as the player's clicks do.</summary>
    public static RunicMagic SelectSpell(int idx)
    {
        var spell = RunicMagic.SpellList[idx];
        playerdat.SetSelectedRune(0, spell.RuneSequence >> 10);
        playerdat.SetSelectedRune(1, (spell.RuneSequence >> 5) & 0x1F);
        playerdat.SetSelectedRune(2, spell.RuneSequence & 0x1F);
        return spell;
    }

    /// <summary>
    /// Casts the selected runes the way the cast button does (RunicMagic.CastRunicSpell), first moving
    /// the clock on by 0x10000 so the delay after any earlier cast has passed.
    /// </summary>
    public static void CastSelected()
    {
        playerdat.ClockValue += 0x10000;
        RunicMagic.CastRunicSpell();
    }

    /// <summary>A square next to the player's, in one of the four directions, that is open and not solid, or the player's own.</summary>
    public static (int x, int y) OpenSquareNearPlayer()
    {
        int px = playerdat.playerObject.tileX, py = playerdat.playerObject.tileY;
        foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
        {
            int x = px + dx, y = py + dy;
            if (UWTileMap.ValidTile(x, y) && UWTileMap.current_tilemap.Tiles[x, y].tileType == UWTileMap.TILE_OPEN)
                return (x, y);
        }
        return (px, py);
    }
}
