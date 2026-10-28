using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using Underworld;

namespace Underworld.Rules.Tests;

/// <summary>
/// The "rule" pins: each rule function in Cases run over a fixed grid of inputs, for each game it
/// belongs to. Per function the pin file holds the row count and a hash of every row, plus four
/// sample rows with different outputs, so a diff names the function that changed and shows a
/// row or two of it, and sixteen short hashes of runs of rows, so a failure can say which inputs
/// changed. A function with four rows or fewer has every row written out instead.
///
/// Each row starts from the same state: UWClass._RES for the game, Rng.Source a BorlandRandSource
/// seeded with the row number (1, 2, ...), PLAYER.DAT a fixed byte pattern, and COMOBJ.DAT and
/// OBJECTS.DAT fixed byte patterns too. The patterns are synthetic, not game data, so these pins
/// hold the port's formulas still; they say nothing about whether the formulas match the original
/// (the vector tests do that). An object argument is a 27-byte record of a fixed pattern with the
/// row's item id, index item id + 2, and static when the item id is odd. A row's output is the return value, then any ref or out argument, then any
/// PLAYER.DAT or object byte the call changed, then the random seed if the call drew.
/// Idea from Underworld Revisited (PQMarine, MIT): Tools/UWSelfCheck/Pins/rule-constants.txt.
/// </summary>
public static class RuleOutputPins
{
    public const int Samples = 4;

    /// <summary>One rule function. Method is found by name (and by Overload, its parameter type
    /// names joined by commas, when overloaded); Axes
    /// gives the values for each parameter in order, and the grid is every combination. A ref or
    /// out parameter still takes an axis (its input value; ignored for out). Seeds repeats each
    /// combination that many times with the next seeds, for functions that draw with few inputs.</summary>
    public sealed record Case(string Games, Type Type, string Method, long[][] Axes, string Overload = null, int Seeds = 1);

    static long[] R(long from, long to, long step = 1)
    {
        var v = new List<long>(); for (long x = from; x <= to; x += step) v.Add(x); return v.ToArray();
    }
    static long[] V(params long[] v) => v;

    static readonly long[] Bool = V(0, 1);
    static readonly long[] Item = R(0, 511);
    static readonly long[] Short = V(-32768, -23043, -23042, -16384, -1000, -256, -255, -128, -64, -2, -1, 0, 1, 2, 3, 7, 8, 15, 16, 31, 32, 63, 64, 100, 127, 128, 255, 256, 1000, 16384, 23170, 23171, 32767);
    static readonly long[] Heading = R(0, 0xFFFF, 0x80);
    static readonly long[] Tile = V(-1, 0, 1, 2, 31, 32, 62, 63, 64);
    static readonly long[] Coord = R(0, 63);
    static readonly long[] Zero = V(0);

    static Case B(Type t, string m, params long[][] axes) => new("uw1 uw2", t, m, axes);
    static Case U1(Type t, string m, params long[][] axes) => new("uw1", t, m, axes);
    static Case U2(Type t, string m, params long[][] axes) => new("uw2", t, m, axes);

