using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// Smite Undead harms only the undead. The original's sp_ward_undead damages a critter only when
/// check_res(target, 1, 0x80) returns 0, that is when its resist byte has bit 0x80 (the undead):
/// 255 damage of type 3, or half its hit points for race 0x17 (uw2/src/combat/SPELLS.C:331-347;
/// uw1/src/combat/SPELLS.C:344-353, where every undead takes 255). check_res with type 0x80 has
/// no magic bits, so it draws nothing: a set 0x80 bit returns 0, a clear one returns the damage
/// (uw1/src/combat/DAMAGE.C:166). In UW2 a floating skull (item 0x13) is not damaged but turned
/// into a plain skull, item 0xC3, by destroy_floatskull (uw2/src/event/WORLDEV.C:1649-1660).
///
/// The scenario spawns one undead critter (resist bit 0x80, not race 0x17) and one living critter
/// (bit clear) next to the player, and in UW2 a floating skull, and calls the port's
/// SpellCasting.SmiteUndead on each, the callback the spell and the undead slaying weapon share
/// (src/magic/spellcasting_class_7.cs, src/interaction/combat/combat_magic.cs). The undead must
/// be dead, the living critter untouched, the skull a plain skull.
///
/// The port once got all three wrong: in UW2 it tested a variable ScaleDamage never changes, so
/// every critter took 255 (spellcasting_class_7.cs:417-418); in UW1 every target went to the
/// SmiteUndeadObject stub and nothing was harmed (spellcasting_class_7.cs:407, 448); the skull
/// case was that stub too.
/// </summary>
public sealed class SmiteUndeadRule : Scenario
{
    const int FloatingSkull = 0x13, PlainSkull = 0xC3;   // uw2/src/include/items.h

    public override string Name => "smite-undead";
    public override string Cites => "underworld-exhumed uw2/src/combat/SPELLS.C:331-347 (sp_ward_undead), uw2/src/event/WORLDEV.C:1649-1660 (destroy_floatskull); uw1/src/combat/SPELLS.C:344-353";

    string _problem;
    uwObject _undead, _living, _skull;
    int _undeadId, _livingId, _undeadHpBefore, _livingHpBefore, _undeadHpAfter, _livingHpAfter, _skullIdAfter;

    public override Task Setup(ScenarioContext c)
    {
        _undeadId = BatchB.FindCritter(id => (commonObjDat.scaleresistances(id) & 0x80) != 0 && critterObjectDat.faction(id) != 0x17);
        _livingId = BatchB.FindCritter(id => (commonObjDat.scaleresistances(id) & 0x80) == 0 && critterObjectDat.faction(id) != 0x17);
        if (_undeadId < 0 || _livingId < 0) { _problem = $"no undead ({_undeadId}) or living ({_livingId}) critter type found"; return Task.CompletedTask; }
        var (x, y) = BatchB.OpenSquareNearPlayer();
        _undead = BatchB.SpawnCritter(_undeadId, x, y);
        _living = BatchB.SpawnCritter(_livingId, x, y);
        if (_undead == null || _living == null) { _problem = "the critters could not be made"; return Task.CompletedTask; }
        _undeadHpBefore = _undead.npc_hp;
        _livingHpBefore = _living.npc_hp;
        if (_undeadHpBefore == 0 || _livingHpBefore == 0) _problem = $"a critter starts with 0 hit points (undead {_undeadHpBefore}, living {_livingHpBefore})";
        if (c.Game == "uw2")
        {
            _skull = BatchB.SpawnStatic(FloatingSkull, x, y);
            if (_skull == null) _problem = "no floating skull could be made";
        }
        c.Note($"undead item 0x{_undeadId:X} hp {_undeadHpBefore}, living item 0x{_livingId:X} hp {_livingHpBefore}");
        return Task.CompletedTask;
    }

    public override Task Act(ScenarioContext c)
    {
        if (_problem != null) return Task.CompletedTask;
        var objs = UWTileMap.current_tilemap.LevelObjects;
        SpellCasting.SmiteUndead(_undead.index, objs, playerdat.playerObject);
        SpellCasting.SmiteUndead(_living.index, objs, playerdat.playerObject);
        _undeadHpAfter = _undead.npc_hp;
        _livingHpAfter = _living.npc_hp;
        if (_skull != null)
        {
            SpellCasting.SmiteUndead(_skull.index, objs, playerdat.playerObject);
            _skullIdAfter = _skull.item_id;
        }
        c.Note($"after Smite Undead: undead hp {_undeadHpAfter}, living hp {_livingHpAfter}{(_skull != null ? $", skull item 0x{_skullIdAfter:X}" : "")}");
        return Task.CompletedTask;
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        bool uw2 = c.Game == "uw2";
        string cite = uw2 ? "uw2 SPELLS.C:340-345" : "uw1 SPELLS.C:347-350";
        var k = new Checks();
        k.Equal($"undead (resist bit 0x80) hit points after Smite Undead", 0, _undeadHpAfter, cite + ", 255 damage");
        k.Equal($"living critter (no resist bit 0x80) hit points after Smite Undead", _livingHpBefore, _livingHpAfter, cite + ", check_res gives nonzero: no damage");
        if (uw2) k.Equal("floating skull's item id after Smite Undead", PlainSkull, _skullIdAfter, "uw2 SPELLS.C:336-337, WORLDEV.C:1651");
        return k.Result;
    }
}
