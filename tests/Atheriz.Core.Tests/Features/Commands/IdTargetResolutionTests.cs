using Atheriz.Core.Commands;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Commands;

// Shared #id lookup: bad formats and unknown ids report usage errors,
// known ids resolve the object.
[Collection("Ported")]
public sealed class IdTargetResolutionTests
{
    private static GameObject MakeHero(string name = "hero")
    {
        ObjectRegistry.ClearAll();
        var hero = GameObject.Create(name, isPc: true);
        ObjectRegistry.AddObject(hero);
        hero.ClearMessages();
        return hero;
    }

    [Fact]
    public void IdTarget_BadFormat_ReportsUsage()
    {
        // The shared resolver rejects non-numeric #ids with the exact message
        // both Ban and Puppet used before the merge.
        var hero = MakeHero();
        try
        {
            var (target, err) = TargetResolution.ResolveById(hero, "#x");
            Assert.Null(target);
            Assert.Equal("Invalid ID format. Use #<number>.", err);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void IdTarget_UnknownId_ReportsNoObject()
    {
        // Unknown numeric ids report the id back verbatim.
        var hero = MakeHero();
        try
        {
            var (target, err) = TargetResolution.ResolveById(hero, "#299991");
            Assert.Null(target);
            Assert.Equal("No object found with ID 299991.", err);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void IdTarget_KnownId_ResolvesObject()
    {
        // A live id resolves to the object with no error.
        var hero = MakeHero();
        try
        {
            var (target, err) = TargetResolution.ResolveById(hero, $"#{hero.Id}");
            Assert.Null(err);
            Assert.Same(hero, target);
        }
        finally { ObjectRegistry.ClearAll(); }
    }
}
