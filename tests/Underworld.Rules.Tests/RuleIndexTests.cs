using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace Underworld.Rules.Tests;

// Idea from Underworld Revisited (PQMarine, MIT): Tools/UWRuleIndex.

/// <summary>Where the port's source tree is: the folder above the test's output that holds Underworld.csproj and src/.</summary>
public static class SourceTree
{
    public static readonly string Root = Find();

    static string Find()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "Underworld.csproj")) && Directory.Exists(Path.Combine(d.FullName, "src")))
                return d.FullName;
        return null;
    }

    public static string RuleIndexDir => Path.Combine(Root, "tests", "Underworld.Rules.Tests", "ruleindex");
}

/// <summary>A test that needs the source tree, skipped when the tests run without it.</summary>
public sealed class SourceTreeFactAttribute : FactAttribute
{
    public SourceTreeFactAttribute()
    {
        if (SourceTree.Root == null) Skip = "the port's source tree (Underworld.csproj and src/) is not above the test's output";
    }
}

/// <summary>
/// The rules index: every routine of UW.EXE and UW2.EXE the port implements and where, and a
/// check that no routine has two implementations that nobody knows about. Two branches writing
/// the same routine twice drift apart; this catches it when the second one lands.
/// </summary>
public class RuleIndexTests
{
    static Crosswalk Crosswalk => Crosswalk.Load(SourceTree.RuleIndexDir);
    static string ExpectedPath => Path.Combine(SourceTree.RuleIndexDir, "EXPECTED_DUPLICATES");

    static (List<RuleImplementation> index, Dictionary<string, List<RuleImplementation>> dups) ScanTree()
    {
        var index = RuleIndexScan.Scan(SourceTree.Root, Crosswalk);
        return (index, RuleIndexScan.Duplicates(index));
    }

    /// <summary>
    /// Writes ruleindex/RULES_INDEX.md when RULES_INDEX_RECORD=1. Otherwise does nothing: the file
    /// is a listing to read, and line numbers move with every change.
    /// </summary>
    [SourceTreeFact]
    public void RuleIndex_Record()
    {
        if (Environment.GetEnvironmentVariable("RULES_INDEX_RECORD") != "1") return;
        var (index, _) = ScanTree();
        var source = File.ReadLines(Path.Combine(SourceTree.RuleIndexDir, "SOURCE")).First();
        File.WriteAllText(Path.Combine(SourceTree.RuleIndexDir, "RULES_INDEX.md"), RuleIndexScan.Markdown(index, source));
    }

    /// <summary>The scanner still finds what it should, so the duplicate check cannot pass by finding nothing.</summary>
    [SourceTreeFact]
    public void RuleIndex_FindsKnownRoutines()
    {
        var (index, _) = ScanTree();
        void Has(string routine, string member, string how)
        {
            Assert.True(index.Any(r => r.Routine == routine && r.Member == member && r.How == how),
                $"the scanner no longer finds {member} implementing {routine} ({how}); has the code or the scanner changed?");
        }
        Has("uw1:SquareRoot_seg019_A78@IMATH.ASM", "sqrt_vanilla", "cite");               // uw1 IMATH.ASM:107, an .ASM file
        Has("uw2:SquareRoot_seg021_22FD_A78@IMATH.ASM", "sqrt_vanilla", "cite");          // uw2 IMATH.ASM:103
        Has("uw2:beeline@PATHFIND.C", "PathAlongStraightLine_seg006_1413_205B", "name");   // UW2 paragraph 1413
        Has("uw2:fill_the_grid@VIEW3D.C", "TestVisionAndShade_seg32_1014", "name");        // segment number cut to two digits
        Has("uw2:dseg_67d6_B4", "BitFieldForPathing_dseg_67d6_B4", "name");                // a UW2 global
        Has("uw1:crit_avoid_player@AI.C", "VectorsToPlayer", "cite");                      // uw1 AI.C:685-713
        Has("uw2:crit_avoid_player@AI.C", "VectorsToPlayer", "cite");
        Has("uw2:crit_hover@AI.C", "NPCGoalC_StandAtLocation", "comment");                 // its address alone on a line
        Assert.True(index.Count > 150, $"only {index.Count} implementations found");
    }

    /// <summary>
    /// Fails when a routine has more than one implementation and EXPECTED_DUPLICATES does not
    /// list it with exactly those declarations and a reason.
    /// </summary>
    [SourceTreeFact]
    public void RuleIndex_NoRoutineImplementedTwice()
    {
        var (_, dups) = ScanTree();
        var expected = ExpectedDuplicate.Load(ExpectedPath).ToDictionary(e => e.Routine);
        var problems = new StringBuilder();
        foreach (var (routine, impls) in dups.OrderBy(d => d.Key, StringComparer.Ordinal))
        {
            var ids = impls.Select(r => r.Id).ToHashSet();
            if (expected.TryGetValue(routine, out var e) && ids.SetEquals(e.Members)) continue;
            problems.Append($"\n{routine} is implemented {impls.Count} times:\n");
            foreach (var r in impls)
                problems.Append($"  {r.Path}:{r.Line} {r.Member} ({How(r.How)}){(e != null && !e.Members.Contains(r.Id) ? "  <- new" : "")}\n");
            problems.Append(e == null
                ? "  If both are meant, add to tests/Underworld.Rules.Tests/ruleindex/EXPECTED_DUPLICATES:\n"
                : $"  EXPECTED_DUPLICATES:{e.Line} lists other declarations for it; if this set is meant, change that line to:\n");
            problems.Append($"  {routine} | {string.Join(" ", impls.Select(r => r.Id))} | <{string.Join(" / ", ExpectedDuplicate.Kinds)}>: why\n");
            problems.Append("  Otherwise one of them should call the other, or go.\n");
        }
        Assert.True(problems.Length == 0, "One of the original's routines has more than one implementation in the port:" + problems);
    }

