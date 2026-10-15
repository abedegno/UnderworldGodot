using System.Threading.Tasks;
using Godot;

namespace Underworld.Scenarios;

/// <summary>
/// Guard: saving to a slot and restoring it brings back the player's square, hit points, an
/// inventory object and a quest bit, after all four have been changed in between.
///
/// The original: SaveGame writes PLAYER.DAT (the player record, with position, hit points and
/// quest bits, and the inventory, SavePlayerInv) and the level into SAVE0 and copies it to SAVEn;
/// RestoreGame copies SAVEn back, resets the player and reads PLAYER.DAT and the inventory
/// (RestorePlayerInv) and the saved level (uw1/src/game/GAMEWRAP.C:237-311, the file comment
/// lines 8-16; uw1/src/inv/INVSAVE.C; uw2/src/game/GAMEWRAP.C, the same pair). Whatever was
/// changed after the save is gone after the restore.
///
/// The port's calls are the ones its menus make: SaveGame.Save(slot, description) on the save
/// description's Enter (src/ui/uimanager_options.cs, OnSaveDescriptionSubmitted) and
/// JourneyOnwards("SAVE1") for the restore menu. The runner gives the game a temporary game
/// folder with empty save slots, so this writes only there (README, "Your settings are never
/// touched").
/// </summary>
public sealed class GuardSaveLoad : Scenario
{
    const int Dagger = 0x03;
    const int Slot = 1;

    public override string Name => "guard-save-load";
    public override string Cites => "underworld-exhumed uw1/src/game/GAMEWRAP.C:237-311 (RestoreGame, SaveGame), uw1/src/inv/INVSAVE.C; uw2/src/game/GAMEWRAP.C";

    string _problem;
    int _quest, _hpSaved, _savedX, _savedY, _otherX, _otherY, _bagSlot, _level;
    int _hpAfter, _xAfter, _yAfter, _questAfter, _itemAfter = -1, _levelAfter;
    int _hpChanged, _xChanged, _yChanged, _questChanged, _itemChanged = -1;
    bool _saved, _restored;
    string _saveError;

    public override async Task Setup(ScenarioContext c)
    {
        if (!System.IO.Path.GetFullPath(UWClass.BasePath).StartsWith(System.IO.Path.GetFullPath(OS.GetEnvironment("HOME"))))
        { _problem = $"the game folder {UWClass.BasePath} is not inside the temporary home; refusing to save"; return; }
        _quest = c.Game == "uw1" ? 31 : 120;
        if (playerdat.GetQuest(_quest) != 0) { _problem = $"quest {_quest} is already set in a new game"; return; }

        // Two squares near the start: where to save, and where to be before restoring.
        var p = playerdat.playerObject;
        (_savedX, _savedY) = OpenSquareNear(p.tileX, p.tileY, 8, -1, -1);
        if (_savedX < 0) { _problem = "no open square near the start to save in"; return; }
        (_otherX, _otherY) = OpenSquareNear(_savedX, _savedY, 8, _savedX, _savedY);
        if (_otherX < 0) { _problem = "no second open square near the start"; return; }
        if (!await Guards.TeleportHere(c, _savedX, _savedY)) { _problem = $"the move to ({_savedX}, {_savedY}) did not happen"; return; }

        int index;
        (index, _bagSlot) = Guards.PutInBackpack(Dagger);
        if (_bagSlot == -1) { _problem = "could not put a dagger in the backpack"; return; }
        playerdat.play_hp = 23;
        playerdat.SetQuest(_quest, 1);
        _hpSaved = playerdat.play_hp;
        _level = playerdat.dungeon_level;
        if (_hpSaved != 23 || playerdat.GetQuest(_quest) != 1) _problem = "the hit points or the quest bit did not take";
    }

    /// <summary>An open square of the same floor height within r of (x, y), not (nx, ny), or (-1, -1).</summary>
    static (int, int) OpenSquareNear(int x, int y, int r, int nx, int ny)
    {
        var map = UWTileMap.current_tilemap;
        for (int d = 1; d <= r; d++)
            for (int dy = -d; dy <= d; dy++)
                for (int dx = -d; dx <= d; dx++)
                {
                    int tx = x + dx, ty = y + dy;
                    if ((tx == nx && ty == ny) || !UWTileMap.ValidTile(tx, ty)) continue;
                    var t = map.Tiles[tx, ty];
                    if (t.tileType == UWTileMap.TILE_OPEN && t.floorHeight == map.Tiles[x, y].floorHeight && t.indexObjectList == 0)
                        return (tx, ty);
                }
        return (-1, -1);
    }

