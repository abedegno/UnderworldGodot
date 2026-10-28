using System.Collections.Generic;
using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// A random source that returns the values it is given, then 0, and counts its draws. The mushroom
/// scenario uses it to choose the skill check's result and the mana roll.
/// </summary>
sealed class ScriptedRand : IRandSource
{
    readonly Queue<int> _values;
    public int Draws { get; private set; }
    public ScriptedRand(params int[] values) { _values = new Queue<int>(values); }
    public int Rand() { Draws++; return _values.Count > 0 ? _values.Dequeue() : 0; }
    public int Next(int max) => max <= 0 ? 0 : Rand() % max;
}

/// <summary>
/// Raising a skill never fills the mana flask. The only recompute after a raise is
/// player_compute(0), which sets the maxima and does not touch mana (restore 0: uw1
/// game/SKILLS.C:102-115, uw2 SKILLS.C:74-85).
///
/// UW1: a shrine mantra calls player_compute(0) once the mantra is handled (mantra_advance,
/// uw1 SKILLS.C:347). A trainer's x_skills either calls get_skill (value 10000) or writes the
/// skill (0 to 30), and neither recomputes anything (uw1 conv/BABLHACK.C:103-113, get_skill
/// uw1 SKILLS.C:189-218), so the maximum stays as it was until the next player_compute.
///
/// UW2: x_skills above 10000 spends a skill point through grant_skill_advance, which ends with
/// player_compute(0) (uw2 conv/BABLHACK.C:414-415, uw2 SKILLS.C:220); 10000 calls get_skill and
/// 0 to 30 writes the skill, neither recomputing (uw2 BABLHACK.C:424-431).
///
/// The scenario starts each part from Intelligence 20, mana skill 3 (maximum 10) and no mana.
/// Both games run x_skills on the mana skill with 15 and then 10000; UW2 then runs it with 10001.
/// UW1 then chants the mana skill's own mantra and the magic group's mantra at a shrine.
/// </summary>
public sealed class SkillAdvanceMana : Scenario
{
    const int Int = 20, SkillStart = 3, ManaSkillNo = 7;
    const int MantraFirst = 51, GroupMagicMantra = 24;   // string block 2's mantras from 0x33; the group mantras are 23 to 25 (uw1 SKILLS.C:303-325)

    public override string Name => "skill-advance-mana";
    public override string Cites => "underworld-exhumed uw1/src/conv/BABLHACK.C:103-113, uw1/src/game/SKILLS.C:347; uw2/src/conv/BABLHACK.C:402-433, uw2/src/game/SKILLS.C:220";

    string _problem;
    readonly List<(string what, int skill, int max, int mana, int points)> _after = new();

    static void Reset()
    {
        playerdat.INT = Int;
        playerdat.ManaSkill = SkillStart;
        playerdat.RecalculateHPManaMaxWeight(false);
        playerdat.play_mana = 0;
    }

    void Record(string what) => _after.Add((what, playerdat.ManaSkill, playerdat.max_mana, playerdat.play_mana, playerdat.SkillPoints));

    static void XSkills(int skill, int value)
    {
        var savedValues = ConversationVM.StackValues; int savedStack = ConversationVM.stack, savedPtr = ConversationVM.stackptr;
        try
        {
            ConversationVM.StackValues = new short[64];
            ConversationVM.stack = 0;
            ConversationVM.stackptr = 3;
            ConversationVM.Set(2, 10); ConversationVM.Set(10, value);
            ConversationVM.Set(1, 11); ConversationVM.Set(11, skill);
            ConversationVM.x_skills();
        }
        finally
        {
            ConversationVM.StackValues = savedValues; ConversationVM.stack = savedStack; ConversationVM.stackptr = savedPtr;
        }
    }

    public override Task Setup(ScenarioContext c)
    {
        Reset();
        if (playerdat.max_mana != (SkillStart + 1) * Int >> 3) _problem = $"the starting maximum is {playerdat.max_mana}";
        return Task.CompletedTask;
    }

    async Task Chant(ScenarioContext c, int mantra)
    {
        shrine.Use(playerdat.playerObject);
        await c.Frames(1);
        uimanager.instance.TypedInput.Text = GameStrings.GetString(2, MantraFirst + mantra);
        MessageDisplay.WaitingForTypedInput = false;
        await c.Frames(3);
    }