    /// <summary>The rule functions that run with no Godot and no game data. The README lists those
    /// that cannot and why.</summary>
    public static readonly Case[] Cases =
    {
        // random rolls and checks
        B(typeof(Rng), "RandMod", R(0, 40)),
        B(typeof(Rng), "RandScale", R(0, 40)),
        B(typeof(Rng), "DiceRoll", R(0, 6), R(0, 20)),
        B(typeof(playerdat), "SkillCheck", R(0, 62, 2), R(0, 62, 2), Zero),
        B(typeof(playerdat), "GetGoverningAttribute", R(-1, 25)),
        B(typeof(ConversationVM), "Range", V(-5, 0, 1, 10, 50, 100, 1000), V(0, 1, 10, 50), V(0, 10, 50, 100, 1000)),
        B(typeof(ConversationVM), "TradeGain", V(0, 1, 2, 5, 10, 20, 50, 100, 500, 1000), V(0, 1, 2, 5, 10, 20, 50, 100, 500, 1000)),
        U2(typeof(ConversationVM), "CalculateJospurDebt", R(0, 12)),

        // combat and damage
        B(typeof(combat), "PickBodyHitPoint", V(0, 8, 16, 24, 32, 48, 64, 96, 127), V(0, 8, 16, 24, 32, 48, 64, 96, 127), V(0, 8, 16, 24, 32, 48, 64, 96, 127), V(0, 8, 16, 24, 32, 48, 64, 96, 127)),
        B(typeof(combat), "isWeapon", Item),
        U2(typeof(combat), "checkforPoisonableWeapon", Item),
        B(typeof(combat), "PlayerWeaponSoundClass", Item),
        B(typeof(damage), "ScaleDamage", R(0, 511, 3), V(0, 1, 5, 10, 30, 100), V(0, 1, 2, 3, 4, 8, 16, 32, 64, 128)),
        B(typeof(damage), "VulnerableDamage", R(0, 255)),
        B(typeof(damage), "GetObjectTypeDebris", Item, V(0, 1, 2, 3, 4, 8, 16, 32, 64, 128)),
        B(typeof(playerdat), "ArmourValue", Item),

        // motion, headings and maths
        B(typeof(motion), "GetCoordinateInDirection", Heading, V(0, 1, 8, 64, 255), V(0, 1000), V(0, 1000)),
        B(typeof(motion), "GetVectorForDirection", Heading, Zero, Zero),
        B(typeof(motion), "HeadingLookupCalc", R(0, 0xFFFF, 0x41), Zero, Zero),
        B(typeof(motion), "SomethingProjectileHeading_seg021_22FD_EAE", Heading, Zero, Zero),
        B(typeof(motion), "SatelliteOrDartVectoring", V(-64, -8, -1, 0, 1, 8, 64), V(-64, -8, -1, 0, 1, 8, 64), V(-64, -8, -1, 0, 1, 8, 64), V(-64, -8, -1, 0, 1, 8, 64)),
        B(typeof(motion), "MaybeGetTangent_seg021_22FD_EFB", Short, Short),
        B(typeof(motion), "seg021_22FD_BA2_MaybeTangent", R(-32768, 32767, 97)),
        B(typeof(motion), "seg021_22FD_B78_maybetangent", R(-32768, 32767, 97)),
        B(typeof(motion), "GetDefelectionMaybe", V(0, 0x20, 0x40, 0x80, 0xC0, 0xFF, 0x100), V(0, 0x20, 0x40, 0x80, 0xC0, 0xFF, 0x100), V(0, 0x10, 0x20, 0x40)),
        B(typeof(motion), "SBB", V(-1, 0, 1, 2, 255)),
        B(typeof(motion), "GetTileState", R(0, 0x1FFF)),
        B(typeof(motion), "ApplyWaterCurrentIceSliding", R(0, 3), V(0, 0x40, 0x80, 0xC0), V(0, 10), V(0, 0x40, 0x80, 0xC0), V(0, 10), V(0, 10), Zero, Zero, Bool),
        B(typeof(UnderWorldSqrt), "sqrt_vanilla", R(0, 0x3FFFF, 0x3F).Concat(V(0xFFFF, 0x10000, 0xFFFFFF, 0x7FFFFFFF, 0xFFFFFFFF)).ToArray()),
        B(typeof(UnderworldMath), "RotateRightThroughCarry", V(0, 1, 0x8000, 0xFFFF, 0x1234), R(0, 17), Bool),

        // npc movement and pathing
        B(typeof(Pathfind), "GetVectorHeading", R(-16, 16), R(-16, 16)),
        B(typeof(Pathfind), "ConvertVectorToHeading", R(-16, 16), R(-16, 16)),
        B(typeof(Pathfind), "ConvertTilePointsToHeading", V(0, 31, 32, 33, 63), V(0, 31, 32, 33, 63), V(0, 31, 32, 33, 63), V(0, 31, 32, 33, 63)),

        // tiles and worlds
        B(typeof(UWTileMap), "IsOpen", R(0, 15)),
        B(typeof(UWTileMap), "isTileOpen", R(0, 15)),
        B(typeof(UWTileMap), "ValidTile", Tile, Tile),
        B(typeof(UWTileMap), "isTerrainWater", R(0, 0xFF)),
        B(typeof(UWTileMap), "isTerrainLava", R(0, 0xFF)),
        B(typeof(UWTileMap), "isTerrainIce", R(0, 0xFF)),
        B(typeof(tmap), "IsDiagonalTile", R(0, 15)),
        B(typeof(worlds), "GetWorldNo", R(0, 80)),
        B(typeof(automap), "CanMap", R(0, 80)),
        U2(typeof(pitsofcarnage), "InArena", Coord, Coord),
        U2(typeof(pitsofcarnage), "Mpos", Coord, Coord),
        U2(typeof(a_hack_trap_teleportpitwarriors), "GetPitWarriorPositionOffset", Coord, Coord),
        U2(typeof(a_hack_trap_qbert), "QbertSeq", R(0, 31)),
        U2(typeof(a_bit_of_a_map), "GetMapPieceSegmentNo", R(0, 15), R(0, 15)),

        // traps and variables
        U1(typeof(a_set_variable_trap), "VariableOperationUW1", R(0, 7), R(0, 7), V(0, 1, 2, 5, 31, 32, 63, 64, 255, 256)),
        U2(typeof(a_set_variable_trap), "VariableOperationUW2", V(0, 1, 0x3F, 0xFF, 0x100, 0x101, 0x17F, 0x180, 0x18F, 0x190, 0x19F, 0x1A0), R(0, 7), V(0, 1, 2, 5, 0x3FF, 0x400)),
        U2(typeof(a_set_variable_trap), "VariableTransformUW2", V(0, 1, 5, 100, 0x3FF), R(0, 8), V(0, 1, 3, 100)),

        // objects
        B(typeof(button), "IsOn", Item),
        B(typeof(Food), "IsFood", Item),
        B(typeof(light), "IsUsedUp", Item),
        B(typeof(look), "CanBeIdentified", Item),
        B(typeof(MagicEnchantment), "IsPotion", Item, Bool),
        B(typeof(enchanting), "CanObjectBeEnchanted", R(0, 511, 3), V(0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15), V(0, 1, 2, 3, 7, 15, 31, 63)),
        B(typeof(repair), "CanObjectBeMagicallyRepaired", Item),
        B(typeof(playerdat), "CanCarryWeight", Item) with { Overload = "uwObject" },
        B(typeof(boots), "GetSpriteIndex", Item),
        B(typeof(ring), "GetSpriteIndex", Item),
        B(typeof(wearable), "GetSpriteIndex", Item),
        B(typeof(uwObject), "GetObjectSprite", Item),
        B(typeof(uwObject), "GetObjectQuantity", Item),
        B(typeof(uwObject), "CheckIfObjectInValidSlot", Item, R(0, 19)),
        B(typeof(uwObject), "ResetIdentification", Item),
        B(typeof(thief), "ClearOwnership", Item),
        B(typeof(runestone), "IsRunestone", Item),
        U1(typeof(pickup), "CanBePickedUpOverrides", Item),
        B(typeof(ConversationVM), "GetConversationNumber", Item),

        // player state (PLAYER.DAT changes are part of the output)
        B(typeof(playerdat), "DeathExperienceChange"),
        B(typeof(sleep), "UpdateIntoxication", V(-100, -10, -1, 0, 1, 2, 5, 10, 32, 50, 63, 64, 100, 255)),
        B(typeof(a_check_variable_trap), "GetVarQuestOrClockValue", R(0, 0x1FF, 3)),
        U2(typeof(Teleportation), "ChangeUW2Quests"),
        U2(typeof(pitsofcarnage), "IsDuelingAgainstCritter", R(0, 1023, 7)),

        // objects changed in place (object byte changes are part of the output)
        B(typeof(light), "LightOff", Item),
        B(typeof(button), "ToggleItemID", Item),
        B(typeof(a_hack_trap_rechargelightsphere), "RechargeLightSphere", Item),
        B(typeof(ObjectCreator), "InitialiseCritter", Item),
        U2(typeof(a_hack_trap_castleschedule), "GetCastleDestination", Item, Zero, Zero),
        B(typeof(TileInfo), "WillObjectMoveWithTileHeightChange", V(0, 5), V(0, 5), V(0, 5), V(0, 5), V(0, 8, 16), V(0, 8, 16), R(0, 511, 17)),

        // magic
        U2(typeof(RunicMagic), "GetSpellSFX", R(0, 15), R(0, 15)),

        // per-game values held in properties
        B(typeof(tracking), "get_DirectionOffset"), B(typeof(tracking), "get_Heightoffset"), B(typeof(tracking), "get_DetectionsOffset"),
        B(typeof(triggerObjectDat), "get_OPEN_TRIGGER_TYPE"), B(typeof(triggerObjectDat), "get_UNLOCK_TRIGGER_TYPE"),
        B(typeof(UWTileMap), "get_NO_OF_LEVELS"),
        B(typeof(automaptileinfo), "get_bridgedisplaytype"), B(typeof(automaptileinfo), "get_stairdisplaytype"), B(typeof(automaptileinfo), "get_doordisplaytype"),
    };

