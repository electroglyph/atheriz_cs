using Atheriz.Core.Commands;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Commands;

// A corrupt group-channel reference must be reported, never thrown (GroupCommand.cs:27,
// contrast the safe lookup at :37).
[Collection("Ported")]
public class GroupCommandTests
{
    [Fact]
    public void Group_List_CorruptChannelRef_DoesNotThrow()
    {
        // When the stored id points at a non-channel, list must answer gracefully.
        ObjectRegistry.ClearAll();
        try
        {
            var hero = GameObject.Create("hero");
            var fake = GameObject.Create("pub");
            ObjectRegistry.AddObject(hero);
            ObjectRegistry.AddObject(fake);
            hero.GroupChannel = fake.Id;
            hero.ClearMessages();
            var job = CommandDispatcher.DispatchLoggedIn(hero, "group list", immediate: true);
            Assert.NotNull(job);
            var ex = Record.Exception(() => job!.Func(job.Caller, job.Args));
            Assert.Null(ex);
            Assert.Contains("Group channel not found", string.Join("\n", hero.PeekMessages()));
        }
        finally { ObjectRegistry.ClearAll(); }
    }
}