    public override async Task Act(ScenarioContext c)
    {
        if (_problem != null) return;
        XSkills(ManaSkillNo, 15);
        Record("x_skills(mana, 15)");
        XSkills(ManaSkillNo, 10000);
        Record("x_skills(mana, 10000)");
        if (c.Game == "uw2")
        {
            Reset();
            playerdat.SkillPoints = 1;
            XSkills(ManaSkillNo, 10001);
            Record("x_skills(mana, 10001)");
        }
        else
        {
            Reset();
            playerdat.SkillPoints = 1;
            await Chant(c, ManaSkillNo);
            Record("the mana mantra");
            Reset();
            playerdat.SkillPoints = 1;
            await Chant(c, GroupMagicMantra);
            Record("the magic group mantra");
        }
        foreach (var a in _after) c.Note($"{a.what}: mana skill {a.skill}, mana {a.mana}/{a.max}, skill points {a.points}");
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        var k = new Checks();
        int start = (SkillStart + 1) * Int >> 3;
        string bh = c.Game == "uw2" ? "uw2 BABLHACK.C:424-431" : "uw1 BABLHACK.C:109-112";
        foreach (var a in _after)
        {
            if (a.what.StartsWith("x_skills(mana, 15)"))
            {
                if (a.skill != 15) return $"precondition: {a.what} left the mana skill at {a.skill}";
                k.Equal($"maximum after {a.what}", start, a.max, "the skill is written with no recompute: " + bh);
            }
            else if (a.what.StartsWith("x_skills(mana, 10000)"))
            {
                if (a.skill <= 15) return $"precondition: {a.what} did not raise the mana skill ({a.skill})";
                k.Equal($"maximum after {a.what}", start, a.max, "get_skill with no recompute: " + bh + ", uw1 SKILLS.C:189-218");
            }
            else
            {
                if (a.points != 0) return $"precondition: {a.what} spent no skill point ({a.points} left)";
                k.Equal($"maximum after {a.what}", (a.skill + 1) * Int >> 3, a.max, c.Game == "uw2" ? "player_compute(0): uw2 SKILLS.C:220, 74-85" : "player_compute(0): uw1 SKILLS.C:347, 102-115");
            }
            k.Equal($"mana after {a.what}", 0, a.mana, c.Game == "uw2" ? "no restore: uw2 SKILLS.C:82-83" : "no restore: uw1 SKILLS.C:113-114");
        }
        return k.Result;
    }
}

/// <summary>
/// A mushroom restores a flat 0 to 2 mana on any skill check but a near miss. Both games' UseFood:
/// if (skill_check(Intelligence, 20)) restore_mana(ThePlayer, -(rand() * 3 / 0x8000))
/// (uw1 obj/USEITEMS.C:621-623, uw2 USEITEMS.C:221-223). skill_check returns -1, 0, 1 or 2 and
/// only 0 is false (uw1 game/SKILLCHK.C:48-61); restore_mana with an amount not above 0 adds
/// -amount, capped at the maximum (uw1 combat/SPELLS.C:168-181, uw2 SPELLS.C:155-170).
///
/// The scenario sets Intelligence 20 and mana skill 29 (maximum 75), no mana, and eats three
/// mushrooms from the backpack with the random source scripted: skill check draws 20 (score 20,
/// a success), 10 (a near miss) and 1 (a critical failure), each followed by 0x7FFF for the
/// mana roll (2).
/// </summary>
public sealed class MushroomMana : Scenario
{
    const int Int = 20, Skill = 29;
    static readonly int[] CheckDraw = { 20, 10, 1 };
    static readonly int[] Expected = { 2, 0, 2 };

    public override string Name => "mushroom-mana";
    public override string Cites => "underworld-exhumed uw1/src/obj/USEITEMS.C:621-623, uw2/src/obj/USEITEMS.C:221-223; uw1/src/combat/SPELLS.C:168-181, uw2/src/combat/SPELLS.C:155-170";

    string _problem;
    readonly int[] _index = new int[3], _slot = new int[3], _gain = new int[3], _draws = new int[3];
    readonly bool[] _eaten = new bool[3];

    public override Task Setup(ScenarioContext c)
    {
        int mushroom = c.Game == "uw2" ? 0xB9 : 0xB8;   // ITEM_MUSHROOM: uw1/src/include/items.h:336, uw2 items.h:329
        for (int i = 0; i < 3; i++)
        {
            (_index[i], _slot[i]) = Guards.PutInBackpack(mushroom);
            if (_slot[i] == -1) { _problem = "could not put three mushrooms in the backpack"; return Task.CompletedTask; }
        }
        playerdat.INT = Int;
        playerdat.ManaSkill = Skill;
        playerdat.RecalculateHPManaMaxWeight(false);
        playerdat.play_mana = 0;
        if (playerdat.max_mana != (Skill + 1) * Int >> 3) _problem = $"the maximum is {playerdat.max_mana}";
        return Task.CompletedTask;
    }

