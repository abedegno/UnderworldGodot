using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Underworld.Rules.Tests;

// Idea from Underworld Revisited (PQMarine, MIT): Tools/UWRuleIndex, an index of the original's
// routines that flags one implemented twice.

/// <summary>
/// One declaration in the port that implements one of the original's routines. Routine is the
/// key "uwN:name@FILE" (the original name and the matched source file from Underworld Exhumed's
/// crosswalk), or "uwN:segNNN_OFF" / "uwN:dseg_PPPP_OFF" when the crosswalk has no routine there.
/// How says why the scanner counts it: "name" (the declaration's name carries the address),
/// "cite" (its comment cites the line where the routine is defined) or "comment" (its comment
/// gives the routine's address as what it is).
/// </summary>
public sealed record RuleImplementation(string Routine, string How, string Path, string Member, int Line)
{
    /// <summary>The declaration as the expected file names it: path#Name.</summary>
    public string Id => Path + "#" + Member;
}

/// <summary>
/// The original's routines per game, from Underworld Exhumed's crosswalks (ruleindex/crosswalk-uwN.tsv,
/// copied by sync-crosswalk.sh): each routine's label and address in hankmorgan's disassembly, its
/// original name, and the file and line where the matched source defines it.
/// </summary>
public sealed class Crosswalk
{
    public static readonly string[] Games = { "uw1", "uw2" };

    // A code address as hankmorgan names it: seg006_1413_2BF5 (segment, paragraph, offset),
    // seg019_A78 (no paragraph) or ovr108_B8E. The port sometimes drops the paragraph or a digit
    // of the segment number (seg032_683, seg32_1014), so both are optional when matching.
    public static readonly Regex Address = new(@"(?<![dA-Za-z])(seg|ovr)(\d{2,3})(?:_([0-9A-Fa-f]{4}))?_([0-9A-Fa-f]+)(?![0-9A-Fa-f])");

    // A global in the data segment: dseg_5c99 is UW1's, dseg_67d6 UW2's (the segment names in
    // UW1_asm.asm and uw2_asm.asm).
    public static readonly Regex DataAddress = new(@"dseg_([0-9A-Fa-f]{4})_([0-9A-Fa-f]+)(?![0-9A-Fa-f])");
    static readonly Dictionary<string, string> DataSegments = new() { ["5C99"] = "uw1", ["67D6"] = "uw2" };

    readonly Dictionary<(string game, string kind, int seg, int off), string> byAddress = new();
    readonly HashSet<(string game, int seg, string para)> paragraphs = new();
    readonly Dictionary<(string game, string file), List<(int line, string routine)>> definitions = new();

    public static Crosswalk Load(string dir)
    {
        var cw = new Crosswalk();
        foreach (var game in Games)
            foreach (var l in File.ReadLines(System.IO.Path.Combine(dir, $"crosswalk-{game}.tsv")))
            {
                if (l.Length == 0 || l[0] == '#') continue;
                var c = l.Split('\t');
                if (c.Length < 4) throw new InvalidDataException($"crosswalk-{game}.tsv: '{l}'");
                string file = null; int line = 0;
                var at = c[3].LastIndexOf(':');
                if (at > 0 && int.TryParse(c[3][(at + 1)..], out line)) file = System.IO.Path.GetFileName(c[3][..at]).ToUpperInvariant();
                var routine = file == null ? $"{game}:{c[2]}" : $"{game}:{c[2]}@{file}";
                // The listing name is the label the port copies; the listing address is the routine's
                // true start, which differs from the label for a few routines.
                foreach (var label in new[] { c[0], c[1] })
                    foreach (Match m in Address.Matches(label))
                    {
                        var key = (game, m.Groups[1].Value, int.Parse(m.Groups[2].Value), Convert.ToInt32(m.Groups[4].Value, 16));
                        cw.byAddress.TryAdd(key, routine);
                        if (m.Groups[3].Success) cw.paragraphs.Add((game, key.Item3, m.Groups[3].Value.ToUpperInvariant()));
                    }
                if (file != null)
                {
                    if (!cw.definitions.TryGetValue((game, file), out var list)) cw.definitions[(game, file)] = list = new();
                    list.Add((line, routine));
                }
            }
        return cw;
    }

