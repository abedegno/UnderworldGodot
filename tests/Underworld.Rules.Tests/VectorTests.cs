using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Underworld;
using Xunit.Abstractions;

namespace Underworld.Rules.Tests;

/// <summary>
/// Each table of uwN/vectors (the original EXE's own outputs) run through UnderworldGodot's
/// version of the rule, every row. A table in vectors/KNOWN_FAILURES still runs and prints its
/// failing rows but passes; a listed table that now passes fails, so the list cannot go stale.
/// </summary>
[Collection("Rules")]
public class VectorTests
{
    readonly ITestOutputHelper output;
    public VectorTests(ITestOutputHelper output) { this.output = output; }

    static readonly IReadOnlySet<string> Known = VectorTable.KnownFailures();

    /// <summary>Runs every row; check returns null for a match or a description of the mismatch.</summary>
    void Run(string game, string table, Func<IReadOnlyDictionary<string, long>, string> check)
    {
        var saved = Rng.Source; var savedRes = UWClass._RES;
        var failures = new List<string>(); int n = 0;
        try
        {
            UWClass._RES = game == "uw1" ? UWClass.GAME_UW1 : UWClass.GAME_UW2;
            foreach (var row in VectorTable.Load(game, table))
            {
                n++;
                if (row.TryGetValue("seed", out var seed)) Rng.Source = new BorlandRandSource((uint)seed);
                string bad;
                try { bad = check(row); }
                catch (TargetInvocationException e) { bad = "threw " + e.InnerException?.GetType().Name + ": " + e.InnerException?.Message; }
                catch (Exception e) { bad = "threw " + e.GetType().Name + ": " + e.Message; }
                if (bad != null)
                    failures.Add(string.Join(",", row.Select(kv => $"{kv.Key}={kv.Value}")) + "  -> " + bad);
            }
        }
        finally { Rng.Source = saved; UWClass._RES = savedRes; }

        var key = $"{game}/{table}";
        foreach (var f in failures.Take(10)) output.WriteLine(f);
        output.WriteLine($"{key}: {n - failures.Count} of {n} rows match");
        Assert.True(n > 0, $"{key} has no rows");
        if (Known.Contains(key))
            Assert.True(failures.Count > 0, $"{key} now matches every row: remove it from vectors/KNOWN_FAILURES");
        else
            Assert.True(failures.Count == 0, $"{key}: {failures.Count} of {n} rows differ; first: {failures.FirstOrDefault()}");
    }

    [Fact]
    public void Known_failures_name_existing_tables()
    {
        var missing = Known.Where(k => !File.Exists(Path.Combine(VectorTable.Dir, k + ".csv"))).ToList();
        Assert.True(missing.Count == 0, "KNOWN_FAILURES names no such table: " + string.Join(", ", missing));
    }

    static string Expect(string what, long expected, long actual) =>
        expected == actual ? null : $"{what} expected {expected}, got {actual}";

    static string Seed(IReadOnlyDictionary<string, long> row) =>
        Expect("seed_out", row["seed_out"], ((BorlandRandSource)Rng.Source).Seed);

    [Theory, InlineData("uw1"), InlineData("uw2")]
    public void Skill_check(string game) => Run(game, "skill_check", r =>
        Expect("result", r["result"], (int)playerdat.SkillCheck((int)r["value"], (int)r["target"])) ?? Seed(r));

    [Theory, InlineData("uw1"), InlineData("uw2")]
    public void Rollem(string game) => Run(game, "rollem", r =>
        Expect("total", r["total"], Rng.DiceRoll((int)r["dice"], (int)r["sides"])) ?? Seed(r));

    [Theory, InlineData("uw1"), InlineData("uw2")]
    public void Pickloc(string game) => Run(game, "pickloc", r =>
        Expect("loc", r["loc"], combat.PickBodyHitPoint((int)r["dz"], (int)r["dtop"], (int)r["az"], (int)r["atop"])) ?? Seed(r));

    [Theory, InlineData("uw1"), InlineData("uw2")]
    public void Move_along(string game) => Run(game, "move_along", r =>
    {
        int x = (int)r["x"], y = (int)r["y"];
        motion.GetCoordinateInDirection((int)r["heading"], (int)r["dist"], ref x, ref y);
        return Expect("x_out", r["x_out"], x) ?? Expect("y_out", r["y_out"], y);
    });

    [Theory, InlineData("uw1"), InlineData("uw2")]
    public void Check_res(string game) => Run(game, "check_res", r =>
    {
        Seams.SetResistance((int)r["item"], (int)r["resist"]);
        int damage = (int)r["damage"];
        var result = damage_ScaleDamage((int)r["item"], damage, (int)r["type"]);
        return Expect("result", r["result"], result) ?? Seed(r);
    });

    static int damage_ScaleDamage(int item, int basedamage, int type) =>
        Underworld.damage.ScaleDamage(item, ref basedamage, type);

    [Theory, InlineData("uw1"), InlineData("uw2")]
    public void Compute_hitangle(string game) => Run(game, "compute_hitangle", r =>
    {
        Seams.SetStatic(typeof(combat), "DefendingCharacter", Seams.Object((int)r["def_item"], (int)r["def_pos"]));
        Seams.SetStatic(typeof(combat), "AttackingCharacter", Seams.Object(0, (int)r["att_pos"]));
        return Expect("hitangle", r["hitangle"], (int)Seams.CallStatic(typeof(combat), "CalcFlankingBonus"));
    });

    [Theory, InlineData("uw1"), InlineData("uw2")]
    public void Sqrt(string game) => Run(game, "sqrt", r =>
        Expect("root", r["root"], UnderWorldSqrt.sqrt_vanilla((uint)r["value"])));

    [Theory, InlineData("uw1"), InlineData("uw2")]
    public void Sincos(string game) => Run(game, "sincos", r =>
    {
        short a = 0, b = 0;
        motion.SomethingProjectileHeading_seg021_22FD_EAE((ushort)r["angle"], ref a, ref b);
        return Expect("a", r["a"], a) ?? Expect("b", r["b"], b);
    });

    [Theory, InlineData("uw2")]
    public void Is_sharp(string game) => Run(game, "is_sharp", r =>
        Expect("sharp", r["sharp"], (bool)Seams.CallStatic(typeof(combat), "checkforPoisonableWeapon", Seams.Object((int)r["item"])) ? 1 : 0));

    [Theory, InlineData("uw2")]
    public void Debris_type(string game) => Run(game, "debris_type", r =>
    {
        Seams.SetWeaponSkill((int)r["item"], (int)r["weapon_skill"]);
        return Expect("debris", r["debris"], Underworld.damage.GetObjectTypeDebris(Seams.Object((int)r["item"]), (int)r["type"]));
    });
}
