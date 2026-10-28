using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit.Abstractions;

namespace Underworld.Rules.Tests;

/// <summary>
/// The port's rule values and rule outputs compared with pins/rule-pins.txt, so a change that
/// nobody meant (a slip, a wrong merge resolution) fails here. With PINS_RECORD=1 the test writes
/// the file instead. Idea from Underworld Revisited (PQMarine, MIT): Tools/UWSelfCheck/Pins/rule-constants.txt.
/// </summary>
[Collection("Rules")]
public class RulePinTests
{
    readonly ITestOutputHelper output;
    public RulePinTests(ITestOutputHelper output) { this.output = output; }

    /// <summary>Every row of every rule function, for a full diff between two commits: written when
    /// the pins differ, or on any run with PINS_ROWS=1. It is a build output, not committed.</summary>
    static string RowsFile => Path.Combine(AppContext.BaseDirectory, "rule-rows.txt");

    [Fact]
    public void Rule_values_and_outputs_match_the_pins()
    {
        var values = NamedValuePins.Collect();
        var problems = new List<string>();
        var rows = new Dictionary<string, List<RuleOutputPins.Row>>();
        var rules = RuleOutputPins.Collect(problems, rows);
        var live = values.Pins.Select(p => p.Line).Concat(rules).ToList();

        foreach (var g in values.Pins.GroupBy(p => p.Folder).OrderBy(g => g.Key, StringComparer.Ordinal))
            output.WriteLine($"values {g.Key}: {g.Count()} ({string.Join(", ", g.GroupBy(p => p.Kind).OrderBy(k => k.Key).Select(k => $"{k.Count()} {k.Key}"))})");
        foreach (var (why, n) in values.LeftOut) output.WriteLine($"left out {why}: {n} fields of a pinnable type");
        output.WriteLine($"values: {values.Pins.Count}; rules: {rows.Count} (function, game) pairs, {rows.Values.Sum(r => r.Count)} rows, {rules.Count} lines");
        foreach (var u in values.Unreadable) output.WriteLine("not readable: " + u);
        foreach (var p in problems) output.WriteLine("problem: " + p);

        var dup = live.GroupBy(PinFile.Key).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.True(dup.Count == 0, "two pins share a key: " + string.Join("; ", dup));
        Assert.True(problems.Count == 0, "rule pin cases need fixing:\n" + string.Join("\n", problems));

        var path = PinFile.PathInSource();
        if (Environment.GetEnvironmentVariable(PinFile.RecordVariable) == "1")
        {
            PinFile.Write(path, live);
            output.WriteLine($"recorded {live.Count} pins to {path}");
            return;
        }
        Assert.True(File.Exists(path), $"no pin file at {path}. Record it with {PinFile.RecordVariable}=1.");
        var committed = PinFile.Read(path);
        var diffs = PinFile.Compare(committed, live);
        if (diffs.Count > 0 || Environment.GetEnvironmentVariable("PINS_ROWS") == "1")
            File.WriteAllLines(RowsFile, rows.SelectMany(kv => kv.Value.Select(r => $"{kv.Key} {r.Input} = {r.Output}")));
        if (diffs.Count == 0) return;

        var old = committed.ToDictionary(PinFile.Key, PinFile.Value);
        var explained = diffs.Select(d => d + WhereRowsChanged(d, old, rows)).ToList();
        Assert.Fail(
            $"{diffs.Count} rule pins differ from tests/Underworld.Rules.Tests/pins/rule-pins.txt:\n\n" +
            string.Join("\n", explained) +
            $"\n\nEvery row of this run is in {RowsFile}; run the test on the previous commit with PINS_ROWS=1 and diff the two for every changed row." +
            "\n\nIf the change is deliberate, re-record with\n" +
            $"    {PinFile.RecordVariable}=1 dotnet test tests/Underworld.Rules.Tests --filter RulePinTests\n" +
            "and commit the pin file in the same commit as the change, so the diff shows what it changed. " +
            "If it is not deliberate, the change is the bug: a slip or a wrong merge resolution.");
    }

    /// <summary>For a changed rule hash line: which runs of inputs changed, and what the first rows
    /// of the first changed run give now.</summary>
    static string WhereRowsChanged(string diff, Dictionary<string, string> old, Dictionary<string, List<RuleOutputPins.Row>> rows)
    {
        var key = diff.Split('\n')[0][8..];
        if (!old.TryGetValue(key, out var before) || !rows.TryGetValue(key, out var now)) return "";
        var oldParts = RuleOutputPins.PartHashes(before);
        if (oldParts == null || oldParts.Length != RuleOutputPins.PartCount || !before.StartsWith($"{now.Count} rows ", StringComparison.Ordinal))
            return "";
        var parts = RuleOutputPins.Parts(now);
        var changed = Enumerable.Range(0, parts.Count).Where(k => RuleOutputPins.PartHash(parts[k]) != oldParts[k]).ToList();
        if (changed.Count == 0) return "";
        var ranges = changed.Select(k => $"{parts[k][0].Input} to {parts[k][^1].Input}");
        var first = parts[changed[0]];
        return $"\n    changed inputs: {string.Join(", ", ranges)}" +
               $"\n    now, first of those: {string.Join("; ", first.Take(4).Select(r => $"{r.Input} = {r.Output}"))}" +
               (first.Count > 4 ? $"; ... {first[^1].Input} = {first[^1].Output}" : "");
    }
}