    /// <summary>
    /// The routines a code address can be. The paragraph, when given, says the game (no segment has
    /// the same paragraph in both); without one the address is looked up in both games, and a
    /// game named in the comment around it (hint) settles a tie. An address the crosswalk does not
    /// know is kept as it is, so two declarations named after it still meet.
    /// </summary>
    public IReadOnlyList<string> ResolveCode(Match m, string hint)
    {
        var kind = m.Groups[1].Value; var seg = int.Parse(m.Groups[2].Value);
        var off = Convert.ToInt32(m.Groups[4].Value, 16);
        var games = Games.ToList();
        if (m.Groups[3].Success)
        {
            var para = m.Groups[3].Value.ToUpperInvariant();
            var known = games.Where(g => paragraphs.Contains((g, seg, para))).ToList();
            if (known.Count > 0) games = known;
        }
        var found = games.Where(g => byAddress.ContainsKey((g, kind, seg, off))).ToList();
        if (found.Count > 1 && hint != null && found.Contains(hint)) found = new() { hint };
        if (found.Count > 0) return found.Select(g => byAddress[(g, kind, seg, off)]).ToList();
        var game = games.Count == 1 ? games[0] : hint ?? "uw?";
        return new[] { $"{game}:{kind}{seg:D3}_{off:X}" };
    }

    /// <summary>A data address: "uwN:dseg_PPPP_OFF".</summary>
    public static string ResolveData(Match m)
    {
        var para = m.Groups[1].Value.ToUpperInvariant();
        var game = DataSegments.TryGetValue(para, out var g) ? g : "uw?";
        return $"{game}:dseg_{para.ToLowerInvariant()}_{Convert.ToInt32(m.Groups[2].Value, 16):X}";
    }

    /// <summary>
    /// The routine whose definition a cited line starts, or null. A citation that starts within
    /// two lines above the routine's signature (its comment) or on the line after it cites the
    /// whole routine; one that starts further in cites a part of it, or a call, and does not count.
    /// </summary>
    public string DefinedAt(string game, string file, int line)
    {
        if (!definitions.TryGetValue((game, file), out var list)) return null;
        foreach (var (start, routine) in list)
            if (line >= start - 2 && line <= start + 1) return routine;
        return null;
    }
}

/// <summary>
/// Reads the port's C# and lists every declaration that implements one of the original's
/// routines: a method or field whose name carries a disassembly address (SquareRoot_seg019_A78,
/// BitFieldForPathing_dseg_67d6_B4), and a method whose comment says which routine it is, by a
/// citation of the line that defines it in Underworld Exhumed (crit_avoid_player, uw1 AI.C:685-713)
/// or by its address alone on a line or after "implements", "matching", "Source:" and the like.
/// A comment is the one directly above the declaration, on its line, or on its opening brace.
/// </summary>
public static class RuleIndexScan
{
    static readonly string Modifiers = @"(?:(?:public|private|protected|internal|static|virtual|override|abstract|async|unsafe|extern|new|sealed|partial|readonly|const|volatile)\s+)*";
    static readonly Regex Method = new(@"^\s*(?:\[[^\]]*\]\s*)*" + Modifiers + @"(?<type>[\w.]+(?:<[^()]*?>)?(?:\[[,\s]*\])*\??)\s+(?<name>[A-Za-z_]\w*)\s*(?:<[^()]*?>)?\s*\((?<rest>.*)$");
    static readonly Regex Field = new(@"^\s*(?:\[[^\]]*\]\s*)*" + Modifiers + @"(?<type>[\w.]+(?:<[^()]*?>)?(?:\[[,\s]*\])*\??)\s+(?<name>[A-Za-z_]\w*)\s*(?:=|;)");
    static readonly HashSet<string> NotTypes = new() { "return", "new", "else", "case", "goto", "throw", "await", "using", "namespace", "var", "yield", "in", "out", "ref", "is", "as", "class", "struct", "enum", "interface", "record", "delegate", "event", "operator" };
    static readonly HashSet<string> NotNames = new() { "if", "while", "for", "foreach", "switch", "catch", "using", "lock", "nameof", "typeof", "sizeof", "default", "when", "fixed", "checked", "unchecked", "return" };

