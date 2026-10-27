using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

namespace Underworld.Rules.Tests;

/// <summary>
/// The committed pin file, pins/rule-pins.txt: one "key = value" line per pin, sorted, with a
/// comment header. Compare reports each changed, added and removed key.
/// Idea from Underworld Revisited (PQMarine, MIT): Tools/UWSelfCheck/Pins/rule-constants.txt.
/// </summary>
public static class PinFile
{
    public const string RecordVariable = "PINS_RECORD";

    /// <summary>The file in the source tree, so recording rewrites the committed copy; the copy in
    /// the build output when the source tree is not where this file was compiled.</summary>
    public static string PathInSource([CallerFilePath] string here = "")
    {
        var source = Path.Combine(Path.GetDirectoryName(here)!, "pins", "rule-pins.txt");
        return File.Exists(source) || Directory.Exists(Path.GetDirectoryName(here)) ? source : Path.Combine(AppContext.BaseDirectory, "pins", "rule-pins.txt");
    }

    static readonly string[] Header =
    {
        "# UnderworldGodot rule pins. Written by RulePinTests; do not edit by hand.",
        "# value: a named constant or table of the game assembly. rule: a rule function's outputs over a fixed grid of inputs.",
        "# Re-record with PINS_RECORD=1 dotnet test tests/Underworld.Rules.Tests --filter RulePinTests",
        "# and commit this file in the same commit as the deliberate change. See the README.",
    };

    public static void Write(string path, IEnumerable<string> lines)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Join("\n", Header.Concat(lines)) + "\n");
    }

    public static IReadOnlyList<string> Read(string path) =>
        File.ReadAllLines(path).Where(l => l.Length > 0 && !l.StartsWith('#')).ToList();

    public static string Key(string line)
    {
        int i = line.IndexOf(" = ", StringComparison.Ordinal);
        return i < 0 ? line : line[..i];
    }

    public static string Value(string line)
    {
        int i = line.IndexOf(" = ", StringComparison.Ordinal);
        return i < 0 ? "" : line[(i + 3)..];
    }

    /// <summary>Every difference, one block per key, in file order; empty when they match.</summary>
    public static List<string> Compare(IReadOnlyList<string> committed, IReadOnlyList<string> live)
    {
        var old = committed.ToDictionary(Key, Value);
        var now = live.ToDictionary(Key, Value);
        var diffs = new List<string>();
        foreach (var key in committed.Select(Key).Concat(live.Select(Key)).Distinct().OrderBy(k => k, StringComparer.Ordinal))
        {
            bool inOld = old.TryGetValue(key, out var o), inNew = now.TryGetValue(key, out var n);
            if (inOld && inNew && o == n) continue;
            if (inOld && inNew) diffs.Add($"changed {key}\n    old: {o}\n    new: {n}");
            else if (inNew) diffs.Add($"added   {key}\n    new: {n}");
            else diffs.Add($"removed {key}\n    old: {o}");
        }
        return diffs;
    }

    public static string Hash(string text) =>
        "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16].ToLowerInvariant();
}
