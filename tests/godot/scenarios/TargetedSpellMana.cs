using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// A targeted spell's mana is paid when it is released, not when the runes are cast. The
/// original's player_cast keeps the cost of missile (class 5) and single-target area (class 7)
/// spells in mspell_mused instead of taking it (uw2/src/combat/RUNES.C:247-251); the target click
/// spends it only if the spell works (uw2/src/combat/SPELLS.C:773). So a class 7 spell that has
/// been cast but not yet aimed has cost nothing, and cancelling it costs nothing.
///
/// With a caster who passes every check (level 16, Casting 30: skill_check(30, 6) always
/// succeeds), the scenario selects the runes of a class 7 spell and casts them through the port's
/// RunicMagic.CastRunicSpell, the cast button. The spell must then be waiting for its target
/// (SpellCasting.currentSpell set) and the mana must be what it was.
///
/// The spell is then aimed through the port's own target path,
/// SpellCasting.CastCurrentSpellOnRayCastTarget, at a static object the scenario places on the
/// player's square. The spell is Detect Trap (class 7, minor 13), an object spell: obj_spells
/// handles it and pays mspell_mused at its end whatever the detection finds, with no waiver for
/// that minor (uw2/src/combat/SPELLS.C:861-866, 913-916). So the click must take exactly the
/// spell's cost, 3 * circle (uw2 RUNES.C:248), once, and leave no cost held.
///
/// Then a failed incantation, a skill_check result of 0 (fixed by a random source that always
/// gives 0, with Casting 3 * circle + 5: a score of 5), costs nothing and holds nothing: player_cast
/// leaves at fail_spell(2) before it sets mspell_mused (uw2 RUNES.C:238-239, 248). So the same
/// spell cast next from an object (SpellCasting.CastSpellFromObject, the wand and scroll path) and
/// aimed at the target costs the player nothing: obj_spells pays mspell_mused, which is 0
/// (uw2 SPELLS.C:915-916).
///
/// The port once took the mana at once: class 7 only sets the cursor but leaves applyCost true
/// (src/magic/spellcasting.cs:77-79, 108-112).
/// </summary>
public sealed class TargetedSpellMana : Scenario
{
    public override string Name => "targeted-spell-mana";
    public override string[] Games => new[] { "uw2" };
    public override string Cites => "underworld-exhumed uw2/src/combat/RUNES.C:247-251 (player_cast), uw2/src/combat/SPELLS.C:773, 806-918 (obj_spells)";

    string _problem;
    int _idx = -1, _before, _after, _afterTarget, _pendingAfter, _cost;
    bool _waiting, _released;
    int _failBefore, _failAfter, _failPending, _objAfterTarget;
    bool _failed, _objWaiting;

    sealed class ZeroSource : IRandSource
    {
        public int Rand() => 0;
        public int Next(int max) => 0;
    }

    public override Task Setup(ScenarioContext c)
    {
        for (int i = 0; i < 24 && _idx < 0; i++)   // circles 1 to 3, allowed in Britannia
            if (RunicMagic.SpellList[i]?.SpellMajorClass == 7 && RunicMagic.SpellList[i].SpellMinorClass == 13) _idx = i;
        if (_idx < 0) _problem = "no class 7 minor 13 (Detect Trap) spell of circle 1 to 3";
        BatchB.ReadyCaster();
        return Task.CompletedTask;
    }

