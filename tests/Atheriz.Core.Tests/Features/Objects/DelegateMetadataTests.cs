// DelegateInvoker metadata: the identity-keyed table keeps separate metadata
// per delegate instance, and equal-but-distinct instances behave identically.
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Objects;

[Collection("Ported")]
public sealed class DelegateMetadataTests
{
    private static int Double(int x) => x * 2;
    private static int Triple(int x) => x * 3;

    [Fact]
    public void Invoke_DistinctDelegates_KeepSeparateMetadata()
    {
        Func<int, int> doubleFn = Double;
        Func<int, int> tripleFn = Triple;
        Assert.Equal(42, DelegateInvoker.Invoke(doubleFn, [21]));
        Assert.Equal(63, DelegateInvoker.Invoke(tripleFn, [21]));
        Assert.Equal(42, DelegateInvoker.Invoke(doubleFn, [21]));
    }

    [Fact]
    public void Invoke_EqualButDistinctInstances_BehaveIdentically()
    {
        // Capturing lambdas: each evaluation allocates a distinct closure
        // instance (method groups and non-capturing lambdas are compiler-cached
        // to one instance, so they cannot prove distinctness).
        int two = 2;
        Func<int, int> first = x => x * two;
        Func<int, int> second = x => x * two;
        Assert.False(ReferenceEquals(first, second));
        Assert.Equal(42, DelegateInvoker.Invoke(first, [21]));
        Assert.Equal(42, DelegateInvoker.Invoke(second, [21]));
    }
}
