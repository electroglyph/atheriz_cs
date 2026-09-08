using Atheriz.Core.Objects;
using MyGame;

namespace Atheriz.Core.Tests.Features.GameTemplate;

// the checked-in template stubs compile as real types against the current
// engine (namespace MyGame) and subclass the engine bases they mirror.
[Collection("Ported")]
public class GameTemplateCompileAsTypesTests
{
    [Fact]
    public void TemplateStubs_SubclassEngineBases()
    {
        Assert.True(typeof(CustomObject).IsSubclassOf(typeof(GameObject)));
        Assert.True(typeof(CustomAccount).IsSubclassOf(typeof(Account)));
        Assert.True(typeof(CustomNode).IsSubclassOf(typeof(Node)));
        Assert.True(typeof(CustomChannel).IsSubclassOf(typeof(Channel)));
        Assert.True(typeof(CustomScript).IsSubclassOf(typeof(Script)));
    }

    [Fact]
    public void TemplateStubs_LiveInMyGameNamespace()
    {
        Assert.Equal("MyGame", typeof(CustomObject).Namespace);
        Assert.Equal("MyGame", typeof(CustomAccount).Namespace);
        Assert.Equal("MyGame", typeof(CustomNode).Namespace);
        Assert.Equal("MyGame", typeof(CustomChannel).Namespace);
        Assert.Equal("MyGame", typeof(CustomScript).Namespace);
    }

    [Fact]
    public void TemplateStubs_InstantiateWithDefaultHooks()
    {
        using var env = GlobalTestEnv.Enter();
        var obj = new CustomObject("tmpl_obj");
        Assert.Equal("tmpl_obj", obj.Name);
        var acc = new CustomAccount();
        Assert.NotNull(acc);
        var node = new CustomNode();
        Assert.NotNull(node);
    }
}
