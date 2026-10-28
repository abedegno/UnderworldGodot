using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// No exploration experience at character level 0 (UW1 and UW2). The original's process_grid gives the experience
/// for squares newly marked on the automap only when player->level, the character's level, is 1 to 15
/// (uw1/src/3d/GRIDDB.C:257, uw2/src/3d/GRIDDB.C:255).
///
/// The port's FakeRender tested play_level >= 0, so it gave it at level 0 too (src/utility/visionlos.cs:303).
///
/// The scenario empties the automap of the level the player is on and positions the camera (playerdat.
/// PositionPlayerCamera, which runs FakeRender), first at character level 5, where some experience must come (the
/// control), then at level 0, where none may come.
/// </summary>
public sealed class ExploreExperienceLevelZero : Scenario
{
    public override string Name => "explore-experience-level-zero";
    public override string Cites => "underworld-exhumed uw1/src/3d/GRIDDB.C:257; uw2/src/3d/GRIDDB.C:255";

    int _keepLevel, _gain5 = -1, _gain0 = -1;

    static int GainAt(int level)
    {
        var buffer = automap.automaps[playerdat.dungeon_level - 1].buffer;
        System.Array.Clear(buffer, 0, buffer.Length);
        playerdat.play_level = level;
        playerdat.Exp = 1000;
        int before = playerdat.Exp;
        playerdat.PositionPlayerCamera(true);
        return playerdat.Exp - before;
    }

    public override Task Setup(ScenarioContext c)
    {
        _keepLevel = playerdat.play_level;
        return Task.CompletedTask;
    }

    public override async Task Act(ScenarioContext c)
    {
        await c.Frames(2);
        _gain5 = GainAt(5);
        _gain0 = GainAt(0);
        c.Note($"exploration experience at character level 5: {_gain5}; at level 0: {_gain0}");
        playerdat.play_level = _keepLevel;
        await c.Frames(1);
    }

    public override string Assert(ScenarioContext c)
    {
        if (_gain5 <= 0) return $"precondition: no exploration experience at character level 5 ({_gain5}), so the camera never ran the automap marking";
        var k = new Checks();
        k.Equal("exploration experience at character level 0", 0, _gain0, c.Game == "uw1" ? "process_grid: uw1 GRIDDB.C:257" : "process_grid: uw2 GRIDDB.C:255");
        return k.Result;
    }
}
