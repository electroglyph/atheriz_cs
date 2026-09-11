using System.Reflection;
using Atheriz.Core.Commands;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Tests.Features.Regression;

namespace Atheriz.Core.Tests.Features.Simplify3;

// MenuRunner.GetSess collapses to a single ISessionProvider read: Session
// returns itself and GameObject resolves through the same interface, while
// session-less and foreign callers yield null without throwing.
[Collection("Ported")]
public class MenuRunnerSessionTests
{
    private static Session? GetSess(object? caller)
    {
        var m = typeof(MenuRunner).GetMethod("GetSess", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(m);
        return (Session?)m.Invoke(null, new object?[] { caller });
    }

    private sealed class ThrowingSessionProvider : ISessionProvider
    {
        public Session? Session => throw new InvalidOperationException("boom");
    }

    [Fact]
    public void GetSess_SessionCaller_ReturnsItself()
    {
        var session = new Session();
        Assert.Same(session, GetSess(session));
    }

    [Fact]
    public void GetSess_GameObjectWithSession_ReturnsSession()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var session = new Session();
            var obj = GameObject.Create("seated");
            obj.Session = session;
            Assert.Same(session, GetSess(obj));
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void GetSess_SessionlessGameObject_ReturnsNull()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var obj = GameObject.Create("sessionless");
            Assert.Null(obj.Session);
            Assert.Null(GetSess(obj));
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void GetSess_PlainObject_ReturnsNull()
    {
        Assert.Null(GetSess(new object()));
        Assert.Null(GetSess(null));
    }

    [Fact]
    public void GetSess_ThrowingProvider_ReturnsNull()
    {
        Assert.Null(GetSess(new ThrowingSessionProvider()));
    }

    [Fact]
    public void NormalizeKey_MixedCaseAndWhitespace_Normalizes()
    {
        Assert.Equal("key", MenuEngine.NormalizeKey("  KEY  "));
        Assert.Equal("key", MenuEngine.NormalizeKey("Key"));
        Assert.Equal("", MenuEngine.NormalizeKey("   "));
    }

    [Fact]
    public void NormalizeKey_DefinedOnceAndReusedByBuildChoicesAndMenuRun()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Menu.cs");
        Assert.Equal(1, SourceScan.Count(src, "NormalizeKey(string"));
        Assert.Contains("NormalizeKey(c.Key)", src);
        Assert.Contains("MenuEngine.NormalizeKey(inp)", src);
        Assert.DoesNotContain("inp.ToLowerInvariant().Trim()", src);
        Assert.DoesNotContain("c.Key.ToLowerInvariant().Trim()", src);
    }

    [Fact]
    public void GetSess_SingleBranchWithoutLegacyCases()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Menu.cs");
        var region = SourceScan.Region(src, "static Session? GetSess(object? caller)");
        Assert.Contains("is ISessionProvider", region);
        Assert.DoesNotContain("is Session", region);
        Assert.DoesNotContain("is GameObject go", region);
    }
}
