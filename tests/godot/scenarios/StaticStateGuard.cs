using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Godot;

namespace Underworld.Scenarios;

/// <summary>
/// Puts the game's static fields back to the values a fresh process has, before each scenario.
/// Idea from Underworld Revisited (PQMarine, MIT): Tools/UWSelfCheck/Harness/StateGuard.cs.
///
/// Snapshot runs once, before the first scenario, after the runner has pointed the game at its
/// data (UWClass.BasePath and _RES) and before the game scene exists. It records every static
/// field of the game's own types (the Underworld assembly, minus Exclusions) whose value can be
/// copied safely: numbers, bools, enums, strings, structs made only of those, arrays of any of
/// these, copied deeply, and objects of classes that hold nothing else (plain data, such as
/// motion.playerMotionParams). Restore writes those values back. The other fields hold
/// references, and Restore resets what it safely can of them without copying any object:
///   - one that was null is set to null again, so nothing of an earlier game (a uwObject such
///     as playerdat.CameraReference, a freed node) is still reachable through it;
///   - one that held an empty .NET collection (a List, a Dictionary) has it emptied again;
///   - one that held an array of objects has its elements pointed back at what they held, which
///     is usually null (playerdat.InventoryObjects);
///   - one that held any other object that is not a Godot one is pointed back at that object
///     (MotionHandler.TpActAtStartUp in npc.SpecialMotionHandler), though what is inside that
///     object is not put back.
/// A field holding a Godot object, or a collection or object that was not empty, keeps what it
/// has: the report (SCENARIO_STATE_REPORT) watches those by a fingerprint of what they reach.
///
/// Reading a static field runs its type's static constructor if it has not run yet, so Snapshot
/// runs those constructors early. The game's explicit ones (RunicMagic, PaletteLoader, door,
/// animo, npc, shade, objectDat, commonObjDat, objectCombination, TerrainDatLoader, GameStrings,
/// modelloader, motion, MotionHandler, VisionParams, tileMapRender) and its few initialisers
/// that call code (StereoPanBake, cutsplayer's two Stopwatches) only build tables, read the
/// game's data files from BasePath or load a shader, so they give the same result run before
/// the game scene as at their first use; uwsettings, the one that reads settings.json and sets
/// BasePath and _RES, is excluded and the runner has already run it.
///
/// An array or plain object is restored in place: the field is pointed back at the one it held
/// in the fresh process, if the game has pointed it at another (which is left alone, since
/// something else may own it), and the fresh contents are copied into it. So anything else
/// holding it sees the reset too, and a test of identity (npc.SpecialMotionHandler against
/// MotionHandler.TpActAtStartUp) still holds.
/// </summary>
public static class StaticStateGuard
{
    /// <summary>
    /// The static fields of the game's types that are never restored, each with its reason.
    /// "Type" alone excludes every static field of that type; "Type.field" excludes one field.
    /// Godot objects and nodes need no entry: a field holding one is never copied or pointed
    /// back (it may have been freed with the scene); it is only set back to null if it was null.
    /// </summary>
    static readonly (string Name, string Why)[] Exclusions =
    {
        // Set by the runner for the game it runs, after Snapshot; the snapshot would undo it.
        ("UWClass.BasePath", "the runner points the game at its temporary game folder"),
        ("UWClass._RES", "the runner sets the game (UW1 or UW2) for the whole process"),
        ("uwsettings", "the runner sets the game paths in memory, and its static constructor reads settings.json and sets BasePath and _RES"),
        ("Rng.Source", "the runner installs a seeded BorlandRandSource for each scenario"),
    };

    /// <summary>
    /// Whole namespaces and script folders that are not the game: the runner and the scenarios
    /// keep their own bookkeeping in statics (StartupCutscenes, StartupHadSaves), and the other
    /// Godot tests under tests/godot never run in this process.
    /// </summary>
    static bool IsTestType(Type t)
    {
        if (t.Namespace == "Underworld.Scenarios") return true;
        var path = t.GetCustomAttribute<ScriptPathAttribute>()?.Path;
        return path != null && path.StartsWith("res://tests/", StringComparison.Ordinal);
    }

    sealed class Tracked
    {
        public FieldInfo Field;
        public string Name;
        public object Fresh;   // a private deep copy, never handed to the game
        public object Owned;   // for an array or plain object: the one the field held in the fresh process
    }

    sealed class Watched
    {
        public FieldInfo Field;
        public string Name;
        public string Fresh;    // fingerprint
        public Reset Reset;
        public object Original; // for Clear, ToOriginal and ArrayRefs: the object the field held
        public Array Refs;      // for ArrayRefs: a shallow copy of that array's elements
    }

