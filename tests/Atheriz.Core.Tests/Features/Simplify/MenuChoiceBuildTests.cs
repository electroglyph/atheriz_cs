using Atheriz.Core.Tests.Features.Regression;

namespace Atheriz.Core.Tests.Features.Simplify;

// One choice-dict build for the sync/async render paths: case-insensitive map
// + byte-identical ToLowerInvariant().Trim() duplicate-key throw.
[Collection("Ported")]
public class MenuChoiceBuildTests
{
    private static (string, List<Choice>) DupNode(MenuContext ctx)
        => ("text", [new Choice("A", "first"), new Choice(" a ", "second")]);

    [Fact]
    public void SyncRender_DuplicateKey_Throws()
    {
        using var env = GlobalTestEnv.Enter();
        var ex = Assert.Throws<InvalidOperationException>(() => new MenuEngine(null, DupNode));
        Assert.Equal("duplicate menu key: ' a '", ex.Message);
    }

    [Fact]
    public async Task AsyncRender_DuplicateKey_ThrowsIdenticalMessage()
    {
        using var env = GlobalTestEnv.Enter();
        Task<(string, List<Choice>)> Start(MenuContext ctx) => Task.FromResult(DupNode(ctx));
        var engine = new MenuEngine(null, Start);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => engine.RenderAsync());
        Assert.Equal("duplicate menu key: ' a '", ex.Message);
    }

    [Fact]
    public void ChoiceBuild_LivesInOneHelper()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Menu.cs");
        Assert.Equal(1, SourceScan.Count(src, "BuildChoices(List<Choice>"));
        Assert.Equal(2, SourceScan.Count(src, "=BuildChoices(cl)"));
    }

    // The rendered menu text is a property, not a Java-style getter: pure,
    // cheap, and side-effect free, so it reads as state.
    [Fact]
    public void Display_IsPropertyNotMethod()
    {
        using var env = GlobalTestEnv.Enter();
        static (string, List<Choice>) Node(MenuContext ctx)
            => ("hello", [new Choice("a", "first")]);
        var engine = new MenuEngine(null, Node);
        Assert.Contains("hello", engine.Display);
        Assert.Contains("[a] first", engine.Display);
        var src = SourceScan.Read("src", "Atheriz.Core", "Menu.cs");
        Assert.DoesNotContain("GetDisplay()", src);
        Assert.Contains("string Display", src);
    }
}
