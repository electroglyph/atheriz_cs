using Atheriz.Core;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Objects;

// Deleting an account must behave the same regardless of static type: both
// the bool Delete and a GameObject-typed reference journal the delete and
// converge at the next save (DB discipline: no mid-game DB writes).
[Collection("Ported")]
public class AccountDeletionTests
{
    [Fact]
    public void Delete_ViaGameObjectRef_DeletesRowLikeBoolPath()
    {
        // Correct: both static types journal the delete; the row goes at save.
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

        using (var dbSave = new AtherizDbContext(path))
        {
            ObjectRegistry.SaveObjects(dbSave, force: true);
        }
        using (var db = new AtherizDbContext(path))
        {
            Assert.Null(db.Objects.Find(accBool.Id));
            Assert.Null(db.Objects.Find(accBase.Id));
        }
    }
}