    public override async Task Act(ScenarioContext c)
    {
        if (_problem != null) return;
        try { SaveGame.Save(Slot, "guard"); _saved = true; }
        catch (System.Exception e) { _saveError = e.GetType().Name + ": " + e.Message.Split('\n')[0]; return; }
        await c.Frames(2);

        // Change everything that was saved.
        if (!await Guards.TeleportHere(c, _otherX, _otherY)) { _problem = $"the move to ({_otherX}, {_otherY}) did not happen"; return; }
        var dagger = playerdat.GetInventorySlotObject(_bagSlot);
        if (dagger != null)
        {
            playerdat.RemoveFromInventory(dagger.index);
            playerdat.SetInventorySlotListHead(_bagSlot, 0);
            uimanager.UpdateInventoryDisplay();
        }
        playerdat.play_hp = 11;
        playerdat.SetQuest(_quest, 0);
        _hpChanged = playerdat.play_hp; _questChanged = playerdat.GetQuest(_quest);
        _xChanged = playerdat.playerObject.tileX; _yChanged = playerdat.playerObject.tileY;
        _itemChanged = playerdat.GetInventorySlotObject(_bagSlot)?.item_id ?? -1;

        // The restore menu's call.
        uimanager.instance.JourneyOnwards($"SAVE{Slot}");
        int stable = 0;
        for (int f = 0; f < 600 && stable < 20; f++)
        {
            await c.Frames(1);
            stable = (uimanager.InGame && !uimanager.WaitingForFade && uimanager.CurrentGameMode == uimanager.GameModes.GAME) ? stable + 1 : 0;
        }
        _restored = stable >= 20;
        _hpAfter = playerdat.play_hp; _questAfter = playerdat.GetQuest(_quest);
        _xAfter = playerdat.playerObject.tileX; _yAfter = playerdat.playerObject.tileY;
        _itemAfter = playerdat.GetInventorySlotObject(_bagSlot)?.item_id ?? -1;
        _levelAfter = playerdat.dungeon_level;
        c.Note($"saved at ({_savedX}, {_savedY}) hp {_hpSaved} quest {_quest}=1 dagger in slot {_bagSlot}; changed to ({_xChanged}, {_yChanged}) hp {_hpChanged} quest {_questChanged} slot item {_itemChanged}; restored: level {_levelAfter} ({_xAfter}, {_yAfter}) hp {_hpAfter} quest {_questAfter} slot item {_itemAfter}");
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        if (!_saved) return "the save threw " + _saveError;
        if (_hpChanged == _hpSaved || _questChanged != 0 || _itemChanged != -1 || (_xChanged == _savedX && _yChanged == _savedY))
            return "precondition: the state was not changed between the save and the restore";
        if (!_restored) return "precondition: the game did not come back to play after the restore";
        var k = new Checks();
        k.Equal("level after the restore", _level, _levelAfter, "RestoreGame loads the saved level: uw1 GAMEWRAP.C:237-268");
        k.Equal("square x after the restore", _savedX, _xAfter, "the player record's position: uw1 GAMEWRAP.C:237-268");
        k.Equal("square y after the restore", _savedY, _yAfter, "the player record's position: uw1 GAMEWRAP.C:237-268");
        k.Equal("hit points after the restore", _hpSaved, _hpAfter, "PLAYER.DAT read back: uw1 GAMEWRAP.C:237-268");
        k.Equal($"quest {_quest} after the restore", 1, _questAfter, "PLAYER.DAT's quest bits: uw1 GAMEWRAP.C:237-268");
        k.Equal($"backpack slot {_bagSlot} item after the restore", Dagger, _itemAfter, "RestorePlayerInv: uw1 GAMEWRAP.C:237-268, INVSAVE.C");
        return k.Result;
    }
}
