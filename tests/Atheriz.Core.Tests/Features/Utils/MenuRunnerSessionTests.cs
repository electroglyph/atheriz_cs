using Atheriz.Core.Commands;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Tests.Features.Regression;

namespace Atheriz.Core.Tests.Features.Utils;

// MenuEngine.ResolveSession collapses to a single ISessionProvider read:
// Session returns itself and GameObject resolves through the same
// interface, while session-less and foreign callers yield null without
// throwing.
[Collection("Ported")]
public class MenuRunnerSessionTests
{
    private static Session? Resolve(object? caller) => MenuEngine.ResolveSession(caller);

    private sealed class ThrowingSessionProvider : ISessionProvider
    {
        public Session? Session => throw new InvalidOperationException("boom");
    }

    [Fact]
    public void Resolve_SessionCaller_ReturnsItself()
    {
        var session = new Session();
        Assert.Same(session, Resolve(session));
    }

    [Fact]
    public void Resolve_GameObjectWithSession_ReturnsSession()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var session = new Session();
            var obj = GameObject.Create("seated");
            obj.Session = session;
            Assert.Same(session, Resolve(obj));
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void Resolve_SessionlessGameObject_ReturnsNull()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var obj = GameObject.Create("sessionless");
            Assert.Null(obj.Session);
            Assert.Null(Resolve(obj));
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void Resolve_PlainObject_ReturnsNull()
    {
        Assert.Null(Resolve(new object()));
        Assert.Null(Resolve(null));
    }

    [Fact]
    public void Resolve_ThrowingProvider_ReturnsNull()
    {
        Assert.Null(Resolve(new ThrowingSessionProvider()));
    }

    [Fact]
    public void NormalizeKey_MixedCaseAndWhitespace_Normalizes()
    {
        Assert.Equal("key", MenuEngine.NormalizeKey("  KEY  "));
        Assert.Equal("key", MenuEngine.NormalizeKey("Key"));
        Assert.Equal("", MenuEngine.NormalizeKey("   "));
    }

    [Fact]
    public void NormalizeKey_DefinedOnceAndReusedByBuildChoicesAndHandle()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Menu.cs");
        Assert.Equal(1, SourceScan.Count(src, "NormalizeKey(string"));
        Assert.Contains("NormalizeKey(c.Key)", src);
        Assert.Contains("NormalizeKey(input)", src);
        Assert.DoesNotContain("inp.ToLowerInvariant().Trim()", src);
        Assert.DoesNotContain("c.Key.ToLowerInvariant().Trim()", src);
    }

    [Fact]
    public void Resolve_SingleBranchWithoutLegacyCases()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Menu.cs");
        Assert.Equal(1, SourceScan.Count(src, "is Commands.ISessionProvider"));
        Assert.DoesNotContain("is Session ", src);
        Assert.DoesNotContain("is GameObject go", src);
    }
}
