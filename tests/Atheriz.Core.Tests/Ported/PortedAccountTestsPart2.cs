using System.Text;
using System.Security.Cryptography;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence.Dto;

namespace Atheriz.Core.Tests.Ported;

// Port of atheriz/tests/test_account.py — part 2 (remaining tests)
[Collection("Ported")]
public class PortedAccountTestsPart2
{
    private static Account MakeAccount(string name="alice", string password="secret")
    {
        var acc = Account.Create(name, password);
        if (ObjectRegistry.Get(acc.Id).Count==0) ObjectRegistry.AddObject(acc);
        return acc;
    }

    [Fact] public void LoginCorrectCredentials()
    {
        using var env=GlobalTestEnv.Enter();
        var acc=MakeAccount("zeke","pw");
        Assert.True(acc.Login("zeke","pw", "testsalt"));
        Assert.True(acc.LoggedIn);
    }
    [Fact] public void LoginWrongPassword()
    {
        using var env=GlobalTestEnv.Enter();
        var acc=MakeAccount("anna","pw");
        Assert.False(acc.Login("anna","wrong", "testsalt"));
        Assert.False(acc.LoggedIn);
    }
    [Fact] public void LoginWrongName()
    {
        using var env=GlobalTestEnv.Enter();
        var acc=MakeAccount("bjorn","pw");
        Assert.False(acc.Login("not-bjorn","pw", "testsalt"));
        Assert.False(acc.LoggedIn);
    }
    [Fact] public void LoginBothWrong()
    {
        using var env=GlobalTestEnv.Enter();
        var acc=MakeAccount("cara","pw");
        Assert.False(acc.Login("cara","wrong", "testsalt"));
        Assert.False(acc.Login("not-cara","pw", "testsalt"));
        Assert.False(acc.Login("nope","nope", "testsalt"));
        Assert.False(acc.LoggedIn);
    }
    [Fact] public void LoginCorrectAfterWrong()
    {
        using var env=GlobalTestEnv.Enter();
        var acc=MakeAccount("drew","pw");
        Assert.False(acc.Login("drew","wrong", "testsalt"));
        Assert.True(acc.Login("drew","pw", "testsalt"));
        Assert.True(acc.LoggedIn);
    }
    [Fact] public void LoginLockUsable()
    {
        using var env=GlobalTestEnv.Enter();
        var acc=MakeAccount("erin","pw");
        Assert.True(acc.SyncRoot.TryEnterWriteLock(0));
        acc.SyncRoot.ExitWriteLock();
    }

    // Port of test_account.py:406 TestAccountHooks
    [Fact] public void AtPrePuppetDefaultReturnsTrue()
    {
        using var env=GlobalTestEnv.Enter();
        var acc=MakeAccount("finn","pw");
        var ch=GameObject.Create("c", isPc:true);
        Assert.True(acc.AtPrePuppet(ch));
    }
    [Fact] public void AtCreateDefaultIsNoop()
    {
        using var env=GlobalTestEnv.Enter();
        var acc=new Account();
        var ex=Record.Exception(()=> acc.AtCreate());
        Assert.Null(ex);
    }
    [Fact] public void AtDisconnectDefaultIsNoop()
    {
        using var env=GlobalTestEnv.Enter();
        var acc=new Account();
        var ex=Record.Exception(()=> acc.AtDisconnect());
        Assert.Null(ex);
    }

    // Port of test_account.py:423 TestAccountDbOps
    [Fact] public void GetSaveOpsReturnsInsertOrReplace()
    {
        using var env=GlobalTestEnv.Enter();
        var acc=MakeAccount("gabe","pw");
        var (sql, pars)=acc.GetSaveOps();
        Assert.Equal("INSERT OR REPLACE INTO objects (id, data) VALUES (?, ?)", sql);
        Assert.Equal(acc.Id, (int)pars[0]);
        Assert.IsType<string>(pars[1]);
    }
    [Fact] public void GetSaveOpsDataCanBeUnpickled()
    {
        using var env=GlobalTestEnv.Enter();
        var acc=MakeAccount("hope","pw");
        var (_, pars)=acc.GetSaveOps();
        var json=(string)pars[1];
        var dto=GameObjectDtoSerializer.FromJson(json);
        Assert.Equal(acc.Id, dto.Id);
        Assert.Equal(acc.Name, dto.Name);
    }
    [Fact] public void GetSaveOpsDoesNotClearIsModified()
    {
        using var env=GlobalTestEnv.Enter();
        var acc=MakeAccount("inga","pw");
        Assert.True(acc.IsModified);
        acc.GetSaveOps();
        Assert.True(acc.IsModified);
    }
    [Fact] public void GetDelOpsReturnsCorrectSql()
    {
        using var env=GlobalTestEnv.Enter();
        var acc=MakeAccount("juno","pw");
        var (sql, pars)=acc.GetDelOps();
        Assert.Equal("DELETE FROM objects WHERE id = ?", sql);
        Assert.Equal(acc.Id, (int)pars[0]);
    }
    [Fact] public void GetDelOpsDoesNotChangeIsModified()
    {
        using var env=GlobalTestEnv.Enter();
        var acc=MakeAccount("kate","pw");
        acc.GetDelOps();
        Assert.True(acc.IsModified);
    }