    /// <summary>What Restore does to a field it cannot copy.</summary>
    enum Reset { None, ToNull, Clear, ToOriginal, ArrayRefs }

    static List<Tracked> _tracked;
    static List<Watched> _watched;
    static readonly Dictionary<Type, bool> _copyable = new();

    /// <summary>Records the fresh values. Returns a one-line description of what it tracks.</summary>
    public static string Snapshot(Assembly game)
    {
        _tracked = new List<Tracked>();
        _watched = new List<Watched>();
        var excluded = Exclusions.Select(e => e.Name).ToHashSet();
        long bytes = 0;
        int arrays = 0, objects = 0, resets = 0;
        foreach (var t in SafeTypes(game))
        {
            if (t.ContainsGenericParameters || IsTestType(t) || IsCompilerGenerated(t)) continue;
            string typeName = DisplayName(t);
            if (excluded.Contains(typeName)) continue;
            foreach (var f in t.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (f.IsLiteral || f.GetCustomAttribute<ThreadStaticAttribute>() != null) continue;
                string name = typeName + "." + FieldName(f);
                if (excluded.Contains(name) || excluded.Contains(typeName + "." + f.Name)) continue;
                if (IsCopyableArray(f.FieldType) || IsPlainClass(f.FieldType) || (!f.IsInitOnly && IsCopyable(f.FieldType)))
                {
                    object v = f.GetValue(null);
                    if (v != null && v.GetType() != f.FieldType && !IsCopyableArray(v.GetType()) && !IsPlainClass(v.GetType()))
                    {
                        // A subclass that is not plain data, held in a field of a plain type.
                        _watched.Add(new Watched { Field = f, Name = name, Fresh = Fingerprint(v) });
                        continue;
                    }
                    _tracked.Add(new Tracked { Field = f, Name = name, Fresh = DeepCopy(v), Owned = IsByReference(v) ? v : null });
                    if (v is Array a) { arrays++; bytes += ApproxBytes(a); }
                    else if (v != null && !v.GetType().IsValueType && v is not string) objects++;
                }
                else if (!f.IsInitOnly || !f.FieldType.IsValueType)
                {
                    object v = f.GetValue(null);
                    var w = new Watched { Field = f, Name = name, Fresh = Fingerprint(v) };
                    // Null in a fresh process: put back to null, so nothing of an earlier game
                    // (a uwObject, a freed node) is still reachable through it. An empty .NET
                    // collection: emptied again. Any other object that is not a Godot one: the
                    // field is pointed back at that same object if the game has pointed it at
                    // another (MotionHandler.TpActAtStartUp in npc.SpecialMotionHandler, for
                    // one), though what is inside it is not put back. A Godot object is only
                    // watched: it may have been freed with the scene.
                    if (v == null && !f.IsInitOnly) w.Reset = Reset.ToNull;
                    else if (IsClearable(v)) { w.Reset = Reset.Clear; w.Original = v; }
                    // An array of objects (playerdat.InventoryObjects, a uwObject[512]): its
                    // elements are pointed back at what they held, which is usually null.
                    else if (v is Array arr && arr.Rank == 1 && (AllNull(arr) || !typeof(GodotObject).IsAssignableFrom(arr.GetType().GetElementType())))
                    { w.Reset = Reset.ArrayRefs; w.Original = v; w.Refs = (Array)arr.Clone(); }
                    else if (v is not GodotObject && !f.IsInitOnly) { w.Reset = Reset.ToOriginal; w.Original = v; }
                    if (w.Reset != Reset.None) resets++;
                    _watched.Add(w);
                }
            }
        }
        return $"{_tracked.Count} statics restored ({arrays} arrays, about {bytes / 1024} KiB, {objects} plain objects), "
             + $"{resets} reset (to null, emptied, or to the object they held), {_watched.Count - resets} only watched, {Exclusions.Length} excluded";
    }