    const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

    public static MethodInfo Find(Case c)
    {
        var ms = c.Type.GetMethods(Any).Where(m => m.Name == c.Method &&
            (c.Overload == null || string.Join(",", m.GetParameters().Select(p => p.ParameterType.Name)) == c.Overload)).ToList();
        if (ms.Count != 1) throw new MissingMethodException($"{c.Type.FullName}.{c.Method}: {ms.Count} matching static methods (set Overload to pick one)");
        return ms[0];
    }

    public static string Signature(MethodInfo m) =>
        $"{NamedValuePins.Name(m.DeclaringType!)}.{m.Name}({string.Join(", ", m.GetParameters().Select(p => p.Name))})";

    // Fixed byte patterns standing in for PLAYER.DAT, COMOBJ.DAT, OBJECTS.DAT and an object record.
    static byte[] Pattern(int length, int mul, int add) =>
        Enumerable.Range(0, length).Select(k => (byte)(k * mul + add + (k >> 8))).ToArray();
    static readonly byte[] PlayerDat = Pattern(0x1000, 29, 7);
    static readonly byte[] ComObjDat = Pattern(2 + 11 * 512, 53, 11);
    static readonly byte[] ObjectsDat = Pattern(0x4000, 41, 3);

    static uwObject Object(long itemId)
    {
        var o = new uwObject { DataBuffer = Pattern(27, 101, (int)itemId * 37 + 13), PTR = 0, index = (short)(itemId + 2), IsStatic = (itemId & 1) == 1 };
        o.item_id = (int)itemId & 0x1FF;
        return o;
    }

