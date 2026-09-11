using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Objects;

// Hot-reload rewire points Puppet, LastPuppet, and every puppet-stack entry at
// the replacement instance matched by id, leaving unrelated ids and the
// already-current instance alone.
[Collection("Ported")]
public class SessionPuppetRewireTests
{
    [Fact]
    public void ReplacePuppetRefs_MatchingIds_RewiresPuppetAndLastPuppet()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var session = new Session();
            var orig = GameObject.Create("orig");
            var rep = GameObject.Create("rep");
            rep.Id = orig.Id;
            session.Puppet = orig;
            session.LastPuppet = orig;

            session.ReplacePuppetRefs(rep);

            Assert.Same(rep, session.Puppet);
            Assert.Same(rep, session.LastPuppet);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void ReplacePuppetRefs_MatchingIds_RewiresStackPrevAndTarget()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var session = new Session();
            var prev = GameObject.Create("prev");
            var target = GameObject.Create("target");
            var bystander = GameObject.Create("bystander");
            var repPrev = GameObject.Create("repPrev");
            repPrev.Id = prev.Id;
            var repTarget = GameObject.Create("repTarget");
            repTarget.Id = target.Id;
            session.PushPuppetEntry(prev, target);
            session.PushPuppetEntry(null, bystander);

            session.ReplacePuppetRefs(repPrev);
            var afterPrev = session.PuppetStack;
            Assert.Same(repPrev, afterPrev[0].Prev);
            Assert.Same(target, afterPrev[0].Target);
            Assert.Same(bystander, afterPrev[1].Target);

            session.ReplacePuppetRefs(repTarget);
            var afterTarget = session.PuppetStack;
            Assert.Same(repPrev, afterTarget[0].Prev);
            Assert.Same(repTarget, afterTarget[0].Target);
            Assert.Same(bystander, afterTarget[1].Target);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void ReplacePuppetRefs_NullPrevEntry_RewiresTargetKeepsNullPrev()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var session = new Session();
            var target = GameObject.Create("target");
            var repTarget = GameObject.Create("repTarget");
            repTarget.Id = target.Id;
            session.PushPuppetEntry(null, target);

            session.ReplacePuppetRefs(repTarget);

            var stack = session.PuppetStack;
            Assert.Null(stack[0].Prev);
            Assert.Same(repTarget, stack[0].Target);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void ReplacePuppetRefs_UnrelatedOrCurrentInstance_LeavesRefs()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var session = new Session();
            var puppet = GameObject.Create("puppet");
            var prev = GameObject.Create("prev");
            var target = GameObject.Create("target");
            var other = GameObject.Create("other");
            session.Puppet = puppet;
            session.LastPuppet = puppet;
            session.PushPuppetEntry(prev, target);

            session.ReplacePuppetRefs(other);

            Assert.Same(puppet, session.Puppet);
            Assert.Same(puppet, session.LastPuppet);
            var stack = session.PuppetStack;
            Assert.Same(prev, stack[0].Prev);
            Assert.Same(target, stack[0].Target);

            session.ReplacePuppetRefs(puppet);
            Assert.Same(puppet, session.Puppet);
            Assert.Same(puppet, session.LastPuppet);
        }
        finally { ObjectRegistry.ClearAll(); }
    }
}