    // A game named in a comment: uw1, UW2, UW1's, uw2/src/...
    static readonly Regex GameWord = new(@"(?<![A-Za-z0-9])(?:uw|UW)([12])(?![0-9])");
    // The pieces of a citation, read left to right: "uw1 183" (a line in the file named last),
    // "uw1", "FILE.C:123" (with or without a path), and ":408" (another line in the same file).
    static readonly Regex CitationPiece = new(
        @"(?<gl>(?<![A-Za-z0-9])(?:uw|UW)(?<glg>[12])\s+(?<gln>\d+)\b)" +
        @"|(?<g>(?<![A-Za-z0-9])(?:uw|UW)(?<gg>[12])(?![0-9]))" +
        @"|(?<f>(?<![\w.])(?:\w+/)*(?<ff>[A-Z][A-Z0-9_]*\.(?:C|ASM))\b(?::(?<fl>\d+))?)" +
        @"|(?<c>(?<![\w)\]]):(?<cl>\d+))");
    // Words that say the address that follows is what the declaration is.
    static readonly Regex ImplementsWord = new(@"\b(?:[Ii]mplements?|[Mm]atching|[Mm]atches|[Pp]ort of|[Ss]ource:|[Oo]riginal:?|as DOS does in|[Ee]quivalent of|[Mm]irrors)\b");

    /// <summary>The C# files the game is built from: src/ and main.cs, as paths relative to root.</summary>
    public static IEnumerable<string> SourceFiles(string root)
    {
        var files = Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/')).ToList();
        if (File.Exists(Path.Combine(root, "main.cs"))) files.Add("main.cs");
        return files.OrderBy(f => f, StringComparer.Ordinal);
    }

    public static List<RuleImplementation> Scan(string root, Crosswalk cw) =>
        SourceFiles(root).SelectMany(f => ScanFile(f, File.ReadAllLines(Path.Combine(root, f)), cw)).ToList();

    public static IEnumerable<RuleImplementation> ScanFile(string path, string[] lines, Crosswalk cw)
    {
        var code = CodeOnly(lines);
        var found = new List<RuleImplementation>();
        for (int i = 0; i < lines.Length; i++)
        {
            var c = code[i];
            if (c.Trim().Length == 0) continue;
            string name; bool isMethod;
            var m = Method.Match(c);
            if (m.Success && !NotTypes.Contains(m.Groups["type"].Value) && !NotNames.Contains(m.Groups["name"].Value)
                && !(m.Groups["rest"].Value.TrimEnd().EndsWith(";") && !m.Groups["rest"].Value.Contains("=>")))
            { name = m.Groups["name"].Value; isMethod = true; }
            else
            {
                var f = Field.Match(c);
                if (!f.Success || NotTypes.Contains(f.Groups["type"].Value)) continue;
                name = f.Groups["name"].Value; isMethod = false;
                if (!Crosswalk.Address.IsMatch(name) && !Crosswalk.DataAddress.IsMatch(name)) continue;
            }

            var parts = CommentParts(lines, code, i);
            var comment = parts.SelectMany(p => p).ToList();
            var hint = SingleGame(comment);
            var keys = new List<(string routine, string how)>();
            var d = Crosswalk.DataAddress.Match(name);
            if (d.Success) keys.Add((Crosswalk.ResolveData(d), "name"));
            else
            {
                var a = Crosswalk.Address.Match(name);
                if (a.Success) keys.AddRange(cw.ResolveCode(a, hint).Select(r => (r, "name")));
            }
            if (isMethod)
            {
                foreach (var part in parts)
                    keys.AddRange(ClaimedByCitation(part, name, cw).Select(r => (r, "cite")));
                foreach (var cl in comment)
                    foreach (Match a in Crosswalk.Address.Matches(cl))
                        if (SaysItIs(cl, a)) keys.AddRange(cw.ResolveCode(a, SingleGame(new[] { cl }) ?? hint).Select(r => (r, "comment")));
            }
            foreach (var k in keys.DistinctBy(k => k.routine))
                found.Add(new RuleImplementation(k.routine, k.how, path, name, i + 1));
        }
        return found;
    }