    /// <summary>Every line of EXPECTED_DUPLICATES is still a duplicate and gives a reason of a known kind.</summary>
    [SourceTreeFact]
    public void RuleIndex_ExpectedDuplicatesAreCurrent()
    {
        var (_, dups) = ScanTree();
        var problems = new List<string>();
        var seen = new HashSet<string>();
        foreach (var e in ExpectedDuplicate.Load(ExpectedPath))
        {
            var at = $"EXPECTED_DUPLICATES:{e.Line} {e.Routine}";
            if (!seen.Add(e.Routine)) problems.Add($"{at}: listed twice");
            if (!ExpectedDuplicate.Kinds.Any(k => e.Reason.StartsWith(k, StringComparison.Ordinal)))
                problems.Add($"{at}: the reason must start with one of: {string.Join(", ", ExpectedDuplicate.Kinds)}");
            if (!dups.TryGetValue(e.Routine, out var impls))
                problems.Add($"{at}: no longer has two implementations; remove the line");
            else
                foreach (var gone in e.Members.Except(impls.Select(r => r.Id)))
                    problems.Add($"{at}: {gone} no longer implements it; update the line");
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    static string How(string how) => how switch
    {
        "name" => "its name carries the address",
        "cite" => "its comment cites where the routine is defined",
        _ => "its comment gives the routine's address",
    };

    // The scanner on small made-up sources, so its rules are pinned without the source tree.

    static readonly Lazy<Crosswalk> Cw = new(() => SourceTree.Root == null ? null : Crosswalk);

    static List<RuleImplementation> ScanText(string text) =>
        RuleIndexScan.ScanFile("src/x.cs", text.Replace("\r", "").Split('\n'), Cw.Value).ToList();

    [SourceTreeFact]
    public void RuleIndex_ScannerRules()
    {
        // A citation of the routine's first line counts; one of a line inside it, or of a call, does not.
        var r = ScanText("""
            /// crit_avoid_player (uw1 AI.C:685-713, uw2 AI.C:652-686)
            static int A(int h)
            {
            }
            /// the 16-bit compare (uw1 AI.C:700)
            static int B(int h)
            {
            }
            """);
        Assert.Equal(new[] { "A uw1:crit_avoid_player@AI.C", "A uw2:crit_avoid_player@AI.C" }, r.Select(x => x.Member + " " + x.Routine));

        // Later in the prose a citation counts only under the routine's name as a label; a routine
        // the method stands in for does not.
        r = ScanText("""
            /// The search before the player is placed. A square of 0x3F is a stand-in, as the callback
            /// sets the real square (moveto in the original, uw1 SKILLS.C:769-784).
            static bool G() { return true; }
            /// Destroys a floating skull. uw2 WORLDEV.C:1649-1660 (destroy_floatskull)
            static bool H() { return true; }
            """);
        Assert.Equal(new[] { "H uw2:destroy_floatskull@WORLDEV.C" }, r.Select(x => x.Member + " " + x.Routine));

        // The comment on the opening brace counts, and ":N" continues the file named before it.
        r = ScanText("""
            static void C()
            {//clear_paths, uw1 PATHFIND.C:1, :945-954
            }
            """);
        Assert.Equal(new[] { "uw1:clear_paths@PATHFIND.C" }, r.Select(x => x.Routine));

        // An address alone on a line, or after "matching", says what the method is; one in a sentence about a call does not.
        r = ScanText("""
            /// Goal 12.
            /// NPCGoalC_seg007_17A2_17F1
            void D() { }
            // Integer square root matching UW1 seg019_A78 behaviour
            int E(int v) { return v; }
            /// DOS refreshes the music (ovr108_162B calls RefreshMusic_seg016_1E73_2BB2).
            void F() { }
            """);
        Assert.Equal(new[] { "D uw2:crit_hover@AI.C", "E uw1:SquareRoot_seg019_A78@IMATH.ASM" }, r.Select(x => x.Member + " " + x.Routine));

        // Calls, commented-out code and strings are not declarations.
        r = ScanText("""
            var x = SquareRoot_seg019_A78(4);
            return SquareRoot_seg019_A78(4);
            //static int SquareRoot_seg019_A78(int v)
            /* static int SquareRoot_seg019_A78(int v) */
            var s = "static int SquareRoot_seg019_A78(int v)";
            """);
        Assert.Empty(r);

        // Two declarations of one routine are a duplicate, keyed per game: the same offset in the
        // other game's segment is another routine.
        r = ScanText("""
            static int SquareRoot_seg019_A78(int v) { return v; }
            /// cSqRt (uw1 IMATH.ASM:107)
            static int IntSqrt(int v) { return v; }
            static void beeline_seg006_1413_205B() { }
            static void Other_seg006_1477_205B() { }
            """);
        var dups = RuleIndexScan.Duplicates(r);
        Assert.Equal(new[] { "uw1:SquareRoot_seg019_A78@IMATH.ASM" }, dups.Keys);
        Assert.Equal(new[] { "src/x.cs#IntSqrt", "src/x.cs#SquareRoot_seg019_A78" }, dups.Values.Single().Select(x => x.Id));
    }
}