    /// <summary>Every pin line for every case, in Cases order.</summary>
    public static List<string> Collect(List<string> problems, Dictionary<string, List<Row>> rowsByName)
    {
        var lines = new List<string>();
        var savedRng = Rng.Source; var savedRes = UWClass._RES; var savedPdat = playerdat.pdat;
        var savedCom = Get(typeof(commonObjDat), "buffer"); var savedObj = Get(typeof(objectDat), "buffer");
        try
        {
            foreach (var c in Cases)
            {
                MethodInfo m;
                try { m = Find(c); } catch (Exception e) { problems.Add(e.Message); continue; }
                if (GodotReach.Find(m) is { } godot) { problems.Add($"{Signature(m)} reaches Godot ({godot}), so it cannot run here"); continue; }
                var ps = m.GetParameters();
                var axes = c.Axes.Length == ps.Length ? c.Axes : null;
                if (axes == null) { problems.Add($"{Signature(m)}: {c.Axes.Length} axes for {ps.Length} parameters"); continue; }
                foreach (var game in c.Games.Split(' '))
                {
                    UWClass._RES = game == "uw1" ? UWClass.GAME_UW1 : UWClass.GAME_UW2;
                    Seams.SetStatic(typeof(commonObjDat), "buffer", ComObjDat.Clone());
                    Seams.SetStatic(typeof(objectDat), "buffer", ObjectsDat.Clone());
                    var rows = Rows(m, ps, axes, c.Seeds);
                    var name = $"rule {game} {Signature(m)}";
                    rowsByName[name] = rows;
                    if (rows.All(r => r.Output.StartsWith("throws ", StringComparison.Ordinal)))
                        problems.Add($"{name}: every row throws ({rows[0].Output})");
                    else if (rows.Count > 1 && rows.All(r => r.Output == rows[0].Output))
                        problems.Add($"{name}: every row gives {rows[0].Output}, which pins nothing; widen the grid or drop the case");
                    if (rows.Count <= Samples)
                    {
                        foreach (var r in rows) lines.Add($"{name} {r.Input} = {r.Output}");
                        continue;
                    }
                    var throws = rows.Count(r => r.Output.StartsWith("throws ", StringComparison.Ordinal));
                    lines.Add($"{name} = {rows.Count} rows {PinFile.Hash(Text(rows))}" + (throws > 0 ? $", {throws} throw" : "") +
                        " parts " + string.Join(" ", Parts(rows).Select(PartHash)));
                    foreach (var r in SampleRows(rows)) lines.Add($"{name} {r.Input} = {r.Output}");
                }
            }
        }
        finally
        {
            Rng.Source = savedRng; UWClass._RES = savedRes; playerdat.pdat = savedPdat;
            Seams.SetStatic(typeof(commonObjDat), "buffer", savedCom); Seams.SetStatic(typeof(objectDat), "buffer", savedObj);
        }
        return lines;
    }

