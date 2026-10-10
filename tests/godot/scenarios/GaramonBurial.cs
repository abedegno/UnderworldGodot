using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace Underworld.Scenarios;

/// <summary>
/// Burying Garamon. In the original, using bones on something calls UseBonesOn
/// (uw1/src/obj/USEITEMS.C:327; bones 0xC2..0xC6 ask for a target, USEITEMS.C:396-399). On a
/// gravestone (item 0x165, USEITEMS.C:337), bones whose owner is 0x3E (USEITEMS.C:338) on the
/// grave that is a quantity with the special link 0x21 (USEITEMS.C:339) set talisman_ok and
/// garamon in the player record (USEITEMS.C:340-341) and change the grave's link to 0x222
/// (USEITEMS.C:342). Without talisman_ok no talisman can be destroyed
/// (uw1/src/motion/OBJPHYS.C:435), so the game cannot be won.
///
/// The scenario finds Garamon's grave the way UseBonesOn identifies it, from the shipped
/// DATA\LEV.ARK at run time (the one level whose object list holds such a grave, and its square),
/// teleports there through the game's own Teleportation.Teleport, finds the same grave among the
/// live level objects, puts a pile of bones (0xC6) with owner 0x3E in the player's hand, and uses
/// them on the grave as a player does: the "use" (use.Use), which must ask for a target as the
/// original's UseUtil does for bones 0xC2..0xC6 (uw1/src/obj/USEITEMS.C:396-399), then the click
/// on the grave, pickup.PickUp, which hands an item waiting for a target to useon.UseOn
/// (src/interaction/pickup.cs:235). Asking for the target is asserted, so a fix that buries
/// Garamon in useon but leaves the bones unusable still fails. When the use asks for nothing,
/// the scenario sets useon.CurrentItemBeingUsed itself, as every port item with a target does
/// (src/objects/lockpick.cs:10 and others), so the burial itself is still tested, and notes it.
///
/// UW1 only. Known failure today, two reasons: use.Use has no bones case, so no target is asked
/// for (src/interaction/use.cs:268, UseMajorClass3); and useon.UseOn has no bones case
/// (src/interaction/useon.cs:32), so nothing sets CanTalismansBeDestroyed true.
/// </summary>
public sealed class GaramonBurial : Scenario
{
    const int Gravestone = 0x165;     // ITEM_GRAVESTONE, uw1/src/include/items.h:526
    const int Bones = 0xC6;           // ITEM_PILE_OF_BONES_C6, uw1/src/include/items.h:352
    const int GaramonOwner = 0x3E;    // USEITEMS.C:338
    const ulong TeleportMs = 20000;

    public override string Name => "garamon-burial";
    public override string[] Games => new[] { "uw1" };
    public override string Cites => "underworld-exhumed uw1/src/obj/USEITEMS.C:327-363 (UseBonesOn), 396-399 (UseUtil)";

    string _problem;
    int _level, _gx, _gy;
    uwObject _grave, _bones;
    bool _askedForTarget, _useOnReached, _talismanOk, _garamon;
    int _graveLinkAfter;

    public override async Task Setup(ScenarioContext c)
    {
        // 1. Which level and square hold Garamon's grave, read from the level data.
        string ark = Path.Combine(UWClass.BasePath, "DATA", "LEV.ARK");
        if (!FindGraveInArk(ark, out _level, out _gx, out _gy, out string why)) { _problem = why; return; }
        c.Note($"Garamon's grave is on level {_level} at ({_gx}, {_gy}) in LEV.ARK");

        // 2. Go there the game's way.
        Teleportation.Teleport(character: 0, tileX: _gx, tileY: _gy, newLevel: _level, HeadingHeightFlag: 0);
        ulong deadline = Time.GetTicksMsec() + TeleportMs;
        int stable = 0;
        while (Time.GetTicksMsec() < deadline && stable < 10)
        {
            await c.Frames(1);
            stable = (playerdat.dungeon_level == _level && !uimanager.WaitingForFade && uimanager.InGame) ? stable + 1 : 0;
        }
        if (playerdat.dungeon_level != _level) { _problem = $"the teleport to level {_level} did not happen (on level {playerdat.dungeon_level})"; return; }

        // 3. The same grave among the live objects, identified as UseBonesOn does.
        var graves = UWTileMap.current_tilemap.LevelObjects
            .Where(o => o != null && o.item_id == Gravestone && o.is_quant == 1 && (o.link & 0x200) != 0 && (o.link & 0x1FF) == 0x21)
            .ToList();
        _grave = graves.FirstOrDefault(o => o.tileX == _gx && o.tileY == _gy);
        if (_grave == null) { _problem = $"no gravestone with special link 0x21 at ({_gx}, {_gy}) among level {_level}'s live objects ({graves.Count} elsewhere)"; return; }
        if (playerdat.playerObject.tileX != _gx || playerdat.playerObject.tileY != _gy)
        { _problem = $"the player is at ({playerdat.playerObject.tileX}, {playerdat.playerObject.tileY}), not at the grave"; return; }

        // 4. Garamon's bones in hand.
        int slot = ObjectCreator.SpawnObjectInHand(Bones);
        if (slot == 0) { _problem = "no pile of bones could be made"; return; }
        _bones = UWTileMap.current_tilemap.LevelObjects[slot];
        _bones.owner = (short)GaramonOwner;
        if (playerdat.ObjectInHand != slot || _bones.item_id != Bones || _bones.owner != GaramonOwner)
        { _problem = "the bones in hand are not item 0xC6 with owner 0x3E"; return; }

        if (playerdat.CanTalismansBeDestroyed || playerdat.IsGaramonBuried)
            _problem = "talisman_ok or garamon was already set before the burial";
    }

