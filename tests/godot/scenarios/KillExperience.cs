using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// The experience a kill is worth, and who earns it. The original's player_killed_a gives
/// exp * 4 + rollem(2, exp) from the critter's record, and for a powerful critter replaces that
/// with exp * (rand() % 24 + 24) / 16 (uw2/src/combat/COMBAT.C:935-947; uw1/src/combat/COMBAT.C:830-842).
/// damage_critter calls it only when the blow was the player's (who == 1, uw2/src/critter/AI.C:1505;
/// uw1/src/critter/AI.C:1494). player_get_exp then gives it: UW2 halves every gain first,
/// (n + rand() % 2) / 2 (uw2/src/game/SKILLCHK.C:63-73); UW1 does not halve, and adds nothing
/// at all once experience is above 0x17700 (uw1/src/game/SKILLCHK.C:85-87). Neither halves again
/// here, where the player's level 1 is not above twice the level number plus two.
///
/// The scenario spawns a critter of a type worth a little experience next to the player and
/// kills it through the port's damage.DamageObject with the player as the source, the path a
/// player's blow or spell takes (damage type 0, so the damage scaling draws nothing), with the
/// game's generator seeded. The expected gain is computed from the same seed with the original's
/// formula. Then a powerful one (bit set), then one killed by a trap (source 0), which must earn
/// nothing. A plain gain of 100 checks player_get_exp alone, and in UW1 so does a gain above
/// 0x17700.
///
/// The port once gave exp + 2d(exp), added the powerful bonus on top (src/player/playerdat.cs:767,
/// 770), paid out whoever made the kill (src/interaction/damage.cs:170), halved every gain in UW1
/// too and kept adding past 0x17700 (playerdat.cs:657, 687).
/// </summary>
public sealed class KillExperience : Scenario
{
    const uint KillSeed = 4242, PowerfulSeed = 777, PlainSeed = 99;

    public override string Name => "kill-experience";
    public override string Cites => "underworld-exhumed uw2/src/combat/COMBAT.C:935-947, uw2/src/critter/AI.C:1502-1506, uw2/src/game/SKILLCHK.C:63-73; uw1/src/combat/COMBAT.C:830-842, uw1/src/critter/AI.C:1491-1495, uw1/src/game/SKILLCHK.C:70-95";

    string _problem;
    int _critterId, _exp;
    long _gainKill, _gainPowerful, _gainTrap, _gainPlain, _gainCapped = -1;
    long _expectKill, _expectPowerful, _expectPlain;
    bool _killed, _poweredKilled, _trapKilled;

    public override Task Setup(ScenarioContext c)
    {
        _critterId = BatchB.FindCritter(id => critterObjectDat.experience(id) is > 0 and < 100);
        if (_critterId < 0) { _problem = "no critter type worth 1..99 experience (UW1 multiplies in 16 bits)"; return Task.CompletedTask; }
        _exp = critterObjectDat.experience(_critterId);
        playerdat.play_level = 1;
        return Task.CompletedTask;
    }

    bool Kill(int source, bool powerful, uint seed, out long gain)
    {
        var (x, y) = BatchB.OpenSquareNearPlayer();
        var critter = BatchB.SpawnCritter(_critterId, x, y);
        gain = 0;
        if (critter == null) return false;
        critter.IsPowerful = (short)(powerful ? 1 : 0);
        critter.npc_whoami = 0;
        playerdat.Exp = 0;
        playerdat.play_level = 1;   // a level gained by an earlier kill would bring in the level halving
        BatchB.Seed(seed);
        damage.DamageObject(critter, basedamage: critter.npc_hp + 1, damagetype: 0,
            objList: UWTileMap.current_tilemap.LevelObjects, WorldObject: true, damagesource: source);
        gain = playerdat.Exp;
        return critter.npc_hp == 0;
    }

    long Expected(bool uw2, bool powerful, uint seed)
    {
        var d = new BatchB.DosRand(seed);
        if (!uw2) d.Rand();   // uw1 COMBAT.C:836 set_screen_frame(4, 2) first picks a dragon, rand() & 1 when both are idle (uw1 PANELS.C:304-305)
        long n = _exp * 4 + d.Rollem(2, _exp);                       // COMBAT.C:942 / 838
        if (powerful) n = n * (d.Rand() % 24 + 24) / 16;            // COMBAT.C:943-944 / 839-840
        if (uw2) n = (n + d.Rand() % 2) / 2;                         // uw2 SKILLCHK.C:72-73
        return n;
    }

    public override Task Act(ScenarioContext c)
    {
        if (_problem != null) return Task.CompletedTask;
        bool uw2 = c.Game == "uw2";
        _killed = Kill(1, false, KillSeed, out _gainKill);
        _expectKill = Expected(uw2, false, KillSeed);
        _poweredKilled = Kill(1, true, PowerfulSeed, out _gainPowerful);
        _expectPowerful = Expected(uw2, true, PowerfulSeed);
        _trapKilled = Kill(0, false, KillSeed, out _gainTrap);

        playerdat.Exp = 0;
        playerdat.play_level = 1;
        BatchB.Seed(PlainSeed);
        playerdat.ChangeExperience(100);
        _gainPlain = playerdat.Exp;
        _expectPlain = uw2 ? (100 + new BatchB.DosRand(PlainSeed).Rand() % 2) / 2 : 100;

        if (!uw2)
        {
            playerdat.Exp = 0x17701;
            playerdat.play_level = 1;
            playerdat.ChangeExperience(100);
            _gainCapped = playerdat.Exp - 0x17701;
        }
        playerdat.Exp = 0;
        c.Note($"critter 0x{_critterId:X} worth {_exp}: kill +{_gainKill} (expected {_expectKill}), powerful +{_gainPowerful} (expected {_expectPowerful}), by a trap +{_gainTrap}, plain 100 gives +{_gainPlain}{(uw2 ? "" : $", 100 above 0x17700 gives +{_gainCapped}")}");
        return Task.CompletedTask;
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        if (!_killed || !_poweredKilled || !_trapKilled) return $"precondition: a critter was not killed (player's {_killed}, powerful {_poweredKilled}, trap's {_trapKilled})";
        bool uw2 = c.Game == "uw2";
        var k = new Checks();
        k.Equal("experience for a kill (exp * 4 + 2d(exp))", _expectKill, _gainKill, uw2 ? "uw2 COMBAT.C:942, SKILLCHK.C:72-73" : "uw1 COMBAT.C:838, SKILLCHK.C:84-94");
        k.Equal("experience for a powerful kill (n * (24..47) / 16)", _expectPowerful, _gainPowerful, uw2 ? "uw2 COMBAT.C:942-944" : "uw1 COMBAT.C:838-840");
        k.Equal("experience for a kill by a trap", 0, _gainTrap, uw2 ? "uw2 AI.C:1505, only who == 1" : "uw1 AI.C:1494, only who == 1");
        k.Equal("experience from a plain gain of 100", _expectPlain, _gainPlain, uw2 ? "uw2 SKILLCHK.C:72-73, halved" : "uw1 SKILLCHK.C:70-95, not halved");
        if (!uw2) k.Equal("experience from a gain of 100 above 0x17700", 0, _gainCapped, "uw1 SKILLCHK.C:85-87");
        return k.Result;
    }
}
