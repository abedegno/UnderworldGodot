using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// UW2's special deaths, death_check (uw2/src/event/WORLDEV.C:703). With mode set (the death has
/// happened): a critter of race 0xB makes every other active critter of that race hostile,
/// gronk_race(0xB, 1, 0, gronkify_attitude) (WORLDEV.C:707-708; gronkify_attitude sets the
/// attitude to its argument, uw2/src/event/SCDEVENT.C:424-430, and gronk_race skips loners,
/// SCDEVENT.C:42-60); the Bishop (whoami 6) sets quest 4 (WORLDEV.C:787-789); Dorstag (whoami
/// 0x63), when quest 23 is clear, adds 6 to the pit record QB_PIT_RECORD, quest 129
/// (WORLDEV.C:769-775, uw2/src/include/player.h:131). Every death that reaches the end raises the
/// best tally, xclock[XC_PIT_KILLS] (xclock 14, player.h:150), to the record when the record is
/// higher (WORLDEV.C:905-906), and never lowers either.
///
/// The scenario makes critters beside the player and kills them through the port's own death
/// path, npc.KillCritter (mode 0 then mode 1, src/npc/npcdeath.cs:659-662).
///
/// UW2 only. The port swaps SetRaceAttitude's arguments (src/npc/npcdeath.cs:128), sets quest 100
/// for the Bishop (npcdeath.cs:189), adds Dorstag's 6 to quest 137 (npcdeath.cs:351), and raises
/// the record to the tally instead of the tally to the record (npcdeath.cs:510).
/// </summary>
public sealed class SpecialDeathsUW2 : Scenario
{
    public override string Name => "special-deaths-uw2";
    public override string[] Games => new[] { "uw2" };
    public override string Cites => "underworld-exhumed uw2/src/event/WORLDEV.C:703-908 (death_check), uw2/src/event/SCDEVENT.C:42-60, 424-430; uw2/src/include/player.h:131, 150";

    const int PitRecord = 129, PitKills = 14;
    string _problem;
    int _kinAttitudeBefore, _kinAttitude;
    int _q4, _q100Before, _q100;
    int _dorstagRecord, _dorstagTally, _q137Before, _q137, _q121;
    int _highRecord, _highTally, _lowRecord, _lowTally;

    public override Task Setup(ScenarioContext c)
    {
        if (playerdat.IsFightingInPit) _problem = "the player is fighting in the pits";
        return Task.CompletedTask;
    }

    uwObject Make(int id, int whoami)
    {
        var o = BatchA.SpawnCritter(id, 0, 0);
        if (o != null) o.npc_whoami = (short)whoami;
        return o;
    }

    public override async Task Act(ScenarioContext c)
    {
        if (_problem != null) return;
        int plain = BatchA.PickCritter(i => BatchA.Walker(i) && critterObjectDat.faction(i) != 0xB && critterObjectDat.faction(i) != 0);
        int trilkhai = BatchA.PickCritter(i => critterObjectDat.avghit(i) > 0 && critterObjectDat.faction(i) == 0xB);
        if (plain < 0 || trilkhai < 0) { _problem = $"no creature types to use (plain {plain}, race 0xB {trilkhai})"; return; }

        // 1. A Trilkhai's death turns its kin hostile.
        var victim = Make(trilkhai, 0);
        var kin = Make(trilkhai, 0);
        if (victim == null || kin == null) { _problem = "no race 0xB critters could be made"; return; }
        kin.UnkBit_0XA_Bit7 = 0;      // not a loner
        _kinAttitudeBefore = kin.npc_attitude;
        npc.KillCritter(victim);
        _kinAttitude = kin.npc_attitude;
        c.Note($"race 0xB (item 0x{trilkhai:X}): kin attitude {_kinAttitudeBefore} -> {_kinAttitude}");

        // 2. The Bishop.
        playerdat.SetQuest(4, 0);
        _q100Before = playerdat.GetQuest(100);
        var bishop = Make(plain, 6);
        if (bishop == null) { _problem = "no critter for the Bishop"; return; }
        npc.KillCritter(bishop);
        _q4 = playerdat.GetQuest(4);
        _q100 = playerdat.GetQuest(100);

        // 3. Dorstag, record 2 and best tally 5, quest 23 clear.
        playerdat.SetQuest(23, 0);
        playerdat.SetQuest(PitRecord, 2);
        playerdat.SetXClock(PitKills, 5);
        _q137Before = playerdat.GetQuest(137);
        var dorstag = Make(plain, 0x63);
        if (dorstag == null) { _problem = "no critter for Dorstag"; return; }
        npc.KillCritter(dorstag);
        _dorstagRecord = playerdat.GetQuest(PitRecord);
        _dorstagTally = playerdat.GetXClock(PitKills);
        _q137 = playerdat.GetQuest(137);
        _q121 = playerdat.GetQuest(121);
        c.Note($"Dorstag: record 2 -> {_dorstagRecord}, best tally 5 -> {_dorstagTally}, quest 137 {_q137Before} -> {_q137}");

        // 4. Any death, record 9 above a tally of 4; then record 3 below a tally of 7.
        playerdat.SetQuest(PitRecord, 9);
        playerdat.SetXClock(PitKills, 4);
        var a = Make(plain, 0);
        if (a == null) { _problem = "no critter for the record"; return; }
        npc.KillCritter(a);
        _highRecord = playerdat.GetQuest(PitRecord);
        _highTally = playerdat.GetXClock(PitKills);
        playerdat.SetQuest(PitRecord, 3);
        playerdat.SetXClock(PitKills, 7);
        var b = Make(plain, 0);
        if (b == null) { _problem = "no critter for the record"; return; }
        npc.KillCritter(b);
        _lowRecord = playerdat.GetQuest(PitRecord);
        _lowTally = playerdat.GetXClock(PitKills);
        c.Note($"record 9 tally 4 -> {_highRecord}/{_highTally}; record 3 tally 7 -> {_lowRecord}/{_lowTally}");
        await c.Frames(1);
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        if (_kinAttitudeBefore == 0) return "precondition: the Trilkhai's kin was hostile before the death";
        var k = new Checks();
        k.Equal("race 0xB kin's attitude after a Trilkhai's death", 0, _kinAttitude, "uw2 WORLDEV.C:707-708, SCDEVENT.C:424-430");
        k.Equal("quest 4 after the Bishop's death", 1, _q4, "uw2 WORLDEV.C:787-789");
        k.Equal("quest 100 after the Bishop's death (untouched)", _q100Before, _q100, "uw2 WORLDEV.C:787-789");
        k.Equal("quest 121 after Dorstag's death", 1, _q121, "uw2 WORLDEV.C:771");
        k.Equal("pit record (quest 129) after Dorstag's death from 2", 8, _dorstagRecord, "uw2 WORLDEV.C:772-773");
        k.Equal("quest 137 after Dorstag's death (untouched)", _q137Before, _q137, "uw2 WORLDEV.C:769-775");
        k.Equal("best tally (xclock 14) after Dorstag's death from 5", 8, _dorstagTally, "uw2 WORLDEV.C:905-906");
        k.Equal("pit record 9 after a death, tally 4", 9, _highRecord, "uw2 WORLDEV.C:905-906");
        k.Equal("best tally 4 after a death, record 9", 9, _highTally, "uw2 WORLDEV.C:905-906");
        k.Equal("pit record 3 after a death, tally 7 (never lowered)", 3, _lowRecord, "uw2 WORLDEV.C:905-906");
        k.Equal("best tally 7 after a death, record 3", 7, _lowTally, "uw2 WORLDEV.C:905-906");
        return k.Result;
    }
}
