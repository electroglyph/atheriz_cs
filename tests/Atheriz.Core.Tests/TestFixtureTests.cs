using Atheriz.Core.Globals;
using Atheriz.Core.Persistence;

namespace Atheriz.Core.Tests;

[Collection("Ported")]
public class TestFixtureTests
{
    // Port of conftest.py global_test_env roundtrip
    [Fact]
    public async Task Fixture_Roundtrip_CreatesTempAndCleans()
    {
        var origEnv = Environment.GetEnvironmentVariable("ATHERIZ_SAVE_PATH");
        var scope = await GlobalTestEnv.EnterAsync(nameof(Fixture_Roundtrip_CreatesTempAndCleans));
        try
        {
            Assert.True(Path.IsPathRooted(scope.TempPath)); // absolute guard Port 66
            Assert.True(Directory.Exists(scope.TempPath));
            Assert.Equal(0, ObjectRegistry.Count); // fresh DB + clear Port 119
            Assert.Equal(-1, IdGenerator.GetId()); // Port 163
            // guard passes for absolute path
            using var db = new AtherizDbContext(scope.TempPath);
            Assert.True(File.Exists(Path.Combine(scope.TempPath, "database.sqlite3"))); // EnsureCreated may create file lazily
            var obj = Atheriz.Core.Objects.GameObject.Create("testobj");
            ObjectRegistry.AddObject(obj);
            Assert.Equal(1, ObjectRegistry.Count);
        }
        finally
        {
            await GlobalTestEnv.ExitAsync(scope);
        }
        Assert.False(Directory.Exists(scope.TempPath)); // Port 232 rmtree
        Assert.Equal(origEnv, Environment.GetEnvironmentVariable("ATHERIZ_SAVE_PATH")); // Port 238
        Assert.Equal(0, ObjectRegistry.Count); // cleared again Port 240
    }

    [Fact]
    public void Fixture_SyncWrapper_EnterCreatesTemp()
    {
        var orig = Environment.GetEnvironmentVariable("ATHERIZ_SAVE_PATH");
        using var scope = GlobalTestEnv.Enter(nameof(Fixture_SyncWrapper_EnterCreatesTemp));
        Assert.True(Path.IsPathRooted(scope.TempPath));
        Assert.True(Directory.Exists(scope.TempPath));
        Assert.Equal(0, ObjectRegistry.Count);
        // exit via Dispose
    }
}
