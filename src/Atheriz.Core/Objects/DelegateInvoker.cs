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
    // First-use metadata snapshot per delegate instance (delegates are immutable,
    // so the snapshot never goes stale): parameter types + defaults + required
    // count + compiled invoker. Replaces the per-invoke Method.GetParameters()
    // reflection on the hook hot path; the arity/type checks below reproduce the
    // exact same TargetParameterCountException behavior as before.
    private sealed record DelegateMetadata(Type[] Types, object?[] Defaults, int RequiredCount, Func<object?[], object?> Invoker);
    private static readonly ConditionalWeakTable<Delegate, DelegateMetadata> _cache = new();

    public static object? Invoke(Delegate d, object?[] args)
    {
        var meta = _cache.GetValue(d, static del => Create(del));
        if (args.Length < meta.RequiredCount || args.Length > meta.Types.Length) throw new TargetParameterCountException();
        // surface type failures as arity failures. Without this the
        // compiled Convert throws NullReference/InvalidCast at invoke time.
        // Only caller-supplied args are checked; filled defaults are trusted.
        for (int i = 0; i < args.Length; i++)
        {
            var t = meta.Types[i];
            var a = args[i];
            if (a is null)
            {
                if (t.IsValueType && Nullable.GetUnderlyingType(t) is null)
                    throw new TargetParameterCountException();
            }
            else if (!t.IsInstanceOfType(a))
                throw new TargetParameterCountException();
        }
        if (args.Length < meta.Types.Length)
        {
            var filled = new object?[meta.Types.Length];
            Array.Copy(args, filled, args.Length);
            for (int i = args.Length; i < meta.Types.Length; i++) filled[i] = meta.Defaults[i];
            return meta.Invoker(filled);
        }
        return meta.Invoker(args);
    }

    private static DelegateMetadata Create(Delegate del)
    {
        var ps = del.Method.GetParameters();
        int required = 0;
        foreach (var p in ps)
        {
            if (p.IsOptional || p.HasDefaultValue) break;
            required++;
        }
        var types = new Type[ps.Length];
        var defaults = new object?[ps.Length];
        for (int i = 0; i < ps.Length; i++) { types[i] = ps[i].ParameterType; defaults[i] = ps[i].DefaultValue; }
        return new DelegateMetadata(types, defaults, required, Compile(del, ps));
    }

    private static Func<object?[], object?> Compile(Delegate del, ParameterInfo[] ps)
    {
        var argv = Expression.Parameter(typeof(object?[]), "args");
        var callArgs = new Expression[ps.Length];
        for (int i = 0; i < ps.Length; i++)
            callArgs[i] = Expression.Convert(Expression.ArrayIndex(argv, Expression.Constant(i)), ps[i].ParameterType);
        Expression body = Expression.Invoke(Expression.Constant(del), callArgs);
        body = del.Method.ReturnType == typeof(void)
            ? Expression.Block(body, Expression.Constant(null, typeof(object)))
            : Expression.Convert(body, typeof(object));
        return Expression.Lambda<Func<object?[], object?>>(body, argv).Compile();
    }
}
