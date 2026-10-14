using System.Collections.Generic;
using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// Which critters the conversation builtin set_race_attitude reaches. The original
/// (uw1/src/conv/BABLHACK.C:66-99, uw2/src/conv/BABLHACK.C:344-377) sets the attitude of every
/// critter of the talker's item id and the given race that is not a loner, on the squares from
/// home - range to home + range, clipped to 1..63, with the range used as given: range 0 is the
/// talker's own square and a negative range is no square at all.
///
/// The scenario calls the port's ConversationVM.set_race_attitude directly with the arguments on the
/// conversation stack, and compares the attitude of every object on the map with a model of the
/// original. Cases:
/// - range 0, a critter of the same kind on the next square: only the talker's square changes;
/// - range -1: nothing changes;
/// - range 1 for a talker on column 1, with a critter of the same kind on column 0: column 0 is
///   left alone (x0 is clipped to 1, uw1 BABLHACK.C:79-81).
///
/// Both games.
/// </summary>
public sealed class RaceAttitudeRange : Scenario
{
    public override string Name => "race-attitude-range";
    public override string Cites => "underworld-exhumed uw1/src/conv/BABLHACK.C:66-99 (set_race_attitude); uw2/src/conv/BABLHACK.C:344-377";

    string _problem;
    readonly List<string> _fails = new();
    readonly List<string> _notes = new();
    int _id, _race;
    uwObject _talker, _next, _edgeTalker, _edge;

    public override Task Setup(ScenarioContext c)
    {
        var p = playerdat.playerObject;
        int cx = p.npc_xhome, cy = p.npc_yhome;
        if (cx < 1 || cx > 61 || cy < 1 || cy > 59) { _problem = $"the player's square ({cx},{cy}) is too near the map's edge"; return Task.CompletedTask; }
        _id = BatchB.FindCritter(i => true);
        if (_id < 0) { _problem = "no critter type"; return Task.CompletedTask; }
        _race = critterObjectDat.faction(_id);
        _talker = BatchB.SpawnCritter(_id, cx, cy + 3);
        _next = BatchB.SpawnCritter(_id, cx + 1, cy + 3);
        _edgeTalker = BatchB.SpawnCritter(_id, 1, cy);
        _edge = BatchB.SpawnCritter(_id, 0, cy);
        if (_talker == null || _next == null || _edgeTalker == null || _edge == null) { _problem = "the critters could not be made"; return Task.CompletedTask; }
        foreach (var o in new[] { _talker, _next, _edgeTalker, _edge }) o.UnkBit_0XA_Bit7 = 0;   // not loners
        return Task.CompletedTask;
    }

    static Dictionary<int, int> Attitudes()
    {
        var map = UWTileMap.current_tilemap;
        var a = new Dictionary<int, int>();
        for (int x = 0; x < 64; x++)
            for (int y = 0; y < 64; y++)
                for (int i = map.Tiles[x, y].indexObjectList; i != 0; i = map.LevelObjects[i].next)
                    if (map.LevelObjects[i].majorclass == 1) a[i] = map.LevelObjects[i].npc_attitude;
        return a;
    }

    /// <summary>set_race_attitude, uw1/src/conv/BABLHACK.C:66-99, uw2/src/conv/BABLHACK.C:344-377.</summary>
    static Dictionary<int, int> Model(Dictionary<int, int> before, uwObject talker, int range, int att, int race)
    {
        var map = UWTileMap.current_tilemap;
        var a = new Dictionary<int, int>(before);
        int x0 = talker.npc_xhome - range; if (x0 < 1) x0 = 1;
        int y0 = talker.npc_yhome - range; if (y0 < 1) y0 = 1;
        int x1 = talker.npc_xhome + range; if (x1 >= 64) x1 = 0x3F;
        int y1 = talker.npc_yhome + range; if (y1 >= 64) y1 = 0x3F;
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                for (int i = map.Tiles[x, y].indexObjectList; i != 0; i = map.LevelObjects[i].next)
                {
                    var o = map.LevelObjects[i];
                    if (o.item_id == talker.item_id && o.UnkBit_0XA_Bit7 == 0 && critterObjectDat.faction(o.item_id) == race)
                        a[i] = att & 3;
                }
        return a;
    }

    void Run(string what, uwObject talker, int range, int att, int minChanged, string cite)
    {
        foreach (var o in new[] { _talker, _next, _edgeTalker, _edge }) o.npc_attitude = (short)(att == 1 ? 2 : 1);
        var before = Attitudes();
        var expected = Model(before, talker, range, att, _race);
        ConversationVM.StackValues = new short[64];
        ConversationVM.stack = 0;
        ConversationVM.stackptr = 4;
        ConversationVM.Set(3, 10); ConversationVM.Set(10, (short)range);   // args[-1], the range
        ConversationVM.Set(2, 11); ConversationVM.Set(11, (short)att);     // args[-2], the attitude
        ConversationVM.Set(1, 12); ConversationVM.Set(12, (short)_race);   // args[-3], the race
        ConversationVM.set_race_attitude(talker);
        var actual = Attitudes();
        int changed = 0;
        var wrong = new List<string>();
        foreach (var kv in expected)
        {
            if (kv.Value != before[kv.Key]) changed++;
            int got = actual.GetValueOrDefault(kv.Key, -1);
            if (got != kv.Value) wrong.Add($"#{kv.Key} at ({UWTileMap.current_tilemap.LevelObjects[kv.Key].tileX},{UWTileMap.current_tilemap.LevelObjects[kv.Key].tileY}) {got} for {kv.Value}");
        }
        _notes.Add($"{what}: DOS changes {changed}");
        if (changed < minChanged && _problem == null) _problem = $"{what}: the model changes only {changed}, too few for a test";
        if (wrong.Count > 0) _fails.Add($"{what}: attitude differs from set_race_attitude: {string.Join(", ", wrong)} ({cite})");
    }

    public override Task Act(ScenarioContext c)
    {
        if (_problem != null) return Task.CompletedTask;
        var savedValues = ConversationVM.StackValues; int savedStack = ConversationVM.stack, savedPtr = ConversationVM.stackptr;
        try
        {
            Run("range 0", _talker, 0, 3, 1, "uw1 BABLHACK.C:79-90, uw2 BABLHACK.C:357-368 (range as given)");
            Run("range -1", _talker, -1, 3, 0, "uw1 BABLHACK.C:79-90, uw2 BABLHACK.C:357-368 (range as given)");
            Run("range 1 by column 0", _edgeTalker, 1, 1, 1, "uw1 BABLHACK.C:79-81, uw2 BABLHACK.C:357-359 (x0 clipped to 1)");
        }
        finally { ConversationVM.StackValues = savedValues; ConversationVM.stack = savedStack; ConversationVM.stackptr = savedPtr; }
        c.Note($"critter 0x{_id:X} race {_race}; " + string.Join("; ", _notes));
        return Task.CompletedTask;
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        return _fails.Count == 0 ? null : string.Join("; ", _fails);
    }
}
