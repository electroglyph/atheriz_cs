using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Atheriz.Core.Objects;

/// <summary>
/// Typed invocation for engine-registered <see cref="Delegate"/>s (hooks, parser
/// callables, value converters) without DynamicInvoke: each delegate is compiled
/// once to a <c>Func&lt;object?[], object?&gt;</c> and cached per delegate.
/// Arity mismatches throw <see cref="TargetParameterCountException"/> (callers keep
/// their historical fallback behavior); delegate errors propagate unwrapped.
/// Params-array delegates are not supported (none exist in the engine).
/// </summary>
public static class DelegateInvoker
{
    private static readonly ConditionalWeakTable<Delegate, Func<object?[], object?>> _cache = new();

    public static object? Invoke(Delegate d, object?[] args)
    {
        var ps = d.Method.GetParameters();
        int required = 0;
        foreach (var p in ps)
        {
            if (p.IsOptional || p.HasDefaultValue) break;
            required++;
        }
        if (args.Length < required || args.Length > ps.Length) throw new TargetParameterCountException();
        // surface type failures as arity failures. Without this the
        // compiled Convert throws NullReference/InvalidCast at invoke time.
        // Only caller-supplied args are checked; filled defaults are trusted.
        for (int i = 0; i < args.Length; i++)
        {
            var t = ps[i].ParameterType;
            var a = args[i];
            if (a is null)
            {
                if (t.IsValueType && Nullable.GetUnderlyingType(t) is null)
                    throw new TargetParameterCountException();
            }
            else if (!t.IsInstanceOfType(a))
                throw new TargetParameterCountException();
        }
        var inv = _cache.GetValue(d, static del => Compile(del));
        if (args.Length < ps.Length)
        {
            var filled = new object?[ps.Length];
            Array.Copy(args, filled, args.Length);
            for (int i = args.Length; i < ps.Length; i++) filled[i] = ps[i].DefaultValue;
            return inv(filled);
        }
        return inv(args);
    }

    private static Func<object?[], object?> Compile(Delegate del)
    {
        var q = del.Method.GetParameters();
        var argv = Expression.Parameter(typeof(object?[]), "args");
        var callArgs = new Expression[q.Length];
        for (int i = 0; i < q.Length; i++)
            callArgs[i] = Expression.Convert(Expression.ArrayIndex(argv, Expression.Constant(i)), q[i].ParameterType);
        Expression body = Expression.Invoke(Expression.Constant(del), callArgs);
        body = del.Method.ReturnType == typeof(void)
            ? Expression.Block(body, Expression.Constant(null, typeof(object)))
            : Expression.Convert(body, typeof(object));
        return Expression.Lambda<Func<object?[], object?>>(body, argv).Compile();
    }
}