    /// <summary>Writes the fresh values back.</summary>
    public static void Restore()
    {
        foreach (var e in _tracked)
        {
            if (IsByReference(e.Fresh))
            {
                object cur = e.Field.GetValue(null);
                // The field is pointed back at its own array or object (the game may have pointed
                // it at another, which is left alone), and the fresh contents are copied into it.
                if (!ReferenceEquals(cur, e.Owned))
                {
                    if (e.Field.IsInitOnly) continue;
                    e.Field.SetValue(null, e.Owned);
                }
                CopyInto(e.Fresh, e.Owned);
            }
            else if (!e.Field.IsInitOnly)
            {
                e.Field.SetValue(null, e.Fresh);   // a boxed value or a string: nothing shared
                e.Owned = null;
            }
        }
        foreach (var w in _watched)
        {
            if (w.Reset == Reset.ToNull) w.Field.SetValue(null, null);
            else if (w.Reset == Reset.ToOriginal) w.Field.SetValue(null, w.Original);
            else if (w.Reset == Reset.ArrayRefs)
            {
                if (!ReferenceEquals(w.Field.GetValue(null), w.Original))
                {
                    if (w.Field.IsInitOnly) continue;
                    w.Field.SetValue(null, w.Original);
                }
                Array.Copy(w.Refs, (Array)w.Original, w.Refs.Length);
            }
            else if (w.Reset == Reset.Clear)
            {
                object cur = w.Field.GetValue(null);
                if (cur == null || cur.GetType() != w.Original.GetType())
                {
                    if (w.Field.IsInitOnly) continue;
                    cur = w.Original;
                    w.Field.SetValue(null, cur);
                }
                cur.GetType().GetMethod("Clear", Type.EmptyTypes).Invoke(cur, null);
            }
        }
    }

    static bool AllNull(Array a)
    {
        foreach (var e in a) if (e != null) return false;
        return true;
    }

    /// <summary>An empty .NET collection (List, Dictionary, HashSet, Queue, Hashtable...) with a Clear().</summary>
    static bool IsClearable(object v) =>
        v is ICollection { Count: 0 } && v.GetType().Namespace?.StartsWith("System") == true
        && v.GetType().GetMethod("Clear", Type.EmptyTypes) != null;

    /// <summary>One value of the current state, comparable with another taken the same way.</summary>
    public sealed class State
    {
        internal object[] Values;
        internal string[] Prints;
    }

    public static State Capture() => new State
    {
        Values = _tracked.Select(e => SafeCopy(e.Field.GetValue(null))).ToArray(),
        Prints = _watched.Select(w => Fingerprint(w.Field.GetValue(null))).ToArray(),
    };

    /// <summary>The fresh values, as a State, for comparing with.</summary>
    public static State Fresh() => new State
    {
        Values = _tracked.Select(e => e.Fresh).ToArray(),
        Prints = _watched.Select(w => w.Fresh).ToArray(),
    };

    /// <summary>
    /// Every static that differs between a and b: (field name, its cover, description).
    /// The cover is "restored" (copied back), "reset" (put back to null, emptied, or pointed back
    /// at the object it held) or "watched" (not put back at all).
    /// </summary>
    public static List<(string Field, string Cover, string What)> Differences(State a, State b)
    {
        var d = new List<(string, string, string)>();
        for (int i = 0; i < _tracked.Count; i++)
        {
            string what = Describe(a.Values[i], b.Values[i]);
            if (what != null) d.Add((_tracked[i].Name, "restored", what));
        }
        for (int i = 0; i < _watched.Count; i++)
        {
            if (a.Prints[i] != b.Prints[i])
                d.Add((_watched[i].Name, _watched[i].Reset == Reset.None ? "watched" : "reset", $"{b.Prints[i]} (was {a.Prints[i]})"));
        }
        return d;
    }

    /// <summary>A copy for comparing, or a fingerprint if the game has put something that is not plain data there.</summary>
    static object SafeCopy(object v) =>
        v == null || v.GetType().IsValueType || v is string || IsCopyableArray(v.GetType()) || IsPlainClass(v.GetType())
            ? DeepCopy(v) : Fingerprint(v);

    // ---- what can be copied ----

