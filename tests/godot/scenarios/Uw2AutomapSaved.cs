using System.Threading.Tasks;
using Godot;

namespace Underworld.Scenarios;

/// <summary>
/// UW2: squares the player has mapped are written to the save and come back on restore
/// (issue #69).
///
/// The original: PlayersMap is the current level's map, a byte per tile, 0 for a tile never
/// seen (uw2/src/ui/AUTOMAP.C:6-13). SaveLevel writes it with SaveAutoMapLevel
/// (uw2/src/game/GAMEWRAP.C:110-135), which puts the 0x1000 bytes (MAP_TILES,
/// uw2/src/include/map.h:82) into block 0x9F + level of the save directory's LEV.ARK
/// (uw2/src/ui/AUTOMAP.C:191-203). DOS levels count from 1 (Map_Save writes the level map to
/// block level - 1, uw2/src/map/MAP.C:133), so the port's level n, counted from 0, has its map
/// in block 0xA0 + n. GetLevel reads it back with GetAutoMapLevel (GAMEWRAP.C:85-108,
/// AUTOMAP.C:205-219).
///
/// The scenario marks squares of level 1 that the map has never seen with the port's own
/// call for a seen square, saves to a slot in the runner's temporary game folder, reads that
/// slot's LEV.ARK to check the bytes are in block 0xA0 + level, clears the squares again,
/// restores the slot and checks the map has them.
/// </summary>
public sealed class Uw2AutomapSaved : Scenario
{
    const int Slot = 1;
    const int MapTiles = 0x1000;          // uw2/src/include/map.h:82
    const int FirstAutomapBlock = 0xA0;   // 0x9F + level, level counted from 1: AUTOMAP.C:199

    public override string Name => "uw2-automap-saved";
    public override string[] Games => new[] { "uw2" };
    public override string Cites => "underworld-exhumed uw2/src/ui/AUTOMAP.C:191-219 (SaveAutoMapLevel, GetAutoMapLevel), uw2/src/game/GAMEWRAP.C:85-135 (GetLevel, SaveLevel)";

    string _problem;
    int _level;
    readonly System.Collections.Generic.List<(int x, int y)> _squares = new();
    byte[] _mapAtSave;
    bool _saved, _restored;
    string _saveError;
    int _blockNo, _blockFlags, _blockLength, _blockDataLength = -1, _blockMismatches = -1;
    int _clearedBefore = -1, _markedAfter = -1, _mapMismatchesAfter = -1;

    public override Task Setup(ScenarioContext c)
    {
        if (!System.IO.Path.GetFullPath(UWClass.BasePath).StartsWith(System.IO.Path.GetFullPath(OS.GetEnvironment("HOME"))))
        { _problem = $"the game folder {UWClass.BasePath} is not inside the temporary home; refusing to save"; return Task.CompletedTask; }
        _level = playerdat.dungeon_level - 1;
        var map = automap.automaps?[_level];
        if (map?.buffer == null) { _problem = $"level {_level} has no automap loaded"; return Task.CompletedTask; }

        // Explore: four squares the map has never seen (byte 0) become seen open floor, as
        // the port's motion code marks a square the player sees.
        for (int y = 1; y < 63 && _squares.Count < 4; y += 7)
            for (int x = 1; x < 63 && _squares.Count < 4; x += 11)
                if (map.buffer[y * 64 + x] == 0)
                {
                    automap.MarkTileVisited(_level, x, y, automaptileinfo.TILE_OPEN);
                    _squares.Add((x, y));
                }
        if (_squares.Count < 4) { _problem = "fewer than four unseen squares on the level's map"; return Task.CompletedTask; }
        foreach (var (x, y) in _squares)
            if (map.buffer[y * 64 + x] == 0) { _problem = $"marking ({x}, {y}) seen did not change the map"; return Task.CompletedTask; }
        return Task.CompletedTask;
    }