    public override async Task Act(ScenarioContext c)
    {
        if (_problem != null) return;
        var saved = Rng.Source;
        try
        {
            for (int i = 0; i < 3; i++)
            {
                int before = playerdat.play_mana;
                var source = new ScriptedRand(CheckDraw[i], 0x7FFF);
                Rng.Source = source;
                uimanager.CurrentSlot = _slot[i];
                use.Use(playerdat.InventoryObjects[_index[i]], playerdat.playerObject, playerdat.InventoryObjects, WorldObject: false);
                Rng.Source = saved;
                _gain[i] = playerdat.play_mana - before;
                _draws[i] = source.Draws;
                _eaten[i] = playerdat.GetInventorySlotListHead(_slot[i]) == 0;
            }
        }
        finally { Rng.Source = saved; }
        await c.Frames(1);
        c.Note($"mana gained {string.Join(", ", _gain)} (draws {string.Join(", ", _draws)}), maximum {playerdat.max_mana}");
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        string[] what = { "a success", "a near miss", "a critical failure" };
        var k = new Checks();
        for (int i = 0; i < 3; i++)
        {
            if (!_eaten[i]) return $"precondition: mushroom {i + 1} was not eaten";
            k.Equal($"mana from a mushroom on {what[i]}", Expected[i], _gain[i],
                c.Game == "uw2" ? "restore_mana(-(rand() * 3 / 0x8000)) unless the check is 0: uw2 USEITEMS.C:222-223" : "restore_mana(-(rand() * 3 / 0x8000)) unless the check is 0: uw1 USEITEMS.C:622-623");
        }
        return k.Result;
    }
}

/// <summary>
/// Eating food heals food_heal / 8 hit points in UW1 and food_heal / 6 in UW2, at most 8, and
/// zeroes food_heal (player_eat, uw1 game/SKILLS.C:652-659, uw2 SKILLS.C:590-597). The scenario
/// sets food_heal to 48 and 1 hit point, and eats an apple from the backpack: 7 hit points in UW1,
/// 9 in UW2.
/// </summary>
public sealed class FoodHeal : Scenario
{
    const int Apple = 0xB3;   // ITEM_APPLE, uw1/src/include/items.h:331, uw2 items.h:323
    const int FoodHealBefore = 48, HpBefore = 1;

    public override string Name => "food-heal";
    public override string Cites => "underworld-exhumed uw1/src/game/SKILLS.C:652-659, uw2/src/game/SKILLS.C:590-597 (player_eat)";

    string _problem;
    int _index, _slot, _hpAfter, _bonusAfter;
    bool _eaten;

    public override Task Setup(ScenarioContext c)
    {
        (_index, _slot) = Guards.PutInBackpack(Apple);
        if (_slot == -1) { _problem = "could not put an apple in the backpack"; return Task.CompletedTask; }
        if (Guards.FoodNutrition(Apple) <= 0) { _problem = "the apple has no nourishment"; return Task.CompletedTask; }
        if (playerdat.max_hp < HpBefore + 8) { _problem = $"the maximum hit points {playerdat.max_hp} are too low"; return Task.CompletedTask; }
        playerdat.play_hunger = 0x40;
        playerdat.play_hp = HpBefore;
        playerdat.foodhealthbonus = FoodHealBefore;
        return Task.CompletedTask;
    }

    public override async Task Act(ScenarioContext c)
    {
        if (_problem != null) return;
        uimanager.CurrentSlot = _slot;
        use.Use(playerdat.InventoryObjects[_index], playerdat.playerObject, playerdat.InventoryObjects, WorldObject: false);
        _hpAfter = playerdat.play_hp;
        _bonusAfter = playerdat.foodhealthbonus;
        _eaten = playerdat.GetInventorySlotListHead(_slot) == 0;
        await c.Frames(1);
        c.Note($"food_heal {FoodHealBefore}, hit points {HpBefore} -> {_hpAfter}, food_heal after {_bonusAfter}");
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        if (!_eaten) return "precondition: the apple was not eaten";
        var k = new Checks();
        int divisor = c.Game == "uw2" ? 6 : 8;
        k.Equal("hit points after eating", HpBefore + FoodHealBefore / divisor, _hpAfter, c.Game == "uw2" ? "food_heal / 6: uw2 SKILLS.C:592-595" : "food_heal / 8: uw1 SKILLS.C:654-657");
        k.Equal("food_heal after eating", 0, _bonusAfter, c.Game == "uw2" ? "uw2 SKILLS.C:596" : "uw1 SKILLS.C:658");
        return k.Result;
    }
}

