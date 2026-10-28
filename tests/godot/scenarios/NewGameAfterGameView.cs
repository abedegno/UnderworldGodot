using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// The first view of a new game started after an earlier one in the same run is the new player's.
/// The original's camera object, UsPtr, is set to ThePlayer once at start-up (init_player,
/// uw1/src/game/PLAYER.C:133-136, uw2/src/game/PLAYER.C:124-129), and ThePlayer is the fixed object
/// slot critdata + 1 that each level's objects are loaded into, so get_eye always places the camera
/// at the player now on the map (uw1/src/motion/PLAYMOVE.C:604-615, uw2/src/motion/PLAYMOVE.C:643-654).
///
/// The port's playerdat.CameraReference holds the earlier game's player object until the end of
/// uimanager.JourneyOnwards (src/ui/uimanager_mainmenu.cs), but a new game's
/// Teleportation.InitialisePlayerOnLevelOrPositionChange positions the camera, with the line of
/// sight, before that. CameraReference is then not the new map's player object, so
/// PositionPlayerCamera takes it for another critter and puts the camera where the earlier game's
/// player stood, facing his way. FakeRender then marks the automap and pays exploration experience
/// from the new player's square but in the old player's facing (measured in UW1: 99 squares and 5
/// experience instead of 165 and 12). In play: die, go back to the main menu and create a new character.
///
/// The scenario goes back to the main menu as death does (uimanager.ReturnToMainMenu), puts the
/// earlier game's player object on an open square far from the start, then starts a new game the way
/// the runner does, and reads the camera's square and the new level 1 automap at once, before a
/// frame can move the camera again. A square counts as marked by the view when it differs from
/// level 1's automap as shipped, which can already have squares marked. The state guard does not
/// hide this: CameraReference is set by the scenario's own first game.
/// </summary>
public sealed class NewGameAfterGameView : Scenario
{
    public override string Name => "new-game-after-game-view";
    public override string Cites => "underworld-exhumed uw1/src/game/PLAYER.C:133-136, uw1/src/motion/PLAYMOVE.C:604-615; uw2/src/game/PLAYER.C:124-129, uw2/src/motion/PLAYMOVE.C:643-654";

    const int Radius = 3;
    string _problem;
    bool _staleBefore, _inGame;
    int _startX, _startY, _oldX = -1, _oldY = -1, _camX, _camY, _markedOld, _markedStart, _markedAll, _exp;

    static bool Open(UWTileMap map, int x, int y) =>
        UWTileMap.ValidTile(x, y) && map.Tiles[x, y].tileType == 1;

    /// <summary>
    /// Squares of level 1 within r of (x, y) that the new game's automap has marked and the level's
    /// automap as shipped (read again from LEV.ARK) does not: the squares the game's own view marked.
    /// </summary>
    static int Marked(int x, int y, int r = Radius)
    {
        var now = automap.automaps[0].buffer;
        var shipped = new automap(0).buffer;
        int n = 0;
        for (int i = x - r; i <= x + r; i++)
            for (int j = y - r; j <= y + r; j++)
                if (UWTileMap.ValidTile(i, j) && now[j * 64 + i] != shipped[j * 64 + i]) n++;
        return n;
    }

    public override Task Setup(ScenarioContext c)
    {
        var map = UWTileMap.current_tilemap;
        _startX = playerdat.playerObject.tileX;
        _startY = playerdat.playerObject.tileY;
        _staleBefore = playerdat.CameraReference == playerdat.playerObject;
        // An open square with open squares all round, as far from the start as there is.
        int best = 0;
        for (int x = 1; x < 63; x++)
            for (int y = 1; y < 63; y++)
            {
                bool open = true;
                for (int i = -1; i <= 1 && open; i++)
                    for (int j = -1; j <= 1 && open; j++)
                        open = Open(map, x + i, y + j);
                int d = System.Math.Max(System.Math.Abs(x - _startX), System.Math.Abs(y - _startY));
                if (open && d > best) { best = d; _oldX = x; _oldY = y; }
            }
        if (playerdat.dungeon_level != 1) _problem = $"the game started on level {playerdat.dungeon_level}, not 1";
        else if (!_staleBefore) _problem = "the first game's camera is not on its player object";
        else if (best < 20) _problem = $"no open square 20 or more from the start ({best})";
        return Task.CompletedTask;
    }

    public override async Task Act(ScenarioContext c)
    {
        if (_problem != null) return;
        uimanager.ReturnToMainMenu();
        await c.Frames(2);

        // The earlier game's player stands at (_oldX, _oldY), facing the middle of the map (heading 0 is north).
        var old = playerdat.CameraReference;
        var tile = UWTileMap.current_tilemap.Tiles[_oldX, _oldY];
        old.npc_xhome = (short)_oldX; old.npc_yhome = (short)_oldY;
        old.tileX = _oldX; old.tileY = _oldY;
        old.xpos = 4; old.ypos = 4;
        old.zpos = (short)(tile.floorHeight << 3);
        old.heading = (short)(_oldY >= 32 ? 4 : 0); old.npc_heading = 0;

        // A new game, as the runner starts one (ScenarioRunner.StartNewGame).
        uimanager.EnableDisable(uimanager.instance.PanelMainMenu, false);
        playerdat.InitEmptyPlayer();
        playerdat.CharName = "DEMO";
        playerdat.play_hp = 30;
        playerdat.STR = 20; playerdat.INT = 20; playerdat.DEX = 20;
        int standInMaxMana = playerdat.max_mana;
        playerdat.RecalculateHPManaMaxWeight(false);
        playerdat.max_hp = 30;
        playerdat.max_mana = standInMaxMana;
        uimanager.instance.JourneyOnwards("DATA");

        // Straight after JourneyOnwards: only its own camera placing has run.
        _inGame = uimanager.CurrentGameMode == uimanager.GameModes.GAME && playerdat.dungeon_level == 1;
        _camX = playerdat.CameraTileX; _camY = playerdat.CameraTileY;
        _markedOld = Marked(_oldX, _oldY);
        _markedStart = Marked(_startX, _startY);
        _markedAll = Marked(0, 0, 63);
        _exp = playerdat.Exp;
        c.Note($"earlier player put at ({_oldX}, {_oldY}); new game's camera square ({_camX}, {_camY}), start ({_startX}, {_startY}); automap squares the view marked within {Radius}: {_markedOld} by the earlier player's square, {_markedStart} by the start, {_markedAll} on the whole map; experience {_exp}");
        await c.Frames(2);
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        if (!_inGame) return "precondition: the second new game is not in game on level 1";
        string cite = c.Game == "uw1" ? "UsPtr = ThePlayer: uw1 PLAYER.C:136, get_eye uw1 PLAYMOVE.C:608" : "UsPtr = ThePlayer: uw2 PLAYER.C:129, get_eye uw2 PLAYMOVE.C:647";
        var k = new Checks();
        k.Equal("the new game's camera square x", _startX, _camX, cite);
        k.Equal("the new game's camera square y", _startY, _camY, cite);
        k.Equal($"automap squares the first view marked within {Radius} of where the earlier game's player stood", 0, _markedOld, cite);
        return k.Result;
    }
}