    public override async Task Act(ScenarioContext c)
    {
        if (_problem != null) return;
        _mapAtSave = (byte[])automap.automaps[_level].buffer.Clone();
        try { SaveGame.Save(Slot, "automap"); _saved = true; }
        catch (System.Exception e) { _saveError = e.GetType().Name + ": " + e.Message.Split('\n')[0]; return; }
        await c.Frames(2);

        // The saved bytes, read from the slot's file, in DOS's block for this level.
        var ark = System.IO.File.ReadAllBytes(System.IO.Path.Combine(UWClass.BasePath, $"SAVE{Slot}", "LEV.ARK"));
        _blockNo = FirstAutomapBlock + _level;
        int count = (int)Loader.getAt(ark, 0, 32);
        _blockFlags = (int)Loader.getAt(ark, 6 + count * 4 + _blockNo * 4, 32);
        _blockLength = (int)Loader.getAt(ark, 6 + count * 8 + _blockNo * 4, 32);
        int offset = (int)Loader.getAt(ark, 6 + _blockNo * 4, 32);
        if (DataLoader.LoadUWBlock(ark, _blockNo, MapTiles, out UWBlock block) && block.Data != null)
        {
            // The length DOS reads: a compressed block (flags bit 1) starts with its unpacked
            // length as a long (uw2/src/sys/ACLZW.C:35-50, ac_unshrink_disk), else it is the recorded
            // length. The port's unpacker can run a few bytes past it, so that is not used.
            _blockDataLength = (_blockFlags & 2) != 0 ? (int)Loader.getAt(ark, offset, 32) : _blockLength;
            _blockMismatches = 0;
            for (int i = 0; i < MapTiles; i++)
                if (i >= block.Data.Length || block.Data[i] != _mapAtSave[i]) _blockMismatches++;
        }

        // Forget the squares again, then restore the slot.
        foreach (var (x, y) in _squares) automap.automaps[_level].buffer[y * 64 + x] = 0;
        _clearedBefore = 0;
        foreach (var (x, y) in _squares) if (automap.automaps[_level].buffer[y * 64 + x] == 0) _clearedBefore++;

        uimanager.instance.JourneyOnwards($"SAVE{Slot}");
        int stable = 0;
        for (int f = 0; f < 600 && stable < 20; f++)
        {
            await c.Frames(1);
            stable = (uimanager.InGame && !uimanager.WaitingForFade && uimanager.CurrentGameMode == uimanager.GameModes.GAME) ? stable + 1 : 0;
        }
        _restored = stable >= 20 && playerdat.dungeon_level - 1 == _level;
        var after = automap.automaps?[_level]?.buffer;
        if (after != null)
        {
            _markedAfter = 0;
            foreach (var (x, y) in _squares) if (after[y * 64 + x] == _mapAtSave[y * 64 + x]) _markedAfter++;
            _mapMismatchesAfter = 0;
            for (int i = 0; i < MapTiles; i++)
                if (i >= after.Length || after[i] != _mapAtSave[i]) _mapMismatchesAfter++;
        }
        c.Note($"level {_level}: marked {string.Join(" ", _squares)}; save block {_blockNo} flags {_blockFlags} length {_blockLength} unpacked {_blockDataLength}, {_blockMismatches} bytes differ from the map at save; after restore {_markedAfter}/{_squares.Count} squares marked, {_mapMismatchesAfter} bytes differ");
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        if (!_saved) return "the save threw " + _saveError;
        if (_clearedBefore != _squares.Count) return "precondition: the squares were not cleared between the save and the restore";
        if (!_restored) return "precondition: the game did not come back to play on the same level after the restore";
        var k = new Checks();
        k.Equal($"bytes in LEV.ARK block {_blockNo}", MapTiles, _blockDataLength, "SaveAutoMapLevel puts MAP_TILES bytes in block 0x9F + level: uw2 AUTOMAP.C:199, map.h:82");
        k.Equal($"bytes of block {_blockNo} that differ from the map at the save", 0, _blockMismatches, "SaveAutoMapLevel writes PlayersMap: uw2 AUTOMAP.C:199, GAMEWRAP.C:127");
        k.Equal("marked squares still marked after the restore", _squares.Count, _markedAfter, "GetLevel reads the map back with GetAutoMapLevel: uw2 GAMEWRAP.C:105, AUTOMAP.C:214");
        k.Equal("map bytes that differ after the restore", 0, _mapMismatchesAfter, "GetAutoMapLevel reads all MAP_TILES bytes: uw2 AUTOMAP.C:214");
        return k.Result;
    }
}
