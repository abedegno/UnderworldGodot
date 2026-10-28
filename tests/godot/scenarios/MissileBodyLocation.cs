using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// Where a missile strikes a critter's body. missile_thwack (uw1/src/combat/COMBAT.C:761-782,
/// uw2/src/combat/COMBAT.C:861-884) sets the blow's height, hitz, to OBJ_Z(missile) plus half the
/// missile's height, and its location to pickloc(OBJ_Z(def), OBJ_Z(def) + height(def),
/// OBJ_Z(missile), OBJ_Z(missile) + height(missile)) + 4: the missile's bottom and top, and 4 more
/// than a blow's location. pickloc (uw1 :107-126, uw2 :98-117) returns 3, the top location, when
/// the middle of the missile is above the defender's top less one.
///
/// do_damage then reads the armour at hitloc % 4 (uw1 :364, uw2 :401), and after damage is done,
/// a location of 4 or more becomes 4 with hitz_tab[4] set to -hitz (uw1 :384-387, uw2 :421-424),
/// so a bleeding critter's blood is put at the missile's own height, hitz (put_effect, uw1 :403,
/// uw2 :440; a negative height is a height in the world, as SpawnAnimoAtTarget takes it).
///
/// The scenario puts a bleeding critter whose top armour (location 3) is at least 2, sets it at
/// height 0, and an arrow (item 0x12) whose bottom is level with the critter's top, then calls the
/// port's missile_thwack, combat.MissileAttackHit, for the player's missile. First with damage 1,
/// which rolls at most 2 and so leaves nothing after the armour: the location is then left as
/// missile_thwack set it, 7 (3 + 4). Then with damage 60: the blood that appears must be at hitz.
///
/// The port passed the missile's height alone as its top, dropped the + 4 and never set hitz or
/// hitz_tab[4] for a missile (src/interaction/combat/combat_missile.cs:96-100).
/// </summary>
public sealed class MissileBodyLocation : Scenario
{
    const int Arrow = 0x12;

    public override string Name => "missile-body-location";
    public override string Cites => "underworld-exhumed uw1/src/combat/COMBAT.C:107-126 (pickloc), 333-422 (do_damage), 761-782 (missile_thwack); uw2/src/combat/COMBAT.C:98-117, 358-459, 861-884";

    string _problem;
    uwObject _critter, _arrow;
    int _hitz;
    readonly List<int> _locations = new();
    int _bloodZ = int.MinValue, _bloodCount;

    public override Task Setup(ScenarioContext c)
    {
        int id = BatchA.PickCritter(i => BatchA.Walker(i) && critterObjectDat.bleed(i) != 0 && critterObjectDat.toughness(i, 3) >= 2);
        if (id < 0) { _problem = "no bleeding creature with top armour of 2 or more"; return Task.CompletedTask; }
        _critter = BatchA.SpawnCritter(id, 0, 0);
        if (_critter == null) { _problem = "no critter could be made"; return Task.CompletedTask; }
        _critter.zpos = 0;
        int top = commonObjDat.height(id);
        var p = playerdat.playerObject;
        _arrow = ObjectCreator.spawnObjectInTile(itemid: Arrow, tileX: p.tileX, tileY: p.tileY, xpos: 3, ypos: 3, zpos: (short)top,
            WhichList: ObjectFreeLists.ObjectListType.StaticList, RenderImmediately: false);
        if (_arrow == null) { _problem = "no arrow could be made"; return Task.CompletedTask; }
        _arrow.zpos = (short)top;
        _hitz = top + commonObjDat.height(Arrow) / 2;   // OBJ_Z(missile) + height / 2
        if (_hitz > 127) { _problem = $"hitz {_hitz} would not fit the original's signed char"; return Task.CompletedTask; }
        c.Note($"critter 0x{id:X} at height 0, top {top}, armour {critterObjectDat.toughness(id, 0)}/{critterObjectDat.toughness(id, 1)}/{critterObjectDat.toughness(id, 2)}/{critterObjectDat.toughness(id, 3)}; arrow at {top}, height {commonObjDat.height(Arrow)}");
        return Task.CompletedTask;
    }

    void Thwack(int damage)
    {
        var p = playerdat.playerObject;
        combat.BodyPartHit = -1;
        BatchA.Call(typeof(combat), "MissileAttackHit", 1, _arrow, _critter, p.tileX, p.tileY, damage, (byte)4);
    }

    static HashSet<uwObject> Blood() =>
        UWTileMap.current_tilemap.LevelObjects.Where(o => o != null && o.item_id == 448 && o.tileX < 64).ToHashSet();

    public override Task Act(ScenarioContext c)
    {
        if (_problem != null) return Task.CompletedTask;
        for (int i = 0; i < 8; i++)
        {
            Thwack(1);
            _locations.Add(combat.BodyPartHit);
        }
        var before = Blood();
        _critter.npc_hp = 200;
        Thwack(60);
        var fresh = Blood().Where(o => !before.Contains(o)).ToList();
        _bloodCount = fresh.Count;
        if (fresh.Count > 0) _bloodZ = fresh[0].zpos;
        c.Note($"locations with no damage done: [{string.Join(", ", _locations)}]; then blood {_bloodCount}, at height {(_bloodCount > 0 ? _bloodZ.ToString() : "-")}, location {combat.BodyPartHit}");
        return Task.CompletedTask;
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        if (_bloodCount == 0) return "precondition: the damaging missile made no blood";
        var k = new Checks();
        k.True(_locations.All(l => l == 7), $"location of a missile whose middle is above the critter's top expected 7 (pickloc 3, plus 4) every time, got [{string.Join(", ", _locations)}] (COMBAT.C missile_thwack, uw1 :772-773, uw2 :872-873)");
        k.Equal("location after the damage is done", 4, combat.BodyPartHit, "uw1 COMBAT.C:384-387, uw2 :421-424");
        k.Equal("height of the blood", _hitz, _bloodZ, "hitz_tab[4] = -hitz, put_effect (uw1 COMBAT.C:768, 385, 403; uw2 :868, 422, 440)");
        return k.Result;
    }
}
