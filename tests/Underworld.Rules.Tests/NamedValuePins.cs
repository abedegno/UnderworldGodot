using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.CompilerServices;
using Underworld;

namespace Underworld.Rules.Tests;

/// <summary>
/// The "value" pins: the game assembly's named rule values, found by reflection.
///
/// A field is pinned when all of these hold:
/// - its type is declared in a source file under src/, outside the folders in ExcludedFolders
///   (the folder comes from the game's PDB, so a type moved between folders moves with it),
///   and is not a drawing or settings type (ExcludedType);
/// - it is const, static readonly, or a plain static array whose start-up contents are not all
///   zero (an all-zero array is working memory; many of the original's tables are plain static
///   arrays, so readonly alone would miss them). Plain static scalars are game state, not rules;
/// - its type is a number, bool, string or enum, or a one-dimensional array of those.
/// Arrays of up to ShortArray elements are written in full, longer ones as length and hash.
/// Idea from Underworld Revisited (PQMarine, MIT): Tools/UWSelfCheck/Pins/rule-constants.txt.
/// </summary>
public static class NamedValuePins
{
    /// <summary>Folders under src/ that hold no rules: screen layout and menus, sound and music,
    /// cutscene playback, file readers and caches, and the save file format (which
    /// Underworld.Save.Tests checks). Everything outside src/ (main.cs, the scenario code, Godot's
    /// generated files) is also left out: engine set-up, not rules.</summary>
    public static readonly string[] ExcludedFolders = { "ui", "audio", "cuts", "loaders", "savegame" };

    /// <summary>Inside the rule folders, also leave out drawing (a type named ...Render) and the
    /// settings file's types.</summary>
    public static bool ExcludedType(Type t) =>
        t.Name.EndsWith("Render", StringComparison.Ordinal) || t.Name is "uwsettings" or "JsonReadOutcome";

    public const int ShortArray = 32;

    public sealed record Pin(string Folder, string Kind, string Line);

    public sealed record Result(List<Pin> Pins, List<string> Unreadable, SortedDictionary<string, int> LeftOut);

    static readonly BindingFlags Statics = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    public static Result Collect()
    {
        var asm = typeof(UWClass).Assembly;
        var folders = SourceFolders.Of(asm);
        var pins = new List<Pin>();
        var unreadable = new List<string>();
        var leftOut = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var t in Types(asm))
        {
            if (t.IsDefined(typeof(CompilerGeneratedAttribute)) || t.Name.Contains('<')) continue;
            var folder = folders.FolderOf(t);
            string why = folder == null ? "outside src" : ExcludedFolders.Contains(folder) ? folder : null;
            for (var c = t; c != null && why == null; c = c.DeclaringType) if (ExcludedType(c)) why = c.Name;
            if (why != null)
            {
                int n = t.GetFields(Statics).Count(f => !f.Name.Contains('<') && Eligible(f.FieldType) && (f.IsLiteral || f.IsInitOnly || f.FieldType.IsArray));
                if (n > 0) leftOut[why] = leftOut.GetValueOrDefault(why) + n;
                continue;
            }
            var whyNot = WhyNotInitialise(t);
            foreach (var f in t.GetFields(Statics))
            {
                if (f.IsDefined(typeof(CompilerGeneratedAttribute)) || f.Name.Contains('<')) continue;
                if (!Eligible(f.FieldType)) continue;
                string kind = f.IsLiteral ? "const" : f.IsInitOnly ? "readonly" : "static";
                if (kind == "static" && !f.FieldType.IsArray) continue;
                object value;
                if (f.IsLiteral) value = f.GetRawConstantValue();
                else
                {
                    if (whyNot != null) { unreadable.Add($"{Name(t)}.{f.Name} ({whyNot})"); continue; }
                    try { Seams.RunStaticConstructor(t); value = f.GetValue(null); }
                    catch (Exception e) { unreadable.Add($"{Name(t)}.{f.Name} ({(e.InnerException ?? e).GetType().Name} on start-up)"); continue; }
                    if (kind == "static" && (value == null || AllZero((Array)value))) continue;
                }
                pins.Add(new Pin(folder, kind, $"value {Name(t)}.{f.Name} = {Format(f.FieldType, value)}"));
            }
        }
        pins.Sort((a, b) => string.CompareOrdinal(a.Line, b.Line));
        return new Result(pins, unreadable, leftOut);
    }

    static IEnumerable<Type> Types(Assembly asm)
    {
        try { return asm.GetTypes(); }
        catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t != null); }
    }

    public static string Name(Type t) => t.FullName!.Replace('+', '.');

    static bool Scalar(Type t) => t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal);

    static bool Eligible(Type t) =>
        Scalar(t) && t != typeof(IntPtr) && t != typeof(UIntPtr) ||
        t.IsSZArray && Scalar(t.GetElementType()!);

    static bool FromGodot(Type t) => t.Assembly.GetName().Name!.StartsWith("Godot", StringComparison.Ordinal);

    /// <summary>A type is not initialised when it is a Godot object or its static constructor
    /// (or a base type's) can reach the engine (GodotReach): with no engine running that crashes
    /// the test host instead of throwing.</summary>
    static string WhyNotInitialise(Type t)
    {
        for (var b = t; b != null; b = b.BaseType)
        {
            if (FromGodot(b)) return "it is a Godot object";
            if (b.TypeInitializer is { } cctor && GodotReach.Find(cctor) is { } why) return "its start-up reaches Godot: " + why;
        }
        return null;
    }

    static bool AllZero(Array a)
    {
        foreach (var v in a) if (v != null && (v is string || !Equals(v, Activator.CreateInstance(v.GetType())))) return false;
        return true;
    }

    public static string Format(Type type, object value)
    {
        if (value == null) return "null";
        if (value is Array a)
        {
            var et = type.GetElementType()!;
            var items = a.Cast<object>().Select(Scalar).ToList();
            var head = $"{TypeName(et)}[{a.Length}]";
            return a.Length <= ShortArray ? $"{head} {{{string.Join(", ", items)}}}" : $"{head} {PinFile.Hash(string.Join(",", items))}";
        }
        return Scalar(value);
    }

    public static string Scalar(object v) => v switch
    {
        null => "null",
        string s => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r") + "\"",
        bool b => b ? "true" : "false",
        float f => f.ToString("R", CultureInfo.InvariantCulture),
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        Enum e => Convert.ToInt64(e, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
        IFormattable x => x.ToString(null, CultureInfo.InvariantCulture),
        _ => v.ToString()!,
    };

    static string TypeName(Type t) => t.IsEnum ? t.Name : Type.GetTypeCode(t) switch
    {
        TypeCode.Byte => "byte", TypeCode.SByte => "sbyte", TypeCode.Int16 => "short", TypeCode.UInt16 => "ushort",
        TypeCode.Int32 => "int", TypeCode.UInt32 => "uint", TypeCode.Int64 => "long", TypeCode.UInt64 => "ulong",
        TypeCode.Single => "float", TypeCode.Double => "double", TypeCode.Boolean => "bool", TypeCode.String => "string",
        TypeCode.Char => "char", _ => t.Name,
    };
}

