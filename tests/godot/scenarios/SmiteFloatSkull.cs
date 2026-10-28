using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// Smite Undead on a floating skull (UW2 only). The original's sp_ward_undead hands item 0x13 to
/// destroy_floatskull (uw2/src/combat/SPELLS.C:336-337), which turns it into a plain skull, item
/// 0xC3, sets its quality to 0 when can_place says a plain skull does not fit where it is (flier 1,
/// range 0), and frees whatever is linked to it when it is not a quantity (Obj_FreeChain on its
/// link) (uw2/src/event/WORLDEV.C:1649-1660). UW1 has no destroy_floatskull: its sp_ward_undead
/// has no skull case (uw1/src/combat/SPELLS.C:344-353).
///
/// The scenario makes two floating skulls of quality 40. One stands on an open square next to the
/// player and holds a linked object; the other is put on a solid square near the player, where the port's fit test,
/// motion.TestIfObjectFitsInTile (can_place), says a plain skull does not fit. It calls the port's
/// SpellCasting.SmiteUndead on each. Both must be plain skulls; the first keeps its quality and
/// loses its linked object, which is freed; the second has quality 0.
///
/// The port only changed the item id (SmiteUndeadObject, src/magic/spellcasting_class_7.cs),
/// leaving the quality and the linked object alone.
/// </summary>
public sealed class SmiteFloatSkull : Scenario
{
    const int FloatingSkull = 0x13, PlainSkull = 0xC3;   // uw2/src/include/items.h
    const int StartQuality = 40;

    public override string Name => "smite-floatskull";
    public override string[] Games => new[] { "uw2" };
    public override string Cites => "underworld-exhumed uw2/src/event/WORLDEV.C:1649-1660 (destroy_floatskull), uw2/src/motion/COLLIDE.C:564-623 (can_place), uw2/src/obj/OBJECTS.C:316-330 (Obj_FreeChain)";

    string _problem;
    uwObject _fits, _blocked, _content;
    int _contentIndex;
    int _fitsId, _fitsQuality, _fitsLink, _blockedId, _blockedQuality, _contentIdAfter;

    static bool PlainSkullFits(uwObject o) => motion.TestIfObjectFitsInTile(
        PlainSkull, o.index, (o.tileX << 3) + o.xpos, (o.tileY << 3) + o.ypos, o.zpos, 1, 0);

    static uwObject Skull(int x, int y)
    {
        var o = BatchB.SpawnStatic(FloatingSkull, x, y);
        if (o == null) return null;
        o.xpos = 3;
        o.ypos = 3;
        o.quality = StartQuality;
        return o;
    }

    public override Task Setup(ScenarioContext c)
    {
        var (x, y) = BatchB.OpenSquareNearPlayer();
        _fits = Skull(x, y);
        int slot = ObjectCreator.PrepareNewObject(PlainSkull, ObjectFreeLists.ObjectListType.StaticList);
        if (_fits == null || slot == 0) { _problem = "the skull or its linked object could not be made"; return Task.CompletedTask; }
        _content = UWTileMap.current_tilemap.LevelObjects[slot];
        _contentIndex = _content.index;
        _fits.is_quant = 0;
        _fits.link = (short)_contentIndex;

        // a square near the player where a plain skull does not fit: a solid one
        int px = playerdat.playerObject.tileX, py = playerdat.playerObject.tileY;
        for (int r = 1; r <= 8 && _blocked == null; r++)
            for (int bx = px - r; bx <= px + r && _blocked == null; bx++)
                for (int by = py - r; by <= py + r && _blocked == null; by++)
                    if (UWTileMap.ValidTile(bx, by) && UWTileMap.current_tilemap.Tiles[bx, by].tileType == UWTileMap.TILE_SOLID)
                        _blocked = Skull(bx, by);
        if (_blocked == null) { _problem = "no solid square near the player for the second skull"; return Task.CompletedTask; }
        if (!PlainSkullFits(_fits)) _problem = "a plain skull does not fit on the open square";
        else if (PlainSkullFits(_blocked)) _problem = "a plain skull fits on the solid square";
        c.Note($"skull {_fits.index} at {x},{y} holding {_contentIndex}; skull {_blocked.index} at {_blocked.tileX},{_blocked.tileY}");
        return Task.CompletedTask;
    }

    public override Task Act(ScenarioContext c)
    {
        if (_problem != null) return Task.CompletedTask;
        var objs = UWTileMap.current_tilemap.LevelObjects;
        SpellCasting.SmiteUndead(_fits.index, objs, playerdat.playerObject);
        SpellCasting.SmiteUndead(_blocked.index, objs, playerdat.playerObject);
        _fitsId = _fits.item_id;
        _fitsQuality = _fits.quality;
        _fitsLink = _fits.link;
        _blockedId = _blocked.item_id;
        _blockedQuality = _blocked.quality;
        _contentIdAfter = objs[_contentIndex].item_id;
        c.Note($"after: skull 1 item 0x{_fitsId:X} quality {_fitsQuality} link {_fitsLink}, linked object item 0x{_contentIdAfter:X}; skull 2 item 0x{_blockedId:X} quality {_blockedQuality}");
        return Task.CompletedTask;
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        var k = new Checks();
        k.Equal("skull where a plain skull fits: item id", PlainSkull, _fitsId, "uw2 WORLDEV.C:1651");
        k.Equal("skull where a plain skull fits: quality", StartQuality, _fitsQuality, "uw2 WORLDEV.C:1652-1656, can_place passes");
        k.Equal("skull's link after Smite Undead", 0, _fitsLink, "uw2 WORLDEV.C:1657-1658, Obj_FreeChain");
        k.Equal("linked object's item id after Smite Undead (freed)", 0, _contentIdAfter, "uw2 WORLDEV.C:1657-1658, OBJECTS.C:316-330");
        k.Equal("skull where a plain skull does not fit: item id", PlainSkull, _blockedId, "uw2 WORLDEV.C:1651");
        k.Equal("skull where a plain skull does not fit: quality", 0, _blockedQuality, "uw2 WORLDEV.C:1652-1656, can_place fails");
        return k.Result;
    }
}
