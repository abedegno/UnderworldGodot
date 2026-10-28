using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using Underworld;

namespace Underworld.Rules.Tests;

/// <summary>
/// Reaches the private statics the rules read, by reflection, so the tests need no production
/// change. A name that no longer exists fails loudly with the name.
/// </summary>
public static class Seams
{
    const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

    static FieldInfo Field(Type t, string name)
    {
        for (var c = t; c != null; c = c.BaseType)
        {
            var f = c.GetField(name, Any);
            if (f != null) return f;
        }
        throw new MissingFieldException(t.FullName, name);
    }

    public static void SetStatic(Type t, string name, object value)
    {
        RunStaticConstructor(t);
        Field(t, name).SetValue(null, value);
    }

    /// <summary>
    /// The loaders' static constructors read game files from UWClass.BasePath, which is null in the
    /// test host, so Path.Combine throws before a buffer can be replaced. Run them once against a
    /// folder with no game data: ReadStreamFile then leaves the buffer null without throwing.
    /// </summary>
    public static void RunStaticConstructor(Type t)
    {
        var saved = UWClass.BasePath;
        try
        {
            UWClass.BasePath ??= Path.Combine(Path.GetTempPath(), "Underworld.Rules.Tests-no-game-data");
            for (var c = t; c != null; c = c.BaseType) RuntimeHelpers.RunClassConstructor(c.TypeHandle);
        }
        finally { UWClass.BasePath = saved; }
    }

    public static object CallStatic(Type t, string name, params object[] args)
    {
        var m = t.GetMethod(name, Any) ?? throw new MissingMethodException(t.FullName, name);
        return m.Invoke(null, args);
    }

    /// <summary>A free-standing object: its own 27-byte record, item id and position word set.</summary>
    public static uwObject Object(int itemId, int positionWord = 0)
    {
        var o = new uwObject { DataBuffer = new byte[27], PTR = 0 };
        o.item_id = itemId & 0x1FF;
        typeof(uwObject).GetMethod("SetAt16", Any).Invoke(o, new object[] { 2, positionWord & 0xFFFF });
        return o;
    }

    /// <summary>COMOBJ.DAT with every byte 0 but the resistance byte of one item.</summary>
    public static void SetResistance(int itemId, int resist)
    {
        var buf = new byte[2 + 11 * 512];
        buf[2 + itemId * 11 + 8] = (byte)resist;
        SetStatic(typeof(commonObjDat), "buffer", buf);
    }

    /// <summary>OBJECTS.DAT's weapon records with one weapon's skill byte set.</summary>
    public static void SetWeaponSkill(int itemId, int skill)
    {
        var buf = new byte[0x2000];
        buf[2 + 6 + (itemId & 0xF) * 8] = (byte)skill;
        SetStatic(typeof(weaponObjectDat), "buffer", buf);
    }
}
