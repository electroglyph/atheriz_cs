using Atheriz.Core.Globals;
using Atheriz.Core.Persistence;

namespace Atheriz.Core.Tests.Features.Globals;

// DoSetup creates tables only (database_setup.py:92-111) — no gametime
// seed row, so legacy save/time migration is never shadowed.
[Collection("Ported")]
public class DoSetupNoSeedTests
{
    [Fact]
    public void DoSetup_SeedsNoGameTimeRow()
    {
        using var env = GlobalTestEnv.Enter();
        AtherizDbContextFactory.DoSetup(env.TempPath);
        using var db = new AtherizDbContext(env.TempPath);
        Assert.Empty(db.GameTime.ToList());
    }
}