    // Port of test_account.py:462 TestAccountPickle — adapted to JSON DTO not pickle
    [Fact] public void GetStateExcludesLock()
    {
        using var env=GlobalTestEnv.Enter();
        var acc=MakeAccount("lena","pw");
        var dto=acc.ToDto();
        var json=GameObjectDtoSerializer.ToJson(dto);
        // Lock not serialized: DTO has no "lock" key, but "locks" array is different — check for isolated key
        Assert.DoesNotContain("\"lock\"", json);
        Assert.Contains("password", json);
    }
    [Fact] public void SetStateRestoresLock()
    {
        using var env=GlobalTestEnv.Enter();
        var acc=MakeAccount("maya","pw");
        var dto=acc.ToDto();
        var json=GameObjectDtoSerializer.ToJson(dto);
        var dto2=GameObjectDtoSerializer.FromJson(json);
        var acc2=Account.FromDto(dto2);
        Assert.NotSame(acc.SyncRoot, acc2.SyncRoot);
        Assert.NotNull(acc2.SyncRoot);
    }
    [Fact] public void PickleRoundtripPreservesState()
    {
        using var env=GlobalTestEnv.Enter();
        var acc=MakeAccount("nick","pw");
        var c=GameObject.Create("c1", isPc:true); c.Id=100; ObjectRegistry.AddObject(c);
        acc.AddCharacter(c);
        var dto=acc.ToDto();
        var json=GameObjectDtoSerializer.ToJson(dto);
        var acc2=Account.FromDto(GameObjectDtoSerializer.FromJson(json));
        Assert.Equal(acc.Id, acc2.Id);
        Assert.Equal(acc.Name, acc2.Name);
        Assert.Equal(acc.PasswordHash, acc2.PasswordHash);
        Assert.Equal(new[]{100}, acc2.Characters);
    }
    [Fact] public void PickledLockIsFresh()
    {
        using var env=GlobalTestEnv.Enter();
        var acc=MakeAccount("olive2","pw");
        var dto=acc.ToDto();
        var json=GameObjectDtoSerializer.ToJson(dto);
        var acc2=Account.FromDto(GameObjectDtoSerializer.FromJson(json));
        Assert.NotSame(acc.SyncRoot, acc2.SyncRoot);
        Assert.True(acc.SyncRoot.TryEnterWriteLock(0)); acc.SyncRoot.ExitWriteLock();
        Assert.True(acc2.SyncRoot.TryEnterWriteLock(0)); acc2.SyncRoot.ExitWriteLock();
    }
    [Fact] public void PickledAccountCanLogin()
    {
        using var env=GlobalTestEnv.Enter();
        var acc=MakeAccount("pete","pw");
        var dto=acc.ToDto();
        var json=GameObjectDtoSerializer.ToJson(dto);
        var acc2=Account.FromDto(GameObjectDtoSerializer.FromJson(json));
        Assert.True(acc2.CheckPassword("pw", "testsalt"));
        Assert.True(acc2.Login("pete","pw", "testsalt"));
    }

    // Port of test_account.py:513 TestAccountThreadSafety
    [Fact] public void ConcurrentCharacterAdds()
    {
        using var env=GlobalTestEnv.Enter();
        var acc=MakeAccount("quinn2","pw");
        var errs=new System.Collections.Concurrent.ConcurrentBag<Exception>();
        var threads=Enumerable.Range(0,20).Select(i=> new System.Threading.Thread(()=>{
            try{ var c=GameObject.Create($"c{i}", isPc:true); c.Id=i; ObjectRegistry.AddObject(c); acc.AddCharacter(c);}catch(Exception ex){errs.Add(ex);}
        })).ToList();
        threads.ForEach(t=>t.Start()); threads.ForEach(t=>t.Join());
        Assert.Empty(errs);
        Assert.Equal(20, acc.Characters.Count);
        Assert.Equal(Enumerable.Range(0,20), acc.Characters.OrderBy(x=>x));
    }

