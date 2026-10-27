using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// Garamon dying takes back the right to destroy the talismans. In the original, death_check
/// (SpecialDeathCases) for whoami 0x1B with mode set, the death having happened, clears
/// talisman_ok (uw1/src/event/WORLDEV.C:557-560), the bit UseBonesOn set when he was buried
/// (uw1/src/obj/USEITEMS.C:340) and that a talisman's destruction needs
/// (uw1/src/motion/OBJPHYS.C:435). talisman_ok is bit 2 of player byte 0x62
/// (uw1/src/include/player.h:68), the port's CanTalismansBeDestroyed (bit 2 of pdat 0x63, the
/// port's offsets being DOS plus one, src/player/playerdatquest.cs:461). Nothing else changes: the
/// quest byte 37 the port uses for the dreams (pdat 0x6F) is left alone.
///
/// The scenario sets talisman_ok and the dream bit (quest 37 bit 2), makes a critter with whoami
/// 0x1B beside the player and kills it through the port's own death path, npc.KillCritter, which
/// calls SpecialDeathCases with mode 0 and then mode 1 (src/npc/npcdeath.cs:659-662, as the death
/// animation's end does with mode 1, src/npc/npcai.cs:369).
///
/// UW1 only. The port once cleared bit 2 of quest 37 instead (src/npc/npcdeath.cs, the 0x1B case),
/// so talisman_ok stayed set and the dream bit was lost.
/// </summary>
public sealed class GaramonDeathClearsTalismans : Scenario
{
    const int Garamon = 0x1B;       // WORLDEV.C:558
    const int DreamQuest = 37;
    const int DreamBit = 4;         // quest 37 bit 2, the bit the port cleared

    public override string Name => "garamon-death-clears-talismans";
    public override string[] Games => new[] { "uw1" };
    public override string Cites => "underworld-exhumed uw1/src/event/WORLDEV.C:557-560 (death_check), uw1/src/include/player.h:68";

    string _problem;
    bool _okBefore, _okAfter;
    int _questBefore, _questAfter;

    public override Task Setup(ScenarioContext c)
    {
        playerdat.CanTalismansBeDestroyed = true;
        playerdat.SetQuest(DreamQuest, playerdat.GetQuest(DreamQuest) | DreamBit);
        _okBefore = playerdat.CanTalismansBeDestroyed;
        _questBefore = playerdat.GetQuest(DreamQuest);
        if (!_okBefore) _problem = "talisman_ok did not read back set";
        else if ((_questBefore & DreamBit) == 0) _problem = "quest 37 bit 2 did not read back set";
        return Task.CompletedTask;
    }

    public override async Task Act(ScenarioContext c)
    {
        if (_problem != null) return;
        var p = playerdat.playerObject;
        var garamon = ObjectCreator.spawnObjectInTile(
            itemid: 0x7E, tileX: p.tileX, tileY: p.tileY, xpos: 0, ypos: 0, zpos: p.zpos,
            WhichList: ObjectFreeLists.ObjectListType.MobileList, RenderImmediately: false);
        if (garamon == null) { _problem = "no critter could be made"; return; }
        garamon.npc_whoami = Garamon;
        if (garamon.npc_whoami != Garamon) { _problem = "the critter's whoami did not read back 0x1B"; return; }
        npc.KillCritter(garamon);
        await c.Frames(2);
        _okAfter = playerdat.CanTalismansBeDestroyed;
        _questAfter = playerdat.GetQuest(DreamQuest);
        c.Note($"after Garamon's death: talisman_ok {_okAfter}, quest 37 0x{_questAfter:X} (was 0x{_questBefore:X})");
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        var k = new Checks();
        k.True(!_okAfter, "talisman_ok (CanTalismansBeDestroyed) expected clear after Garamon (whoami 0x1B) died, got set (WORLDEV.C:559)");
        k.Equal("quest 37 (the dream bits), untouched by Garamon's death", _questBefore, _questAfter, "WORLDEV.C:557-560 changes only talisman_ok");
        return k.Result;
    }
}