/// <summary>Which folder of the repository each type's source is in, read from the portable PDB
/// beside the game assembly: the document of any of the type's methods, or for a type with no
/// method bodies (an enum, a class of constants) the type's own document record.</summary>
public sealed class SourceFolders
{
    static readonly Guid TypeDefinitionDocuments = new("932E74BC-DBA9-4478-8D46-0F32A7BAB3D3");
    readonly Dictionary<Type, string> folderOf = new();

    public string FolderOf(Type t)
    {
        for (var c = t; c != null; c = c.DeclaringType)
            if (folderOf.TryGetValue(c, out var f)) return f;
        return null;
    }

    public static SourceFolders Of(Assembly asm)
    {
        var pdb = Path.ChangeExtension(asm.Location, ".pdb");
        if (!File.Exists(pdb)) throw new FileNotFoundException("The value pins need the game's PDB beside it to know each type's folder", pdb);
        using var provider = MetadataReaderProvider.FromPortablePdbStream(File.OpenRead(pdb));
        var r = provider.GetMetadataReader();
        var result = new SourceFolders();
        const BindingFlags all = BindingFlags.DeclaredOnly | BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (var t in asm.GetTypes())
        {
            var docs = new HashSet<string>();
            foreach (var m in t.GetMethods(all).Cast<MethodBase>().Concat(t.GetConstructors(all)))
            {
                var info = r.GetMethodDebugInformation(MetadataTokens.MethodDebugInformationHandle(m.MetadataToken & 0xFFFFFF));
                if (!info.Document.IsNil) docs.Add(r.GetString(r.GetDocument(info.Document).Name));
                else foreach (var sp in info.GetSequencePoints()) docs.Add(r.GetString(r.GetDocument(sp.Document).Name));
            }
            foreach (var h in r.GetCustomDebugInformation(MetadataTokens.TypeDefinitionHandle(t.MetadataToken & 0xFFFFFF)))
            {
                var cdi = r.GetCustomDebugInformation(h);
                if (r.GetGuid(cdi.Kind) != TypeDefinitionDocuments) continue;
                var blob = r.GetBlobReader(cdi.Value);
                while (blob.RemainingBytes > 0) docs.Add(r.GetString(r.GetDocument(MetadataTokens.DocumentHandle(blob.ReadCompressedInteger())).Name));
            }
            // A partial type with a Godot-generated half keeps the folder of its hand-written half.
            var folders = docs.Select(Folder).Where(f => f != "").Distinct().ToList();
            if (folders.Count > 0) result.folderOf[t] = folders.Count == 1 ? folders[0] : string.Join("+", folders.OrderBy(f => f, StringComparer.Ordinal));
        }
        return result;
    }

    /// <summary>"physics" for .../src/physics/motion.cs; "" for anything not under src/.</summary>
    static string Folder(string document)
    {
        var p = document.Replace('\\', '/');
        int i = p.LastIndexOf("/src/", StringComparison.Ordinal);
        if (i < 0 || p.Contains("/.godot/") || p.Contains("/tests/")) return "";
        var rest = p[(i + 5)..];
        int slash = rest.IndexOf('/');
        return slash < 0 ? "" : rest[..slash];
    }
}

