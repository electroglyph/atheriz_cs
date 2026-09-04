// Port of atheriz/tests/test_salt_creation.py:1
using Atheriz.Core.Globals;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedSaltCreationTests
{
    [Fact] public void Salt_NotCreated_InNonGameFolder_Throws()
    {
        using var env = GlobalTestEnv.Enter();
        var tmp = Path.Combine(Path.GetTempPath(), $"salt_nogame_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmp);
        var orig = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(tmp);
            SaltProvider.Clear();
            var ex = Assert.Throws<InvalidOperationException>(() => SaltProvider.GetSalt("secret"));
            Assert.Contains("Cannot determine salt", ex.Message);
            Assert.False(Directory.Exists(Path.Combine(tmp, "secret")));
        }
        finally
        {
            Directory.SetCurrentDirectory(orig);
            SaltProvider.Clear();
            try{Directory.Delete(tmp,true);}catch{}
        }
    }
    [Fact] public void Salt_Created_InGameFolder()
    {
        using var env = GlobalTestEnv.Enter();
        var tmp = Path.Combine(Path.GetTempPath(), $"salt_game_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmp);
        var orig = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(tmp);
            File.WriteAllText(Path.Combine(tmp,"settings.py"), "# settings");
            File.WriteAllText(Path.Combine(tmp,"__init__.py"), "");
            Directory.CreateDirectory(Path.Combine(tmp,"save"));
            SaltProvider.Clear();
            var salt = SaltProvider.GetSalt(Path.Combine(tmp,"secret"));
            Assert.NotNull(salt);
            Assert.True(File.Exists(Path.Combine(tmp,"secret","salt.txt")));
            var saved = File.ReadAllText(Path.Combine(tmp,"secret","salt.txt")).Trim();
            Assert.Equal(salt, saved);
        }
        finally
        {
            Directory.SetCurrentDirectory(orig);
            SaltProvider.Clear();
            try{Directory.Delete(tmp,true);}catch{}
        }
    }
    [Fact] public void Salt_Created_WithAbsolutePath()
    {
        using var env = GlobalTestEnv.Enter();
        var tmp = Path.Combine(Path.GetTempPath(), $"salt_abs_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmp);
        var absSecret = Path.Combine(tmp, "secret");
        try
        {
            SaltProvider.Clear();
            var salt = SaltProvider.GetSalt(absSecret);
            Assert.NotNull(salt);
            Assert.True(File.Exists(Path.Combine(absSecret,"salt.txt")));
        }
        finally
        {
            SaltProvider.Clear();
            try{Directory.Delete(tmp,true);}catch{}
        }
    }
    [Fact] public void Salt_ConcurrentRace_ReturnsSame()
    {
        using var env = GlobalTestEnv.Enter();
        var tmp = Path.Combine(Path.GetTempPath(), $"salt_race_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmp);
        try
        {
            var secret = Path.Combine(tmp,"secret");
            SaltProvider.Clear();
            var results = new System.Collections.Concurrent.ConcurrentBag<string>();
            var threads = Enumerable.Range(0,2).Select(_ => new System.Threading.Thread(() => {
                try{ results.Add(SaltProvider.GetSalt(secret)); }catch{}
            })).ToList();
            threads.ForEach(t=>t.Start());
            threads.ForEach(t=>t.Join(2000));
            Assert.Equal(2, results.Count);
            Assert.Equal(results.First(), results.Last());
            Assert.True(File.Exists(Path.Combine(secret,"salt.txt")));
        }
        finally
        {
            SaltProvider.Clear();
            try{Directory.Delete(tmp,true);}catch{}
        }
    }
}
