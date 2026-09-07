using Atheriz.Core;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Objects;

// Deleting an account must behave the same regardless of static type: the
// bool Delete hides base Delete, so a GameObject-typed reference converges
// only at next save while the bool path deletes the row immediately
// (Account.cs:42-46 vs GameObject.Puppet.cs:394).
[Collection("Ported")]
public class AccountDeletionTests
{
    [Fact]
    public void Delete_ViaGameObjectRef_DeletesRowLikeBoolPath()
    {
        // Correct: both static types delete the DB row immediately and report it.
        using var env = GlobalTestEnv.Enter();
        var caller = GameObject.Create("admin", privilege: Privilege.Admin);
        ObjectRegistry.AddObject(caller);
        var accBool = Account.Create("boolpath", "pw");
        var accBase = Account.Create("basepath", "pw");

        var path = Environment.GetEnvironmentVariable("ATHERIZ_SAVE_PATH")!;
        using (var db = new AtherizDbContext(path))
        {
            db.Database.EnsureCreated();
            ObjectRegistry.SaveObjects(db, force: true);
        }
        using (var db = new AtherizDbContext(path))
        {
            Assert.NotNull(db.Objects.Find(accBool.Id));
            Assert.NotNull(db.Objects.Find(accBase.Id));
        }

        Assert.True(accBool.Delete(caller, false));
        GameObject asBase = accBase;
        var res = asBase.Delete(caller, recursive: false);
        Assert.NotNull(res);

        using (var db = new AtherizDbContext(path))
        {
            Assert.Null(db.Objects.Find(accBool.Id));
            Assert.Null(db.Objects.Find(accBase.Id));
        }
    }
}
