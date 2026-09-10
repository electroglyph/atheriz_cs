using Atheriz.Core.Commands;

namespace Atheriz.Core.Tests.Features.Commands;

// The double-checked lazy registries build exactly once and the unlogged-in
// set registers every verb in its single batch.
[Collection("Ported")]
public sealed class CommandRegistryInitTests
{
    [Fact]
    public void LoggedIn_ReturnsSameInstance()
    {
        CommandRegistry.Reset();
        try
        {
            Assert.Same(CommandRegistry.LoggedIn, CommandRegistry.LoggedIn);
        }
        finally { CommandRegistry.Reset(); }
    }

    [Fact]
    public void UnloggedIn_ReturnsSameInstance()
    {
        CommandRegistry.Reset();
        try
        {
            Assert.Same(CommandRegistry.UnloggedIn, CommandRegistry.UnloggedIn);
        }
        finally { CommandRegistry.Reset(); }
    }

    [Fact]
    public void UnloggedIn_RegistersAllVerbs()
    {
        CommandRegistry.Reset();
        try
        {
            var keys = CommandRegistry.UnloggedIn.GetKeys();
            foreach (var k in new[] { "connect", "create", "new", "guest", "none", "screenreader", "help", "quit" })
                Assert.Contains(k, keys);
            Assert.Equal(8, CommandRegistry.UnloggedIn.GetAll().Count);
        }
        finally { CommandRegistry.Reset(); }
    }
}