    static object Get(Type t, string field)
    {
        Seams.RunStaticConstructor(t);
        for (var c = t; c != null; c = c.BaseType)
        {
            var f = c.GetField(field, Any | BindingFlags.DeclaredOnly);
            if (f != null) return f.GetValue(null);
        }
        throw new MissingFieldException(t.FullName, field);
    }

    public sealed record Row(string Input, string Output);

    public const int PartCount = 16;

    static string Text(IEnumerable<Row> rows) => string.Join("\n", rows.Select(r => r.Input + " " + r.Output));

    /// <summary>The rows cut into PartCount runs in grid order, each pinned by a short hash, so a
    /// failure can say which inputs changed.</summary>
    public static List<List<Row>> Parts(List<Row> rows) =>
        Enumerable.Range(0, PartCount).Select(k => rows.GetRange(k * rows.Count / PartCount, (k + 1) * rows.Count / PartCount - k * rows.Count / PartCount)).ToList();

    public static string PartHash(List<Row> part) => PinFile.Hash(Text(part))[7..11];

    /// <summary>The short part hashes on a rule's hash line, or null if it has none.</summary>
    public static string[] PartHashes(string value)
    {
        int i = value.IndexOf(" parts ", StringComparison.Ordinal);
        return i < 0 ? null : value[(i + 7)..].Split(' ');
    }

    /// <summary>The first row, then the first rows (in grid order) whose output is not yet shown;
    /// evenly spaced rows make up the number when there are fewer distinct outputs.</summary>
    static IEnumerable<Row> SampleRows(List<Row> rows)
    {
        var picked = new List<Row>();
        foreach (var r in rows)
            if (picked.Count < Samples && picked.All(p => p.Output != r.Output)) picked.Add(r);
        var spread = Enumerable.Range(0, Samples).Select(s => rows[s * (rows.Count - 1) / (Samples - 1)]);
        foreach (var r in spread.Concat(rows))
            if (picked.Count < Samples && !picked.Contains(r)) picked.Add(r);
        return picked.OrderBy(rows.IndexOf);
    }

