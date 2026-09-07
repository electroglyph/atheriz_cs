using Atheriz.Core;
using Atheriz.Core.Commands;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Commands;

// Quoted string contents must pass through verbatim (SetCommand.cs:49-64),
// and conversion failures must not be misreported as read-only (SetHelper.cs:72, SetCommand.cs:95).
[Collection("Ported")]
public class SetCommandTests
{
    [Fact]
    public void Set_QuotedStringContainingKeyword_IsPreservedVerbatim()
    {
        // The inner keyword inside a quoted literal must survive untouched.
        ObjectRegistry.ClearAll();
        try
        {
            var builder = GameObject.Create("bob", privilege: Privilege.Builder);
            ObjectRegistry.AddObject(builder);
            builder.Desc = "before";
            builder.ClearMessages();
            var job = CommandDispatcher.DispatchLoggedIn(builder, "set me desc \"'Nonexistent'\"", immediate: true);
            Assert.NotNull(job);
            job!.Func(job.Caller, job.Args);
            Assert.DoesNotContain("nullxistent", builder.Desc);
            Assert.Contains("Nonexistent", builder.Desc);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void Set_InvalidTickSeconds_DoesNotReportReadOnly()
    {
        // A non-numeric value for a numeric property is a conversion problem, not a lock problem.
        ObjectRegistry.ClearAll();
        try
        {
            var builder = GameObject.Create("bob", privilege: Privilege.Builder);
            ObjectRegistry.AddObject(builder);
            builder.ClearMessages();
            var job = CommandDispatcher.DispatchLoggedIn(builder, "set me tick_seconds abc", immediate: true);
            Assert.NotNull(job);
            job!.Func(job.Caller, job.Args);
            var msgs = string.Join("\n", builder.PeekMessages());
            Assert.DoesNotContain("read-only", msgs, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(1.0, builder.TickSeconds);
        }
        finally { ObjectRegistry.ClearAll(); }
    }
}
