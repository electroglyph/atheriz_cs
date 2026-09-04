// Port of atheriz/tests/test_new_webclient.py:1
namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedNewWebclientTests
{
    // Port of test_copy_web_folder_copies_compiled_webclient + overwrites + uses_packaged_compiled_webclient
    // Mirrors atheriz/new.py:530 copy_web_folder -> C# GameTemplateGenerator.CopyWebFolder

    [Fact]
    public void CopyWebFolder_CopiesCompiledWebclient()
    {
        using var env = GlobalTestEnv.Enter();
        var tmp = Path.Combine(Path.GetTempPath(), $"copyweb_{Guid.NewGuid():N}");
        try
        {
            var source = Path.Combine(tmp, "package", "web");
            var compiledIndex = Path.Combine(source, "static", "webclient", "index.html");
            Directory.CreateDirectory(Path.GetDirectoryName(compiledIndex)!);
            File.WriteAllText(compiledIndex, "<script type=\"module\" src=\"/assets/webclient.js\"></script>");
            var drawIndex = Path.Combine(source, "static", "atheriz_draw", "index.html");
            Directory.CreateDirectory(Path.GetDirectoryName(drawIndex)!);
            File.WriteAllText(drawIndex, "draw");

            var destination = Path.Combine(tmp, "game");
            Directory.CreateDirectory(destination);

            Atheriz.Server.Infrastructure.GameTemplateGenerator.CopyWebFolder(destination, source);

            Assert.Equal(File.ReadAllText(compiledIndex),
                File.ReadAllText(Path.Combine(destination, "web", "static", "webclient", "index.html")));
            Assert.Equal("draw",
                File.ReadAllText(Path.Combine(destination, "web", "static", "atheriz_draw", "index.html")));
        }
        finally { try { Directory.Delete(tmp, true); } catch { } }
    }

    [Fact]
    public void CopyWebFolder_OverwritesCompiledWebclientIndex()
    {
        using var env = GlobalTestEnv.Enter();
        var tmp = Path.Combine(Path.GetTempPath(), $"copyweb2_{Guid.NewGuid():N}");
        try
        {
            var source = Path.Combine(tmp, "package", "web");
            var compiledIndex = Path.Combine(source, "static", "webclient", "index.html");
            Directory.CreateDirectory(Path.GetDirectoryName(compiledIndex)!);
            File.WriteAllText(compiledIndex, "new client");

            var destination = Path.Combine(tmp, "game");
            var oldIndex = Path.Combine(destination, "web", "static", "webclient", "index.html");
            Directory.CreateDirectory(Path.GetDirectoryName(oldIndex)!);
            File.WriteAllText(oldIndex, "old client");

            Atheriz.Server.Infrastructure.GameTemplateGenerator.CopyWebFolder(destination, source);

            Assert.Equal("new client", File.ReadAllText(oldIndex));
        }
        finally { try { Directory.Delete(tmp, true); } catch { } }
    }

    [Fact]
    public void CopyWebFolder_UsesPackagedCompiledWebclient()
    {
        using var env = GlobalTestEnv.Enter();
        var tmp = Path.Combine(Path.GetTempPath(), $"copyweb3_{Guid.NewGuid():N}");
        try
        {
            // Without explicit source, copies from engine's bundled web/wwwroot
            Atheriz.Server.Infrastructure.GameTemplateGenerator.CopyWebFolder(tmp);

            var webclientIndex = Path.Combine(tmp, "web", "static", "webclient", "index.html");
            var drawIndex = Path.Combine(tmp, "web", "static", "atheriz_draw", "index.html");
            Assert.True(File.Exists(webclientIndex), "webclient index should exist from bundled wwwroot");
            Assert.Contains("/assets/", File.ReadAllText(webclientIndex));
            Assert.True(File.Exists(drawIndex), "draw index should exist");
        }
        finally { try { Directory.Delete(tmp, true); } catch { } }
    }
}