    static List<Row> Rows(MethodInfo m, ParameterInfo[] ps, long[][] axes, int seeds)
    {
        var rows = new List<Row>();
        var idx = new int[ps.Length];
        uint seed = 0;
        while (true)
        {
            for (int s = 0; s < seeds; s++)
            {
                var input = idx.Select((v, i) => axes[i][v]).ToArray();
                rows.Add(new Row("(" + string.Join(",", input.Select(v => v.ToString(CultureInfo.InvariantCulture))) + ")", Run(m, ps, input, ++seed)));
            }
            int k = ps.Length - 1;
            while (k >= 0 && ++idx[k] == axes[k].Length) { idx[k] = 0; k--; }
            if (k < 0) break;
        }
        return rows;
    }

    static string Run(MethodInfo m, ParameterInfo[] ps, long[] input, uint seed)
    {
        var rng = new BorlandRandSource(seed);
        Rng.Source = rng;
        playerdat.pdat = (byte[])PlayerDat.Clone();
        var args = new object[ps.Length];
        for (int i = 0; i < ps.Length; i++) args[i] = Arg(ps[i].ParameterType, input[i]);
        var objBefore = args.Select(a => a is uwObject o ? (byte[])o.DataBuffer.Clone() : null).ToArray();
        var parts = new List<string>();
        try
        {
            var ret = m.Invoke(null, args);
            if (m.ReturnType != typeof(void)) parts.Add(NamedValuePins.Scalar(ret is bool b ? (b ? 1 : 0) : ret));
        }
        catch (TargetInvocationException e) { return "throws " + e.InnerException!.GetType().Name; }
        for (int i = 0; i < ps.Length; i++)
        {
            if (ps[i].ParameterType.IsByRef) parts.Add($"{ps[i].Name}={NamedValuePins.Scalar(args[i])}");
            if (objBefore[i] != null) Changes(parts, $"{ps[i].Name}", objBefore[i], ((uwObject)args[i]).DataBuffer);
        }
        if (playerdat.pdat != null) Changes(parts, "pdat", PlayerDat, playerdat.pdat);
        if (rng.Seed != seed && ReferenceEquals(Rng.Source, rng)) parts.Add($"seed={rng.Seed:x8}");
        return parts.Count == 0 ? "-" : string.Join(" ", parts);
    }

    static void Changes(List<string> parts, string what, byte[] before, byte[] after)
    {
        if (after.Length != before.Length) { parts.Add($"{what} length {after.Length}"); return; }
        var sb = new StringBuilder();
        for (int i = 0; i < before.Length; i++)
            if (before[i] != after[i]) sb.Append(sb.Length == 0 ? "" : ",").Append($"{i:x}:{after[i]:x2}");
        if (sb.Length > 0) parts.Add($"{what}[{sb}]");
    }

    static object Arg(Type t, long v)
    {
        if (t.IsByRef) t = t.GetElementType()!;
        if (t == typeof(uwObject)) return Object(v);
        if (t.IsEnum) return Enum.ToObject(t, v);
        return Type.GetTypeCode(t) switch
        {
            TypeCode.Boolean => v != 0,
            TypeCode.Byte => unchecked((byte)v), TypeCode.SByte => unchecked((sbyte)v),
            TypeCode.Int16 => unchecked((short)v), TypeCode.UInt16 => unchecked((ushort)v),
            TypeCode.Int32 => unchecked((int)v), TypeCode.UInt32 => unchecked((uint)v),
            TypeCode.Int64 => v, TypeCode.UInt64 => unchecked((ulong)v),
            _ => throw new NotSupportedException($"no grid for a {t.Name} parameter"),
        };
    }
}
