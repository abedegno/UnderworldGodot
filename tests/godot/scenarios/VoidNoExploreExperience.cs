using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// No exploration experience in the Void (UW1). The original's process_grid gives the experience for squares
/// newly marked on the automap, pipeexp * PlayerLevel / 10, only when the player's level is 1 to 15 and the
/// dungeon level is not 9 (uw1/src/3d/GRIDDB.C:256-261). UW2's has no such exception.
///
/// The port's FakeRender gives it on every level (src/utility/visionlos.cs:299-318).
///
/// The scenario sets the player's level to 5, empties the automap of the level he is on, notes his experience
/// and positions the camera (playerdat.PositionPlayerCamera, which runs FakeRender), first on level 1, where some
/// experience must come (the control: the mechanism ran), then on level 9 after a move there (as void-automap
/// moves him), where none may come.
/// </summary>
public sealed class VoidNoExploreExperience : Scenario
{
    const int Void = 9;

    public override string Name => "void-no-explore-experience";
    public override string[] Games => new[] { "uw1" };
    public override string Cites => "underworld-exhumed uw1/src/3d/GRIDDB.C:256-261";

    int _level0, _inLevel, _gain1 = -1, _gain9 = -1;

    static void MoveTo(int level)
    {
        Teleportation.CodeToRunOnTeleport = () => { };
        Teleportation.Teleport(0, 0x3F, 0x3F, level, 0);
        Teleportation.HandleTeleportation();
    }

    static int GainHere()
    {
        var buffer = automap.automaps[playerdat.dungeon_level - 1].buffer;
        System.Array.Clear(buffer, 0, buffer.Length);
        playerdat.play_level = 5;
        playerdat.Exp = 1000;
        int before = playerdat.Exp;
        playerdat.PositionPlayerCamera(true);
        return playerdat.Exp - before;
    }

    public override Task Setup(ScenarioContext c)
    {
        _level0 = playerdat.dungeon_level;
        return Task.CompletedTask;
    }

    public override async Task Act(ScenarioContext c)
    {
        await c.Frames(2);
        _gain1 = GainHere();
        MoveTo(Void);
        _inLevel = playerdat.dungeon_level;
        await c.Frames(2);
        if (_inLevel == Void) _gain9 = GainHere();
        c.Note($"exploration experience on level {_level0}: {_gain1}; on level {_inLevel}: {_gain9}");
        MoveTo(_level0);
        await c.Frames(2);
    }

    public override string Assert(ScenarioContext c)
    {
        if (_level0 != 1) return $"precondition: the game started on level {_level0}, not 1";
        if (_gain1 <= 0) return $"precondition: no exploration experience on level 1 ({_gain1}), so the camera never ran the automap marking";
        if (_inLevel != Void) return $"precondition: the move to the Void left the player on level {_inLevel}";
        var k = new Checks();
        k.Equal("exploration experience on level 9", 0, _gain9, "process_grid gives none when PlayerLevel is 9: uw1 GRIDDB.C:257");
        return k.Result;
    }
}
