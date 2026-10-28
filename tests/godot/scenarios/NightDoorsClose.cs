using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// Doors closing in the night (UW1 and UW2). The original's DoClosingDoors, run when the player goes to sleep and by
/// UW2's SCD hack 4, closes each open door not on a square with door bit 1 when (rand() * 10) / 0x8000 < 3, a chance
/// of 3 in 10 (uw1/src/event/TRIGGER.C:684-686, uw2/src/event/TRIGGER.C:1062-1064).
///
/// The scenario opens the plain door nearest the player and lets it settle, then runs the port's FindAndCloseDoors
/// (not as the player, as sleep does) with rand() fixed first at 0x2668, where (0x2668 * 10) / 0x8000 is 3 and the
/// door must stay open, then at 0x2666, where it is 2 and the door must close.
///
/// The port closed a door one time in two (Rng.r.Next(2) == 0, src/scd/functions/scd_closedoors.cs:38).
/// </summary>
public sealed class NightDoorsClose : Scenario
{
    public override string Name => "night-doors-close";
    public override string Cites => "underworld-exhumed uw1/src/event/TRIGGER.C:684-686; uw2/src/event/TRIGGER.C:1062-1064";

    sealed class FixedRand : IRandSource
    {
        readonly int _v;
        public FixedRand(int v) { _v = v; }
        public int Rand() => _v;
        public int Next(int max) => max <= 0 ? 0 : _v % max;
    }

    string _problem;
    uwObject _door;
    bool? _openAfterHigh, _openAfterLow;

    public override Task Setup(ScenarioContext c)
    {
        var p = playerdat.playerObject;
        _door = DoorTools.NearestPlainDoor(p.tileX, p.tileY);
        if (_door == null) { _problem = "no plain door on the level"; return Task.CompletedTask; }
        if (UWTileMap.current_tilemap.Tiles[_door.tileX, _door.tileY].doorBit != 0) _problem = "the nearest plain door is on a square with door bit 1";
        return Task.CompletedTask;
    }

    async Task<bool?> CloseWith(ScenarioContext c, int rand)
    {
        var keep = Rng.Source;
        try
        {
            Rng.Source = new FixedRand(rand);
            scd.FindAndCloseDoors(arg0_isPlayer: false);
        }
        finally { Rng.Source = keep; }
        if (!await DoorTools.Settle(c, _door)) return null;
        return door.isOpen(_door);
    }

    public override async Task Act(ScenarioContext c)
    {
        if (_problem != null) return;
        door.OpenDoor(_door);
        if (!await DoorTools.Settle(c, _door) || !door.isOpen(_door)) { _problem = "the door did not open"; return; }
        _openAfterHigh = await CloseWith(c, 0x2668);
        if (_openAfterHigh == true) _openAfterLow = await CloseWith(c, 0x2666);
        c.Note($"door {_door.index} at ({_door.tileX}, {_door.tileY}): open after rand 0x2668 {_openAfterHigh}, after rand 0x2666 {_openAfterLow}");
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        string cite = c.Game == "uw1" ? "DoClosingDoors: uw1 TRIGGER.C:686" : "DoClosingDoors: uw2 TRIGGER.C:1064";
        var k = new Checks();
        k.True(_openAfterHigh == true, $"rand 0x2668 (3 of 10): expected the door to stay open, was {(_openAfterHigh == null ? "still moving" : "closed")} ({cite})");
        if (_openAfterHigh == true)
            k.True(_openAfterLow == false, $"rand 0x2666 (2 of 10): expected the door to close, was {(_openAfterLow == null ? "still moving" : "open")} ({cite})");
        return k.Result;
    }
}
