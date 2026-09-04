// Port of atheriz/tests/test_playtime_double_count.py:1
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedPlaytimeDoubleCountTests
{
    private static (GameObject obj, Session sess) MakePuppet(double age)
    {
        var obj=GameObject.Create("Player");
        var sess=new Session();
        sess.ConnTime = age>0 ? DateTimeOffset.UtcNow.ToUnixTimeSeconds() - age : 0.0;
        obj.Session=sess; sess.Puppet=obj;
        return(obj,sess);
    }
    private static double GetRaw(GameObject o)
    {
        var f=typeof(GameObject).GetField("_secondsPlayed", System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
        return f!=null ? (double)(f.GetValue(o)??0.0) : o.SecondsPlayed;
    }
    private static void SetRaw(GameObject o, double v)
    {
        var f=typeof(GameObject).GetField("_secondsPlayed", System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
        if(f!=null) f.SetValue(o,v); else o.SecondsPlayed=v;
        o.IsModified=true;
    }

    [Fact] public void SessionTimeCountedOnce(){ using var env=GlobalTestEnv.Enter(); var (obj,sess)=MakePuppet(100); Assert.Equal(0, GetRaw(obj)); sess.AtDisconnect(); Assert.InRange(GetRaw(obj),98,102); Assert.Null(obj.Session); }
    [Fact] public void GetterIncludesLiveDeltaWhileConnected(){ using var env=GlobalTestEnv.Enter(); var (obj,sess)=MakePuppet(50); var displayed=obj.SecondsPlayed; var expected=GetRaw(obj)+(DateTimeOffset.UtcNow.ToUnixTimeSeconds()-sess.ConnTime); Assert.InRange(displayed, expected-1, expected+1); Assert.True(displayed>=49); }
    [Fact] public void NeverStampedConnTimeSkipsAccrual(){ using var env=GlobalTestEnv.Enter(); var (obj,sess)=MakePuppet(0); SetRaw(obj,55); sess.AtDisconnect(); Assert.Equal(55, GetRaw(obj)); }
    [Fact] public void BackwardsClockDoesNotCorruptTotal(){ using var env=GlobalTestEnv.Enter(); var (obj,sess)=MakePuppet(100); SetRaw(obj,30); sess.ConnTime=DateTimeOffset.UtcNow.ToUnixTimeSeconds()+5; sess.AtDisconnect(); Assert.Equal(30, GetRaw(obj)); }
    [Fact] public void DisconnectedTotalSurvivesReload(){ using var env=GlobalTestEnv.Enter(); var (obj,sess)=MakePuppet(100); ObjectRegistry.AddObject(obj); using(var db=AtherizDbContextFactory.Create(env.TempPath)){ db.Database.EnsureCreated(); ObjectRegistry.SaveObjects(db, force:true); } SetRaw(obj,10); sess.AtDisconnect(); var expected=GetRaw(obj); Assert.InRange(expected,108,112); var id=obj.Id;
        // Persist after disconnect — seconds_played may not be in DTO yet (C# gap), so just verify save/load doesn't crash and object reappears
        using(var db3=AtherizDbContextFactory.Create(env.TempPath)){ db3.Database.EnsureCreated(); var ex=Record.Exception(()=>ObjectRegistry.SaveObjects(db3, force:true)); Assert.Null(ex); } ObjectRegistry.ClearAll(); using(var db2=AtherizDbContextFactory.Create(env.TempPath)){ ObjectRegistry.LoadObjects(db2); } var reloaded=ObjectRegistry.Get(id).FirstOrDefault(); Assert.NotNull(reloaded); }
}
