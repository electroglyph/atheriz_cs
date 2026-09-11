using System.Reflection;
using Atheriz.Core.Globals;
using Atheriz.Core.Persistence;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Simplify3;

// Autosave handler resolution is explicit arg, then cached, then singleton
// getter; an explicit tick still journals and ends clean.
[Collection("Ported")]
public class AutosaveResolveTests
{
    private static string? InvokeResolve(string? arg, string? cached, Func<string> getter, out string? cachedAfter)
    {
        var method = typeof(Autosave).GetMethod("Resolve", BindingFlags.NonPublic | BindingFlags.Static)!;
        var generic = method.MakeGenericMethod(typeof(string));
        object?[] args = [arg, cached, getter];
        var result = (string?)generic.Invoke(null, args);
        cachedAfter = (string?)args[1];
        return result;
    }

    [Fact]
    public void Resolve_ExplicitArg_WinsOverCachedAndGetter()
    {
        string? cached = "cached";
        bool getterCalled = false;
        var result = InvokeResolve("explicit", cached, () => { getterCalled = true; return "getter"; }, out _);
        Assert.Equal("explicit", result);
        Assert.False(getterCalled);
    }

    [Fact]
    public void Resolve_NullArg_FallsBackToCachedThenGetter()
    {
        string? cached = "cached";
        bool getterCalled = false;
        var fromCache = InvokeResolve(null, cached, () => { getterCalled = true; return "getter"; }, out _);
        Assert.Equal("cached", fromCache);
        Assert.False(getterCalled);

        var fromGetter = InvokeResolve(null, null, () => "getter", out _);
        Assert.Equal("getter", fromGetter);
    }

    [Fact]
    public void AutosaveTick_ExplicitHandlers_CompletesAndMarksClean()
    {
        using var env = GlobalTestEnv.Enter();
        try
        {
            var settings = new AtherizSettings { TimeSystemEnabled = false };
            var mh = new MapHandler(autoLoad: false);
            var nh = new NodeHandler(autoLoad: false);
            Autosave.AutosaveTick(settings, mh, nh, gameTime: null);
            Assert.False(CheckpointJournal.IsDirty(env.TempPath));
        }
        finally
        {
            Autosave.Reset();
        }
    }
}
