using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// The automap in the Void (UW1). On arriving at level 9 the original's do_level_hacks keeps the automap switch
/// aside in player->saved_mana and turns it off; on leaving it puts it back (uw1/src/game/GAMEWRAP.C:472-477).
///
/// Before this fix the port's LevelChangeEventsUW1 (src/utility/teleportation.cs) kept the switch aside in
/// backup_mana but left the automap on in the Void.
///
/// With the automap on, the player is moved to level 9 and back to level 1, each time with the stand-in square 0x3F
/// and an empty callback, which parks him with no search (Teleportation.HandleTeleportation, called directly).
/// </summary>
public sealed class VoidAutomap : Scenario
{
    const int Void = 9;

    public override string Name => "void-automap";
    public override string[] Games => new[] { "uw1" };
    public override string Cites => "underworld-exhumed uw1/src/game/GAMEWRAP.C:472-477";

    int _level0, _inLevel, _outLevel, _saved;
    bool _before, _inVoid, _after;

    static void MoveTo(int level)
    {
        Teleportation.CodeToRunOnTeleport = () => { };
        Teleportation.Teleport(0, 0x3F, 0x3F, level, 0);
        Teleportation.HandleTeleportation();
    }

    public override Task Setup(ScenarioContext c)
    {
        _level0 = playerdat.dungeon_level;
        playerdat.AutomapEnabled = true;
        _before = playerdat.AutomapEnabled;
        return Task.CompletedTask;
    }

    public override async Task Act(ScenarioContext c)
    {
        MoveTo(Void);
        _inLevel = playerdat.dungeon_level;
        _inVoid = playerdat.AutomapEnabled;
        _saved = playerdat.backup_mana;
        await c.Frames(2);
        MoveTo(_level0);
        _outLevel = playerdat.dungeon_level;
        _after = playerdat.AutomapEnabled;
        c.Note($"automap {_before}; in the Void (level {_inLevel}) {_inVoid}, kept aside {_saved}; back on level {_outLevel} {_after}");
        await c.Frames(2);
    }

    public override string Assert(ScenarioContext c)
    {
        if (!_before) return "precondition: the automap was not on before the Void";
        if (_inLevel != Void) return $"precondition: the move to the Void left the player on level {_inLevel}";
        if (_outLevel != _level0) return $"precondition: the move back left the player on level {_outLevel}";
        var k = new Checks();
        k.Equal("automap switch kept aside on arriving in the Void", 1, _saved, "player->saved_mana = automap: uw1 GAMEWRAP.C:474");
        k.True(!_inVoid, "automap expected off in the Void (uw1 GAMEWRAP.C:475)");
        k.True(_after, "automap expected back on after leaving the Void (uw1 GAMEWRAP.C:476-477)");
        return k.Result;
    }
}
