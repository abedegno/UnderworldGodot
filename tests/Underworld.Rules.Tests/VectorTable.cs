using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Underworld.Rules.Tests;

/// <summary>A vector table: the header row's column names, then one row of decimal integers per case.</summary>
public static class VectorTable
{
    public static string Dir => Path.Combine(AppContext.BaseDirectory, "vectors");

    public static IReadOnlyList<IReadOnlyDictionary<string, long>> Load(string game, string table)
    {
        var lines = File.ReadAllLines(Path.Combine(Dir, game, table + ".csv"))
            .Where(l => l.Length > 0).ToArray();
        var cols = lines[0].Split(',');
        return lines.Skip(1).Select(l =>
        {
            var v = l.Split(',');
            if (v.Length != cols.Length) throw new InvalidDataException($"{game}/{table}: '{l}'");
            var row = new Dictionary<string, long>();
            for (int i = 0; i < cols.Length; i++) row[cols[i]] = long.Parse(v[i]);
            return (IReadOnlyDictionary<string, long>)row;
        }).ToList();
    }

    /// <summary>Tables listed in vectors/KNOWN_FAILURES ("uwN/table  # what is wrong").</summary>
    public static IReadOnlySet<string> KnownFailures()
    {
        var path = Path.Combine(Dir, "KNOWN_FAILURES");
        if (!File.Exists(path)) return new HashSet<string>();
        return File.ReadAllLines(path).Select(l => l.Split('#')[0].Trim())
            .Where(l => l.Length > 0).ToHashSet();
    }
}