    /// <summary>Each line with comments and string contents blanked, keeping block comment state.</summary>
    static string[] CodeOnly(string[] lines)
    {
        var o = new string[lines.Length]; bool block = false;
        for (int i = 0; i < lines.Length; i++)
        {
            var s = lines[i]; var sb = new StringBuilder();
            for (int j = 0; j < s.Length; j++)
            {
                if (block) { if (s[j] == '*' && j + 1 < s.Length && s[j + 1] == '/') { block = false; j++; } continue; }
                if (s[j] == '/' && j + 1 < s.Length && s[j + 1] == '/') break;
                if (s[j] == '/' && j + 1 < s.Length && s[j + 1] == '*') { block = true; j++; continue; }
                if (s[j] == '"')
                {
                    // Keep the length, so that code[i].Length is where a trailing comment starts.
                    sb.Append('"'); j++;
                    while (j < s.Length && s[j] != '"')
                    {
                        if (s[j] == '\\' && j + 1 < s.Length) { sb.Append(' '); j++; }
                        sb.Append(' '); j++;
                    }
                    if (j < s.Length) sb.Append('"');
                    continue;
                }
                sb.Append(s[j]);
            }
            o[i] = sb.ToString();
        }
        return o;
    }

    /// <summary>
    /// The comments of the declaration on line i, each a list of lines: the comment directly above
    /// it (attributes between are skipped), split where an XML doc tag starts (summary, returns and
    /// so on), the comment on line i itself, and the comment on the opening brace when that is the
    /// next line.
    /// </summary>
    static List<List<string>> CommentParts(string[] lines, string[] code, int i)
    {
        var above = new List<string>();
        for (int j = i - 1; j >= 0; j--)
        {
            var t = lines[j].Trim();
            if (t.StartsWith("//")) above.Insert(0, t.TrimStart('/').Trim());
            else if (t.StartsWith("[") && t.EndsWith("]")) continue;
            else break;
        }
        var parts = new List<List<string>> { new() };
        foreach (var l in above)
        {
            var pieces = Regex.Split(l, @"<(?!/)\w+[^>]*>");
            for (int k = 0; k < pieces.Length; k++)
            {
                if (k > 0) parts.Add(new());
                var text = Regex.Replace(pieces[k], @"</?\w+[^>]*>", " ").Trim();
                if (text.Length > 0) parts[^1].Add(text);
            }
        }
        void Tail(int j)
        {
            var at = code[j].Length;
            if (string.CompareOrdinal(lines[j], at, "//", 0, 2) == 0) parts.Add(new() { lines[j][(at + 2)..].TrimStart('/').Trim() });
        }
        Tail(i);
        if (i + 1 < lines.Length && code[i + 1].Trim() == "{") Tail(i + 1);
        return parts.Where(p => p.Count > 0).ToList();
    }

    /// <summary>
    /// The routines one comment says the declaration is, by citing the line that defines them. A
    /// citation counts when it is in the comment's first sentence, on a line that holds only
    /// citations, directly after the routine's original name ("crit_avoid_player (uw1 AI.C:685",
    /// "(BonesLook, uw1 LOOK.C:323"), anywhere in a comment that starts with that name
    /// ("clear_paths: frees ..."), or anywhere when the declaration has the routine's name. A
    /// citation further into the prose is usually of a routine the declaration calls, is called
    /// from or stands in for ("moveto in the original, uw1 SKILLS.C:769"), and does not count.
    /// </summary>
    static IEnumerable<string> ClaimedByCitation(List<string> part, string member, Crosswalk cw)
    {
        var text = string.Join(" ", part);
        var firstEnd = FirstSentenceEnd(text);
        var lineStarts = new List<int>(); int pos = 0;
        foreach (var l in part) { lineStarts.Add(pos); pos += l.Length + 1; }
        foreach (var (game, file, line, at) in Citations(text))
            foreach (var g in game == null ? Crosswalk.Games : new[] { game })
            {
                if (cw.DefinedAt(g, file, line) is not string routine) continue;
                // Exhumed keeps the listing's address on a routine with no original name
                // (ReturnTradeObjectsToNPC_ovr097_F76); a comment gives the name without it.
                var full = routine[(routine.IndexOf(':') + 1)..].Split('@')[0];
                var name = Regex.Replace(full, @"_+(?:seg|ovr)\d+(?:_[0-9A-Fa-f]+)+$", "");
                if (name.Length == 0) name = full;
                var onLine = part[lineStarts.FindLastIndex(s => s <= at)];
                if (at < firstEnd || !Regex.IsMatch(CitationPiece.Replace(onLine, ""), "[A-Za-z]")
                    || Regex.IsMatch(text, @"^(?:(?:UW[12]|uw[12])'s\s+)?" + Regex.Escape(name) + @"\b")
                    || Regex.IsMatch(text[..at], @"\b" + Regex.Escape(name) + @"\s*[(,:]?\s*(?:(?:uw|UW)[12][\s/]*)?(?:underworld-exhumed\s+)?$")
                    || LabelGroup(text, at) is string label && Regex.IsMatch(label, @"[(,;]\s*" + Regex.Escape(name) + @"\s*[,;:)]")
                    || string.Equals(member.Replace("_", ""), name.Replace("_", ""), StringComparison.OrdinalIgnoreCase))
                    yield return routine;
            }
    }

