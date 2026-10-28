using System.Threading.Tasks;
using Godot;

namespace Underworld.Scenarios;

/// <summary>
/// Altara's wand cuts a line of power within two squares (UW2). The original's
/// thump_your_magic_twanger_froggie runs check_Guardian_magic_marker on every object within two
/// squares of the player (gronk_area(ThePlayer, 1, ..., 2), uw2/src/combat/SPELLS2.C:804-813).
/// An invisible Guardian signet ring (item 0x35) with its doordir bit set is a marker: it is
/// removed and, if the world's line was not yet cut, bit (world - 1) of quest 128
/// (QB_LINES_OF_POWER) is set (SPELLS2.C:820-848).
///
/// The port's AltarasWand starts the y loop at cX - range instead of cY - range and tests
/// ValidTile(aY, aX) (src/magic/spellcasting_class_13.cs:160-162), so when the player's x is
/// more than 4 above y the box is empty and the marker is missed.
///
/// The scenario goes to level 9 (world 1, so the bit is 1 &lt;&lt; 0), and casts the wand's spell
/// through SpellCasting.CastClassD_Spells(0), the spell a use of the wand reaches, twice: with
/// the player's square set to (40, 20) and a marker at (42, 20), where x &gt; y + 4, and to
/// (20, 40) with a marker at (22, 40), where x &lt; y. Quest 128 is cleared before each.
/// </summary>
public sealed class AltarasWand : Scenario
{
    const int Ring = 0x35;   // ITEM_GUARDIAN_SIGNET_RING, uw2/src/include/items.h:189
    const int Level = 9;     // world (9 - 1) / 8 = 1
    const ulong TeleportMs = 20000;

    public override string Name => "altaras-wand-scan";
    public override string[] Games => new[] { "uw2" };
    public override string Cites => "underworld-exhumed uw2/src/combat/SPELLS2.C:807-848";

    record Case(int Px, int Py, int Rx, int Ry) { public bool Removed; public int Quest; }

    readonly Case[] _cases = { new(40, 20, 42, 20), new(20, 40, 22, 40) };
    string _problem;

    public override async Task Setup(ScenarioContext c)
    {
        Teleportation.Teleport(character: 0, tileX: 32, tileY: 32, newLevel: Level, HeadingHeightFlag: 0);
        ulong deadline = Time.GetTicksMsec() + TeleportMs;
        int stable = 0;
        while (Time.GetTicksMsec() < deadline && stable < 10)
        {
            await c.Frames(1);
            stable = (playerdat.dungeon_level == Level && !uimanager.WaitingForFade && uimanager.InGame) ? stable + 1 : 0;
        }
        if (playerdat.dungeon_level != Level) _problem = $"the teleport to level {Level} did not happen (on level {playerdat.dungeon_level})";
    }

    public override Task Act(ScenarioContext c)
    {
        if (_problem != null) return Task.CompletedTask;
        var p = playerdat.playerObject;
        int px = p.tileX, py = p.tileY;
        foreach (var k in _cases)
        {
            var ring = ObjectCreator.spawnObjectInTile(Ring, k.Rx, k.Ry, 3, 3, 0, RenderImmediately: false);
            if (ring == null) { _problem = "no signet ring could be made"; break; }
            ring.invis = 1;
            ring.doordir = 1;
            playerdat.SetQuest(128, 0);
            p.tileX = k.Px; p.tileY = k.Py;
            SpellCasting.CastClassD_Spells(0, p);
            p.tileX = px; p.tileY = py;
            k.Removed = !InChain(k.Rx, k.Ry, ring.index);
            k.Quest = playerdat.GetQuest(128);
            c.Note($"player at ({k.Px}, {k.Py}), marker at ({k.Rx}, {k.Ry}): marker removed {k.Removed}, quest 128 = {k.Quest}");
            if (!k.Removed) ObjectRemover_OLD.DeleteObjectFromTile_DEPRECIATED(k.Rx, k.Ry, ring.index);
        }
        return Task.CompletedTask;
    }

    static bool InChain(int x, int y, int index)
    {
        var objs = UWTileMap.current_tilemap.LevelObjects;
        int i = UWTileMap.current_tilemap.Tiles[x, y].indexObjectList;
        for (int guard = 0; i != 0 && guard < 1024; guard++)
        {
            if (i == index) return true;
            i = objs[i].next;
        }
        return false;
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        var chk = new Checks();
        foreach (var k in _cases)
        {
            string where = $"player ({k.Px}, {k.Py}), marker ({k.Rx}, {k.Ry})";
            chk.True(k.Removed, $"{where}: marker within two squares expected removed, still there (SPELLS2.C:807, 828, 835)");
            chk.Equal($"{where}: quest 128 (lines of power)", 1, k.Quest, "bit (world - 1) set: uw2 SPELLS2.C:827, 839");
        }
        return chk.Result;
    }
}
