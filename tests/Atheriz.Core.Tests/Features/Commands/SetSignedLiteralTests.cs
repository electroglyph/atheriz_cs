using Atheriz.Core.Commands;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Commands;

// set parses signed/leading-dot numbers as numbers (set.py:141-143
// unconditional literal_eval), not strings.
[Collection("Ported")]
public class SetSignedLiteralTests
{
    private static void RunJob(CommandDispatcher.Job? job)
    {
        Assert.NotNull(job);
        job!.Func(job.Caller, job.Args);
    }

    private static int ReadScore(GameObject o)
    {
        Assert.True(o.TryGetExtraJson("score", out var je));
        return je.GetInt32();
    }

    private static double ReadRatio(GameObject o)
    {
        Assert.True(o.TryGetExtraJson("ratio", out var je));
        return je.GetDouble();
    }

    [Fact]
    public void Set_NegativeNumber_StoresInt()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var admin = GameObject.Create("admin", privilege: Privilege.Admin);
            ObjectRegistry.AddObject(admin);
            RunJob(CommandDispatcher.DispatchLoggedIn(admin, "set me score -5", immediate: true));
            Assert.Equal(-5, ReadScore(admin));
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void Set_PlusAndDotNumbers_StoreNumerically()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var admin = GameObject.Create("admin", privilege: Privilege.Admin);
            ObjectRegistry.AddObject(admin);
            RunJob(CommandDispatcher.DispatchLoggedIn(admin, "set me score +7", immediate: true));
            Assert.Equal(7, ReadScore(admin));
            RunJob(CommandDispatcher.DispatchLoggedIn(admin, "set me ratio .5", immediate: true));
            Assert.Equal(0.5, ReadRatio(admin));
        }
        finally { ObjectRegistry.ClearAll(); }
    }
}