/// <summary>
/// In UW2, eating or drinking anything through UseFood clears the wait between object casts:
/// nextSpellTime = 0 on each side of its checkSpell (uw2 obj/USEITEMS.C:349-351), reached by
/// every food that is not refused. UW1's UseFood has no such line and its checkSpell only touches
/// nextSpellTime when the food carries a spell (uw1 USEITEMS.C:714, 1050-1074), so plain food
/// leaves the wait alone.
///
/// The scenario starts an object cast wait at clock T (SpellCasting.ObjectSpellWaitHasElapsed,
/// the port's checkSpell gate), then at the same T eats an apple and a mushroom and, in UW2,
/// drinks a red potion with no spell, and after each asks the gate again: UW2 allows the cast
/// (and so starts the wait again), UW1 still refuses.
/// </summary>
public sealed class FoodClearsCastWait : Scenario
{
    const int Apple = 0xB3;   // ITEM_APPLE, uw1/src/include/items.h:331, uw2 items.h:323

    public override string Name => "food-clears-cast-wait";
    public override string Cites => "underworld-exhumed uw2/src/obj/USEITEMS.C:349-351; uw1/src/obj/USEITEMS.C:714, 1050-1074";

    string _problem;
    readonly List<(string what, int index, int slot)> _items = new();
    readonly List<(string what, bool eaten, bool allowed)> _after = new();
    bool _started;

    public override Task Setup(ScenarioContext c)
    {
        var items = new List<(string, int)> { ("an apple", Apple) };
        // ITEM_MUSHROOM uw1/src/include/items.h:336, uw2 items.h:329; ITEM_RED_POTION uw2 items.h:377
        items.Add(("a mushroom", c.Game == "uw2" ? 0xB9 : 0xB8));
        if (c.Game == "uw2") items.Add(("a red potion", 0xE5));
        foreach (var (what, item) in items)
        {
            var (index, slot) = Guards.PutInBackpack(item);
            if (slot == -1) { _problem = $"could not put {what} in the backpack"; return Task.CompletedTask; }
            var o = playerdat.InventoryObjects[index];
            if (o.is_quant == 0 && o.link != 0) { _problem = $"{what} carries a linked object"; return Task.CompletedTask; }
            _items.Add((what, index, slot));
        }
        playerdat.play_hunger = 0x40;
        playerdat.intoxication = 0;
        // Clear of any wait an earlier scenario in this process left behind.
        playerdat.ClockValue += SpellCasting.ObjectSpellInterval + 1;
        return Task.CompletedTask;
    }

    public override async Task Act(ScenarioContext c)
    {
        if (_problem != null) return;
        _started = SpellCasting.ObjectSpellWaitHasElapsed();
        foreach (var (what, index, slot) in _items)
        {
            uimanager.CurrentSlot = slot;
            use.Use(playerdat.InventoryObjects[index], playerdat.playerObject, playerdat.InventoryObjects, WorldObject: false);
            bool eaten = playerdat.GetInventorySlotListHead(slot) == 0;
            bool allowed = SpellCasting.ObjectSpellWaitHasElapsed();   // at the same clock; when allowed it starts the wait again
            _after.Add((what, eaten, allowed));
        }
        SpellCasting.ClearObjectSpellWait();   // leave no wait for the next scenario: the static outlives this game
        await c.Frames(1);
        foreach (var a in _after) c.Note($"after {a.what}: used {a.eaten}, object cast allowed {a.allowed}");
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        if (!_started) return "precondition: the first object cast at T was refused";
        var k = new Checks();
        foreach (var a in _after)
        {
            if (!a.eaten) return $"precondition: {a.what} was not used";
            if (c.Game == "uw2")
                k.True(a.allowed, $"after {a.what} the object cast wait was still running (UseFood sets nextSpellTime = 0: uw2 USEITEMS.C:349-351)");
            else
                k.True(!a.allowed, $"after {a.what} the object cast wait was cleared (UW1's UseFood leaves nextSpellTime alone: uw1 USEITEMS.C:714, 1050-1074)");
        }
        return k.Result;
    }
}