    // Port of test_account.py:537 TestAccountIntegration
    [Fact] public void FullLifecycle()
    {
        using var env=GlobalTestEnv.Enter();
        var acc=Account.Create("ruth","initial");
        if (ObjectRegistry.Get(acc.Id).Count==0) ObjectRegistry.AddObject(acc);
        var c=GameObject.Create("ruth_char", isPc:true); c.Id=1000; ObjectRegistry.AddObject(c);
        acc.AddCharacter(c);
        Assert.Contains(1000, acc.Characters);
        acc.SetPassword("updated", "testsalt");
        Assert.True(acc.CheckPassword("updated", "testsalt"));
        Assert.False(acc.CheckPassword("initial", "testsalt"));
        Assert.True(acc.Login("ruth","updated", "testsalt"));
        Assert.True(acc.Delete());
        Assert.True(acc.IsDeleted);
        Assert.DoesNotContain(acc, ObjectRegistry.FilterBy(_=>true));
    }
    [Fact] public void SubclassHooksCanVeto()
    {
        using var env=GlobalTestEnv.Enter();
        var orig=Account.AtDeleteHook;
        Account.AtDeleteHook=_=>false;
        var a=Account.Create("sam2","pw");
        if(ObjectRegistry.Get(a.Id).Count==0) ObjectRegistry.AddObject(a);
        try{
            Assert.False(a.Delete());
            Assert.Contains(a, ObjectRegistry.FilterBy(_=>true));
            Account.AtDeleteHook=_=>true;
            Assert.True(a.Delete());
        } finally { Account.AtDeleteHook=orig; }
    }

    // Port of test_account.py:575 TestAccountRemoveCharacter
    [Fact] public void RemoveMissingCharacterIsNoop()
    {
        using var env=GlobalTestEnv.Enter();
        var account=MakeAccount("bob","pw1");
        var other=GameObject.Create("other"); ObjectRegistry.AddObject(other);
        var ex=Record.Exception(()=> account.RemoveCharacter(other));
        Assert.Null(ex);
    }
    [Fact] public void RemoveAddedCharacterWorks()
    {
        using var env=GlobalTestEnv.Enter();
        var account=MakeAccount("bob","pw1");
        var ch=GameObject.Create("hero", isPc:true); ObjectRegistry.AddObject(ch);
        account.AddCharacter(ch);
        account.RemoveCharacter(ch);
        Assert.DoesNotContain(ch.Id, account.Characters);
    }

    // Port of test_account.py:593 TestAccountDisconnect
    [Fact] public void FailedLoginClearsLoggedIn()
    {
        using var env=GlobalTestEnv.Enter();
        var account=MakeAccount("bob","pw1");
        Assert.True(account.Login("bob","pw1", "testsalt"));
        Assert.True(account.LoggedIn);
        Assert.False(account.Login("bob","wrong", "testsalt"));
        Assert.False(account.LoggedIn);
    }
    [Fact] public void DisconnectClearsLoggedIn()
    {
        using var env=GlobalTestEnv.Enter();
        var account=MakeAccount("bob","pw1");
        Assert.True(account.Login("bob","pw1", "testsalt"));
        Assert.True(account.LoggedIn);
        account.AtDisconnect();
        Assert.False(account.LoggedIn);
    }

    // Port of test_account.py:614 TestPerUserSalt
    [Fact] public void SamePasswordHasSameHashWithGlobalSalt()
    {
        using var env=GlobalTestEnv.Enter();
        var acc1=MakeAccount("alice_salt","samepassword123");
        var acc2=MakeAccount("bob_salt","samepassword123");
        Assert.Equal(acc1.PasswordHash, acc2.PasswordHash);
        Assert.True(acc1.CheckPassword("samepassword123", "testsalt"));
        Assert.True(acc2.CheckPassword("samepassword123", "testsalt"));
        Assert.False(acc1.CheckPassword("wrong", "testsalt"));
    }
    [Fact] public void HashUsesGlobalSalt()
    {
        using var env=GlobalTestEnv.Enter();
        var orig=SaltProvider.GetSalt();
        SaltProvider.SetSaltForTesting("globalsalt");
        try{
            var a1=Account.Create("u1","pw123456");
            if(ObjectRegistry.Get(a1.Id).Count==0) ObjectRegistry.AddObject(a1);
            var a2=Account.Create("u2","pw123456");
            if(ObjectRegistry.Get(a2.Id).Count==0) ObjectRegistry.AddObject(a2);
            Assert.Equal(a1.PasswordHash, a2.PasswordHash);
            var h=Account.HashPassword("x", "globalsalt");
            Assert.Equal(64, h.Length);
        } finally { SaltProvider.SetSaltForTesting(orig); }
    }
}
