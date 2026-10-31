using Atheriz.Core.Plugins;

namespace Atheriz.Core.Tests.Features.Plugins;

// The engine staleness rule must match the per-game build.sh/build.cmd/
// build.ps1 rule exactly: missing dll or any engine source newer than it
// (bin/obj never count). These pins stage fake checkouts, so no real engine
// paths or compiler runs are involved.
[Collection("Ported")]
public class EngineBuilderTests
{
    private static string StageEngineRoot(string prefix, bool withDll, bool withCsproj)
    {
        var root = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        if (withCsproj)
        {
            var csprojDir = Path.Combine(root, "src", "Atheriz.Server");
            Directory.CreateDirectory(csprojDir);
            File.WriteAllText(Path.Combine(csprojDir, "Atheriz.Server.csproj"), "<Project></Project>");
        }
        if (withDll)
        {
            var binDir = Path.Combine(root, "src", "Atheriz.Server", "bin", "Release", "net10.0");
            Directory.CreateDirectory(binDir);
            File.WriteAllBytes(Path.Combine(binDir, "Atheriz.Server.dll"), [1, 2, 3]);
        }
        var srcDir = Path.Combine(root, "src", "Core");
        Directory.CreateDirectory(srcDir);
        File.WriteAllText(Path.Combine(srcDir, "a.cs"), "// engine source");
        return root;
    }

    private static void TouchAll(string root, DateTime utc)
    {
        foreach (var f in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
        {
            try { File.SetLastWriteTimeUtc(f, utc); } catch { }
        }
    }

    [Fact]
    public void EngineNeedsBuild_MissingDll_IsStale()
    {
        var root = StageEngineRoot("atheriz_eng_", withDll: false, withCsproj: true);
        try
        {
            Assert.True(EngineBuilder.EngineNeedsBuild(root, out var reason));
            Assert.NotEmpty(reason);
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void EngineNeedsBuild_FreshTree_IsNotStale()
    {
        var root = StageEngineRoot("atheriz_eng_", withDll: true, withCsproj: true);
        try
        {
            TouchAll(root, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            File.SetLastWriteTimeUtc(
                Path.Combine(root, "src", "Atheriz.Server", "bin", "Release", "net10.0", "Atheriz.Server.dll"),
                new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));
            Assert.False(EngineBuilder.EngineNeedsBuild(root, out var reason));
            Assert.Empty(reason);
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void EngineNeedsBuild_NewerSource_IsStale()
    {
        var root = StageEngineRoot("atheriz_eng_", withDll: true, withCsproj: true);
        try
        {
            TouchAll(root, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            File.SetLastWriteTimeUtc(
                Path.Combine(root, "src", "Atheriz.Server", "bin", "Release", "net10.0", "Atheriz.Server.dll"),
                new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));
            File.SetLastWriteTimeUtc(
                Path.Combine(root, "src", "Core", "a.cs"),
                new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc));
            Assert.True(EngineBuilder.EngineNeedsBuild(root, out _));
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void EngineNeedsBuild_OnlyBuildOutputNewer_IsNotStale()
    {
        // A rebuild leaves fresh files under bin/obj; those must never count
        // as sources, or every build would look stale forever.
        var root = StageEngineRoot("atheriz_eng_", withDll: true, withCsproj: true);
        try
        {
            TouchAll(root, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            var dll = Path.Combine(root, "src", "Atheriz.Server", "bin", "Release", "net10.0", "Atheriz.Server.dll");
            File.SetLastWriteTimeUtc(dll, new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));
            var objCopy = Path.Combine(root, "src", "Core", "obj", "a.cs");
            Directory.CreateDirectory(Path.GetDirectoryName(objCopy)!);
            File.WriteAllText(objCopy, "// stale copy");
            File.SetLastWriteTimeUtc(objCopy, new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc));
            Assert.False(EngineBuilder.EngineNeedsBuild(root, out _));
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void EngineNeedsBuild_NewerPropsFile_IsStale()
    {
        var root = StageEngineRoot("atheriz_eng_", withDll: true, withCsproj: true);
        try
        {
            TouchAll(root, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            File.SetLastWriteTimeUtc(
                Path.Combine(root, "src", "Atheriz.Server", "bin", "Release", "net10.0", "Atheriz.Server.dll"),
                new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));
            File.WriteAllText(Path.Combine(root, "Directory.Build.props"), "<Project></Project>");
            File.SetLastWriteTimeUtc(
                Path.Combine(root, "Directory.Build.props"),
                new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc));
            Assert.True(EngineBuilder.EngineNeedsBuild(root, out _));
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void EngineNeedsBuild_MissingCsproj_IsNotStale()
    {
        // No sources to compare against: warn-and-skip upstream, not stale.
        var root = StageEngineRoot("atheriz_eng_", withDll: true, withCsproj: false);
        try
        {
            TouchAll(root, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            Assert.False(EngineBuilder.EngineNeedsBuild(root, out _));
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public async Task EnsureEngineBuilt_FreshTree_DoesNotBuild()
    {
        var root = StageEngineRoot("atheriz_eng_", withDll: true, withCsproj: true);
        try
        {
            TouchAll(root, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            File.SetLastWriteTimeUtc(
                Path.Combine(root, "src", "Atheriz.Server", "bin", "Release", "net10.0", "Atheriz.Server.dll"),
                new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));
            var called = false;
            var result = await EngineBuilder.EnsureEngineBuiltAsync(root,
                (_, _) => { called = true; return Task.FromResult(true); });
            Assert.True(result.Ok);
            Assert.False(result.WasBuilt);
            Assert.False(called);
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public async Task EnsureEngineBuilt_StaleTree_BuildsServerProject()
    {
        var root = StageEngineRoot("atheriz_eng_", withDll: false, withCsproj: true);
        try
        {
            string? built = null;
            var result = await EngineBuilder.EnsureEngineBuiltAsync(root,
                (csproj, _) => { built = csproj; return Task.FromResult(true); });
            Assert.True(result.Ok);
            Assert.True(result.WasBuilt);
            Assert.Equal(Path.Combine(root, "src", "Atheriz.Server", "Atheriz.Server.csproj"), built);
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public async Task EnsureEngineBuilt_BuildFailure_IsNotOk()
    {
        var root = StageEngineRoot("atheriz_eng_", withDll: false, withCsproj: true);
        try
        {
            var result = await EngineBuilder.EnsureEngineBuiltAsync(root,
                (_, _) => Task.FromResult(false));
            Assert.False(result.Ok);
            Assert.False(result.WasBuilt);
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public async Task EnsureEngineBuilt_UnknownCheckout_SkipsOk()
    {
        var missing = Path.Combine(Path.GetTempPath(), "atheriz_eng_nonexistent_" + Guid.NewGuid().ToString("N"));
        var called = false;
        var result = await EngineBuilder.EnsureEngineBuiltAsync(missing,
            (_, _) => { called = true; return Task.FromResult(true); });
        Assert.True(result.Ok);
        Assert.False(result.WasBuilt);
        Assert.False(called);
    }

    [Fact]
    public void TryResolveEngineRoot_EnvVarWins()
    {
        var root = StageEngineRoot("atheriz_eng_", withDll: false, withCsproj: true);
        var orig = Environment.GetEnvironmentVariable("ATHERIZ_ROOT");
        Environment.SetEnvironmentVariable("ATHERIZ_ROOT", root);
        try
        {
            Assert.True(EngineBuilder.TryResolveEngineRoot(out var resolved));
            Assert.Equal(Path.GetFullPath(root), resolved);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ATHERIZ_ROOT", orig);
            try { Directory.Delete(root, true); } catch { }
        }
    }
}
