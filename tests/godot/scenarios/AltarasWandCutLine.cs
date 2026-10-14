using System.Threading.Tasks;
using Godot;

namespace Underworld.Scenarios;

/// <summary>
/// Altara's wand on a line of power already cut (UW2). check_Guardian_magic_marker clears the
/// marker's doordir bit, and when the world's bit of quest 128 (QB_LINES_OF_POWER) is already set
/// it puts no effect but still removes and frees the marker, and returns 1 without setting
/// area_spell_state (uw2/src/combat/SPELLS2.C:829-836). The scan takes one marker
/// (gronk_area count 1, SPELLS2.C:807) and, with area_spell_state 0, the spell prints 0x142,
/// "The spell has no noticeable effect." (SPELLS2.C:808-812).
///
/// The port cleared doordir and left the ring where it was (src/magic/spellcasting_class_13.cs).
///
/// The scenario goes to level 9 (world 1, bit 1 &lt;&lt; 0), sets quest 128 to 1, puts a marker two
/// squares from the player at (20, 40) and casts the wand's spell through
/// SpellCasting.CastClassD_Spells(0). (The square is one the port's scan reaches, so this does
/// not depend on the separate fault altaras-wand-scan tests.)
/// </summary>
public sealed class AltarasWandCutLine : Scenario
{
    const int Ring = 0x35;   // ITEM_GUARDIAN_SIGNET_RING, uw2/src/include/items.h:189
    const int Level = 9;
    const int Px = 20, Py = 40, Rx = 22, Ry = 40;
    const ulong TeleportMs = 20000;

    public override string Name => "altaras-wand-cut-line";
    public override string[] Games => new[] { "uw2" };
    public override string Cites => "underworld-exhumed uw2/src/combat/SPELLS2.C:804-836";

    bool _removed, _doordir;
    int _quest, _quest14Before, _quest14;
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
        var ring = ObjectCreator.spawnObjectInTile(Ring, Rx, Ry, 3, 3, 0, RenderImmediately: false);
        if (ring == null) { _problem = "no signet ring could be made"; return Task.CompletedTask; }
        ring.invis = 1;
        ring.doordir = 1;
        playerdat.SetQuest(128, 1);
        _quest14Before = playerdat.GetQuest(14);
        p.tileX = Px; p.tileY = Py;
        SpellCasting.CastClassD_Spells(0, p);
        p.tileX = px; p.tileY = py;
        _removed = !InChain(Rx, Ry, ring.index);
        _doordir = ring.doordir == 1;
        _quest = playerdat.GetQuest(128);
        _quest14 = playerdat.GetQuest(14);
        c.Note($"marker removed {_removed}, doordir {ring.doordir}, quest 128 = {_quest}");
        if (!_removed) ObjectRemover_OLD.DeleteObjectFromTile_DEPRECIATED(Rx, Ry, ring.index);
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
        chk.True(!_doordir, "the marker's doordir bit expected cleared, still set (SPELLS2.C:828); the scan never reached it");
        chk.True(_removed, "marker on a line already cut expected removed and freed, still in its square (SPELLS2.C:833-834)");
        chk.Equal("quest 128 (lines of power)", 1, _quest, "unchanged when the line is already cut: uw2 SPELLS2.C:830-836");
        chk.Equal("quest 14", _quest14Before, _quest14, "unchanged: uw2 SPELLS2.C:835-837");
        return chk.Result;
    }
}