    public override async Task Act(ScenarioContext c)
    {
        if (_problem != null) return;
        useon.CurrentItemBeingUsed = null;
        use.Use(_bones, playerdat.playerObject, UWTileMap.current_tilemap.LevelObjects, WorldObject: true);
        _askedForTarget = useon.CurrentItemBeingUsed != null && useon.CurrentItemBeingUsed.index == _bones.index;
        if (!_askedForTarget)
        {
            c.Note("using the bones asked for no target (use.Use has no bones case); setting useon.CurrentItemBeingUsed as a port item with a target does");
            useon.CurrentItemBeingUsed = new useon(_bones, true);
        }
        // The click on the grave: PickUp passes it to the item waiting for a target.
        var objects = UWTileMap.current_tilemap.LevelObjects;
        pickup.PickUp(_grave.index, objects, WorldObject: true);
        _useOnReached = useon.CurrentItemBeingUsed == null;   // UseOn clears it as it finishes
        await c.Frames(2);
        _talismanOk = playerdat.CanTalismansBeDestroyed;
        _garamon = playerdat.IsGaramonBuried;
        _graveLinkAfter = _grave.link;
        c.Note($"after use-on: talisman_ok {_talismanOk}, garamon {_garamon}, grave link 0x{_graveLinkAfter:X}");
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        if (!_useOnReached) return "precondition: the click on the grave (pickup.PickUp) did not reach the use-on (useon.CurrentItemBeingUsed still set)";
        var k = new Checks();
        k.True(_askedForTarget, "using the bones expected to ask for a target, asked for none (USEITEMS.C:396-399)");
        k.True(_talismanOk, "talisman_ok (CanTalismansBeDestroyed) expected set after Garamon's bones (0xC6, owner 0x3E) were used on his grave (0x165, link 0x221), got clear (USEITEMS.C:340)");
        k.True(_garamon, "garamon (IsGaramonBuried) expected set, got clear (USEITEMS.C:341)");
        k.Equal("grave link", 0x222, _graveLinkAfter, "USEITEMS.C:342");
        return k.Result;
    }

    /// <summary>
    /// Scans LEV.ARK's nine level blocks (UW1: 64x64 tiles of 4 bytes, then 256 mobile objects
    /// of 27 bytes and 768 static of 8) for the object UseBonesOn accepts as Garamon's grave, and
    /// the square whose object chain holds it. Exactly one must exist.
    /// </summary>
    static bool FindGraveInArk(string path, out int level, out int x, out int y, out string why)
    {
        level = x = y = 0;
        if (!File.Exists(path)) { why = $"{path} not found"; return false; }
        byte[] d = File.ReadAllBytes(path);
        int U16(int o) => d[o] | d[o + 1] << 8;
        uint U32(int o) => (uint)(d[o] | d[o + 1] << 8 | d[o + 2] << 16 | d[o + 3] << 24);
        int found = 0;
        for (int lev = 0; lev < 9; lev++)
        {
            int b = (int)U32(2 + 4 * lev);
            if (b == 0) continue;
            int Obj(int i) => b + 0x4000 + (i < 256 ? i * 27 : 256 * 27 + (i - 256) * 8);
            for (int ty = 0; ty < 64; ty++)
            for (int tx = 0; tx < 64; tx++)
            {
                int i = (int)(U32(b + (ty * 64 + tx) * 4) >> 22) & 0x3FF;
                for (int guard = 0; i != 0 && guard < 1024; guard++)
                {
                    int o = Obj(i);
                    int w0 = U16(o), w3 = U16(o + 6);
                    int item = w0 & 0x1FF, isq = w0 >> 15, link = w3 >> 6;
                    if (item == Gravestone && isq == 1 && (link & 0x200) != 0 && (link & 0x1FF) == 0x21)
                    {
                        found++;
                        level = lev + 1; x = tx; y = ty;
                    }
                    i = U16(o + 4) >> 6;
                }
            }
        }
        why = found == 1 ? null : $"{found} graves in LEV.ARK match UseBonesOn's test, not 1";
        return found == 1;
    }
}
