using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// The player's armour and defence. The original's FixPlayerEquips gives each armour piece in
/// slots 0 to 4 armor_val, quality * protection / 64 + 1, at its hit location (helm 3, body 0,
/// gloves 1, leggings and boots 2); adds a shield's armor_val (hack major, minor 3, types 11 to
/// 15) held in the off hand to locations 0 and 1; and makes defence the Defence skill plus half
/// the skill of the weapon in the weapon hand, clamped to sword, axe or mace (uw2/src/game/PLAYDATA.C:322-385;
/// uw1/src/game/PLAYDATA.C:371-425). The off hand is slot 7 + lefty and the weapon hand 8 - lefty,
/// so they are the two hand slots, the weapon in the port's DominantHandSlot.
///
/// The scenario puts a breastplate (0x22) of quality 20 on the body, a helmet (0x2E) of quality 10
/// on the head, a tower shield (0x3B) of quality 30 in the off hand and a longsword (0x5) in the weapon hand, sets Defence 10 and
/// Sword 12, runs the port's playerdat.PlayerStatusUpdate (its FixPlayerEquips), and compares
/// LocationalArmourValues and the defence the port's combat uses (critterObjectDat.defence(127))
/// with the original's arithmetic over the shipped protection values (armourObjectDat.protection,
/// the same table as Armor[], OBJECTS.DAT).
///
/// The port once added the table protection whatever the quality, without the +1
/// (src/player/playerdatloop.cs:653), never looked at the shield hand, and took the bare
/// Defence skill as the player's defence (src/objectdata/critterObjectDat.cs:358).
/// </summary>
public sealed class PlayerArmour : Scenario
{
    const int Vest = 0x22, Shield = 0x3B, Helmet = 0x2E, Longsword = 0x05;   // breastplate, tower shield, helmet, longsword
    const int VestQuality = 20, ShieldQuality = 30, HelmetQuality = 10, DefenceSkill = 10, SwordSkill = 12;

    public override string Name => "player-armour";
    public override string Cites => "underworld-exhumed uw2/src/game/PLAYDATA.C:322-332 (armor_val), 347-385 (FixPlayerEquips); uw1/src/game/PLAYDATA.C:364-374, 387-425";

    string _problem;
    int[] _armour = new int[4];
    int _defence, _expectDefence, _weaponSkill;
    int[] _expectArmour = new int[4];

    static int ArmorVal(int item, int quality) => ((quality * armourObjectDat.protection(item)) >> 6) + 1;

    public override Task Setup(ScenarioContext c)
    {
        int weaponHand = uimanager.DominantHandSlot, offHand = weaponHand == 8 ? 7 : 8;
        for (int s = 0; s <= 8; s++)
            if (playerdat.GetInventorySlotListHead(s) != 0) { _problem = $"paper doll slot {s} is not empty in the new game"; return Task.CompletedTask; }
        if (BatchB.Equip(Vest, 1, VestQuality) == null || BatchB.Equip(Shield, offHand, ShieldQuality) == null || BatchB.Equip(Longsword, weaponHand, 40) == null || BatchB.Equip(Helmet, 0, HelmetQuality) == null)
        { _problem = "the breastplate, shield, sword or helmet could not be put in its slot"; return Task.CompletedTask; }
        playerdat.Defense = DefenceSkill;
        _weaponSkill = weaponObjectDat.skill(Longsword);
        int skillIndex = _weaponSkill < 3 ? 3 : _weaponSkill > 5 ? 5 : _weaponSkill;   // PLAYDATA.C:373-375 / 413-415
        playerdat.SetSkillValue(skillIndex, SwordSkill);

        int vest = ArmorVal(Vest, VestQuality), shield = ArmorVal(Shield, ShieldQuality);
        int helmet = ArmorVal(Helmet, HelmetQuality);
        _expectArmour = new[] { vest + shield, shield, 0, helmet };
        _expectDefence = DefenceSkill + (SwordSkill >> 1);
        c.Note($"breastplate protection {armourObjectDat.protection(Vest)} at quality {VestQuality} gives {vest}; helmet protection {armourObjectDat.protection(Helmet)} at quality {HelmetQuality} gives {helmet}; shield protection {armourObjectDat.protection(Shield)} at quality {ShieldQuality} gives {shield}; longsword skill {_weaponSkill}");
        return Task.CompletedTask;
    }

    public override Task Act(ScenarioContext c)
    {
        if (_problem != null) return Task.CompletedTask;
        playerdat.PlayerStatusUpdate();
        for (int i = 0; i < 4; i++) _armour[i] = playerdat.LocationalArmourValues[i];
        _defence = critterObjectDat.defence(127);
        c.Note($"armour by location {string.Join(", ", _armour)}, defence {_defence}");
        return Task.CompletedTask;
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        bool uw2 = c.Game == "uw2";
        var k = new Checks();
        string[] names = { "body (0)", "arms (1)", "legs (2)", "head (3)" };
        for (int i = 0; i < 4; i++)
            k.Equal($"armour at {names[i]}", _expectArmour[i], _armour[i],
                uw2 ? "uw2 PLAYDATA.C:322-332 quality * protection / 64 + 1, shield 362-368" : "uw1 PLAYDATA.C:364-374, shield 401-408");
        k.Equal("player's defence (Defence + weapon skill / 2)", _expectDefence, _defence, uw2 ? "uw2 PLAYDATA.C:370-385" : "uw1 PLAYDATA.C:409-425");
        return k.Result;
    }
}
