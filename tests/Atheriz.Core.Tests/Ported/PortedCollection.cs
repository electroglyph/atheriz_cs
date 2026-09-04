using Xunit;

namespace Atheriz.Core.Tests.Ported;

// Keep Ported tests sequential within collection but allow other test classes to run parallel.
// Previously [assembly: CollectionBehavior(DisableTestParallelization=true)] killed parallelism globally — now scoped to Ported only.
[CollectionDefinition("Ported", DisableParallelization = true)]
public class PortedCollection : ICollectionFixture<PortedFixture> { }