    public override Task Act(ScenarioContext c)
    {
        if (_problem != null) return Task.CompletedTask;
        BatchB.SelectSpell(_idx);
        _before = playerdat.play_mana;
        BatchB.CastSelected();
        _after = playerdat.play_mana;
        _waiting = SpellCasting.currentSpell != null && SpellCasting.currentSpell.SpellMajorClass == 7;
        _cost = RunicMagic.SpellList[_idx].SpellLevel * 3;
        if (_waiting)
        {
            var target = BatchB.SpawnStatic(0xA0, playerdat.playerObject.tileX, playerdat.playerObject.tileY);
            if (target == null) { _problem = "no free static slot for the target"; return Task.CompletedTask; }
            SpellCasting.CastCurrentSpellOnRayCastTarget(target.index, UWTileMap.current_tilemap.LevelObjects, WorldObject: true);
            _afterTarget = playerdat.play_mana;
            _pendingAfter = RunicMagic.PendingSpellCost;
            _released = SpellCasting.currentSpell == null;
        }
        SpellCasting.currentSpell = null;
        RunicMagic.PendingSpellCost = 0;
        uimanager.instance.mousecursor.SetCursorToCursor();

        // A failed incantation, then the same spell from an object, aimed at a target.
        BatchB.ReadyCaster(casting: _cost + 5);
        BatchB.SelectSpell(_idx);
        _failBefore = playerdat.play_mana;
        var saved = Rng.Source;
        Rng.Source = new ZeroSource();
        try { BatchB.CastSelected(); } finally { Rng.Source = saved; }
        _failAfter = playerdat.play_mana;
        _failPending = RunicMagic.PendingSpellCost;
        _failed = SpellCasting.currentSpell == null;
        var wand = BatchB.SpawnStatic(0xA0, playerdat.playerObject.tileX, playerdat.playerObject.tileY);
        var target2 = BatchB.SpawnStatic(0xA0, playerdat.playerObject.tileX, playerdat.playerObject.tileY);
        if (wand == null || target2 == null) { _problem = "no free static slot for the object cast"; return Task.CompletedTask; }
        SpellCasting.CastSpellFromObject(_idx, wand);
        _objWaiting = SpellCasting.currentSpell != null && SpellCasting.currentSpell.SpellMajorClass == 7;
        if (_objWaiting)
            SpellCasting.CastCurrentSpellOnRayCastTarget(target2.index, UWTileMap.current_tilemap.LevelObjects, WorldObject: true);
        _objAfterTarget = playerdat.play_mana;
        SpellCasting.currentSpell = null;
        RunicMagic.PendingSpellCost = 0;
        uimanager.instance.mousecursor.SetCursorToCursor();
        c.Note($"failed incantation: mana {_failBefore} -> {_failAfter}, cost held {_failPending}; object cast waiting ({_objWaiting}), mana after its target click {_objAfterTarget}");
        c.Note($"class 7 spell {_idx} (circle {RunicMagic.SpellList[_idx].SpellLevel}): mana {_before} -> {_after} while waiting for a target ({_waiting}), {_afterTarget} after the target click, cost held after {_pendingAfter}");
        return Task.CompletedTask;
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        if (!_waiting) return "precondition: the class 7 spell is not waiting for a target after casting";
        var k = new Checks();
        k.Equal("mana spent on a targeted spell before its target is chosen", 0, _before - _after, "uw2 RUNES.C:247-251, the cost is held in mspell_mused");
        k.Equal("mana spent when the target is clicked", _cost, _after - _afterTarget, "uw2 SPELLS.C:913-916, obj_spells pays mspell_mused (3 * circle, RUNES.C:248)");
        k.Equal("cost still held after the target click", 0, _pendingAfter, "uw2 SPELLS.C:916, mspell_mused = 0");
        if (!_failed) return "precondition: the incantation with a skill roll of 0 did not fail; " + (k.Result ?? "");
        if (!_objWaiting) return "precondition: the class 7 spell cast from an object is not waiting for a target; " + (k.Result ?? "");
        k.Equal("mana spent on a failed incantation", 0, _failBefore - _failAfter, "uw2 RUNES.C:238-239, fail_spell(2)");
        k.Equal("cost held after a failed incantation", 0, _failPending, "uw2 RUNES.C:238-239, 248, mspell_mused is set only after the skill check");
        k.Equal("mana spent when an object's class 7 spell is aimed, after a failed incantation", 0, _failAfter - _objAfterTarget, "uw2 SPELLS.C:915-916, obj_spells pays mspell_mused, which is 0");
        if (!_released) return "precondition: the spell was still waiting after the target click; " + (k.Result ?? "");
        return k.Result;
    }
}
