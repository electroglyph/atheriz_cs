using System.Reflection;
using Atheriz.Core.Objects;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Simplify;

// Delegate invocation: parameter metadata is snapshotted on first use, so
// repeat calls reuse it and arity/type mismatches keep throwing the same
// exception as the uncached path.
[Collection("Ported")]
public class DelegateInvokerCacheTests
{
    [Fact]
    public void Invoke_ReusesMetadata_AcrossCalls()
    {
        Func<int, int> f = x => x * 2;

        Assert.Equal(42, DelegateInvoker.Invoke(f, new object?[] { 21 }));
        Assert.Equal(42, DelegateInvoker.Invoke(f, new object?[] { 21 }));
    }

    [Fact]
    public void Invoke_MismatchedArityOrType_ThrowsParameterCount()
    {
        Func<int, int> f = x => x * 2;

        Assert.Throws<TargetParameterCountException>(() => DelegateInvoker.Invoke(f, Array.Empty<object?>()));
        Assert.Throws<TargetParameterCountException>(() => DelegateInvoker.Invoke(f, new object?[] { 1, 2 }));
        Assert.Throws<TargetParameterCountException>(() => DelegateInvoker.Invoke(f, new object?[] { "x" }));
        Assert.Throws<TargetParameterCountException>(() => DelegateInvoker.Invoke(f, new object?[] { null }));
    }
}
