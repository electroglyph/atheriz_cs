using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Simplify3;

// The closed-DB guard routes every load/save failure through one core with
// the caller's verb: load sites log "skipping load", save sites log
// "skipping save", and open-DB failures still propagate.
[Collection("Ported")]
public sealed class ClosedDbGuardTests
{
    private static int CountOccurrences(string text, string needle)
    {
        int n = 0, i = 0;
        while ((i = text.IndexOf(needle, i, StringComparison.Ordinal)) >= 0)
        {
            n++;
            i += needle.Length;
        }
        return n;
    }

    [Fact]
    public void LoadObjects_ClosedDb_LogsLoadVerbAndSkips()
    {
        using var env = GlobalTestEnv.Enter();
        var live = GameObject.Create("closedlive");
        ObjectRegistry.AddObject(live);
        string log;
        AtherizDbContextFactory.CloseDatabase();
        try
        {
            using var cap = new CaptureAtherizLog();
            var ex = Record.Exception(() => ObjectRegistry.LoadObjects(env.TempPath));
            log = cap.Read();
            Assert.Null(ex);
        }
        finally
        {
            AtherizDbContextFactory.ReopenDatabase();
            AtherizDbContextFactory.DoSetup(env.TempPath);
        }
        Assert.Contains("database closed; skipping load", log);
        Assert.NotEmpty(ObjectRegistry.Get(live.Id));
    }

    [Fact]
    public void SaveObjects_ClosedDb_LogsSaveVerbAndSkips()
    {
        using var env = GlobalTestEnv.Enter();
        var obj = GameObject.Create("closedsave");
        ObjectRegistry.AddObject(obj);
        obj.Desc = "dirty";
        obj.IsModified = true;
        string log;
        AtherizDbContextFactory.CloseDatabase();
        try
        {
            using var cap = new CaptureAtherizLog();
            var ex = Record.Exception(() => ObjectRegistry.SaveObjects(env.TempPath));
            log = cap.Read();
            Assert.Null(ex);
        }
        finally
        {
            AtherizDbContextFactory.ReopenDatabase();
            AtherizDbContextFactory.DoSetup(env.TempPath);
        }
        Assert.Contains("database closed; skipping save", log);
    }

    [Fact]
    public void LoadObjects_DisposedDbClosedFlag_LogsBothWarningLines()
    {
        using var env = GlobalTestEnv.Enter();
        var db = new AtherizDbContext(env.TempPath);
        db.Dispose();
        string log;
        AtherizDbContextFactory.CloseDatabase();
        try
        {
            using var cap = new CaptureAtherizLog();
            var ex = Record.Exception(() => ObjectRegistry.LoadObjects(db));
            log = cap.Read();
            Assert.Null(ex);
        }
        finally
        {
            AtherizDbContextFactory.ReopenDatabase();
            AtherizDbContextFactory.DoSetup(env.TempPath);
        }
        Assert.Contains("database closed; skipping load", log);
        Assert.True(CountOccurrences(log, "database closed") >= 2);
    }

    [Fact]
    public void LoadObjects_DisposedDbOpenFlag_Rethrows()
    {
        using var env = GlobalTestEnv.Enter();
        var db = new AtherizDbContext(env.TempPath);
        db.Dispose();
        Assert.ThrowsAny<Exception>(() => ObjectRegistry.LoadObjects(db));
    }
}