    /// <summary>
    /// The bracket group that labels the citation at: the one it is in, or else one that follows
    /// it with only more citations between ("uw2 WORLDEV.C:1649-1660 (destroy_floatskull)").
    /// </summary>
    static string LabelGroup(string text, int at)
    {
        int depth = 0;
        for (int k = at - 1; k >= 0; k--)
        {
            if (text[k] == ')') depth++;
            else if (text[k] == '(' && depth-- == 0)
            {
                int end = text.IndexOf(')', at);
                return end < 0 ? text[k..] : text[k..(end + 1)];
            }
        }
        var open = text.IndexOf('(', at);
        if (open < 0) return null;
        var between = CitationPiece.Replace(text[at..open], "");
        if (Regex.IsMatch(between, @"[^\s,;\-\d]") && !Regex.IsMatch(between, @"^[\s,;\-\d]*(?:and[\s,;\-\d]*)*$")) return null;
        var close = text.IndexOf(')', open);
        return close < 0 ? text[open..] : text[open..(close + 1)];
    }

    /// <summary>Where the first sentence ends: the first full stop followed by a space or the end, outside brackets.</summary>
    static int FirstSentenceEnd(string text)
    {
        int depth = 0;
        for (int k = 0; k < text.Length; k++)
        {
            var ch = text[k];
            if (ch == '(') depth++;
            else if (ch == ')' && depth > 0) depth--;
            else if ((ch == '.' || ch == '!' || ch == '?') && depth == 0 && (k + 1 == text.Length || text[k + 1] == ' ')) return k;
        }
        return text.Length;
    }

    /// <summary>The game a comment names, when it names only one.</summary>
    static string SingleGame(IEnumerable<string> comment)
    {
        var games = comment.SelectMany(l => GameWord.Matches(l).Select(m => "uw" + m.Groups[1].Value)).Distinct().ToList();
        return games.Count == 1 ? games[0] : null;
    }

    /// <summary>Every (game, FILE, line) the text cites, and where; game is null when no game was named before it.</summary>
    public static IEnumerable<(string game, string file, int line, int at)> Citations(string text)
    {
        string game = null, file = null;
        foreach (var m in CitationPiece.Matches(text).Cast<Match>())
        {
            if (m.Groups["gl"].Success)
            {
                game = "uw" + m.Groups["glg"].Value;
                if (file != null) yield return (game, file, int.Parse(m.Groups["gln"].Value), m.Index);
            }
            else if (m.Groups["g"].Success) game = "uw" + m.Groups["gg"].Value;
            else if (m.Groups["f"].Success)
            {
                file = m.Groups["ff"].Value;
                if (m.Groups["fl"].Success) yield return (game, file, int.Parse(m.Groups["fl"].Value), m.Index);
            }
            else if (file != null) yield return (game, file, int.Parse(m.Groups["cl"].Value), m.Index);
        }
    }