    static bool IsCopyable(Type t)
    {
        if (_copyable.TryGetValue(t, out bool ok)) return ok;
        _copyable[t] = false;   // a struct that contains itself (it cannot, but be safe) is not
        if (t == typeof(string) || t.IsEnum || t == typeof(decimal)) ok = true;
        else if (t.IsPrimitive) ok = t != typeof(IntPtr) && t != typeof(UIntPtr);
        // A Godot Rid names an engine resource; restoring an old one would point at a freed one.
        else if (t == typeof(Rid) || t.IsPointer || t.IsByRefLike) ok = false;
        else if (t.IsValueType)
            ok = t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).All(f => IsCopyable(f.FieldType));
        _copyable[t] = ok;
        return ok;
    }

    static bool IsCopyableArray(Type t) =>
        t.IsArray && (IsCopyable(t.GetElementType())
                      || (t.GetArrayRank() == 1 && IsCopyableArray(t.GetElementType())));

    /// <summary>
    /// A class that is only plain data: not a Godot object, a delegate or a collection, and every
    /// instance field (its base classes' too) copyable, an array of copyable values, or a
    /// delegate. Such an object (UWMotionParamArray, MotionHandler) is restored like an array,
    /// field by field.
    /// </summary>
    static bool IsPlainClass(Type t)
    {
        if (_plain.TryGetValue(t, out bool ok)) return ok;
        _plain[t] = false;
        ok = t.IsClass && !t.IsArray && t != typeof(string) && !t.IsAbstract
             && !typeof(GodotObject).IsAssignableFrom(t) && !typeof(Delegate).IsAssignableFrom(t)
             && !typeof(IEnumerable).IsAssignableFrom(t)
             && InstanceFields(t).All(f => IsCopyable(f.FieldType) || IsCopyableArray(f.FieldType)
                                         || typeof(Delegate).IsAssignableFrom(f.FieldType));   // a delegate cannot change
        _plain[t] = ok;
        return ok;
    }

    static readonly Dictionary<Type, bool> _plain = new();
    static readonly Dictionary<Type, FieldInfo[]> _fields = new();
    static readonly MethodInfo _memberwiseClone = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);

    static FieldInfo[] InstanceFields(Type t)
    {
        if (_fields.TryGetValue(t, out var fs)) return fs;
        var list = new List<FieldInfo>();
        for (var b = t; b != null && b != typeof(object); b = b.BaseType)
            list.AddRange(b.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly));
        return _fields[t] = list.ToArray();
    }

    /// <summary>An array or a plain object: something the field shares, not a copy.</summary>
    static bool IsByReference(object v) => v is Array || (v != null && IsPlainClass(v.GetType()));

    static object DeepCopy(object v)
    {
        if (v is Array a)
        {
            var c = (Array)a.Clone();
            if (a.GetType().GetElementType().IsArray)
                for (int i = 0; i < c.Length; i++) c.SetValue(DeepCopy(a.GetValue(i)), i);
            return c;
        }
        if (!IsByReference(v)) return v;   // boxed values are copies already, strings are immutable
        object o = _memberwiseClone.Invoke(v, null);
        foreach (var f in InstanceFields(v.GetType()))
            if (f.FieldType.IsArray) f.SetValue(o, DeepCopy(f.GetValue(v)));
        return o;
    }

    /// <summary>Copies from's contents into to, which has the same shape; to stays the same object.</summary>
    static void CopyInto(object from, object to)
    {
        if (from is Array fa)
        {
            var ta = (Array)to;
            if (!fa.GetType().GetElementType().IsArray) { Array.Copy(fa, ta, fa.Length); return; }
            for (int i = 0; i < fa.Length; i++) ta.SetValue(DeepCopy(fa.GetValue(i)), i);
            return;
        }
        // A plain object's own arrays are replaced by fresh copies, not copied into: the game
        // may have put an array there that something else owns.
        foreach (var f in InstanceFields(from.GetType()))
            f.SetValue(to, DeepCopy(f.GetValue(from)));
    }

    static bool SameShape(object x, object y)
    {
        if (x.GetType() != y.GetType()) return false;
        if (x is not Array a) return true;
        var b = (Array)y;
        if (a.Rank != b.Rank) return false;
        for (int r = 0; r < a.Rank; r++) if (a.GetLength(r) != b.GetLength(r)) return false;
        return true;
    }

    static long ApproxBytes(Array a)
    {
        var et = a.GetType().GetElementType();
        if (et.IsArray) { long n = 0; foreach (var x in a) if (x is Array s) n += ApproxBytes(s); return n; }
        int size = et.IsPrimitive ? System.Runtime.InteropServices.Marshal.SizeOf(et == typeof(bool) ? typeof(byte) : et == typeof(char) ? typeof(short) : et) : 8;
        return (long)a.Length * size;
    }

    // ---- describing a difference ----

    /// <summary>Null when equal, else what b is compared with a.</summary>
    static string Describe(object a, object b)
    {
        if (a is Array x && b is Array y)
        {
            if (!SameShape(x, y)) return $"{Shape(y)} (was {Shape(x)})";
            int n = 0, first = -1, i = 0;
            var ey = y.GetEnumerator();
            foreach (var ex in x)
            {
                ey.MoveNext();
                if (Describe(ex, ey.Current) != null) { n++; if (first < 0) first = i; }
                i++;
            }
            return n == 0 ? null : $"{n} of {x.Length} elements differ, first [{first}]";
        }
        if (IsByReference(a) && IsByReference(b) && a.GetType() == b.GetType())
        {
            var differ = InstanceFields(a.GetType()).Where(f => Describe(f.GetValue(a), f.GetValue(b)) != null).Select(f => f.Name).ToList();
            return differ.Count == 0 ? null : "fields differ: " + string.Join(", ", differ);
        }
        if (Equals(a, b)) return null;
        return $"{Show(b)} (was {Show(a)})";
    }

    static string Shape(Array a) => a == null ? "null" : $"{a.GetType().GetElementType().Name}[{string.Join(",", Enumerable.Range(0, a.Rank).Select(a.GetLength))}]";

    static string Show(object v) => v switch
    {
        null => "null",
        string s => "\"" + (s.Length > 40 ? s[..40] + "..." : s) + "\"",
        Array a => Shape(a),
        _ => v.ToString(),
    };

    /// <summary>
    /// What a field that is not restored holds, without touching the engine: null, a live or a
    /// freed Godot object, a collection's count, or just its type.
    /// </summary>
    static string Fingerprint(object v)
    {
        string kind = v switch
        {
            null => "null",
            GodotObject g => GodotObject.IsInstanceValid(g) ? g.GetType().Name : "freed " + g.GetType().Name,
            Array a => Shape(a),
            ICollection c when v.GetType().Namespace?.StartsWith("Godot") != true => $"{v.GetType().Name} count {c.Count}",
            Delegate => "a delegate",
            _ => v.GetType().IsValueType ? v.ToString() : v.GetType().Name,
        };
        if (!DeepFingerprints || v == null || v is GodotObject || v is Delegate) return kind;
        return $"{kind} content {ContentHash(v):x16}";
    }

    /// <summary>
    /// Set by the runner for SCENARIO_STATE_REPORT: fingerprints then also hash everything a
    /// watched field reaches (fields, array elements, collection entries), so a list or game
    /// object whose contents changed shows up, not only one whose count did. Godot objects
    /// count only as live or freed, and the walk stops after 200,000 values.
    /// </summary>
    public static bool DeepFingerprints;

    static ulong ContentHash(object root)
    {
        ulong h = 14695981039346656037UL;
        void Mix(long x) { h = (h ^ (ulong)x) * 1099511628211UL; }
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var todo = new Stack<object>();
        todo.Push(root);
        int budget = 200000;
        while (todo.Count > 0 && budget-- > 0)
        {
            object o = todo.Pop();
            switch (o)
            {
                case null: Mix(0); continue;
                case string str: Mix(str.GetHashCode()); continue;
                case GodotObject g: Mix(GodotObject.IsInstanceValid(g) ? 1 : 2); continue;
                case Delegate or Type or System.Reflection.MemberInfo: Mix(3); continue;
            }
            var t = o.GetType();
            if (t.IsPrimitive || t.IsEnum) { Mix(o.GetHashCode()); continue; }
            if (!t.IsValueType && !seen.Add(o)) { Mix(4); continue; }
            if (o is Array a) { Mix(a.Length); foreach (var e in a) todo.Push(e); continue; }
            if (o is IEnumerable en && t.Namespace?.StartsWith("System") == true)
            {
                int n = 0;
                foreach (var e in en) { todo.Push(e); n++; }
                Mix(n);
                continue;
            }
            if (t.Namespace?.StartsWith("System") == true || t.Namespace?.StartsWith("Godot") == true)
            {
                // A value such as a Vector3 hashes by value; a reference type's hash is its identity, which differs every run.
                Mix(t.IsValueType ? o.GetHashCode() : t.FullName.GetHashCode());
                continue;
            }
            foreach (var f in InstanceFields(t)) todo.Push(f.GetValue(o));
        }
        return h;
    }

    // ---- reflection helpers ----

    static IEnumerable<Type> SafeTypes(Assembly a)
    {
        try { return a.GetTypes(); }
        catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t != null); }
    }

    static bool IsCompilerGenerated(Type t) =>
        t.Name.StartsWith('<') || t.GetCustomAttribute<CompilerGeneratedAttribute>() != null;

    /// <summary>"Outer.Inner" for nested types, no namespace: how the code itself names them.</summary>
    static string DisplayName(Type t) => t.DeclaringType == null ? t.Name : DisplayName(t.DeclaringType) + "." + t.Name;

    /// <summary>An auto-property's backing field by its property's name.</summary>
    static string FieldName(FieldInfo f)
    {
        string n = f.Name;
        if (n.StartsWith('<') && n.Contains(">k__BackingField")) return n[1..n.IndexOf('>')];
        return n;
    }
}
