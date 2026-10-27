using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace Underworld.Rules.Tests;

/// <summary>
/// Whether code can reach the Godot engine, found by reading IL: the method, and every method of
/// the game assembly it calls, transitively, must not call, construct or read anything declared in
/// a Godot assembly, and must not make a virtual call into the game assembly (which could land in
/// a Godot override). With no engine running, such a call crashes the test host rather than
/// throwing, so the pins check this before running a static constructor or a rule function.
/// Calls through IRandSource are allowed: Rng.Source is set to BorlandRandSource.
/// </summary>
public static class GodotReach
{
    static readonly Dictionary<short, OpCode> Ops = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(f => (OpCode)f.GetValue(null)!).ToDictionary(o => o.Value);

    static readonly Dictionary<MethodBase, string> Cache = new();

    /// <summary>The first Godot member the code reaches, or null when it reaches none.</summary>
    public static string Find(MethodBase start)
    {
        if (Cache.TryGetValue(start, out var known)) return known;
        var game = typeof(UWClass).Assembly;
        var seen = new HashSet<MethodBase>();
        var queue = new Queue<MethodBase>();
        queue.Enqueue(start);
        string found = null;
        while (queue.Count > 0 && found == null)
        {
            var m = queue.Dequeue();
            if (!seen.Add(m)) continue;
            foreach (var (op, member) in Members(m))
            {
                var asm = member.Module.Assembly;
                if (asm.GetName().Name!.StartsWith("Godot", StringComparison.Ordinal)) { found = $"{m.DeclaringType?.Name}.{m.Name} uses {member.DeclaringType?.Name}.{member.Name}"; break; }
                if (asm != game || member is not MethodBase callee) continue;
                if (callee.IsVirtual && op == OpCodes.Callvirt && callee.DeclaringType != typeof(IRandSource)) { found = $"{m.DeclaringType?.Name}.{m.Name} makes a virtual call to {callee.DeclaringType?.Name}.{callee.Name}"; break; }
                queue.Enqueue(callee);
                if (callee.DeclaringType?.TypeInitializer is { } cctor) queue.Enqueue(cctor);
            }
            if (m.DeclaringType?.TypeInitializer is { } own && own != m) queue.Enqueue(own);
        }
        Cache[start] = found;
        return found;
    }

    static IEnumerable<(OpCode, MemberInfo)> Members(MethodBase m)
    {
        var il = m.GetMethodBody()?.GetILAsByteArray();
        if (il == null) yield break;
        int i = 0;
        while (i < il.Length)
        {
            short code = il[i++];
            if (code == 0xFE) code = (short)(0xFE00 | il[i++]);
            var op = Ops[code];
            int size = op.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, i),
                _ => 4,
            };
            if (op.OperandType is OperandType.InlineMethod or OperandType.InlineField or OperandType.InlineType or OperandType.InlineTok)
            {
                MemberInfo member = null;
                try
                {
                    member = m.Module.ResolveMember(BitConverter.ToInt32(il, i),
                        m.DeclaringType is { IsGenericType: true } dt ? dt.GetGenericArguments() : null,
                        m.IsGenericMethod ? m.GetGenericArguments() : null);
                }
                catch (ArgumentException) { }
                if (member is Type t) member = t.IsGenericType ? t.GetGenericTypeDefinition() : t;
                if (member != null) yield return (op, member);
            }
            i += size;
        }
    }
}