    /// <summary>
    /// Whether a comment line gives the address as what the declaration is: the address (in a
    /// name or alone) is all the line holds, ends a line after a full stop, or follows a word such
    /// as "implements", "matching" or "Source:". An address in any other sentence is a call or a
    /// reference and does not count.
    /// </summary>
    static bool SaysItIs(string line, Match a)
    {
        var l = Regex.Replace(line, @"</?\w+[^>]*>", " ");
        var word = Regex.Match(l, @"\S*" + Regex.Escape(a.Value) + @"\S*");
        if (!word.Success) return false;
        var before = l[..word.Index]; var after = l[(word.Index + word.Length)..];
        if (Regex.IsMatch(after, @"[A-Za-z]") && !Regex.IsMatch(after, @"^\s*\((?:UW1|UW2|uw1|uw2)?_?asm\.asm[^)]*\)\s*[.,;:]?\s*$")) return ImplementsWord.IsMatch(before);
        var lead = Regex.Replace(before, @"(?<![A-Za-z0-9])(?:uw|UW)[12](?:'s)?", "");
        if (!Regex.IsMatch(lead, @"[A-Za-z]")) return true;
        return Regex.IsMatch(before, @"\.\s+$") || ImplementsWord.IsMatch(before);
    }

    /// <summary>The routines with more than one declaration, by routine, the declarations by Id.</summary>
    public static Dictionary<string, List<RuleImplementation>> Duplicates(IEnumerable<RuleImplementation> index) =>
        index.GroupBy(r => r.Routine)
            .Select(g => (g.Key, list: g.DistinctBy(r => r.Id).OrderBy(r => r.Id, StringComparer.Ordinal).ToList()))
            .Where(g => g.list.Count > 1)
            .ToDictionary(g => g.Key, g => g.list);

    /// <summary>The index as Markdown: one row per routine, with every declaration that implements it.</summary>
    public static string Markdown(IReadOnlyList<RuleImplementation> index, string source)
    {
        var sb = new StringBuilder();
        var routines = index.GroupBy(r => r.Routine).OrderBy(g => g.Key, StringComparer.Ordinal).ToList();
        sb.Append("# Rules index\n\n");
        sb.Append("The original's routines that UnderworldGodot implements, and where. Written by `RULES_INDEX_RECORD=1 dotnet test --filter RuleIndex` in tests/Underworld.Rules.Tests; do not edit by hand. Routine names and files are Underworld Exhumed's (").Append(source).Append(").\n\n");
        sb.Append($"{routines.Count} routines in {index.Select(r => r.Id).Distinct().Count()} declarations: {routines.Count(g => g.Key.StartsWith("uw1:"))} UW1, {routines.Count(g => g.Key.StartsWith("uw2:"))} UW2. ");
        sb.Append($"{routines.Count(g => g.Select(r => r.Id).Distinct().Count() > 1)} have more than one declaration (see EXPECTED_DUPLICATES).\n\n");
        sb.Append("How: `name` the declaration's name carries the address, `cite` its comment cites the routine's definition, `comment` its comment gives the address.\n\n");
        sb.Append("| Routine | Implemented at | How |\n|---|---|---|\n");
        foreach (var g in routines)
            foreach (var r in g.OrderBy(r => r.Path, StringComparer.Ordinal).ThenBy(r => r.Line))
                sb.Append($"| `{g.Key}` | `{r.Path}:{r.Line}` {r.Member} | {r.How} |\n");
        return sb.ToString();
    }
}

/// <summary>
/// ruleindex/EXPECTED_DUPLICATES: routines that more than one declaration implements, each with
/// its declarations and why: "routine | path#Member path#Member | reason".
/// </summary>
public sealed record ExpectedDuplicate(string Routine, IReadOnlySet<string> Members, string Reason, int Line)
{
    /// <summary>A reason starts with one of these.</summary>
    public static readonly string[] Kinds = { "UW1 and UW2 variant", "wrapper", "deliberate split", "duplicate, to review" };

    public static List<ExpectedDuplicate> Load(string path)
    {
        var list = new List<ExpectedDuplicate>();
        if (!File.Exists(path)) return list;
        var lines = File.ReadAllLines(path);
        for (int i = 0; i < lines.Length; i++)
        {
            var l = lines[i].Trim();
            if (l.Length == 0 || l.StartsWith("#")) continue;
            var p = l.Split('|');
            if (p.Length != 3) throw new InvalidDataException($"EXPECTED_DUPLICATES:{i + 1}: want 'routine | members | reason': '{l}'");
            list.Add(new ExpectedDuplicate(p[0].Trim(),
                p[1].Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(), p[2].Trim(), i + 1));
        }
        return list;
    }
}
